using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ArmyFinalApproachTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private sealed class Scene
        {
            internal readonly Battle Simulation;
            internal readonly SimPoint Goal;

            internal Scene(Battle simulation, SimPoint goal)
            {
                Simulation = simulation;
                Goal = goal;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClearFinalPathApproachDoesNotStartJoining(bool armyFlow)
        {
            var scene = MakeScene(armyFlow, true, false);
            Prepare(scene.Simulation);

            foreach (object soldier in ArmyOneSoldiers(scene.Simulation))
            {
                if (!(bool)GetField(soldier, "Alive")) continue;
                Assert.That((bool)GetField(soldier, "Joining"), Is.False,
                    "A clear soldier at the final path cell must keep approaching the goal.");
                Assert.That(Same((SimPoint)GetField(soldier, "MoveGoal"), scene.Goal), Is.True);
            }
            Assert.That(scene.Simulation.ArmyFinalApproachSkippedJoins(1), Is.GreaterThan(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FlagOffPullsTheSameSoldierBackToThePathCentre(bool armyFlow)
        {
            // #543: the old rule this flag fixes. 11 m past the final centre with a clear line, the soldier joins.
            var scene = MakeScene(armyFlow, false, false);
            Prepare(scene.Simulation);
            Assert.That(ArmyOneSoldiers(scene.Simulation).Where(s => (bool)GetField(s, "Alive"))
                .All(s => (bool)GetField(s, "Joining")), Is.True);
            Assert.That(scene.Simulation.ArmyFinalApproachSkippedJoins(1), Is.EqualTo(0));
        }

        [Test]
        public void BlockedFinalGoalStillJoinsThePathCentre()
        {
            var scene = MakeScene(true, true, true);
            Prepare(scene.Simulation);

            Assert.That(ArmyOneSoldiers(scene.Simulation).Where(s => (bool)GetField(s, "Alive"))
                .All(s => (bool)GetField(s, "Joining")), Is.True);
            Assert.That(scene.Simulation.ArmyFinalApproachSkippedJoins(1), Is.EqualTo(0));
        }

        [Test]
        public void IntermediatePathCellStillJoinsWhenTheFlagIsOn()
        {
            var scene = MakeScene(true, true, false, intermediate: true);
            Prepare(scene.Simulation);

            Assert.That(ArmyOneSoldiers(scene.Simulation).Where(s => (bool)GetField(s, "Alive"))
                .All(s => (bool)GetField(s, "Joining")), Is.True);
            Assert.That(scene.Simulation.ArmyFinalApproachSkippedJoins(1), Is.EqualTo(0));
        }

        [Test]
        public void Extension102RoundTripsAndFlagOffAddsNoBytes()
        {
            var on = LiveGameRules.Create(9102UL, true, true, false, false);
            byte[] bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.ArmyFinalApproach, Is.True);
            Assert.That(decoded.Extensions.Single(e => e.Id == 102).Version, Is.EqualTo(1));
            Assert.That(decoded.Extensions.Single(e => e.Id == 102).Data.Length, Is.EqualTo(4));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = LiveGameRules.Create(9102UL, true, true, false, false);
            off.Economy.ArmyFinalApproach = false;
            var offDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(off));
            Assert.That(offDecoded.Extensions.Any(e => e.Id == 102), Is.False);
            Assert.That(offDecoded.Economy.ArmyFinalApproach, Is.False);
        }

        [Test]
        public void FlagOffKeepsTheCanonicalHashUnchangedForEveryTick()
        {
            var source = MakeScenario(false, false, false);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 100; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(Rts.Replay.ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(Rts.Replay.ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        private static Scene MakeScene(bool armyFlow, bool finalApproach, bool blockGoal, bool intermediate = false)
        {
            var scenario = MakeScenario(armyFlow, finalApproach, blockGoal);
            var simulation = new Battle(scenario);
            object world = GetField(simulation, "world");
            object map = GetField(world, "Map");
            var grid = (GridMap)map;
            var goal = Point(120, 65);
            int finalCell = grid.Cell(Point(90, 65));
            int nextCell = grid.Cell(Point(94, 65));

            Array armies = (Array)GetField(world, "Armies");
            object army = armies.GetValue(0);
            SetField(army, "Policy", PolicyKind.Focus);
            SetField(army, "Goal", new PolicyGoal(GoalKind.Point, 0, goal));
            SetField(army, "Path", intermediate ? new[] { finalCell, nextCell } : new[] { grid.Cell(Point(50, 65)), finalCell });
            SetField(army, "PathCursor", intermediate ? 0 : 1);
            SetField(army, "PathGoal", goal);
            SetField(army, "HasPathGoal", true);
            SetField(army, "PathImpossible", false);
            armies.SetValue(army, 0);

            Array soldiers = (Array)GetField(world, "Soldiers");
            for (int i = 0; i < soldiers.Length; i++)
            {
                object soldier = soldiers.GetValue(i);
                var initial = (SoldierDefinition)GetField(soldier, "Initial");
                if (!initial.Alive || initial.FactionId != 1 || initial.ArmyId != 1) continue;
                SetField(soldier, "Position", Point(101, 65));
                SetField(soldier, "MoveGoal", goal);
                SetField(soldier, "Joining", false);
                SetField(soldier, "TacticalRoute", false);
                soldiers.SetValue(soldier, i);
            }
            return new Scene(simulation, goal);
        }

        private static ScenarioDefinition MakeScenario(bool armyFlow, bool finalApproach, bool blockGoal)
        {
            var scenario = WeekOneScenario.Create();
            scenario.Economy.Enabled = true;
            scenario.Economy.LargeEconomy = true;
            scenario.Economy.ArmyFlow = armyFlow;
            scenario.Economy.HumanOrderFlow = false;
            scenario.Economy.ArmyFinalApproach = finalApproach;
            scenario.Rules.CoreReinforcementIntervalTicks = int.MaxValue;
            scenario.Rules.OutpostReinforcementIntervalTicks = int.MaxValue;
            if (blockGoal) scenario.Map.BlockedCellIds = new[] { 32 * scenario.Map.WidthCells + 60 };
            return scenario;
        }

        private static void Prepare(Battle simulation)
            => typeof(Battle).GetMethod("PrepareArmyPaths", Hidden).Invoke(simulation, null);

        private static Array Soldiers(Battle simulation)
            => (Array)GetField(GetField(simulation, "world"), "Soldiers");

        private static System.Collections.Generic.IEnumerable<object> ArmyOneSoldiers(Battle simulation)
            => Soldiers(simulation).Cast<object>().Where(s =>
            {
                var initial = (SoldierDefinition)GetField(s, "Initial");
                return initial.FactionId == 1 && initial.ArmyId == 1;
            });

        private static object GetField(object instance, string name)
            => instance.GetType().GetField(name, Hidden).GetValue(instance);

        private static void SetField(object instance, string name, object value)
            => instance.GetType().GetField(name, Hidden).SetValue(instance, value);

        private static SimPoint Point(int x, int z)
            => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        private static bool Same(SimPoint left, SimPoint right) => left.X.Raw == right.X.Raw && left.Z.Raw == right.Z.Raw;

    }
}
