using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class HumanWallWhenBlockedTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private delegate void PickBreachTargetDelegate(ref SoldierState soldier, in ArmyState army, bool hasHumanCommand);
        private delegate void PlaceBuildingAtDelegate(uint faction, BuildingKind kind, int origin, Facing facing, uint nodeId);
        private delegate void RefreshFortifyOwnersDelegate();

        [Test]
        public void Extension138RoundTripsAndOffWritesNothing()
        {
            var on = MapGenerator.GenerateTerrain(1);
            on.Economy.HumanWallWhenBlocked = true;
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);

            Assert.That(decoded.Economy.HumanWallWhenBlocked, Is.True);
            Assert.That(decoded.Extensions.Single(extension => extension.Id == 138).Version, Is.EqualTo(1));
            Assert.That(decoded.Extensions.Single(extension => extension.Id == 138).Data.Length, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = MapGenerator.GenerateTerrain(1);
            var baseline = MapGenerator.GenerateTerrain(1);
            off.Economy.HumanWallWhenBlocked = false;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(baseline)));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(extension => extension.Id == 138), Is.False);
        }

        [Test]
        public void LiveGameRulesEnableTheFlagOnlyOnTheLargeMap()
        {
            Assert.That(LiveGameRules.Create(1, false, true, false, false).Economy.HumanWallWhenBlocked, Is.False);
            Assert.That(LiveGameRules.Create(1, true, true, false, false).Economy.HumanWallWhenBlocked, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void HumanCoreArmyUsesTheChosenBreachWallOnlyWhenFlagIsOn(bool flag)
        {
            var scene = BreachScene(flag);
            var soldier = scene.state.Soldiers[scene.soldierIndex];
            var army = scene.state.Armies[scene.armyIndex];
            army.BreakingPath = true;
            army.BreachCell = scene.breachCell;
            scene.state.Armies[scene.armyIndex] = army;

            var method = typeof(Battle).GetMethod("PickBreachTarget", Hidden);
            var callback = (PickBreachTargetDelegate)method.CreateDelegate(typeof(PickBreachTargetDelegate), scene.simulation);
            callback(ref soldier, in army, true);

            Assert.That(soldier.TargetKind, Is.EqualTo(4));
            Assert.That(soldier.TargetId, Is.EqualTo(flag ? scene.breachId : scene.nearId));
            if (flag)
                Assert.That(soldier.MoveGoal, Is.EqualTo(scene.breachPoint));
        }

        [Test]
        public void AutonomousBreachPathIsIdenticalWithTheFlagOnOrOff()
        {
            var scene = BreachScene(false);
            var method = typeof(Battle).GetMethod("PickBreachTarget", Hidden);
            var callback = (PickBreachTargetDelegate)method.CreateDelegate(typeof(PickBreachTargetDelegate), scene.simulation);
            var soldier = scene.state.Soldiers[scene.soldierIndex];
            var army = scene.state.Armies[scene.armyIndex];

            scene.state.Config.Economy.HumanWallWhenBlocked = false;
            callback(ref soldier, in army, false);
            var offKind = soldier.TargetKind;
            var offId = soldier.TargetId;

            soldier.TargetKind = 0;
            soldier.TargetId = 0;
            soldier.MoveGoal = default;
            scene.state.Config.Economy.HumanWallWhenBlocked = true;
            callback(ref soldier, in army, false);

            Assert.That(soldier.TargetKind, Is.EqualTo(offKind));
            Assert.That(soldier.TargetId, Is.EqualTo(offId));
        }

        [Test]
        public void OpenHumanCorePathDoesNotUseWallFallbackWhenFlagIsOn()
        {
            Assert.That(CountWallTargets(false), Is.GreaterThan(0));
            Assert.That(CountWallTargets(true), Is.EqualTo(0));
        }

        private static int CountWallTargets(bool flag)
        {
            var scenario = LiveGameRules.Create(flag ? 31UL : 32UL, true, true, false, false,
                randomCoresOff: true, wardsOff: true, skillsOff: true);
            scenario.Economy.HumanWallWhenBlocked = flag;
            scenario.Economy.AiLevelWest = 2;
            scenario.Economy.AiLevelEast = 0;
            var simulation = new Battle(scenario);
            var state = State(simulation);
            uint armyId = state.Factions[0].ArmyIds[0];
            int soldierIndex = Array.FindIndex(state.Soldiers, soldier => soldier.Alive && soldier.Initial.ArmyId == armyId);
            int start = state.Map.Cell(state.Soldiers[soldierIndex].Position);
            int pathTarget = state.Map.Cell(state.Cores[state.Factions[1].CoreId - 1].Definition.Position);
            int wallCell = FindWallCell(state, start, pathTarget);
            int wallIndex = AddWall(simulation, state, wallCell);
            for (int i = 0; i < state.SoldierCount; i++)
                if (state.Soldiers[i].Alive && state.Soldiers[i].Initial.ArmyId == armyId)
                {
                    state.Soldiers[i].Position = state.Map.Center(start);
                    state.Soldiers[i].MoveGoal = state.Map.Center(start);
                }
            Array.Fill(state.Factions[0].VisibleCells, true);
            simulation.RebuildSpatialCombatIndexForTest();

            var target = new ScopeKey(1, ScopeKind.Army, armyId);
            var goal = new PolicyGoal(GoalKind.Core, state.Factions[1].CoreId, default);
            var order = new PolicyOrder(1, 0, CommandSource.Human, target, PolicyKind.Focus, goal, 100,
                new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0, simulation.Revision(target),
                Array.Empty<PolicyVersion>(), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None));
            var token = new PolicyOrder(order.CommandId, order.BatchId, order.Source, order.Target, order.Kind, order.Goal,
                order.Priority, order.AllowedLoss, order.End, order.ReservePermille, order.TargetRevision + 1,
                order.Parents, order.ObservedTick, order.Expiration);
            simulation.Step(1, new[]
            {
                new ScheduledInput(1, InputKind.Reserve, 0, 1, order.CommandId, 1, new[] { order }, long.MaxValue, ReasonCode.None),
                new ScheduledInput(2, InputKind.Resolve, 0, 1, order.CommandId, 2, new[] { token }, long.MaxValue, ReasonCode.None)
            });

            Assert.That(state.Buildings[wallIndex].Alive, Is.True);
            return state.Soldiers.Count(soldier => soldier.Alive && soldier.Initial.ArmyId == armyId && soldier.TargetKind == 4);
        }

        private static (Battle simulation, WorldState state, int soldierIndex, int armyIndex, int breachCell,
            uint nearId, uint breachId, SimPoint breachPoint) BreachScene(bool flag)
        {
            var scenario = MapGenerator.GenerateTerrain(flag ? 41UL : 42UL);
            scenario.Economy.Enabled = true;
            scenario.Economy.Fortify = true;
            scenario.Economy.Ages = true;
            scenario.Economy.HumanWallWhenBlocked = flag;
            scenario.Economy.AiLevelWest = 2;
            var simulation = new Battle(scenario);
            var state = State(simulation);
            uint armyId = state.Factions[0].ArmyIds[0];
            int soldierIndex = Array.FindIndex(state.Soldiers, soldier => soldier.Alive && soldier.Initial.ArmyId == armyId);
            int start = state.Map.Cell(state.Soldiers[soldierIndex].Position);
            int width = state.Config.Map.WidthCells;
            int nearCell = -1, breachCell = -1;
            for (int z = 0; z < state.Config.Map.HeightCells && nearCell < 0; z++)
                for (int x = 0; x + 12 < width; x++)
                {
                    int candidate = z * width + x;
                    int near = candidate + 9, breachCandidate = candidate + 11;
                    if (candidate != start || !state.Map.IsPassable(candidate) || !state.Map.IsPassable(near)
                        || !state.Map.IsPassable(breachCandidate) || Math.Abs(candidate % width - start % width) > 12
                        || state.Nodes.Any(node => node.Definition.Id != 0 && (state.Map.Cell(node.Definition.Position) == near
                            || state.Map.Cell(node.Definition.Position) == breachCandidate))) continue;
                    nearCell = near; breachCell = breachCandidate; start = candidate;
                }
            Assert.That(nearCell, Is.GreaterThanOrEqualTo(0));
            int nearIndex = AddWall(simulation, state, nearCell);
            int breachIndex = AddWall(simulation, state, breachCell);
            var refresh = (RefreshFortifyOwnersDelegate)typeof(Battle).GetMethod("RefreshFortifyOwners", Hidden)
                .CreateDelegate(typeof(RefreshFortifyOwnersDelegate), simulation);
            refresh();
            var soldier = state.Soldiers[soldierIndex];
            soldier.Position = state.Map.Center(start);
            soldier.MoveGoal = soldier.Position;
            soldier.TargetKind = 0;
            soldier.TargetId = 0;
            soldier.IsRetreating = false;
            state.Soldiers[soldierIndex] = soldier;
            Array.Fill(state.Factions[0].VisibleCells, true);
            simulation.RebuildSpatialCombatIndexForTest();
            var breachBuilding = state.Buildings[breachIndex];
            Assert.That(state.BuildingCount, Is.GreaterThanOrEqualTo(2));
            return (simulation, state, soldierIndex, (int)armyId - 1, breachCell,
                state.Buildings[nearIndex].Id, breachBuilding.Id, NearestWallPoint(state, breachBuilding, soldier.Position));
        }

        private static SimPoint NearestWallPoint(WorldState state, BuildingState building, SimPoint from)
        {
            long cell = Fix64.FromInt(state.Config.Map.CellSizeMeters).Raw;
            long minX = (long)(building.OriginCell % state.Config.Map.WidthCells) * cell;
            long minZ = (long)(building.OriginCell / state.Config.Map.WidthCells) * cell;
            long x = from.X.Raw < minX ? minX : from.X.Raw > minX + cell ? minX + cell : from.X.Raw;
            long z = from.Z.Raw < minZ ? minZ : from.Z.Raw > minZ + cell ? minZ + cell : from.Z.Raw;
            return new SimPoint(Fix64.FromRaw(x), Fix64.FromRaw(z));
        }

        private static int AddWall(Battle simulation, WorldState state, int cell)
        {
            state.Economies[1].Stone = 100000;
            state.Economies[1].Wood = 100000;
            var place = typeof(Battle).GetMethod("PlaceBuildingAt", Hidden, null,
                new[] { typeof(uint), typeof(BuildingKind), typeof(int), typeof(Facing), typeof(uint) }, null);
            var callback = (PlaceBuildingAtDelegate)place.CreateDelegate(typeof(PlaceBuildingAtDelegate), simulation);
            callback(2u, BuildingKind.Wall, cell, Facing.North, 0u);
            int index = state.BuildingCount - 1;
            state.Buildings[index].Complete = true;
            state.Buildings[index].Progress = 1;
            state.Buildings[index].Hp = 1000;
            return index;
        }

        private static int FindWallCell(WorldState state, int start, int pathTarget)
        {
            int width = state.Config.Map.WidthCells, sx = start % width, sz = start / width;
            var path = state.Map.FindPath(start, state.Map.Center(pathTarget), 1);
            for (int z = Math.Max(0, sz - 12); z <= Math.Min(state.Config.Map.HeightCells - 1, sz + 12); z++)
                for (int x = Math.Max(0, sx - 12); x <= Math.Min(width - 1, sx + 12); x++)
                {
                    int cell = z * width + x;
                    if (Math.Abs(x - sx) < 6 || Math.Abs(x - sx) > 11 || !state.Map.IsPassable(cell) || path.Contains(cell)) continue;
                    if (state.Nodes.Any(node => node.Definition.Id != 0 && state.Map.Cell(node.Definition.Position) == cell)) continue;
                    return cell;
                }
            Assert.Fail("no wall test cell");
            return -1;
        }

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(simulation);
    }
}
