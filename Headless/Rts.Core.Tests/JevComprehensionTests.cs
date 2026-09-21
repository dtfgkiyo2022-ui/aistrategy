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
    /// The factual questions are all things this code works out for itself, so their only use is as a running check on
    /// whether the model is reading the state. When it stops getting them right, its one real judgement - where the
    /// match is being decided - is not worth acting on either.
    /// </summary>
    public sealed class JevComprehensionTests
    {
        private sealed class ScriptedTransport : IJevTransport
        {
            internal JevAnswers Next;
            public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel) => Task.FromResult(Next);
        }

        // In this observation: we have 8 soldiers and see none, no sighting near our core, and the enemy holds outpost 2.
        private static FactionObservation Observation(long tick) => new FactionObservation(1, tick,
            new[] { new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(24), Fix64.FromInt(96)), 8, default(PolicyGoal)) },
            Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(),
            new[]
            {
                new KnownObjective(GoalKind.Outpost, 1, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(96)), true, 1, false, 0, 0),
                new KnownObjective(GoalKind.Outpost, 2, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(32)), true, 2, false, 0, 0)
            });

        private static PolicyRequest Request(ulong id, long tick) =>
            new PolicyRequest(id, 1, new ScopeKey(1, ScopeKind.All, 0), tick, Observation(tick),
                new[] { new PolicyVersion(new ScopeKey(1, ScopeKind.All, 0), 0) }, tick + 240, PolicyKind.Focus, default(PolicyGoal));

        private static JevAnswers Answers(bool right)
        {
            // Truths for the observation above: outnumbering true, enemy near core false, enemy holds an outpost true.
            var answers = new JevAnswers { Choice = JevChoice.NorthOutpost, ChoiceConfidence = 0.9 };
            answers.Facts[JevFacts.Outnumbering] = right ? 0.98 : 0.02;
            answers.Facts[JevFacts.EnemyNearMyCore] = right ? 0.04 : 0.96;
            answers.Facts[JevFacts.OutpostHeldByEnemy] = right ? 0.95 : 0.05;
            return answers;
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
        public void AModelThatReadsTheStateHasItsChoiceActedOn()
        {
            var transport = new ScriptedTransport { Next = Answers(right: true) };
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RepeatSameOrderAfterTicks = 0 }))
            {
                for (ulong i = 1; i <= 4; i++) Assert.That(Round(provider, i, 20L * (long)i).Orders, Is.Not.Empty);
                Assert.That(provider.ComprehensionCorrect, Is.EqualTo(provider.ComprehensionAnswered));
                Assert.That(provider.NotUnderstoodCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void OnceTheFactualAnswersGoWrongTheChoiceIsNoLongerActedOn()
        {
            var transport = new ScriptedTransport { Next = Answers(right: false) };
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RepeatSameOrderAfterTicks = 0 }))
            {
                // The first reply carries only three answers, which is below the minimum to judge on, so it still counts.
                Assert.That(Round(provider, 1, 20).Orders, Is.Not.Empty, "too little evidence to distrust it yet");
                Assert.That(Round(provider, 2, 40).Orders, Is.Empty);
                Assert.That(Round(provider, 3, 60).Orders, Is.Empty);
                Assert.That(provider.NotUnderstoodCount, Is.EqualTo(2));
                Assert.That(provider.ComprehensionCorrect, Is.EqualTo(0));
                Assert.That(provider.DeclinedCount, Is.EqualTo(0), "the answer was not weighed at all, so it was not declined");
            }
        }

        [Test]
        public void TrustComesBackWhenTheAnswersDo()
        {
            var transport = new ScriptedTransport { Next = Answers(right: false) };
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RepeatSameOrderAfterTicks = 0, ComprehensionWindow = 6 }))
            {
                Round(provider, 1, 20);
                Round(provider, 2, 40);
                Assert.That(Round(provider, 3, 60).Orders, Is.Empty);
                transport.Next = Answers(right: true);
                // The window holds six answers, so two good replies push the two bad ones out of it.
                Round(provider, 4, 80);
                Assert.That(Round(provider, 5, 100).Orders, Is.Not.Empty);
                Assert.That(provider.ComprehensionCorrect, Is.EqualTo(6));
            }
        }

        [Test]
        public void TheCheckCanBeTurnedOff()
        {
            var transport = new ScriptedTransport { Next = Answers(right: false) };
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RepeatSameOrderAfterTicks = 0, MinComprehensionPermille = 0 }))
            {
                for (ulong i = 1; i <= 4; i++) Assert.That(Round(provider, i, 20L * (long)i).Orders, Is.Not.Empty);
                Assert.That(provider.NotUnderstoodCount, Is.EqualTo(0));
                Assert.That(provider.ComprehensionCorrect, Is.EqualTo(0), "it is still measured, it is simply not acted on");
            }
        }

        [Test]
        public void AHeldBackReplyIsMarkedInTheDiagnosticLogWithWhatWasAnswered()
        {
            var transport = new ScriptedTransport { Next = Answers(right: false) };
            var records = new List<JevAnswerRecord>();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { RepeatSameOrderAfterTicks = 0 }))
            {
                provider.Observe = records.Add;
                Round(provider, 1, 20);
                Round(provider, 2, 40);
                Assert.That(records.Select(r => r.Understood), Is.EqualTo(new[] { true, false }));
                Assert.That(records[1].Choice, Is.EqualTo(JevChoice.NorthOutpost), "what was answered is still recorded");
                Assert.That(records[1].Facts.Select(f => f.Name), Is.EqualTo(JevFacts.All));
            }
        }
    }
}
