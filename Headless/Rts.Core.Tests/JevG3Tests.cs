using System;
using System.Collections.Generic;
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
    public sealed class JevG3Tests
    {
        private static FactionObservation LargeObservation()
        {
            var armies = new List<OwnArmyView>();
            var contacts = new List<EnemyContact>();
            var north = new KnownObjective(GoalKind.Outpost, 1, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(96)), true, 1, false, 0, 0);
            var south = new KnownObjective(GoalKind.Outpost, 2, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(32)), true, 1, false, 0, 0);
            for (uint i = 0; i < 100; i++)
            {
                armies.Add(new OwnArmyView(i + 1, 1, i % 2 == 0 ? UnitKind.Infantry : UnitKind.Cavalry,
                    new SimPoint(Fix64.FromInt(20 + (int)(i % 10)), Fix64.FromInt(30 + (int)(i % 70))), 8,
                    new PolicyGoal(GoalKind.Outpost, i % 2 == 0 ? 1u : 2u, default(SimPoint))));
                contacts.Add(new EnemyContact(i + 1, new SimPoint(Fix64.FromInt(150 + (int)(i % 8)), Fix64.FromInt(30 + (int)(i % 70))),
                    1200, 1, 2, true));
            }
            return new FactionObservation(1, 1200, armies, Array.Empty<VisibleEnemy>(), contacts, new[] { north, south });
        }

        [Test]
        public void GroupedStateStaysWithinTheOneThousandTokenApproximation()
        {
            string state = JevState.Build(LargeObservation());
            Assert.That(state.Length / 4, Is.LessThanOrEqualTo(1000), state);
            Assert.That(state, Does.Not.Contain("VisibleEnemies"));
            Assert.That(state, Does.Contain("\"count\":"));
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            public string Body;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                string content = "{\"answers\":{\"decisive_point\":{\"choice\":\"north_outpost\",\"confidence\":0.9}}}";
                string reply = "{\"choices\":[{\"message\":{\"content\":\"" + content.Replace("\"", "\\\"") + "\"}}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":4}}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
            }
        }

        [Test]
        public async Task LocalTransportUsesSchemaAndReadsTheOpenAiShape()
        {
            var handler = new FakeHandler();
            using (var transport = new LocalLlmTransport(handler: handler))
            {
                var answer = await transport.AskAsync("{\"stateVersion\":\"s5\"}", CancellationToken.None);
                Assert.That(answer.Choice, Is.EqualTo(JevChoice.NorthOutpost));
                Assert.That(answer.ChoiceConfidence, Is.EqualTo(0.9).Within(1e-9));
                Assert.That(answer.Usage.InputTokens, Is.EqualTo(12));
                Assert.That(handler.Body, Does.Contain("json_schema"));
                Assert.That(handler.Body, Does.Contain("jev_judgement"));
                Assert.That(handler.Body, Does.Contain("\"temperature\":0"));
            }
        }
    }
}
