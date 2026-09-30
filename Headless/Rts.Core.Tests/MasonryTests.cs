using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>V3-8 #1: masonry entry, quarry and the shared food-market policy.</summary>
    public sealed class MasonryTests
    {
        private static ScenarioDefinition MasonryScenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Masonry = true;
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.QuarryWork = 1;
            s.Economy.QuarryIntervalTicks = 1;
            s.Economy.BeltTicksPerCell = 1;
            s.Economy.BeltLimit = 4096;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static int StoneOrigin(ScenarioDefinition s)
        {
            var node = s.ResourceNodes.First(n => n.Kind == ResourceKind.Stone);
            int width = s.Map.WidthCells;
            int cell = (int)(node.Position.Z.Raw / 65536 / s.Map.CellSizeMeters) * width
                + (int)(node.Position.X.Raw / 65536 / s.Map.CellSizeMeters);
            int size = s.Economy.QuarrySizeCells;
            int nx = cell % width, nz = cell / width;
            for (int dz = 0; dz < size; dz++)
                for (int dx = 0; dx < size; dx++)
                {
                    int x = nx - dx, z = nz - dz;
                    if (x >= 0 && z >= 0 && x + size <= s.Map.WidthCells && z + size <= s.Map.HeightCells)
                        return z * width + x;
                }
            return -1;
        }

        [Test]
        public void MasonryTailRoundTripsAndOffBytesRemainTheOldBytes()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var oldBytes = ScenarioBinary.Encode(old);
            var masonry = MasonryScenario(1);
            var masonryBytes = ScenarioBinary.Encode(masonry);
            Assert.That(ScenarioBinary.Decode(oldBytes).Economy.Masonry, Is.False);
            Assert.That(ScenarioBinary.Decode(masonryBytes).Economy.Masonry, Is.True);
            Assert.That(masonryBytes.Length, Is.GreaterThan(oldBytes.Length));

            var combined = MasonryScenario(1);
            combined.Economy.Forestry = true;
            combined.Economy.ProcessingChain = true;
            var combinedDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(combined)).Economy;
            Assert.That(combinedDecoded.Forestry, Is.True);
            Assert.That(combinedDecoded.ProcessingChain, Is.True);
            Assert.That(combinedDecoded.Masonry, Is.True);
        }

        [Test]
        public void ManualMasonryQuarryCanHandHaulStoneToTheCore()
        {
            var s = MasonryScenario(2);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 2, CivKind.Masonry));
            Steps(gateway, sim, 4);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Masonry));

            int origin = StoneOrigin(s);
            Assume.That(origin, Is.GreaterThanOrEqualTo(0));
            gateway.SubmitEconomy(EconomyCommand.Place(1, 3, BuildingKind.Quarry, origin, Facing.North));
            Steps(gateway, sim, 800);
            var quarry = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Quarry && b.FactionId == 1);
            Assert.That(quarry.Id, Is.Not.EqualTo(0u));
            Assert.That(quarry.Complete, Is.True);
            int before = sim.Capture(1).Economy.Stone;
            gateway.SubmitEconomy(EconomyCommand.Assign(1, 4, new uint[] { 1 }, EconomyTargetKind.Building, quarry.Id));
            Steps(gateway, sim, 2500);
            Assert.That(sim.Capture(1).Economy.Stone, Is.GreaterThan(before));
        }

        [Test]
        public void AutomaticMasonryBuildsAQuarryAndDoesNotSendVillagersToStone()
        {
            var s = MasonryScenario(3);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Masonry));
            Steps(gateway, sim, 12000);
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.Civ, Is.EqualTo(CivKind.Masonry));
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Quarry && b.Complete), Is.True);
            Assert.That(economy.Villagers.Any(v => v.CarryKind == ResourceKind.Stone &&
                (v.Activity == VillagerActivity.ToResource || v.Activity == VillagerActivity.Gathering || v.Activity == VillagerActivity.Returning)), Is.False);
            Assert.That(economy.Stone, Is.GreaterThan(0));
            Assert.That(economy.Age, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void MasonryReplayIsDeterministicWhenTheFlagIsOn()
        {
            var s = MasonryScenario(4);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Masonry));
            Steps(gateway, sim, 6000);
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, s, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }
    }
}
