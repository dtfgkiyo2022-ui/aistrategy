using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class SoldierControlRulesTests
    {
        private static SoldierControlRules.Target Point(SoldierControlRules.TargetKind kind)
        {
            return new SoldierControlRules.Target(kind,
                new PolicyGoal(GoalKind.Point, 0, new SimPoint(Fix64.FromInt(12), Fix64.FromInt(34))));
        }

        [TestCase(SoldierControlRules.TargetKind.Ground)]
        [TestCase(SoldierControlRules.TargetKind.EnemyArmy)]
        [TestCase(SoldierControlRules.TargetKind.EnemySoldier)]
        [TestCase(SoldierControlRules.TargetKind.EnemyBuilding)]
        public void GroundAndVisibleEnemyTargetsBecomePointFocus(SoldierControlRules.TargetKind kind)
        {
            Assert.That(SoldierControlRules.TryResolve(true, false, Point(kind), out var policy, out var goal), Is.True);
            Assert.That(policy, Is.EqualTo(PolicyKind.Focus));
            Assert.That(goal.Kind, Is.EqualTo(GoalKind.Point));
        }

        [Test]
        public void EnemyObjectivesKeepTheirObjectiveGoal()
        {
            var core = new SoldierControlRules.Target(SoldierControlRules.TargetKind.EnemyCore,
                new PolicyGoal(GoalKind.Core, 2, default(SimPoint)));
            var outpost = new SoldierControlRules.Target(SoldierControlRules.TargetKind.EnemyOutpost,
                new PolicyGoal(GoalKind.Outpost, 3, default(SimPoint)));

            Assert.That(SoldierControlRules.TryResolve(true, false, core, out var corePolicy, out var coreGoal), Is.True);
            Assert.That(corePolicy, Is.EqualTo(PolicyKind.Focus));
            Assert.That(coreGoal.Kind, Is.EqualTo(GoalKind.Core));
            Assert.That(coreGoal.Id, Is.EqualTo(2u));
            Assert.That(SoldierControlRules.TryResolve(true, false, outpost, out var outpostPolicy, out var outpostGoal), Is.True);
            Assert.That(outpostPolicy, Is.EqualTo(PolicyKind.Focus));
            Assert.That(outpostGoal.Kind, Is.EqualTo(GoalKind.Outpost));
            Assert.That(outpostGoal.Id, Is.EqualTo(3u));
        }

        [TestCase(false, false, SoldierControlRules.TargetKind.Ground)]
        [TestCase(true, true, SoldierControlRules.TargetKind.Ground)]
        [TestCase(true, false, SoldierControlRules.TargetKind.Ui)]
        [TestCase(true, false, SoldierControlRules.TargetKind.Nothing)]
        public void UiVillagerAndEmptySelectionsDoNotIssue(bool army, bool villager, SoldierControlRules.TargetKind kind)
        {
            Assert.That(SoldierControlRules.TryResolve(army, villager, Point(kind), out _, out _), Is.False);
        }
    }
}
