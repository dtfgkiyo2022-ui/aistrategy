using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class SimulationTests
    {
        private static readonly ScheduledInput[] NoInputs = Array.Empty<ScheduledInput>();
        private static SimPoint Point(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
        private static ScenarioDefinition Duel(int hp = 100, int damage = 10, int interval = 20)
        {
            var s = WeekOneScenario.Create();
            s.UnitParameters[0].Speed = Fix64.FromRaw(0);
            s.UnitParameters[0].Hp = hp;
            s.UnitParameters[0].Damage = damage;
            s.UnitParameters[0].AttackIntervalTicks = interval;
            s.Soldiers = new[]
            {
                new SoldierDefinition { Id = 1, FactionId = 1, ArmyId = 1, Kind = UnitKind.Infantry, Alive = true, Hp = hp, Position = Point(100, 64) },
                new SoldierDefinition { Id = 2, FactionId = 2, ArmyId = 5, Kind = UnitKind.Infantry, Alive = true, Hp = hp, Position = Point(102, 64) }
            };
            return s;
        }

        private static ScheduledInput Input(long tick, uint faction, uint army, PolicyKind kind, PolicyGoal goal = default, ulong command = 1)
        {
            var order = new PolicyOrder(command, 0, CommandSource.Human, new ScopeKey(faction, ScopeKind.Army, army), kind,
                goal, 50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0, 0,
                Array.Empty<PolicyVersion>(), tick - 1, new Expiration(long.MaxValue, 0, ExpireFlags.None));
            return new ScheduledInput(command, InputKind.Resolve, tick - 1, tick, command, command, new[] { order });
        }

        private static int Hp(Battle sim, uint faction) => sim.Capture(faction).Units.Single(u => u.IsOwn).Hp;
        private static KnownObjective Core(Battle sim, uint id) => sim.Capture(1).Observation.Objectives.Single(o => o.Kind == GoalKind.Core && o.Id == id);

        [Test]
        public void SimultaneousLethalHitsLeaveBothSoldiersDead()
        {
            var sim = new Battle(Duel(10, 10));
            int retainedBefore = RetainedSoldierAndContactFields(sim);
            CommandTestInput.Step(sim, 1, NoInputs);
            Assert.That(sim.Capture(1).Units, Is.Empty);
            Assert.That(sim.Capture(2).Units, Is.Empty);
            Assert.That(sim.Capture(1).Observation.OwnArmies.All(a => a.AliveCount == 0), Is.True);
            Assert.That(RetainedSoldierAndContactFields(sim), Is.EqualTo(retainedBefore), "Tombstone slots and contact allocations are retained.");
        }

        [TestCase(0, 90)]
        [TestCase(1, 100)]
        public void SoldierRangeUsesExactRawBoundary(long extraRaw, int expectedHp)
        {
            var s = Duel();
            s.Soldiers[1].Position = new SimPoint(Fix64.FromRaw(Fix64.FromInt(102).Raw + extraRaw), Fix64.FromInt(64));
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, NoInputs);
            Assert.That(Hp(sim, 1), Is.EqualTo(expectedHp));
            Assert.That(Hp(sim, 2), Is.EqualTo(expectedHp));
        }

        [TestCase(0, 2990)]
        [TestCase(1, 3000)]
        public void CoreRangeIncludesRadiusAtExactRawBoundary(long extraRaw, int expectedHp)
        {
            var s = Duel();
            s.Soldiers = new[] { s.Soldiers[0] };
            s.Soldiers[0].Position = new SimPoint(Fix64.FromRaw(Fix64.FromInt(226).Raw - extraRaw), Fix64.FromInt(64));
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, NoInputs);
            Assert.That(Core(sim, 2).Hp, Is.EqualTo(expectedHp));
        }

        [Test]
        public void BothCoresDestroyedOnSameTickIsDrawAndTerminal()
        {
            var s = Duel();
            s.Cores[0].Hp = s.Cores[1].Hp = 10;
            s.Soldiers[0].Position = Point(232, 64);
            s.Soldiers[1].Position = Point(24, 64);
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, NoInputs);
            Assert.That(Core(sim, 1).Hp, Is.Zero);
            Assert.That(Core(sim, 2).Hp, Is.Zero);
            Assert.That(sim.Capture(1).Result.IsDraw, Is.True);
            Assert.That(sim.Capture(1).Result.WinnerFactionId, Is.Zero);
            var bytes = sim.CaptureDiagnostic().CanonicalState;
            CommandTestInput.Step(sim, 2, new[] { Input(2, 1, 1, PolicyKind.Retreat) });
            CommandTestInput.Step(sim, 999, null);
            Assert.That(sim.CaptureDiagnostic().CanonicalState, Is.EqualTo(bytes));
            Assert.That(sim.Capture(1).Tick, Is.EqualTo(1));
        }

        [Test]
        public void CooldownIsMaintainedAcrossRetreatAndFocus()
        {
            var sim = new Battle(Duel());
            CommandTestInput.Step(sim, 1, new[] {
                PolicyDecisionTests.Order(1,1,ScopeKind.All,0,PolicyKind.MaintainReserve),
                PolicyDecisionTests.Order(1,2,ScopeKind.All,0,PolicyKind.MaintainReserve) });
            CommandTestInput.Step(sim, 2, new[] { Input(2, 1, 1, PolicyKind.Retreat) });
            CommandTestInput.Step(sim, 3, new[] { Input(3, 1, 1, PolicyKind.Focus, new PolicyGoal(GoalKind.Core, 2, default)) });
            for (int t = 4; t <= 20; t++) CommandTestInput.Step(sim, t, NoInputs);
            Assert.That(Hp(sim, 1), Is.EqualTo(90));
            Assert.That(Hp(sim, 2), Is.EqualTo(90));
            CommandTestInput.Step(sim, 21, NoInputs);
            Assert.That(Hp(sim, 1), Is.EqualTo(80));
            Assert.That(Hp(sim, 2), Is.EqualTo(80));
        }

        [Test]
        public void RetreatDisablesAttacksButNotIncomingDamage()
        {
            var sim = new Battle(Duel());
            CommandTestInput.Step(sim, 1, new[] { Input(1, 1, 1, PolicyKind.Retreat) });
            Assert.That(Hp(sim, 1), Is.EqualTo(90));
            Assert.That(Hp(sim, 2), Is.EqualTo(100));
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn).IsRetreating, Is.True);
            Assert.That(sim.Capture(2).Units.Single(u => !u.IsOwn).IsRetreating, Is.False, "Enemy policy is private.");
        }

        [Test]
        public void MovementTruncatesSpeedOnceAndAppliesLatestInputBeforeAi()
        {
            var s = Duel();
            s.UnitParameters[0].Speed = Fix64.FromRaw(65539);
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, new[] { Input(1, 1, 1, PolicyKind.Retreat),
                Input(1, 1, 1, PolicyKind.Focus, new PolicyGoal(GoalKind.Point, 0, Point(110, 64)), 2) });
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn).Position.X.Raw, Is.EqualTo(Fix64.FromInt(100).Raw + 65539 / 20));
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn).IsRetreating, Is.False);
            CommandTestInput.Step(sim, 2, new[] { Input(2, 1, 1, PolicyKind.Retreat) });
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn).Position.X.Raw, Is.EqualTo(Fix64.FromInt(100).Raw));
        }

        [Test]
        public void MovementDoesNotOvershootPointGoal()
        {
            var s = Duel();
            s.UnitParameters[0].Speed = Fix64.FromInt(2);
            var goal = new SimPoint(Fix64.FromRaw(Fix64.FromInt(100).Raw + 1), Fix64.FromInt(64));
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, new[] { Input(1, 1, 1, PolicyKind.Focus, new PolicyGoal(GoalKind.Point, 0, goal)) });
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn).Position.X, Is.EqualTo(goal.X));
        }

        [Test]
        public void NewlyInRangeEnemyIsSelectedNextTick()
        {
            var s = Duel();
            s.UnitParameters[0].Speed = Fix64.FromInt(2);
            s.Soldiers[1].Position = new SimPoint(Fix64.FromRaw(Fix64.FromInt(102).Raw + 1), Fix64.FromInt(64));
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, new[] {
                Input(1, 1, 1, PolicyKind.Focus, new PolicyGoal(GoalKind.Point, 0, Point(100, 64))),
                Input(1, 2, 5, PolicyKind.Focus, new PolicyGoal(GoalKind.Point, 0, Point(100, 64)), 2) });
            Assert.That(Hp(sim, 1), Is.EqualTo(100));
            CommandTestInput.Step(sim, 2, NoInputs);
            Assert.That(Hp(sim, 1), Is.EqualTo(90));
        }

        private static int RetainedSoldierAndContactFields(Battle sim) => Rts.Replay.DiagnosticComparison.Fields(sim.CaptureDiagnostic())
            .Count(field => field.Key.StartsWith("Soldiers[", StringComparison.Ordinal) || field.Key.Contains("ContactIds"));

        [Test]
        public void TargetMovingOutOfRangeIsNotHit()
        {
            var s = Duel();
            s.UnitParameters[0].Speed = Fix64.FromInt(2);
            var sim = new Battle(s);
            // Initially exactly in range: west selects east, then east moves away before attack resolution.
            CommandTestInput.Step(sim, 1, new[] { Input(1, 1, 1, PolicyKind.Focus, new PolicyGoal(GoalKind.Point, 0, Point(100, 64))),
                Input(1, 2, 5, PolicyKind.Retreat) });
            Assert.That(Hp(sim, 2), Is.EqualTo(100));
            Assert.That(sim.Capture(1).Units.Single(u => u.IsOwn).IsAttacking, Is.False);
        }

        [Test]
        public void TargetTiePrefersSoldierBeforeCore()
        {
            var s = Duel();
            s.Cores[1].Position = s.Soldiers[1].Position;
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, NoInputs);
            Assert.That(Hp(sim, 2), Is.EqualTo(90));
            Assert.That(Core(sim, 2).Hp, Is.EqualTo(3000));
        }

        [Test]
        public void SoldiersPrecedeCoreAndContactIdBreaksEqualDistanceTie()
        {
            var s = Duel();
            s.Soldiers = new[] { s.Soldiers[0], s.Soldiers[1], s.Soldiers[1] };
            s.Soldiers[2].Id = 3;
            s.Soldiers[1].ArmyId = 6; // Contact IDs follow faction/army/soldier observation traversal.
            s.Soldiers[2].ArmyId = 5;
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, NoInputs);
            Assert.That(sim.Capture(2).Units.Single(u => u.IsOwn && u.Id == 2).Hp, Is.EqualTo(100));
            Assert.That(sim.Capture(2).Units.Single(u => u.IsOwn && u.Id == 3).Hp, Is.EqualTo(90));
            s.Cores[1].Position = Point(101, 64);
            sim = new Battle(s);
            CommandTestInput.Step(sim, 1, NoInputs);
            Assert.That(Core(sim, 2).Hp, Is.EqualTo(3000));
            Assert.That(sim.Capture(2).Units.Single(u => u.IsOwn && u.Id == 2).Hp, Is.EqualTo(100));
        }

        [Test]
        public void IndependentSimulationsMatchEveryTickFor2000TicksWithInputs()
        {
            var a = new Battle(WeekOneScenario.Create());
            var b = new Battle(WeekOneScenario.Create());
            CommandTestInput.AssertCanonicalEqual(a.CaptureDiagnostic(),b.CaptureDiagnostic());
            for (int tick = 1; tick <= 2000; tick++)
            {
                ScheduledInput[] inputs = tick == 200 ? new[] { Input(tick, 1, 1, PolicyKind.Retreat) }
                    : tick == 400 ? new[] { Input(tick, 1, 1, PolicyKind.Focus, new PolicyGoal(GoalKind.Core, 2, default)) }
                    : tick == 800 ? new[] { Input(tick, 2, 6, PolicyKind.Focus, new PolicyGoal(GoalKind.Point, 0, Point(128, 32))) }
                    : NoInputs;
                CommandTestInput.Step(a, tick, inputs);
                a.Capture(2); a.Capture(1); // Capture frequency/order must have no effect.
                CommandTestInput.Step(b, tick, inputs);
                CommandTestInput.AssertCanonicalEqual(a.CaptureDiagnostic(),b.CaptureDiagnostic(),"tick "+tick);
            }
        }

        [Test]
        public void TenVersusTenAutoRunsToVictoryOrScenarioLimitWithoutFault()
        {
            var scenario = WeekOneScenario.Create();
            var sim = new Battle(scenario);
            for (int tick = 1; tick <= scenario.VerificationTickLimit && !sim.Capture(1).Result.HasEnded; tick++) CommandTestInput.Step(sim, tick, NoInputs);
            var frame = sim.Capture(1);
            TestContext.WriteLine("10v10 end tick: " + frame.Tick + ", winner: " + frame.Result.WinnerFactionId + ", draw: " + frame.Result.IsDraw);
            Assert.That(frame.Result.IsFault, Is.False);
            if (frame.Result.HasEnded)
                Assert.That(Core(sim, 1).Hp == 0 || Core(sim, 2).Hp == 0, Is.True);
            else
            {
                Assert.That(frame.Tick, Is.EqualTo(scenario.VerificationTickLimit));
                Assert.That(Core(sim, 1).Hp > 0 && Core(sim, 2).Hp > 0, Is.True);
            }
        }

        [Test]
        public void ContactsAreFactionLocalFirstObservationNumbersAndCaptureIsPure()
        {
            var s = Duel();
            // A dead initial soldier consumes an internal ID but no contact number.
            s.Soldiers = new[] { s.Soldiers[0], s.Soldiers[1], s.Soldiers[1] };
            s.Soldiers[1].Alive = false; s.Soldiers[1].Hp = 0;
            s.Soldiers[2].Id = 3;
            var sim = new Battle(s);
            var before = sim.CaptureDiagnostic().CanonicalState;
            var enemy = sim.Capture(1).Units.Single(u => !u.IsOwn);
            Assert.That(enemy.Id, Is.EqualTo(1));
            Assert.That(enemy.HasHp, Is.False); Assert.That(enemy.Hp, Is.Zero);
            Assert.That(sim.Capture(1).Observation.VisibleEnemies.Single().ContactId, Is.EqualTo(enemy.Id));
            Assert.That(sim.Capture(2).Units.Single(u => !u.IsOwn).Id, Is.EqualTo(1));
            Assert.That(sim.CaptureDiagnostic().CanonicalState, Is.EqualTo(before));
            var oldFrame = sim.Capture(1);
            CommandTestInput.Step(sim, 1, NoInputs);
            Assert.That(oldFrame.Tick, Is.Zero);
            Assert.That(oldFrame.Units.Single(u => u.IsOwn).Hp, Is.EqualTo(100));
            Assert.That(sim.Capture(1).Units.Single(u => !u.IsOwn).Id, Is.EqualTo(enemy.Id));
        }

        [Test]
        public void InitialArrayOrderAndCallerMutationsDoNotChangeSimulation()
        {
            var s = WeekOneScenario.Create();
            var a = new Battle(s);
            Array.Reverse(s.Soldiers); Array.Reverse(s.Armies); Array.Reverse(s.Cores);
            Array.Reverse(s.Outposts); Array.Reverse(s.UnitParameters); Array.Reverse(s.Factions);
            foreach (var f in s.Factions) Array.Reverse(f.ArmyIds);
            var b = new Battle(s);
            s.Soldiers[0].Hp = 0; s.Map.WidthMeters = 1; s.Rules.CoreRadius = Fix64.FromInt(100);
            s.Factions[0].ArmyIds[0] = 0;
            Assert.That(a.CaptureDiagnostic().CanonicalState, Is.EqualTo(b.CaptureDiagnostic().CanonicalState));
            CommandTestInput.Step(a, 1, NoInputs); CommandTestInput.Step(b, 1, NoInputs);
            Assert.That(a.CaptureDiagnostic().CanonicalState, Is.EqualTo(b.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void InvalidTickOrUnsupportedInputDoesNotPartiallyApply()
        {
            var sim = new Battle(Duel());
            var before = sim.CaptureDiagnostic().CanonicalState;
            Assert.Throws<ArgumentOutOfRangeException>(() => CommandTestInput.Step(sim, 0, NoInputs));
            Assert.Throws<ArgumentOutOfRangeException>(() => CommandTestInput.Step(sim, 2, NoInputs));
            Assert.Throws<ArgumentException>(() => sim.Step(1, new ScheduledInput[] { null }));
            Assert.Throws<ArgumentNullException>(() => sim.Step(1, null));
            Assert.That(sim.CaptureDiagnostic().CanonicalState, Is.EqualTo(before));
        }

        [Test]
        public void InvalidScenarioIsRejected()
        {
            var s = Duel(); s.Soldiers[1].Id = 1;
            Assert.Throws<ArgumentException>(() => new Battle(s));
            s = Duel(); s.Map.DefaultPassable = false;
            Assert.Throws<ArgumentException>(() => new Battle(s));
            s = Duel(); s.UnitParameters[0].Speed = Fix64.FromInt(17);
            Assert.Throws<ArgumentException>(() => new Battle(s));
        }

        [Test]
        public void LargeSimultaneousDamageDoesNotOverflowIntAccumulator()
        {
            var s = Duel(int.MaxValue, int.MaxValue);
            s.Soldiers = new[] { s.Soldiers[0], s.Soldiers[1], s.Soldiers[0] };
            s.Soldiers[2].Id = 3;
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, NoInputs);
            Assert.That(sim.Capture(1).Result.IsFault, Is.False);
            Assert.That(sim.Capture(1).Units.Count, Is.EqualTo(1));
        }

        [Test]
        public void DiagnosticIncludesParametersThatChangeFutureBehavior()
        {
            var s = Duel(); var a = new Battle(s);
            s.UnitParameters[0].AttackIntervalTicks++;
            var b = new Battle(s);
            Assert.That(a.CaptureDiagnostic().CanonicalState, Is.Not.EqualTo(b.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void SimulationAssemblyHasNoUnityOrUnexpectedProjectDependency()
        {
            foreach (var reference in typeof(Battle).Assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False);
                if (reference.Name.StartsWith("Rts.", StringComparison.Ordinal))
                    Assert.That(reference.Name, Is.EqualTo("Rts.Contracts").Or.EqualTo("Rts.Decision"));
            }
        }

        [Test]
        public void TickArithmeticOverflowProducesTerminalFaultNotVictory()
        {
            var sim = new Battle(Duel());
            // Reach the boundary without executing long.MaxValue ticks; production exposes no state setter.
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            object state = typeof(Battle).GetField("world", flags).GetValue(sim);
            state.GetType().GetField("Tick", flags).SetValue(state, long.MaxValue - 1);
            CommandTestInput.Step(sim, long.MaxValue, NoInputs);
            var result = sim.Capture(1).Result;
            Assert.That(result.HasEnded && result.IsFault, Is.True);
            Assert.That(result.IsDraw, Is.False);
            Assert.That(result.WinnerFactionId, Is.Zero);
            var diagnostic = sim.CaptureDiagnostic().CanonicalState;
            CommandTestInput.Step(sim, long.MaxValue, NoInputs);
            Assert.That(sim.CaptureDiagnostic().CanonicalState, Is.EqualTo(diagnostic));
        }
    }
}
