using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Tests.Headless
{
    public sealed class JevQuestionSelectionTests
    {
        private sealed class CapturingTransport : IJevTransport, IQuestionAwareJevTransport
        {
            internal readonly List<string> Questions = new List<string>();

            public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel) =>
                Task.FromResult(new JevAnswers());

            public Task<JevAnswers> AskAsync(string stateJson, string questionsJson, CancellationToken cancel)
            {
                lock (Questions) Questions.Add(questionsJson);
                return Task.FromResult(new JevAnswers());
            }
        }

        private static PolicyRequest Request(ulong id, long tick) =>
            new PolicyRequest(id, 1, new ScopeKey(1, ScopeKind.All, 0), tick,
                new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                    Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>()),
                new[] { new PolicyVersion(new ScopeKey(1, ScopeKind.All, 0), 0) }, tick + 240,
                PolicyKind.Focus, default(PolicyGoal));

        private static string[] Names(JevQuestionContext context) =>
            JevQuestions.Names(JevQuestions.Build(context)).ToArray();

        [Test]
        public void TheRegularSetHasOnlyTheThreeRecurringJudgements()
        {
            Assert.That(Names(new JevQuestionContext { IncludeComprehension = false }),
                Is.EqualTo(new[] { "decisive_point", "dangerous_outpost", "retreat_north" }));
            Assert.That(Names(new JevQuestionContext { IncludeComprehension = true }),
                Is.EqualTo(new[] { "decisive_point", "dangerous_outpost", "retreat_north",
                    "outnumbering", "enemy_near_my_core", "outpost_held_by_enemy" }));
        }

        [Test]
        public void OperationAndInstructionQuestionsAreAddedOnlyByTheirContext()
        {
            var operation = Names(new JevQuestionContext { OperationConditionNeeded = true, IncludeComprehension = false });
            Assert.That(operation, Does.Contain("operation_north_broken"));
            Assert.That(operation, Does.Not.Contain("instruction_kind"));

            var instruction = Names(new JevQuestionContext { InstructionTranslationNeeded = true, IncludeComprehension = false });
            Assert.That(instruction, Does.Contain("instruction_kind"));
            Assert.That(instruction, Does.Contain("instruction_target"));
            Assert.That(instruction, Does.Contain("instruction_goal"));
            Assert.That(instruction, Does.Not.Contain("operation_north_broken"));
        }

        [Test]
        public void ComprehensionIsFirstAndThenEveryConfiguredNumberOfCalls()
        {
            var transport = new CapturingTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { ComprehensionIntervalCalls = 3 }))
            {
                for (ulong i = 1; i <= 4; i++)
                {
                    provider.Request(Request(i, (long)i * 20));
                    var clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < 5000)
                    {
                        if (provider.Poll((long)i * 20).Count != 0) break;
                        Thread.Sleep(1);
                    }
                }
            }
            var sent = transport.Questions.SelectMany(q => new[] { JevQuestions.Names(q) }).ToArray();
            Assert.That(sent.Length, Is.EqualTo(4));
            Assert.That(sent[0], Does.Contain("outnumbering"));
            Assert.That(sent[1], Does.Not.Contain("outnumbering"));
            Assert.That(sent[2], Does.Not.Contain("outnumbering"));
            Assert.That(sent[3], Does.Contain("outnumbering"));
        }

        [Test]
        public void TheFixedGroupedStateAndRegularQuestionsStayNearEightHundredTokens()
        {
            var armies = Enumerable.Range(0, 100).Select(i => new OwnArmyView((uint)i + 1, 1,
                UnitKind.Infantry, new SimPoint(Fix64.FromInt(20 + i % 10), Fix64.FromInt(30 + i % 70)), 8,
                new PolicyGoal(GoalKind.Outpost, i % 2 == 0 ? 1u : 2u, default(SimPoint)))).ToArray();
            var contacts = Enumerable.Range(0, 100).Select(i => new EnemyContact((uint)i + 1,
                new SimPoint(Fix64.FromInt(150 + i % 8), Fix64.FromInt(30 + i % 70)), 1200, 1, 2, true)).ToArray();
            var observation = new FactionObservation(1, 1200, armies, Array.Empty<VisibleEnemy>(), contacts,
                new[]
                {
                    new KnownObjective(GoalKind.Outpost, 1, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(96)), true, 1, false, 0, 0),
                    new KnownObjective(GoalKind.Outpost, 2, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(32)), true, 1, false, 0, 0)
                });
            string state = JevState.Build(observation);
            string questions = JevQuestions.Build(new JevQuestionContext { IncludeComprehension = true });
            string body = "{\"model\":\"jev-latest\",\"state\":" + state + ",\"questions\":" + questions + "}";
            string regularQuestions = JevQuestions.Build(new JevQuestionContext { IncludeComprehension = false });
            string regularBody = "{\"model\":\"jev-latest\",\"state\":" + state + ",\"questions\":" + regularQuestions + "}";
            TestContext.Out.WriteLine("regular input: " + body.Length + " chars, approximately " + body.Length / 4 + " tokens");
            TestContext.Out.WriteLine("regular input without comprehension: " + regularBody.Length + " chars, approximately " + regularBody.Length / 4 + " tokens");
            Assert.That(body.Length / 4, Is.LessThanOrEqualTo(800), body.Length.ToString());
        }
    }
}
