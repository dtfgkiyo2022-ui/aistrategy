using System;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Core.Tests
{
    public sealed class MovementStallTests
    {
        private static SimPoint P(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        [Test]
        public void ScoutRetreatHomeKeepsMovingHomeWhenTheEnemyIsNotOnTheHomeRay()
        {
            var position = P(100, 100);
            var home = P(24, 100);
            var enemy = new[] { new VisibleEnemy(1, P(100, 106), (byte)UnitKind.Infantry) };
            var observation = new FactionObservation(1, 1, Array.Empty<OwnArmyView>(), enemy,
                Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>());

            var next = PolicyDecision.ScoutRetreatHome(observation, position, home, Fix64.FromRatio(1, 5));

            Assert.That(Rts.Tests.EditMode.TestDistanceReference.Squared(next, home), Is.LessThan(Rts.Tests.EditMode.TestDistanceReference.Squared(position, home)));
            Assert.That(Rts.Tests.EditMode.TestDistanceReference.Squared(next, enemy[0].Position), Is.GreaterThanOrEqualTo(
                Rts.Tests.EditMode.TestDistanceReference.Squared(position, enemy[0].Position)));
        }

        [Test]
        public void ScoutRetreatHomeDoesNotStepTowardAnEnemyBlockingTheHomeRay()
        {
            var position = P(100, 100);
            var home = P(24, 100);
            var enemy = new[] { new VisibleEnemy(1, P(94, 100), (byte)UnitKind.Infantry) };
            var observation = new FactionObservation(1, 1, Array.Empty<OwnArmyView>(), enemy,
                Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>());

            var next = PolicyDecision.ScoutRetreatHome(observation, position, home, Fix64.FromRatio(1, 5));

            Assert.That(Rts.Tests.EditMode.TestDistanceReference.Squared(next, enemy[0].Position), Is.GreaterThanOrEqualTo(
                Rts.Tests.EditMode.TestDistanceReference.Squared(position, enemy[0].Position)));
            Assert.That(next, Is.Not.EqualTo(position));
        }
    }
}
