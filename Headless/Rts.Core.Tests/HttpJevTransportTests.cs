using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Tests.Headless
{
    /// <summary>The real transport against a fake HTTP handler: what is sent, what is read back, and how it fails.</summary>
    public sealed class HttpJevTransportTests
    {
        private const string Key = "sk-test-secret-value-1234567890";
        private const string State = "{\"tick\":20,\"faction\":1}";

        private sealed class FakeHandler : HttpMessageHandler
        {
            internal HttpRequestMessage Sent;
            internal string SentBody;
            internal int Calls;
            internal HttpStatusCode Status = HttpStatusCode.OK;
            internal string Reply = "{}";
            internal Func<CancellationToken, Task> Before;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            {
                Calls++;
                Sent = request;
                SentBody = request.Content == null ? null : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (Before != null) await Before(cancel).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                return new HttpResponseMessage(Status) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
            }
        }

        private const string GoodReply =
            "{\"model\":\"typesafe/jev-latest\",\"answers\":{" +
            "\"decisive_point\":{\"choice\":\"enemy_core\",\"probabilities\":{\"north_outpost\":0.1,\"south_outpost\":0.1,\"my_core\":0.0,\"enemy_core\":0.8},\"confidence\":0.8}," +
            "\"outnumbering\":{\"noul\":0.42},\"enemy_near_my_core\":{\"noul\":0.03},\"outpost_held_by_enemy\":{\"noul\":0.9}},\"usage\":{\"input_tokens\":1234,\"output_tokens\":12}}";

        private static HttpJevTransport Make(FakeHandler handler, Func<string> key = null, TimeSpan? timeout = null) =>
            new HttpJevTransport(key ?? (() => Key), handler, timeout: timeout);

        [Test]
        public async Task TheRequestCarriesTheKeyOnlyInTheAuthorizationHeader()
        {
            var handler = new FakeHandler { Reply = GoodReply };
            await Make(handler).AskAsync(State, CancellationToken.None);
            Assert.That(handler.Sent.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(handler.Sent.RequestUri.ToString(), Is.EqualTo(HttpJevTransport.DefaultUrl));
            Assert.That(handler.Sent.Headers.Authorization.Scheme, Is.EqualTo("Bearer"));
            Assert.That(handler.Sent.Headers.Authorization.Parameter, Is.EqualTo(Key));
            Assert.That(handler.SentBody, Does.Not.Contain(Key));
        }

        [Test]
        public async Task TheBodyHoldsTheModelTheStateAndTheVersionedQuestions()
        {
            var handler = new FakeHandler { Reply = GoodReply };
            await Make(handler).AskAsync(State, CancellationToken.None);
            var body = (Dictionary<string, object>)MiniJson.Parse(handler.SentBody);
            Assert.That(body["model"], Is.EqualTo(HttpJevTransport.DefaultModel));
            Assert.That(((Dictionary<string, object>)body["state"])["tick"], Is.EqualTo(20d));
            var questions = (Dictionary<string, object>)body["questions"];
            Assert.That(questions.Keys, Is.EquivalentTo(new[] { "decisive_point" }.Concat(JevFacts.All).ToArray()));
            Assert.That(JevQuestions.Version, Is.EqualTo("q7"), "a change of wording is a change of behaviour and must change the version");
        }

        [Test]
        public async Task TheAnswersAreMappedToTheGamesNames()
        {
            var handler = new FakeHandler { Reply = GoodReply };
            var answers = await Make(handler).AskAsync(State, CancellationToken.None);
            Assert.That(answers.Choice, Is.EqualTo(JevChoice.EnemyCore));
            Assert.That(answers.ChoiceConfidence, Is.EqualTo(0.8).Within(1e-9));
            Assert.That(answers.Facts[JevFacts.Outnumbering], Is.EqualTo(0.42).Within(1e-9));
        }

        [TestCase("north_outpost", JevChoice.NorthOutpost)]
        [TestCase("south_outpost", JevChoice.SouthOutpost)]
        [TestCase("my_core", JevChoice.MyCore)]
        [TestCase("enemy_core", JevChoice.EnemyCore)]
        public async Task EveryQuestionedChoiceMapsToAGameChoice(string jev, string game)
        {
            var handler = new FakeHandler { Reply = "{\"answers\":{\"decisive_point\":{\"choice\":\"" + jev + "\",\"confidence\":0.9}}}" };
            Assert.That((await Make(handler).AskAsync(State, CancellationToken.None)).Choice, Is.EqualTo(game));
        }

        [Test]
        public async Task AnUnknownChoiceOrAMissingConfidenceNeverClearsAThreshold()
        {
            var unknown = await Make(new FakeHandler { Reply = "{\"answers\":{\"decisive_point\":{\"choice\":\"west\",\"confidence\":0.99}}}" }).AskAsync(State, CancellationToken.None);
            Assert.That(unknown.Choice, Is.Null);
            var noConfidence = await Make(new FakeHandler { Reply = "{\"answers\":{\"decisive_point\":{\"choice\":\"enemy_core\"}}}" }).AskAsync(State, CancellationToken.None);
            Assert.That(noConfidence.ChoiceConfidence, Is.EqualTo(0));
            var outOfRange = await Make(new FakeHandler { Reply = "{\"answers\":{\"outnumbering\":{\"noul\":1.5}}}" }).AskAsync(State, CancellationToken.None);
            Assert.That(outOfRange.Facts, Is.Empty);
        }

        [Test]
        public async Task InputTokensAccumulateForTheCostDisplay()
        {
            var handler = new FakeHandler { Reply = GoodReply };
            var transport = Make(handler);
            await transport.AskAsync(State, CancellationToken.None);
            await transport.AskAsync(State, CancellationToken.None);
            Assert.That(transport.InputTokens, Is.EqualTo(2468));
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase((HttpStatusCode)429)]
        [TestCase(HttpStatusCode.InternalServerError)]
        public void AFailureStatusIsAnExceptionThatDoesNotRepeatTheKeyOrTheBody(HttpStatusCode status)
        {
            var handler = new FakeHandler { Status = status, Reply = "{\"error\":\"" + Key + "\"}" };
            var thrown = Assert.ThrowsAsync<HttpRequestException>(() => Make(handler).AskAsync(State, CancellationToken.None));
            Assert.That(thrown.Message, Does.Contain(((int)status).ToString()));
            Assert.That(thrown.Message, Does.Not.Contain(Key));
        }

        [Test]
        public void NoKeyFailsBeforeAnythingIsSent()
        {
            var handler = new FakeHandler { Reply = GoodReply };
            Assert.ThrowsAsync<InvalidOperationException>(() => Make(handler, () => null).AskAsync(State, CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(() => Make(handler, () => "").AskAsync(State, CancellationToken.None));
            Assert.That(handler.Calls, Is.EqualTo(0));
        }

        [TestCase("not json")]
        [TestCase("[1,2]")]
        [TestCase("{\"answers\":")]
        [TestCase("{\"model\":\"x\"}")]
        public void AnUnreadableReplyIsAnException(string reply)
        {
            var handler = new FakeHandler { Reply = reply };
            Assert.ThrowsAsync<FormatException>(() => Make(handler).AskAsync(State, CancellationToken.None));
        }

        [Test]
        public void ASlowGatewayIsCutOffAtTheTimeout()
        {
            var handler = new FakeHandler { Reply = GoodReply, Before = token => Task.Delay(5000, token) };
            var clock = Stopwatch.StartNew();
            Assert.CatchAsync<OperationCanceledException>(() => Make(handler, timeout: TimeSpan.FromMilliseconds(100)).AskAsync(State, CancellationToken.None));
            Assert.That(clock.ElapsedMilliseconds, Is.LessThan(3000));
        }

        [Test]
        public async Task TheProviderTurnsAConfidentAnswerIntoAnOrderThroughTheRealTransport()
        {
            // The enemy core is only charged when the "we outnumber them" statement holds too.
            var handler = new FakeHandler { Reply = GoodReply.Replace("\"outnumbering\":{\"noul\":0.42}", "\"outnumbering\":{\"noul\":0.95}") };
            using (var provider = new JevPolicyProvider(Make(handler), new JevThresholds { MinChoiceConfidence = 0.7 }))
            {
                var enemyCore = new KnownObjective(GoalKind.Core, 2, new SimPoint(Fix64.FromInt(240), Fix64.FromInt(64)), true, 2, true, 3000, 10);
                var observation = new FactionObservation(1, 20, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), new[] { enemyCore });
                provider.Request(new PolicyRequest(1, 1, new ScopeKey(1, ScopeKind.All, 0), 20, observation,
                    new[] { new PolicyVersion(new ScopeKey(1, ScopeKind.All, 0), 0) }, 260, PolicyKind.MaintainReserve, default(PolicyGoal)));
                var replies = new List<PolicyReply>();
                var clock = Stopwatch.StartNew();
                while (replies.Count == 0 && clock.ElapsedMilliseconds < 5000) { replies.AddRange(provider.Poll(100)); await Task.Delay(2); }
                var order = replies.Single().Orders.Single();
                Assert.That(order.Kind, Is.EqualTo(PolicyKind.Focus));
                Assert.That(order.Goal, Is.EqualTo(new PolicyGoal(GoalKind.Core, 2, default(SimPoint))));
            }
        }

        [Test, Explicit("Sends ONE real request to the AI gateway. Needs the PROBE_KEY environment variable.")]
        public async Task LiveSmokeOneRealRequest()
        {
            var transport = new HttpJevTransport(() => Environment.GetEnvironmentVariable("PROBE_KEY"), timeout: TimeSpan.FromSeconds(20));
            var clock = Stopwatch.StartNew();
            var observation = new FactionObservation(1, 6000,
                new[] { new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(24), Fix64.FromInt(96)), 8, new PolicyGoal(GoalKind.Outpost, 1, default)) },
                Array.Empty<VisibleEnemy>(),
                new[] { new EnemyContact(1, new SimPoint(Fix64.FromInt(60), Fix64.FromInt(96)), 6000, 14, 18, true) },
                new[]
                {
                    new KnownObjective(GoalKind.Outpost, 1, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(96)), true, 2, false, 0, 6000),
                    new KnownObjective(GoalKind.Outpost, 2, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(32)), true, 0, false, 0, 6000),
                    new KnownObjective(GoalKind.Core, 1, new SimPoint(Fix64.FromInt(16), Fix64.FromInt(64)), true, 1, true, 2100, 6000)
                });
            var answers = await transport.AskAsync(JevState.Build(observation), CancellationToken.None);
            TestContext.Out.WriteLine("live: " + clock.ElapsedMilliseconds + " ms choice=" + answers.Choice + " confidence=" + answers.ChoiceConfidence
                + " facts=" + string.Join(" ", answers.Facts.Select(f => f.Key + "=" + f.Value.ToString("0.00"))) + " inputTokens=" + transport.InputTokens);
            Assert.That(answers.Choice, Is.AnyOf(JevChoice.NorthOutpost, JevChoice.SouthOutpost, JevChoice.MyCore, JevChoice.EnemyCore));
            Assert.That(answers.Facts.Keys, Is.EquivalentTo(JevFacts.All));
        }

        [Test]
        public void TheQuestionTextIsValidJsonAndItsChoicesAreTheOnesTheTransportMaps()
        {
            var questions = (Dictionary<string, object>)MiniJson.Parse(JevQuestions.Json);
            var focus = (Dictionary<string, object>)questions["decisive_point"];
            var criteria = (Dictionary<string, object>)focus["criteria"];
            Assert.That(criteria.Keys, Is.EquivalentTo(new[] { "north_outpost", "south_outpost", "my_core", "enemy_core" }));
        }

        [Test]
        public void MiniJsonReadsEscapesNestingAndRejectsMalformedText()
        {
            var value = (Dictionary<string, object>)MiniJson.Parse(" {\"a\":[1,-2.5e1,true,false,null],\"b\":{\"c\":\"x\\n\\u0041\\\"\"}} ");
            Assert.That(((List<object>)value["a"])[1], Is.EqualTo(-25d));
            Assert.That(((Dictionary<string, object>)value["b"])["c"], Is.EqualTo("x\nA\""));
            foreach (var bad in new[] { "", "{", "{\"a\":1,}", "{\"a\" 1}", "[1 2]", "\"open", "tru", "{} x", "\"\\q\"" })
                Assert.Throws<FormatException>(() => MiniJson.Parse(bad), bad);
            Assert.Throws<FormatException>(() => MiniJson.Parse(new string('[', 200) + new string(']', 200)));
        }
    }
}
