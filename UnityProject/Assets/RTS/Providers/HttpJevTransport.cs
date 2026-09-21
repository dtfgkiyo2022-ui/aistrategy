using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rts.Providers
{
    /// <summary>
    /// Talks to the AI gateway (POST /v1/systemone). The key comes from a supplied function (an environment variable in
    /// the game host) and is only ever placed in the Authorization header: never in a message, a log or the state.
    /// Any failure (no key, timeout, non-2xx, unreadable reply) is an exception, which the provider turns into "no order".
    /// </summary>
    public sealed class HttpJevTransport : IJevTransport
    {
        public const string DefaultUrl = "https://ai-gateway.lolipop.jp/v1/systemone";
        public const string DefaultModel = "typesafe/jev-latest";

        private readonly HttpClient client;
        private readonly Func<string> readKey;
        private readonly string url;
        private readonly string model;

        /// <summary>Tokens sent so far, for the running cost display. Read from any thread.</summary>
        public long InputTokens => Interlocked.Read(ref inputTokens);
        private long inputTokens;

        public HttpJevTransport(Func<string> readKey, HttpMessageHandler handler = null, string url = DefaultUrl,
            string model = DefaultModel, TimeSpan? timeout = null)
        {
            this.readKey = readKey ?? throw new ArgumentNullException(nameof(readKey));
            this.url = url;
            this.model = model;
            client = handler == null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = timeout ?? TimeSpan.FromSeconds(12);
        }

        public async Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel)
        {
            string key = readKey();
            if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("No API key is set.");
            string body = "{\"model\":\"" + model + "\",\"state\":" + stateJson + ",\"questions\":" + JevQuestions.Json + "}";
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request, cancel).ConfigureAwait(false))
                {
                    // The status is enough to explain a failure; the body is not echoed, in case it repeats the request.
                    // The status travels on the exception so the diagnostic log can say why a call failed; the body is
                    // never echoed, in case it repeats the request.
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("The AI gateway answered " + (int)response.StatusCode + ".", null, response.StatusCode);
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return Read(text);
                }
            }
        }

        internal JevAnswers Read(string text)
        {
            var root = MiniJson.Parse(text) as Dictionary<string, object> ?? throw new FormatException("The reply is not an object.");
            if (root.TryGetValue("usage", out var usage) && usage is Dictionary<string, object> u
                && u.TryGetValue("input_tokens", out var tokens) && tokens is double t)
                Interlocked.Add(ref inputTokens, (long)t);
            var answers = root.TryGetValue("answers", out var a) ? a as Dictionary<string, object> : null;
            if (answers == null) throw new FormatException("The reply has no answers.");
            var result = new JevAnswers();
            if (answers.TryGetValue("decisive_point", out var point) && point is Dictionary<string, object> p)
            {
                result.Choice = Game(p.TryGetValue("choice", out var choice) ? choice as string : null);
                if (p.TryGetValue("confidence", out var confidence) && confidence is double c) result.ChoiceConfidence = c;
                // A missing confidence stays 0, so an answer without one never clears the threshold.
            }
            if (answers.TryGetValue("commit_reserve", out var commit) && commit is Dictionary<string, object> r
                && r.TryGetValue("noul", out var noul) && noul is double n && n >= 0 && n <= 1)
                result.CommitReserve = n;
            return result;
        }

        private static string Game(string choice)
        {
            switch (choice)
            {
                case "north_outpost": return JevChoice.NorthOutpost;
                case "south_outpost": return JevChoice.SouthOutpost;
                case "my_core": return JevChoice.MyCore;
                case "enemy_core": return JevChoice.EnemyCore;
                default: return null; // unknown or missing: no choice
            }
        }
    }
}
