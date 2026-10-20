using System;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class LargeEconomyEcon5Tests
    {
        [Test]
        public void LargeEconomyUsesEightForHumanPlacementAndFrameWhileOrdinaryStaysTwo()
        {
            var large = LiveGameRules.Create(1, true, true, false, false);
            var ordinary = LiveGameRules.Create(1, false, true, false, false);
            Assert.That(large.Economy.DropSiteMax, Is.EqualTo(8));
            Assert.That(large.Economy.LargeDropSiteMax, Is.EqualTo(8));
            Assert.That(large.Economy.LargeResourceClusterMaxDropSites, Is.EqualTo(8));
            Assert.That(ordinary.Economy.DropSiteMax, Is.EqualTo(2));

            var simulation = new Battle(large);
            Assert.That(simulation.Capture(1).Economy.DropSiteMax, Is.EqualTo(8));
        }

        [Test]
        public void LargeEconomyHumanPlacementCountsConstructionButNotTown()
        {
            var scenario = LiveGameRules.Create(17, true, true, false, false);
            scenario.Economy.StartWood = 10000;
            scenario.Economy.DropSiteWoodCost = 1;
            scenario.Economy.DropSiteWork = 1;
            scenario.Economy.TownWoodCost = 1;
            scenario.Economy.TownStoneCost = 1;
            scenario.Economy.TownWork = 1;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            ulong sequence = 0;
            var core = scenario.Cores[0].Position;
            var grid = new GridMap(scenario.Map);
            int coreCell = grid.Cell(core);
            var candidates = Enumerable.Range(0, scenario.Map.WidthCells * scenario.Map.HeightCells)
                .Where(cell => Math.Abs(cell % scenario.Map.WidthCells - coreCell % scenario.Map.WidthCells) >= 12
                    && Math.Abs(cell / scenario.Map.WidthCells - coreCell / scenario.Map.WidthCells) >= 4)
                .Where(cell => grid.IsPassable(cell)
                    && scenario.ResourceNodes.All(node => grid.Cell(node.Position) != cell))
                .OrderBy(cell => cell)
                .ToArray();

            int placed = 0;
            foreach (int cell in candidates)
            {
                if (placed >= 8) break;
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.DropSite, cell, Facing.North));
                gateway.Step();
                placed = simulation.Capture(1).Economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.DropSite);
            }
            Assert.That(placed, Is.EqualTo(8), "8つの合法な候補を置けなかった");

            int before = simulation.Capture(1).Economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.DropSite);
            foreach (int cell in candidates.Skip(8).Take(40))
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.DropSite, cell, Facing.North));
            gateway.Step();
            Assert.That(simulation.Capture(1).Economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.DropSite), Is.EqualTo(before));
        }

        [Test]
        public void OrdinaryMapHashesMatchForEveryTickThroughThirtyThousand()
        {
            var left = new Battle(LiveGameRules.Create(23, false, true, false, false));
            var right = new Battle(LiveGameRules.Create(23, false, true, false, false));
            for (long tick = 1; tick <= 30000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
                if (left.Capture(1).Result.HasEnded || right.Capture(1).Result.HasEnded) break;
            }
        }
    }
}
