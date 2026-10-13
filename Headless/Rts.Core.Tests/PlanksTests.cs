using System;
using System.Collections.Generic;
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
    public sealed class PlanksTests
    {
        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(ScenarioDefinition scenario)
            {
                Scenario = scenario;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Send(EconomyCommand.Auto(1, ++Sequence, false));
                Send(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);
            internal void Step(int count)
            {
                for (int i = 0; i < count && !Simulation.Capture(1).Result.HasEnded; i++) Gateway.Step();
            }
        }

        private static ScenarioDefinition Scenario(bool planks)
        {
            var s = MapGenerator.GenerateTerrain(9417UL, false, true);
            s.Economy.Ages = true;
            s.Economy.Industry = true;
            s.Economy.ProcessingChain = true;
            s.Economy.Bridge = true;
            s.Economy.Planks = planks;
            s.Economy.StartFood = 5000;
            s.Economy.StartWood = 50000;
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
            s.Economy.BarracksWoodCost = 1;
            s.Economy.BarracksWork = 1;
            s.Economy.VillagerFoodCost = 0;
            s.Economy.SawmillWoodCost = 1;
            s.Economy.SawmillWork = 1;
            s.Economy.PlankTicks = 3;
            s.Economy.PlankWoodInput = 3;
            s.Economy.PlankWoodReserve = 0;
            s.Economy.BeltTicksPerCell = 1;
            s.Economy.BeltLimit = 4096;
            return s;
        }

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> fields, string name)
            => long.Parse(fields[name], CultureInfo.InvariantCulture);

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private static void SetEconomy(Battle simulation, uint faction, string name, int value)
        {
            var world = World(simulation);
            var economiesField = world.GetType().GetField("Economies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var economies = (Array)economiesField.GetValue(world);
            var economy = economies.GetValue((int)faction - 1);
            economy.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(economy, value);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static void SetBuilding(Battle simulation, uint buildingId, string name, int value)
        {
            var world = World(simulation);
            var buildingsField = world.GetType().GetField("Buildings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var buildings = (Array)buildingsField.GetValue(world);
            for (int i = 0; i < buildings.Length; i++)
            {
                var building = buildings.GetValue(i);
                var id = (uint)building.GetType().GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(building);
                if (id != buildingId) continue;
                building.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(building, value);
                buildings.SetValue(building, i);
                return;
            }
            Assert.Fail("建物が見つからない: " + buildingId);
        }

        private static int CellOf(ScenarioDefinition scenario, SimPoint point)
            => (int)(point.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                + (int)(point.X.Raw / 65536 / scenario.Map.CellSizeMeters);

        private static (int cell, Facing toward) BeltToCore(ScenarioDefinition scenario)
        {
            int width = scenario.Map.WidthCells;
            int core = CellOf(scenario, scenario.Cores[0].Position);
            int cx = core % width, cz = core / width;
            foreach (var away in new[] { Facing.East, Facing.North, Facing.South, Facing.West })
            {
                int dx = away == Facing.East ? 1 : away == Facing.West ? -1 : 0;
                int dz = away == Facing.North ? 1 : away == Facing.South ? -1 : 0;
                int x = cx + dx * 3, z = cz + dz * 3;
                int cell = z * width + x;
                if (x >= 0 && z >= 0 && x < width && z < scenario.Map.HeightCells
                    && !scenario.Map.BlockedCellIds.Contains(cell)
                    && scenario.ResourceNodes.All(n => CellOf(scenario, n.Position) != cell))
                    return (cell, (Facing)(((int)away + 2) % 4));
            }
            Assert.Fail("コアへ向かうベルトの場所が見つからない");
            return (0, Facing.North);
        }

        private static int[] FindBridgeCells(ScenarioDefinition scenario)
        {
            var blocked = new bool[scenario.Map.WidthCells * scenario.Map.HeightCells];
            foreach (int cell in scenario.Map.BlockedCellIds) blocked[cell] = true;
            int width = scenario.Map.WidthCells, height = scenario.Map.HeightCells;
            for (int length = 1; length <= scenario.Economy.MaxBridgeLength; length++)
                for (int z = 0; z < height; z++)
                    for (int x = 0; x < width; x++)
                        foreach (int delta in new[] { 1, width })
                        {
                            int last = z * width + x + delta * (length - 1);
                            if (last < 0 || last >= blocked.Length || delta == 1 && last / width != z || delta == width && last % width != x) continue;
                            var cells = Enumerable.Range(0, length).Select(i => z * width + x + delta * i).ToArray();
                            if (!cells.All(c => scenario.Map.Terrain[c] == (byte)TerrainKind.River && blocked[c])) continue;
                            int before = cells[0] - delta, after = cells[cells.Length - 1] + delta;
                            if (before < 0 || after < 0 || before >= blocked.Length || after >= blocked.Length || blocked[before] || blocked[after]) continue;
                            if (delta == 1 && (before / width != z || after / width != z) || delta == width && (before % width != x || after % width != x)) continue;
                            return cells;
                        }
            return null;
        }

        private static void ReachCivilisation(Fixture fixture, CivKind civ)
        {
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, civ));
            fixture.Step(3);
            Assert.That(Fields(fixture.Simulation)["Economy[1].Civ"], Is.EqualTo(((byte)civ).ToString(CultureInfo.InvariantCulture)));
        }

        private static uint Place(Fixture fixture, BuildingKind kind)
        {
            int before = (int)Number(Fields(fixture.Simulation), "Buildings.Count");
            int cells = fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, kind, cell, Facing.North));
                fixture.Step(1);
                var fields = Fields(fixture.Simulation);
                int after = (int)Number(fields, "Buildings.Count");
                if (after == before + 1 && fields["Buildings[" + after + "].Kind"] == ((byte)kind).ToString(CultureInfo.InvariantCulture)) return (uint)after;
            }
            Assert.Fail("合法な建設地点が見つからない: " + kind);
            return 0;
        }

        private static void Build(Fixture fixture, uint buildingId)
        {
            fixture.Send(EconomyCommand.Assign(1, ++fixture.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, buildingId));
            for (int i = 0; i < 1500 && Fields(fixture.Simulation)["Buildings[" + buildingId + "].Complete"] != "1"; i++) fixture.Step(1);
            Assert.That(Fields(fixture.Simulation)["Buildings[" + buildingId + "].Complete"], Is.EqualTo("1"));
        }

        [Test]
        public void PlanksExtensionRoundTripsChangedValues()
        {
            var scenario = Scenario(true);
            scenario.Economy.SawmillWoodCost = 17;
            scenario.Economy.PlankTicks = 91;
            scenario.Economy.PlankRamHpPermille = 1633;
            scenario.Economy.PlankBridgeWoodPermille = 477;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 23).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.SawmillWoodCost, Is.EqualTo(17));
            Assert.That(decoded.Economy.PlankTicks, Is.EqualTo(91));
            Assert.That(decoded.Economy.PlankRamHpPermille, Is.EqualTo(1633));
            Assert.That(decoded.Economy.PlankBridgeWoodPermille, Is.EqualTo(477));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void PlanksOffRejectsSawmillAndLeavesNoPlanksDiagnostic()
        {
            var fixture = new Fixture(Scenario(false));
            ReachCivilisation(fixture, CivKind.Bridge);
            int before = (int)Number(Fields(fixture.Simulation), "Buildings.Count");
            fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Sawmill, 0, Facing.North));
            fixture.Send(EconomyCommand.RequestLineAt(1, ++fixture.Sequence, ProcessingLineKind.Planks, 0));
            fixture.Step(1);
            var fields = Fields(fixture.Simulation);
            Assert.That(Number(fields, "Buildings.Count"), Is.EqualTo(before));
            Assert.That(fields.Keys.Any(k => k.Contains("Plank", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void SawmillConsumesThreeWoodAndProducesOnePlankAfterConfiguredTicks()
        {
            var fixture = new Fixture(Scenario(true));
            ReachCivilisation(fixture, CivKind.Bridge);
            uint sawmill = Place(fixture, BuildingKind.Sawmill);
            Build(fixture, sawmill);
            SetBuilding(fixture.Simulation, sawmill, "Input", 0);
            SetEconomy(fixture.Simulation, 1, "Wood", 0);
            long producedBefore = Number(Fields(fixture.Simulation), "Economy[1].PlanksProduced");
            fixture.Step(4);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].PlanksProduced"), Is.EqualTo(producedBefore), "木材切れで止まる");
            SetBuilding(fixture.Simulation, sawmill, "Input", 3);
            fixture.Step(4);
            var fields = Fields(fixture.Simulation);
            Assert.That(Number(fields, "Economy[1].PlanksProduced"), Is.EqualTo(producedBefore + 1), "木材を戻すと再開する");
        }

        [Test]
        public void PlanksOnACompletedBeltReachTheCoreStock()
        {
            var scenario = Scenario(true);
            scenario.Economy.BeltComponents = true;
            scenario.Economy.BeltTicksPerCell = 1;
            var route = BeltToCore(scenario);
            scenario.Belts = new[] { new BeltDefinition
            {
                Cell = route.cell, FactionId = 1, Facing = route.toward, Item = ResourceKind.Planks
            } };
            var fixture = new Fixture(scenario);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].Planks"), Is.EqualTo(1));
        }

        [Test]
        public void NonBridgeCannotBuildSawmillWhenPlanksAreOn()
        {
            var fixture = new Fixture(Scenario(true));
            ReachCivilisation(fixture, CivKind.Agrarian);
            int before = (int)Number(Fields(fixture.Simulation), "Buildings.Count");
            fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Sawmill, 0, Facing.North));
            fixture.Step(1);
            Assert.That(Number(Fields(fixture.Simulation), "Buildings.Count"), Is.EqualTo(before));
        }

        [Test]
        public void PlankedRamUsesStockShortensTrainingAndRefundsOnCancel()
        {
            var fixture = new Fixture(Scenario(true));
            ReachCivilisation(fixture, CivKind.Bridge);
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, CivKind.Bridge));
            fixture.Step(3);
            uint workshop = Place(fixture, BuildingKind.SiegeWorkshop);
            Build(fixture, workshop);
            SetEconomy(fixture.Simulation, 1, "Planks", 3);
            fixture.Send(EconomyCommand.Train(1, ++fixture.Sequence, workshop, UnitKind.Ram));
            fixture.Step(1);
            var queued = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == workshop);
            Assert.That(queued.TrainRemaining, Is.EqualTo(fixture.Scenario.Economy.RamTicks * 700 / 1000 - 1));
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].Planks"), Is.EqualTo(0));
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].PlanksConsumedRams"), Is.EqualTo(3));
            fixture.Send(EconomyCommand.CancelTrain(1, ++fixture.Sequence, workshop));
            fixture.Step(1);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].Planks"), Is.EqualTo(3));
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].PlanksConsumedRams"), Is.EqualTo(0));

            SetEconomy(fixture.Simulation, 1, "Planks", 3);
            fixture.Send(EconomyCommand.Train(1, ++fixture.Sequence, workshop, UnitKind.Ram));
            for (int i = 0; i < fixture.Scenario.Economy.RamTicks + 10; i++) fixture.Step(1);
            var trained = Fields(fixture.Simulation);
            string ramClass = ((byte)UnitKind.Ram).ToString(CultureInfo.InvariantCulture);
            string ram = trained.Keys.Where(k => k.StartsWith("Soldiers[", StringComparison.Ordinal) && k.EndsWith("].Class", StringComparison.Ordinal))
                .Where(k => trained[k] == ramClass).Select(k => k.Substring("Soldiers[".Length, k.IndexOf(']') - "Soldiers[".Length)).LastOrDefault();
            Assert.That(ram, Is.Not.Null, "板付き破城槌が完成する");
            Assert.That(Number(trained, "Soldiers[" + ram + "].Parameters.Hp"), Is.EqualTo(fixture.Scenario.Economy.RamHp * 1500 / 1000));
            Assert.That(Number(trained, "Economy[1].PlanksEnhancedRams"), Is.EqualTo(1));
        }

        [Test]
        public void RamTrainingDoesNotStopWhenPlanksAreInsufficient()
        {
            var fixture = new Fixture(Scenario(true));
            ReachCivilisation(fixture, CivKind.Bridge);
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, CivKind.Bridge));
            fixture.Step(3);
            uint workshop = Place(fixture, BuildingKind.SiegeWorkshop);
            Build(fixture, workshop);
            SetEconomy(fixture.Simulation, 1, "Planks", 2);
            fixture.Send(EconomyCommand.Train(1, ++fixture.Sequence, workshop, UnitKind.Ram));
            fixture.Step(1);
            var queued = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == workshop);
            Assert.That(queued.TrainRemaining, Is.EqualTo(fixture.Scenario.Economy.RamTicks - 1));
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].Planks"), Is.EqualTo(2));
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].PlanksConsumedRams"), Is.EqualTo(0));
        }

        [Test]
        public void PlankedBridgeUsesOnePlankPerCellAndHalfWood()
        {
            var fixture = new Fixture(Scenario(true));
            ReachCivilisation(fixture, CivKind.Bridge);
            uint camp = Place(fixture, BuildingKind.EngineerCamp);
            Build(fixture, camp);
            var cells = FindBridgeCells(fixture.Scenario);
            Assume.That(cells, Is.Not.Null);
            SetEconomy(fixture.Simulation, 1, "Wood", 100);
            SetEconomy(fixture.Simulation, 1, "Planks", cells.Length);
            fixture.Send(EconomyCommand.PlaceBridge(1, ++fixture.Sequence, cells, Facing.East));
            fixture.Step(1);
            var fields = Fields(fixture.Simulation);
            Assert.That(Number(fields, "Economy[1].Wood"), Is.EqualTo(100 - fixture.Scenario.Economy.BridgeWoodCost * fixture.Scenario.Economy.PlankBridgeWoodPermille / 1000));
            Assert.That(Number(fields, "Economy[1].Planks"), Is.EqualTo(0));
            Assert.That(Number(fields, "Economy[1].PlanksConsumedBridges"), Is.EqualTo(cells.Length));
        }

        [Test]
        public void PlanksReplayHashesMatch()
        {
            var scenario = Scenario(true);
            using var stream = new MemoryStream();
            var build = new BuildIdentity();
            ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 40, build);
            stream.Position = 0;
            var replay = ReplayRunner.Replay(stream, build);
            Assert.That(replay.FirstMismatchTick, Is.Null);
            Assert.That(replay.IsFault, Is.False);
        }
    }
}
