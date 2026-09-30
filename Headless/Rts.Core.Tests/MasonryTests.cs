using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>V3-8: masonry entry, quarry, terrain choice, defence discounts and the shared food-market policy.</summary>
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

        private static ScenarioDefinition MasonryMatchScenario(ulong seed, bool forestry = false)
        {
            var s = MasonryScenario(seed);
            s.Economy.Forestry = forestry;
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            s.Economy.Age3Ticks = 1;
            return s;
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
            var masonryDecoded = ScenarioBinary.Decode(masonryBytes).Economy;
            Assert.That(masonryDecoded.Masonry, Is.True);
            Assert.That((masonryDecoded.MasonryDefenceCostPermille, masonryDecoded.MasonryDefenceWorkPermille), Is.EqualTo((750, 800)));
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

        [TestCase(0, 3, 0, 0, CivKind.Agrarian)]
        [TestCase(0, 3, 0, 1, CivKind.Masonry)]
        [TestCase(0, 3, 0, 2, CivKind.Masonry)]
        [TestCase(2, 3, 1, 1, CivKind.Metallurgy)]
        [TestCase(0, 3, 2, 1, CivKind.Forestry)]
        public void FourTerrainScoresChooseExpectedCivilisation(int ore, int food, int forest, int stone, CivKind expected)
        {
            Assert.That(EconomyDecision.ChooseCiv(ore, food, forest, stone, 3), Is.EqualTo(expected));
        }

        [Test]
        public void MasonryDefenceDiscountsOnlyApplyToTheMasonryFaction()
        {
            var wood = typeof(Battle).GetMethod("WoodOf", BindingFlags.Instance | BindingFlags.NonPublic);
            var stone = typeof(Battle).GetMethod("StoneOf", BindingFlags.Instance | BindingFlags.NonPublic);
            var work = typeof(Battle).GetMethod("WorkOf", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(wood, Is.Not.Null);
            Assert.That(stone, Is.Not.Null);
            Assert.That(work, Is.Not.Null);
            foreach (var civ in new[] { CivKind.Agrarian, CivKind.Metallurgy, CivKind.Forestry, CivKind.Masonry })
            {
                var s = MasonryMatchScenario(20, forestry: civ == CivKind.Forestry);
                var sim = new Battle(s);
                var gateway = new CommandGateway(sim);
                gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, civ));
                Steps(gateway, sim, 4);
                int expectedWood = civ == CivKind.Masonry ? 150 : 200;
                int expectedStone = civ == CivKind.Masonry ? 225 : 300;
                int expectedTowerWood = civ == CivKind.Masonry ? 37 : 50;
                int expectedTowerStone = civ == CivKind.Masonry ? 75 : 100;
                int expectedTowerWork = civ == CivKind.Masonry ? 200 : 250;
                int expectedWallStone = civ == CivKind.Masonry ? 2 : 3;
                int actualCastleWood = (int)wood.Invoke(sim, new object[] { BuildingKind.Castle, 1u });
                int actualCastleStone = (int)stone.Invoke(sim, new object[] { BuildingKind.Castle, 1u });
                int actualCastleWork = (int)work.Invoke(sim, new object[] { BuildingKind.Castle, 1u });
                int actualTowerWood = (int)wood.Invoke(sim, new object[] { BuildingKind.Tower, 1u });
                int actualTowerStone = (int)stone.Invoke(sim, new object[] { BuildingKind.Tower, 1u });
                int actualTowerWork = (int)work.Invoke(sim, new object[] { BuildingKind.Tower, 1u });
                int actualWallStone = (int)stone.Invoke(sim, new object[] { BuildingKind.Wall, 1u });
                Assert.That((actualCastleWood, actualCastleStone, actualCastleWork), Is.EqualTo((expectedWood, expectedStone, civ == CivKind.Masonry ? 480 : 600)), civ.ToString());
                Assert.That((actualTowerWood, actualTowerStone, actualTowerWork), Is.EqualTo((expectedTowerWood, expectedTowerStone, expectedTowerWork)), civ.ToString());
                Assert.That(actualWallStone, Is.EqualTo(expectedWallStone), civ.ToString());
            }
        }

        [Test]
        public void FirstFiftyMasonryMapsReportAllChoicesAndIncludeMasonry()
        {
            var counts = new Dictionary<CivKind, int>();
            var score = typeof(Battle).GetMethod("CountUsableMasonryStone", BindingFlags.Instance | BindingFlags.NonPublic);
            var choose = typeof(Battle).GetMethod("ChooseCiv", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(score, Is.Not.Null);
            Assert.That(choose, Is.Not.Null);
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var s = MapGenerator.GenerateTerrain(seed, masonry: true);
                var sim = new Battle(s);
                for (int side = 0; side < 2; side++)
                {
                    int stone = (int)score.Invoke(sim, new object[] { (uint)side + 1, s.Cores[side].Position });
                    var civ = (CivKind)choose.Invoke(sim, new object[] { (uint)side + 1 });
                    TestContext.WriteLine("seed " + seed + " faction " + (side + 1) + ": usable stone=" + stone + " -> " + civ);
                    counts[civ] = counts.TryGetValue(civ, out int n) ? n + 1 : 1;
                }
            }
            TestContext.WriteLine("masonry choices: " + string.Join(", ", counts.Select(p => p.Key + "=" + p.Value)));
            Assert.That(counts.ContainsKey(CivKind.Masonry), Is.True, "石工が選ばれる地図がある");
            Assert.That(counts.ContainsKey(CivKind.Agrarian), Is.True);
            Assert.That(counts.ContainsKey(CivKind.Metallurgy), Is.True);
        }

        [TestCase(CivKind.Agrarian, CivKind.Masonry)]
        [TestCase(CivKind.Metallurgy, CivKind.Masonry)]
        [TestCase(CivKind.Forestry, CivKind.Masonry)]
        [TestCase(CivKind.Masonry, CivKind.Masonry)]
        public void MasonryCombinationsReachTheSecondAgeWithoutFault(CivKind west, CivKind east)
        {
            var s = MasonryMatchScenario(21, forestry: west == CivKind.Forestry || east == CivKind.Forestry);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, west));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 2, east));
            for (int i = 0; i < 20000 && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "fault at tick " + sim.Capture(1).Tick);
            }
            var result = sim.Capture(1).Result;
            TestContext.WriteLine(west + " vs " + east + ": tick=" + sim.Capture(1).Tick + ", winner=" + result.WinnerFactionId
                + ", ended=" + result.HasEnded + ", undecided=" + result.IsUndecided
                + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age);
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), west + " reaches the second age");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), east + " reaches the second age");
        }

        [Test]
        public void NormalMasonryStartReplaysForTwentyThousandTicks()
        {
            // Seed 7 is kept as a normal-start probe; the test also records the automatically chosen civs.
            var s = MasonryMatchScenario(7, forestry: true);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 20000);
            TestContext.WriteLine("normal seed 7: civs=" + sim.Capture(1).Economy.Civ + "/" + sim.Capture(2).Economy.Civ
                + ", tick=" + sim.Capture(1).Tick + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age);
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から西が第2時代まで進む");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から東が第2時代まで進む");
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
