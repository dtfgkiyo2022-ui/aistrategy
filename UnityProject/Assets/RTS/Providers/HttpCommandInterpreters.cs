using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rts.Contracts;

namespace Rts.Providers
{
    public static class AiModelAvailability
    {
        public static bool IsConfigured(string model)
        {
            if (string.IsNullOrEmpty(model)) return false;
            if (model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
            if (model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)) return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
            if (model.Equals("jev", StringComparison.OrdinalIgnoreCase)) return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));
            // A built-in default URL is not proof that a local server is running. The host uses this same explicit
            // setting when constructing the transport, so the model is shown as unavailable until the user opts in.
            if (model.Equals("local-llm", StringComparison.OrdinalIgnoreCase)) return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOCAL_LLM_URL"));
            return false;
        }

        public static IReadOnlyList<AiModelPrice> ConfiguredModels()
            => AiModelCatalog.Available(IsConfigured);
    }

    /// <summary>HTTP-backed command interpreters. The transport stays outside Application so Unity's deterministic code never sees HTTP, Task or decimal.</summary>
    public abstract class HttpCommandInterpreter : ICommandInterpreter, IDisposable
    {
        private sealed class Pending
        {
            internal ulong RequestId;
            internal Task<TransportReply> Task;
        }
        private sealed class TransportReply
        {
            internal string Json;
            internal AiTokenUsage Usage;
            internal string FailureReason;
        }

        private readonly HttpClient client;
        private readonly Func<string> readKey;
        private readonly string url;
        private readonly bool requiresKey;
        private readonly List<Pending> pending = new List<Pending>();
        private readonly object gate = new object();
        private bool disposed;

        protected HttpCommandInterpreter(Func<string> readKey, HttpMessageHandler handler, string url, bool requiresKey, TimeSpan? timeout)
        {
            this.readKey = readKey ?? (() => null);
            this.url = string.IsNullOrEmpty(url) ? throw new ArgumentException("URL is required.", nameof(url)) : url;
            this.requiresKey = requiresKey;
            client = handler == null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = timeout ?? TimeSpan.FromSeconds(12);
        }

        protected abstract string BuildBody(InterpreterRequest request);
        protected abstract void AddHeaders(HttpRequestMessage request, string key);
        protected abstract string ProviderName { get; }

        public void Request(InterpreterRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (disposed) throw new ObjectDisposedException(GetType().Name);
            AiModelCatalog.Get(request.Model);
            string key = readKey();
            if (requiresKey && string.IsNullOrEmpty(key)) throw new InvalidOperationException(ProviderName + " の API キーが設定されていません。");
            string body = BuildBody(request);
            var item = new Pending { RequestId = request.RequestId };
            item.Task = SendAsync(request, body, key);
            lock (gate) pending.Add(item);
        }

        // Kept as a separate method because HttpClient must not be awaited from the simulation tick.
        private async Task<TransportReply> SendAsync(InterpreterRequest requestInfo, string body, string key)
        {
            try
            {
                DateTime retryDeadline = DateTime.UtcNow.AddSeconds(Math.Max(1, Math.Min(60, requestInfo.DeadlineTick > requestInfo.StartedTick ? requestInfo.DeadlineTick - requestInfo.StartedTick : 1)));
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                    {
                        AddHeaders(request, key);
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        using (var response = await client.SendAsync(request).ConfigureAwait(false))
                        {
                            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                            {
                                if (attempt == 3) return HttpFailure(response.StatusCode);
                                TimeSpan delay = RetryDelay(response, attempt);
                                TimeSpan remaining = retryDeadline - DateTime.UtcNow;
                                if (remaining <= TimeSpan.Zero || delay > remaining) return HttpFailure(response.StatusCode);
                                await Task.Delay(delay).ConfigureAwait(false);
                                continue;
                            }
                            if (!response.IsSuccessStatusCode) return HttpFailure(response.StatusCode);
                            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return ReadResponse(text);
                        }
                    }
                }
                return HttpFailure(HttpStatusCode.TooManyRequests);
            }
            catch (OperationCanceledException)
            {
                return new TransportReply { FailureReason = ProviderName + " の応答が時間切れになりました。" };
            }
            catch (Exception)
            {
                // Do not echo exception text: some handlers/proxies include request headers or response bodies.
                return new TransportReply { FailureReason = ProviderName + " の応答を読めませんでした。" };
            }
        }

        private TransportReply HttpFailure(HttpStatusCode status)
            => new TransportReply { FailureReason = ProviderName + " の通信に失敗しました（HTTP " + (int)status + "）。" };

        private static TimeSpan RetryDelay(HttpResponseMessage response, int retry)
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue) return response.Headers.RetryAfter.Delta.Value < TimeSpan.Zero ? TimeSpan.Zero : response.Headers.RetryAfter.Delta.Value;
                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    TimeSpan until = response.Headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow;
                    return until < TimeSpan.Zero ? TimeSpan.Zero : until;
                }
            }
            return TimeSpan.FromMilliseconds(10 * (1 << retry));
        }

        public IReadOnlyList<InterpreterReply> Poll(long tick)
        {
            var ready = new List<InterpreterReply>();
            lock (gate)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var item = pending[i];
                    if (!item.Task.IsCompleted) continue;
                    pending.RemoveAt(i);
                    TransportReply reply;
                    try { reply = item.Task.GetAwaiter().GetResult(); }
                    catch (Exception) { reply = new TransportReply { FailureReason = ProviderName + " の応答を読めませんでした。" }; }
                    ready.Add(new InterpreterReply { RequestId = item.RequestId, ReturnedTick = tick, Json = reply.Json,
                        Usage = reply.Usage, FailureReason = reply.FailureReason });
                }
            }
            ready.Sort((a, b) => a.RequestId.CompareTo(b.RequestId));
            return ready;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client.Dispose();
        }

        private static TransportReply ReadResponse(string text)
        {
            try
            {
                var root = MiniJson.Parse(text) as Dictionary<string, object>;
                if (root == null) return Bad("答えが JSON オブジェクトではありません。");
                var usage = Usage(root);
                string stop = String(root, "stop_reason");
                if (string.Equals(stop, "refusal", StringComparison.OrdinalIgnoreCase) || string.Equals(stop, "max_tokens", StringComparison.OrdinalIgnoreCase))
                    return Bad(string.Equals(stop, "refusal", StringComparison.OrdinalIgnoreCase) ? "モデルが拒否したため、答えなしです。" : "出力上限に達したため、答えなしです。");
                if (root.TryGetValue("choices", out var choicesValue) && choicesValue is List<object> choices && choices.Count != 0 &&
                    choices[0] is Dictionary<string, object> choice && string.Equals(String(choice, "finish_reason"), "length", StringComparison.OrdinalIgnoreCase))
                    return Bad("出力上限に達したため、答えなしです。");
                string json = ExtractClaude(root) ?? ExtractOpenAi(root);
                if (string.IsNullOrEmpty(json)) return Bad("モデルから答えがありませんでした。");
                return new TransportReply { Json = json, Usage = usage };
            }
            catch (Exception) { return Bad("応答の形が違うため、答えを読めませんでした。"); }
        }

        private static TransportReply Bad(string reason) => new TransportReply { FailureReason = reason };

        private static string ExtractClaude(Dictionary<string, object> root)
        {
            if (!(root.TryGetValue("content", out var raw) && raw is List<object> content)) return null;
            foreach (var item in content.OfType<Dictionary<string, object>>())
            {
                string type = String(item, "type");
                if (type == "text" && item.TryGetValue("text", out var text) && text is string s) return s;
            }
            return null;
        }

        private static string ExtractOpenAi(Dictionary<string, object> root)
        {
            if (root.TryGetValue("output_text", out var outputText) && outputText is string direct) return direct;
            if (!(root.TryGetValue("choices", out var raw) && raw is List<object> choices) || choices.Count == 0) return null;
            var choice = choices[0] as Dictionary<string, object>;
            var message = choice != null && choice.TryGetValue("message", out var m) ? m as Dictionary<string, object> : null;
            if (message == null) return null;
            if (message.TryGetValue("refusal", out var refusal) && refusal is string) return null;
            if (!message.TryGetValue("content", out var content)) return null;
            if (content is string s) return s;
            if (content is List<object> blocks)
                foreach (var block in blocks.OfType<Dictionary<string, object>>())
                    if (block.TryGetValue("text", out var text) && text is string t) return t;
            return null;
        }

        private static AiTokenUsage Usage(Dictionary<string, object> root)
        {
            var u = root.TryGetValue("usage", out var raw) ? raw as Dictionary<string, object> : null;
            if (u == null) return new AiTokenUsage(0, 0);
            int input = Int(u, "input_tokens", Int(u, "prompt_tokens", 0));
            int output = Int(u, "output_tokens", Int(u, "completion_tokens", 0));
            int cacheRead = Int(u, "cache_read_input_tokens", Int(u, "prompt_tokens_details", 0));
            if (u.TryGetValue("input_tokens_details", out var details) && details is Dictionary<string, object> d)
                cacheRead = Int(d, "cached_tokens", cacheRead);
            return new AiTokenUsage(input, output, cacheRead, Int(u, "cache_creation_input_tokens", 0));
        }

        private static int Int(Dictionary<string, object> map, string key, int fallback)
        {
            if (!map.TryGetValue(key, out var value)) return fallback;
            if (value is double d && d >= 0 && d <= int.MaxValue) return (int)d;
            return fallback;
        }
        private static string String(Dictionary<string, object> map, string key) => map.TryGetValue(key, out var value) ? value as string : null;
    }

    public sealed class ClaudeCommandInterpreter : HttpCommandInterpreter
    {
        public const string DefaultUrl = "https://api.anthropic.com/v1/messages";
        public ClaudeCommandInterpreter(Func<string> readKey, HttpMessageHandler handler = null, string url = DefaultUrl, TimeSpan? timeout = null)
            : base(readKey, handler, url, true, timeout ?? TimeSpan.FromSeconds(60)) { }
        protected override string ProviderName => "Claude";
        protected override void AddHeaders(HttpRequestMessage request, string key)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", key);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
        protected override string BuildBody(InterpreterRequest request)
        {
            var limits = AiModelCatalog.Get(request.Model);
            string schema = AiCommandSchema.Build(request.Summary, limits);
            return JsonValueWriter.Write(new Dictionary<string, object>
            {
                ["model"] = request.Model, ["max_tokens"] = (long)limits.MaxOutputTokens,
                ["system"] = new List<object>
                {
                    new Dictionary<string, object> { ["type"] = "text", ["text"] = AiCommandSchema.ForModel(request.Model), ["cache_control"] = new Dictionary<string, object> { ["type"] = "ephemeral" } },
                    new Dictionary<string, object> { ["type"] = "text", ["text"] = "JSON Schema:\n" + schema }
                },
                ["messages"] = new List<object> { new Dictionary<string, object> { ["role"] = "user", ["content"] = request.Summary.DynamicPrompt(request.Instruction,
                    request.HasFixedTarget && string.IsNullOrEmpty(request.FixedTargetName) ? (ScopeKey?)request.FixedTarget : null, request.FixedTargetName) } },
                ["output_config"] = new Dictionary<string, object> { ["format"] = new Dictionary<string, object> { ["type"] = "json_schema", ["schema"] = MiniJson.Parse(schema) } }
            });
        }
    }

    public sealed class OpenAiCommandInterpreter : HttpCommandInterpreter
    {
        public const string DefaultUrl = "https://api.openai.com/v1/chat/completions";
        public OpenAiCommandInterpreter(Func<string> readKey, HttpMessageHandler handler = null, string url = DefaultUrl, TimeSpan? timeout = null)
            : base(readKey, handler, url, true, timeout ?? TimeSpan.FromSeconds(60)) { }
        protected override string ProviderName => "OpenAI";
        protected override void AddHeaders(HttpRequestMessage request, string key) => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        protected override string BuildBody(InterpreterRequest request) => OpenAiBody(request, request.Model);
        internal static string OpenAiBody(InterpreterRequest request, string model, bool completionTokens = true)
        {
            var limits = AiModelCatalog.Get(request.Model);
            string schema = AiCommandSchema.Build(request.Summary, limits);
            string userPrompt = request.Summary.DynamicPrompt(request.Instruction,
                request.HasFixedTarget && string.IsNullOrEmpty(request.FixedTargetName) ? (ScopeKey?)request.FixedTarget : null, request.FixedTargetName);
            if (limits.DisableThinking && request.Model.Equals("local-llm", StringComparison.OrdinalIgnoreCase))
                userPrompt += "\n/no_think";
            var messages = new List<object>
            {
                new Dictionary<string, object> { ["role"] = "system", ["content"] = AiCommandSchema.ForModel(request.Model) + "\nJSON Schema:\n" + schema },
                new Dictionary<string, object> { ["role"] = "user", ["content"] = userPrompt }
            };
            var body = new Dictionary<string, object>
            {
                ["model"] = model, [completionTokens ? "max_completion_tokens" : "max_tokens"] = (long)limits.MaxOutputTokens, ["messages"] = messages,
                ["response_format"] = new Dictionary<string, object> { ["type"] = "json_schema", ["json_schema"] = new Dictionary<string, object> { ["name"] = "rts_commands", ["strict"] = true, ["schema"] = MiniJson.Parse(schema) } }
            };
            if (limits.DisableThinking && request.Model.Equals("local-llm", StringComparison.OrdinalIgnoreCase)) body["think"] = false;
            return JsonValueWriter.Write(body);
        }
    }

    public sealed class LocalLlmCommandInterpreter : HttpCommandInterpreter
    {
        public const string DefaultUrl = "http://127.0.0.1:11434/v1/chat/completions";
        private readonly string localModel;
        public LocalLlmCommandInterpreter(string model, HttpMessageHandler handler = null, string url = DefaultUrl, TimeSpan? timeout = null)
            : base(() => null, handler, url, false, timeout ?? TimeSpan.FromSeconds(60)) { localModel = string.IsNullOrEmpty(model) ? throw new ArgumentException("ローカルモデル名が必要です。", nameof(model)) : model; }
        protected override string ProviderName => "ローカル LLM";
        protected override void AddHeaders(HttpRequestMessage request, string key) { }
        protected override string BuildBody(InterpreterRequest request)
        {
            var copy = new InterpreterRequest { RequestId = request.RequestId, FactionId = request.FactionId, Instruction = request.Instruction, Summary = request.Summary,
                Model = request.Model, HasFixedTarget = request.HasFixedTarget, FixedTarget = request.FixedTarget, FixedTargetName = request.FixedTargetName };
            // OpenAI's newer models only take max_completion_tokens; OpenAI-compatible local servers (Ollama) take max_tokens.
            return OpenAiCommandInterpreter.OpenAiBody(copy, localModel, false);
        }
    }

    internal static class JsonValueWriter
    {
        internal static string Write(object value)
        {
            var b = new StringBuilder(); WriteValue(b, value); return b.ToString();
        }
        private static void WriteValue(StringBuilder b, object value)
        {
            if (value == null) { b.Append("null"); return; }
            if (value is string s) { WriteString(b, s); return; }
            if (value is bool flag) { b.Append(flag ? "true" : "false"); return; }
            if (value is IDictionary<string, object> map)
            {
                b.Append('{'); bool first = true;
                foreach (var pair in map) { if (!first) b.Append(','); first = false; WriteString(b, pair.Key); b.Append(':'); WriteValue(b, pair.Value); }
                b.Append('}'); return;
            }
            if (value is IEnumerable<object> list)
            {
                b.Append('['); bool first = true; foreach (var item in list) { if (!first) b.Append(','); first = false; WriteValue(b, item); } b.Append(']'); return;
            }
            if (value is double d) { b.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (value is float f) { b.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
            b.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        private static void WriteString(StringBuilder b, string value)
        {
            b.Append('"'); foreach (char c in value ?? "")
            {
                switch (c) { case '"': b.Append("\\\""); break; case '\\': b.Append("\\\\"); break; case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break; default: if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else b.Append(c); break; }
            } b.Append('"');
        }
    }
}
