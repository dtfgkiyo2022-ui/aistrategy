using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public sealed class HttpJevTransport : IJudgement, IQuestionAwareJevTransport
    {
        public const string DefaultUrl = "https://api.typesafe.ai/v1/systemone";
        public const string DefaultModel = "jev-latest";
        public const string DefaultKeyEnvironment = "TYPESAFE_API_KEY";

        private readonly HttpClient client;
        private readonly Func<string> readKey;
        private readonly string url;
        private readonly string model;

        /// <summary>Tokens sent so far, for the running cost display. Read from any thread.</summary>
        public long InputTokens => Interlocked.Read(ref inputTokens);
        private long inputTokens;

        /// <summary>Output tokens reported by the provider, for the common cost meter.</summary>
        public long OutputTokens => Interlocked.Read(ref outputTokens);
        private long outputTokens;

        public HttpJevTransport(Func<string> readKey, HttpMessageHandler handler = null, string url = DefaultUrl,
            string model = DefaultModel, TimeSpan? timeout = null)
        {
            this.readKey = readKey ?? throw new ArgumentNullException(nameof(readKey));
            this.url = url;
            this.model = model;
            client = handler == null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = timeout ?? TimeSpan.FromSeconds(12);
        }

        public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel) =>
            AskAsync(stateJson, JevQuestions.Json, cancel);

        public async Task<JevAnswers> AskAsync(string stateJson, string questionsJson, CancellationToken cancel)
        {
            string key = readKey();
            if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("No API key is set.");
            if (stateJson == null) throw new ArgumentNullException(nameof(stateJson));
            if (questionsJson == null) throw new ArgumentNullException(nameof(questionsJson));
            string body = "{\"model\":\"" + model + "\",\"state\":" + stateJson + ",\"questions\":" + questionsJson + "}";
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
                        throw new JevHttpException((int)response.StatusCode, "The AI gateway answered " + (int)response.StatusCode + ".");
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return Read(text);
                }
            }
        }

        internal JevAnswers Read(string text)
        {
            var root = MiniJson.Parse(text) as Dictionary<string, object> ?? throw new FormatException("The reply is not an object.");
            if (root.TryGetValue("usage", out var usage) && usage is Dictionary<string, object> u)
            {
                if (u.TryGetValue("input_tokens", out var tokens) && tokens is double t)
                    Interlocked.Add(ref inputTokens, (long)t);
                if (u.TryGetValue("output_tokens", out var output) && output is double ot)
                    Interlocked.Add(ref outputTokens, (long)ot);
            }
            var answers = root.TryGetValue("answers", out var a) ? a as Dictionary<string, object> : null;
            if (answers == null) throw new FormatException("The reply has no answers.");
            var result = new JevAnswers();
            if (root.TryGetValue("usage", out var usageRoot) && usageRoot is Dictionary<string, object> usageData)
            {
                result.Usage.InputTokens = Number(usageData, "input_tokens");
                result.Usage.OutputTokens = Number(usageData, "output_tokens");
            }
            foreach (var pair in answers)
            {
                if (!(pair.Value is Dictionary<string, object> answer)) continue;
                if (answer.TryGetValue("choice", out var choiceValue) && choiceValue is string rawChoice)
                {
                    string choice = Game(rawChoice);
                    if (choice != null) result.Choices[pair.Key] = choice;
                    // A missing confidence stays 0, so an answer without one never clears the threshold.
                    if (answer.TryGetValue("confidence", out var confidence) && confidence is double c
                        && c >= 0 && c <= 1) result.ChoiceConfidences[pair.Key] = c;
                    if (pair.Key == "decisive_point")
                    {
                        result.Choice = choice;
                        result.ChoiceConfidence = result.ChoiceConfidences.TryGetValue(pair.Key, out var decisiveConfidence) ? decisiveConfidence : 0;
                    }
                }
                if (answer.TryGetValue("noul", out var noul) && noul is double n && n >= 0 && n <= 1)
                {
                    result.Noul[pair.Key] = n;
                    if (JevFacts.All.Contains(pair.Key)) result.Facts[pair.Key] = n;
                }
                if (answer.TryGetValue("score", out var score) && score is double s && s >= 0 && s <= 1)
                    result.Scores[pair.Key] = s;
            }
            return result;
        }

        private static long Number(Dictionary<string, object> values, string name) =>
            values.TryGetValue(name, out var value) && value is double number ? (long)number : 0;

        private static string Game(string choice)
        {
            switch (choice)
            {
                case "north_outpost": return JevChoice.NorthOutpost;
                case "south_outpost": return JevChoice.SouthOutpost;
                case "my_core": return JevChoice.MyCore;
                case "enemy_core": return JevChoice.EnemyCore;
                case "focus": return "focus";
                case "defend": return JevChoice.Defend;
                case "retreat": return JevChoice.Retreat;
                case "economy": return JevChoice.Economy;
                case "unknown": return JevChoice.Unknown;
                case "hold": return "hold";
                case "capture": return "capture";
                default: return null; // unknown or missing: no choice
            }
        }
    }
}
