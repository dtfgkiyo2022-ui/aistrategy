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
    /// <summary>
    /// An order runs until it is replaced, so answering the same thing again changes nothing in the match while still
    /// costing a command id and a supersede in the log. Measured over one match the model answered "the north outpost"
    /// twelve times in a row; only the first of those is worth issuing.
    /// </summary>
    public sealed class JevRepeatTests
    {
        private sealed class ScriptedTransport : IJevTransport
        {
            internal JevAnswers Next = Answer(JevChoice.NorthOutpost, 0.9);
            public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel) => Task.FromResult(Next);
        }

        private static readonly KnownObjective North = new KnownObjective(GoalKind.Outpost, 1, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(96)), true, 1, false, 0, 0);
        private static readonly KnownObjective South = new KnownObjective(GoalKind.Outpost, 2, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(32)), true, 0, false, 0, 0);

        private static JevAnswers Answer(string choice, double confidence)
        {
            var answers = new JevAnswers { Choice = choice, ChoiceConfidence = confidence };
            answers.Facts[JevFacts.Outnumbering] = 0.95;
            return answers;
        }

        private static PolicyRequest Request(ulong id, long tick)
        {
            var observation = new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                Array.Empty<EnemyContact>(), new[] { North, South });
            return new PolicyRequest(id, 1, new ScopeKey(1, ScopeKind.All, 0), tick, observation,
                new[] { new PolicyVersion(new ScopeKey(1, ScopeKind.All, 0), 0) }, tick + 240, PolicyKind.Focus, default(PolicyGoal));
        }

        private static PolicyReply Round(JevPolicyProvider provider, ulong id, long tick)
        {
            provider.Request(Request(id, tick));
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 5000)
            {
                var replies = provider.Poll(tick);
                if (replies.Count != 0) return replies.Single();
                Thread.Sleep(2);
            }
            throw new TimeoutException("No reply for request " + id);
        }

        [Test]
        public void TheSameAnswerTwiceIssuesOneOrder()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RepeatSameOrderAfterTicks = 1200 }))
            {
                Assert.That(Round(provider, 1, 20).Orders.Single().Goal.Id, Is.EqualTo(1u));
                Assert.That(Round(provider, 2, 40).Orders, Is.Empty);
                Assert.That(provider.SuppressedCount, Is.EqualTo(1));
                Assert.That(provider.DeclinedCount, Is.EqualTo(0), "the model answered and the answer was usable; it was simply already in force");
            }
        }

        [Test]
        public void AChangeOfAnswerIsIssuedStraightAway()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RepeatSameOrderAfterTicks = 1200 }))
            {
                Round(provider, 1, 20);
                transport.Next = Answer(JevChoice.SouthOutpost, 0.9);
                Assert.That(Round(provider, 2, 40).Orders.Single().Goal.Id, Is.EqualTo(2u));
                Assert.That(provider.SuppressedCount, Is.EqualTo(0));
                // Going back to the first answer is a change again, so it is issued.
                transport.Next = Answer(JevChoice.NorthOutpost, 0.9);
                Assert.That(Round(provider, 3, 60).Orders.Single().Goal.Id, Is.EqualTo(1u));
            }
        }

        [Test]
        public void TheSameOrderIsSentAgainOnceTheWindowHasPassed()
        {
            // The provider is never told whether the gateway accepted the order, so suppression has to be a delay and
            // not a ban: an order that was rejected must get another chance.
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RepeatSameOrderAfterTicks = 1200 }))
            {
                Round(provider, 1, 20);
                Assert.That(Round(provider, 2, 1219).Orders, Is.Empty);
                Assert.That(Round(provider, 3, 1220).Orders.Single().Goal.Id, Is.EqualTo(1u), "1200 ticks after tick 20");
                Assert.That(Round(provider, 4, 1240).Orders, Is.Empty, "and the window starts again from there");
            }
        }

        [Test]
        public void SuppressionCanBeTurnedOff()
        {
            var transport = new ScriptedTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RepeatSameOrderAfterTicks = 0 }))
            {
                for (ulong i = 1; i <= 4; i++) Assert.That(Round(provider, i, 20L * (long)i).Orders, Is.Not.Empty);
                Assert.That(provider.SuppressedCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void ASuppressedAnswerIsMarkedAsSuchInTheDiagnosticLog()
        {
            var transport = new ScriptedTransport();
            var records = new List<JevAnswerRecord>();
            using (var provider = new JevPolicyProvider(transport))
            {
                provider.Observe = records.Add;
                Round(provider, 1, 20);
                Round(provider, 2, 40);
                Assert.That(records.Select(r => r.Suppressed), Is.EqualTo(new[] { false, true }));
                Assert.That(records[1].Choice, Is.EqualTo(JevChoice.NorthOutpost), "what was answered is still recorded");
            }
        }

        [Test]
        public void AnAnswerBelowTheThresholdIsDeclinedNotSuppressed()
        {
            var transport = new ScriptedTransport { Next = Answer(JevChoice.NorthOutpost, 0.2) };
            using (var provider = new JevPolicyProvider(transport))
            {
                Round(provider, 1, 20);
                Round(provider, 2, 40);
                Assert.That(provider.DeclinedCount, Is.EqualTo(2));
                Assert.That(provider.SuppressedCount, Is.EqualTo(0));
            }
        }
    }
}
