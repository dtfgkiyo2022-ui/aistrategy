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
    public sealed class MapOutpostTests
    {
        private static SimPoint P(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
        private static readonly ScheduledInput[] None = Array.Empty<ScheduledInput>();
        private static ScheduledInput Focus(long tick, uint faction, uint army, PolicyGoal goal)
        {
            var order = new PolicyOrder((ulong)tick, 0, CommandSource.Human, new ScopeKey(faction, ScopeKind.Army, army),
                PolicyKind.Focus, goal, 50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0, 0,
                Array.Empty<PolicyVersion>(), tick - 1, new Expiration(long.MaxValue, 0, ExpireFlags.None));
            return new ScheduledInput((ulong)tick, InputKind.Resolve, tick - 1, tick, (ulong)tick, (ulong)tick, new[] { order });
        }
        private static KnownObjective Outpost(Battle sim) => sim.Capture(1).Objectives.Single(o => o.Kind == GoalKind.Outpost && o.Id == 1);
        private static ScenarioDefinition CaptureScenario(int enemyX = 150)
        {
            var s = WeekTwoScenario.Create();
            s.UnitParameters[0].Damage = 0;
            s.Soldiers = new[] { s.Soldiers[0], s.Soldiers[20] };
            s.Soldiers[0].Position = P(128, 96);
            s.Soldiers[1].Id = 2; s.Soldiers[1].Position = P(enemyX, 96);
            return s;
        }
        private static void Step(Battle sim, int from, int to) { for (int t = from; t <= to; t++) CommandTestInput.Step(sim, t, None); }

        [Test]
        public void Exactly200TicksAndSnapshotCopies()
        {
            var s = CaptureScenario(); s.UnitParameters[0].Speed = Fix64.FromInt(0);
            var sim = new Battle(s); Step(sim, 1, 199);
            var before = Outpost(sim);
            Assert.That(before.OwnerFactionId, Is.Zero); Assert.That(before.CaptureTicks, Is.EqualTo(199));
            Assert.That(before.CapturingFactionId, Is.EqualTo(1)); Assert.That(before.CaptureDurationTicks, Is.EqualTo(200));
            CommandTestInput.Step(sim, 200, None);
            Assert.That(Outpost(sim).OwnerFactionId, Is.EqualTo(1)); Assert.That(Outpost(sim).CaptureTicks, Is.Zero);
            Assert.That(before.CaptureTicks, Is.EqualTo(199));
            Step(sim, 201, 500); Assert.That(sim.Capture(1).Units.Count, Is.EqualTo(2), "No reinforcement.");
        }

        [Test]
        public void OpponentEnteringResetsProgress()
        {
            var sim = new Battle(CaptureScenario());
            CommandTestInput.Step(sim, 1, new[] { Focus(1, 1, 1, new PolicyGoal(GoalKind.Outpost, 1, default)),
                Focus(1, 2, 5, new PolicyGoal(GoalKind.Outpost, 1, default)) });
            bool progressing = false, contested = false;
            for (int t = 2; t <= 240; t++)
            {
                CommandTestInput.Step(sim, t, None); var o = Outpost(sim);
                if (o.CaptureTicks > 0) progressing = true;
                if (progressing && o.CaptureTicks == 0 && o.OwnerFactionId == 0) { contested = true; break; }
            }
            Assert.That(contested, Is.True);
        }

        [Test]
        public void LeavingEmptyResets()
        {
            var s = CaptureScenario(); s.Soldiers = new[] { s.Soldiers[0] };
            var sim = new Battle(s); Step(sim, 1, 30);
            Assert.That(Outpost(sim).CaptureTicks, Is.GreaterThan(0));
            CommandTestInput.Step(sim, 31, new[] { Focus(31, 1, 1, new PolicyGoal(GoalKind.Point, 0, P(160, 96))) });
            Step(sim, 32, 180);
            Assert.That(Outpost(sim).CaptureTicks, Is.Zero); Assert.That(Outpost(sim).CapturingFactionId, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DeadSoldiersAndScoutsNeverContest(bool dead)
        {
            var s = CaptureScenario(128); s.UnitParameters[0].Speed = s.UnitParameters[1].Speed = Fix64.FromInt(0);
            if (dead) { s.Soldiers[1].Hp = 0; s.Soldiers[1].Alive = false; }
            else { s.Soldiers[1].Kind = UnitKind.Scout; s.Soldiers[1].Hp = 40; s.UnitParameters[1].Damage = 0; }
            var sim = new Battle(s); Step(sim, 1, 200);
            Assert.That(Outpost(sim).OwnerFactionId, Is.EqualTo(1));
        }

        [Test]
        public void MoreInfantryDoNotAccelerateCaptureAndRadiusIsInclusive()
        {
            var s = CaptureScenario(); s.UnitParameters[0].Speed = Fix64.FromInt(0);
            s.Soldiers = new[] { s.Soldiers[0], s.Soldiers[0] };
            s.Soldiers[1].Id = 2; s.Soldiers[0].Position = s.Soldiers[1].Position = P(136, 96);
            var sim = new Battle(s); Step(sim, 1, 199);
            Assert.That(Outpost(sim).CaptureTicks, Is.EqualTo(199));
            CommandTestInput.Step(sim, 200, None); Assert.That(Outpost(sim).OwnerFactionId, Is.EqualTo(1));
            s.Soldiers = new[] { s.Soldiers[0] };
            s.Soldiers[0].Position = new SimPoint(Fix64.FromRaw(Fix64.FromInt(136).Raw + 1), Fix64.FromInt(96));
            sim = new Battle(s); CommandTestInput.Step(sim, 1, None); Assert.That(Outpost(sim).CaptureTicks, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DeadOnlyOrScoutOnlyCannotCapture(bool dead)
        {
            var s = CaptureScenario(); s.Soldiers = new[] { s.Soldiers[0] };
            if (dead) { s.Soldiers[0].Hp = 0; s.Soldiers[0].Alive = false; }
            else { s.Soldiers[0].Kind = UnitKind.Scout; s.Soldiers[0].Hp = 40; s.UnitParameters[1].Speed = Fix64.FromInt(0); }
            var sim = new Battle(s); Step(sim, 1, 210);
            Assert.That(Outpost(sim).OwnerFactionId, Is.Zero); Assert.That(Outpost(sim).CaptureTicks, Is.Zero);
        }

        [Test]
        public void DeathPhasePrecedesCapture()
        {
            var s = CaptureScenario(); s.UnitParameters[0].Speed = Fix64.FromInt(0);
            s.Soldiers[1].Position = P(128, 96); s.Soldiers[0].Hp = 10;
            s.UnitParameters[0].Damage = 10;
            var sim = new Battle(s); CommandTestInput.Step(sim, 1, None);
            Assert.That(Outpost(sim).CapturingFactionId, Is.EqualTo(2));
            Assert.That(Outpost(sim).CaptureTicks, Is.EqualTo(1));
            Step(sim, 2, 200); Assert.That(Outpost(sim).OwnerFactionId, Is.EqualTo(2));
        }

        [Test]
        public void NewChallengerDoesNotInheritProgress()
        {
            var s = CaptureScenario(140); s.Soldiers[0].Hp = 10; s.UnitParameters[0].Damage = 10;
            var sim = new Battle(s);
            CommandTestInput.Step(sim, 1, new[] {
                PolicyDecisionTests.Order(1,1,ScopeKind.All,0,PolicyKind.MaintainReserve),
                PolicyDecisionTests.Order(1,2,ScopeKind.All,0,PolicyKind.MaintainReserve),
                Focus(1, 1, 1, new PolicyGoal(GoalKind.Outpost, 1, default)),
                Focus(1, 2, 5, new PolicyGoal(GoalKind.Outpost, 1, default)) });
            bool westProgress = false, switched = false;
            for (int t = 2; t <= 250; t++)
            {
                CommandTestInput.Step(sim, t, None); var o = Outpost(sim);
                if (o.CapturingFactionId == 1) westProgress = true;
                if (o.CapturingFactionId == 2)
                { Assert.That(o.CaptureTicks, Is.EqualTo(1)); switched = true; break; }
            }
            Assert.That(westProgress && switched, Is.True);
        }

        [Test]
        public void ArmyPathsReachEnemyCoreThroughBothTurns()
        {
            foreach (uint faction in new uint[] { 1, 2 })
            {
                var s = WeekTwoScenario.Create();
                s.UnitParameters[0].Damage = s.UnitParameters[1].Damage = 0;
                s.Soldiers = s.Soldiers.Where(p => p.FactionId == faction).ToArray();
                for (int i = 0; i < s.Soldiers.Length; i++) s.Soldiers[i].Id = (uint)i + 1;
                var sim = new Battle(s);
                CommandTestInput.Step(sim, 1, new[] { PolicyDecisionTests.Order(1, faction, ScopeKind.All, 0, PolicyKind.MaintainReserve) });
                CommandTestInput.Step(sim, 2, new[] { PolicyDecisionTests.Order(2, faction, ScopeKind.All, 0, PolicyKind.Focus, new PolicyGoal(GoalKind.Core, 3-faction, default)) });
                Step(sim, 3, 4500);
                foreach (var a in sim.Capture(faction).Observation.OwnArmies)
                {
                    Assert.That(Math.Abs(a.Position.X.Raw - Fix64.FromInt(faction == 1 ? 232 : 24).Raw), Is.LessThan(Fix64.FromInt(6).Raw), "army " + a.Id);
                    Assert.That(Math.Abs(a.Position.Z.Raw - Fix64.FromInt(64).Raw), Is.LessThan(Fix64.FromInt(6).Raw), "army " + a.Id);
                }
            }
        }

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static Array States(Battle sim, string name)
        {
            var world = typeof(Battle).GetField("world", Hidden).GetValue(sim);
            return (Array)world.GetType().GetField(name, Hidden).GetValue(world);
        }

        [Test]
        public void EveryNewMutableFieldChangesCanonicalState()
        {
            var sim = new Battle(WeekTwoScenario.Create()); CommandTestInput.Step(sim, 1, None);
            string[] armyFields = { "Path", "PathCursor", "AutoStage", "PathGoal", "HasPathGoal", "PathImpossible" };
            string[] outpostFields = { "OwnerFactionId", "CapturingFaction", "CaptureTicks" };
            foreach (string group in new[] { "Armies", "Outposts" })
                foreach (string name in group == "Armies" ? armyFields : outpostFields)
                {
                    var array = States(sim, group); object state = array.GetValue(0);
                    var field = state.GetType().GetField(name, Hidden); object original = field.GetValue(state);
                    var before = sim.CaptureDiagnostic();
                    object changed = field.FieldType == typeof(int[]) ? new[] { 1, 2 }
                        : field.FieldType == typeof(bool) ? (object)!(bool)original
                        : field.FieldType == typeof(SimPoint) ? P(10, 10)
                        : field.FieldType == typeof(uint) ? (object)((uint)original + 1U) : (int)original + 1;
                    field.SetValue(state, changed); array.SetValue(state, 0);
                    Assert.That(sim.CaptureDiagnostic().CanonicalState, Is.Not.EqualTo(before.CanonicalState), group + "." + name);
                    Assert.That(DiagnosticComparison.Fields(sim.CaptureDiagnostic()).Any(p => p.Key.StartsWith(group + "[1]." + name, StringComparison.Ordinal)), Is.True);
                    field.SetValue(state, original); array.SetValue(state, 0);
                }
        }

        [Test]
        public void UnchangedGoalReusesOneArmyPathAndFrameAliasIsImmutable()
        {
            var sim = new Battle(WeekTwoScenario.Create()); Step(sim, 1, 20);
            var armies = States(sim, "Armies"); var field = armies.GetValue(0).GetType().GetField("Path", Hidden);
            object path = field.GetValue(armies.GetValue(0));
            Step(sim, 21, 50);
            Assert.That(field.GetValue(armies.GetValue(0)), Is.SameAs(path));
            CommandTestInput.Step(sim, 51, new[] { PolicyDecisionTests.Order(51, 1, ScopeKind.Army, 1, PolicyKind.Defend, new PolicyGoal(GoalKind.Outpost, 1, default)) });
            Assert.That(field.GetValue(armies.GetValue(0)), Is.Not.SameAs(path));
            var frame = sim.Capture(1);
            Assert.That(frame.Objectives, Is.SameAs(frame.Observation.Objectives));
            Assert.That(((System.Collections.IList)frame.Objectives).IsReadOnly, Is.True);
        }

        private static GridMap Grid(params int[] blocked) => new GridMap(new MapDefinition {
            WidthMeters = 10, HeightMeters = 10, WidthCells = 5, HeightCells = 5, BlockedCellIds = blocked });

        [Test]
        public void AStarTieUsesFThenHThenCellIdAndIsRepeatable()
        {
            var grid = Grid();
            // East cell 1 beats north cell 5 at equal f/h; then smaller h keeps moving east.
            int[] expected = { 0, 1, 2, 7, 12 };
            Assert.That(grid.FindPath(0, P(5, 5)), Is.EqualTo(expected));
            Assert.That(grid.FindPath(0, P(5, 5)), Is.EqualTo(expected));
        }

        [Test]
        public void AStarRoutesAroundWallAndFindsNearestReachableCell()
        {
            var grid = Grid(2, 7, 12, 17);
            var path = grid.FindPath(0, P(9, 1));
            Assert.That(path.Last(), Is.EqualTo(4)); Assert.That(path.Length, Is.EqualTo(13));
            for (int i = 1; i < path.Length; i++) Assert.That(Math.Abs(path[i] % 5 - path[i - 1] % 5) + Math.Abs(path[i] / 5 - path[i - 1] / 5), Is.EqualTo(1));
            grid = Grid(2, 7, 12, 17, 22);
            Assert.That(grid.FindPath(0, P(9, 1)).Last(), Is.EqualTo(1));
            Assert.That(Grid(12).FindPath(0, P(5, 5)).Last(), Is.EqualTo(7), "Nearest distance ties use cell ID.");
        }

        [Test]
        public void BoundaryChecksStopAtWallsAndDoNotCutCorners()
        {
            var grid = Grid(1);
            var result = grid.ClipMove(P(1, 1), P(9, 1));
            Assert.That(result.X.Raw, Is.EqualTo(Fix64.FromInt(2).Raw - 1));
            Assert.That(grid.IsPassable(grid.Cell(result)), Is.True);
            Assert.That(Grid(1, 5).ClipMove(P(1, 1), P(3, 3)).X.Raw, Is.LessThan(Fix64.FromInt(2).Raw));
            Assert.That(Grid(1).ClipMove(P(5, 1), P(1, 1)).X, Is.EqualTo(Fix64.FromInt(4)));
        }

        [Test]
        public void UnreachablePointIsImpossibleAndOutpostCanBeFocused()
        {
            var sim = new Battle(WeekTwoScenario.Create());
            CommandTestInput.Step(sim, 1, new[] { Focus(1, 1, 1, new PolicyGoal(GoalKind.Point, 0, P(128, 64))) });
            Assert.That(sim.Capture(1).Commands.Last().Status, Is.EqualTo(CommandStatus.Impossible));
            Assert.That(sim.Capture(1).Commands.Last().Reason, Is.EqualTo(ReasonCode.NoPath));
            CommandTestInput.Step(sim, 2, new[] { Focus(2, 1, 1, new PolicyGoal(GoalKind.Outpost, 1, default)) });
            Assert.That(sim.Capture(1).Commands.Last().Status, Is.EqualTo(CommandStatus.Executing));
        }

        [Test]
        public void EverySoldierStaysOnRoadEveryTickAndArmiesAdvance()
        {
            var s = WeekTwoScenario.Create(); var grid = new GridMap(s.Map);
            var sim = new Battle(s);
            var soldiers = States(sim, "Soldiers");
            var position = soldiers.GetValue(0).GetType().GetField("Position", Hidden);
            for (int tick = 0; tick <= 1000; tick++)
            {
                if (tick > 0) CommandTestInput.Step(sim, tick, None);
                for (int i = 0; i < soldiers.Length; i++)
                    Assert.That(grid.IsPassable(grid.Cell((SimPoint)position.GetValue(soldiers.GetValue(i)))), Is.True, "tick=" + tick + " id=" + (i + 1));
                Assert.That(sim.Capture(1).Result.IsFault, Is.False);
            }
            Assert.That(sim.Capture(1).Observation.OwnArmies.Single(a => a.Id == 2).Position.X, Is.GreaterThan(Fix64.FromInt(80)));
            Assert.That(sim.Capture(1).Observation.OwnArmies.Single(a => a.Id == 2).Position.Z, Is.LessThan(Fix64.FromInt(40)));
            Assert.That(sim.Capture(1).Observation.OwnArmies.Single(a => a.Id == 3).Position.X, Is.LessThan(Fix64.FromInt(32)));
        }

        [Test]
        public void ScenarioBinaryPreservesGridAndExplicitFortySoldiers()
        {
            var s = WeekTwoScenario.Create(); var copy = ScenarioBinary.Decode(ScenarioBinary.Encode(s));
            Assert.That(copy.Soldiers.Length, Is.EqualTo(40));
            Assert.That(copy.Map.BlockedCellIds, Is.EqualTo(s.Map.BlockedCellIds));
            Assert.That(copy.Soldiers.Select(p => p.Position), Is.EqualTo(s.Soldiers.Select(p => p.Position)));
            var grid = new GridMap(copy.Map);
            for (int cell = 0; cell < 8192; cell++)
            {
                int x = cell % 128 * 2 + 1, z = cell / 128 * 2 + 1;
                Assert.That(grid.IsPassable(cell), Is.EqualTo((z >= 88 && z < 104) || (z >= 24 && z < 40) || (x >= 16 && x < 32) || (x >= 224 && x < 240)));
            }
        }
    }
}
