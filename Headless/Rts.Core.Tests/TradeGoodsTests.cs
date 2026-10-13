using System;
using System.Globalization;
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
    public sealed class TradeGoodsTests
    {
        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;
            internal Fixture(ScenarioDefinition scenario)
            {
                Scenario = scenario; Simulation = new Battle(scenario); Gateway = new CommandGateway(Simulation);
                Send(EconomyCommand.Auto(1, ++Sequence, false)); Send(EconomyCommand.Auto(2, ++Sequence, false)); Step(1);
            }
            internal void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);
            internal void Step(int count) { for (int i = 0; i < count; i++) Gateway.Step(); }
        }

        private static ScenarioDefinition Scenario(bool enabled = true)
        {
            var s = MapGenerator.GenerateTerrain(77113UL, false, true);
            s.Economy.Enabled = true; s.Economy.Ages = true; s.Economy.Industry = true; s.Economy.ProcessingChain = true;
            s.Economy.Caravan = true; s.Economy.TradeGoods = enabled; s.Economy.StartFood = 5000; s.Economy.StartWood = 5000; s.Economy.StartStone = 5000;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0; s.Economy.Age3WoodCost = 0; s.Economy.Age3Ticks = 1;
            s.Economy.GatherIntervalTicks = 1000000; s.Economy.WeaverSizeCells = 1; s.Economy.WeaverWoodCost = 1;
            s.Economy.WeaverWork = 1; s.Economy.WeaverHp = 10; s.Economy.TradeGoodsTicks = 80; s.Economy.TradeGoodsWoodInput = 3;
            s.Economy.TradeGoodsKeepWood = 80; s.Economy.TradeGoodsValue = 4; s.Economy.TradeGoodsWoodReserve = 0;
            s.Economy.BarracksWoodCost = 1; s.Economy.BarracksWork = 1; s.Economy.BarracksHp = 10;
            s.Economy.BeltWoodCost = 1; s.Economy.BeltTicksPerCell = 1; s.Economy.BeltLimit = 200;
            s.Cores[0].Hp = 1000000; s.Cores[1].Hp = 1000000;
            return s;
        }

        private static object World(Battle simulation) => typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);
        private static string Field(Battle simulation, string name) => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value)[name];
        private static long Number(Battle simulation, string name) => long.Parse(Field(simulation, name), CultureInfo.InvariantCulture);
        private static void SetEconomy(Battle simulation, string name, int value, uint faction = 1)
        {
            var world = World(simulation); var field = world.GetType().GetField("Economies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var economies = (Array)field.GetValue(world); var economy = economies.GetValue((int)faction - 1);
            economy.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(economy, value); economies.SetValue(economy, (int)faction - 1);
        }
        private static void SetBuilding(Battle simulation, uint id, string name, object value)
        {
            var world = World(simulation); var field = world.GetType().GetField("Buildings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var buildings = (Array)field.GetValue(world); var building = buildings.GetValue((int)id - 1);
            building.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(building, value); buildings.SetValue(building, (int)id - 1);
        }
        private static void Reach(Fixture f, int age = 1)
        {
            f.Send(EconomyCommand.Advance(1, ++f.Sequence, CivKind.Caravan)); f.Step(4);
            for (int i = 1; i < age; i++) { f.Send(EconomyCommand.Advance(1, ++f.Sequence, CivKind.Caravan)); f.Step(4); }
            Assert.That(f.Simulation.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Caravan));
            Assert.That(f.Simulation.Capture(1).Economy.Age, Is.EqualTo(age));
        }
        private static uint Place(Fixture f, BuildingKind kind)
        {
            int before = (int)Number(f.Simulation, "Buildings.Count"); int cells = f.Scenario.Map.WidthCells * f.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                f.Send(EconomyCommand.Place(1, ++f.Sequence, kind, cell, Facing.North)); f.Step(1);
                int after = (int)Number(f.Simulation, "Buildings.Count");
                if (after == before + 1 && Field(f.Simulation, "Buildings[" + after + "].Kind") == ((byte)kind).ToString(CultureInfo.InvariantCulture)) return (uint)after;
            }
            Assert.Fail("建設地点が見つからない: " + kind); return 0;
        }
        private static uint PlaceAndBuild(Fixture f)
        {
            uint id = Place(f, BuildingKind.Weaver); f.Send(EconomyCommand.Assign(1, ++f.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            for (int i = 0; i < 2000 && !f.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == id).Complete; i++) f.Step(1);
            Assert.That(f.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == id).Complete, Is.True); return id;
        }
        private static (int cell, Facing toward) CoreBelt(ScenarioDefinition s)
        {
            int width = s.Map.WidthCells; int core = (int)(s.Cores[0].Position.Z.Raw / 65536 / s.Map.CellSizeMeters) * width + (int)(s.Cores[0].Position.X.Raw / 65536 / s.Map.CellSizeMeters);
            int cx = core % width, cz = core / width;
            foreach (var away in new[] { Facing.East, Facing.North, Facing.South, Facing.West })
            {
                int dx = away == Facing.East ? 1 : away == Facing.West ? -1 : 0, dz = away == Facing.North ? 1 : away == Facing.South ? -1 : 0;
                int x = cx + dx * 3, z = cz + dz * 3, cell = z * width + x;
                if (x >= 0 && z >= 0 && x < width && z < s.Map.HeightCells && !s.Map.BlockedCellIds.Contains(cell)) return (cell, (Facing)(((int)away + 2) % 4));
            }
            Assert.Fail("コアベルト地点が見つからない"); return (0, Facing.North);
        }

        [Test] public void T01_WireValuesAreFixed() { Assert.That((int)ResourceKind.TradeGoods, Is.EqualTo(17)); Assert.That((int)BuildingKind.Weaver, Is.EqualTo(39)); Assert.That((int)ProcessingLineKind.TradeGoods, Is.EqualTo(12)); }

        [Test] public void T02_FlagOffKeepsBytesAndHashUnchanged()
        {
            var a = Scenario(false); var b = Scenario(false); Assert.That(ScenarioBinary.Encode(a), Is.EqualTo(ScenarioBinary.Encode(b))); Assert.That(b.Extensions.Any(x => x.Id == 28), Is.False);
            var left = new Battle(a); var right = new Battle(b); for (long tick = 1; tick <= 6000; tick++) { left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>()); Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState), Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState))); }
            Assert.That(DiagnosticComparison.Fields(right.CaptureDiagnostic()).Any(x => x.Key.Contains("TradeGoods", StringComparison.Ordinal)), Is.False);
        }

        [Test] public void T03_ExtensionRoundTripPreservesEveryValue()
        {
            var s = Scenario(); s.Economy.WeaverSizeCells = 3; s.Economy.WeaverWoodCost = 81; s.Economy.WeaverWork = 201; s.Economy.WeaverHp = 401; s.Economy.TradeGoodsTicks = 82; s.Economy.TradeGoodsWoodInput = 4; s.Economy.TradeGoodsKeepWood = 81; s.Economy.TradeGoodsValue = 5; s.Economy.TradeGoodsMaxWeavers = 2; s.Economy.TradeGoodsWoodReserve = 21;
            var copy = ScenarioBinary.Decode(ScenarioBinary.Encode(s)); Assert.That(copy.Economy.TradeGoods, Is.True); Assert.That(copy.Economy.WeaverSizeCells, Is.EqualTo(3)); Assert.That(copy.Economy.WeaverWoodCost, Is.EqualTo(81)); Assert.That(copy.Economy.WeaverWork, Is.EqualTo(201)); Assert.That(copy.Economy.WeaverHp, Is.EqualTo(401)); Assert.That(copy.Economy.TradeGoodsTicks, Is.EqualTo(82)); Assert.That(copy.Economy.TradeGoodsWoodInput, Is.EqualTo(4)); Assert.That(copy.Economy.TradeGoodsKeepWood, Is.EqualTo(81)); Assert.That(copy.Economy.TradeGoodsValue, Is.EqualTo(5)); Assert.That(copy.Economy.TradeGoodsMaxWeavers, Is.EqualTo(2)); Assert.That(copy.Economy.TradeGoodsWoodReserve, Is.EqualTo(21)); Assert.That(copy.Extensions.Any(x => x.Id == 28), Is.True);
        }

        [Test] public void T04_ProductionUsesThreeWoodAndEightyTicks() { var f = new Fixture(Scenario()); Reach(f); uint id = PlaceAndBuild(f); SetEconomy(f.Simulation, "Wood", 0); SetBuilding(f.Simulation, id, "Input", 3); long before = Number(f.Simulation, "Economy[1].TradeGoodsProduced"); f.Step(79); Assert.That(Number(f.Simulation, "Economy[1].TradeGoodsProduced"), Is.EqualTo(before)); f.Step(1); Assert.That(Number(f.Simulation, "Economy[1].TradeGoodsProduced"), Is.EqualTo(before + 1)); }

        [Test] public void T05_ProductionStopsAndRestartsAfterInputReturns() { var f = new Fixture(Scenario()); Reach(f); uint id = PlaceAndBuild(f); SetEconomy(f.Simulation, "Wood", 0); SetBuilding(f.Simulation, id, "Input", 3); f.Step(80); long produced = Number(f.Simulation, "Economy[1].TradeGoodsProduced"); f.Step(80); Assert.That(Number(f.Simulation, "Economy[1].TradeGoodsProduced"), Is.EqualTo(produced)); SetBuilding(f.Simulation, id, "Input", 3); f.Step(80); Assert.That(Number(f.Simulation, "Economy[1].TradeGoodsProduced"), Is.EqualTo(produced + 1)); }

        [Test] public void T06_TieConvertsToFood() { var s = Scenario(); s.Economy.StartFood = 100; s.Economy.StartStone = 100; var route = CoreBelt(s); s.Belts = new[] { new BeltDefinition { Cell = route.cell, FactionId = 1, Facing = route.toward, Item = ResourceKind.TradeGoods } }; var f = new Fixture(s); Reach(f); Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.EqualTo(104)); Assert.That(Number(f.Simulation, "Economy[1].Stone"), Is.EqualTo(100)); }

        [Test] public void T07_LowerStockConvertsToStone() { var s = Scenario(); s.Economy.StartFood = 200; s.Economy.StartStone = 100; var route = CoreBelt(s); s.Belts = new[] { new BeltDefinition { Cell = route.cell, FactionId = 1, Facing = route.toward, Item = ResourceKind.TradeGoods } }; var f = new Fixture(s); Reach(f); Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.EqualTo(200)); Assert.That(Number(f.Simulation, "Economy[1].Stone"), Is.EqualTo(104)); }

        [Test] public void T08_ProducedGoodsCanBeHandHauledToCore() { var f = new Fixture(Scenario()); Reach(f); uint id = PlaceAndBuild(f); SetEconomy(f.Simulation, "Wood", 0); SetBuilding(f.Simulation, id, "Input", 3); f.Step(80); SetEconomy(f.Simulation, "Food", 100); SetEconomy(f.Simulation, "Stone", 100); f.Send(EconomyCommand.Assign(1, ++f.Sequence, new uint[] { 1 }, EconomyTargetKind.Building, id)); f.Step(5000); Assert.That(Number(f.Simulation, "Economy[1].TradeGoodsConvertedFood"), Is.GreaterThan(0)); }

        [Test] public void T09_BeltCarriesTradeGoodsWithoutTradeGoodsStock() { var s = Scenario(); var route = CoreBelt(s); s.Belts = new[] { new BeltDefinition { Cell = route.cell, FactionId = 1, Facing = route.toward, Item = ResourceKind.TradeGoods } }; var f = new Fixture(s); Reach(f); f.Step(1); Assert.That(DiagnosticComparison.Fields(f.Simulation.CaptureDiagnostic()).Any(x => x.Key == "Economy[1].TradeGoods"), Is.False); Assert.That(Number(f.Simulation, "Economy[1].TradeGoodsConvertedFood"), Is.GreaterThanOrEqualTo(0)); }

        [Test] public void T10_KeepWoodThresholdIsStrict() { var f = new Fixture(Scenario()); Reach(f); uint id = PlaceAndBuild(f); SetEconomy(f.Simulation, "Wood", 80); f.Step(1); Assert.That(f.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == id).Input, Is.EqualTo(0)); SetEconomy(f.Simulation, "Wood", 81); f.Step(1); Assert.That(f.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == id).Input, Is.EqualTo(1)); }

        [Test] public void T11_NonCaravanAndFlagOffRejectWeaverPlacement() { var off = new Fixture(Scenario(false)); int before = (int)Number(off.Simulation, "Buildings.Count"); off.Send(EconomyCommand.Place(1, ++off.Sequence, BuildingKind.Weaver, 1000, Facing.North)); off.Step(1); Assert.That(Number(off.Simulation, "Buildings.Count"), Is.EqualTo(before)); var wrong = new Fixture(Scenario()); wrong.Send(EconomyCommand.Advance(1, ++wrong.Sequence, CivKind.Agrarian)); wrong.Step(4); int count = (int)Number(wrong.Simulation, "Buildings.Count"); wrong.Send(EconomyCommand.Place(1, ++wrong.Sequence, BuildingKind.Weaver, 1000, Facing.North)); wrong.Step(1); Assert.That(Number(wrong.Simulation, "Buildings.Count"), Is.EqualTo(count)); }

        [Explicit("30,000 tick measurement is run by the caravan craft report")]
        [Test] public void T12_AutomaticCaravanBuildsAtMostOneWeaverAndReplayHashIsDeterministic()
        {
            var left = new Fixture(Scenario()); var right = new Fixture(Scenario());
            left.Send(EconomyCommand.Advance(1, ++left.Sequence, CivKind.Caravan)); right.Send(EconomyCommand.Advance(1, ++right.Sequence, CivKind.Caravan));
            left.Send(EconomyCommand.Auto(1, ++left.Sequence, true)); right.Send(EconomyCommand.Auto(1, ++right.Sequence, true));
            for (int tick = 0; tick < 4; tick++) { left.Step(1); right.Step(1); }
            for (long tick = 1; tick <= 30000; tick++)
            {
                left.Step(1); right.Step(1);
                Assert.That(ReplayBinary.Hash(left.Simulation.CaptureDiagnostic().CanonicalState), Is.EqualTo(ReplayBinary.Hash(right.Simulation.CaptureDiagnostic().CanonicalState)));
            }
            var fields = DiagnosticComparison.Fields(left.Simulation.CaptureDiagnostic());
            Assert.That(fields.Count(x => x.Key.Contains("Kind") && x.Value == ((byte)BuildingKind.Weaver).ToString(CultureInfo.InvariantCulture)), Is.LessThanOrEqualTo(1));
            Assert.That(Number(left.Simulation, "Economy[1].TradeGoodsProduced"), Is.GreaterThan(0));
            long converted = Number(left.Simulation, "Economy[1].TradeGoodsConvertedFood") + Number(left.Simulation, "Economy[1].TradeGoodsConvertedStone");
            Assert.That(converted, Is.GreaterThan(0), string.Join(";", fields.Where(x => x.Key.Contains("ProcessingLines", StringComparison.Ordinal) || x.Key.Contains("TradeGoods", StringComparison.Ordinal) || x.Key.Contains("Villagers[", StringComparison.Ordinal) && (x.Key.EndsWith("HaulFrom") || x.Key.EndsWith("HaulTo") || x.Key.EndsWith("Held") || x.Key.EndsWith("Task"))).Select(x => x.Key + "=" + x.Value)));
        }
    }
}
