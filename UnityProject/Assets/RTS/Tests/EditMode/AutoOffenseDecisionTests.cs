using System;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Tests.EditMode
{
    public sealed class AutoOffenseDecisionTests
    {
        private static SimPoint P(int x, int z = 0) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
        private static KnownObjective[] Objectives() => new[] {
            new KnownObjective(GoalKind.Core, 1, P(0), true, 1, true, 3000, 0),
            new KnownObjective(GoalKind.Core, 2, P(100), true, 2, true, 3000, 0),
            new KnownObjective(GoalKind.Outpost, 1, P(60), true, 0, false, 0, 0) };
        private static FactionObservation Observation(long tick, SimPoint enemy, bool visible = true)
        {
            var enemies = visible ? new[] { new VisibleEnemy(7, enemy, (byte)UnitKind.Infantry) } : Array.Empty<VisibleEnemy>();
            var contacts = new[] { new EnemyContact(7, enemy, tick, 1, 3, visible) };
            return new FactionObservation(1, tick, new[] { new OwnArmyView(1, 1, UnitKind.Infantry, P(0), 10, default) }, enemies, contacts, Objectives());
        }

        [Test]
        public void RouteThreatCountsAContactBesideThePolylineButNotBesideTheGoalOnly()
        {
            var o = Observation(20, P(20, 20));
            var route = new ObjectiveRoute(new PolicyGoal(GoalKind.Outpost, 1, default), 6, new[] { P(0), P(0, 40), P(60, 40), P(60) });
            var input = new ArmyDecisionInput(o.OwnArmies[0], default, false, default, new[] { route, new ObjectiveRoute(new PolicyGoal(GoalKind.Core, 2, default), 10, new[] { P(0), P(100) }) });
            var result = PolicyDecision.Allocate(o, 20, new[] { input }, 0, Array.Empty<uint>(), Array.Empty<AttackMemory>(), Array.Empty<PolicyOrder>(), out _);
            Assert.That(result[0].Goal.Id, Is.EqualTo(1));
        }

        [Test]
        public void ApproachRequiresADecreasingPathDistanceAndExpiresAfterTwoHundredTicks()
        {
            var old = PolicyDecision.UpdateApproaches(Observation(1, P(80)), Array.Empty<ContactApproachMemory>(), new[] { new ContactApproachRoute(7, 1, 10) });
            var next = PolicyDecision.UpdateApproaches(Observation(2, P(70)), old, new[] { new ContactApproachRoute(7, 1, 8) });
            Assert.That(next[0].ThreatUntilTick, Is.EqualTo(202));
            var unchanged = PolicyDecision.UpdateApproaches(Observation(3, P(60)), next, new[] { new ContactApproachRoute(7, 1, 9) });
            // A confirmed increase clears the approach immediately; it is not merely allowed to age out.
            Assert.That(unchanged[0].ThreatUntilTick, Is.Zero);
        }

        [Test]
        public void MissingCandidateRouteIsUnreachableRatherThanThrowing()
        {
            var o = Observation(20, P(90));
            // The outpost is deliberately absent from route metadata.  Core is reachable.
            var input = new ArmyDecisionInput(o.OwnArmies[0], default, false, default,
                new[] { new ObjectiveRoute(new PolicyGoal(GoalKind.Core, 2, default), 10, new[] { P(0), P(100) }) });
            Assert.DoesNotThrow(() => PolicyDecision.Allocate(o, 20, new[] { input }, 0, Array.Empty<uint>(),
                Array.Empty<AttackMemory>(), Array.Empty<PolicyOrder>(), out _));
        }

        [Test]
        public void ApproachMemoryIsRemovedWhenTheContactDisappears()
        {
            var old = PolicyDecision.UpdateApproaches(Observation(1, P(80)), Array.Empty<ContactApproachMemory>(),
                new[] { new ContactApproachRoute(7, 1, 50) });
            var noContacts = new FactionObservation(1, 2, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), Objectives());
            Assert.That(PolicyDecision.UpdateApproaches(noContacts, old, Array.Empty<ContactApproachRoute>()), Is.Empty);
        }
    }
}
