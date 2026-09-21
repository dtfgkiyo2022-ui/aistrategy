using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Providers;
using Rts.Simulation;

namespace Rts.Tests.Headless
{
    /// <summary>
    /// Ver.2 first slice: the invariants the design sketch (sections 3 and 5) requires of a provider that talks to an
    /// external model. Everything runs against a fake transport; no network is touched.
    /// </summary>
    public sealed class JevProviderTests
    {
        private sealed class ScriptedTransport : IJevTransport
        {
            internal readonly Dictionary<string, TaskCompletionSource<JevAnswers>> Calls = new Dictionary<string, TaskCompletionSource<JevAnswers>>();
            internal readonly List<string> States = new List<string>();
            public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel)
            {
                var source = new TaskCompletionSource<JevAnswers>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (Calls) { States.Add(stateJson); Calls[stateJson] = source; }
                return source.Task;
            }
            internal TaskCompletionSource<JevAnswers> Wait(int count)
            {
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 5000)
                {
                    lock (Calls) { if (States.Count >= count) return Calls[States[count - 1]]; }
                    Thread.Sleep(2);
                }
                throw new TimeoutException("The provider never started call " + count);
            }
        }

        private static FactionObservation Observation(long tick, params KnownObjective[] objectives) =>
            new FactionObservation(1, tick, new[] { new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(24), Fix64.FromInt(96)), 8, default(PolicyGoal)) },
                Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), objectives);

        private static PolicyRequest Request(ulong id, long tick, FactionObservation observation = null) =>
            new PolicyRequest(id, 1, new ScopeKey(1, ScopeKind.All, 0), tick, observation ?? Observation(tick),
                new[] { new PolicyVersion(new ScopeKey(1, ScopeKind.All, 0), 0) }, tick + 240, PolicyKind.MaintainReserve, default(PolicyGoal));

        private static IReadOnlyList<PolicyReply> PollUntil(JevPolicyProvider provider, int replies)
        {
            var got = new List<PolicyReply>();
            var clock = Stopwatch.StartNew();
            while (got.Count < replies && clock.ElapsedMilliseconds < 5000) { got.AddRange(provider.Poll(100)); Thread.Sleep(2); }
            return got;
        }

        private static JevAnswers Focus(string choice, double confidence) => new JevAnswers { Focus = choice, FocusConfidence = confidence };

        [Test]
        public void PollAndRequestNeverWaitForTheNetwork()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                var clock = Stopwatch.StartNew();
                provider.Request(Request(1, 20));
                var replies = provider.Poll(21);
                clock.Stop();
                // The call is still open, so there is nothing to hand back and nothing to wait for.
                Assert.That(replies, Is.Empty);
                Assert.That(clock.ElapsedMilliseconds, Is.LessThan(500));
            }
        }

        [Test]
        public void AnswersAreHandedBackInRequestIdOrderNotInArrivalOrder()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Request(Request(1, 20, Observation(20)));
                provider.Request(Request(2, 20, Observation(21)));
                var first = transport.Wait(1); var second = transport.Wait(2);
                // The network answers request 2 first. Command ids follow the order replies are received, so this matters.
                second.SetResult(Focus("north", 0.9));
                first.SetResult(Focus("south", 0.9));
                var replies = PollUntil(provider, 2);
                Assert.That(replies.Select(r => r.RequestId), Is.EqualTo(new ulong[] { 1, 2 }));
            }
        }

        [Test]
        public void AFailedCallBecomesAnEmptyReplyAndIsCounted()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Request(Request(7, 20));
                transport.Wait(1).SetException(new InvalidOperationException("HTTP 429"));
                var replies = PollUntil(provider, 1);
                Assert.That(replies.Single().Orders, Is.Empty);
                Assert.That(replies.Single().ReturnedTick, Is.EqualTo(100));
                Assert.That(provider.FailureCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void ALowConfidenceChoiceIssuesNoOrder()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { MinFocusConfidence = 0.7 }))
            {
                provider.Request(Request(1, 20));
                transport.Wait(1).SetResult(Focus("north", 0.69));
                Assert.That(PollUntil(provider, 1).Single().Orders, Is.Empty);
            }
        }

        [TestCase("north", 1u)]
        [TestCase("south", 2u)]
        public void AConfidentChoiceBecomesAFocusOnThatOutpost(string choice, uint outpost)
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Request(Request(1, 20));
                transport.Wait(1).SetResult(Focus(choice, 0.8));
                var order = PollUntil(provider, 1).Single().Orders.Single();
                Assert.That(order.Kind, Is.EqualTo(PolicyKind.Focus));
                Assert.That(order.Goal.Kind, Is.EqualTo(GoalKind.Outpost));
                Assert.That(order.Goal.Id, Is.EqualTo(outpost));
                Assert.That(order.Source, Is.EqualTo(CommandSource.Ai));
                Assert.That(order.Target, Is.EqualTo(new ScopeKey(1, ScopeKind.All, 0)));
            }
        }

        [Test]
        public void TheEnemyCoreCanOnlyBeNamedOnceItHasBeenSeen()
        {
            var seen = new KnownObjective(GoalKind.Core, 2, new SimPoint(Fix64.FromInt(240), Fix64.FromInt(64)), true, 2, true, 3000, 10);
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Request(Request(1, 20, Observation(20)));
                provider.Request(Request(2, 20, Observation(20, seen)));
                transport.Wait(1).SetResult(Focus("core", 0.9));
                transport.Wait(2).SetResult(Focus("core", 0.9));
                var replies = PollUntil(provider, 2);
                Assert.That(replies[0].Orders, Is.Empty, "the enemy core is not in this observation");
                Assert.That(replies[1].Orders.Single().Goal, Is.EqualTo(new PolicyGoal(GoalKind.Core, 2, default(SimPoint))));
            }
        }

        [Test]
        public void AnUnknownChoiceIsNotGuessedAt()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Request(Request(1, 20));
                transport.Wait(1).SetResult(Focus("west", 0.99));
                Assert.That(PollUntil(provider, 1).Single().Orders, Is.Empty);
            }
        }

        [Test]
        public void ARetreatProbabilityAtTheThresholdIssuesARetreat()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RetreatProbability = 0.7 }))
            {
                provider.Request(Request(1, 20, Observation(20)));
                provider.Request(Request(2, 20, Observation(21)));
                transport.Wait(1).SetResult(new JevAnswers { RetreatProbability = 0.69 });
                transport.Wait(2).SetResult(new JevAnswers { RetreatProbability = 0.70 });
                var replies = PollUntil(provider, 2);
                Assert.That(replies[0].Orders, Is.Empty);
                Assert.That(replies[1].Orders.Single().Kind, Is.EqualTo(PolicyKind.Retreat));
            }
        }

        [Test]
        public void TheStateSentToTheModelIsFixedWhenTheRequestIsMade()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                var request = Request(1, 20);
                provider.Request(request);
                string sent = transport.Wait(1) != null ? transport.States[0] : null;
                Assert.That(sent, Is.EqualTo(JevState.Build(request.Observation)));
                Assert.That(sent, Does.Contain("\"tick\":20").And.Contain("\"myTotalSoldiers\":8"));
                // Meters must not depend on the machine's locale.
                var previous = System.Globalization.CultureInfo.CurrentCulture;
                try
                {
                    System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                    var half = new FactionObservation(1, 20, new[] { new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromRaw(1605632), Fix64.FromInt(96)), 8, default(PolicyGoal)) },
                        Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>());
                    Assert.That(JevState.Build(half), Does.Contain("\"x\":24.5"));
                }
                finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
            }
        }

        [Test]
        public void AMatchKeepsRunningWhenEveryCallFails()
        {
            var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
            var transport = new AlwaysFailingTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                var gateway = new CommandGateway(sim, provider);
                gateway.EnableAutonomous(new UserPolicyIntent(0, new ScopeKey(1, ScopeKind.All, 0), PolicyKind.MaintainReserve, default(PolicyGoal),
                    50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 100, new Expiration(long.MaxValue, 0, ExpireFlags.None)));
                for (int i = 0; i < 300; i++) { gateway.Step(); Thread.Sleep(1); }
                Assert.That(sim.Capture(1).Result.IsFault, Is.False);
                Assert.That(provider.FailureCount, Is.GreaterThan(0));
            }
        }

        private sealed class AlwaysFailingTransport : IJevTransport
        {
            public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel) => Task.FromException<JevAnswers>(new InvalidOperationException("offline"));
        }
    }
}
