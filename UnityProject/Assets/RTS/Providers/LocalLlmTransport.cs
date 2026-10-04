using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rts.Providers
{
    /// <summary>
    /// OpenAI-compatible local chat-completions transport. It has no API key and reports zero currency cost; the
    /// returned usage is still kept for diagnostics. The request includes a strict JSON Schema so a small local model
    /// cannot answer with prose.
    /// </summary>
    public class LocalLlmTransport : IJudgement, IQuestionAwareJevTransport, ISingleFlightJudgementTransport, IDisposable
    {
        public const string DefaultUrl = "http://127.0.0.1:11434/v1/chat/completions";
        public const string DefaultModel = "qwen3.5:4b";

        private readonly HttpClient client;
        private readonly string url;
        private readonly string model;

        public LocalLlmTransport(string url = DefaultUrl, string model = DefaultModel,
            HttpMessageHandler handler = null, TimeSpan? timeout = null)
        {
            this.url = url ?? throw new ArgumentNullException(nameof(url));
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            client = handler == null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = timeout ?? TimeSpan.FromSeconds(12);
        }

        public bool SingleFlight => true;

        public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel) =>
            AskAsync(stateJson, JevQuestions.Json, cancel);

        public async Task<JevAnswers> AskAsync(string stateJson, string questionsJson, CancellationToken cancel)
        {
            if (stateJson == null) throw new ArgumentNullException(nameof(stateJson));
            if (questionsJson == null) throw new ArgumentNullException(nameof(questionsJson));
            string body = "{\"model\":\"" + Escape(model) + "\",\"temperature\":0,\"messages\":[" +
                "{\"role\":\"system\",\"content\":\"選択肢から1つ選び、確信度を0から1で付け、指定されたJSONで答える。説明文は返さない。\"}," +
                "{\"role\":\"user\",\"content\":\"state=" + Escape(stateJson) + " questions=" + Escape(questionsJson) + "\"}" +
                "],\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{" +
                "\"name\":\"jev_judgement\",\"strict\":true,\"schema\":" + JevQuestions.AnswerSchema(questionsJson) + "}}}";
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request, cancel).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new JevHttpException((int)response.StatusCode, "The local AI answered " + (int)response.StatusCode + ".");
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ReadOpenAi(text);
                }
            }
        }

        private static JevAnswers ReadOpenAi(string text)
        {
            var root = MiniJson.Parse(text) as Dictionary<string, object>
                ?? throw new FormatException("The local reply is not an object.");
            var usage = root.TryGetValue("usage", out var usageValue) ? usageValue as Dictionary<string, object> : null;
            string content = null;
            if (root.TryGetValue("answers", out _)) return ReadNormalized(root);
            if (root.TryGetValue("choices", out var choicesValue) && choicesValue is List<object> choices && choices.Count > 0
                && choices[0] is Dictionary<string, object> first
                && first.TryGetValue("message", out var messageValue) && messageValue is Dictionary<string, object> message
                && message.TryGetValue("content", out var contentValue))
            {
                content = contentValue as string;
                if (content == null && contentValue is Dictionary<string, object> contentObject)
                    content = MiniJsonText(contentObject);
            }
            if (content == null) throw new FormatException("The local reply has no message content.");
            var normalized = MiniJson.Parse(content) as Dictionary<string, object>
                ?? throw new FormatException("The local message is not JSON.");
            var result = ReadNormalized(normalized);
            result.Usage.InputTokens = Number(usage, "prompt_tokens", "input_tokens");
            result.Usage.OutputTokens = Number(usage, "completion_tokens", "output_tokens");
            return result;
        }

        private static JevAnswers ReadNormalized(Dictionary<string, object> root)
        {
            var answers = root.TryGetValue("answers", out var value) ? value as Dictionary<string, object> : null;
            if (answers == null) throw new FormatException("The local JSON has no answers.");
            var result = new JevAnswers();
            foreach (var pair in answers)
            {
                if (!(pair.Value is Dictionary<string, object> answer)) continue;
                if (answer.TryGetValue("choice", out var choiceValue) && choiceValue is string raw)
                {
                    string choice = MapChoice(raw);
                    if (choice == null) continue;
                    result.Choices[pair.Key] = choice;
                    if (answer.TryGetValue("confidence", out var confidence) && confidence is double c && c >= 0 && c <= 1)
                        result.ChoiceConfidences[pair.Key] = c;
                    if (pair.Key == "decisive_point")
                    {
                        result.Choice = choice;
                        result.ChoiceConfidence = result.ChoiceConfidences.TryGetValue(pair.Key, out var decisive) ? decisive : 0;
                    }
                }
                if (answer.TryGetValue("noul", out var noul) && noul is double n && n >= 0 && n <= 1)
                {
                    result.Noul[pair.Key] = n;
                    if (Array.IndexOf(JevFacts.All, pair.Key) >= 0) result.Facts[pair.Key] = n;
                }
                if (answer.TryGetValue("score", out var score) && score is double s && s >= 0 && s <= 1)
                    result.Scores[pair.Key] = s;
            }
            return result;
        }

        private static string MapChoice(string value)
        {
            switch (value)
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
                default: return null;
            }
        }

        private static long Number(Dictionary<string, object> values, string first, string second)
        {
            if (values == null) return 0;
            if (values.TryGetValue(first, out var a) && a is double ad) return (long)ad;
            return values.TryGetValue(second, out var b) && b is double bd ? (long)bd : 0;
        }

        private static string MiniJsonText(Dictionary<string, object> value)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var pair in value)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(Escape(pair.Key)).Append("\":");
                if (pair.Value is string s) sb.Append('"').Append(Escape(s)).Append('"');
                else if (pair.Value is bool b) sb.Append(b ? "true" : "false");
                else sb.Append(pair.Value);
            }
            return sb.Append('}').ToString();
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n");

        public void Dispose() { client.Dispose(); }
    }

    /// <summary>Descriptive alias used by hosts that call the backend an HTTP local-LLM transport.</summary>
    public sealed class HttpLocalLlmTransport : LocalLlmTransport
    {
        public HttpLocalLlmTransport(string url = DefaultUrl, string model = DefaultModel,
            HttpMessageHandler handler = null, TimeSpan? timeout = null) : base(url, model, handler, timeout) { }
    }
}
