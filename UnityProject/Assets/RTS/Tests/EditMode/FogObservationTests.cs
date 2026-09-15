using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class FogObservationTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static SimPoint P(int x, int z = 65) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
        private static ScenarioDefinition Scenario(int enemies = 1)
        {
            var s = WeekTwoScenario.Create();
            s.Map.BlockedCellIds = Array.Empty<int>();
            s.Rules.CoreReinforcementIntervalTicks = s.Rules.OutpostReinforcementIntervalTicks = int.MaxValue;
            for (int i = 0; i < s.UnitParameters.Length; i++) { s.UnitParameters[i].Speed = Fix64.FromInt(0); s.UnitParameters[i].Damage = 0; }
            var own = s.Soldiers[0]; own.Position = P(81);
            var enemy = s.Soldiers[20];
            s.Soldiers = new SoldierDefinition[enemies + 1]; s.Soldiers[0] = own;
            for (int i = 1; i <= enemies; i++) { enemy.Id = (uint)i + 1; enemy.Position = P(91); s.Soldiers[i] = enemy; }
            return s;
        }
        private static void Set(Battle sim, string collection, int index, string field, object value)
        {
            var w = typeof(Battle).GetField("world", Hidden).GetValue(sim);
            var states = (Array)w.GetType().GetField(collection, Hidden).GetValue(w);
            var item = states.GetValue(index); item.GetType().GetField(field, Hidden).SetValue(item, value); states.SetValue(item, index);
        }
        private static void Until(Battle sim, long tick) { while (sim.Capture(1).Tick < tick) sim.Step(sim.Capture(1).Tick + 1, Array.Empty<ScheduledInput>()); }
        private static EnemyContact Individual(Battle sim) => sim.Capture(1).Observation.Contacts.Single(c => !c.IsArmyContact);
        private static EnemyContact Aggregate(Battle sim) => sim.Capture(1).Observation.Contacts.Single(c => c.IsArmyContact);
        private static string Snapshot(object value)
        {
            if (value == null) return "null";
            if (value is IEnumerable list) return "[" + string.Join(",", list.Cast<object>().Select(Snapshot)) + "]";
            var t = value.GetType();
            if (t.IsPrimitive || t.IsEnum || value is string) return value.ToString();
            return "{" + string.Join(",", t.GetProperties().Select(p => p.Name + ":" + Snapshot(p.GetValue(value)))) + "}";
        }

        [TestCase(1)] [TestCase(5)] [TestCase(6)]
        public void ArmyEstimateUsesOnlyVisibleMembers(int visible)
        {
            var s = Scenario(8); for (int i = visible + 1; i < s.Soldiers.Length; i++) s.Soldiers[i].Position = P(231);
            var sim = new Battle(s); var c = Aggregate(sim);
            int k = (visible + 4) / 5;
            Assert.That(c.EstimateMin, Is.EqualTo(5 * (k - 1) + 1)); Assert.That(c.EstimateMax, Is.EqualTo(5 * k));
            Assert.That(c.CoveredContactIds, Is.EquivalentTo(sim.Capture(1).Observation.VisibleEnemies.Select(e => e.ContactId)));
            Assert.That(sim.Capture(1).Observation.Contacts.Where(x => !x.IsArmyContact).All(x => x.EstimateMin == 1 && x.EstimateMax == 1), Is.True);
            Assert.That(PolicyDecision.CountableContacts(sim.Capture(1).Observation).Single().IsArmyContact, Is.True);
        }

        [Test]
        public void HiddenDeathFreezesContactThroughBothAgeThresholds()
        {
            var sim = new Battle(Scenario()); var first = Individual(sim); var army = Aggregate(sim);
            Set(sim, "Soldiers", 0, "Position", P(25)); Set(sim, "Soldiers", 1, "Hp", 0);
            Until(sim, 1); Assert.That(Individual(sim).IsAbsentAtLastPosition, Is.False);
            foreach (long tick in new long[] { 199, 200, 599, 600 })
            {
                Until(sim, tick);
                foreach (var c in sim.Capture(1).Observation.Contacts)
                {
                    Assert.That(c.LastPosition, Is.EqualTo(first.LastPosition)); Assert.That(c.LastSeenTick, Is.EqualTo(first.LastSeenTick));
                    Assert.That(c.IsUncertain, Is.EqualTo(tick >= 200)); Assert.That(c.IsStrengthUnknown, Is.EqualTo(tick >= 600));
                    Assert.That(c.EstimateMax, Is.EqualTo(tick >= 600 ? -1 : c.IsArmyContact ? army.EstimateMax : first.EstimateMax));
                    Assert.That(c.AssumedStrength, Is.EqualTo(10));
                }
            }
            Set(sim, "Soldiers", 0, "Position", P(81)); Until(sim, 601);
            Assert.That(Individual(sim).IsAbsentAtLastPosition, Is.True, "Later discovery of an empty cell must not reveal an earlier hidden death.");
        }

        [Test]
        public void VisibleDeathRemovesContactAndLivingHiddenEnemyLeavesAbsence()
        {
            var sim = new Battle(Scenario()); Set(sim, "Soldiers", 1, "Hp", 0); Until(sim, 1);
            Assert.That(sim.Capture(1).Observation.Contacts, Is.Empty);
            sim = new Battle(Scenario()); var first = Individual(sim);
            Set(sim, "Soldiers", 1, "Position", P(231)); Until(sim, 1);
            Assert.That(Individual(sim).IsAbsentAtLastPosition, Is.True);
            Assert.That(Individual(sim).LastPosition, Is.EqualTo(first.LastPosition)); Assert.That(Individual(sim).LastSeenTick, Is.EqualTo(first.LastSeenTick));
            Assert.That(sim.Capture(1).Units.All(u => u.IsOwn), Is.True);
        }

        [Test]
        public void ContactSequencesFollowDiscoveryAndAggregateNamespaceIsIndependent()
        {
            var s = Scenario(2); s.Soldiers[1].Position = P(231); s.Soldiers[2].ArmyId = 6;
            var sim = new Battle(s);
            Assert.That(Individual(sim).ContactId, Is.EqualTo(1)); Assert.That(Individual(sim).ContactId, Is.Not.EqualTo(s.Soldiers[2].Id));
            Assert.That(Aggregate(sim).ContactId, Is.EqualTo(1));
            Set(sim, "Soldiers", 1, "Position", P(95)); Until(sim, 1);
            var o = sim.Capture(1).Observation;
            Assert.That(o.VisibleEnemies.Single(e => e.Position.Equals(P(95))).ContactId, Is.EqualTo(2));
            Assert.That(o.Contacts.Where(c => c.IsArmyContact).Select(c => c.ContactId), Is.EqualTo(new uint[] { 1, 2 }));
            Assert.That(PolicyDecision.CountableContacts(o).Count(), Is.EqualTo(2));
        }

        [Test]
        public void VisibilityBoundaryAndOwnedSourcesUseInclusiveCellCenters()
        {
            var s = Scenario(0); s.UnitParameters[0].Vision = Fix64.FromInt(4); s.Rules.OwnedObjectiveVision = Fix64.FromInt(4);
            s.Cores[0].Position = P(25); s.Outposts[0].Position = P(151); s.Outposts[0].OwnerFactionId = 1;
            var sim = new Battle(s);
            Func<int, bool> visible = x => sim.Capture(1).Fog.VisibleCells[32 * s.Map.WidthCells + x / 2];
            foreach (int source in new[] { 25, 81, 151 }) { Assert.That(visible(source + 4), Is.True); Assert.That(visible(source + 6), Is.False); }
            Set(sim, "Outposts", 0, "OwnerFactionId", 2U); Until(sim, 1);
            Assert.That(visible(151), Is.False); Assert.That(sim.Capture(1).Fog.ExploredCells[32 * s.Map.WidthCells + 151 / 2], Is.True);
        }

        [Test]
        public void ObjectiveOwnersFreezeWhileCoreAllegianceIsPublic()
        {
            var s = Scenario(0); s.Outposts[0].Position = P(91); s.Outposts[0].OwnerFactionId = 2;
            var sim = new Battle(s);
            var core = sim.Capture(1).Objectives.Single(o => o.Kind == GoalKind.Core && o.Id == 2);
            Assert.That(core.IsOwnerKnown, Is.True); Assert.That(core.OwnerFactionId, Is.EqualTo(2)); Assert.That(core.IsHpKnown, Is.False);
            Assert.That(PolicyDecision.Core(sim.Capture(1).Observation, false).Id, Is.EqualTo(core.Id));
            Set(sim, "Soldiers", 0, "Position", P(25)); Set(sim, "Outposts", 0, "OwnerFactionId", 0U); Until(sim, 1);
            var post = sim.Capture(1).Objectives.Single(o => o.Kind == GoalKind.Outpost && o.Id == 1);
            Assert.That(post.OwnerFactionId, Is.EqualTo(2)); Assert.That(post.LastSeenTick, Is.EqualTo(0));
        }

        [Test]
        public void HiddenWorldChangesDoNotChangeFramesOrAiFor360Ticks()
        {
            var a = Scenario(3); a.Soldiers[1].Position = P(83); a.Soldiers[2].Position = P(231); a.Soldiers[3].Position = P(233);
            a.Rules.CoreReinforcementIntervalTicks = 100;
            var b = Scenario(3); b.Soldiers[1].Position = P(83); b.Soldiers[2].Position = P(211); b.Soldiers[3].Position = P(215);
            b.Rules.CoreReinforcementIntervalTicks = 100;
            var left = new Battle(a); var right = new Battle(b);
            for (int t = 0; t <= 360; t++)
            {
                if (t > 0) { Until(left, t); Until(right, t); }
                Assert.That(Snapshot(left.Capture(1)), Is.EqualTo(Snapshot(right.Capture(1))), "Frame tick " + t);
                var lw = typeof(Battle).GetField("world", Hidden).GetValue(left); var rw = typeof(Battle).GetField("world", Hidden).GetValue(right);
                var la = (Array)lw.GetType().GetField("Armies", Hidden).GetValue(lw); var ra = (Array)rw.GetType().GetField("Armies", Hidden).GetValue(rw);
                for (int i = 0; i < 4; i++)
                {
                    var ld = la.GetValue(i).GetType().GetField("Decision", Hidden).GetValue(la.GetValue(i));
                    var rd = ra.GetValue(i).GetType().GetField("Decision", Hidden).GetValue(ra.GetValue(i));
                    foreach (var field in ld.GetType().GetFields()) Assert.That(Snapshot(field.GetValue(ld)), Is.EqualTo(Snapshot(field.GetValue(rd))), field.Name + " tick " + t);
                }
                var ls = (Array)lw.GetType().GetField("Soldiers", Hidden).GetValue(lw); var rs = (Array)rw.GetType().GetField("Soldiers", Hidden).GetValue(rw);
                foreach (string name in new[] { "TargetKind", "TargetId", "MoveGoal" })
                    Assert.That(Snapshot(ls.GetValue(0).GetType().GetField(name, Hidden).GetValue(ls.GetValue(0))), Is.EqualTo(Snapshot(rs.GetValue(0).GetType().GetField(name, Hidden).GetValue(rs.GetValue(0)))), name + " tick " + t);
                if (t == 120) Set(right, "Soldiers", 2, "Hp", 0);
            }
        }

        [Test]
        public void HiddenObserverCannotLeakThroughReinforcementAudience()
        {
            var a = Scenario(); var b = Scenario();
            a.Rules.CoreReinforcementIntervalTicks = b.Rules.CoreReinforcementIntervalTicks = 100;
            a.Soldiers[1].Kind = b.Soldiers[1].Kind = UnitKind.Scout;
            a.Soldiers[1].ArmyId = b.Soldiers[1].ArmyId = 8;
            a.Soldiers[1].Hp = b.Soldiers[1].Hp = 40;
            a.Soldiers[1].Position = P(54); b.Soldiers[1].Position = P(231);
            var left = new Battle(a); var right = new Battle(b); Until(left, 100); Until(right, 100);
            Assert.That(left.Capture(1).Observation.VisibleEnemies, Is.Empty);
            Assert.That(right.Capture(1).Observation.VisibleEnemies, Is.Empty);
            Assert.That(left.Capture(2).Events.Any(e => e.Kind == EventKind.Reinforcement && e.Position.X == a.Cores[0].Position.X), Is.True);
            Assert.That(right.Capture(2).Events.Any(e => e.Kind == EventKind.Reinforcement && e.Position.X == a.Cores[0].Position.X), Is.False);
            Assert.That(Snapshot(left.Capture(1)), Is.EqualTo(Snapshot(right.Capture(1))));
        }

        [Test]
        public void ScoutAtTickCompletesDuringReturnAndDoesNotCrossTwelveMeters()
        {
            var s = Scenario(); s.Soldiers[0].Kind = UnitKind.Scout; s.Soldiers[0].ArmyId = 4; s.Soldiers[0].Hp = 40;
            s.Soldiers[1].Position = P(69); s.UnitParameters[1].Speed = Fix64.FromInt(4);
            var sim = new Battle(s);
            var order = new PolicyOrder(1, 0, CommandSource.Human, new ScopeKey(1, ScopeKind.Army, 4), PolicyKind.Scout,
                new PolicyGoal(GoalKind.Point, 0, P(151)), 50, new LossBudget(1000), new EndCondition(EndKind.AtTick, 8),
                0, 0, Array.Empty<PolicyVersion>(), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None));
            CommandTestInput.Step(sim, 1, new[] { new ScheduledInput(1, InputKind.Resolve, 0, 1, 1, 1, new[] { order }) });
            for (int t = 1; t <= 8; t++)
            {
                Until(sim, t);
                Assert.That(PolicyDecision.Distance(sim.Capture(1).Units.Single(u => u.IsOwn).Position, P(69)), Is.GreaterThanOrEqualTo(PolicyDecision.Distance(P(0), P(12))));
                Assert.That(sim.Capture(1).Commands.Single().Status, Is.EqualTo(t == 8 ? CommandStatus.Completed : CommandStatus.Executing));
            }
        }

        [Test]
        public void AggregateIdsNeverOverwriteIndividualApproachMemory()
        {
            var o = new FactionObservation(1, 1, Array.Empty<OwnArmyView>(), new[] { new VisibleEnemy(1, P(91), (byte)UnitKind.Infantry) },
                new[] { new EnemyContact(1, P(91), 1, 1, 1, true), new EnemyContact(1, P(151), 1, 1, 5, true, new uint[] { 1 }, isArmyContact: true) },
                new[] { new KnownObjective(GoalKind.Outpost, 1, P(101), true, 1, false, 0, 1) });
            var memory = PolicyDecision.UpdateApproaches(o, Array.Empty<ContactApproachMemory>(), new[] { new ContactApproachRoute(1, 1, 10) });
            Assert.That(memory.Single().PreviousPosition, Is.EqualTo(P(91)));
        }

        [Test]
        public void ScoutOnlyReturnsForItsOwnSightAndStopsFollowingLostEnemy()
        {
            var s = Scenario(1); s.Soldiers[0].Kind = UnitKind.Scout; s.Soldiers[0].ArmyId = 4; s.Soldiers[0].Hp = 40;
            s.Soldiers[1].Position = P(25); s.UnitParameters[1].Speed = Fix64.FromInt(6);
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, new[] { PolicyDecisionTests.Order(1, 1, ScopeKind.Army, 4, PolicyKind.Scout, new PolicyGoal(GoalKind.Point, 0, P(151)), end: EndKind.Arrived) });
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn).Position.X, Is.GreaterThan(P(81).X));
            var scout = sim.Capture(1).Units.Single(u => u.IsOwn).Position;
            Set(sim, "Soldiers", 1, "Position", new SimPoint(scout.X + Fix64.FromInt(12), scout.Z)); Until(sim, 2); Until(sim, 3);
            var returning = sim.Capture(1).Units.Single(u => u.IsOwn);
            Assert.That(returning.IsRetreating, Is.True); Assert.That(returning.Position.X, Is.LessThan(scout.X + Fix64.FromInt(1)));
            Assert.That(PolicyDecision.Distance(returning.Position, sim.Capture(1).Observation.VisibleEnemies.Single().Position), Is.GreaterThanOrEqualTo(PolicyDecision.Distance(P(0), P(12))));
            Set(sim, "Soldiers", 1, "Position", P(231)); Until(sim, 4); Until(sim, 5);
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn).Position.X, Is.LessThan(returning.Position.X));
            Until(sim, 240); Assert.That(sim.Capture(1).Commands.Single().Status, Is.EqualTo(CommandStatus.Completed));
        }
    }
}
