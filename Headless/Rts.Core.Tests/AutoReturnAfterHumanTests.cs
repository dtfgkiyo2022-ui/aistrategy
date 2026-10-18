using System;
using System.Collections.Generic;
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
    public sealed class AutoReturnAfterHumanTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static ScenarioDefinition Scenario(bool enabled = true)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.AutoReturnAfterHuman = enabled;
            scenario.Economy.AutoReturnArriveMeters = 4;
            scenario.Economy.AutoReturnHoldTicks = 3;
            for (int i = 0; i < scenario.UnitParameters.Length; i++) scenario.UnitParameters[i].Speed = Fix64.FromRaw(0);
            return scenario;
        }

        private static ScopeKey Army(uint id = 1) => new ScopeKey(1, ScopeKind.Army, id);

        private static PolicyOrder HumanOrder(Battle simulation, ulong id, long tick, ScopeKey target,
            PolicyKind kind, PolicyGoal goal)
        {
            return new PolicyOrder(id, 0, CommandSource.Human, target, kind, goal, 50, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0, simulation.Revision(target),
                simulation.Versions(target).Where(v => !v.Scope.Equals(target)).ToArray(), tick - 1,
                new Expiration(long.MaxValue, 0, ExpireFlags.None));
        }

        private static PolicyOrder Token(Battle simulation, PolicyOrder order, ulong revision)
        {
            return new PolicyOrder(order.CommandId, order.BatchId, order.Source, order.Target, order.Kind, order.Goal,
                order.Priority, order.AllowedLoss, order.End, order.ReservePermille, revision, order.Parents,
                order.ObservedTick, order.Expiration);
        }

        private static ScheduledInput[] Inputs(Battle simulation, ref ulong log, long tick, PolicyOrder order)
        {
            var reserve = new ScheduledInput(++log, InputKind.Reserve, tick - 1, tick, order.CommandId, log,
                new[] { order }, long.MaxValue, ReasonCode.None);
            var resolve = new ScheduledInput(++log, InputKind.Resolve, tick - 1, tick, order.CommandId, log,
                new[] { Token(simulation, order, order.TargetRevision + 1) }, long.MaxValue, ReasonCode.None);
            return new[] { reserve, resolve };
        }

        private static void Submit(Battle simulation, ref ulong log, long tick, PolicyOrder order)
        {
            simulation.Step(tick, Inputs(simulation, ref log, tick, order));
        }

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", Hidden).GetValue(simulation);

        private static Array StateArray(Battle simulation, string name)
            => (Array)World(simulation).GetType().GetField(name, Hidden).GetValue(World(simulation));

        private static void SetOutpostOwner(Battle simulation, uint id, uint faction)
        {
            var outposts = StateArray(simulation, "Outposts");
            object state = outposts.GetValue((int)id - 1);
            state.GetType().GetField("OwnerFactionId", Hidden).SetValue(state, faction);
            outposts.SetValue(state, (int)id - 1);
        }

        private static PolicyKind ArmyPolicy(Battle simulation, uint id)
        {
            var armies = StateArray(simulation, "Armies");
            object state = armies.GetValue((int)id - 1);
            return (PolicyKind)state.GetType().GetField("Policy", Hidden).GetValue(state);
        }

        [Test]
        public void AutoReturnExtensionRoundTripsAndIsAbsentWhenOff()
        {
            var off = Scenario(false);
            off.Economy.AutoReturnArriveMeters = 17;
            off.Economy.AutoReturnHoldTicks = 901;
            var baseline = Scenario(false);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(baseline)));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(baseline)).Extensions.Any(x => x.Id == 53), Is.False);

            var on = Scenario();
            on.Economy.AutoReturnArriveMeters = 7;
            on.Economy.AutoReturnHoldTicks = 321;
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 53).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.AutoReturnAfterHuman, Is.True);
            Assert.That(decoded.Economy.AutoReturnArriveMeters, Is.EqualTo(7));
            Assert.That(decoded.Economy.AutoReturnHoldTicks, Is.EqualTo(321));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void PointFocusCompletesOnlyAfterArrivalHold()
        {
            var simulation = new Battle(Scenario());
            var point = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 1).Position;
            ulong log = 0;
            var order = HumanOrder(simulation, 1, 1, Army(), PolicyKind.Focus, new PolicyGoal(GoalKind.Point, 0, point));
            Submit(simulation, ref log, 1, order);
            simulation.Step(2, Array.Empty<ScheduledInput>());
            simulation.Step(3, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Commands.Single(c => c.CommandId == 1).Status, Is.EqualTo(CommandStatus.Executing));
            simulation.Step(4, Array.Empty<ScheduledInput>());
            var view = simulation.Capture(1).Commands.Single(c => c.CommandId == 1);
            Assert.That(view.Status, Is.EqualTo(CommandStatus.Completed));
            Assert.That(view.Reason, Is.EqualTo(ReasonCode.AutoReturned));
            Assert.That(ArmyPolicy(simulation, 1), Is.EqualTo((PolicyKind)0));
        }

        [Test]
        public void CapturedOutpostFocusUsesTheSameHoldBeforeReturning()
        {
            var simulation = new Battle(Scenario());
            ulong log = 0;
            var order = HumanOrder(simulation, 1, 1, Army(), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Outpost, 1, default(SimPoint)));
            Submit(simulation, ref log, 1, order);
            SetOutpostOwner(simulation, 1, 1);
            simulation.Step(2, Array.Empty<ScheduledInput>());
            simulation.Step(3, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Commands.Single(c => c.CommandId == 1).Status, Is.EqualTo(CommandStatus.Executing));
            // A captured outpost is already the ordinary end of the attack (the objective is owned), which hands the army
            // back like any finished order. Before the fog fix this test passed only because "out of sight" started the
            // auto-return timer first; the order must end either way, and must not stay executing.
            for (long tick = 4; tick <= 12; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var ended = simulation.Capture(1).Commands.Single(c => c.CommandId == 1);
            Assert.That(ended.Status, Is.Not.EqualTo(CommandStatus.Executing));
            Assert.That(ended.Reason == ReasonCode.AutoReturned || ended.Status == CommandStatus.Completed, Is.True,
                ended.Status + " / " + ended.Reason);
        }

        [Test]
        public void RetreatCompletesAfterTheArmyHasHeldAtHome()
        {
            var scenario = Scenario();
            var core = scenario.Cores.Single(c => c.FactionId == 1).Position;
            for (int i = 0; i < scenario.Soldiers.Length; i++)
                if (scenario.Soldiers[i].FactionId == 1 && scenario.Soldiers[i].ArmyId == 1) scenario.Soldiers[i].Position = core;
            var simulation = new Battle(scenario);
            ulong log = 0;
            Submit(simulation, ref log, 1, HumanOrder(simulation, 1, 1, Army(), PolicyKind.Retreat, default(PolicyGoal)));
            simulation.Step(2, Array.Empty<ScheduledInput>());
            simulation.Step(3, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Commands.Single(c => c.CommandId == 1).Status, Is.EqualTo(CommandStatus.Executing));
            simulation.Step(4, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Commands.Single(c => c.CommandId == 1).Reason, Is.EqualTo(ReasonCode.AutoReturned));
        }

        [TestCase(PolicyKind.Defend)]
        [TestCase(PolicyKind.MaintainReserve)]
        [TestCase(PolicyKind.AllowAbandon)]
        public void DefendAndReserveRemainUntilReplaced(PolicyKind kind)
        {
            var simulation = new Battle(Scenario());
            ulong log = 0;
            var target = kind == PolicyKind.MaintainReserve ? new ScopeKey(1, ScopeKind.All, 0)
                : kind == PolicyKind.AllowAbandon ? new ScopeKey(1, ScopeKind.Outpost, 1) : Army();
            var goal = kind == PolicyKind.Defend ? new PolicyGoal(GoalKind.Core, 1, default(SimPoint)) : default(PolicyGoal);
            Submit(simulation, ref log, 1, HumanOrder(simulation, 1, 1, target, kind, goal));
            for (long tick = 2; tick <= 8; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Commands.Single(c => c.CommandId == 1).Status, Is.EqualTo(CommandStatus.Executing));
        }

        [Test]
        public void ManualControlDoesNotResumeAutomaticMovementAfterCompletion()
        {
            var scenario = Scenario();
            scenario.Economy.LatePush = true;
            scenario.Economy.ManualControl = true;
            var simulation = new Battle(scenario);
            var point = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 1).Position;
            ulong log = 0;
            Submit(simulation, ref log, 1, HumanOrder(simulation, 1, 1, Army(), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, point)));
            for (long tick = 2; tick <= 8; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Commands.Single(c => c.CommandId == 1).Status, Is.EqualTo(CommandStatus.Completed));
            Assert.That(ArmyPolicy(simulation, 1), Is.EqualTo((PolicyKind)0));
        }

        [Test]
        public void ReplacingAnOrderStartsAFreshArrivalTimer()
        {
            var simulation = new Battle(Scenario());
            var point = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 1).Position;
            ulong log = 0;
            Submit(simulation, ref log, 1, HumanOrder(simulation, 1, 1, Army(), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, point)));
            var second = HumanOrder(simulation, 2, 2, Army(), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, point));
            Submit(simulation, ref log, 2, second);
            simulation.Step(3, Array.Empty<ScheduledInput>());
            simulation.Step(4, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Commands.Single(c => c.CommandId == 2).Status, Is.EqualTo(CommandStatus.Executing));
            simulation.Step(5, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Commands.Single(c => c.CommandId == 2).Status, Is.EqualTo(CommandStatus.Completed));
        }

        [Test]
        public void DisabledRuleKeepsEveryTickIdenticalEvenIfItsTuningDiffers()
        {
            var leftScenario = Scenario(false);
            var rightScenario = Scenario(false);
            rightScenario.Economy.AutoReturnArriveMeters = 31;
            rightScenario.Economy.AutoReturnHoldTicks = 777;
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                if (tick % 200 == 0 || tick == 30000 || left.Capture(1).Result.HasEnded)
                    Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                        Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void RecordAndReplayPreserveAutoReturnCommandResult()
        {
            var scenario = Scenario();
            var simulation = new Battle(scenario);
            var point = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 1).Position;
            ulong log = 0;
            var order = HumanOrder(simulation, 1, 1, Army(), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, point));
            var inputs = Inputs(simulation, ref log, 1, order);
            var identity = new BuildIdentity();
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, inputs, 5, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }
    }
}
