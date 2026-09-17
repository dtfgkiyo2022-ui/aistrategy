using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    /// <summary>Chapter 5.1/9.2 sentry rules: immobile, unallocated, uncounted, unreinforced.</summary>
    public sealed class SentryTests
    {
        private static readonly ScheduledInput[] None = Array.Empty<ScheduledInput>();
        private static SimPoint P(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
        private static string Raw(int meters) => Fix64.FromInt(meters).Raw.ToString(CultureInfo.InvariantCulture);
        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static void Until(Battle sim, long tick)
        { while (sim.Capture(1).Tick < tick) CommandTestInput.Step(sim, sim.Capture(1).Tick + 1, None); }
        private static Dictionary<string, string> Fields(Battle sim) =>
            DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
        private static string Field(Battle sim, string name) => Fields(sim)[name];

        private static SoldierDefinition Sentry(uint id, uint faction, SimPoint position) => new SoldierDefinition
        { Id = id, FactionId = faction, ArmyId = 8 + faction, Kind = UnitKind.Sentry, Alive = true, Hp = 800, Position = position };
        private static SoldierDefinition Infantry(uint id, uint faction, SimPoint position) => new SoldierDefinition
        { Id = id, FactionId = faction, ArmyId = (faction - 1) * 4 + 1, Kind = UnitKind.Infantry, Alive = true, Hp = 100, Position = position };

        /// <summary>Week-one map (everything passable) with the periodic reinforcements switched off.</summary>
        private static ScenarioDefinition Isolated(params SoldierDefinition[] soldiers)
        {
            var s = WeekOneScenario.Create();
            s.Rules.CoreReinforcementIntervalTicks = s.Rules.OutpostReinforcementIntervalTicks = int.MaxValue;
            s.Soldiers = soldiers;
            return s;
        }

        [Test]
        public void EveryScenarioPlacesTwoSentriesInFrontOfEachCore()
        {
            foreach (var s in new[] { WeekOneScenario.Create(), WeekTwoScenario.Create() })
            {
                var grid = new GridMap(s.Map);
                var sentries = s.Soldiers.Where(p => p.Kind == UnitKind.Sentry).ToArray();
                Assert.That(sentries.Length, Is.EqualTo(4));
                foreach (uint faction in new uint[] { 1, 2 })
                {
                    var own = sentries.Where(p => p.FactionId == faction).ToArray();
                    Assert.That(own.Length, Is.EqualTo(2));
                    var core = s.Cores.Single(c => c.FactionId == faction).Position;
                    foreach (var p in own)
                    {
                        Assert.That(p.ArmyId, Is.EqualTo(8 + faction), "Sentry armies are appended as 9 and 10.");
                        Assert.That(grid.IsPassable(grid.Cell(p.Position)), Is.True, "A sentry must stand on passable terrain.");
                        // Inside the 8 m hold radius, on the enemy side of its own core.
                        Assert.That(PolicyDecision.Within(p.Position, core, 8), Is.True);
                        Assert.That(p.Position.X.Raw, faction == 1 ? Is.GreaterThan(core.X.Raw) : Is.LessThan(core.X.Raw));
                    }
                }
                Assert.That(s.Factions.Select(f => f.ArmyIds.Length), Is.All.EqualTo(5));
            }
        }

        [Test]
        public void SentriesNeverMoveAndNeverReportMovement()
        {
            var definition = WeekTwoScenario.Create();
            var expected = definition.Soldiers.Where(p => p.Kind == UnitKind.Sentry).ToDictionary(p => p.Id, p => p.Position);
            var sim = new Battle(definition);
            for (long tick = 0; tick <= 200; tick++)
            {
                if (tick > 0) CommandTestInput.Step(sim, tick, None);
                var d = Fields(sim);
                foreach (var pair in expected)
                {
                    string n = "Soldiers[" + pair.Key.ToString(CultureInfo.InvariantCulture) + "].";
                    string context = "tick=" + tick + " id=" + pair.Key;
                    Assert.That(d[n + "Position.X.Raw"], Is.EqualTo(pair.Value.X.Raw.ToString(CultureInfo.InvariantCulture)), context);
                    Assert.That(d[n + "Position.Z.Raw"], Is.EqualTo(pair.Value.Z.Raw.ToString(CultureInfo.InvariantCulture)), context);
                    Assert.That(d[n + "IsMoving"], Is.EqualTo("0"), context);
                    Assert.That(d[n + "IsRetreating"], Is.EqualTo("0"), context);
                }
            }
        }

        [TestCase(63, 20)]  // Both sentries are within the 8 m range: 2 x 40 damage.
        [TestCase(79, 100)] // Visible and inside the 24 m core leash, but beyond the weapon range.
        public void SentryAttacksOnlyWithinItsRangeAndStillHoldsItsPosition(int enemyZ, int expectedHp)
        {
            var s = Isolated(Sentry(1, 1, P(30, 60)), Sentry(2, 1, P(30, 68)), Infantry(3, 2, P(30, enemyZ)));
            s.UnitParameters[0].Speed = Fix64.FromInt(0); // Keep the target where it is placed.
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, None);
            var d = Fields(sim);
            Assert.That(d["Soldiers[3].Hp"], Is.EqualTo(Number(expectedHp)));
            Assert.That(d["Soldiers[1].IsAttacking"], Is.EqualTo(expectedHp == 100 ? "0" : "1"));
            Assert.That(d["Soldiers[1].Position.Z.Raw"], Is.EqualTo(Raw(60)));
            Assert.That(d["Soldiers[2].Position.Z.Raw"], Is.EqualTo(Raw(68)));
            Assert.That(d["Soldiers[1].Position.X.Raw"], Is.EqualTo(Raw(30)));
        }

        [Test]
        public void SentryNeitherCapturesNorContestsAnOutpost()
        {
            var sim = new Battle(Isolated(Sentry(1, 1, P(128, 96))));
            Until(sim, 210);
            var d = Fields(sim);
            Assert.That(d["Outposts[1].OwnerFactionId"], Is.EqualTo("0"));
            Assert.That(d["Outposts[1].CaptureTicks"], Is.EqualTo("0"));
            Assert.That(d["Outposts[1].CapturingFaction"], Is.EqualTo("0"));
        }

        [Test]
        public void SentriesNeitherConsumeTheFactionCapNorReceiveReinforcements()
        {
            var s = WeekTwoScenario.Create();
            for (int i = 0; i < s.UnitParameters.Length; i++)
            { s.UnitParameters[i].Speed = Fix64.FromInt(0); s.UnitParameters[i].Damage = 0; }
            // 20 infantry + 2 sentries per faction: a spawn is only possible if sentries are exempt.
            s.Rules.FactionCap = 21;
            s.Armies[2].Capacity = 2; // The west reserve army is full.
            s.Armies[8].Capacity = 4; // The west sentry army has room and must still be skipped.
            var sim = new Battle(s);
            Until(sim, 100);
            Assert.That(sim.Capture(1).Events.Any(e => e.Kind == EventKind.Reinforcement), Is.True,
                "A cap-exempt sentry must not cost the faction its reinforcement.");
            Assert.That(Field(sim, "Soldiers[45].ArmyId"), Is.EqualTo("1"),
                "Sentry armies never receive infantry, even when they have a free slot.");
        }

        [Test]
        public void SentryArmiesAreNeverAllocatedOrPartOfTheCoordinatedOffence()
        {
            var sim = new Battle(WeekTwoScenario.Create());
            for (long tick = 0; tick <= 200; tick++)
            {
                if (tick > 0) CommandTestInput.Step(sim, tick, None);
                var d = Fields(sim);
                foreach (uint army in new uint[] { 9, 10 })
                {
                    string n = "Ai.Armies[" + army.ToString(CultureInfo.InvariantCulture) + "].";
                    string context = "tick=" + tick + " army=" + army;
                    Assert.That(d[n + "Assignment"], Is.EqualTo(Number((byte)AssignmentKind.CoreDefense)), context);
                    Assert.That(d[n + "Goal.Kind"], Is.EqualTo(Number((byte)GoalKind.Core)), context);
                    Assert.That(d[n + "Returning"], Is.EqualTo("0"), context);
                    Assert.That(d[n + "InferiorTicks"], Is.EqualTo("0"), context);
                }
                foreach (var pair in d.Where(p => p.Key.Contains(".Offense.") &&
                    (p.Key.Contains("Planned[") || p.Key.Contains("Joining[") || p.Key.Contains("Advancing[") || p.Key.Contains("CommittedReserve["))))
                    Assert.That(pair.Value, Is.Not.EqualTo("9").And.Not.EqualTo("10"), pair.Key + " tick=" + tick);
            }
        }

        [Test]
        public void HumanOrdersCannotTargetASentryArmy()
        {
            var sim = new Battle(WeekTwoScenario.Create());
            CommandTestInput.Step(sim, 1, new[] { PolicyDecisionTests.Order(1, 1, ScopeKind.Army, 9,
                PolicyKind.Defend, new PolicyGoal(GoalKind.Core, 1, default)) });
            var rejected = sim.Capture(1).Commands.Last();
            Assert.That(rejected.Status, Is.EqualTo(CommandStatus.Impossible));
            Assert.That(rejected.Reason, Is.EqualTo(ReasonCode.InvalidPayload));
            CommandTestInput.Step(sim, 2, new[] { PolicyDecisionTests.Order(2, 1, ScopeKind.All, 0, PolicyKind.Retreat) });
            var d = Fields(sim);
            Assert.That(d["Commands[Army=1].Policy"], Is.EqualTo(Number((byte)PolicyKind.Retreat)));
            Assert.That(d["Commands[Army=9].Policy"], Is.EqualTo("0"), "An All-scope order never reaches a sentry army.");
        }

        [Test]
        public void EnemySentriesAreVisibleButNeverCountedInAnEstimate()
        {
            var seen = new Battle(Isolated(Infantry(1, 1, P(226, 64)),
                Sentry(2, 2, P(226, 60)), Sentry(3, 2, P(226, 68)))).Capture(1).Observation;
            Assert.That(seen.VisibleEnemies.Count, Is.EqualTo(2));
            Assert.That(seen.VisibleEnemies.All(e => e.Kind == (byte)UnitKind.Sentry), Is.True);
            Assert.That(seen.Contacts.Count, Is.EqualTo(2), "Contacts must exist so that tactics can target a sentry.");
            Assert.That(seen.Contacts.All(c => c.IsFixedDefense && !c.IsArmyContact), Is.True,
                "A sentry army never becomes an aggregate contact.");
            Assert.That(PolicyDecision.CountableContacts(seen).Count(), Is.Zero);
            Assert.That(PolicyDecision.Estimate(seen, P(226, 64)), Is.Zero);

            var counted = new Battle(Isolated(Infantry(1, 1, P(226, 64)),
                Sentry(2, 2, P(226, 60)), Sentry(3, 2, P(226, 68)), Infantry(4, 2, P(227, 64)))).Capture(1).Observation;
            Assert.That(PolicyDecision.Estimate(counted, P(226, 64)), Is.GreaterThan(0),
                "An ordinary enemy in the same place is still counted.");
        }

        /// <summary>
        /// Regression for the pursuit trap: an enemy inside the 24 m core leash but outside the
        /// sentry's 8 m range used to start a pursuit, which flipped to "returning" after 60 ticks
        /// and never cleared, because an immobile unit can never travel back within 1 m of its
        /// mission. The sentry then stopped selecting targets for the rest of the match.
        /// </summary>
        [Test]
        public void OutOfRangeEnemyNeverTrapsASentryInThePursuitCycle()
        {
            // 8.94 m from the sentry (outside its 8 m range), 14 m from the core (inside the leash).
            var s = Isolated(Sentry(1, 1, P(30, 60)), Infantry(2, 2, P(38, 64)));
            s.UnitParameters[0].Speed = Fix64.FromInt(0); // Hold the enemy at that distance.
            var sim = new Battle(s);
            for (long tick = 1; tick <= 200; tick++)
            {
                CommandTestInput.Step(sim, tick, None);
                var d = Fields(sim);
                string context = "tick=" + tick;
                Assert.That(d["Ai.Soldiers[1].Pursuit.Active"], Is.EqualTo("0"), context);
                Assert.That(d["Ai.Soldiers[1].Pursuit.Returning"], Is.EqualTo("0"), context);
                Assert.That(d["Soldiers[1].Position.X.Raw"], Is.EqualTo(Raw(30)), context);
            }
            // Still able to fire the moment something does enter range.
            var closer = Isolated(Sentry(1, 1, P(30, 60)), Infantry(2, 2, P(38, 64)), Infantry(3, 2, P(30, 64)));
            closer.UnitParameters[0].Speed = Fix64.FromInt(0);
            var engaged = new Battle(closer);
            Until(engaged, 100);
            Assert.That(int.Parse(Field(engaged, "Soldiers[3].Hp"), CultureInfo.InvariantCulture), Is.LessThan(100),
                "A sentry held by a distant enemy must still shoot one that is in range.");
        }
    }
}
