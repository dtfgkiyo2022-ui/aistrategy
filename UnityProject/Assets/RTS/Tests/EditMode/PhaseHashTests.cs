using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;

namespace Rts.Tests.EditMode
{
    /// <summary>Chapter 13.3: per-phase end-of-phase hashes locate the first phase that differs inside one tick.</summary>
    public sealed class PhaseHashTests
    {
        private const long TargetTick = 5;

        private static ScheduledInput Retreat(long acceptedTick) => new ScheduledInput(1, InputKind.Resolve, acceptedTick, acceptedTick + 1, 7, 8,
            new[]
            {
                new PolicyOrder(9, 10, CommandSource.Human, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Retreat,
                    new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 50, new LossBudget(300),
                    new EndCondition(EndKind.UntilReplaced, 0), 0, 0, new PolicyVersion[0], 0,
                    new Expiration(long.MaxValue, 240, ExpireFlags.SubjectGone | ExpireFlags.ObservationTooOld))
            });

        private static List<KeyValuePair<string, string>> PhasesAt(long tick, params ScheduledInput[] inputs)
        {
            var sim = new Simulation.Simulation(WeekOneScenario.Create());
            var phases = new List<KeyValuePair<string, string>>();
            for (long t = 1; t <= tick; t++)
            {
                if (t == tick) sim.PhaseHashObserver = (ordinal, name, hash) => phases.Add(new KeyValuePair<string, string>(name, ReplayBinary.Hex(hash)));
                sim.Step(t, inputs.Where(i => i.AcceptedTick + 1 == t).ToArray());
                sim.PhaseHashObserver = null;
            }
            return phases;
        }

        [Test]
        public void TheObserverSeesEveryPhaseOfTheTickInOrder()
        {
            var phases = PhasesAt(TargetTick);
            Assert.That(phases.Select(p => p.Key).ToArray(), Is.EqualTo(new[]
            {
                "Commands", "AI", "Commands", "EnemySearchCombat", "Movement", "Visibility",
                "EnemySearchCombat", "ObjectivesReinforcements", "Visibility", "Commands", "ObjectivesReinforcements", "Frames"
            }));
        }

        [Test]
        public void TwoIdenticalRunsHaveIdenticalPhaseHashes()
        {
            Assert.That(PhasesAt(TargetTick).Select(p => p.Value), Is.EqualTo(PhasesAt(TargetTick).Select(p => p.Value)));
        }

        [Test]
        public void AnExtraInputMakesTheFirstDifferenceAppearInTheCommandsPhase()
        {
            var plain = PhasesAt(TargetTick);
            var withOrder = PhasesAt(TargetTick, Retreat(TargetTick - 1));
            int first = -1;
            for (int i = 0; i < plain.Count; i++)
                if (plain[i].Value != withOrder[i].Value) { first = i; break; }
            Assert.That(first, Is.EqualTo(0), "An input is applied in the first phase, so that is where the states first differ.");
            Assert.That(plain[first].Key, Is.EqualTo("Commands"));
        }

        [Test]
        public void ObservingPhasesDoesNotChangeTheSimulation()
        {
            var observed = new Simulation.Simulation(WeekOneScenario.Create());
            var quiet = new Simulation.Simulation(WeekOneScenario.Create());
            observed.PhaseHashObserver = (ordinal, name, hash) => { };
            for (long t = 1; t <= 30; t++)
            {
                observed.Step(t, new ScheduledInput[0]);
                quiet.Step(t, new ScheduledInput[0]);
                Assert.That(observed.CaptureDiagnostic().CanonicalState, Is.EqualTo(quiet.CaptureDiagnostic().CanonicalState), "tick " + t);
            }
        }
    }
}
