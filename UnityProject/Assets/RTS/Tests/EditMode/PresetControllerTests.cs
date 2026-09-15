using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;

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
        public void MaintainIssuesDefendOnOwnershipLossAndReacquisition()
        {
            var port = new Port(); var controller = new PresetController("maintain", 1, port); controller.Initialize();
            controller.Step(Frame(1, 1));
            Assert.That(port.Batches[1].Single().Kind, Is.EqualTo(PolicyKind.Defend));
            controller.Step(Frame(2, 0));
            controller.Step(Frame(3, 1));
            Assert.That(port.Batches.Count, Is.EqualTo(3));
            Assert.That(port.Batches[2].Single().Expiration.Flags, Is.EqualTo(ExpireFlags.OwnershipChanged));
        }
        [Test]
        public void ConcentrateOnlyAdvancesAfterNorthFocusCompletesNormally()
        {
            var port = new Port(); var controller = new PresetController("concentrate", 1, port); controller.Initialize();
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
