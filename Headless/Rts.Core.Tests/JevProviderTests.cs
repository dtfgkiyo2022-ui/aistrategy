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

        // Two outposts are always in view, because "north" and "south" are decided by position: with fewer than two
        // there is no north to name and no order to give.
        private static readonly KnownObjective North = new KnownObjective(GoalKind.Outpost, 1, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(96)), true, 1, false, 0, 0);
        private static readonly KnownObjective South = new KnownObjective(GoalKind.Outpost, 2, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(32)), true, 0, false, 0, 0);

        private static FactionObservation Observation(long tick, params KnownObjective[] objectives) =>
            new FactionObservation(1, tick, new[] { new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(24), Fix64.FromInt(96)), 8, default(PolicyGoal)) },
                Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), new[] { North, South }.Concat(objectives).ToArray());

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

        private static JevAnswers Focus(string choice, double confidence)
        {
            // Every order that goes anywhere but our own ground needs the "we outnumber them" statement to hold.
            var answers = new JevAnswers { Choice = choice, ChoiceConfidence = confidence };
            answers.Facts[JevFacts.Outnumbering] = 0.95;
            return answers;
        }

        private static JevAnswers Fact(string name, double probability)
        {
            var answers = new JevAnswers();
            answers.Facts[name] = probability;
            return answers;
        }

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
                var first = transport.Wait(1);
                provider.Request(Request(2, 20, Observation(21)));
                var second = transport.Wait(2);
                // The network answers request 2 first. Command ids follow the order replies are received, so this matters.
                second.SetResult(Focus(JevChoice.NorthOutpost, 0.9));
                first.SetResult(Focus(JevChoice.SouthOutpost, 0.9));
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
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { MinChoiceConfidence = 0.7 }))
            {
                provider.Request(Request(1, 20));
                transport.Wait(1).SetResult(Focus(JevChoice.NorthOutpost, 0.69));
                Assert.That(PollUntil(provider, 1).Single().Orders, Is.Empty);
            }
        }

        [Test]
        public void GroundThatIsAlreadyOursIsHeldRatherThanAttacked()
        {
            // North is ours in this observation, so "the decisive point is the north outpost" means hold it.
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Request(Request(1, 20));
                transport.Wait(1).SetResult(Focus(JevChoice.NorthOutpost, 0.8));
                var order = PollUntil(provider, 1).Single().Orders.Single();
                Assert.That(order.Kind, Is.EqualTo(PolicyKind.Defend));
                Assert.That(order.Goal, Is.EqualTo(new PolicyGoal(GoalKind.Outpost, 1, default(SimPoint))));
                Assert.That(order.Source, Is.EqualTo(CommandSource.Ai));
                Assert.That(order.Target, Is.EqualTo(new ScopeKey(1, ScopeKind.All, 0)));
            }
        }

        [TestCase(0.95, PolicyKind.Focus)]
        [TestCase(0.30, PolicyKind.Defend)]
        public void GroundThatIsNotOursIsOnlyAttackedWhenWeOutnumberThem(double outnumbering, PolicyKind expected)
        {
            // South belongs to nobody here. Going to take it is worth it only if we are the larger force; otherwise
            // the same answer means hold the line where it is.
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Request(Request(1, 20));
                var answers = new JevAnswers { Choice = JevChoice.SouthOutpost, ChoiceConfidence = 0.9 };
                answers.Facts[JevFacts.Outnumbering] = outnumbering;
                transport.Wait(1).SetResult(answers);
                var order = PollUntil(provider, 1).Single().Orders.Single();
                Assert.That(order.Kind, Is.EqualTo(expected));
                Assert.That(order.Goal.Id, Is.EqualTo(2u));
            }
        }

        [Test]
        public void ChargingTheEnemyCoreNeedsBothTheChoiceAndTheNumbers()
        {
            var seen = new KnownObjective(GoalKind.Core, 2, new SimPoint(Fix64.FromInt(240), Fix64.FromInt(64)), true, 2, true, 3000, 10);
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Request(Request(1, 20, Observation(20, seen)));
                var outnumbered = new JevAnswers { Choice = JevChoice.EnemyCore, ChoiceConfidence = 0.99 };
                outnumbered.Facts[JevFacts.Outnumbering] = 0.05;
                transport.Wait(1).SetResult(outnumbered);
                Assert.That(PollUntil(provider, 1).Single().Orders, Is.Empty, "confident about where, but we are the smaller force");
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
                var withoutCore = transport.Wait(1);
                provider.Request(Request(2, 20, Observation(20, seen)));
                var withCore = transport.Wait(2);
                withoutCore.SetResult(Focus(JevChoice.EnemyCore, 0.9));
                withCore.SetResult(Focus(JevChoice.EnemyCore, 0.9));
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
        public void ANoulAnswerIsRecordedButNeverBecomesAnOrder()
        {
            // Two action-shaped noul questions in a row sat near 0.3 whatever the match did, so q3 asks a statement
            // this code can check for itself and nothing is derived from the answer until that score says it tracks.
            var transport = new ScriptedTransport();
            var records = new List<JevAnswerRecord>();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Observe = records.Add;
                provider.Request(Request(1, 20, Observation(20)));
                transport.Wait(1).SetResult(Fact(JevFacts.Outnumbering, 0.99));
                Assert.That(PollUntil(provider, 1).Single().Orders, Is.Empty);
                Assert.That(records.Single().Facts.Single(f => f.Name == JevFacts.Outnumbering).Probability, Is.EqualTo(0.99).Within(1e-9));
            }
        }
    }
}
