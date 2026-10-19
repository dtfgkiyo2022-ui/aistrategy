using System;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Tests.EditMode
{
    public sealed class Perf6DecisionTests
    {
        private static SimPoint P(int x, int z = 32) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        [Test]
        public void ApproachIndexKeepsFirstDuplicateRouteAndCanonicalOrder()
        {
            var enemies = new[] { new VisibleEnemy(2, P(10), 1), new VisibleEnemy(1, P(20), 1) };
            var contacts = new[]
            {
                new EnemyContact(2, P(10), 0, 1, 1, true),
                new EnemyContact(1, P(20), 0, 1, 1, true)
            };
            var objectives = new[]
            {
                new KnownObjective(GoalKind.Outpost, 2, P(40), true, 1, false, 0, 0),
                new KnownObjective(GoalKind.Outpost, 1, P(30), true, 1, false, 0, 0)
            };
            var observation = new FactionObservation(1, 10, Array.Empty<OwnArmyView>(), enemies, contacts, objectives);
            var result = PolicyDecision.UpdateApproaches(observation, Array.Empty<ContactApproachMemory>(), new[]
            {
                new ContactApproachRoute(2, 2, 77),
                new ContactApproachRoute(2, 2, 99),
                new ContactApproachRoute(1, 1, 55)
            });

            Assert.That(result, Has.Length.EqualTo(4));
            Assert.That(result[0].ObjectiveId, Is.EqualTo(1));
            Assert.That(result[0].ContactId, Is.EqualTo(1));
            Assert.That(result[0].PreviousDistance, Is.EqualTo(55));
            Assert.That(result[3].ObjectiveId, Is.EqualTo(2));
            Assert.That(result[3].ContactId, Is.EqualTo(2));
            Assert.That(result[3].PreviousDistance, Is.EqualTo(77));
        }

        [Test]
        public void TacticalCoreScanUsesTheSameLowestIdTieBreak()
        {
            var observation = new FactionObservation(1, 0, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                Array.Empty<EnemyContact>(), new[]
                {
                    new KnownObjective(GoalKind.Core, 1, P(-20), true, 1, true, 1000, 0),
                    new KnownObjective(GoalKind.Core, 3, P(4), true, 2, true, 1000, 0),
                    new KnownObjective(GoalKind.Core, 2, P(4), true, 2, true, 1000, 0)
                });
            var input = new TacticalInput(1, 0, P(0), P(40), P(-20), Fix64.FromInt(100), Fix64.FromInt(0),
                (PolicyKind)0, AssignmentKind.Advance, false);
            var pursuit = default(PursuitMemory);

            var intent = PolicyDecision.Tactics(observation, input, ref pursuit);

            Assert.That(intent.TargetObjective.Kind, Is.EqualTo(GoalKind.Core));
            Assert.That(intent.TargetObjective.Id, Is.EqualTo(2));
        }
    }
}
