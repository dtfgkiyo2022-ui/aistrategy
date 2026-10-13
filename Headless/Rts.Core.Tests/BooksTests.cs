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
    public sealed class BooksTests
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

        private static ScenarioDefinition Scenario(bool books)
        {
            var s = MapGenerator.GenerateTerrain(7711UL, true, true);
            s.Economy.Ages = true; s.Economy.Industry = true; s.Economy.ProcessingChain = true;
            s.Economy.Academy = true; s.Economy.Books = books;
            s.Economy.StartFood = 5000; s.Economy.StartWood = 50000; s.Economy.StartStone = 5000;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0; s.Economy.Age3WoodCost = 0; s.Economy.Age3Ticks = 1;
            s.Economy.BarracksWoodCost = 1; s.Economy.BarracksWork = 1; s.Economy.VillagerFoodCost = 0;
            s.Economy.ScriptoriumWoodCost = 1; s.Economy.ScriptoriumWork = 1; s.Economy.ScriptoriumHp = 10;
            s.Economy.BookTicks = 3; s.Economy.BookGoldInput = 1; s.Economy.BookWoodInput = 2; s.Economy.BookWoodReserve = 0;
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

        private static void Reach(Fixture f, CivKind civ)
        {
            f.Send(EconomyCommand.Advance(1, ++f.Sequence, civ));
            f.Step(4);
            Assert.That(f.Simulation.Capture(1).Economy.Civ, Is.EqualTo(civ));
        }

        private static uint Place(Fixture f, BuildingKind kind)
        {
            int before = (int)Number(f.Simulation, "Buildings.Count");
            int cells = f.Scenario.Map.WidthCells * f.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                f.Send(EconomyCommand.Place(1, ++f.Sequence, kind, cell, Facing.North));
                f.Step(1);
                var fields = Fields(f.Simulation);
                int after = int.Parse(fields["Buildings.Count"], CultureInfo.InvariantCulture);
                if (after == before + 1 && fields["Buildings[" + after + "].Kind"] == ((byte)kind).ToString(CultureInfo.InvariantCulture)) return (uint)after;
            }
            Assert.Fail("建設地点が見つからない: " + kind);
            return 0;
        }

        private static uint PlaceAndBuild(Fixture f, BuildingKind kind)
        {
            uint id = Place(f, kind);
            f.Send(EconomyCommand.Assign(1, ++f.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            for (int i = 0; i < 1500 && !f.Simulation.Capture(1).Economy.Buildings.First(b => b.Id == id).Complete; i++) f.Step(1);
            Assert.That(f.Simulation.Capture(1).Economy.Buildings.First(b => b.Id == id).Complete, Is.True);
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
        public void BooksOffKeepsOldBytesAndDiagnostic()
        {
            var old = Scenario(false);
            var off = Scenario(false);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
            var fields = Fields(new Battle(off));
            Assert.That(fields.Keys.Any(k => k.Contains("Books", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void BooksExtensionUsesId24AndRoundTripsChangedValues()
        {
            var scenario = Scenario(true);
            scenario.Economy.ScriptoriumWoodCost = 17;
            scenario.Economy.BookTicks = 91;
            scenario.Economy.BookResearchPermille = 477;
            scenario.Economy.BookResearchCostPermille = 733;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 24).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.ScriptoriumWoodCost, Is.EqualTo(17));
            Assert.That(decoded.Economy.BookTicks, Is.EqualTo(91));
            Assert.That(decoded.Economy.BookResearchPermille, Is.EqualTo(477));
            Assert.That(decoded.Economy.BookResearchCostPermille, Is.EqualTo(733));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void ScriptoriumStopsForMissingGoldAndResumesWithGold()
        {
            var f = new Fixture(Scenario(true));
            Reach(f, CivKind.Academy);
            uint id = PlaceAndBuild(f, BuildingKind.Scriptorium);
            SetEconomy(f.Simulation, "Gold", 0); SetEconomy(f.Simulation, "Wood", 0);
            long produced = Number(f.Simulation, "Economy[1].BooksProduced");
            f.Step(5);
            Assert.That(Number(f.Simulation, "Economy[1].BooksProduced"), Is.EqualTo(produced));
            SetEconomy(f.Simulation, "Gold", 2); SetEconomy(f.Simulation, "Wood", 10);
            f.Step(5);
            Assert.That(Number(f.Simulation, "Economy[1].BooksProduced"), Is.GreaterThan(produced));
        }

        [Test]
        public void BooksOnCompletedBeltReachCoreStock()
        {
            var scenario = Scenario(true);
            var route = CoreBelt(scenario);
            scenario.Belts = new[] { new BeltDefinition { Cell = route.cell, FactionId = 1, Facing = route.toward, Item = ResourceKind.Books } };
            var f = new Fixture(scenario);
            Assert.That(Number(f.Simulation, "Economy[1].Books"), Is.EqualTo(1));
        }

        [Test]
        public void NonAcademyCannotBuildScriptorium()
        {
            var f = new Fixture(Scenario(true));
            Reach(f, CivKind.Agrarian);
            int before = (int)Number(f.Simulation, "Buildings.Count");
            f.Send(EconomyCommand.Place(1, ++f.Sequence, BuildingKind.Scriptorium, 0, Facing.North));
            f.Step(1);
            Assert.That(Number(f.Simulation, "Buildings.Count"), Is.EqualTo(before));
        }

        [Test]
        public void ThreeBooksDiscountAcademyResearchTimeAndCosts()
        {
            var f = new Fixture(Scenario(true));
            Reach(f, CivKind.Academy);
            uint academy = PlaceAndBuild(f, BuildingKind.Academy);
            SetEconomy(f.Simulation, "Food", 1000); SetEconomy(f.Simulation, "Wood", 1000); SetEconomy(f.Simulation, "Gold", 1000); SetEconomy(f.Simulation, "Books", 3);
            f.Send(EconomyCommand.Research(1, ++f.Sequence, academy, TechKind.Tools));
            f.Step(1);
            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.EqualTo(965));
            Assert.That(Number(f.Simulation, "Economy[1].Wood"), Is.EqualTo(982));
            Assert.That(Number(f.Simulation, "Economy[1].Gold"), Is.EqualTo(950));
            Assert.That(Number(f.Simulation, "Economy[1].Books"), Is.EqualTo(0));
            Assert.That(Number(f.Simulation, "Economy[1].BooksUsedResearch"), Is.EqualTo(1));
            Assert.That(Number(f.Simulation, "Buildings[" + academy + "].TrainRemaining"), Is.LessThanOrEqualTo(100));
        }

        [Test]
        public void TwoBooksLeaveAcademyResearchUnchanged()
        {
            var f = new Fixture(Scenario(true));
            Reach(f, CivKind.Academy);
            uint academy = PlaceAndBuild(f, BuildingKind.Academy);
            SetEconomy(f.Simulation, "Food", 1000); SetEconomy(f.Simulation, "Wood", 1000); SetEconomy(f.Simulation, "Gold", 1000); SetEconomy(f.Simulation, "Books", 2);
            f.Send(EconomyCommand.Research(1, ++f.Sequence, academy, TechKind.Tools));
            f.Step(1);
            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.EqualTo(950));
            Assert.That(Number(f.Simulation, "Economy[1].Wood"), Is.EqualTo(975));
            Assert.That(Number(f.Simulation, "Economy[1].Books"), Is.EqualTo(2));
            Assert.That(Number(f.Simulation, "Economy[1].BooksUsedResearch"), Is.EqualTo(0));
            Assert.That(Number(f.Simulation, "Buildings[" + academy + "].TrainRemaining"), Is.GreaterThan(100));
        }

        [Test]
        public void AutonomousAcademyResearchDoesNotWaitForScriptorium()
        {
            var f = new Fixture(Scenario(true), auto: true);
            Reach(f, CivKind.Academy);
            uint academy = PlaceAndBuild(f, BuildingKind.Academy);
            SetBuilding(f.Simulation, academy, "Held", false);
            SetEconomy(f.Simulation, "Food", 10000); SetEconomy(f.Simulation, "Wood", 10000);
            SetEconomy(f.Simulation, "Gold", 10000); SetEconomy(f.Simulation, "Books", 3);

            f.Step(25);

            Assert.That(Number(f.Simulation, "Buildings.Count"), Is.GreaterThan(academy), "the book line is started");
            var fields = Fields(f.Simulation);
            Assert.That(Number(f.Simulation, "Buildings[" + academy + "].Researching"), Is.Not.EqualTo(0),
                "DecideResearch starts Academy research while the Scriptorium is pending; civ=" + fields["Economy[1].Civ"]
                + " age=" + fields["Economy[1].Age"] + " autoOff=" + fields["Economy[1].AutoOff"]
                + " complete=" + fields["Buildings[" + academy + "].Complete"] + " held=" + fields["Buildings[" + academy + "].Held"]
                + " gold=" + fields["Economy[1].Gold"] + " food=" + fields["Economy[1].Food"] + " wood=" + fields["Economy[1].Wood"]);
            Assert.That(Number(f.Simulation, "Economy[1].BooksUsedResearch"), Is.EqualTo(1));
        }

        [Test]
        public void AcademyBooksAlsoApplyToHumanBlacksmithResearch()
        {
            var f = new Fixture(Scenario(true));
            Reach(f, CivKind.Academy);
            uint smith = PlaceAndBuild(f, BuildingKind.Blacksmith);
            SetEconomy(f.Simulation, "Food", 1000); SetEconomy(f.Simulation, "Wood", 1000);
            SetEconomy(f.Simulation, "Gold", 1000); SetEconomy(f.Simulation, "Books", 3);
            int normalFood = f.Scenario.Economy.TechFood[(int)TechKind.Weapons - 1];

            f.Send(EconomyCommand.Research(1, ++f.Sequence, smith, TechKind.Weapons));
            f.Step(1);

            Assert.That(Number(f.Simulation, "Economy[1].Food"), Is.EqualTo(1000 - (normalFood * f.Scenario.Economy.BookResearchCostPermille + 999) / 1000));
            Assert.That(Number(f.Simulation, "Economy[1].Books"), Is.EqualTo(0));
            Assert.That(Number(f.Simulation, "Economy[1].BooksUsedResearch"), Is.EqualTo(1));
            Assert.That(Number(f.Simulation, "Buildings[" + smith + "].TrainRemaining"), Is.LessThanOrEqualTo(
                Math.Max(1, f.Scenario.Economy.TechTicks[(int)TechKind.Weapons - 1] * f.Scenario.Economy.BookResearchPermille / 1000)));
        }

        [Test]
        public void BooksResearchReplayHashesMatch()
        {
            var left = new Fixture(Scenario(true)); var right = new Fixture(Scenario(true));
            for (int tick = 1; tick <= 120; tick++)
            {
                left.Gateway.Step(); right.Gateway.Step();
                Assert.That(ReplayBinary.Hash(left.Simulation.CaptureDiagnostic().CanonicalState), Is.EqualTo(ReplayBinary.Hash(right.Simulation.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
