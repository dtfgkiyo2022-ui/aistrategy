using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class RetreatReturnTests
    {
        private static SimPoint P(int x, int z = 64) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        private static FactionObservation Observation(OwnArmyView army, VisibleEnemy[] enemies = null)
        {
            return new FactionObservation(1, 100, new[] { army }, enemies ?? Array.Empty<VisibleEnemy>(),
                Array.Empty<EnemyContact>(), new[] {
                    new KnownObjective(GoalKind.Core, 1, P(24), true, 1, true, 3000, 100),
                    new KnownObjective(GoalKind.Core, 2, P(232), true, 2, true, 3000, 100) });
        }

        [Test]
        public void SoldiersAddedAfterRetreatStartsDoNotDelayTheReturn()
        {
            var army = new OwnArmyView(1, 1, UnitKind.Infantry, P(24), 2, new PolicyGoal(GoalKind.Core, 1, default));
            var observation = Observation(army);
            var inputs = new[] { new ArmyDecisionInput(army, default, false,
                new ArmyDecisionMemory { Returning = true }, Array.Empty<ObjectiveRoute>()) };
            var own = new[] { new OffenseArmyInput(1, new[] { P(24), P(80) }, Fix64.FromInt(2).Raw,
                false, true) };
            var result = new[] { inputs[0].Memory };

            OffenseDecision.Retreat(observation, 100, new FactionOffenseMemory(), inputs, own, result, null);

            Assert.That(result[0].Returning, Is.False);
            Assert.That(result[0].HoldUntilTick, Is.EqualTo(160));
            Assert.That(result[0].Assignment, Is.EqualTo(AssignmentKind.Reserve));
        }

        [Test]
        public void TerrainFourArmyTwoDoesNotRemainInRetreatForThreeThousandTicks()
        {
            Assert.That(Battle.HomeArrivalRadius, Is.EqualTo(24));
            Assert.That(PolicyDecision.Within(P(44), P(24), 24), Is.True);
            Assert.That(PolicyDecision.Within(P(44), P(24), 4), Is.False);
            var sim = new Battle(MapGenerator.GenerateTerrain(4));
            int current = 0, longest = 0;
            for (long tick = 1; tick <= 3000; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var fields = DiagnosticComparison.Fields(sim.CaptureDiagnostic())
                    .ToDictionary(p => p.Key, p => p.Value);
                bool returning = fields["Ai.Armies[2].Returning"] == "1";
                if (returning) { current++; longest = Math.Max(longest, current); }
                else current = 0;
            }

            Assert.That(longest, Is.LessThan(3000));
        }

        [Test]
        public void ScoutArmyLeavesRetreatWhenNoEnemyIsVisible()
        {
            var army = new OwnArmyView(1, 1, UnitKind.Scout, P(100), 2, new PolicyGoal(GoalKind.Core, 1, default));
            var enemy = new[] { new VisibleEnemy(1, P(120), (byte)UnitKind.Infantry) };
            var returning = new ArmyDecisionMemory { Returning = true };

            returning = PolicyDecision.AssessRetreat(Observation(army, enemy), army, default, returning, 100, false, false);
            Assert.That(returning.Returning, Is.True);

            returning = PolicyDecision.AssessRetreat(Observation(army), army, default, returning, 100, false, false);
            Assert.That(returning.Returning, Is.False);
            Assert.That(returning.HoldUntilTick, Is.EqualTo(160));
            Assert.That(returning.Assignment, Is.EqualTo(AssignmentKind.Reserve));
        }
    }
}
