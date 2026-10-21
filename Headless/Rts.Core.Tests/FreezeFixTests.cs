using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>Freeze1 regression checks. These tests stream one canonical hash at a time.</summary>
    public sealed class FreezeFixTests
    {
        [Test]
        public void DefaultTerrainRemainsDeterministicForEveryTick()
        {
            var left = new Battle(MapGenerator.GenerateTerrain(1));
            var right = new Battle(MapGenerator.GenerateTerrain(1));
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
        }

        [Test, Explicit("freeze1 large-map verification")]
        public void LargeSeed3BridgeSearchStaysWithinTraversalBudget()
        {
            var simulation = new Battle(LiveGameRules.Create(3, true, true, false, false), (name, start) => { });
            for (long tick = 1; tick <= 10360; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());

            TestContext.WriteLine("bridgeSearchCalls=" + simulation.BridgeSearchCalls);
            TestContext.WriteLine("bridgeCandidateStartCellsInspected=" + simulation.BridgeCandidateStartCellsInspected);
            TestContext.WriteLine("bridgeCandidatesEvaluated=" + simulation.BridgeCandidatesEvaluated);
            TestContext.WriteLine("bridgeRouteEvaluations=" + simulation.BridgeRouteEvaluations);
            TestContext.WriteLine("bridgeDistanceMapCells=" + simulation.BridgeDistanceMapCells);
            Assert.That(simulation.BridgeRouteEvaluations, Is.LessThan(5_000_000L));
            Assert.That(simulation.BridgeDistanceMapCells, Is.LessThan(500_000_000L));
        }

        [Test, Explicit("freeze1 large-map verification")]
        public void LargeSeed2CavalrySearchReportsItsObservedInputs()
        {
            var simulation = new Battle(LiveGameRules.Create(2, true, true, false, false), (name, start) => { });
            for (long tick = 1; tick <= 4800; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            TestContext.WriteLine("cavalryObjectiveCount=" + simulation.LastCavalryObjectiveCount);
            TestContext.WriteLine("cavalryPathCellsVisited=" + simulation.LastCavalryPathCellsVisited);
            TestContext.WriteLine("cavalryRouteCellsVisited=" + simulation.LastCavalryRouteCellsVisited);
            TestContext.WriteLine("cavalryAlternativeRouteCellsVisited=" + simulation.LastCavalryAlternativeRouteCellsVisited);
            Assert.That(simulation.LastCavalryObjectiveCount, Is.GreaterThanOrEqualTo(0));
        }

        [Test, Explicit("freeze1 large-map verification")]
        public void LargeSeedOutcomesMatchTheRecordedMainRuns()
        {
            AssertOutcome(1, 13580, 2);
            AssertOutcome(2, 15145, 1);
            AssertOutcome(3, 15317, 2);
        }

        private static void AssertOutcome(ulong seed, long expectedTick, uint expectedWinner)
        {
            var simulation = new Battle(LiveGameRules.Create(seed, true, true, false, false));
            for (long tick = 1; tick <= expectedTick; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                if (simulation.Capture(1).Result.HasEnded) break;
            }
            var result = simulation.Capture(1).Result;
            Assert.That(result.HasEnded, Is.True, "seed " + seed);
            Assert.That(simulation.Capture(1).Tick, Is.EqualTo(expectedTick), "seed " + seed + " tick");
            Assert.That(result.WinnerFactionId, Is.EqualTo(expectedWinner), "seed " + seed + " winner");
        }
    }
}
