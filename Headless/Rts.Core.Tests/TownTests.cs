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
    public sealed class TownTests
    {
        [Test]
        public void TownsOffKeepsBytesAndHashesUnchanged()
        {
            var leftScenario = MapGenerator.GenerateTerrain(1);
            var rightScenario = MapGenerator.GenerateTerrain(1);
            rightScenario.Economy.TownSizeCells = 7;
            rightScenario.Economy.TownWoodCost = 9999;
            rightScenario.Economy.TownStoneCost = 8888;
            rightScenario.Economy.TownWork = 7777;
            rightScenario.Economy.TownHp = 6666;
            rightScenario.Economy.TownMaxBuildings = 1;
            rightScenario.Economy.Towns = false;
            Assert.That(ScenarioBinary.Encode(leftScenario), Is.EqualTo(ScenarioBinary.Encode(rightScenario)));
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void TownPlacementUsesTheProvisionalCostDistanceAndFactionLimit()
        {
            var scenario = AgeTwoTownScenario();
            scenario.Economy.TownWork = 1;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.StartStone = 5000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, scenario.Economy.Age2Ticks + 2);

            int woodBefore = sim.Capture(1).Economy.Wood, stoneBefore = sim.Capture(1).Economy.Stone;
            uint first = PlaceTownNearResource(scenario, sim, gateway, ref sequence);
            Assert.That(first, Is.Not.EqualTo(0u));
            Assert.That(sim.Capture(1).Economy.Wood, Is.EqualTo(woodBefore - scenario.Economy.TownWoodCost));
            Assert.That(sim.Capture(1).Economy.Stone, Is.EqualTo(stoneBefore - scenario.Economy.TownStoneCost));

            for (int i = 0; i < 2; i++) Assert.That(PlaceTownNearResource(scenario, sim, gateway, ref sequence), Is.Not.EqualTo(0u));
            Assert.That(sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Town), Is.EqualTo(3));
            Assert.That(PlaceTownNearResource(scenario, sim, gateway, ref sequence), Is.EqualTo(0u));

            var town = sim.Capture(1).Economy.Buildings.First(b => b.Id == first);
            Assert.That(town.Hp, Is.EqualTo(scenario.Economy.TownHp));
            Assert.That(town.Center.X.Raw, Is.GreaterThan(0));
        }

        [Test]
        public void VillagersPreferANearerCompletedTownAsTheDropOff()
        {
            long without = WoodFromFarNode(false), with = WoodFromFarNode(true);
            Assert.That(with, Is.GreaterThan(without), "a nearby town must shorten the return trip");
        }

        [Test]
        public void AutomaticEconomyBuildsATownNearARemoteClusterOnTheLargeMap()
        {
            var scenario = MapGenerator.GenerateLarge(3);
            scenario.Map.Terrain = new byte[scenario.Map.WidthCells * scenario.Map.HeightCells];
            scenario.Map.BlockedCellIds = Array.Empty<int>();
            scenario.Economy.Towns = true;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.StartStone = 5000;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.Age2Ticks = 1;
            scenario.Economy.AdvanceFoodCost = scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.Age2FoodCost = scenario.Economy.Age2WoodCost = 0;
            scenario.Economy.TownWork = 1;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.BarracksWoodCost = scenario.Economy.HouseWoodCost = scenario.Economy.DropSiteWoodCost = 100000;
            scenario.Economy.TowerWoodCost = scenario.Economy.FarmWoodCost = scenario.Economy.BlacksmithWoodCost = 100000;
            scenario.Economy.MarketWoodCost = scenario.Economy.WorkshopWoodCost = scenario.Economy.RangeWoodCost = 100000;
            scenario.Economy.StableWoodCost = 100000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 5);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 10000);

            var town = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Town && b.Complete);
            Assert.That(town.Kind, Is.EqualTo(BuildingKind.Town));
            Assert.That(scenario.ResourceNodes.Any(node => DistanceSquared(node.Position, town.Center) <= 24 * 24), Is.True);
            Assert.That(DistanceSquared(scenario.Cores[0].Position, town.Center), Is.GreaterThanOrEqualTo(60 * 60));
        }

        [Test]
        public void TownExtensionIdElevenRoundTripsItsRules()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Towns = true;
            scenario.Economy.TownSizeCells = 4;
            scenario.Economy.TownWoodCost = 301;
            scenario.Economy.TownStoneCost = 202;
            scenario.Economy.TownWork = 803;
            scenario.Economy.TownHp = 2004;
            scenario.Economy.TownMaxBuildings = 3;
            scenario.Economy.TownCoreDistance = 60;
            scenario.Economy.TownResourceReach = 16;
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(extension => extension.Id == 11).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.Towns, Is.True);
            Assert.That((decoded.Economy.TownSizeCells, decoded.Economy.TownWoodCost, decoded.Economy.TownStoneCost,
                decoded.Economy.TownWork, decoded.Economy.TownHp, decoded.Economy.TownMaxBuildings),
                Is.EqualTo((4, 301, 202, 803, 2004, 3)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void TownCanBePlacedOnlyAfterAgeTwoAndTrainsVillagersWithItsOwnQueue()
        {
            var scenario = AgeTwoTownScenario();
            scenario.Economy.TownWork = 1;
            scenario.Economy.VillagerTrainTicks = 5;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(building => building.Kind == BuildingKind.Town), Is.False);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, scenario.Economy.Age2Ticks + 2);
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(2));

            uint town = PlaceTownNearResource(scenario, sim, gateway, ref sequence);
            Assert.That(town, Is.Not.EqualTo(0u));
            Steps(gateway, sim, 10000);
            var completed = sim.Capture(1).Economy.Buildings.Single(building => building.Id == town);
            Assert.That(completed.Complete, Is.True);
            int villagersBefore = sim.Capture(1).Economy.Villagers.Count(v => v.IsOwn);
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++sequence, town, UnitKind.Villager));
            Steps(gateway, sim, scenario.Economy.VillagerTrainTicks + 2);
            Assert.That(sim.Capture(1).Economy.Villagers.Count(v => v.IsOwn), Is.EqualTo(villagersBefore + 1));
        }

        [Test]
        public void TownsAreDeterministicForARecordedTwentyThousandTickRun()
        {
            var scenario = AgeTwoTownScenario();
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.StartStone = 5000;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.Age2FoodCost = 0;
            scenario.Economy.Age2WoodCost = 0;
            var inputs = PolicyPresets.RecordedInputs(scenario, "maintain", "maintain", 20000);
            var identity = new BuildIdentity();
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, inputs, 20000, identity);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, identity);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        private static ScenarioDefinition AgeTwoTownScenario()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Towns = true;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.StartStone = 5000;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.Age2FoodCost = 0;
            scenario.Economy.Age2WoodCost = 0;
            scenario.Economy.Age2Ticks = 1;
            scenario.Economy.PopulationCap = 200;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000;
            return scenario;
        }

        private static uint PlaceTownNearResource(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            var core = scenario.Cores[0].Position;
            var node = scenario.ResourceNodes.Where(resource => resource.Kind == ResourceKind.Wood)
                .OrderByDescending(resource => DistanceSquared(resource.Position, core)).First();
            int width = scenario.Map.WidthCells;
            int cell = Cell(scenario, node.Position);
            int size = scenario.Economy.TownSizeCells;
            int before = sim.Capture(1).Economy.Buildings.Count;
            for (int radius = 2; radius <= 18; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                        int x = cell % width + dx, z = cell / width + dz;
                        if (x < 0 || z < 0 || x + size > scenario.Map.WidthCells || z + size > scenario.Map.HeightCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Town, z * width + x, Facing.North));
                        Steps(gateway, sim, 1);
                        var building = sim.Capture(1).Economy.Buildings.Skip(before).FirstOrDefault(value => value.Kind == BuildingKind.Town);
                        if (building.Id != 0) return building.Id;
                    }
            return 0;
        }

        private static long WoodFromFarNode(bool towns)
        {
            var scenario = AgeTwoTownScenario();
            scenario.Economy.Towns = towns;
            scenario.Economy.TownWork = 1;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, scenario.Economy.Age2Ticks + 2);
            if (towns) Assert.That(PlaceTownNearResource(scenario, sim, gateway, ref sequence), Is.Not.EqualTo(0u));
            if (towns) Steps(gateway, sim, 10000);
            var core = scenario.Cores[0].Position;
            var node = scenario.ResourceNodes.Where(resource => resource.Kind == ResourceKind.Wood)
                .OrderByDescending(resource => DistanceSquared(resource.Position, core)).First();
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.ResourceNode, node.Id));
            Steps(gateway, sim, 1);
            int before = sim.Capture(1).Economy.Wood;
            Steps(gateway, sim, 6000);
            int after = sim.Capture(1).Economy.Wood;
            return after - before;
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static int Cell(ScenarioDefinition scenario, SimPoint point)
            => (int)(point.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                + (int)(point.X.Raw / 65536 / scenario.Map.CellSizeMeters);

        private static long DistanceSquared(SimPoint left, SimPoint right)
        {
            long dx = (left.X.Raw - right.X.Raw) / 65536;
            long dz = (left.Z.Raw - right.Z.Raw) / 65536;
            return dx * dx + dz * dz;
        }
    }
}
