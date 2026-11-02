using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class HumanOrderFlowTests
    {
        [Test]
        public void Extension92RoundTripsAndIsRemovedWhenHumanFlowIsOff()
        {
            var source = LiveGameRules.Create(1001UL, true, true, false, false);
            source.Economy.HumanOrderQuorumPermille = 251;
            source.Economy.HumanOrderWaitTicks = 21;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(e => e.Id == 92);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(12));
            Assert.That(decoded.Economy.HumanOrderFlow, Is.True);
            Assert.That(decoded.Economy.HumanOrderQuorumPermille, Is.EqualTo(251));
            Assert.That(decoded.Economy.HumanOrderWaitTicks, Is.EqualTo(21));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            source.Economy.HumanOrderFlow = false;
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(source)).Extensions.Any(e => e.Id == 92), Is.False);
        }

        [Test]
        public void HumanResolveUsesAcceptedPlusOneWhileOldPathKeepsFortyTickDelay()
        {
            var on = LiveGameRules.Create(1002UL, true, true, false, false);
            on.Economy.HumanOrderFlow = true;
            var onGateway = new CommandGateway(new Battle(on));
            onGateway.Submit(Intent(on));
            onGateway.Step();
            var onResolve = onGateway.Step().Single(i => i.Kind == InputKind.Resolve);
            Assert.That(onResolve.ApplyTick, Is.EqualTo(2));

            var aiScenario = LiveGameRules.Create(1005UL, true, true, false, false);
            var aiGateway = new CommandGateway(new Battle(aiScenario));
            var aiTarget = new ScopeKey(2, ScopeKind.Army, aiScenario.Factions[1].ArmyIds[0]);
            var aiOrder = new PolicyOrder(1, 1, CommandSource.Ai, aiTarget, PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, new SimPoint(Fix64.FromInt(80), Fix64.FromInt(80))), 50,
                new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0, 0,
                Array.Empty<PolicyVersion>(), 0, new Expiration(long.MaxValue, 240, ExpireFlags.ObservationTooOld));
            aiGateway.Propose(2, 1, new[] { aiOrder }, 10);
            var aiProposal = aiGateway.Step().Single(i => i.Kind == InputKind.Proposal);
            Assert.That(aiProposal.ApplyTick, Is.EqualTo(10));

            var off = LiveGameRules.Create(1003UL, true, true, false, false);
            off.Economy.HumanOrderFlow = false;
            var offGateway = new CommandGateway(new Battle(off));
            offGateway.Submit(Intent(off));
            offGateway.Step();
            var offResolve = offGateway.Step().Single(i => i.Kind == InputKind.Resolve);
            Assert.That(offResolve.ApplyTick, Is.EqualTo(40));
        }

        [Test]
        public void HumanFlowOffKeepsScenarioAndSimulationHashStable()
        {
            var source = LiveGameRules.Create(1004UL, true, true, false, false);
            source.Economy.HumanOrderFlow = false;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 100; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        // 11-02 test play: every new order sent the front soldiers back to the path start (the lowest soldier id), and a
        // re-click on the same spot rebuilt the path. With the flag on the front keeps closing in from the first ticks.
        [Test]
        public void HumanFocusDoesNotSendSoldiersBackAndReclicksKeepThePath()
        {
            var scenario = LiveGameRules.Create(1006UL, true, true, false, false);
            Assert.That(scenario.Economy.ArmyFlow && scenario.Economy.HumanOrderFlow, Is.True);
            var simulation = new Battle(scenario);
            uint armyId = scenario.Factions[0].ArmyIds[0];
            var target = new ScopeKey(1, ScopeKind.Army, armyId);
            var start = Soldiers(simulation, armyId);
            Assert.That(start.Length, Is.GreaterThan(0));
            var own = scenario.Cores[0].Position; var enemy = scenario.Cores[1].Position;
            var goal = new SimPoint(own.X + (enemy.X - own.X) / Fix64.FromInt(4), own.Z + (enemy.Z - own.Z) / Fix64.FromInt(4));
            ulong log = 0;
            Submit(simulation, ref log, 1, HumanOrder(simulation, 1, 1, target, goal));
            var before = Soldiers(simulation, armyId).ToDictionary(s => s.Id, s => Distance(s.X, s.Z, goal));
            double front = before.Values.Min();
            for (long tick = 2; tick <= 41; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var after = Soldiers(simulation, armyId);
            Assert.That(after.Count(s => before.ContainsKey(s.Id) && Distance(s.X, s.Z, goal) >= before[s.Id] + 3), Is.EqualTo(0),
                "soldiers walking away from the goal");
            Assert.That(after.Min(s => Distance(s.X, s.Z, goal)), Is.LessThan(front));

            int[] path = (int[])Field(Army(simulation, armyId), "Path");
            long next = 42;
            for (int click = 0; click < 10; click++)
            {
                var nudged = new SimPoint(goal.X + Fix64.FromRatio(click + 1, 100), goal.Z);
                Submit(simulation, ref log, next, HumanOrder(simulation, (ulong)(2 + click), next, target, nudged));
                for (long tick = next + 1; tick < next + 10; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
                next += 10;
                Assert.That((int[])Field(Army(simulation, armyId), "Path"), Is.SameAs(path), "click " + click);
            }
            for (; next <= 241; next++) simulation.Step(next, Array.Empty<ScheduledInput>());
            Assert.That(Soldiers(simulation, armyId).Min(s => Distance(s.X, s.Z, goal)), Is.LessThan(front - 20));
        }

        // 11-02: the advisor's fixed 2-second transmission is dropped with the flag on. A same-tick answer applies on the
        // next tick; with the flag off it still waits until StartedTick + 40.
        [TestCase(true, 1)]
        [TestCase(false, 40)]
        public void AdvisorAnswerAppliesOnTheNextTickWithTheFlag(bool flag, long delay)
        {
            var scenario = LiveGameRules.Create(1007UL, true, true, false, false);
            scenario.Economy.HumanOrderFlow = flag;
            var provider = new AnsweringProvider();
            var gateway = new CommandGateway(new Battle(scenario), provider);
            gateway.EnableAutonomous(new UserPolicyIntent(0, new ScopeKey(1, ScopeKind.All, 0), PolicyKind.MaintainReserve,
                default(PolicyGoal), 50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 100,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            for (int i = 0; i < 5 && !gateway.Inputs.Any(v => v.Kind == InputKind.Proposal && v.Orders.Count > 0); i++) gateway.Step();
            var proposal = gateway.Inputs.First(v => v.Kind == InputKind.Proposal && v.Orders.Count > 0);
            Assert.That(proposal.AcceptedTick - provider.StartedTick, Is.LessThan(5));
            Assert.That(proposal.ApplyTick, Is.EqualTo(flag ? proposal.AcceptedTick + delay : provider.StartedTick + delay));
        }

        private sealed class AnsweringProvider : IPolicyProvider
        {
            internal long StartedTick = -1;
            private readonly List<PolicyRequest> open = new List<PolicyRequest>();
            public void Request(PolicyRequest request) { if (StartedTick < 0) StartedTick = request.StartedTick; open.Add(request); }
            public IReadOnlyList<PolicyReply> Poll(long tick)
            {
                var replies = open.Select(r => new PolicyReply(r.RequestId, tick, new[]
                {
                    new PolicyOrder(0, 0, CommandSource.Ai, r.Scope, PolicyKind.MaintainReserve, default(PolicyGoal), 50,
                        new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 100, 0, Array.Empty<PolicyVersion>(), 0,
                        new Expiration(long.MaxValue, 0, ExpireFlags.None))
                }, ReasonCode.None)).ToArray();
                open.Clear();
                return replies;
            }
        }

        private const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

        private static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden).GetValue(owner);

        private static object Army(Battle simulation, uint id)
            => ((Array)Field(Field(simulation, "world"), "Armies")).GetValue((int)id - 1);

        private static (uint Id, double X, double Z)[] Soldiers(Battle simulation, uint armyId)
        {
            var soldiers = (Array)Field(Field(simulation, "world"), "Soldiers");
            return ((uint[])Field(Army(simulation, armyId), "SoldierIds")).Select(id => (Id: id, State: soldiers.GetValue((int)id - 1)))
                .Where(s => (bool)Field(s.State, "Alive"))
                .Select(s => { var p = (SimPoint)Field(s.State, "Position"); return (s.Id, p.X.Raw / 65536.0, p.Z.Raw / 65536.0); })
                .ToArray();
        }

        private static double Distance(double x, double z, SimPoint goal)
        {
            double dx = x - goal.X.Raw / 65536.0, dz = z - goal.Z.Raw / 65536.0;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static PolicyOrder HumanOrder(Battle simulation, ulong id, long tick, ScopeKey target, SimPoint point)
            => new PolicyOrder(id, 0, CommandSource.Human, target, PolicyKind.Focus, new PolicyGoal(GoalKind.Point, 0, point), 50,
                new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0, simulation.Revision(target),
                simulation.Versions(target).Where(v => !v.Scope.Equals(target)).ToArray(), tick - 1,
                new Expiration(long.MaxValue, 0, ExpireFlags.None));

        private static void Submit(Battle simulation, ref ulong log, long tick, PolicyOrder order)
        {
            var token = new PolicyOrder(order.CommandId, order.BatchId, order.Source, order.Target, order.Kind, order.Goal,
                order.Priority, order.AllowedLoss, order.End, order.ReservePermille, order.TargetRevision + 1, order.Parents,
                order.ObservedTick, order.Expiration);
            simulation.Step(tick, new[]
            {
                new ScheduledInput(++log, InputKind.Reserve, tick - 1, tick, order.CommandId, log, new[] { order }, long.MaxValue, ReasonCode.None),
                new ScheduledInput(++log, InputKind.Resolve, tick - 1, tick, order.CommandId, log, new[] { token }, long.MaxValue, ReasonCode.None),
            });
        }

        private static UserPolicyIntent Intent(ScenarioDefinition scenario)
        {
            uint army = scenario.Factions[0].ArmyIds[0];
            return new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, army), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, new SimPoint(Fix64.FromInt(80), Fix64.FromInt(80))), 50,
                new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None));
        }
    }
}
