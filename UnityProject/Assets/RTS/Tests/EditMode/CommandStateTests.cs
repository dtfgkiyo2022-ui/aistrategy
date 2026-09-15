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

namespace Rts.Tests.EditMode
{
    public sealed class CommandStateTests
    {
        private static ScopeKey North => new ScopeKey(1, ScopeKind.Army, 1);
        private static ScopeKey South => new ScopeKey(1, ScopeKind.Army, 2);
        private static ScopeKey All => new ScopeKey(1, ScopeKind.All, 0);
        private static ScopeKey Outpost => new ScopeKey(1, ScopeKind.Outpost, 1);
        private static PolicyGoal Goal(uint id = 1) => new PolicyGoal(GoalKind.Outpost, id, default);
        private static ScenarioDefinition Frozen()
        {
            var s = WeekTwoScenario.Create();
            foreach (var p in s.UnitParameters.Select((v, i) => i)) { s.UnitParameters[p].Speed = Fix64.FromInt(0); s.UnitParameters[p].Damage = 0; }
            return s;
        }
        private sealed class Harness
        {
            internal Battle Sim;
            internal long Tick;
            internal ulong Index, Id;
            internal List<ScheduledInput> Log = new List<ScheduledInput>();
            internal Harness(ScenarioDefinition scenario = null) { Sim = new Battle(scenario ?? Frozen()); }
            internal PolicyOrder Order(ScopeKey scope, PolicyKind kind = PolicyKind.Focus, CommandSource source = CommandSource.Human,
                EndKind end = EndKind.UntilReplaced, long endTick = 0, ushort loss = 1000, ulong batch = 0, long valid = long.MaxValue)
            {
                return new PolicyOrder(++Id, batch, source, scope, kind, kind == PolicyKind.Focus || kind == PolicyKind.Defend || kind == PolicyKind.Scout ? Goal() : default,
                    source == CommandSource.Ai ? (byte)100 : (byte)1, new LossBudget(loss), new EndCondition(end, endTick),
                    kind == PolicyKind.MaintainReserve ? (ushort)200 : (ushort)0, Sim.Revision(scope),
                    Sim.Versions(scope).Where(v => !v.Scope.Equals(scope)).ToArray(), Tick,
                    new Expiration(valid, 240, source == CommandSource.Ai ? ExpireFlags.ObservationTooOld : ExpireFlags.None));
            }
            internal static PolicyOrder Version(PolicyOrder o, ulong version) => new PolicyOrder(o.CommandId, o.BatchId, o.Source, o.Target, o.Kind, o.Goal,
                o.Priority, o.AllowedLoss, o.End, o.ReservePermille, version, o.Parents, o.ObservedTick, o.Expiration);
            internal ScheduledInput Input(InputKind kind, PolicyOrder[] orders, long apply = 0, ulong request = 0, long deadline = long.MaxValue) =>
                new ScheduledInput(++Index, kind, Tick, apply == 0 ? Tick + 1 : apply, request == 0 ? orders[0].CommandId : request, Index, orders, deadline, ReasonCode.None);
            internal void Step(params ScheduledInput[] inputs) { Sim.Step(++Tick, inputs); Log.AddRange(inputs); }
            internal void Until(long tick) { while (Tick < tick) Step(); }
            internal PolicyOrder Human(ScopeKey scope, PolicyKind kind = PolicyKind.Focus, int wait = 1, EndKind end = EndKind.UntilReplaced, long endTick = 0, ushort loss = 1000, long valid = long.MaxValue)
            {
                var o = Order(scope, kind, end: end, endTick: endTick, loss: loss, valid: valid);
                var token = Version(o, o.TargetRevision + 1);
                Step(Input(InputKind.Reserve, new[] { o }), Input(InputKind.Resolve, new[] { token }, Tick + wait));
                return token;
            }
            internal CommandView View(PolicyOrder o) => Sim.Capture(o.Target.FactionId).Commands.Single(c => c.CommandId == o.CommandId);
            internal string Field(string name) => DiagnosticComparison.Fields(Sim.CaptureDiagnostic()).Single(p => p.Key == name).Value;
        }
        [Test]
        public void OldAiReplyDuringReservationCannotInvalidateHumanToken()
        {
            var h = new Harness(); var ai = h.Order(North, source: CommandSource.Ai); var human = h.Order(North);
            h.Step(h.Input(InputKind.Reserve, new[] { human }));
            h.Step(h.Input(InputKind.Proposal, new[] { ai }));
            Assert.That(h.View(ai).Status, Is.EqualTo(CommandStatus.Expired));
            Assert.That(h.Sim.Revision(North), Is.EqualTo(1));
            h.Step(h.Input(InputKind.Resolve, new[] { Harness.Version(human, 1) }));
            Assert.That(h.View(human).Status, Is.EqualTo(CommandStatus.Executing));
        }
        [Test]
        public void AiAlreadyPendingIsRecheckedAfterReservation()
        {
            var h = new Harness(); var ai = h.Order(North, source: CommandSource.Ai);
            h.Step(h.Input(InputKind.Proposal, new[] { ai }, 5));
            var human = h.Order(North); h.Step(h.Input(InputKind.Reserve, new[] { human })); h.Until(5);
            Assert.That(h.View(ai).Status, Is.EqualTo(CommandStatus.Expired));
            Assert.That(h.View(human).Status, Is.EqualTo(CommandStatus.Interpreting));
        }
        [Test]
        public void PendingCancellationRemovesFutureApplicationAndBumpsRevision()
        {
            var h = new Harness(); var o = h.Human(North, wait: 8); ulong revision = h.Sim.Revision(North);
            h.Step(h.Input(InputKind.Cancel, Array.Empty<PolicyOrder>(), request: o.CommandId)); h.Until(9);
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Cancelled));
            Assert.That(h.Sim.Revision(North), Is.GreaterThan(revision));
        }
        [Test]
        public void NewGlobalReplacesOldLocalAndNewLocalIsExceptionWithoutResurrection()
        {
            var h = new Harness(); var north = h.Human(North); var all = h.Human(All, PolicyKind.Retreat);
            Assert.That(h.View(north).Status, Is.EqualTo(CommandStatus.Cancelled));
            var local = h.Human(North);
            Assert.That(h.View(all).Status, Is.EqualTo(CommandStatus.Executing));
            Assert.That(h.Field("Commands[Army=1].Policy"), Is.EqualTo("1"));
            Assert.That(h.Field("Commands[Army=2].Policy"), Is.EqualTo("3"));
            h.Step(h.Input(InputKind.Cancel, Array.Empty<PolicyOrder>(), request: local.CommandId));
            Assert.That(h.Field("Commands[Army=1].Policy"), Is.EqualTo("0"));
            Assert.That(h.Field("Commands[Army=2].Policy"), Is.EqualTo("3"));
        }
        [Test]
        public void IndependentFieldsCoexistAndHighPriorityAiCannotOverrideHuman()
        {
            var h = new Harness(); var reserve = h.Human(All, PolicyKind.MaintainReserve); var human = h.Human(North);
            var ai = h.Order(North, PolicyKind.Retreat, CommandSource.Ai); h.Step(h.Input(InputKind.Proposal, new[] { ai }));
            Assert.That(h.View(reserve).Status, Is.EqualTo(CommandStatus.Executing));
            Assert.That(h.View(human).Status, Is.EqualTo(CommandStatus.Executing));
            Assert.That(h.View(ai).Status, Is.EqualTo(CommandStatus.Expired));
        }
        [Test]
        public void NorthernChangeDoesNotInvalidateSouthernDecision()
        {
            var h = new Harness(); var ai = h.Order(South, source: CommandSource.Ai); h.Human(North);
            h.Step(h.Input(InputKind.Proposal, new[] { ai }));
            Assert.That(h.View(ai).Status, Is.EqualTo(CommandStatus.Executing));
        }
        [Test]
        public void UsedOutpostDependencyInvalidatesArmyProposal()
        {
            var h = new Harness(); h.Human(Outpost, PolicyKind.AllowAbandon);
            var ai = h.Order(North, source: CommandSource.Ai);
            Assert.That(ai.Parents.Any(p => p.Scope.Equals(Outpost)), Is.True);
            h.Human(Outpost, PolicyKind.AllowAbandon); h.Step(h.Input(InputKind.Proposal, new[] { ai }));
            Assert.That(h.View(ai).Status, Is.EqualTo(CommandStatus.Expired));
        }
        [Test]
        public void AiCannotReserve()
        {
            var h = new Harness(); var o = h.Order(North, source: CommandSource.Ai); h.Step(h.Input(InputKind.Reserve, new[] { o }));
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Impossible)); Assert.That(h.Sim.Revision(North), Is.Zero);
        }
        [Test]
        public void BatchWithUnreachableMemberAppliesNothing()
        {
            var h = new Harness(); var a = h.Order(North, batch: 44); var b = h.Order(South, batch: 44);
            b = new PolicyOrder(b.CommandId, b.BatchId, b.Source, b.Target, b.Kind,
                new PolicyGoal(GoalKind.Point, 0, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(64))), b.Priority, b.AllowedLoss, b.End, 0, 0, b.Parents, b.ObservedTick, b.Expiration);
            h.Step(h.Input(InputKind.Reserve, new[] { a, b }), h.Input(InputKind.Resolve, new[] { Harness.Version(a, 1), Harness.Version(b, 1) }, request: a.CommandId));
            Assert.That(h.View(a).Status, Is.EqualTo(CommandStatus.Impossible)); Assert.That(h.View(b).Reason, Is.EqualTo(ReasonCode.NoPath));
            Assert.That(h.Field("Commands[Army=1].Policy"), Is.EqualTo("0"));
        }
        [Test]
        public void BatchCancellationAndExpirationAreAtomic()
        {
            var h = new Harness(); var a = h.Order(North, batch: 7); var b = h.Order(South, batch: 7);
            h.Step(h.Input(InputKind.Reserve, new[] { a, b }), h.Input(InputKind.Resolve, new[] { Harness.Version(a, 1), Harness.Version(b, 1) }, 6, a.CommandId));
            h.Step(h.Input(InputKind.Cancel, Array.Empty<PolicyOrder>(), request: a.CommandId)); h.Until(6);
            Assert.That(h.View(a).Status, Is.EqualTo(CommandStatus.Cancelled)); Assert.That(h.View(b).Status, Is.EqualTo(CommandStatus.Cancelled));
        }
        [Test]
        public void TerminalLateReplyIsRecordedWithoutSecondExecution()
        {
            var h = new Harness(); var o = h.Human(North, end: EndKind.AtTick, endTick: 1);
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Completed)); var rev = h.Sim.Revision(North);
            h.Step(h.Input(InputKind.Resolve, new[] { o }));
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Completed)); Assert.That(h.Sim.Revision(North), Is.EqualTo(rev));
            Assert.That(h.Sim.Capture(1).Events.Single().Reason, Is.EqualTo(ReasonCode.StaleVersion));
        }
        [Test]
        public void CancellationWinsOverExpirationAndCompletion()
        {
            var h = new Harness(); var o = h.Human(North, end: EndKind.AtTick, endTick: 2, valid: 1);
            h.Step(h.Input(InputKind.Cancel, Array.Empty<PolicyOrder>(), request: o.CommandId));
            Assert.That(h.View(o).Reason, Is.EqualTo(ReasonCode.UserCancelled));
        }
        [Test]
        public void DeadlineAndPolicyLifetimeAreSeparate()
        {
            var h = new Harness(); var o = h.Order(North);
            h.Step(h.Input(InputKind.Reserve, new[] { o }, deadline: 2)); h.Until(2);
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Interpreting)); h.Step();
            Assert.That(h.View(o).Reason, Is.EqualTo(ReasonCode.Deadline));
        }
        [Test]
        public void ReturnToAutoCancelsHumanPoliciesAndWaitingCommands()
        {
            var h = new Harness(); var a = h.Human(North); var b = h.Human(South, wait: 8);
            var reset = h.Human(All, PolicyKind.ReturnToAuto); h.Until(10);
            Assert.That(h.View(reset).Status, Is.EqualTo(CommandStatus.Completed));
            Assert.That(h.View(a).Status, Is.EqualTo(CommandStatus.Cancelled)); Assert.That(h.View(b).Status, Is.EqualTo(CommandStatus.Cancelled));
        }
        [TestCase(PolicyKind.AllowAbandon)] [TestCase(PolicyKind.MaintainReserve)] [TestCase(PolicyKind.Defend)] [TestCase(PolicyKind.Scout)]
        public void CatalogPoliciesAreHeldAndMovementPoliciesAreComposed(PolicyKind kind)
        {
            var h = new Harness(); var scope = kind == PolicyKind.AllowAbandon ? Outpost : kind == PolicyKind.MaintainReserve ? All : kind == PolicyKind.Scout ? new ScopeKey(1, ScopeKind.Army, 4) : North;
            var o = h.Human(scope, kind);
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Executing));
            Assert.That(h.Field("Commands[Army=1].Policy"), Is.EqualTo(kind == PolicyKind.Defend ? "5" : "0"));
        }
        [Test]
        public void AllDeadIsImpossibleRatherThanVacuousArrival()
        {
            var s = Frozen(); s.UnitParameters[0].Damage = 100;
            s.Soldiers = new[] {
                new SoldierDefinition { Id=1,FactionId=1,ArmyId=1,Kind=UnitKind.Infantry,Alive=true,Hp=10,Position=new SimPoint(Fix64.FromInt(100),Fix64.FromInt(96)) },
                new SoldierDefinition { Id=2,FactionId=2,ArmyId=5,Kind=UnitKind.Infantry,Alive=true,Hp=10,Position=new SimPoint(Fix64.FromInt(101),Fix64.FromInt(96)) } };
            var h = new Harness(s); var o = h.Human(North, end: EndKind.Arrived);
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Impossible)); Assert.That(h.View(o).Reason, Is.EqualTo(ReasonCode.EmptyArmy));
        }
        [Test]
        public void StartIdsAndDenominatorDoNotGrowWithReinforcement()
        {
            var scenario = Frozen();
            scenario.Armies[2].Capacity = 2; // Core fallback fills north after its reserve is full.
            var h = new Harness(scenario); var o = h.Human(North, end: EndKind.LossReached, loss: 500);
            string prefix = "CommandStates[" + o.CommandId + "].Armies[1].";
            int n0 = int.Parse(h.Field(prefix + "N0"));
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var world = typeof(Battle).GetField("world", flags).GetValue(h.Sim);
            var armies = (Array)world.GetType().GetField("Armies", flags).GetValue(world);
            object army = armies.GetValue(0); var idsField = army.GetType().GetField("SoldierIds", flags);
            uint[] ids = (uint[])idsField.GetValue(army);
            h.Until(100);
            Assert.That(h.Sim.Capture(1).Events.Any(e => e.Kind == EventKind.Reinforcement), Is.True);
            Assert.That(((uint[])idsField.GetValue(armies.GetValue(0))).Length, Is.EqualTo(ids.Length + 1));
            var soldiers = (Array)world.GetType().GetField("Soldiers", flags).GetValue(world);
            for (int i=0;i<(n0+1)/2;i++)
            {
                object soldier = soldiers.GetValue((int)ids[i]-1);
                soldier.GetType().GetField("Alive", flags).SetValue(soldier, false);
                soldier.GetType().GetField("Hp", flags).SetValue(soldier, 0); soldiers.SetValue(soldier, (int)ids[i]-1);
            }
            h.Step();
            Assert.That(h.Field(prefix+"N0"), Is.EqualTo(n0.ToString()));
            Assert.That(h.Field(prefix+"StartIds.Count"), Is.EqualTo(n0.ToString()));
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Completed));
        }
        [Test]
        public void MissingParentVersionAndStaleApplicationExpireWholeBatch()
        {
            var h = new Harness(); var a = h.Order(North, source: CommandSource.Ai, batch: 8); var b = h.Order(South, source: CommandSource.Ai, batch: 8);
            h.Step(h.Input(InputKind.Proposal, new[] { a, b }, 5));
            var human = h.Order(North); h.Step(h.Input(InputKind.Reserve, new[] { human })); h.Until(5);
            Assert.That(h.View(a).Status, Is.EqualTo(CommandStatus.Expired));
            Assert.That(h.View(b).Status, Is.EqualTo(CommandStatus.Expired));
            Assert.That(h.Field("Commands[Army=2].Policy"), Is.EqualTo("0"));
        }
        [Test]
        public void NewReservationCancelsOlderPendingHumanCommand()
        {
            var h = new Harness(); var a = h.Human(North, wait: 10); var b = h.Order(North);
            h.Step(h.Input(InputKind.Reserve, new[] { b }));
            Assert.That(h.View(a).Status, Is.EqualTo(CommandStatus.Cancelled));
            Assert.That(h.View(a).Reason, Is.EqualTo(ReasonCode.Superseded));
            h.Step(h.Input(InputKind.Resolve, new[] { Harness.Version(b, h.Sim.ExecutionRevision(b.CommandId)) }));
            Assert.That(h.View(b).Status, Is.EqualTo(CommandStatus.Executing));
        }
        [Test]
        public void ReplyReceivedOnDeadlineIsAcceptedAndObservationAgeDoesNotExpireExecutingPolicy()
        {
            var h = new Harness(); var human = h.Order(North);
            h.Step(h.Input(InputKind.Reserve, new[] { human }, deadline: 2)); h.Step();
            h.Step(h.Input(InputKind.Resolve, new[] { Harness.Version(human, 1) }));
            Assert.That(h.View(human).Status, Is.EqualTo(CommandStatus.Executing));
            var ai = h.Order(South, source: CommandSource.Ai); h.Step(h.Input(InputKind.Proposal, new[] { ai })); h.Until(250);
            Assert.That(h.View(ai).Status, Is.EqualTo(CommandStatus.Executing));
        }
        [Test]
        public void DoctrineOutranksAiAndExpiredPolicyOutranksCompletion()
        {
            var h = new Harness(); var doctrine = h.Order(North, source: CommandSource.Doctrine); h.Step(h.Input(InputKind.Proposal, new[] { doctrine }));
            var ai = h.Order(North, source: CommandSource.Ai); h.Step(h.Input(InputKind.Proposal, new[] { ai }));
            Assert.That(h.View(ai).Status, Is.EqualTo(CommandStatus.Expired));
            var human = h.Human(South, end: EndKind.AtTick, endTick: 4, valid: 3); h.Step();
            Assert.That(h.View(human).Status, Is.EqualTo(CommandStatus.Expired));
        }
        [Test]
        public void OwnershipIsRecheckedBeforePendingBatchStarts()
        {
            var h = new Harness(); var o = h.Order(Outpost, PolicyKind.Defend);
            o = new PolicyOrder(o.CommandId, o.BatchId, o.Source, o.Target, o.Kind, o.Goal, o.Priority, o.AllowedLoss,
                o.End, 0, 0, o.Parents, 0, new Expiration(long.MaxValue, 0, ExpireFlags.OwnershipChanged));
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var world = typeof(Battle).GetField("world", flags).GetValue(h.Sim);
            var outposts = (Array)world.GetType().GetField("Outposts", flags).GetValue(world);
            object post = outposts.GetValue(0); var owner = post.GetType().GetField("OwnerFactionId", flags);
            owner.SetValue(post, 1U); outposts.SetValue(post, 0);
            h.Step(h.Input(InputKind.Reserve, new[] { o }), h.Input(InputKind.Resolve, new[] { Harness.Version(o, 1) }, 5));
            owner.SetValue(post, 2U); outposts.SetValue(post, 0); h.Until(5);
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Expired)); Assert.That(h.View(o).Reason, Is.EqualTo(ReasonCode.OwnershipChanged));
        }
        [Test]
        public void GlobalOrderContinuesOtherArmiesAfterOneIsDestroyed()
        {
            var h = new Harness(); var o = h.Human(All);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var world = typeof(Battle).GetField("world", flags).GetValue(h.Sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", flags).GetValue(world);
            for (int i = 0; i < 8; i++)
            {
                object soldier = soldiers.GetValue(i); soldier.GetType().GetField("Alive", flags).SetValue(soldier, false);
                soldier.GetType().GetField("Hp", flags).SetValue(soldier, 0); soldiers.SetValue(soldier, i);
            }
            h.Step();
            Assert.That(h.View(o).Status, Is.EqualTo(CommandStatus.Executing));
            Assert.That(h.View(o).Reason, Is.EqualTo(ReasonCode.EmptyArmy));
            Assert.That(h.Field("Commands[Army=2].Policy"), Is.EqualTo("1"));
        }
        [Test]
        public void GatewayCanonicalStateTracksUndeliveredRequestsAndProviderFailure()
        {
            var sim = new Battle(Frozen()); var gateway = new CommandGateway(sim);
            byte[] before = gateway.CaptureDiagnostic(); var state = sim.CaptureDiagnostic().CanonicalState;
            ulong id = gateway.SubmitInterpreted(Intent(North, 1));
            Assert.That(gateway.CaptureDiagnostic(), Is.Not.EqualTo(before));
            Assert.That(sim.CaptureDiagnostic().CanonicalState, Is.EqualTo(state));
            gateway.Step(); gateway.Resolve(new PolicyReply(id, 1, Array.Empty<PolicyOrder>(), ReasonCode.InvalidPayload)); gateway.Step();
            Assert.That(sim.Capture(1).Commands.Single().Status, Is.EqualTo(CommandStatus.Impossible));
            Assert.That(gateway.Inputs.Last().ResolutionReason, Is.EqualTo(ReasonCode.InvalidPayload));
        }
        [Test]
        public void IndependentHumanFieldsAlsoCoexistWhilePending()
        {
            var h = new Harness(); var focus = h.Human(All, wait: 8); var reserve = h.Human(All, PolicyKind.MaintainReserve, wait: 8);
            h.Until(10);
            Assert.That(h.View(focus).Status, Is.EqualTo(CommandStatus.Executing));
            Assert.That(h.View(reserve).Status, Is.EqualTo(CommandStatus.Executing));
        }
        [Test]
        public void GatewayUsesConfirmedReservationTokensAcrossThreeSameTickReplacements()
        {
            var sim = new Battle(Frozen()); var gateway = new CommandGateway(sim);
            gateway.Submit(Intent(North, 1)); gateway.Submit(Intent(North, 2)); gateway.Submit(Intent(North, 3));
            gateway.Step();
            Assert.That(sim.Revision(North), Is.EqualTo(5), "Three reservations and two cancelled waiting orders each advance the revision.");
            for (int tick = 2; tick <= 40; tick++) gateway.Step();
            Assert.That(sim.Capture(1).Commands.Select(c => c.Status), Is.EqualTo(new[] {
                CommandStatus.Cancelled, CommandStatus.Cancelled, CommandStatus.Executing }));
        }
        private static UserPolicyIntent Intent(ScopeKey target, ulong sequence) => new UserPolicyIntent(sequence, target, PolicyKind.Focus, Goal(), 50,
            new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None));
        [Test]
        public void GatewayOrdersByFactionAndSequenceAllocatesIdsAndDelaysFortyTicks()
        {
            var sim = new Battle(Frozen()); var g = new CommandGateway(sim);
            ulong south = g.Submit(Intent(South, 20)); ulong north = g.Submit(Intent(North, 10));
            var inputs = g.Step();
            Assert.That(inputs[0].RequestId, Is.EqualTo(north)); Assert.That(inputs[1].RequestId, Is.EqualTo(south));
            Assert.That(inputs.Select(i => i.LogIndex), Is.EqualTo(new ulong[] { 1, 2 }));
            Assert.That(sim.Capture(1).Commands.All(c => c.Status == CommandStatus.Interpreting), Is.True);
            for (int i=2;i<=40;i++) g.Step();
            Assert.That(sim.Capture(1).Commands.All(c => c.Status == CommandStatus.Executing), Is.True);
        }
        [Test]
        public void GatewayAndControlLogReplayMatchEveryTick()
        {
            var scenario = Frozen(); var sim = new Battle(scenario); var g = new CommandGateway(sim);
            ulong request = g.Submit(Intent(North, 1)); g.Step(); g.Cancel(request); g.Step();
            g.Submit(Intent(South, 2)); g.Step();
            var hashes = new List<byte[]>(); var build = new BuildIdentity { SourceHash = "commands" };
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, g.Inputs, 60, build, (s, h, e) => hashes.Add(h));
                for (int run=0;run<2;run++)
                {
                    stream.Position=0; int count=0;
                    var result=ReplayRunner.Replay(stream,build,(s,h,e)=>Assert.That(h,Is.EqualTo(hashes[count++])));
                    Assert.That(result.FirstMismatchTick,Is.Null); Assert.That(count,Is.EqualTo(61));
                }
            }
        }
        [TestCase(0, 40)]
        [TestCase(60, 61)]
        [TestCase(200, 201)]
        public void DelayedInterpretedReplyUsesRequestTickForApplyTick(int delay, long expectedApply)
        {
            var sim = new Battle(Frozen());
            var provider = new DelayedPolicyProvider(delay, r => new[] {
                new PolicyOrder(999, 999, CommandSource.Human, r.Scope, r.Kind, r.Goal, 50, new LossBudget(1000),
                    new EndCondition(EndKind.UntilReplaced, 0), 0, 0, Array.Empty<PolicyVersion>(), r.StartedTick,
                    new Expiration(long.MaxValue, DelayedPolicyProvider.DefaultMaxObservationAgeTicks, ExpireFlags.None)) });
            var gateway = new CommandGateway(sim, provider);
            gateway.SubmitInterpreted(Intent(North, 1));
            for (long i = 0; i <= delay; i++) gateway.Step();
            Assert.That(gateway.Inputs.Last(v => v.Kind == InputKind.Resolve).ApplyTick, Is.EqualTo(expectedApply));
        }
        [Test]
        public void DelayedReplyPastDeadlineIsLoggedOnceAndNeverCarriedForward()
        {
            var sim = new Battle(Frozen());
            var provider = new DelayedPolicyProvider(400, r => Array.Empty<PolicyOrder>());
            var gateway = new CommandGateway(sim, provider);
            gateway.SubmitInterpreted(Intent(North, 1));
            for (int i = 0; i <= 401; i++) gateway.Step();
            Assert.That(gateway.Inputs.Count(v => v.Kind == InputKind.Resolve), Is.EqualTo(1));
            Assert.That(gateway.Inputs.Last().ResolutionReason, Is.EqualTo(ReasonCode.Deadline));
            Assert.That(sim.Capture(1).Commands.Single().Status, Is.EqualTo(CommandStatus.Expired));
        }
        private static PolicyOrder ReplyOrder(PolicyRequest r) => new PolicyOrder(999, 999, CommandSource.Human,
            r.Scope, r.Kind, r.Goal, 50, new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0),
            0, 999, Array.Empty<PolicyVersion>(), 999, new Expiration(long.MaxValue, 0, ExpireFlags.None));

        [Test]
        public void LongProfileAppliesTwentySecondReplyAt401()
        {
            var sim = new Battle(Frozen());
            var g = new CommandGateway(sim, new DelayedPolicyProvider(400, r => new[] { ReplyOrder(r) }, AiTimingProfile.Long));
            g.SubmitInterpreted(Intent(North, 1));
            for (int i = 0; i < 401; i++) g.Step();
            var input = g.Inputs.Single(i => i.Kind == InputKind.Resolve);
            Assert.That(input.DeadlineTick, Is.EqualTo(500));
            Assert.That(input.ApplyTick, Is.EqualTo(401));
            Assert.That(input.Orders.Single().ObservedTick, Is.Zero);
            Assert.That(input.Orders.Single().Expiration.MaxObservationAgeTicks, Is.EqualTo(500));
            Assert.That(sim.Capture(1).Commands.Single().Status, Is.EqualTo(CommandStatus.Executing));
        }
        [Test]
        public void DelayedObservationExpiresUsingSimulationAgeCheck()
        {
            var sim = new Battle(Frozen());
            var g = new CommandGateway(sim, new DelayedPolicyProvider(400, r => new[] { ReplyOrder(r) }));
            g.SubmitInterpreted(Intent(North, 1), 500); // independent override isolates the observation check
            for (int i = 0; i < 401; i++) g.Step();
            Assert.That(g.Inputs.Last().Orders.Single().Expiration.MaxObservationAgeTicks, Is.EqualTo(240));
            Assert.That(sim.Capture(1).Commands.Single().Reason, Is.EqualTo(ReasonCode.ObservationTooOld));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void DelayedReplyCannotReplaceLaterDirectHumanOrder(bool autonomous)
        {
            var sim = new Battle(Frozen());
            var g = new CommandGateway(sim, new DelayedPolicyProvider(200, r => new[] { ReplyOrder(r) }));
            if (autonomous) g.EnableAutonomous(Intent(North, 1)); else g.SubmitInterpreted(Intent(North, 1));
            for (int i = 0; i < 10; i++) g.Step();
            g.Submit(new UserPolicyIntent(2, North, PolicyKind.Defend, Goal(2), 50, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            for (int i = 10; i < 230; i++) g.Step();
            var human = sim.Capture(1).Commands.Single(c => c.Kind == PolicyKind.Defend);
            Assert.That(human.Status, Is.EqualTo(CommandStatus.Executing));
            Assert.That(human.Goal.Id, Is.EqualTo(2));
            Assert.That(sim.Capture(1).Commands.Any(c => c.Kind == PolicyKind.Focus && c.Status == CommandStatus.Executing), Is.False);
            if (autonomous) Assert.That(g.Inputs.Any(i => i.ResolutionReason == ReasonCode.StaleVersion), Is.True);
            else Assert.That(sim.Capture(1).Commands.First().Status, Is.EqualTo(CommandStatus.Cancelled));
        }
        [TestCase(200, CommandStatus.Executing)]
        [TestCase(199, CommandStatus.Expired)]
        public void DelayedDeadlineIncludesReplyOnDeadlineOnly(int deadline, CommandStatus expected)
        {
            var sim = new Battle(Frozen());
            var g = new CommandGateway(sim, new DelayedPolicyProvider(200, r => new[] { ReplyOrder(r) }));
            g.SubmitInterpreted(Intent(North, 1), deadline);
            for (int i = 0; i < 201; i++) g.Step();
            Assert.That(sim.Capture(1).Commands.Single().Status, Is.EqualTo(expected));
            Assert.That(g.Inputs.Last().ResolutionReason, Is.EqualTo(deadline == 200 ? ReasonCode.None : ReasonCode.Deadline));
        }
        [Test]
        public void DelayedReplyFreezesObservationAndDiagnosticIncludesQueuedContents()
        {
            var sim = new Battle(WeekTwoScenario.Create()); int calls = 0;
            SimPoint initial = default;
            var provider = new DelayedPolicyProvider(60, r => {
                calls++; initial = r.Observation.OwnArmies.First().Position;
                var o = ReplyOrder(r);
                return new[] { new PolicyOrder(0, 0, o.Source, o.Target, o.Kind, new PolicyGoal(GoalKind.Point, 0, initial),
                    o.Priority, o.AllowedLoss, o.End, 0, 0, o.Parents, 0, o.Expiration) };
            });
            var g = new CommandGateway(sim, provider); g.SubmitInterpreted(Intent(North, 1));
            byte[] queued = g.CaptureDiagnostic();
            var other = new CommandGateway(new Battle(WeekTwoScenario.Create()), new DelayedPolicyProvider(60, r => new[] { ReplyOrder(r) }));
            other.SubmitInterpreted(Intent(North, 1));
            Assert.That(queued, Is.Not.EqualTo(other.CaptureDiagnostic()));
            for (int i = 0; i < 61; i++) g.Step();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(sim.Capture(1).Observation.OwnArmies.First().Position, Is.Not.EqualTo(initial));
            Assert.That(g.Inputs.Last().Orders.Single().Goal.Point, Is.EqualTo(initial));
        }
        [Test]
        public void AutonomousHasOnePendingTargetAndRetriesChangedVersionsOnNextDecision()
        {
            var sim = new Battle(Frozen()); var requests = new List<PolicyRequest>();
            var g = new CommandGateway(sim, new DelayedPolicyProvider(200, r => { requests.Add(r); return new[] { ReplyOrder(r) }; }));
            g.EnableAutonomous(Intent(North, 1));
            for (int i = 0; i < 45; i++) g.Step();
            Assert.That(requests.Count, Is.EqualTo(1));
            g.Submit(Intent(All, 2)); g.Step(); // parent revision changes at S46
            for (int i = 46; i < 60; i++) g.Step();
            Assert.That(requests.Count, Is.EqualTo(1));
            g.Step();
            Assert.That(requests.Select(r => r.StartedTick), Is.EqualTo(new long[] { 0, 60 }));
            Assert.That(requests[1].Versions.Single(v => v.Scope.Equals(All)).Revision, Is.EqualTo(sim.Revision(All)));
            for (int i = 61; i < 201; i++) g.Step();
            Assert.That(g.Inputs.Count(i => i.ResolutionReason == ReasonCode.StaleVersion), Is.EqualTo(1));
            Assert.That(g.Inputs.Where(i => i.Kind == InputKind.Proposal).All(i => i.Orders.Count == 0), Is.True);
        }
        [TestCase(0, "long")] [TestCase(60, "long")] [TestCase(200, "long")] [TestCase(400, "long")] [TestCase(400, "default")]
        public void AutonomousAutoVsAutoContinuesAcrossRepeatedDelays(int delay, string profile)
        {
            var scenario = WeekTwoScenario.Create();
            var inputs = PolicyPresets.DelayedInputs(scenario, 1500, delay, AiTimingProfile.Parse(profile));
            var sim = new Battle(scenario);
            for (int tick = 1; tick <= 1500; tick++)
            {
                sim.Step(tick, inputs.Where(i => i.AcceptedTick == tick - 1).ToArray());
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "tick " + tick);
                Assert.That(sim.Capture(1).Result.HasEnded, Is.False, "tick " + tick);
            }
            if (profile == "default")
            {
                Assert.That(inputs.Count(i => i.ResolutionReason == ReasonCode.Deadline), Is.GreaterThanOrEqualTo(6));
                Assert.That(inputs.SelectMany(i => i.Orders), Is.Empty);
            }
            else Assert.That(inputs.Count(i => i.Orders.Count > 0), Is.GreaterThanOrEqualTo(6));
            Assert.That(inputs.SelectMany(i => i.Orders).All(o => o.Source == CommandSource.Ai), Is.True);
        }
        [Test]
        public void DelayedRecordingReplaysAllLiveHashesWithoutProviderOrDuplicateInputs()
        {
            var scenario = Frozen(); var sim = new Battle(scenario);
            var g = new CommandGateway(sim, new DelayedPolicyProvider(400, r => new[] { ReplyOrder(r) }, AiTimingProfile.Long));
            g.EnableAutonomous(Intent(North, 1));
            var hashes = new List<byte[]> { ReplayBinary.Hash(sim.CaptureDiagnostic().CanonicalState) };
            for (int i = 0; i < 850; i++) { g.Step(); hashes.Add(ReplayBinary.Hash(sim.CaptureDiagnostic().CanonicalState)); }
            var build = new BuildIdentity { SourceHash = "delayed" };
            using (var stream = new MemoryStream())
            {
                int n = 0;
                ReplayRunner.Record(stream, scenario, g.Inputs, 850, build, (s,h,e) => Assert.That(h, Is.EqualTo(hashes[n++])), aiDelayTicks:400, aiProfile:"long");
                Assert.That(n, Is.EqualTo(851)); stream.Position = 0;
                using (var reader = new ReplayReader(stream))
                {
                    Assert.That(reader.Header.AiDelayTicks, Is.EqualTo(400)); Assert.That(reader.Header.AiProfile, Is.EqualTo("long"));
                    var ids = new List<ulong>(); ReplayRecord record;
                    while ((record = reader.Read()) != null) if (record.Kind == ReplayRecordKind.Input) ids.Add(record.LogIndex);
                    Assert.That(ids.Count, Is.EqualTo(g.Inputs.Count)); Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
                }
                stream.Position = 0; n = 0;
                var result = ReplayRunner.Replay(stream, build, (s,h,e) => Assert.That(h, Is.EqualTo(hashes[n++])));
                Assert.That(result.FirstMismatchTick, Is.Null); Assert.That(n, Is.EqualTo(851));
            }
        }
        [TestCase(0, 40)] [TestCase(60, 61)] [TestCase(200, 201)] [TestCase(400, 401)]
        public void AutonomousProposalUsesRequestVersionsAndTransmission(int delay, int apply)
        {
            var sim = new Battle(Frozen()); PolicyRequest request = null;
            var g = new CommandGateway(sim, new DelayedPolicyProvider(delay, r => {
                request = r; return new[] { ReplyOrder(r) }; }, AiTimingProfile.Long));
            g.EnableAutonomous(Intent(North, 1));
            for (int i = 0; i <= delay; i++) g.Step();
            var proposal = g.Inputs.First(i => i.Kind == InputKind.Proposal);
            Assert.That(g.Inputs.Any(i => i.Kind == InputKind.Reserve), Is.False);
            Assert.That(proposal.ApplyTick, Is.EqualTo(apply));
            var order = proposal.Orders.Single();
            Assert.That(order.Source, Is.EqualTo(CommandSource.Ai));
            Assert.That(order.TargetRevision, Is.EqualTo(request.Versions.Single(v => v.Scope.Equals(North)).Revision));
            Assert.That(order.Parents.Select(v => v.Revision), Is.EqualTo(request.Versions.Where(v => !v.Scope.Equals(North)).Select(v => v.Revision)));
            Assert.That(order.ObservedTick, Is.EqualTo(request.StartedTick));
            Assert.That(order.Expiration.MaxObservationAgeTicks, Is.EqualTo(500));
        }
        [Test]
        public void CancellationAppliesNextTickWithoutResettingPhysicalStateOrWaits()
        {
            var sim = new Battle(Frozen()); var g = new CommandGateway(sim);
            ulong id = g.Submit(Intent(North, 1));
            for (int i = 0; i < 40; i++) g.Step();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var world = typeof(Battle).GetField("world", flags).GetValue(sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", flags).GetValue(world);
            object soldier = soldiers.GetValue(0);
            soldier.GetType().GetField("NextAttackTick", flags).SetValue(soldier, 999L);
            soldier.GetType().GetField("Hp", flags).SetValue(soldier, 17);
            soldiers.SetValue(soldier, 0);
            var armies = (Array)world.GetType().GetField("Armies", flags).GetValue(world);
            object army = armies.GetValue(0); var decisionField = army.GetType().GetField("Decision", flags);
            object decision = decisionField.GetValue(army);
            decision.GetType().GetField("HoldUntilTick").SetValue(decision, 888L);
            decisionField.SetValue(army, decision); armies.SetValue(army, 0);
            var before = DiagnosticComparison.Fields(sim.CaptureDiagnostic());
            g.Cancel(id);
            Assert.That(sim.Capture(1).Commands.Single().Status, Is.EqualTo(CommandStatus.Executing));
            g.Step();
            Assert.That(sim.Capture(1).Commands.Single().Status, Is.EqualTo(CommandStatus.Cancelled));
            Assert.That(g.Inputs.Last().ApplyTick, Is.EqualTo(41));
            var after = DiagnosticComparison.Fields(sim.CaptureDiagnostic());
            foreach (var field in before.Where(f => f.Key.StartsWith("Soldiers[1].Position") ||
                f.Key == "Soldiers[1].Hp" || f.Key == "Soldiers[1].NextAttackTick" || f.Key == "Ai.Armies[1].HoldUntilTick"))
                Assert.That(after.Single(f => f.Key == field.Key).Value, Is.EqualTo(field.Value), field.Key);
            Assert.That(after.Single(f => f.Key == "Soldiers[1].NextAttackTick").Value, Is.EqualTo("999"));
            Assert.That(after.Single(f => f.Key == "Ai.Armies[1].HoldUntilTick").Value, Is.EqualTo("888"));
        }
    }
}
