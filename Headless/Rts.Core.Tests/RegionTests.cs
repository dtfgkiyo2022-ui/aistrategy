using System;
using System.Collections.Generic;
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
    public sealed class RegionTests
    {
        [Test]
        public void FlagOffKeepsBytesAndFramesUnchanged()
        {
            var baseline = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.Regions = false;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(baseline)));
            Assert.That(off.Extensions.Any(e => e.Id == 13), Is.False);
            var frame = new Battle(off).Capture(1);
            Assert.That(frame.Regions, Is.Empty);
            Assert.That(frame.CellRegions, Is.Empty);
        }

        [Test]
        public void RegionsUseNearestCenterAndSmallerIdOnTies()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Regions = true;
            var frame = new Battle(scenario).Capture(1);
            var centers = frame.Regions.Where(r => r.CenterKind != RegionCenterKind.None).ToArray();
            bool foundTie = false;
            for (int cell = 0; cell < frame.CellRegions.Count; cell++)
            {
                var point = CellCenter(scenario, cell);
                long bestDistance = long.MaxValue;
                uint expected = 0;
                foreach (var center in centers)
                {
                    long distance = DistanceSquared(point, center.Center);
                    if (distance == bestDistance) foundTie = true;
                    if (distance < bestDistance || distance == bestDistance && center.Id < expected)
                    {
                        bestDistance = distance;
                        expected = center.Id;
                    }
                }
                Assert.That(frame.CellRegions[cell], Is.EqualTo(expected), "cell " + cell);
            }
            Assert.That(foundTie, Is.True, "the generated grid must exercise the equal-distance tie rule");
        }

        [Test]
        public void CompletedTownAddsARegionAndDestroyedTownKeepsItsIdAndCellTable()
        {
            var scenario = TownRegionScenario();
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, scenario.Economy.Age2Ticks + 2);

            var before = sim.Capture(1);
            uint townId = PlaceTown(scenario, sim, gateway, ref sequence);
            Assert.That(townId, Is.Not.EqualTo(0u));
            Steps(gateway, sim, 10000);
            var completed = sim.Capture(1);
            Assert.That(completed.Economy.Buildings.Single(b => b.Id == townId).Complete, Is.True);
            Assert.That(completed.Regions.Count, Is.EqualTo(before.Regions.Count + 1));
            var townRegion = completed.Regions.Single(r => r.CenterKind == RegionCenterKind.Town && r.CenterId == townId);
            var cellsBeforeDestroy = completed.CellRegions.ToArray();

            gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, ++sequence, townId));
            gateway.Step();
            var destroyed = sim.Capture(1);
            var removed = destroyed.Regions.Single(r => r.Id == townRegion.Id);
            Assert.That(removed.CenterKind, Is.EqualTo(RegionCenterKind.None));
            Assert.That(removed.CenterId, Is.EqualTo(0u));
            Assert.That(destroyed.CellRegions, Is.EqualTo(cellsBeforeDestroy));
            Assert.That(destroyed.Regions.Select(r => r.Id), Is.EqualTo(completed.Regions.Select(r => r.Id)));
        }

        [Test]
        public void HumanRegionStopsNewEconomyWorkButKeepsExistingWorkAndReturnsHeldObjectsToAi()
        {
            var scenario = MapGenerator.GenerateTerrain(1, true, true);
            scenario.Economy.Regions = true;
            scenario.Economy.Industry = true;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.AutoInfantryQueue = 0;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            var initial = sim.Capture(1);
            int initialBuildings = initial.Economy.Buildings.Count;
            var initialPositions = initial.Economy.Villagers.Where(v => v.IsOwn).ToDictionary(v => v.Id, v => v.Position);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.SetRegionControl(1, ++sequence, 1, RegionControl.Human));
            Steps(gateway, sim, 120);
            var held = sim.Capture(1);
            Assert.That(held.Economy.Buildings.Count, Is.EqualTo(initialBuildings));
            foreach (var villager in held.Economy.Villagers.Where(v => v.IsOwn && initialPositions.ContainsKey(v.Id)))
            {
                Assert.That(villager.Position, Is.EqualTo(initialPositions[villager.Id]), "villager " + villager.Id);
                Assert.That(villager.Activity, Is.EqualTo(VillagerActivity.Idle), "villager " + villager.Id);
            }

            var node = scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Wood);
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { 1u }, EconomyTargetKind.ResourceNode, node.Id));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.Villagers.Single(v => v.Id == 1).PlayerHeld, Is.True);

            gateway.SubmitEconomy(EconomyCommand.SetRegionControl(1, ++sequence, 1, RegionControl.Ai));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.Villagers.Single(v => v.Id == 1).PlayerHeld, Is.False);
            Steps(gateway, sim, 120);
            Assert.That(sim.Capture(1).Economy.Villagers.Single(v => v.Id == 1).Activity,
                Is.Not.EqualTo(VillagerActivity.Idle), "the automatic economy resumes after returning the region");
        }

        [Test]
        public void HumanRegionSoldiersStayStillButStillAttackAnEnemyInRange()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Regions = true;
            var own = scenario.Soldiers.First(s => s.FactionId == 1);
            var enemy = scenario.Soldiers.First(s => s.FactionId == 2);
            var position = scenario.Cores[0].Position;
            own.Position = position;
            enemy.Position = new SimPoint(Fix64.FromRaw(position.X.Raw + Fix64.FromInt(1).Raw), position.Z);
            own.Hp = enemy.Hp = 100;
            scenario.Soldiers[own.Id - 1] = own;
            scenario.Soldiers[enemy.Id - 1] = enemy;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.SetRegionControl(1, 1, 1, RegionControl.Human));
            gateway.Step();
            var after = sim.Capture(1);
            var ownAfter = after.Units.Single(u => u.IsOwn && u.Id == own.Id);
            var enemyAfter = sim.Capture(2).Units.Single(u => u.IsOwn && u.Id == enemy.Id);
            Assert.That(ownAfter.Position, Is.EqualTo(position));
            Assert.That(enemyAfter.Hp, Is.LessThan(enemy.Hp));
        }

        [Test]
        public void HumanRegionLetsAnExistingConstructionFinish()
        {
            var scenario = MapGenerator.GenerateTerrain(1, true);
            scenario.Economy.Regions = true;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.BarracksWoodCost = 1;
            scenario.Economy.BarracksWork = 10;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            uint buildingId = PlaceBarracks(scenario, sim, gateway, ref sequence);
            Assert.That(buildingId, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { 1u }, EconomyTargetKind.Building, buildingId));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.Buildings.Single(b => b.Id == buildingId).Complete, Is.False);

            gateway.SubmitEconomy(EconomyCommand.SetRegionControl(1, ++sequence, 1, RegionControl.Human));
            gateway.Step();
            Steps(gateway, sim, 200);
            var finished = sim.Capture(1).Economy.Buildings.Single(b => b.Id == buildingId);
            Assert.That(finished.Complete, Is.True, "progress " + finished.Progress + "/" + finished.Work);
        }

        [Test]
        public void RegionalPolicyIsAppliedToItsRegionOnly()
        {
            var scenario = MapGenerator.GenerateTerrain(2);
            scenario.Economy.Regions = true;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            var frame = sim.Capture(1);
            uint first = RegionAt(scenario, frame, frame.Observation.OwnArmies[0].Position);
            uint second = frame.Regions.First(r => r.Id != first).Id;
            Assume.That(first, Is.Not.EqualTo(second));
            gateway.Submit(new UserPolicyIntent(0, new ScopeKey(1, ScopeKind.Region, first), PolicyKind.Defend,
                new PolicyGoal(GoalKind.Core, 1, default),
                50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            Steps(gateway, sim, 50);
            var command = sim.Capture(1).Commands.Single(c => c.Kind == PolicyKind.Defend);
            Assert.That(command.Target, Is.EqualTo(new ScopeKey(1, ScopeKind.Region, first)));
            Assert.That(command.Status, Is.EqualTo(CommandStatus.Executing));
            Assert.That(sim.Capture(1).Regions.Single(r => r.Id == first).Policy, Is.EqualTo(PolicyKind.Defend));
            Assert.That(sim.Capture(1).Regions.Single(r => r.Id == second).Policy, Is.Not.EqualTo(PolicyKind.Defend));
        }

        [Test]
        public void RegionalCommandsReplayIdenticallyForTwentyThousandTicks()
        {
            var scenario = MapGenerator.GenerateTerrain(3, true, true);
            scenario.Economy.Regions = true;
            scenario.Economy.Industry = true;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.SetRegionControl(1, 1, 1, RegionControl.Human));
            gateway.SubmitEconomy(EconomyCommand.SetRegionPolicy(1, 2, 2, EconomyPolicy.Military));
            for (int tick = 0; tick < 20000 && !sim.Capture(1).Result.HasEnded; tick++) gateway.Step();
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, identity);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        [Test]
        public void ExtensionRoundTripsAndBuildsDeterministicCellMap()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Regions = true;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Regions, Is.True);
            Assert.That(decoded.Extensions.Single(e => e.Id == 13).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var frame = new Battle(decoded).Capture(1);
            Assert.That(frame.Regions.Count, Is.EqualTo(decoded.Cores.Length + decoded.Outposts.Length));
            Assert.That(frame.CellRegions.Count, Is.EqualTo(decoded.Map.WidthCells * decoded.Map.HeightCells));
            foreach (var region in frame.Regions.Where(r => r.CenterKind != RegionCenterKind.None))
            {
                int cell = Cell(decoded, region.Center);
                Assert.That(frame.CellRegions[cell], Is.EqualTo(region.Id), region.CenterKind + " " + region.CenterId);
            }
        }

        [Test]
        public void ControlAndRegionalPolicyAreLoggedAndReplayable()
        {
            var scenario = MapGenerator.GenerateTerrain(2);
            scenario.Economy.Regions = true;
            scenario.Economy.Industry = true;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.SetRegionControl(1, 1, 1, RegionControl.Human));
            gateway.SubmitEconomy(EconomyCommand.SetRegionPolicy(1, 2, 2, EconomyPolicy.Military));
            gateway.Step();

            var regions = sim.Capture(1).Regions;
            Assert.That(regions.Single(r => r.Id == 1).Control, Is.EqualTo(RegionControl.Human));
            Assert.That(regions.Single(r => r.Id == 2).EconomyPolicy, Is.EqualTo(EconomyPolicy.Military));
            Assert.That(DiagnosticComparison.Fields(sim.CaptureDiagnostic()).Any(field => field.Key.Contains("Regions.Changes", StringComparison.Ordinal)), Is.True);

            var input = gateway.Inputs.Single(i => i.Economy.Kind == EconomyCommandKind.SetRegionControl);
            Assert.That(InputBinary.Encode(InputBinary.Decode(InputBinary.Encode(input))), Is.EqualTo(InputBinary.Encode(input)));
        }

        private static int Cell(ScenarioDefinition scenario, SimPoint point)
            => checked((int)(point.Z.Raw / (65536L * scenario.Map.CellSizeMeters)) * scenario.Map.WidthCells
                + (int)(point.X.Raw / (65536L * scenario.Map.CellSizeMeters)));

        private static long DistanceSquared(SimPoint left, SimPoint right)
        {
            long dx = left.X.Raw - right.X.Raw;
            long dz = left.Z.Raw - right.Z.Raw;
            return checked(dx * dx + dz * dz);
        }

        private static uint RegionAt(ScenarioDefinition scenario, FactionFrame frame, SimPoint point)
            => frame.CellRegions[Cell(scenario, point)];

        private static SimPoint CellCenter(ScenarioDefinition scenario, int cell)
        {
            int x = cell % scenario.Map.WidthCells, z = cell / scenario.Map.WidthCells;
            return new SimPoint(Fix64.FromInt(x * scenario.Map.CellSizeMeters + scenario.Map.CellSizeMeters / 2),
                Fix64.FromInt(z * scenario.Map.CellSizeMeters + scenario.Map.CellSizeMeters / 2));
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition TownRegionScenario()
        {
            var scenario = MapGenerator.GenerateTerrain(1, true, true);
            scenario.Economy.Regions = true;
            scenario.Economy.Towns = true;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.StartStone = 5000;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AdvanceFoodCost = scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.Age2FoodCost = scenario.Economy.Age2WoodCost = 0;
            scenario.Economy.Age2Ticks = 1;
            scenario.Economy.TownWork = 1;
            scenario.Economy.TownWoodCost = 1;
            scenario.Economy.TownStoneCost = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000;
            return scenario;
        }

        private static uint PlaceTown(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            var node = scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Wood)
                .OrderByDescending(n => DistanceSquared(n.Position, scenario.Cores[0].Position)).First();
            int center = Cell(scenario, node.Position);
            int size = scenario.Economy.TownSizeCells;
            int width = scenario.Map.WidthCells;
            int before = sim.Capture(1).Economy.Buildings.Count;
            for (int radius = 2; radius <= 18; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                        int x = center % width + dx, z = center / width + dz;
                        if (x < 0 || z < 0 || x + size > scenario.Map.WidthCells || z + size > scenario.Map.HeightCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Town, z * width + x, Facing.North));
                        gateway.Step();
                        var building = sim.Capture(1).Economy.Buildings.Skip(before).FirstOrDefault(b => b.Kind == BuildingKind.Town);
                        if (building.Id != 0) return building.Id;
                    }
            return 0;
        }

        private static uint PlaceBarracks(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            int center = Cell(scenario, scenario.Cores[0].Position);
            int width = scenario.Map.WidthCells;
            int before = sim.Capture(1).Economy.Buildings.Count;
            for (int radius = 3; radius <= 12; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                        int x = center % width + dx, z = center / width + dz;
                        if (x < 0 || z < 0 || x + 2 > scenario.Map.WidthCells || z + 2 > scenario.Map.HeightCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Barracks, z * width + x));
                        gateway.Step();
                        var building = sim.Capture(1).Economy.Buildings.Skip(before).FirstOrDefault(b => b.Kind == BuildingKind.Barracks);
                        if (building.Id != 0) return building.Id;
                    }
            return 0;
        }
    }
}
