using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Battle = Rts.Simulation.Simulation;
using Rts.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class PresetControllerTests
    {
        private sealed class Port : ICommandPort
        {
            public readonly List<PolicyOrder[]> Batches = new List<PolicyOrder[]>();
            public ulong Submit(UserPolicyIntent intent) => 0;
            public void Cancel(ulong requestId) { }
            public ulong Propose(uint faction, ulong sequence, IReadOnlyList<PolicyOrder> orders, long applyTick)
            { Batches.Add(orders.ToArray()); return (ulong)Batches.Count; }
        }
        private static readonly ScopeKey All = new ScopeKey(1, ScopeKind.All, 0);
        private static FactionFrame Frame(long tick, uint owner, params CommandView[] commands)
        {
            var objectives = new[] { new KnownObjective(GoalKind.Outpost, 1, default, true, owner, false, 0, tick), new KnownObjective(GoalKind.Outpost, 2, default, true, 0, false, 0, tick) };
            return new FactionFrame(tick, 1, Array.Empty<RenderUnit>(), new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), objectives), commands, Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()), default);
        }
        [Test]
        [TestCase("maintain")]
        [TestCase("maintain-legacy")]
        [TestCase("concentrate")]
        public void FirstPresetProposalIsAcceptedWithoutStaleVersion(string preset)
        {
            var scenario = WeekTwoScenario.Create();
            var inputs = PolicyPresets.RecordedInputs(scenario, "none", preset, 10);
            var initial = inputs.First(i => i.Kind == InputKind.Proposal).Orders.Select(o => o.CommandId).ToArray();
            var simulation = new Battle(scenario);
            for (int tick = 1; tick <= 10; tick++) simulation.Step(tick, inputs.Where(i => i.ApplyTick == tick).ToArray());
            var commands = simulation.Capture(2).Commands.Where(c => initial.Contains(c.CommandId)).ToArray();
            Assert.That(commands, Has.Length.EqualTo(initial.Length));
            Assert.That(commands.All(c => (c.Status == CommandStatus.Executing || c.Status == CommandStatus.Completed) && c.Reason != ReasonCode.StaleVersion), Is.True);
        }
        [Test]
        public void MaintainIssuesDefendOnOwnershipLossAndReacquisition()
        {
            var port = new Port(); var controller = new PresetController("maintain", 1, port); controller.Initialize();
            controller.Step(Frame(1, 1));
            Assert.That(port.Batches[1].Single().Kind, Is.EqualTo(PolicyKind.Defend));
            var defend = new CommandView(7, new ScopeKey(1, ScopeKind.Outpost, 1), PolicyKind.Defend,
                new PolicyGoal(GoalKind.Outpost, 1, default), CommandStatus.Executing, 0, 1, ReasonCode.None, CommandSource.Doctrine);
            controller.Step(Frame(2, 1, defend));
            Assert.That(port.Batches.Count, Is.EqualTo(2), "Executing Defend is retained while the outpost is owned.");
            defend = new CommandView(7, new ScopeKey(1, ScopeKind.Outpost, 1), PolicyKind.Defend,
                new PolicyGoal(GoalKind.Outpost, 1, default), CommandStatus.Expired, 0, 1, ReasonCode.OwnershipChanged, CommandSource.Doctrine);
            controller.Step(Frame(3, 0, defend));
            controller.Step(Frame(4, 1));
            Assert.That(port.Batches.Count, Is.EqualTo(3));
            Assert.That(port.Batches[2].Single().Expiration.Flags, Is.EqualTo(ExpireFlags.OwnershipChanged));
        }
        [Test]
        public void MaintainDoesNotRepeatAnUnreflectedDefendProposalFor2000Ticks()
        {
            var port = new Port(); var controller = new PresetController("maintain", 1, port); controller.Initialize();
            for (int tick = 1; tick <= 2000; tick++) controller.Step(Frame(tick, 1));
            controller.Step(Frame(2001, 0));
            for (int tick = 2002; tick <= 4001; tick++) controller.Step(Frame(tick, 1));
            Assert.That(port.Batches.Skip(1).SelectMany(batch => batch).Count(order => order.Kind == PolicyKind.Defend), Is.EqualTo(2),
                "Defend is proposed once per ownership acquisition, even while gateway rejection or delivery is still unobserved.");
        }
        [Test]
        public void MaintainRetriesAStaleDefendAtMostThreeTimesUntilOwnershipChanges()
        {
            var port = new Port(); var controller = new PresetController("maintain", 1, port); controller.Initialize();
            controller.Step(Frame(1, 1));
            for (int retry = 1; retry <= 3; retry++)
            {
                var stale = new CommandView((ulong)retry, new ScopeKey(1, ScopeKind.Outpost, 1), PolicyKind.Defend,
                    new PolicyGoal(GoalKind.Outpost, 1, default), CommandStatus.Expired, 0, 1, ReasonCode.StaleVersion, CommandSource.Doctrine);
                controller.Step(Frame(retry + 1, 1, stale));
            }
            Assert.That(port.Batches.Count, Is.EqualTo(5), "Initial reserve, one Defend, and exactly three stale retries.");
            var exhausted = new CommandView(4, new ScopeKey(1, ScopeKind.Outpost, 1), PolicyKind.Defend,
                new PolicyGoal(GoalKind.Outpost, 1, default), CommandStatus.Expired, 0, 1, ReasonCode.StaleVersion, CommandSource.Doctrine);
            controller.Step(Frame(5, 1, exhausted));
            Assert.That(port.Batches.Count, Is.EqualTo(5));
            controller.Step(Frame(6, 0, exhausted));
            controller.Step(Frame(7, 1));
            Assert.That(port.Batches.Count, Is.EqualTo(6), "Ownership loss resets the per-outpost retry budget.");
        }
        [Test]
        public void ConcentrateRetriesInitialSouthAbandonAtMostThreeTimes()
        {
            var port = new Port(); var controller = new PresetController("concentrate", 1, port); controller.Initialize();
            for (int retry = 1; retry <= 3; retry++)
            {
                var stale = new CommandView((ulong)retry, new ScopeKey(1, ScopeKind.Outpost, 2), PolicyKind.AllowAbandon,
                    default, CommandStatus.Expired, 0, 1, ReasonCode.StaleVersion, CommandSource.Doctrine);
                controller.Step(Frame(retry, 0, stale));
            }
            Assert.That(port.Batches.Count, Is.EqualTo(4));
            var exhausted = new CommandView(4, new ScopeKey(1, ScopeKind.Outpost, 2), PolicyKind.AllowAbandon,
                default, CommandStatus.Expired, 0, 1, ReasonCode.StaleVersion, CommandSource.Doctrine);
            controller.Step(Frame(4, 0, exhausted));
            Assert.That(port.Batches.Count, Is.EqualTo(4));
        }
        [Test]
        public void ConcentrateOnlyAdvancesAfterNorthFocusCompletesNormally()
        {
            var port = new Port(); var controller = new PresetController("concentrate", 1, port); controller.Initialize();
            Assert.That(port.Batches.Single().Select(o => o.Kind), Is.EquivalentTo(new[] { PolicyKind.MaintainReserve, PolicyKind.Focus, PolicyKind.AllowAbandon }));
            Assert.That(port.Batches.Single().Single(o => o.Kind == PolicyKind.AllowAbandon).Target, Is.EqualTo(new ScopeKey(1, ScopeKind.Outpost, 2)));
            var north = new CommandView(7, All, PolicyKind.Focus, new PolicyGoal(GoalKind.Outpost, 1, default), CommandStatus.Completed, 0, 1, ReasonCode.LossLimit, CommandSource.Doctrine);
            controller.Step(Frame(1, 0, north));
            Assert.That(port.Batches.Count, Is.EqualTo(1));
            north = new CommandView(7, All, PolicyKind.Focus, new PolicyGoal(GoalKind.Outpost, 1, default), CommandStatus.Completed, 0, 1, ReasonCode.None, CommandSource.Doctrine);
            controller.Step(Frame(2, 1, north)); controller.Step(Frame(3, 1, north));
            Assert.That(port.Batches.Count, Is.EqualTo(2));
            Assert.That(port.Batches[1].Select(o => o.Kind), Is.EquivalentTo(new[] { PolicyKind.AllowAbandon, PolicyKind.Focus }));
            var core = port.Batches[1].Single(o => o.Kind == PolicyKind.Focus).Goal;
            Assert.That(core.Kind, Is.EqualTo(GoalKind.Core)); Assert.That(core.Id, Is.EqualTo(2));
        }
        [Test]
        public void InvisibleEnemyStateCannotAffectPresetProposals()
        {
            var a = new Port(); var b = new Port(); var left = new PresetController("maintain", 1, a); var right = new PresetController("maintain", 1, b);
            left.Initialize(); right.Initialize();
            left.Step(Frame(1, 1)); right.Step(Frame(1, 1));
            Assert.That(a.Batches.SelectMany(x => x).Select(x => x.Kind), Is.EqualTo(b.Batches.SelectMany(x => x).Select(x => x.Kind)));
        }
    }
}
