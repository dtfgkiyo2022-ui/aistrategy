using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class JewelsTests
    {
        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(ScenarioDefinition scenario, bool auto = false)
            {
                Scenario = scenario;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Send(EconomyCommand.Auto(1, ++Sequence, auto));
                Send(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);
            internal void Step(int count) { for (int i = 0; i < count; i++) Gateway.Step(); }
        }

        private static ScenarioDefinition Scenario(bool jewels = true)
        {
            var s = MapGenerator.GenerateTerrain(7711UL, false, true);
            s.Economy.Enabled = true;
            s.Economy.Ages = true;
            s.Economy.Industry = true;
            s.Economy.ProcessingChain = true;
            s.Economy.Mountain = true;
            s.Economy.Jewels = jewels;
            s.Economy.StartFood = 5000;
            s.Economy.StartWood = 5000;
            s.Economy.StartStone = 5000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            s.Economy.Age3Ticks = 1;
            s.Economy.GatherIntervalTicks = 1000000;
            s.Economy.LapidarySizeCells = 1;
            s.Economy.LapidaryWoodCost = 1;
            s.Economy.LapidaryWork = 1;
            s.Economy.LapidaryHp = 10;
            s.Economy.JewelTicks = 120;
            s.Economy.JewelStoneInput = 3;
            s.Economy.JewelOreInput = 1;
            s.Economy.JewelKeepStone = 0;
            s.Economy.JewelWoodReserve = 0;
            // The mechanics are pinned to explicit numbers so retuning the live defaults does not move these tests.
            s.Economy.JewelMercenaryGems = 12;
            s.Economy.JewelMercenaryFood = 40;
            s.Economy.JewelMercenaryInfantryRatio = 3;
            s.Economy.BarracksWoodCost = 1;
            s.Economy.BarracksWork = 1;
            s.Economy.BarracksHp = 10;
            s.Economy.CastleWoodCost = 1;
            s.Economy.CastleStoneCost = 0;
            s.Economy.BeltWoodCost = 1;
            s.Economy.BeltTicksPerCell = 1;
            s.Economy.BeltLimit = 200;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static string Field(Battle simulation, string name)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value)[name];

        private static long Number(Battle simulation, string name)
            => long.Parse(Field(simulation, name), CultureInfo.InvariantCulture);

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private static void SetEconomy(Battle simulation, string name, int value, uint faction = 1)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Economies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var economies = (Array)field.GetValue(world);
            var economy = economies.GetValue((int)faction - 1);
            economy.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(economy, value);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static void SetBuilding(Battle simulation, uint id, string name, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Buildings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var buildings = (Array)field.GetValue(world);
            var building = buildings.GetValue((int)id - 1);
            building.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(building, value);
            buildings.SetValue(building, (int)id - 1);
        }

        private static void Reach(Fixture fixture, CivKind civ, int age = 1)
        {
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, civ));
            fixture.Step(4);
            Assert.That(fixture.Simulation.Capture(1).Economy.Civ, Is.EqualTo(civ));
            for (int current = 1; current < age; current++)
            {
                fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, civ));
                fixture.Step(4);
            }
            Assert.That(fixture.Simulation.Capture(1).Economy.Age, Is.EqualTo(age));
        }

        private static uint Place(Fixture fixture, BuildingKind kind)
        {
            int before = (int)Number(fixture.Simulation, "Buildings.Count");
            int cells = fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, kind, cell, Facing.North));
                fixture.Step(1);
                int after = int.Parse(Field(fixture.Simulation, "Buildings.Count"), CultureInfo.InvariantCulture);
                if (after == before + 1 && Field(fixture.Simulation, "Buildings[" + after + "].Kind") == ((byte)kind).ToString(CultureInfo.InvariantCulture)) return (uint)after;
            }
            Assert.Fail("合法な建設地点が見つからない: " + kind);
            return 0;
        }

        private static uint PlaceAndBuild(Fixture fixture, BuildingKind kind)
        {
            uint id = Place(fixture, kind);
            fixture.Send(EconomyCommand.Assign(1, ++fixture.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            for (int i = 0; i < 2000 && !fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == id).Complete; i++) fixture.Step(1);
            Assert.That(fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == id).Complete, Is.True);
            return id;
        }

        private static (int cell, Facing toward) CoreBelt(ScenarioDefinition scenario)
        {
            int width = scenario.Map.WidthCells;
            int core = (int)(scenario.Cores[0].Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * width
                + (int)(scenario.Cores[0].Position.X.Raw / 65536 / scenario.Map.CellSizeMeters);
            int cx = core % width, cz = core / width;
            foreach (var away in new[] { Facing.East, Facing.North, Facing.South, Facing.West })
            {
                int dx = away == Facing.East ? 1 : away == Facing.West ? -1 : 0;
                int dz = away == Facing.North ? 1 : away == Facing.South ? -1 : 0;
                int x = cx + dx * 3, z = cz + dz * 3;
                int cell = z * width + x;
                if (x >= 0 && z >= 0 && x < width && z < scenario.Map.HeightCells
                    && !scenario.Map.BlockedCellIds.Contains(cell)
                    && scenario.ResourceNodes.All(n => (int)(n.Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * width
                        + (int)(n.Position.X.Raw / 65536 / scenario.Map.CellSizeMeters) != cell))
                    return (cell, (Facing)(((int)away + 2) % 4));
            }
            Assert.Fail("コアへ向かうベルトの場所が見つからない");
            return (0, Facing.North);
        }

        [Test]
        public void JewelsOffIsByteAndHashIdenticalToTheOldScenarioFor6000Ticks()
        {
            var old = Scenario(false);
            var off = Scenario(false);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
            Assert.That(off.Extensions.Any(e => e.Id == 27), Is.False);
            var left = new Battle(old);
            var right = new Battle(off);
            for (long tick = 1; tick <= 6000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
            Assert.That(DiagnosticComparison.Fields(right.CaptureDiagnostic()).Any(p => p.Key.Contains("Jewels", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void LapidaryConsumesThreeStoneAndOneOreIn120TicksThenStopsAndResumes()
        {
            var f = new Fixture(Scenario());
            Reach(f, CivKind.Mountain);
            uint lapidary = PlaceAndBuild(f, BuildingKind.Lapidary);
            SetEconomy(f.Simulation, "Stone", 0);
            SetEconomy(f.Simulation, "Ore", 0);
            SetBuilding(f.Simulation, lapidary, "Input", 3);
            SetBuilding(f.Simulation, lapidary, "InputSecondary", 1);
            long before = Number(f.Simulation, "Economy[1].JewelsProduced");
            f.Step(119);
            Assert.That(Number(f.Simulation, "Economy[1].JewelsProduced"), Is.EqualTo(before));
            f.Step(1);
            Assert.That(Number(f.Simulation, "Economy[1].JewelsProduced"), Is.EqualTo(before + 1));
            SetEconomy(f.Simulation, "Stone", 0);
            SetEconomy(f.Simulation, "Ore", 0);
            f.Step(120);
            Assert.That(Number(f.Simulation, "Economy[1].JewelsProduced"), Is.EqualTo(before + 1));
            SetEconomy(f.Simulation, "Stone", 0);
            SetEconomy(f.Simulation, "Ore", 0);
            SetBuilding(f.Simulation, lapidary, "Input", 3);
            SetBuilding(f.Simulation, lapidary, "InputSecondary", 1);
            f.Step(120);
            Assert.That(Number(f.Simulation, "Economy[1].JewelsProduced"), Is.GreaterThan(before + 1));
        }

        [Test]
        public void ProducedGemReachesCoreByVillagerHauling()
        {
            var f = new Fixture(Scenario());
            Reach(f, CivKind.Mountain);
            uint lapidary = PlaceAndBuild(f, BuildingKind.Lapidary);
            SetEconomy(f.Simulation, "Stone", 0);
            SetEconomy(f.Simulation, "Ore", 0);
            SetBuilding(f.Simulation, lapidary, "Input", 3);
            SetBuilding(f.Simulation, lapidary, "InputSecondary", 1);
            f.Step(120);
            Assert.That(Number(f.Simulation, "Economy[1].Gems"), Is.EqualTo(0));
            f.Send(EconomyCommand.Assign(1, ++f.Sequence, new uint[] { 1 }, EconomyTargetKind.Building, lapidary));
            f.Step(5000);
            Assert.That(Number(f.Simulation, "Economy[1].Gems"), Is.GreaterThan(0));
        }

        [Test]
        public void GemReachesCoreByBelt()
        {
            var scenario = Scenario();
            var route = CoreBelt(scenario);
            scenario.Belts = new[] { new BeltDefinition { Cell = route.cell, FactionId = 1, Facing = route.toward, Item = ResourceKind.Gems } };
            var f = new Fixture(scenario);
            Reach(f, CivKind.Mountain);
            Assert.That(Number(f.Simulation, "Economy[1].Gems"), Is.EqualTo(1));
        }

        [Test]
        public void LapidaryDoesNotTakeStoneAtOrBelowTheFortyStoneReserveButTakesItAt41()
        {
            var scenario = Scenario();
            scenario.Economy.JewelKeepStone = 40;
            var f = new Fixture(scenario);
            Reach(f, CivKind.Mountain);
            uint lapidary = PlaceAndBuild(f, BuildingKind.Lapidary);
            SetEconomy(f.Simulation, "Stone", 40);
            SetEconomy(f.Simulation, "Ore", 1);
            f.Step(1);
            var building = f.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == lapidary);
            Assert.That(building.Input, Is.EqualTo(0));
            SetEconomy(f.Simulation, "Stone", 41);
            f.Step(1);
            building = f.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == lapidary);
            Assert.That(building.Input, Is.EqualTo(1));
        }

        [Test]
        public void OnlyMountainWithJewelsCanBuildLapidary()
        {
            var f = new Fixture(Scenario());
            Reach(f, CivKind.Agrarian);
            int before = (int)Number(f.Simulation, "Buildings.Count");
            f.Send(EconomyCommand.Place(1, ++f.Sequence, BuildingKind.Lapidary, 0, Facing.North));
            f.Step(1);
            Assert.That(Number(f.Simulation, "Buildings.Count"), Is.EqualTo(before));
        }

        [Test]
        public void MountainWithJewelsCanTrainBarracksMercenaryFor12GemsAnd40Food()
        {
            var f = new Fixture(Scenario());
            Reach(f, CivKind.Mountain, 2);
            uint barracks = PlaceAndBuild(f, BuildingKind.Barracks);
            SetEconomy(f.Simulation, "Gems", 12);
            SetEconomy(f.Simulation, "Food", 40);
            f.Send(EconomyCommand.Train(1, ++f.Sequence, barracks, UnitKind.Mercenary));
            f.Step(1);
            Assert.That(Number(f.Simulation, "Economy[1].Gems"), Is.EqualTo(0));
            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.EqualTo(0));
            Assert.That(Number(f.Simulation, "Buildings[" + barracks + "].Queued"), Is.EqualTo(1));
            f.Step(f.Scenario.Economy.MercenaryTicks + 10);
            Assert.That(DiagnosticComparison.Fields(f.Simulation.CaptureDiagnostic()).Any(p => p.Value == ((byte)UnitKind.Mercenary).ToString(CultureInfo.InvariantCulture)), Is.True);
        }

        private static void AssertBarracksMercenaryRejected(CivKind civ, bool jewels, int age, int gems, int food)
        {
            var f = new Fixture(Scenario(jewels));
            Reach(f, civ, age);
            uint barracks = PlaceAndBuild(f, BuildingKind.Barracks);
            SetEconomy(f.Simulation, "Gems", gems);
            SetEconomy(f.Simulation, "Food", food);
            f.Send(EconomyCommand.Train(1, ++f.Sequence, barracks, UnitKind.Mercenary));
            f.Step(1);
            Assert.That(Number(f.Simulation, "Economy[1].Gems"), Is.EqualTo(gems));
            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.EqualTo(food));
            Assert.That(Number(f.Simulation, "Buildings[" + barracks + "].Queued"), Is.EqualTo(0));
        }

        [Test] public void NonMountainBarracksMercenaryIsRejected() => AssertBarracksMercenaryRejected(CivKind.Agrarian, true, 2, 12, 40);
        [Test] public void AgeOneBarracksMercenaryIsRejected() => AssertBarracksMercenaryRejected(CivKind.Mountain, true, 1, 12, 40);
        [Test] public void JewelsOffBarracksMercenaryIsRejected() => AssertBarracksMercenaryRejected(CivKind.Mountain, false, 2, 12, 40);
        [Test] public void BarracksMercenaryWithTooFewGemsIsRejected() => AssertBarracksMercenaryRejected(CivKind.Mountain, true, 2, 11, 40);
        [Test] public void BarracksMercenaryWithTooLittleFoodIsRejected() => AssertBarracksMercenaryRejected(CivKind.Mountain, true, 2, 12, 39);

        [Test]
        public void CancellingBarracksMercenaryReturns12GemsAnd40Food()
        {
            var f = new Fixture(Scenario());
            Reach(f, CivKind.Mountain, 2);
            uint barracks = PlaceAndBuild(f, BuildingKind.Barracks);
            SetEconomy(f.Simulation, "Gems", 12);
            SetEconomy(f.Simulation, "Food", 40);
            f.Send(EconomyCommand.Train(1, ++f.Sequence, barracks, UnitKind.Mercenary));
            f.Step(1);
            f.Send(EconomyCommand.CancelTrain(1, ++f.Sequence, barracks));
            f.Step(1);
            Assert.That(Number(f.Simulation, "Economy[1].Gems"), Is.EqualTo(12));
            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.EqualTo(40));
            Assert.That(Number(f.Simulation, "Economy[1].JewelsConsumed"), Is.EqualTo(0));
        }

        [Test]
        public void MountainCastleStillChargesTheOldTwentyGemsForMercenary()
        {
            var f = new Fixture(Scenario());
            Reach(f, CivKind.Mountain, 3);
            uint castle = PlaceAndBuild(f, BuildingKind.Castle);
            SetEconomy(f.Simulation, "Gems", 20);
            f.Send(EconomyCommand.Train(1, ++f.Sequence, castle, UnitKind.Mercenary));
            f.Step(1);
            Assert.That(Number(f.Simulation, "Economy[1].Gems"), Is.EqualTo(0));
        }

        [Test]
        public void MountainAutoEconomyUsesGemsForMercenariesWithoutStoppingInfantryTraining()
        {
            var scenario = Scenario();
            scenario.Economy.InfantryTrainTicks = 1;
            scenario.Economy.MercenaryTicks = 1;
            var f = new Fixture(scenario);
            Reach(f, CivKind.Mountain, 2);
            uint barracks = PlaceAndBuild(f, BuildingKind.Barracks);
            SetBuilding(f.Simulation, barracks, "Held", false);
            SetEconomy(f.Simulation, "Food", 10000);
            SetEconomy(f.Simulation, "Wood", 10000);
            SetEconomy(f.Simulation, "Metal", 10000);
            SetEconomy(f.Simulation, "Gems", 12);
            f.Send(EconomyCommand.Auto(1, ++f.Sequence, true));
            f.Step(300);
            var snapshot = f.Simulation.CaptureMatchPackSnapshot().Factions[0];
            Assert.That(snapshot.Civ, Is.EqualTo("Mountain"));
            Assert.That(snapshot.JewelsConsumed, Is.GreaterThanOrEqualTo(12));
            Assert.That(snapshot.JewelsConsumed % 12, Is.EqualTo(0));
            Assert.That(snapshot.JewelsTrainedMercenaries, Is.GreaterThan(0));
            Assert.That(snapshot.Soldiers.Single(s => s.Kind == "Infantry").Count, Is.GreaterThan(0));
        }

        [Test]
        public void JewelsMatchRecordsAndReplaysWithTheSameHashEveryTick()
        {
            var scenario = Scenario();
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Mountain));
            for (int i = 0; i < 6000; i++) gateway.Step();
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }
    }
}
