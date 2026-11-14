using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BuildingAttackOrderTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(ulong seed, bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(seed);
            scenario.Economy.BuildingAttackOrder = enabled;
            scenario.Economy.StartFood = 100000;
            scenario.Economy.StartWood = 100000;
            scenario.Economy.StartStone = 100000;
            scenario.Economy.GatherIntervalTicks = 1000000;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            return scenario;
        }

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(simulation);

        private static int FindOrigin(WorldState state, int size)
        {
            int width = state.Config.Map.WidthCells, height = state.Config.Map.HeightCells;
            int core = state.Map.Cell(state.Cores[0].Definition.Position);
            int cx = core % width, cz = core / width;
            for (int radius = 6; radius < 20; radius++)
                for (int z = Math.Max(0, cz - radius); z <= Math.Min(height - size, cz + radius); z++)
                    for (int x = Math.Max(0, cx - radius); x <= Math.Min(width - size, cx + radius); x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) != radius) continue;
                        int origin = z * width + x;
                        bool clear = true;
                        for (int dz = 0; dz < size && clear; dz++)
                            for (int dx = 0; dx < size; dx++)
                            {
                                int cell = origin + dz * width + dx;
                                if (!state.Map.IsPassable(cell) || state.Nodes.Any(n => n.Definition.Id != 0 && state.Map.Cell(n.Definition.Position) == cell)
                                    || state.Cores.Any(c => state.Map.Cell(c.Definition.Position) == cell)) { clear = false; break; }
                            }
                        if (clear) return origin;
                    }
            Assert.Fail("no clear building origin");
            return 0;
        }

        private static int FindStart(WorldState state, int origin, int size)
        {
            int width = state.Config.Map.WidthCells, height = state.Config.Map.HeightCells;
            int ox = origin % width, oz = origin / width;
            for (int radius = 5; radius < 20; radius++)
                for (int z = Math.Max(0, oz - radius); z <= Math.Min(height - 1, oz + size - 1 + radius); z++)
                    for (int x = Math.Max(0, ox - radius); x <= Math.Min(width - 1, ox + size - 1 + radius); x++)
                    {
                        if (Math.Max(Math.Max(ox - x, x - (ox + size - 1)), Math.Max(oz - z, z - (oz + size - 1))) != radius) continue;
                        int cell = z * width + x;
                        if (state.Map.IsPassable(cell)) return cell;
                    }
            Assert.Fail("no passable attack start cell");
            return 0;
        }

        private static (Battle simulation, WorldState state, int buildingIndex, uint armyId, SimPoint center) Scene(bool enabled, BuildingKind kind, ulong seed,
            bool humanAttackFix = false, bool fortify = false, bool directPath = false)
        {
            var scenario = Scenario(seed, enabled);
            scenario.Economy.HumanAttackFix = humanAttackFix;
            scenario.Economy.Fortify = fortify;
            scenario.Economy.DirectPathMovement = directPath;
            var simulation = new Battle(scenario);
            var state = State(simulation);
            int size = kind == BuildingKind.Wall ? 1 : kind == BuildingKind.Barracks ? 3 : 4;
            int origin = FindOrigin(state, size);
            state.Economies[1].Wood = 100000;
            state.Economies[1].Stone = 100000;
            var place = typeof(Battle).GetMethod("PlaceBuildingAt", Hidden, null,
                new[] { typeof(uint), typeof(BuildingKind), typeof(int), typeof(Facing), typeof(uint) }, null);
            Assert.That(place, Is.Not.Null);
            place.Invoke(simulation, new object[] { 2u, kind, origin, Facing.North, 0u });
            int buildingIndex = state.BuildingCount - 1;
            Assert.That(buildingIndex, Is.GreaterThanOrEqualTo(0));
            state.Buildings[buildingIndex].Complete = true;
            state.Buildings[buildingIndex].Progress = 1;
            state.Buildings[buildingIndex].Hp = 1000;
            uint armyId = state.Factions[0].ArmyIds[0];
            int start = FindStart(state, origin, size);
            var startPoint = state.Map.Center(start);
            for (int i = 0; i < state.SoldierCount; i++)
                if (state.Soldiers[i].Alive && state.Soldiers[i].Initial.FactionId == 1 && state.Soldiers[i].Initial.ArmyId == armyId)
                {
                    state.Soldiers[i].Position = startPoint;
                    state.Soldiers[i].MoveGoal = startPoint;
                    state.Soldiers[i].TargetKind = 0;
                    state.Soldiers[i].TargetId = 0;
                    state.Soldiers[i].IsRetreating = false;
                }
            Array.Fill(state.Factions[0].VisibleCells, true);
            simulation.RebuildSpatialCombatIndexForTest();
            var centerMethod = typeof(Battle).GetMethod("BuildingCenter", Hidden);
            var center = (SimPoint)centerMethod.Invoke(simulation, new object[] { state.Buildings[buildingIndex] });
            return (simulation, state, buildingIndex, armyId, center);
        }

        private static int AddCompleteWall((Battle simulation, WorldState state, int buildingIndex, uint armyId, SimPoint center) scene, int cell)
            => AddCompleteBuilding(scene, BuildingKind.Wall, cell, 1000);

        private static int AddCompleteBuilding((Battle simulation, WorldState state, int buildingIndex, uint armyId, SimPoint center) scene,
            BuildingKind kind, int cell, int hp)
        {
            var place = typeof(Battle).GetMethod("PlaceBuildingAt", Hidden, null,
                new[] { typeof(uint), typeof(BuildingKind), typeof(int), typeof(Facing), typeof(uint) }, null);
            place.Invoke(scene.simulation, new object[] { 2u, kind, cell, Facing.North, 0u });
            int index = scene.state.BuildingCount - 1;
            scene.state.Buildings[index].Complete = true;
            scene.state.Buildings[index].Progress = 1;
            scene.state.Buildings[index].Hp = hp;
            return index;
        }

        private static int FindPassableSideCell(WorldState state, int origin, int size, int side)
        {
            int width = state.Config.Map.WidthCells, height = state.Config.Map.HeightCells;
            int ox = origin % width, oz = origin / width;
            for (int offset = 0; offset < size; offset++)
            {
                int x = side == 1 ? ox + size : side == 3 ? ox - 1 : ox + offset;
                int z = side == 0 ? oz + size : side == 2 ? oz - 1 : oz + offset;
                if (x < 0 || x >= width || z < 0 || z >= height) continue;
                int cell = z * width + x;
                if (state.Map.IsPassable(cell)) return cell;
            }
            return -1;
        }

        private static long DistanceSquaredRaw(SimPoint first, SimPoint second)
        {
            long dx = first.X.Raw - second.X.Raw, dz = first.Z.Raw - second.Z.Raw;
            return checked(dx * dx + dz * dz);
        }

        private static SimPoint NearestBuildingFootprintPoint(WorldState state, BuildingState building, SimPoint from)
        {
            int width = state.Config.Map.WidthCells, size = building.Kind == BuildingKind.Barracks ? 3 : 4;
            long cell = Fix64.FromInt(state.Config.Map.CellSizeMeters).Raw;
            long minX = (long)(building.OriginCell % width) * cell;
            long minZ = (long)(building.OriginCell / width) * cell;
            long maxX = minX + size * cell, maxZ = minZ + size * cell;
            long x = from.X.Raw < minX ? minX : from.X.Raw > maxX ? maxX : from.X.Raw;
            long z = from.Z.Raw < minZ ? minZ : from.Z.Raw > maxZ ? maxZ : from.Z.Raw;
            return new SimPoint(Fix64.FromRaw(x), Fix64.FromRaw(z));
        }

        private static int FindRepairProbeCell(WorldState state, BuildingState building, Fix64 range)
        {
            int width = state.Config.Map.WidthCells, height = state.Config.Map.HeightCells;
            int ox = building.OriginCell % width, oz = building.OriginCell / width;
            for (int radius = 2; radius <= 8; radius++)
                for (int z = Math.Max(0, oz - radius); z <= Math.Min(height - 1, oz + 3 + radius); z++)
                    for (int x = Math.Max(0, ox - radius); x <= Math.Min(width - 1, ox + 2 + radius); x++)
                    {
                        if (Math.Max(Math.Abs(x - ox), Math.Abs(z - oz)) != radius) continue;
                        int cell = z * width + x;
                        if (!state.Map.IsPassableFor(cell, 1)) continue;
                        var position = state.Map.Center(cell);
                        var foot = NearestBuildingFootprintPoint(state, building, position);
                        if (DistanceSquaredRaw(position, foot) <= checked(range.Raw * range.Raw)) continue;
                        if (DistanceSquaredRaw(position, state.Map.Center(building.OriginCell + width + 1))
                            > checked((range.Raw + Fix64.FromInt(4).Raw) * (range.Raw + Fix64.FromInt(4).Raw))) continue;
                        return cell;
                    }
            Assert.Fail("no repair probe cell");
            return 0;
        }

        private static int FindNearestPassablePerimeterCell(WorldState state, BuildingState building, SimPoint from)
        {
            int width = state.Config.Map.WidthCells, height = state.Config.Map.HeightCells;
            int size = building.Kind == BuildingKind.Barracks ? 3 : 4;
            int ox = building.OriginCell % width, oz = building.OriginCell / width, best = -1;
            long bestDistance = 0;
            for (int z = oz - 1; z <= oz + size; z++)
                for (int x = ox - 1; x <= ox + size; x++)
                {
                    if (x < 0 || x >= width || z < 0 || z >= height
                        || x > ox - 1 && x < ox + size && z > oz - 1 && z < oz + size) continue;
                    int cell = z * width + x;
                    if (!state.Map.IsPassableFor(cell, 1)) continue;
                    long distance = DistanceSquaredRaw(from, state.Map.Center(cell));
                    if (best < 0 || distance < bestDistance || distance == bestDistance && cell < best)
                    { best = cell; bestDistance = distance; }
                }
            Assert.That(best, Is.GreaterThanOrEqualTo(0), "no passable perimeter cell");
            return best;
        }

        private static int RunOrder((Battle simulation, WorldState state, int buildingIndex, uint armyId, SimPoint center) scene, int ticks,
            CommandGateway gateway = null)
        {
            gateway = gateway ?? new CommandGateway(scene.simulation);
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, scene.armyId), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, scene.center), 100, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            int before = scene.state.Buildings[scene.buildingIndex].Hp;
            for (int tick = 0; tick < ticks; tick++) gateway.Step();
            return before - scene.state.Buildings[scene.buildingIndex].Hp;
        }

        [TestCase(BuildingKind.Wall)]
        [TestCase(BuildingKind.Farm)]
        [TestCase(BuildingKind.Barracks)]
        [TestCase(BuildingKind.Castle)]
        public void PointInsideOneToFourCellBuildingDamagesItWhenEnabled(BuildingKind kind)
        {
            var scene = Scene(true, kind, 1000UL + (ulong)kind);
            int damage = RunOrder(scene, 600);
            Assert.That(damage, Is.GreaterThan(0), "building kind " + kind);
        }

        [Test]
        public void DestroyedBuildingIsGoneBeforeTheNextPointOrder()
        {
            var scene = Scene(true, BuildingKind.Wall, 1150);
            scene.state.Buildings[scene.buildingIndex].Hp = 40;
            var gateway = new CommandGateway(scene.simulation);
            Assert.That(RunOrder(scene, 600, gateway), Is.GreaterThan(0));
            Assert.That(scene.state.Buildings[scene.buildingIndex].Alive, Is.False);

            gateway.Submit(new UserPolicyIntent(2, new ScopeKey(1, ScopeKind.Army, scene.armyId), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, scene.center), 100, new LossBudget(1000),
                new EndCondition(EndKind.Arrived, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            for (int tick = 0; tick < 100; tick++) gateway.Step();
            Assert.That(scene.state.Buildings[scene.buildingIndex].Hp, Is.EqualTo(0));
            Assert.That(scene.state.Buildings[scene.buildingIndex].Alive, Is.False);
        }

        [Test]
        public void HumanAttackFixBreaksAnEnclosingWallThenAttacksTheClickedBuilding()
        {
            var scene = Scene(true, BuildingKind.Wall, 1311, true, true);
            int width = scene.state.Config.Map.WidthCells;
            int origin = scene.state.Buildings[scene.buildingIndex].OriginCell;
            int[] neighbours = { origin + width, origin + 1, origin - width, origin - 1 };
            var walls = new List<int>();
            foreach (int cell in neighbours) if (cell >= 0) walls.Add(AddCompleteWall(scene, cell));
            scene.state.Buildings[scene.buildingIndex].Hp = 2000;
            simulationRebuild(scene.simulation);
            int targetBefore = scene.state.Buildings[scene.buildingIndex].Hp;
            scene.state.Economies[0].AutoOff = true;
            RunOrder(scene, 1800);
            Assert.That(walls.Any(index => !scene.state.Buildings[index].Alive), Is.True);
            Assert.That(scene.state.Buildings[scene.buildingIndex].Hp, Is.LessThan(targetBefore));
        }

        [Test]
        public void HumanAttackFixUsesTheOnlyOpenFaceWithoutHittingTheNearbyWall()
        {
            var scene = Scene(true, BuildingKind.Farm, 1312, true, false, true);
            var target = scene.state.Buildings[scene.buildingIndex];
            int size = 4, width = scene.state.Config.Map.WidthCells;
            var sideCells = new List<int>();
            for (int side = 0; side < 4; side++)
            {
                int adjacent = FindPassableSideCell(scene.state, target.OriginCell, size, side);
                if (adjacent >= 0) sideCells.Add(adjacent);
            }
            Assert.That(sideCells.Count, Is.GreaterThanOrEqualTo(4), "four sides around target");
            for (int i = 0; i < 3; i++) AddCompleteWall(scene, sideCells[i]);
            int openSide = 3;
            int openCell = sideCells[openSide];
            int openX = openCell % width, openZ = openCell / width;
            int targetX = target.OriginCell % width, targetZ = target.OriginCell / width;
            int dx = Math.Sign(openX - targetX), dz = Math.Sign(openZ - targetZ);
            int wallCell = openCell + dx + dz * width;
            if (wallCell < 0 || wallCell >= scene.state.Config.Map.WidthCells * scene.state.Config.Map.HeightCells
                || !scene.state.Map.IsPassable(wallCell)) wallCell = sideCells[openSide];
            int wall = AddCompleteWall(scene, wallCell);
            target = scene.state.Buildings[scene.buildingIndex];
            target.Hp = 100000;
            scene.state.Buildings[scene.buildingIndex] = target;
            simulationRebuild(scene.simulation);
            scene.state.Economies[0].AutoOff = true;
            scene.state.Economies[1].AutoOff = true;
            for (int i = 0; i < scene.state.SoldierCount; i++)
                if (scene.state.Soldiers[i].Alive && scene.state.Soldiers[i].Initial.FactionId == 1
                    && scene.state.Soldiers[i].Initial.ArmyId != scene.armyId)
                    scene.state.Soldiers[i].Alive = false;
            simulationRebuild(scene.simulation);
            var gateway = new CommandGateway(scene.simulation);
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, scene.armyId), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, scene.center), 100, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            var attackedBuildings = new HashSet<uint>();
            gateway.Step(); gateway.Step();
            for (int i = 0; i < scene.state.BuildingCount; i++)
                if (scene.state.Buildings[i].Kind == BuildingKind.Wall) scene.state.Buildings[i].Hp = 1000;
            for (int tick = 2; tick < 600; tick++)
            {
                gateway.Step();
                foreach (var soldier in scene.state.Soldiers)
                    if (soldier.Alive && soldier.Initial.FactionId == 1 && soldier.Initial.ArmyId == scene.armyId
                        && soldier.IsAttacking && soldier.TargetKind == Battle.TargetBuilding)
                        attackedBuildings.Add(soldier.TargetId);
            }
            Assert.That(scene.state.Buildings[scene.buildingIndex].Hp, Is.LessThan(target.Hp));
            Assert.That(scene.state.Buildings[wall].Hp, Is.EqualTo(1000),
                string.Join(";", scene.state.Buildings.Select(b => b.Id + ":" + b.Kind + ":" + b.Hp + ":" + b.OriginCell))
                + " allTargets=" + string.Join(",", attackedBuildings));
        }

        [Test]
        public void HumanAttackFixRetargetsAToBuildRepairerBeforeTheBuilding()
        {
            var scene = Scene(true, BuildingKind.Wall, 1313, true, true);
            var target = scene.state.Buildings[scene.buildingIndex];
            target.Hp = 500;
            scene.state.Buildings[scene.buildingIndex] = target;
            int villagerIndex = Array.FindIndex(scene.state.Villagers,
                villager => villager.Alive && villager.FactionId == 2);
            Assert.That(villagerIndex, Is.GreaterThanOrEqualTo(0));
            scene.state.Villagers[villagerIndex].Position = scene.state.Soldiers[0].Position;
            scene.state.Villagers[villagerIndex].MoveGoal = scene.state.Villagers[villagerIndex].Position;
            scene.state.Villagers[villagerIndex].Task = VillagerTask.ToBuild;
            scene.state.Villagers[villagerIndex].BuildingId = target.Id;
            Array.Fill(scene.state.Factions[0].VisibleCells, true);
            simulationRebuild(scene.simulation);
            RunOrder(scene, 1);
            Assert.That(scene.state.Soldiers.Any(s => s.Alive && s.Initial.FactionId == 1
                && s.TargetKind == Battle.TargetVillager && s.TargetId == scene.state.Villagers[villagerIndex].Id), Is.True);
        }

        [Test]
        public void HumanAttackFixKeepsHalfTheArmyOnTheRecordedBuildingApproach()
        {
            var scene = Scene(true, BuildingKind.Barracks, 1314, true, false);
            var target = scene.state.Buildings[scene.buildingIndex];
            target.Hp = 100000;
            scene.state.Buildings[scene.buildingIndex] = target;
            int living = scene.state.Soldiers.Count(s => s.Alive && s.Initial.FactionId == 1 && s.Initial.ArmyId == scene.armyId);
            var gateway = new CommandGateway(scene.simulation);
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, scene.armyId), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, scene.center), 100, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            var attacked = new HashSet<uint>();
            for (int tick = 0; tick < 600; tick++)
            {
                gateway.Step();
                for (int i = 0; i < scene.state.SoldierCount; i++)
                {
                    var soldier = scene.state.Soldiers[i];
                    if (soldier.Alive && soldier.Initial.FactionId == 1 && soldier.Initial.ArmyId == scene.armyId
                        && soldier.TargetKind == Battle.TargetBuilding && soldier.TargetId == target.Id && soldier.IsAttacking)
                        attacked.Add(soldier.Initial.Id);
                }
            }
            Assert.That(attacked.Count, Is.GreaterThanOrEqualTo((living + 1) / 2));
            Assert.That(scene.state.Buildings[scene.buildingIndex].Hp, Is.LessThan(target.Hp));
        }

        [Test]
        public void HumanAttackFixWalksToAOpenPerimeterCellForARepairerInsideTheFootprint()
        {
            var scene = Scene(true, BuildingKind.Barracks, 1315, true, true);
            var target = scene.state.Buildings[scene.buildingIndex];
            target.Hp = 500;
            scene.state.Buildings[scene.buildingIndex] = target;
            int middleCell = target.OriginCell + scene.state.Config.Map.WidthCells + 1;
            int villagerIndex = Array.FindIndex(scene.state.Villagers,
                villager => villager.Alive && villager.FactionId == 2);
            Assert.That(villagerIndex, Is.GreaterThanOrEqualTo(0));
            scene.state.Villagers[villagerIndex].Position = scene.state.Map.Center(middleCell);
            scene.state.Villagers[villagerIndex].MoveGoal = scene.state.Villagers[villagerIndex].Position;
            scene.state.Villagers[villagerIndex].Task = VillagerTask.Building;
            scene.state.Villagers[villagerIndex].BuildingId = target.Id;
            int soldierIndex = Array.FindIndex(scene.state.Soldiers,
                soldier => soldier.Alive && soldier.Initial.FactionId == 1 && soldier.Initial.ArmyId == scene.armyId);
            Assert.That(soldierIndex, Is.GreaterThanOrEqualTo(0));
            int probeCell = FindRepairProbeCell(scene.state, target, scene.state.Soldiers[soldierIndex].Parameters.Range);
            var soldier = scene.state.Soldiers[soldierIndex];
            soldier.Position = scene.state.Map.Center(probeCell);
            soldier.MoveGoal = soldier.Position;
            soldier.TargetKind = Battle.TargetBuilding;
            soldier.TargetId = target.Id;
            soldier.IsRetreating = false;
            scene.state.Soldiers[soldierIndex] = soldier;
            Array.Fill(scene.state.Factions[0].VisibleCells, true);
            simulationRebuild(scene.simulation);

            var args = new object[] { soldier };
            typeof(Battle).GetMethod("RetargetRepairingVillager", Hidden).Invoke(scene.simulation, args);
            var result = (SoldierState)args[0];
            int expectedCell = FindNearestPassablePerimeterCell(scene.state, target, soldier.Position);
            Assert.That(result.TargetKind, Is.EqualTo(Battle.TargetBuilding));
            Assert.That(result.MoveGoal, Is.EqualTo(scene.state.Map.Center(expectedCell)));
            Assert.That(result.MoveGoal, Is.Not.EqualTo(scene.state.Villagers[villagerIndex].Position));
        }

        [Test]
        public void HumanAttackFixDoesNotChangeAGroundPointOrder()
        {
            var off = Scene(true, BuildingKind.Wall, 1316, false, false);
            var on = Scene(true, BuildingKind.Wall, 1316, true, false);
            int width = off.state.Config.Map.WidthCells;
            int groundCell = off.state.Buildings[off.buildingIndex].OriginCell + width * 2 + 2;
            var goal = off.state.Map.Center(groundCell);
            var offGateway = new CommandGateway(off.simulation);
            var onGateway = new CommandGateway(on.simulation);
            offGateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, off.armyId), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, goal), 100, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            onGateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, on.armyId), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, goal), 100, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            for (int tick = 0; tick < 300; tick++) { offGateway.Step(); onGateway.Step(); }
            for (int i = 0; i < off.state.SoldierCount; i++)
            {
                Assert.That(on.state.Soldiers[i].Position, Is.EqualTo(off.state.Soldiers[i].Position), "soldier " + i);
                Assert.That(on.state.Soldiers[i].Hp, Is.EqualTo(off.state.Soldiers[i].Hp), "soldier hp " + i);
            }
        }

        private static void simulationRebuild(Battle simulation)
            => simulation.RebuildSpatialCombatIndexForTest();

        [Test]
        public void Extension100RoundTripsAndOffAddsNoBytes()
        {
            var on = Scenario(1200, true);
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.BuildingAttackOrder, Is.True);
            Assert.That(decoded.Extensions.Single(e => e.Id == 100).Data.Length, Is.EqualTo(4));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var off = Scenario(1200, false);
            var baseline = Scenario(1200, false);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(baseline)));
            Assert.That(off.Extensions.Any(e => e.Id == 100), Is.False);
        }

        [Test]
        public void Extension111RoundTripsAndOffAddsNoBytes()
        {
            var on = Scenario(1211, true);
            on.Economy.HumanAttackFix = true;
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.HumanAttackFix, Is.True);
            Assert.That(decoded.Extensions.Single(e => e.Id == 111).Data.Length, Is.EqualTo(4));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = Scenario(1211, false);
            var baseline = Scenario(1211, false);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(baseline)));
            Assert.That(off.Extensions.Any(e => e.Id == 111), Is.False);
        }
    }
}
