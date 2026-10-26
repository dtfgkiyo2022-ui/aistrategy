using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class OpenFarmsTests
    {
        private static ScenarioDefinition Scenario(bool open)
        {
            var scenario = MapGenerator.GenerateTerrain(1, processingChain: true);
            var e = scenario.Economy;
            e.OpenFarms = open;
            e.Forestry = true;
            e.StartFood = 50000; e.StartWood = 50000; e.StartStone = 50000; e.StartMetal = 50000;
            e.AutoVillagerTarget = 12; e.AutoInfantryQueue = 0;
            e.FarmWoodCost = 0; e.FarmWork = 1;
            e.LumberCampWoodCost = 0; e.LumberCampWork = 1;
            e.AdvanceFoodCost = e.AdvanceWoodCost = 0; e.AdvanceTicks = 1;
            e.Age2FoodCost = e.Age2WoodCost = 0; e.Age2Ticks = 1;
            return scenario;
        }

        [Test]
        public void FlagOffPreservesEveryTickHash()
        {
            var source = Scenario(false);
            source.Economy.AutoVillagerTarget = 0;
            source.Economy.AutoInfantryQueue = 0;
            var left = new Battle(source);
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(source)));
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension69RoundTripsOpenFarmRules()
        {
            var source = Scenario(true);
            source.Economy.OpenFarmsOtherCivExtraTicks = 27;
            source.Economy.OpenFarmsPrimitiveAiFarms = 3;
            source.Economy.OpenFarmsOtherCivAiFarms = 5;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 69 && x.Version == 1), Is.True);
            Assert.That(decoded.Economy.OpenFarms, Is.True);
            Assert.That(decoded.Economy.OpenFarmsOtherCivExtraTicks, Is.EqualTo(27));
            Assert.That(decoded.Economy.OpenFarmsPrimitiveAiFarms, Is.EqualTo(3));
            Assert.That(decoded.Economy.OpenFarmsOtherCivAiFarms, Is.EqualTo(5));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void PrimitiveHumanFarmProducesFoodAndIsTwentyTicksSlower()
        {
            var primitive = Scenario(true);
            var primitiveSimulation = new Battle(primitive);
            var primitiveGateway = new CommandGateway(primitiveSimulation);
            primitiveGateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            int origin = PlaceFirstFarm(primitive, primitiveSimulation, primitiveGateway, 2);
            var primitiveFarm = primitiveSimulation.Capture(1).Economy.Buildings.Single(b => b.Kind == BuildingKind.Farm);
            primitiveGateway.SubmitEconomy(EconomyCommand.Assign(1, 100, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, primitiveFarm.Id));
            Run(primitiveGateway, primitiveSimulation, 1000);
            var beforeFood = primitiveSimulation.Capture(1).Economy.Food;
            int primitiveInterval = BuildingInterval(primitiveSimulation, primitiveFarm.Id);
            Run(primitiveGateway, primitiveSimulation, primitiveInterval + 1);
            Assert.That(primitiveSimulation.Capture(1).Economy.Buildings.Single(b => b.Id == primitiveFarm.Id).Output, Is.GreaterThan(0));
            Assert.That(primitiveSimulation.Capture(1).Economy.Food, Is.GreaterThanOrEqualTo(beforeFood));

            var agrarian = Scenario(true);
            var agrarianSimulation = new Battle(agrarian);
            var agrarianGateway = new CommandGateway(agrarianSimulation);
            agrarianGateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Agrarian));
            Run(agrarianGateway, agrarianSimulation, agrarian.Economy.AdvanceTicks + 1);
            Assert.That(agrarianSimulation.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Agrarian));
            agrarianGateway.SubmitEconomy(EconomyCommand.Auto(1, 2, false));
            agrarianGateway.SubmitEconomy(EconomyCommand.Place(1, 3, BuildingKind.Farm, origin, Facing.North));
            Run(agrarianGateway, agrarianSimulation, 1);
            var agrarianFarm = agrarianSimulation.Capture(1).Economy.Buildings.Single(b => b.Kind == BuildingKind.Farm);
            int agrarianInterval = BuildingInterval(agrarianSimulation, agrarianFarm.Id);
            Assert.That(primitiveInterval, Is.EqualTo(agrarianInterval + primitive.Economy.OpenFarmsOtherCivExtraTicks),
                "primitive=" + primitiveInterval + ", agrarian=" + agrarianInterval);
        }

        [Test]
        public void OpenFarmAiBuildsPrimitiveFarmAndConnectsBelt()
        {
            var scenario = Scenario(true);
            var simulation = new Battle(scenario);
            Run(simulation, 6000);
            var economy = simulation.Capture(1).Economy;
            Assert.That(economy.Buildings.Count(b => b.Kind == BuildingKind.Farm), Is.GreaterThanOrEqualTo(1));
            Assert.That(economy.Belts.Count, Is.GreaterThan(0));
        }

        [Test]
        public void NonAgrarianFarmDoesNotSuppressForestryProcessingDecision()
        {
            var scenario = Scenario(true);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Forestry));
            Run(gateway, simulation, 800);
            var buildings = simulation.Capture(1).Economy.Buildings;
            Assert.That(buildings.Any(b => b.Kind == BuildingKind.Farm), Is.True);
            Assert.That(buildings.Any(b => b.Kind == BuildingKind.LumberCamp), Is.True);
        }

        [Test]
        public void OpenFarmRecordReplayMatchesEveryTickHash()
        {
            var scenario = Scenario(true);
            var identity = new BuildIdentity();
            using (var stream = new System.IO.MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 3000, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        private static int PlaceFirstFarm(ScenarioDefinition scenario, Battle simulation, CommandGateway gateway, ulong sequence)
        {
            int width = scenario.Map.WidthCells;
            int core = (int)(scenario.Cores[0].Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * width
                + (int)(scenario.Cores[0].Position.X.Raw / 65536 / scenario.Map.CellSizeMeters);
            int cx = core % width, cz = core / width;
            for (int r = 5; r <= 14; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x = cx + dx - 1, z = cz + dz - 1;
                        if (x < 0 || z < 0 || x + 2 > scenario.Map.WidthCells || z + 2 > scenario.Map.HeightCells) continue;
                        int origin = z * width + x;
                        int before = simulation.Capture(1).Economy.Buildings.Count;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, sequence++, BuildingKind.Farm, origin, Facing.North));
                        gateway.Step();
                        if (simulation.Capture(1).Economy.Buildings.Count > before) return origin;
                    }
            Assert.Fail("農場を置ける場所が見つからない");
            return -1;
        }

        private static void Run(CommandGateway gateway, Battle simulation, int ticks)
        {
            for (int i = 0; i < ticks && !simulation.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static void Run(Battle simulation, int ticks)
        {
            for (long tick = 1; tick <= ticks && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
        }

        private static int BuildingInterval(Battle simulation, uint buildingId)
        {
            var fields = DiagnosticComparison.Fields(simulation.CaptureDiagnostic());
            string value = fields.Single(p => p.Key == "Buildings[" + buildingId.ToString(CultureInfo.InvariantCulture) + "].Interval").Value;
            return int.Parse(value, CultureInfo.InvariantCulture);
        }
    }
}
