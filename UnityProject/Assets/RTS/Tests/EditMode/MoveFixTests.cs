using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class MoveFixTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

        private static SimPoint P(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        private static ScenarioDefinition Peace(uint armyId, bool economy)
        {
            var scenario = WeekTwoScenario.Create();
            scenario.Soldiers = scenario.Soldiers.Where(s => s.ArmyId == armyId).ToArray();
            scenario.Rules.CaptureDurationTicks = int.MaxValue;
            scenario.Rules.CoreReinforcementIntervalTicks = int.MaxValue;
            scenario.Rules.OutpostReinforcementIntervalTicks = int.MaxValue;
            for (int i = 0; i < scenario.UnitParameters.Length; i++) scenario.UnitParameters[i].Damage = 0;
            scenario.Economy.Enabled = economy;
            return scenario;
        }

        private static Array WorldArray(Battle simulation, string name)
        {
            var world = typeof(Battle).GetField("world", Hidden).GetValue(simulation);
            return (Array)world.GetType().GetField(name, Hidden).GetValue(world);
        }

        private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);

        private static ScheduledInput Focus(long tick, uint army, SimPoint goal)
            => PolicyDecisionTests.Order(tick, 1, ScopeKind.Army, army, PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, goal));

        [TestCase(false)]
        [TestCase(true)]
        public void HumanFocusMovesTheSelectedArmyWhenEconomyIsOnOrOff(bool economy)
        {
            var simulation = new Battle(Peace(1, economy));
            var goal = P(128, 32);
            var before = simulation.Capture(1).Units.Where(u => u.IsOwn).Select(u => u.Position).ToArray();
            CommandTestInput.Step(simulation, 1, new[] { Focus(1, 1, goal) });
            for (int tick = 2; tick <= 12000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());

            var after = simulation.Capture(1).Units.Where(u => u.IsOwn).ToArray();
            Assert.That(after.Length, Is.GreaterThan(0));
            Assert.That(after.Min(u => DistanceSquared(u.Position, goal)), Is.LessThan(DistanceSquared(before[0], goal)));
        }

        [Test]
        public void DirectPathMovementRoundTripsAsExtension45()
        {
            var scenario = Peace(1, false);
            for (int i = 0; i < scenario.Soldiers.Length; i++)
            {
                var soldier = scenario.Soldiers[i];
                soldier.Kind = UnitKind.Scout;
                soldier.Hp = 40;
                scenario.Soldiers[i] = soldier;
            }
            scenario.Economy.DirectPathMovement = true;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);

            Assert.That(decoded.Economy.DirectPathMovement, Is.True);
            Assert.That(decoded.Extensions.Any(extension => extension.Id == 45 && extension.Version == 1), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void DirectPathMovementUsesBothAxesForAnOpenDiagonalScoutRoute()
        {
            var scenario = Peace(1, false);
            for (int i = 0; i < scenario.Soldiers.Length; i++)
            {
                var soldier = scenario.Soldiers[i];
                soldier.Kind = UnitKind.Scout;
                soldier.Hp = 40;
                scenario.Soldiers[i] = soldier;
            }
            scenario.Economy.DirectPathMovement = true;
            var simulation = new Battle(scenario);
            var before = simulation.Capture(1).Units.Where(u => u.IsOwn && u.Kind == UnitKind.Scout).ToArray();
            CommandTestInput.Step(simulation, 1, new[] { Focus(1, 1, P(100, 100)) });
            simulation.Step(2, Array.Empty<ScheduledInput>());
            var after = simulation.Capture(1).Units.Where(u => u.IsOwn && u.Kind == UnitKind.Scout).ToArray();

            Assert.That(after.Any(unit => unit.Position.X.Raw != before.First(v => v.Id == unit.Id).Position.X.Raw
                && unit.Position.Z.Raw != before.First(v => v.Id == unit.Id).Position.Z.Raw), Is.True);
            var army = WorldArray(simulation, "Armies").GetValue(0);
            Assert.That(Field<int[]>(army, "Path").Length, Is.EqualTo(2));
        }

        [Test]
        public void DirectPathMovementOffKeepsTheExistingCellRouteAndNoExtension45()
        {
            var scenario = Peace(1, false);
            for (int i = 0; i < scenario.Soldiers.Length; i++)
            {
                var soldier = scenario.Soldiers[i];
                soldier.Kind = UnitKind.Scout;
                soldier.Hp = 40;
                scenario.Soldiers[i] = soldier;
            }
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.DirectPathMovement, Is.False);
            Assert.That(decoded.Extensions.Any(extension => extension.Id == 45), Is.False);

            var simulation = new Battle(scenario);
            CommandTestInput.Step(simulation, 1, new[] { Focus(1, 1, P(100, 100)) });
            var army = WorldArray(simulation, "Armies").GetValue(0);
            Assert.That(Field<int[]>(army, "Path").Length, Is.GreaterThan(2));
        }

        [Test]
        public void DirectPathMovementOffMatchesTheEncodedScenarioFor30000Ticks()
        {
            var source = Peace(1, false);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var baseline = new Battle(source);
            var roundTrip = new Battle(decoded);
            for (int tick = 1; tick <= 30000; tick++)
            {
                baseline.Step(tick, Array.Empty<ScheduledInput>());
                roundTrip.Step(tick, Array.Empty<ScheduledInput>());
                CommandTestInput.AssertCanonicalEqual(baseline.CaptureDiagnostic(), roundTrip.CaptureDiagnostic(), "tick=" + tick);
            }
        }

        [Test]
        public void DirectPathMovementOnIsReplayable()
        {
            var scenario = Peace(1, false);
            scenario.Economy.DirectPathMovement = true;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, P(100, 100)), 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            for (int i = 0; i < 400; i++) gateway.Step();

            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, gateway.Inputs, simulation.Capture(1).Tick, new BuildIdentity());
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        [Test]
        public void ReplacingAGroupMoveLeavesOneLiveCommand()
        {
            var simulation = new Battle(Peace(1, false));
            CommandTestInput.Step(simulation, 1, new[] { Focus(1, 1, P(128, 32)) });
            CommandTestInput.Step(simulation, 2, new[] { Focus(2, 1, P(100, 100)) });

            var commands = simulation.Capture(1).Commands.Where(c => c.Target.Kind == ScopeKind.Army && c.Target.Id == 1
                && c.Kind == PolicyKind.Focus).ToArray();
            Assert.That(commands.Count(c => c.Status == CommandStatus.Executing || c.Status == CommandStatus.Pending), Is.EqualTo(1));
            Assert.That(commands.Count(c => c.Status >= CommandStatus.Completed), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void NewerArmyCommandWinsArrowSelection()
        {
            var older = new CommandView(1, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, P(80, 80)), CommandStatus.Executing, 10, 12, ReasonCode.None);
            var newer = new CommandView(2, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, P(100, 100)), CommandStatus.Pending, 11, 13, ReasonCode.None);

            Assert.That(BattlefieldArrowRules.IsNewer(newer, older), Is.True);
            Assert.That(BattlefieldArrowRules.IsNewer(older, newer), Is.False);
        }

        private static long DistanceSquared(SimPoint a, SimPoint b)
        {
            long x = a.X.Raw - b.X.Raw;
            long z = a.Z.Raw - b.Z.Raw;
            return checked(x * x + z * z);
        }
    }
}
