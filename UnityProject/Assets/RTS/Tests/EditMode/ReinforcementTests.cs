using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class ReinforcementTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static SimPoint P(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
        private static ScenarioDefinition Frozen()
        {
            var s = WeekTwoScenario.Create();
            for (int i = 0; i < s.UnitParameters.Length; i++) { s.UnitParameters[i].Speed = Fix64.FromInt(0); s.UnitParameters[i].Damage = 0; }
            return s;
        }
        private static void Until(Battle sim, long tick)
        {
            while (sim.Capture(1).Tick < tick) sim.Step(sim.Capture(1).Tick + 1, Array.Empty<ScheduledInput>());
        }
        private static string Field(Battle sim, string name) => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).Single(p => p.Key == name).Value;
        private static object World(Battle sim) => typeof(Battle).GetField("world", Hidden).GetValue(sim);
        private static Array States(Battle sim, string name) { var w = World(sim); return (Array)w.GetType().GetField(name, Hidden).GetValue(w); }
        private static void Set(Battle sim, string name, int index, string field, object value)
        {
            var states = States(sim, name); var item = states.GetValue(index);
            item.GetType().GetField(field, Hidden).SetValue(item, value); states.SetValue(item, index);
        }
        private static void Kill(Battle sim, int index) { Set(sim, "Soldiers", index, "Alive", false); Set(sim, "Soldiers", index, "Hp", 0); }
        private static GameEvent[] Births(Battle sim, uint faction = 1) => sim.Capture(faction).Events.Where(e => e.Kind == EventKind.Reinforcement).ToArray();

        [Test]
        public void CorePeriodFirstTickFramesAndObserverContactIds()
        {
            var sim = new Battle(Frozen());
            Assert.That(sim.Capture(1).Reinforcements.Single().TicksRemaining, Is.EqualTo(100));
            Until(sim, 99); Assert.That(Field(sim, "NextSoldierId"), Is.EqualTo("41"));
            var previous = sim.Capture(1);
            Until(sim, 100);
            Assert.That(Field(sim, "NextSoldierId"), Is.EqualTo("43"));
            Assert.That(Births(sim).Select(e => e.Position.X), Is.EqualTo(new[] { Fix64.FromInt(24), Fix64.FromInt(232) }));
            Assert.That(Births(sim)[0].SubjectId, Is.EqualTo(41));
            Assert.That(Births(sim, 2)[0].SubjectId, Is.EqualTo(sim.Capture(2).Units.Single(u => !u.IsOwn && u.Position.Equals(P(23, 63))).Id));
            Assert.That(sim.Capture(1).AliveCount, Is.EqualTo(21)); Assert.That(sim.Capture(1).FactionCap, Is.EqualTo(40));
            Assert.That(previous.AliveCount, Is.EqualTo(20));
            Assert.That(previous.Reinforcements.Single().TicksRemaining, Is.EqualTo(1));
            Until(sim, 199); Assert.That(Field(sim, "NextSoldierId"), Is.EqualTo("43"));
            Until(sim, 200); Assert.That(Field(sim, "NextSoldierId"), Is.EqualTo("45"));
            Assert.That(Field(sim, "Cores[1].NextReinforcementTick"), Is.EqualTo("300"));
        }

        [Test]
        public void CaptureStartsClockAndRecaptureResetsIt()
        {
            var s = Frozen(); s.Soldiers[0].Position = P(128, 96);
            var sim = new Battle(s); Until(sim, 199);
            Assert.That(Field(sim, "Outposts[1].NextReinforcementTick"), Is.EqualTo("0"));
            Until(sim, 200); Assert.That(Field(sim, "Outposts[1].NextReinforcementTick"), Is.EqualTo("400"));
            Until(sim, 399); Assert.That(Births(sim), Is.Empty);
            Until(sim, 400); Assert.That(Births(sim).Select(e => e.Position), Is.EqualTo(new[] { P(24,64), P(128,96), P(232,64) }));
            Assert.That(Field(sim, "Outposts[1].NextReinforcementTick"), Is.EqualTo("600"));
            for (int i = 0; i < int.Parse(Field(sim, "Soldiers.Count")); i++)
                if (Field(sim, "Soldiers[" + (i + 1) + "].FactionId") == "1") Kill(sim, i);
            Set(sim, "Soldiers", 20, "Position", P(128,96));
            Until(sim, 600);
            Assert.That(Field(sim, "Outposts[1].OwnerFactionId"), Is.EqualTo("2"));
            Assert.That(Field(sim, "Outposts[1].NextReinforcementTick"), Is.EqualTo("800"));
            Assert.That(Births(sim).Length, Is.EqualTo(2), "Recapture on the old due tick cancels that attempt.");
            Until(sim, 800); Assert.That(Births(sim).Length, Is.EqualTo(3));
        }

        [Test]
        public void FullFactionDiscardsWithoutBankingAndUsesDeathsFromThisTick()
        {
            var s = Frozen(); s.Rules.FactionCap = 20;
            var sim = new Battle(s); Until(sim, 100);
            Assert.That(Births(sim), Is.Empty); Assert.That(Field(sim, "NextSoldierId"), Is.EqualTo("41"));
            Kill(sim, 0); Until(sim, 199); Assert.That(Field(sim, "NextSoldierId"), Is.EqualTo("41"));
            Until(sim, 200); Assert.That(Births(sim).Length, Is.EqualTo(1));
            Set(sim, "Soldiers", 1, "Hp", 0); // ResolveDeaths before the due reinforcement.
            Until(sim, 300); Assert.That(Field(sim, "NextSoldierId"), Is.EqualTo("43"));
            Assert.That(sim.Capture(1).AliveCount, Is.EqualTo(20));
        }

        [TestCase(0, 1)]
        [TestCase(1, 3)]
        [TestCase(2, 2)]
        [TestCase(3, 0)]
        public void HomeThenReserveThenRemainingInfantryAndFullDiscard(int full, int expected)
        {
            var s = Frozen(); s.Rules.CoreReinforcementIntervalTicks = int.MaxValue;
            s.Outposts[0].OwnerFactionId = 1;
            if (full >= 1) s.Armies[0].Capacity = 8;
            if (full >= 2) s.Armies[2].Capacity = 2;
            if (full >= 3) s.Armies[1].Capacity = 8;
            s.Armies[3].Capacity = 10; // Scout slots never receive infantry.
            var sim = new Battle(s); Until(sim, 200);
            if (expected == 0)
            {
                Assert.That(Births(sim), Is.Empty); Kill(sim, 0); Until(sim, 399);
                Assert.That(Field(sim, "NextSoldierId"), Is.EqualTo("41"));
                Until(sim, 400); Assert.That(Field(sim, "Soldiers[41].ArmyId"), Is.EqualTo("1"));
            }
            else Assert.That(Field(sim, "Soldiers[41].ArmyId"), Is.EqualTo(expected.ToString()));
        }

        [Test]
        public void SameTickFactionCoreAndOutpostIdOrderCompetesForLastSlot()
        {
            var s = Frozen(); s.Rules.FactionCap = 21;
            s.Rules.CoreReinforcementIntervalTicks = 200;
            s.Outposts[0].OwnerFactionId = s.Outposts[1].OwnerFactionId = 1;
            var sim = new Battle(s); Until(sim, 200);
            Assert.That(Births(sim).Select(e => e.Position), Is.EqualTo(new[] { P(24,64), P(232,64) }));
            Assert.That(Field(sim, "Outposts[1].NextReinforcementTick"), Is.EqualTo("400"));
            Assert.That(Field(sim, "Outposts[2].NextReinforcementTick"), Is.EqualTo("400"));
            s.Rules.CoreReinforcementIntervalTicks = int.MaxValue;
            sim = new Battle(s); Until(sim, 200);
            Assert.That(Births(sim).Single().Position, Is.EqualTo(P(128,96)));
        }

        [Test]
        public void SpawnUsesNearestPassableCenterDistanceThenCellIdAndWaitsToAct()
        {
            var s = Frozen(); s.UnitParameters[0].Speed = Fix64.FromInt(2);
            s.Cores[0].Position = P(34,64); // Blocked origin: nearest legal center is (31,63).
            s.Outposts[0].Position = P(34,64);
            s.Soldiers = Array.Empty<SoldierDefinition>();
            var sim = new Battle(s); Until(sim, 100);
            var newborn = sim.Capture(1).Units.Single(u => u.IsOwn);
            Assert.That(newborn.Position, Is.EqualTo(P(31,63)));
            Assert.That(new GridMap(s.Map).IsPassable(new GridMap(s.Map).Cell(newborn.Position)), Is.True);
            Assert.That(newborn.IsMoving || newborn.IsAttacking, Is.False);
            Assert.That(Field(sim, "Outposts[1].CaptureTicks"), Is.EqualTo("0"));
            Until(sim, 101); Assert.That(Field(sim, "Outposts[1].CaptureTicks"), Is.EqualTo("1"));
            // Allocation runs every 20 ticks (9.2), so the newborn waits through the
            // spawning tick and the next allocation boundary before receiving movement.
            Until(sim, 120);
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn).IsMoving, Is.True);
        }

        [Test]
        public void NewbornInRangeDoesNotAttackUntilFollowingTick()
        {
            var s = Frozen(); s.Cores[1].Position = P(24,64);
            s.Soldiers = Array.Empty<SoldierDefinition>(); s.UnitParameters[0].Damage = 10;
            var sim = new Battle(s); Until(sim, 100);
            Assert.That(sim.Capture(1).Units.All(u => !u.IsAttacking && u.Hp == (u.IsOwn ? 100 : 0)), Is.True);
            Assert.That(Field(sim, "Cores[1].Hp"), Is.EqualTo("3000"));
            Until(sim, 101); Assert.That(sim.Capture(1).Units.All(u => u.IsAttacking), Is.True);
            Assert.That(Field(sim, "Soldiers[1].Hp"), Is.EqualTo("90"));
        }

        [Test]
        public void AllowAbandonKeepsOwnershipAndReinforcements()
        {
            var s = Frozen(); s.Outposts[0].OwnerFactionId = 1;
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, new[] { PolicyDecisionTests.Order(1,1,ScopeKind.Outpost,1,PolicyKind.AllowAbandon) });
            Until(sim, 200);
            Assert.That(Field(sim, "Outposts[1].OwnerFactionId"), Is.EqualTo("1"));
            Assert.That(Births(sim).Any(e => e.Position.Equals(P(128,96))), Is.True);
            Assert.That(sim.Capture(1).Reinforcements.Single(r => r.Kind == GoalKind.Outpost).TicksRemaining, Is.EqualTo(200));
        }

        [Test]
        public void TombstonesAndDoubledCapacityDoNotChangeCanonicalState()
        {
            var sim = new Battle(Frozen()); Kill(sim, 0); Until(sim, 100);
            Assert.That(States(sim, "Soldiers").Length, Is.EqualTo(80));
            Assert.That(Field(sim, "Soldiers[1].Alive"), Is.EqualTo("0"));
            Assert.That(Field(sim, "Soldiers[41].Id"), Is.EqualTo("41"));
            Assert.That(Field(sim, "Soldiers.Count"), Is.EqualTo("42"));
            var before = sim.CaptureDiagnostic();
            typeof(Battle).GetMethod("EnsureSoldierCapacity", Hidden).Invoke(sim, new object[] { 81 });
            Assert.That(States(sim, "Soldiers").Length, Is.EqualTo(160));
            Assert.That(ReplayBinary.Hash(sim.CaptureDiagnostic().CanonicalState.ToArray()), Is.EqualTo(ReplayBinary.Hash(before.CanonicalState.ToArray())));
            var other = new Battle(Frozen()); Kill(other, 0); Until(other, 100);
            Until(sim, 300); Until(other, 300);
            Assert.That(DiagnosticComparison.First(sim.CaptureDiagnostic(), other.CaptureDiagnostic()), Is.Null);
            var traversal = (int[])States(sim, "SoldierTraversal");
            Assert.That(traversal.Select(i => Field(sim, "Soldiers[" + (i+1) + "].ArmyId")), Is.Ordered);
        }

        [Test]
        public void RecruitUsesArmyPolicyWithoutChangingAllocationPeriod()
        {
            var s = Frozen(); s.Outposts[0].OwnerFactionId = 1;
            s.UnitParameters[0].Speed = Fix64.FromInt(2);
            var sim = new Battle(s);
            Until(sim, 199);
            CommandTestInput.Step(sim, 200, new[] { PolicyDecisionTests.Order(200,1,ScopeKind.Army,1,PolicyKind.Retreat) });
            var recruit = Births(sim).Single(e => e.Position.Equals(P(128,96)));
            Assert.That(Field(sim, "Soldiers[" + recruit.SubjectId + "].ArmyId"), Is.EqualTo("1"));
            Assert.That(Field(sim, "Ai.LastAllocationTick"), Is.EqualTo("200"));
            Until(sim, 201);
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn && u.Id == recruit.SubjectId).IsRetreating, Is.True);
            Assert.That(Field(sim, "Ai.LastAllocationTick"), Is.EqualTo("200"));
        }

        [Test]
        public void ClocksAndNextIdAffectCanonicalStateAndDestroyedCoreCannotSpawn()
        {
            var sim = new Battle(Frozen()); var initial = sim.CaptureDiagnostic();
            Set(sim,"Cores",0,"NextReinforcementTick",101L);
            Assert.That(DiagnosticComparison.First(initial,sim.CaptureDiagnostic()), Is.Not.Null);
            Set(sim,"Cores",0,"NextReinforcementTick",100L);
            Set(sim,"Outposts",0,"NextReinforcementTick",200L);
            Assert.That(DiagnosticComparison.First(initial,sim.CaptureDiagnostic()), Is.Not.Null);
            Set(sim,"Outposts",0,"NextReinforcementTick",0L);
            var world = World(sim); world.GetType().GetField("NextSoldierId",Hidden).SetValue(world,40U);
            Assert.That(DiagnosticComparison.First(initial,sim.CaptureDiagnostic()), Is.Not.Null);
            world.GetType().GetField("NextSoldierId",Hidden).SetValue(world,41U);
            Until(sim,99); Set(sim,"Cores",0,"Hp",0); Until(sim,100);
            Assert.That(Births(sim).Select(e => e.Position), Is.EqualTo(new[] { P(232,64) }));
            Assert.That(sim.Capture(1).Reinforcements.Any(r => r.Kind == GoalKind.Core), Is.False);
        }
    }
}
