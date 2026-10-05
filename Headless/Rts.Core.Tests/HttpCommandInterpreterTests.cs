using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Tests.Headless
{
    public sealed class HttpCommandInterpreterTests
    {
        private const string Key = "api-secret-for-test-only";
        private sealed class Handler : HttpMessageHandler
        {
            internal HttpRequestMessage Request;
            internal string Body;
            internal HttpStatusCode Status = HttpStatusCode.OK;
            internal string Reply = "{}";
            internal Queue<HttpResponseMessage> Responses = new Queue<HttpResponseMessage>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Request = request; Body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (Responses.Count != 0) return Responses.Dequeue();
                return new HttpResponseMessage(Status) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
            }
        }

        private static InterpreterRequest Request(string model, bool selected = false)
        {
            var point = new SimPoint(Fix64.FromInt(0), Fix64.FromInt(10));
            var observation = new FactionObservation(1, 20, new[] { new OwnArmyView(1, 1, UnitKind.Infantry, point, 5, default(PolicyGoal)) },
                Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), new[] { new KnownObjective(GoalKind.Outpost, 1, point, true, 1, false, 0, 20) });
            var frame = new FactionFrame(20, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(), Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()), new MatchResult(false, 0, false, false, true));
            return new InterpreterRequest { RequestId = 7, Model = model, Instruction = "ここを守れ", FixedTarget = new ScopeKey(1, ScopeKind.Outpost, 1), HasFixedTarget = selected,
                Summary = AiSituationSummary.From(frame), StartedTick = 20, DeadlineTick = 260 };
        }

        private static InterpreterReply Poll(HttpCommandInterpreter interpreter)
        {
            for (int i = 0; i < 100; i++)
            {
                var replies = interpreter.Poll(21); if (replies.Count != 0) return replies[0];
                Thread.Sleep(1);
            }
            Assert.Fail("HTTP reply did not arrive"); return null;
        }

        [Test]
        public void ClaudeUsesCachedSystemPromptAndHeaderOnlyKey()
        {
            var handler = new Handler { Reply = "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"{\\\"commands\\\":[],\\\"say\\\":\\\"ok\\\",\\\"reason\\\":null,\\\"unknown\\\":false}\"}],\"usage\":{\"input_tokens\":100,\"output_tokens\":20,\"cache_read_input_tokens\":80}}" };
            using (var interpreter = new ClaudeCommandInterpreter(() => Key, handler))
            {
                interpreter.Request(Request("claude-haiku-4-5")); var reply = Poll(interpreter);
                Assert.That(handler.Request.Headers.GetValues("x-api-key"), Does.Contain(Key));
                Assert.That(handler.Body, Does.Not.Contain(Key)); Assert.That(handler.Body, Does.Contain("cache_control"));
                Assert.That(handler.Body, Does.Contain("output_config")); Assert.That(handler.Body, Does.Contain("\"type\":\"json_schema\""));
                Assert.That(handler.Body, Does.Not.Contain("tools")); Assert.That(handler.Body, Does.Not.Contain("tool_choice"));
                Assert.That(handler.Body, Does.Contain("選択中の対象：なし"));
                Assert.That(reply.Json, Does.Contain("\"commands\"")); Assert.That(reply.Usage.CacheReadInputTokens, Is.EqualTo(80));
            }
        }

        [Test]
        public void FixedTargetIsIncludedByItsDisplayName()
        {
            var handler = new Handler { Reply = "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"{\\\"commands\\\":[],\\\"say\\\":\\\"ok\\\",\\\"reason\\\":null,\\\"unknown\\\":false}\"}]}" };
            using (var interpreter = new ClaudeCommandInterpreter(() => Key, handler))
            {
                interpreter.Request(Request("claude-haiku-4-5", true));
                Poll(interpreter);
                Assert.That(handler.Body, Does.Contain("選択中の対象：北の拠点"));
            }
        }

        [Test]
        public void OpenAiAndLocalUseStrictJsonSchemaAndDoNotLeakLocalKey()
        {
            var openHandler = new Handler { Reply = "{\"choices\":[{\"message\":{\"content\":\"{\\\"commands\\\":[],\\\"say\\\":\\\"ok\\\"}\"}}],\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":2}}" };
            using (var open = new OpenAiCommandInterpreter(() => Key, openHandler))
            { open.Request(Request("gpt-6-luna")); Assert.That(Poll(open).Json, Does.Contain("commands")); Assert.That(openHandler.Body, Does.Contain("response_format")); Assert.That(openHandler.Body, Does.Contain("\"strict\":true")); Assert.That(openHandler.Request.Headers.Authorization.Parameter, Is.EqualTo(Key)); Assert.That(openHandler.Body, Does.Not.Contain(Key)); }
            var localHandler = new Handler { Reply = "{\"choices\":[{\"message\":{\"content\":\"{\\\"commands\\\":[],\\\"say\\\":\\\"ok\\\"}\"}}]}" };
            using (var local = new LocalLlmCommandInterpreter("qwen-test", localHandler))
            { local.Request(Request("local-llm")); Poll(local); Assert.That(localHandler.Request.Headers.Authorization, Is.Null); Assert.That(localHandler.Body, Does.Contain("qwen-test")); Assert.That(localHandler.Body, Does.Contain("\"max_tokens\":2048")); Assert.That(localHandler.Body, Does.Contain("/no_think")); }
        }

        [Test]
        public void OllamaUsesNativeChatShapeAndReadsMessageAndNativeUsage()
        {
            var handler = new Handler { Reply = "{\"message\":{\"content\":\"{\\\"kind\\\":\\\"unknown\\\",\\\"scope\\\":\\\"\\\",\\\"goal\\\":\\\"\\\",\\\"region\\\":\\\"\\\",\\\"control\\\":\\\"\\\",\\\"reason\\\":\\\"質問\\\"}\"},\"prompt_eval_count\":12,\"eval_count\":7,\"done\":true}" };
            using (var local = new LocalLlmCommandInterpreter("qwen3:8b", handler, endpoint: "ollama"))
            {
                local.Request(Request("local-llm"));
                var reply = Poll(local);
                Assert.That(handler.Request.RequestUri.AbsolutePath, Is.EqualTo("/api/chat"));
                Assert.That(handler.Body, Does.Contain("\"stream\":false"));
                Assert.That(handler.Body, Does.Contain("\"think\":false"));
                Assert.That(handler.Body, Does.Contain("\"format\":{\"type\":\"object\""));
                Assert.That(handler.Body, Does.Contain("\"temperature\":0"));
                Assert.That(handler.Body, Does.Contain("\"num_predict\":256"));
                Assert.That(handler.Body, Does.Not.Contain("response_format"));
                Assert.That(reply.Json, Does.Contain("\"kind\":\"unknown\""));
                Assert.That(reply.Usage.InputTokens, Is.EqualTo(12));
                Assert.That(reply.Usage.OutputTokens, Is.EqualTo(7));
            }
        }

        [Test]
        public void RefusalAndHttpFailureBecomeNoAnswerWithoutBodyOrKey()
        {
            var refusal = new Handler { Reply = "{\"stop_reason\":\"refusal\",\"content\":[],\"usage\":{}}" };
            using (var interpreter = new ClaudeCommandInterpreter(() => Key, refusal))
            { interpreter.Request(Request("claude-haiku-4-5")); Assert.That(Poll(interpreter).FailureReason, Does.Contain("答えなし")); }
            var failed = new Handler { Status = HttpStatusCode.InternalServerError, Reply = "{\"error\":\"" + Key + "\"}" };
            using (var interpreter = new OpenAiCommandInterpreter(() => Key, failed))
            { interpreter.Request(Request("gpt-6-luna")); var reply = Poll(interpreter); Assert.That(reply.FailureReason, Does.Contain("HTTP 500")); Assert.That(reply.FailureReason, Does.Not.Contain(Key)); }
        }

        [Test]
        public void ClaudeTextAndMaxTokensAreHandledAnd429IsRetried()
        {
            var handler = new Handler { Reply = "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"{\\\"commands\\\":[],\\\"say\\\":\\\"ok\\\",\\\"reason\\\":null,\\\"unknown\\\":false}\"}]}" };
            using (var interpreter = new ClaudeCommandInterpreter(() => Key, handler))
            {
                interpreter.Request(Request("claude-haiku-4-5"));
                Assert.That(Poll(interpreter).Json, Does.Contain("commands"));
            }

            var maxed = new Handler { Reply = "{\"stop_reason\":\"max_tokens\",\"content\":[{\"type\":\"text\",\"text\":\"partial\"}]}" };
            using (var interpreter = new ClaudeCommandInterpreter(() => Key, maxed))
            {
                interpreter.Request(Request("claude-haiku-4-5"));
                Assert.That(Poll(interpreter).FailureReason, Does.Contain("答えなし"));
            }

            var retry = new Handler();
            retry.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero) }
            });
            retry.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"commands\\\":[],\\\"say\\\":\\\"ok\\\",\\\"reason\\\":null,\\\"unknown\\\":false}\"}}]}", Encoding.UTF8, "application/json")
            });
            using (var interpreter = new OpenAiCommandInterpreter(() => Key, retry))
            {
                interpreter.Request(Request("gpt-6-luna"));
                Assert.That(Poll(interpreter).FailureReason, Is.Null);
            }
        }

        [Test]
        public void UnknownModelsAreRejectedAndCacheTokensArePricedFromSettings()
        {
            Assert.Throws<ArgumentException>(() => AiModelCatalog.Get("not-a-model"));
            Assert.That(AiCostCalculator.Calculate("claude-haiku-4-5", new AiTokenUsage(100, 20, 80, 0)), Is.EqualTo(0.0312m));
        }
    }
}
