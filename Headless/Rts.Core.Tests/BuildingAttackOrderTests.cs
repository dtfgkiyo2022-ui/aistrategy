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
            bool humanAttackFix = false, bool fortify = false)
        {
            var scenario = Scenario(seed, enabled);
            scenario.Economy.HumanAttackFix = humanAttackFix;
            scenario.Economy.Fortify = fortify;
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
        {
            var place = typeof(Battle).GetMethod("PlaceBuildingAt", Hidden, null,
                new[] { typeof(uint), typeof(BuildingKind), typeof(int), typeof(Facing), typeof(uint) }, null);
            place.Invoke(scene.simulation, new object[] { 2u, BuildingKind.Wall, cell, Facing.North, 0u });
            int index = scene.state.BuildingCount - 1;
            scene.state.Buildings[index].Complete = true;
            scene.state.Buildings[index].Progress = 1;
            scene.state.Buildings[index].Hp = 1000;
            return index;
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
