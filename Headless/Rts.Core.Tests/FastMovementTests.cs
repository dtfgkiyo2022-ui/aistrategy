using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Rts.Tests.EditMode;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class FastMovementTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void FlagOffPreservesEveryTickHashAndHasNoFastMovementExtension()
        {
            var source = MapGenerator.GenerateTerrain(1, processingChain: true);
            source.Economy.FastMovement = false;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Extensions.Any(e => e.Id == 75), Is.False);

            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(Hash(left), Is.EqualTo(Hash(right)), "tick " + tick);
            }
        }

        [Test]
        public void Extension75RoundTripsFastMovementRules()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            var small = LiveGameRules.Create(1, false, true, false, false);
            Assert.That(small.Economy.LargeEconomy, Is.False);
            Assert.That(small.Economy.FastMovement, Is.False);
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(e => e.Id == 75);

            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(3 * sizeof(int)));
            Assert.That(decoded.Economy.FastMovement, Is.True);
            Assert.That(decoded.Economy.FastMovementUnitSpeedPermille, Is.EqualTo(2000));
            Assert.That(decoded.Economy.FastMovementVillagerSpeedPermille, Is.EqualTo(2000));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void FastMovementDoublesOneTickStepsForUnitsAndVillagers()
        {
            var onRules = LiveGameRules.Create(1, true, true, false, false).Economy;
            var offRules = LiveGameRules.Create(1, true, true, false, false).Economy;
            offRules.FastMovement = false;

            var infantry = Fix64.FromRaw(20 * 6553);
            var cavalry = Fix64.FromRaw(20 * 13107);
            var villager = Fix64.FromRaw(20 * 6553);
            Assert.That(MovementMath.StepDistance(infantry, onRules).Raw,
                Is.EqualTo(MovementMath.StepDistance(infantry, offRules).Raw * 2));
            Assert.That(MovementMath.StepDistance(cavalry, onRules).Raw,
                Is.EqualTo(MovementMath.StepDistance(cavalry, offRules).Raw * 2));
            Assert.That(MovementMath.StepDistance(villager, onRules, villager: true).Raw,
                Is.EqualTo(MovementMath.StepDistance(villager, offRules, villager: true).Raw * 2));

            var on = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var offScenario = LiveGameRules.Create(1, true, true, false, false);
            offScenario.Economy.FastMovement = false;
            var off = new Battle(offScenario);
            Assert.That(Math.Abs(SoldierStep(on, 0).Raw - SoldierStep(off, 0).Raw * 2), Is.LessThanOrEqualTo(1));
            Assert.That(Math.Abs(VillagerStep(on).Raw - VillagerStep(off).Raw * 2), Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void FastMovementSoldierStopsAtPointWithoutOscillation()
        {
            var scenario = MapGenerator.GenerateTerrain(1, processingChain: true);
            scenario.Economy.FastMovement = true;
            scenario.Economy.DirectPathMovement = true;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.AiFactory = false;
            scenario.Economy.AiExpand = false;
            scenario.Economy.FreeTowns = false;
            var simulation = new Battle(scenario);
            var goal = new SimPoint(Fix64.FromInt(160), Fix64.FromInt(64));
            CommandTestInput.Step(simulation, 1, new[] { Focus(1, 1, goal) });
            var positions = new List<SimPoint>();
            for (int tick = 2; tick <= 2400; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                positions.Add(simulation.Capture(1).Observation.OwnArmies.Single(a => a.Id == 1).Position);
            }

            long tail = 0;
            for (int i = Math.Max(1, positions.Count - 600); i < positions.Count; i++)
                tail += PositionDelta(positions[i], positions[i - 1]);
            var final = positions[positions.Count - 1];
            Assert.That(tail, Is.LessThanOrEqualTo(2L * 65536), "movement continued or oscillated after arrival");
            Assert.That(Squared(final, goal), Is.LessThanOrEqualTo(4L * 65536 * 65536), "destination was not reached");
        }

        [Test, Explicit("long movement/economy run")]
        public void FastMovementVillagersCompleteGatherAndDropOffRoundTrips()
        {
            var simulation = new Battle(LiveGameRules.Create(1, true, true, false, false));
            bool gathered = false, returned = false;
            for (long tick = 1; tick <= 14400 && !simulation.Capture(1).Result.HasEnded; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                var villagers = simulation.Capture(1).Economy.Villagers;
                gathered |= villagers.Any(v => v.Activity == VillagerActivity.Gathering);
                returned |= villagers.Any(v => v.Activity == VillagerActivity.Returning);
            }
            Assert.That(gathered, Is.True, "no villager reached a resource");
            Assert.That(returned, Is.True, "no villager completed a return leg");
        }

        private static ScheduledInput Focus(long tick, uint army, SimPoint goal)
        {
            var order = new PolicyOrder(1, 0, CommandSource.Human,
                new ScopeKey(1, ScopeKind.Army, army), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, goal), 50, new LossBudget(1000),
                new EndCondition(EndKind.UntilReplaced, 0), 0, 0,
                Array.Empty<PolicyVersion>(), tick - 1,
                new Expiration(long.MaxValue, 0, ExpireFlags.None));
            return new ScheduledInput(1, InputKind.Resolve, tick - 1, tick, 1, 1, new[] { order });
        }

        private static object World(Battle battle) => typeof(Battle).GetField("world", Hidden).GetValue(battle);

        private static Fix64 SoldierStep(Battle battle, int index)
        {
            var world = World(battle);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Hidden).GetValue(world);
            var state = soldiers.GetValue(index);
            return (Fix64)state.GetType().GetField("StepDistance", Hidden).GetValue(state);
        }

        private static Fix64 VillagerStep(Battle battle)
            => (Fix64)World(battle).GetType().GetField("VillagerStep", Hidden).GetValue(World(battle));

        private static string Hash(Battle battle) => Convert.ToHexString(ReplayBinary.Hash(battle.CaptureDiagnostic().CanonicalState));

        private static long PositionDelta(SimPoint a, SimPoint b)
            => Math.Abs(a.X.Raw - b.X.Raw) + Math.Abs(a.Z.Raw - b.Z.Raw);

        private static long Squared(SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw;
            long dz = a.Z.Raw - b.Z.Raw;
            return checked(dx * dx + dz * dz);
        }
    }
}
