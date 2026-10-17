using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ExploreScoutingTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static SimPoint P(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        [Test]
        public void ExploreDecisionChoosesNearestEligibleChunkAndLowerNumberOnTie()
        {
            var explored = Enumerable.Repeat(true, 8 * 4).ToArray();
            var passable = Enumerable.Repeat(true, explored.Length).ToArray();
            for (int cell = 0; cell < 4; cell++) explored[cell] = false;
            var target = ExploreDecision.NextTarget(explored, 8, 4, 1, 2, 500, P(1, 0), passable,
                Array.Empty<ExploreAvoidChunk>(), 0, 0);
            Assert.That(target.HasTarget, Is.True);
            Assert.That(target.ChunkX, Is.EqualTo(0));
            Assert.That(target.ChunkZ, Is.EqualTo(0));
        }

        [Test]
        public void ExploreDecisionSkipsFinishedBlockedAndAvoidedChunks()
        {
            var explored = Enumerable.Repeat(true, 8 * 4).ToArray();
            var passable = Enumerable.Repeat(true, explored.Length).ToArray();
            for (int cell = 2; cell < 4; cell++) explored[cell] = false;
            for (int cell = 4; cell < 6; cell++) explored[cell] = false;
            passable[4] = false; passable[5] = false; passable[12] = false; passable[13] = false;
            var avoid = new[] { new ExploreAvoidChunk(1, 0, 100) };
            var target = ExploreDecision.NextTarget(explored, 8, 4, 1, 2, 500, P(0, 0), passable, avoid, 50, 0);
            Assert.That(target.HasTarget, Is.False);
        }

        [Test]
        public void ExploreDecisionReturnsHomeWhenEveryCellIsExplored()
        {
            var explored = Enumerable.Repeat(true, 8 * 4).ToArray();
            var passable = Enumerable.Repeat(true, explored.Length).ToArray();
            var target = ExploreDecision.NextTarget(explored, 8, 4, 1, 2, 500, P(0, 0), passable,
                Array.Empty<ExploreAvoidChunk>(), 0, 1);
            Assert.That(target.HasTarget, Is.False);
        }

        [Test]
        public void ExploreScoutingExtensionRoundTripsAllNonDefaultsAndIsAbsentWhenOff()
        {
            var off = MapGenerator.GenerateTerrain(1);
            var offWithIgnoredTuning = MapGenerator.GenerateTerrain(1);
            offWithIgnoredTuning.Economy.ExploreChunkCells = 5;
            offWithIgnoredTuning.Economy.ExploreMinUnexploredPermille = 731;
            offWithIgnoredTuning.Economy.ExploreRetargetTicks = 37;
            offWithIgnoredTuning.Economy.ExploreAvoidChunks = 2;
            offWithIgnoredTuning.Economy.ExploreAvoidTicks = 901;
            offWithIgnoredTuning.Economy.ExploreAutoUntilTick = 4321;
            Assert.That(ScenarioBinary.Encode(offWithIgnoredTuning), Is.EqualTo(ScenarioBinary.Encode(off)));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 50), Is.False);

            var oldSetting = MapGenerator.Generate(1, false);
            var oldSettingWithIgnoredTuning = MapGenerator.Generate(1, false);
            oldSettingWithIgnoredTuning.Economy.ExploreChunkCells = 5;
            oldSettingWithIgnoredTuning.Economy.ExploreMinUnexploredPermille = 731;
            oldSettingWithIgnoredTuning.Economy.ExploreRetargetTicks = 37;
            oldSettingWithIgnoredTuning.Economy.ExploreAvoidChunks = 2;
            oldSettingWithIgnoredTuning.Economy.ExploreAvoidTicks = 901;
            oldSettingWithIgnoredTuning.Economy.ExploreAutoUntilTick = 4321;
            var left = new Battle(oldSetting);
            var right = new Battle(oldSettingWithIgnoredTuning);
            for (int tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
            }
            Assert.That(right.Capture(1).Tick, Is.EqualTo(left.Capture(1).Tick));
            Assert.That(right.CaptureDiagnostic().CanonicalState.ToArray(),
                Is.EqualTo(left.CaptureDiagnostic().CanonicalState.ToArray()));

            var on = MapGenerator.GenerateTerrain(1);
            on.Economy.ExploreScouting = true;
            on.Economy.ExploreChunkCells = 5;
            on.Economy.ExploreMinUnexploredPermille = 731;
            on.Economy.ExploreRetargetTicks = 37;
            on.Economy.ExploreAvoidChunks = 2;
            on.Economy.ExploreAvoidTicks = 901;
            on.Economy.ExploreAutoUntilTick = 4321;
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 50).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.ExploreScouting, Is.True);
            Assert.That(decoded.Economy.ExploreChunkCells, Is.EqualTo(5));
            Assert.That(decoded.Economy.ExploreMinUnexploredPermille, Is.EqualTo(731));
            Assert.That(decoded.Economy.ExploreRetargetTicks, Is.EqualTo(37));
            Assert.That(decoded.Economy.ExploreAvoidChunks, Is.EqualTo(2));
            Assert.That(decoded.Economy.ExploreAvoidTicks, Is.EqualTo(901));
            Assert.That(decoded.Economy.ExploreAutoUntilTick, Is.EqualTo(4321));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void AutomaticScoutPatrolStartsBeforeTheExistingAi()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.ExploreScouting = true;
            scenario.Economy.ExploreAutoUntilTick = 10;
            scenario.Economy.DirectPathMovement = true;
            var simulation = new Battle(scenario);

            simulation.Step(1, Array.Empty<ScheduledInput>());

            var scout = simulation.Capture(1).Observation.OwnArmies
                .Single(a => a.Kind == UnitKind.Scout);
            Assert.That(scout.PatrolObjective.Kind, Is.EqualTo(GoalKind.Point));
        }

        [Test]
        public void DestinationlessScoutOrderActivatesManualPatrol()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.ExploreScouting = true;
            scenario.Economy.ExploreAutoUntilTick = 0;
            scenario.Economy.DirectPathMovement = true;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var scout = scenario.Soldiers.First(s => s.FactionId == 1 && s.Kind == UnitKind.Scout).ArmyId;
            var target = new ScopeKey(1, ScopeKind.Army, scout);
            gateway.Submit(new UserPolicyIntent(1, target, PolicyKind.Scout,
                new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 50, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));

            for (int i = 0; i < 60; i++) gateway.Step();

            var frame = simulation.Capture(1);
            var army = frame.Observation.OwnArmies.Single(a => a.Id == scout);
            Assert.That(army.PatrolObjective.Kind, Is.EqualTo(GoalKind.Point));
            Assert.That(frame.Commands.Any(c => c.Target.Equals(target)
                && c.Kind == PolicyKind.Scout && c.Goal.Kind == GoalKind.None), Is.True);
        }

        [Test]
        public void ExploreScoutingOffMatchesTheLegacyScenarioAtEveryTick()
        {
            var legacy = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.ExploreChunkCells = 5;
            off.Economy.ExploreMinUnexploredPermille = 731;
            off.Economy.ExploreRetargetTicks = 37;
            off.Economy.ExploreAvoidChunks = 2;
            off.Economy.ExploreAvoidTicks = 901;
            off.Economy.ExploreAutoUntilTick = 4321;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(legacy)));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 50), Is.False);

            var left = new Battle(legacy);
            var right = new Battle(off);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(right.CaptureDiagnostic().CanonicalState.ToArray(),
                    Is.EqualTo(left.CaptureDiagnostic().CanonicalState.ToArray()), "tick " + tick);
            }
            Assert.That(right.Capture(1).Tick, Is.EqualTo(left.Capture(1).Tick));
            Assert.That(right.Capture(1).Result.HasEnded, Is.EqualTo(left.Capture(1).Result.HasEnded));
        }

        [Test]
        public void PatrolExploresMoreCellsThanTheSameSeedWithoutPatrolAtTick3600()
        {
            var withPatrol = PatrolScenario(1);
            withPatrol.Economy.ExploreAutoUntilTick = 3600;
            var withoutPatrol = PatrolScenario(1);
            withoutPatrol.Economy.ExploreScouting = false;
            var explored = new Battle(withPatrol);
            var baseline = new Battle(withoutPatrol);
            for (long tick = 1; tick <= 3600; tick++)
            {
                explored.Step(tick, Array.Empty<ScheduledInput>());
                baseline.Step(tick, Array.Empty<ScheduledInput>());
            }

            int patrolCells = explored.Capture(1).Fog.ExploredCells.Count(x => x);
            int baselineCells = baseline.Capture(1).Fog.ExploredCells.Count(x => x);
            TestContext.WriteLine($"tick=3600 patrol={patrolCells} baseline={baselineCells}");
            Assert.That(patrolCells, Is.GreaterThan(baselineCells));
        }

        [Test]
        public void ScoutReturnsFromVisibleEnemyAndResumesInAChunkOutsideTheAvoidanceRadius()
        {
            var scenario = PatrolScenario(1);
            scenario.Economy.ExploreAutoUntilTick = 0;
            scenario.Economy.ExploreAvoidChunks = 1;
            scenario.Economy.ExploreAvoidTicks = 1200;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            uint scoutArmy = scenario.Soldiers.First(s => s.FactionId == 1 && s.Kind == UnitKind.Scout).ArmyId;
            uint enemySoldier = scenario.Soldiers.First(s => s.FactionId == 2 && s.Kind == UnitKind.Infantry).Id;
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, scoutArmy), PolicyKind.Scout,
                new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 50, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            for (int i = 0; i < 60; i++) gateway.Step();

            var before = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == scoutArmy).PatrolObjective;
            Assert.That(before.Kind, Is.EqualTo(GoalKind.Point));
            var scoutPosition = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == scoutArmy).Position;
            SetSoldierPosition(simulation, enemySoldier, scoutPosition);

            bool sawReturn = false;
            bool wasReturning = false;
            bool returnedHome = false;
            bool resumed = false;
            PolicyGoal after = default(PolicyGoal);
            long returnTick = 0, homeTick = 0, resumeTick = 0;
            int avoidChunkX = 0, avoidChunkZ = 0;
            for (int i = 0; i < 2400; i++)
            {
                if (!sawReturn) SetSoldierPosition(simulation, enemySoldier, scoutPosition);
                gateway.Step();
                bool returning = AnyArmySoldierReturning(simulation, scoutArmy);
                if (!sawReturn && returning)
                {
                    returnTick = simulation.Capture(1).Tick;
                    avoidChunkX = ArmyField<int>(simulation, scoutArmy, "ExploreAvoidChunkX");
                    avoidChunkZ = ArmyField<int>(simulation, scoutArmy, "ExploreAvoidChunkZ");
                }
                sawReturn |= returning;
                if (wasReturning && !returning)
                {
                    returnedHome = true;
                    homeTick = simulation.Capture(1).Tick;
                }
                wasReturning = returning;
                var army = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == scoutArmy);
                if (returnedHome && army.PatrolObjective.Kind == GoalKind.Point
                    && !SamePoint(army.PatrolObjective.Point, before.Point))
                {
                    resumed = true;
                    resumeTick = simulation.Capture(1).Tick;
                    after = army.PatrolObjective;
                    break;
                }
            }

            long avoidUntil = ArmyField<long>(simulation, scoutArmy, "ExploreAvoidUntilTick");
            TestContext.WriteLine($"returnTick={returnTick} homeTick={homeTick} resumeTick={resumeTick} "
                + $"afterChunk=({ChunkX(scenario, after.Point)},{ChunkZ(scenario, after.Point)}) "
                + $"avoid=({avoidChunkX},{avoidChunkZ}) "
                + $"avoidUntil={avoidUntil}");
            Assert.That(sawReturn, Is.True, "The scout never entered the return state after seeing the enemy.");
            Assert.That(returnedHome, Is.True, "The scout did not finish its return to the home core.");
            Assert.That(resumed, Is.True, "The scout did not select a new patrol target after returning home.");
            bool insideAvoidance = Math.Abs(ChunkX(scenario, after.Point) - avoidChunkX) <= scenario.Economy.ExploreAvoidChunks
                && Math.Abs(ChunkZ(scenario, after.Point) - avoidChunkZ) <= scenario.Economy.ExploreAvoidChunks;
            Assert.That(insideAvoidance, Is.False, "The resumed target was still inside the avoided chunk radius.");
        }

        [Test]
        public void PatrolMovementDoesNotReverseEveryTickOrRepeatATwoCycle()
        {
            var scenario = PatrolScenario(1);
            scenario.Economy.ExploreAutoUntilTick = 3600;
            var simulation = new Battle(scenario);
            var positions = new List<SimPoint>();
            for (long tick = 1; tick <= 3600; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                positions.Add(simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 4).Position);
            }

            int twoCycles = 0, cycleRun = 0, maxCycleRun = 0, reverseRun = 0, maxReverseRun = 0;
            long lastDx = 0, lastDz = 0;
            var cycleSamples = new List<string>();
            for (int i = 1; i < positions.Count; i++)
            {
                long dx = positions[i].X.Raw - positions[i - 1].X.Raw;
                long dz = positions[i].Z.Raw - positions[i - 1].Z.Raw;
                bool cycle = i >= 2 && SamePoint(positions[i], positions[i - 2]) &&
                    !SamePoint(positions[i], positions[i - 1]);
                if (cycle)
                {
                    twoCycles++;
                    cycleRun++;
                    maxCycleRun = Math.Max(maxCycleRun, cycleRun);
                    if (cycleSamples.Count < 12)
                        cycleSamples.Add($"i={i} p0={positions[i - 2].X.Raw},{positions[i - 2].Z.Raw} p1={positions[i - 1].X.Raw},{positions[i - 1].Z.Raw} p2={positions[i].X.Raw},{positions[i].Z.Raw}");
                }
                else cycleRun = 0;
                if (dx == 0 && dz == 0) continue;
                long dot = dx * lastDx + dz * lastDz;
                if (lastDx != 0 || lastDz != 0)
                {
                    if (dot < 0) reverseRun++;
                    else reverseRun = 0;
                    maxReverseRun = Math.Max(maxReverseRun, reverseRun);
                }
                lastDx = dx; lastDz = dz;
            }

            TestContext.WriteLine($"twoCycles={twoCycles} maxCycleRun={maxCycleRun} maxReverseRun={maxReverseRun} {string.Join("; ", cycleSamples)}");
            Assert.That(maxCycleRun, Is.LessThanOrEqualTo(2));
            Assert.That(maxReverseRun, Is.LessThanOrEqualTo(3));
        }

        [Test]
        public void AutomaticPatrolStopsAfterExploreAutoUntilTickAndLeavesNormalAiInControl()
        {
            var scenario = PatrolScenario(1);
            scenario.Economy.ExploreAutoUntilTick = 120;
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 120; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 4).PatrolObjective.Kind,
                Is.EqualTo(GoalKind.Point));
            simulation.Step(121, Array.Empty<ScheduledInput>());
            var after = simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 4);
            Assert.That(after.PatrolObjective.Kind, Is.EqualTo(GoalKind.None));
            Assert.That(ArmyField<bool>(simulation, 4, "ExplorePatrolActive"), Is.False);
            for (long tick = 122; tick <= 180; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 4).PatrolObjective.Kind,
                Is.EqualTo(GoalKind.None));
        }

        [Test]
        public void HumanFocusReplacesDestinationlessScoutPatrol()
        {
            var scenario = PatrolScenario(1);
            scenario.Economy.ExploreAutoUntilTick = 0;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            uint scout = scenario.Soldiers.First(s => s.FactionId == 1 && s.Kind == UnitKind.Scout).ArmyId;
            var target = new ScopeKey(1, ScopeKind.Army, scout);
            gateway.Submit(new UserPolicyIntent(1, target, PolicyKind.Scout,
                new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 50, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            for (int i = 0; i < 60; i++) gateway.Step();
            Assert.That(simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == scout).PatrolObjective.Kind,
                Is.EqualTo(GoalKind.Point));

            var focus = P(160, 64);
            gateway.Submit(new UserPolicyIntent(1, target, PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, focus), 50, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            for (int i = 0; i < 60; i++) gateway.Step();
            var frame = simulation.Capture(1);
            Assert.That(frame.Observation.OwnArmies.Single(a => a.Id == scout).PatrolObjective.Kind,
                Is.EqualTo(GoalKind.None));
            Assert.That(frame.Commands.Any(c => c.Target.Equals(target) && c.Kind == PolicyKind.Focus
                && c.Goal.Kind == GoalKind.Point && c.Status == CommandStatus.Executing), Is.True);
        }

        [Test]
        public void ReplayOfAnExploreScoutingMatchMatchesEveryTick()
        {
            var scenario = PatrolScenario(3);
            scenario.Economy.ExploreAutoUntilTick = 2400;
            var expected = new List<byte[]>();
            using (var stream = new MemoryStream())
            {
                var recorded = ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 2400,
                    new BuildIdentity(), (state, hash, events) => expected.Add(hash.Concat(events).ToArray()));
                Assert.That(recorded.IsFault, Is.False);
                stream.Position = 0;
                int count = 0;
                var replayed = ReplayRunner.Replay(stream, new BuildIdentity(), (state, hash, events) =>
                {
                    Assert.That(hash.Concat(events).ToArray(), Is.EqualTo(expected[count]), "tick " + state.Tick);
                    count++;
                });
                Assert.That(replayed.FirstMismatchTick, Is.Null);
                Assert.That(count, Is.EqualTo(expected.Count));
            }
        }

        private static ScenarioDefinition PatrolScenario(ulong seed)
        {
            var scenario = MapGenerator.GenerateTerrain(seed);
            scenario.Economy.ExploreScouting = true;
            scenario.Economy.DirectPathMovement = true;
            scenario.Rules.CoreReinforcementIntervalTicks = int.MaxValue;
            scenario.Rules.OutpostReinforcementIntervalTicks = int.MaxValue;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            for (int i = 0; i < scenario.UnitParameters.Length; i++) scenario.UnitParameters[i].Damage = 0;
            return scenario;
        }

        private static int ExploredCells(Battle simulation, uint faction)
            => simulation.Capture(faction).Fog.ExploredCells.Count(x => x);

        private static int ChunkX(ScenarioDefinition scenario, SimPoint point)
            => (int)(point.X.Raw / Fix64.FromInt(scenario.Map.CellSizeMeters).Raw) / scenario.Economy.ExploreChunkCells;

        private static int ChunkZ(ScenarioDefinition scenario, SimPoint point)
            => (int)(point.Z.Raw / Fix64.FromInt(scenario.Map.CellSizeMeters).Raw) / scenario.Economy.ExploreChunkCells;

        private static bool SamePoint(SimPoint left, SimPoint right) => left.X.Raw == right.X.Raw && left.Z.Raw == right.Z.Raw;

        private static long DistanceSquared(SimPoint left, SimPoint right)
        {
            long dx = left.X.Raw - right.X.Raw;
            long dz = left.Z.Raw - right.Z.Raw;
            return checked(dx * dx + dz * dz);
        }

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", Hidden).GetValue(simulation);

        private static object Soldier(Battle simulation, uint id)
        {
            var world = World(simulation);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Hidden).GetValue(world);
            return soldiers.GetValue((int)id - 1);
        }

        private static void SetSoldierPosition(Battle simulation, uint id, SimPoint position)
        {
            var world = World(simulation);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Hidden).GetValue(world);
            object soldier = soldiers.GetValue((int)id - 1);
            soldier.GetType().GetField("Position", Hidden).SetValue(soldier, position);
            soldiers.SetValue(soldier, (int)id - 1);
        }

        private static bool SoldierReturning(Battle simulation, uint id)
        {
            object soldier = Soldier(simulation, id);
            object pursuit = soldier.GetType().GetField("Pursuit", Hidden).GetValue(soldier);
            return (bool)pursuit.GetType().GetField("Returning", Hidden).GetValue(pursuit);
        }

        private static bool AnyArmySoldierReturning(Battle simulation, uint armyId)
        {
            var world = World(simulation);
            var armies = (Array)world.GetType().GetField("Armies", Hidden).GetValue(world);
            object army = armies.GetValue((int)armyId - 1);
            var ids = (uint[])army.GetType().GetField("SoldierIds", Hidden).GetValue(army);
            return ids.Any(id => SoldierReturning(simulation, id));
        }

        private static T ArmyField<T>(Battle simulation, uint armyId, string field)
        {
            var world = World(simulation);
            var armies = (Array)world.GetType().GetField("Armies", Hidden).GetValue(world);
            object army = armies.GetValue((int)armyId - 1);
            return (T)army.GetType().GetField(field, Hidden).GetValue(army);
        }
    }
}
