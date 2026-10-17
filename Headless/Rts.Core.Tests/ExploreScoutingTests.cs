using System;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Decision;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ExploreScoutingTests
    {
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
    }
}
