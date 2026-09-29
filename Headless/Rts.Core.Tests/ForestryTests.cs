using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>V3-7 #1: the forestry branch, lumber camp and the old market/replay contracts.</summary>
    public sealed class ForestryTests
    {
        private const int Width = 128;

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> fields, string key)
            => long.Parse(fields[key], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition ForestScenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Forestry = true;
            s.Economy.StartFood = 2000;
            s.Economy.StartWood = 2000;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static ScenarioDefinition ForestSecondAgeScenario(ulong seed)
        {
            var s = ForestScenario(seed);
            s.Economy.Ages = true;
            s.Economy.AdvanceTicks = 1;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.LumberCampWork = 1;
            s.Economy.FletcherWork = 1;
            s.Economy.FletcherTicks = 1;
            s.Economy.BeltTicksPerCell = 1;
            s.Economy.BeltLimit = 2000;
            s.Armies[2].Capacity = 50;
            return s;
        }

        private static int FirstWoodCampOrigin(ScenarioDefinition s)
        {
            var node = s.ResourceNodes.First(n => n.Kind == ResourceKind.Wood);
            int cell = (int)(node.Position.Z.Raw / 65536 / s.Map.CellSizeMeters) * Width
                + (int)(node.Position.X.Raw / 65536 / s.Map.CellSizeMeters);
            int size = s.Economy.LumberCampSizeCells;
            int nx = cell % Width, nz = cell / Width;
            for (int dz = 0; dz < size; dz++)
                for (int dx = 0; dx < size; dx++)
                {
                    int x = nx - dx, z = nz - dz;
                    if (x >= 0 && z >= 0 && x + size <= s.Map.WidthCells && z + size <= s.Map.HeightCells)
                        return z * Width + x;
                }
            return -1;
        }

        [Test]
        public void ForestryTailRoundTripsAndTheFlagOffBytesStaySeparate()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var oldBytes = ScenarioBinary.Encode(old);
            var forest = ForestScenario(1);
            var forestBytes = ScenarioBinary.Encode(forest);
            Assert.That(ScenarioBinary.Decode(oldBytes).Economy.Forestry, Is.False);
            Assert.That(ScenarioBinary.Decode(forestBytes).Economy.Forestry, Is.True);
            Assert.That(forestBytes.Length, Is.GreaterThan(oldBytes.Length));
        }

        [Test]
        public void AForestryFactionCanPlaceAndHandHaulFromALumberCamp()
        {
            var s = ForestScenario(2);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Forestry));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Forestry));

            int origin = FirstWoodCampOrigin(s);
            Assume.That(origin, Is.GreaterThanOrEqualTo(0));
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.LumberCamp, origin, Facing.North));
            Steps(gateway, sim, 1);
            var placed = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.LumberCamp && b.FactionId == 1);
            Assume.That(placed.Id, Is.Not.EqualTo(0u));
            Steps(gateway, sim, 500);
            Assert.That(sim.Capture(1).Economy.Buildings.First(b => b.Id == placed.Id).Complete, Is.True);

            long woodBefore = sim.Capture(1).Economy.Wood;
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1 }, EconomyTargetKind.Building, placed.Id));
            Steps(gateway, sim, 3000);
            Assert.That(sim.Capture(1).Economy.Wood, Is.GreaterThan(woodBefore));

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

        [Test]
        public void AutomaticForestryBuildsItsCampWithoutTerrainScoring()
        {
            var s = ForestScenario(3);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Forestry));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 7000);
            var f = Fields(sim);
            Assert.That(f["Economy[1].Civ"], Is.EqualTo(((byte)CivKind.Forestry).ToString(CultureInfo.InvariantCulture)));
            int buildings = (int)Number(f, "Buildings.Count");
            Assert.That(Enumerable.Range(1, buildings).Any(i => f["Buildings[" + i + "].Kind"] == ((byte)BuildingKind.LumberCamp).ToString(CultureInfo.InvariantCulture)), Is.True);
        }

        [Test]
        public void SecondAgeForestryBuildsTheBowLineAndKeepsItsMaterialsAtomic()
        {
            var s = ForestSecondAgeScenario(4);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Forestry));
            Steps(gateway, sim, 14000);

            var economy = sim.Capture(1).Economy;
            Assert.That(economy.Age, Is.GreaterThanOrEqualTo(2));
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Fletcher && b.Complete), Is.True,
                "第2時代の自動経済がFletcherを完成させる");
            Assert.That(economy.BowGear, Is.GreaterThan(0), "Fletcherが木材と食料からBowGearを生産する");

            var fields = Fields(sim);
            Assert.That(fields.Where(p => p.Key.EndsWith(".QueueKinds[0]", StringComparison.Ordinal))
                .Select(p => p.Value), Does.Not.Contain(((byte)UnitKind.SkirmishArcher).ToString(CultureInfo.InvariantCulture)),
                "自動AIはSkirmishArcherを訓練しない");
        }

        [Test]
        public void HumanForestryCanTrainAndCancelSkirmishArcherWithBowGearRefund()
        {
            var s = ForestSecondAgeScenario(5);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Forestry));
            Steps(gateway, sim, 5000);
            var barracks = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Barracks && b.Complete);
            Assume.That(barracks.Id, Is.Not.EqualTo(0u), "第2時代の兵舎がある");

            gateway.SubmitEconomy(EconomyCommand.Auto(1, 2, false));
            for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, (ulong)(3 + i), barracks.Id));
            Steps(gateway, sim, 1);
            int bowBefore = sim.Capture(1).Economy.BowGear;
            int foodBefore = sim.Capture(1).Economy.Food;
            gateway.SubmitEconomy(EconomyCommand.Train(1, 8, barracks.Id, UnitKind.SkirmishArcher));
            Steps(gateway, sim, 1);
            var queued = sim.Capture(1).Economy.Buildings.First(b => b.Id == barracks.Id);
            Assert.That(queued.Queued, Is.EqualTo(1));
            Assert.That(queued.QueuedBowGear, Is.EqualTo(s.Economy.SkirmishArcherBowGearCost));
            Assert.That(sim.Capture(1).Economy.BowGear, Is.EqualTo(bowBefore - s.Economy.SkirmishArcherBowGearCost));
            Assert.That(sim.Capture(1).Economy.Food, Is.EqualTo(foodBefore - s.Economy.SkirmishArcherFoodCost));
            int bowAfterTrain = sim.Capture(1).Economy.BowGear;
            int foodAfterTrain = sim.Capture(1).Economy.Food;

            gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, 9, barracks.Id));
            Steps(gateway, sim, 1);
            var cancelled = sim.Capture(1).Economy.Buildings.First(b => b.Id == barracks.Id);
            Assert.That(cancelled.Queued, Is.EqualTo(0));
            Assert.That(sim.Capture(1).Economy.BowGear, Is.EqualTo(bowAfterTrain + s.Economy.SkirmishArcherBowGearCost));
            Assert.That(sim.Capture(1).Economy.Food, Is.GreaterThan(foodAfterTrain),
                "訓練取消しで食料が返却される（同じtickのFletcher消費分は別に進む）");
        }
    }
}
