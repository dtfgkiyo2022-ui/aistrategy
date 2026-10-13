using System;
using System.Collections.Generic;
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
    public sealed class PreserveTests
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

        private static ScenarioDefinition Scenario(bool preserves)
        {
            var s = MapGenerator.GenerateTerrain(9017UL, true, true);
            s.Economy.Ages = true; s.Economy.Industry = true; s.Economy.ProcessingChain = true;
            s.Economy.FishingCiv = true; s.Economy.FishingEnabled = true; s.Economy.Preserves = preserves;
            s.Economy.StartFood = 100; s.Economy.StartWood = 5000;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0; s.Economy.Age3WoodCost = 0; s.Economy.Age3Ticks = 1;
            s.Economy.BarracksWoodCost = 1; s.Economy.BarracksWork = 1; s.Economy.VillagerFoodCost = 0;
            s.Economy.DryingRackWoodCost = 1; s.Economy.DryingRackWork = 1; s.Economy.DryingRackHp = 10;
            s.Economy.PreserveTicks = 3; s.Economy.PreserveFoodInput = 2; s.Economy.PreserveFoodValue = 3;
            s.Economy.PreserveKeepFood = 60; s.Economy.PreserveMaxRacks = 2; s.Economy.PreserveWoodReserve = 0;
            s.Economy.BeltTicksPerCell = 1; s.Economy.BeltLimit = 4096;
            s.Cores[0].Hp = s.Cores[1].Hp = 1000000;
            return s;
        }

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Battle simulation, string name)
            => long.Parse(Fields(simulation)[name], CultureInfo.InvariantCulture);

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private static void SetEconomy(Battle simulation, string name, int value)
        {
            var world = World(simulation);
            var economies = (Array)world.GetType().GetField("Economies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
            var economy = economies.GetValue(0);
            economy.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(economy, value);
            economies.SetValue(economy, 0);
        }

        private static void SetBuilding(Battle simulation, uint id, string name, object value)
        {
            var world = World(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
            var building = buildings.GetValue((int)id - 1);
            building.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(building, value);
            buildings.SetValue(building, (int)id - 1);
        }

        private static void ReachFishing(Fixture f)
        {
            f.Send(EconomyCommand.Advance(1, ++f.Sequence, CivKind.Fishing));
            f.Step(4);
            Assert.That(f.Simulation.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Fishing));
        }

        private static uint PlaceAndBuild(Fixture f)
        {
            int before = (int)Number(f.Simulation, "Buildings.Count");
            int cells = f.Scenario.Map.WidthCells * f.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                f.Send(EconomyCommand.Place(1, ++f.Sequence, BuildingKind.DryingRack, cell, Facing.North));
                f.Step(1);
                int after = (int)Number(f.Simulation, "Buildings.Count");
                if (after == before + 1)
                {
                    uint id = (uint)after;
                    f.Send(EconomyCommand.Assign(1, ++f.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
                    for (int i = 0; i < 1500 && Fields(f.Simulation)["Buildings[" + id + "].Complete"] != "1"; i++) f.Step(1);
                    Assert.That(Fields(f.Simulation)["Buildings[" + id + "].Complete"], Is.EqualTo("1"));
                    return id;
                }
            }
            Assert.Fail("干し場を配置できるセルがありません。");
            return 0;
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
                if (x >= 0 && z >= 0 && x < width && z < scenario.Map.HeightCells && !scenario.Map.BlockedCellIds.Contains(cell))
                    return (cell, (Facing)(((int)away + 2) % 4));
            }
            Assert.Fail("コアへ向かうベルトのセルがありません。");
            return (0, Facing.North);
        }

        [Test]
        public void PreservesOffKeepsBytesAndDiagnosticUnchanged()
        {
            var left = Scenario(false); var right = Scenario(false);
            Assert.That(ScenarioBinary.Encode(left), Is.EqualTo(ScenarioBinary.Encode(right)));
            var fields = Fields(new Battle(left));
            Assert.That(fields.Keys.Any(k => k.Contains("Preserve", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void PreservesExtensionUsesId25AndRoundTripsChangedValues()
        {
            var scenario = Scenario(true);
            scenario.Economy.DryingRackWoodCost = 17; scenario.Economy.PreserveTicks = 91; scenario.Economy.PreserveFoodValue = 7;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 25).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.DryingRackWoodCost, Is.EqualTo(17));
            Assert.That(decoded.Economy.PreserveTicks, Is.EqualTo(91));
            Assert.That(decoded.Economy.PreserveFoodValue, Is.EqualTo(7));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void DryingRackConsumesTwoFoodAndProducesThreeFoodValue()
        {
            var f = new Fixture(Scenario(true));
            ReachFishing(f);
            uint rack = PlaceAndBuild(f);
            long beforeFood = Number(f.Simulation, "Economy[1].Food");
            f.Step(8);
            Assert.That(Number(f.Simulation, "Economy[1].PreservesProduced"), Is.GreaterThan(0));
            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.LessThan(beforeFood));
            SetBuilding(f.Simulation, rack, "Output", 1);
            f.Send(EconomyCommand.Assign(1, ++f.Sequence, new uint[] { 1 }, EconomyTargetKind.Building, rack));
            f.Step(5000);
            Assert.That(Number(f.Simulation, "Economy[1].PreservesConvertedFood"), Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void FoodKeepFloorStopsCorePullAndPreservesResumeAfterFoodReturns()
        {
            var f = new Fixture(Scenario(true));
            ReachFishing(f);
            uint rack = PlaceAndBuild(f);
            SetEconomy(f.Simulation, "Food", 60); SetBuilding(f.Simulation, rack, "Input", 0); SetBuilding(f.Simulation, rack, "Output", 0);
            long produced = Number(f.Simulation, "Economy[1].PreservesProduced");
            f.Step(10);
            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.EqualTo(60));
            Assert.That(Number(f.Simulation, "Economy[1].PreservesProduced"), Is.EqualTo(produced));
            SetEconomy(f.Simulation, "Food", 100);
            f.Step(10);
            Assert.That(Number(f.Simulation, "Economy[1].PreservesProduced"), Is.GreaterThan(produced));
        }

        [Test]
        public void PreservesReachCoreFromBeltAndNonFishingCannotBuildRack()
        {
            var scenario = Scenario(true); var route = CoreBelt(scenario);
            scenario.Belts = new[] { new BeltDefinition { Cell = route.cell, FactionId = 1, Facing = route.toward, Item = ResourceKind.Preserves } };
            var f = new Fixture(scenario);
            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.GreaterThanOrEqualTo(103));

            var other = new Fixture(Scenario(true));
            other.Send(EconomyCommand.Advance(1, ++other.Sequence, CivKind.Agrarian)); other.Step(4);
            int before = (int)Number(other.Simulation, "Buildings.Count");
            other.Send(EconomyCommand.Place(1, ++other.Sequence, BuildingKind.DryingRack, 0, Facing.North)); other.Step(1);
            Assert.That(Number(other.Simulation, "Buildings.Count"), Is.EqualTo(before));
        }

        [Test]
        public void FishingAutoEconomyUsesAtMostTwoRacksAndIncreasesFood()
        {
            var f = new Fixture(Scenario(true), auto: true);
            ReachFishing(f);
            long startFood = Number(f.Simulation, "Economy[1].Food");
            f.Step(6000);
            var fields = Fields(f.Simulation);
            int racks = fields.Where(p => p.Key.EndsWith(".Kind", StringComparison.Ordinal) && p.Value == ((byte)BuildingKind.DryingRack).ToString(CultureInfo.InvariantCulture)).Count();
            Assert.That(racks, Is.LessThanOrEqualTo(2));
            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.GreaterThan(startFood));
        }

        [Test]
        public void PreservesReplayHashesMatch()
        {
            var left = new Fixture(Scenario(true)); var right = new Fixture(Scenario(true));
            for (int tick = 1; tick <= 120; tick++)
            {
                left.Gateway.Step(); right.Gateway.Step();
                Assert.That(ReplayBinary.Hash(left.Simulation.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.Simulation.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
