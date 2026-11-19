using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class UnitStuckFixTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void Extension129RoundTripsAndIsAbsentWhenOff()
        {
            var scenario = LiveGameRules.Create(1291UL, true, true, false, false);
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.UnitStuckFix, Is.True);
            Assert.That(decoded.Extensions.Single(e => e.Id == 129).Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            scenario.Economy.UnitStuckFix = false;
            var off = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario));
            Assert.That(off.Economy.UnitStuckFix, Is.False);
            Assert.That(off.Extensions.Any(e => e.Id == 129), Is.False);
        }

        [Test]
        public void EmptyArmyJoinRouteRecoversOnlyWhenFlagIsOn()
        {
            var on = PrepareJoinScenario(true);
            var off = PrepareJoinScenario(false);
            InvokeJoinFormation(on);
            InvokeJoinFormation(off);

            Assert.That(Current(on).Joining, Is.False);
            Assert.That(Current(off).Joining, Is.True);
            Assert.That(Current(off).MoveGoal, Is.EqualTo(Current(off).Position));
        }

        [Test]
        public void LateHumanSoldierJoinsOnlyWhenFlagIsOn()
        {
            var on = PrepareHumanScenario(true);
            var off = PrepareHumanScenario(false);
            InvokeFormation(on);
            InvokeFormation(off);

            Assert.That(Current(on).MoveGoal, Is.Not.EqualTo(Current(on).Position));
            Assert.That(Current(off).MoveGoal, Is.EqualTo(Current(off).Position));
        }

        [Test]
        public void FlagOffKeepsEveryTickHashStableAfterScenarioRoundTrip()
        {
            var source = LiveGameRules.Create(1292UL, true, true, false, false);
            source.Economy.UnitStuckFix = false;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 120; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void OutOfRangeBuildingTargetAdvancesWhenFlagIsOn()
        {
            var on = PrepareOutOfRangeBuildingScenario(true);
            var off = PrepareOutOfRangeBuildingScenario(false);
            var old = Current(on).Position;
            on.Simulation.Step(1, Array.Empty<ScheduledInput>());
            on.Simulation.Step(2, Array.Empty<ScheduledInput>());
            off.Simulation.Step(1, Array.Empty<ScheduledInput>());
            off.Simulation.Step(2, Array.Empty<ScheduledInput>());

            Assert.That(Current(on).TargetKind, Is.EqualTo(4));
            Assert.That(Current(on).TargetId, Is.EqualTo(on.BuildingId));
            Assert.That(Current(on).MoveGoal, Is.Not.EqualTo(old));
        }

        private sealed class JoinSetup
        {
            internal Battle Simulation;
            internal uint SoldierId;
            internal uint ArmyId;
            internal SimPoint Goal;
        }

        private static JoinSetup PrepareJoinScenario(bool enabled)
        {
            var scenario = LiveGameRules.Create(1293UL, true, true, false, false);
            scenario.Economy.UnitStuckFix = enabled;
            scenario.Economy.ArmyFinalApproach = false;
            scenario.Economy.ArmyFlowWaitTicks = 3;
            var simulation = new Battle(scenario);
            var world = (WorldState)Field(simulation, "world");
            uint armyId = scenario.Factions[0].ArmyIds[0];
            ref var army = ref world.Armies[armyId - 1];
            var goal = scenario.Cores[1].Position;
            army.Policy = PolicyKind.Focus;
            army.Goal = new PolicyGoal(GoalKind.Core, scenario.Cores[1].Id, default);
            army.CommandId = 0;
            army.Decision = default;
            army.Path = new[] { world.Map.Cell(goal) };
            army.PathCursor = 0;
            army.PathGoal = goal;
            army.HasPathGoal = true;
            army.PathImpossible = false;
            uint soldierId = army.SoldierIds[0];
            ref var soldier = ref world.Soldiers[soldierId - 1];
            soldier.Position = scenario.Cores[0].Position;
            soldier.MoveGoal = goal;
            soldier.Joining = true;
            soldier.JoinCursor = 0;
            soldier.LocalGoal = goal;
            soldier.LocalPath = Array.Empty<int>();
            return new JoinSetup { Simulation = simulation, SoldierId = soldierId, ArmyId = armyId, Goal = goal };
        }

        private static void InvokeJoinFormation(JoinSetup setup)
        {
            var world = (WorldState)Field(setup.Simulation, "world");
            var army = world.Armies[setup.ArmyId - 1];
            var method = typeof(Battle).GetMethod("PrepareArmyFlowFormation", Hidden);
            method.Invoke(setup.Simulation, new object[] { army, setup.Goal });
        }

        private sealed class HumanSetup
        {
            internal Battle Simulation;
            internal uint SoldierId;
            internal uint ArmyId;
            internal SimPoint Goal;
        }

        private static HumanSetup PrepareHumanScenario(bool enabled)
        {
            var scenario = LiveGameRules.Create(1294UL, true, true, false, false);
            scenario.Economy.UnitStuckFix = enabled;
            scenario.Economy.ArmyFinalApproach = false;
            var simulation = new Battle(scenario);
            uint armyId = scenario.Factions[0].ArmyIds[0];
            var target = new ScopeKey(1, ScopeKind.Army, armyId);
            var point = scenario.Cores[1].Position;
            var order = new PolicyOrder(1, 0, CommandSource.Human, target, PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, point), 50, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0, simulation.Revision(target),
                simulation.Versions(target).Where(v => !v.Scope.Equals(target)).ToArray(), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None));
            var token = new PolicyOrder(order.CommandId, order.BatchId, order.Source, order.Target, order.Kind, order.Goal,
                order.Priority, order.AllowedLoss, order.End, order.ReservePermille, order.TargetRevision + 1,
                order.Parents, order.ObservedTick, order.Expiration);
            simulation.Step(1, new[]
            {
                new ScheduledInput(1, InputKind.Reserve, 0, 1, order.CommandId, 1, new[] { order }, long.MaxValue, ReasonCode.None),
                new ScheduledInput(2, InputKind.Resolve, 0, 1, order.CommandId, 1, new[] { token }, long.MaxValue, ReasonCode.None)
            });
            var world = (WorldState)Field(simulation, "world");
            ref var army = ref world.Armies[armyId - 1];
            ref var soldier = ref world.Soldiers[army.SoldierIds[0] - 1];
            soldier.MoveGoal = soldier.Position;
            soldier.Joining = false;
            soldier.TacticalRoute = false;
            return new HumanSetup { Simulation = simulation, SoldierId = army.SoldierIds[0], ArmyId = armyId, Goal = point };
        }

        private sealed class BuildingSetup
        {
            internal Battle Simulation;
            internal uint SoldierId, BuildingId;
        }

        private static BuildingSetup PrepareOutOfRangeBuildingScenario(bool enabled)
        {
            var scenario = LiveGameRules.Create(1295UL, true, true, false, false);
            scenario.Economy.UnitStuckFix = enabled;
            scenario.Economy.StartFood = 100000;
            scenario.Economy.StartWood = 100000;
            scenario.Economy.StartStone = 100000;
            var simulation = new Battle(scenario);
            var world = (WorldState)Field(simulation, "world");
            world.Economies[1].Wood = 100000;
            world.Economies[1].Stone = 100000;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            uint soldierId = world.Factions[0].ArmyIds.SelectMany(id => world.Armies[id - 1].SoldierIds)
                .First(id => world.Soldiers[id - 1].Alive);
            var soldier = world.Soldiers[soldierId - 1];
            int origin = -1;
            int ownCore = world.Map.Cell(world.Cores[world.Factions[0].CoreId - 1].Definition.Position);
            for (int radius = 12; radius < Math.Min(width, height); radius++)
            {
                int cx = ownCore % width, cz = ownCore / width;
                for (int z = Math.Max(0, cz - radius); z <= Math.Min(height - 1, cz + radius) && origin < 0; z++)
                    for (int x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) != radius) continue;
                        int cell = z * width + x;
                        if (world.Map.IsPassable(cell) && !world.Buildings.Any(b => b.Alive && b.OriginCell == cell)) { origin = cell; break; }
                    }
                if (origin >= 0) break;
            }
            Assert.That(origin, Is.GreaterThanOrEqualTo(0));
            var place = typeof(Battle).GetMethod("PlaceBuildingAt", Hidden, null,
                new[] { typeof(uint), typeof(BuildingKind), typeof(int), typeof(Facing), typeof(uint) }, null);
            place.Invoke(simulation, new object[] { 2u, BuildingKind.Wall, origin, Facing.North, 0u });
            int buildingIndex = world.BuildingCount - 1;
            world.Buildings[buildingIndex].Complete = true;
            world.Buildings[buildingIndex].Progress = 1;
            world.Buildings[buildingIndex].Hp = 1000;
            Array.Fill(world.Factions[0].VisibleCells, true);
            soldier.TargetKind = 4;
            soldier.TargetId = world.Buildings[buildingIndex].Id;
            soldier.MoveGoal = soldier.Position;
            world.Soldiers[soldierId - 1] = soldier;
            simulation.RebuildSpatialCombatIndexForTest();
            return new BuildingSetup { Simulation = simulation, SoldierId = soldierId, BuildingId = world.Buildings[buildingIndex].Id };
        }

        private static void InvokeFormation(HumanSetup setup)
        {
            var world = (WorldState)Field(setup.Simulation, "world");
            var army = world.Armies[setup.ArmyId - 1];
            var method = typeof(Battle).GetMethod("PrepareArmyFlowFormation", Hidden);
            method.Invoke(setup.Simulation, new object[] { army, setup.Goal });
        }

        private static SoldierState Current(JoinSetup setup)
        {
            var world = (WorldState)Field(setup.Simulation, "world");
            return world.Soldiers[setup.SoldierId - 1];
        }

        private static SoldierState Current(HumanSetup setup)
        {
            var world = (WorldState)Field(setup.Simulation, "world");
            return world.Soldiers[setup.SoldierId - 1];
        }

        private static SoldierState Current(BuildingSetup setup)
        {
            var world = (WorldState)Field(setup.Simulation, "world");
            return world.Soldiers[setup.SoldierId - 1];
        }

        private static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden).GetValue(owner);
    }
}
