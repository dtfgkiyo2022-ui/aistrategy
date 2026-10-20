using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>Perf7: visibility ranges and integer movement preserve the old values exactly.</summary>
    public sealed class Perf7DeterminismTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void VisibilityCountsMatchTheLegacyCirclesAfterEveryMovedUnitTick()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            var optimized = new Battle(scenario);
            var legacy = new Battle(scenario, null, false, true);
            Assert.That(Counts(optimized, 0), Is.EqualTo(Counts(legacy, 0)), "initial faction 1");
            Assert.That(Counts(optimized, 1), Is.EqualTo(Counts(legacy, 1)), "initial faction 2");
            for (long tick = 1; tick <= 300 && (!optimized.Capture(1).Result.HasEnded || !legacy.Capture(1).Result.HasEnded); tick++)
            {
                optimized.Step(tick, Array.Empty<ScheduledInput>());
                legacy.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(Counts(optimized, 0), Is.EqualTo(Counts(legacy, 0)), "faction 1 tick " + tick);
                Assert.That(Counts(optimized, 1), Is.EqualTo(Counts(legacy, 1)), "faction 2 tick " + tick);
            }
        }

        [Test]
        public void VisibilityRangesMatchTheLegacyCirclesOnSmallMapBoundaries()
        {
            var scenario = SmallVisibilityScenario();
            var optimized = new Battle(scenario);
            var legacy = new Battle(scenario, null, false, true);
            var probes = new[]
            {
                Point(9, 9),   // A cell-centre-aligned source: the 20 m radius edge is exact.
                Point(11, 9),  // One cell.
                Point(10, 9),  // Half a cell.
                Point(11, 11), // Diagonal movement.
                Point(9, 9)    // Back to the exact-radius position.
            };
            foreach (var position in probes)
            {
                SetSoldierPosition(optimized, 0, position);
                SetSoldierPosition(legacy, 0, position);
                InvokeUpdateVisibility(optimized);
                InvokeUpdateVisibility(legacy);
                Assert.That(Counts(optimized, 0), Is.EqualTo(Counts(legacy, 0)), "faction 1 at " + position);
                Assert.That(Counts(optimized, 1), Is.EqualTo(Counts(legacy, 1)), "faction 2 at " + position);
            }
        }

        [Test]
        public void ScaledWideLiveMatchHashesMatchTheLegacyPathEveryTick()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            ScenarioScale.MultiplyForMeasurement(scenario, 4, 8);
            var optimized = new Battle(scenario);
            var legacy = new Battle(scenario, null, true, true);
            // 4000 ticks: two full canonical-state copies per tick on the scaled large map ran the full suite out of memory
            // at 12000 (10-20). The 12000-tick comparison is the recorded-replay check (record with the previous build,
            // replay with this one), which streams the hashes instead of holding both states.
            for (long tick = 1; tick <= 4000 && (!optimized.Capture(1).Result.HasEnded || !legacy.Capture(1).Result.HasEnded); tick++)
            {
                optimized.Step(tick, Array.Empty<ScheduledInput>());
                legacy.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(optimized.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(legacy.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
            Assert.That(optimized.Capture(1).Tick, Is.EqualTo(legacy.Capture(1).Tick));
            Assert.That(optimized.Capture(1).Result.WinnerFactionId, Is.EqualTo(legacy.Capture(1).Result.WinnerFactionId));
        }

        [Test]
        public void MoveTowardsFastAndBigIntegerRoutesMatchOneMillionFixedInputs()
        {
            var cases = new List<(long X, long Z, long TargetX, long TargetZ, long Step)>
            {
                (0, 0, 1, 1, 0), (0, 0, 1, 1, 1), (0, 0, 1, 1, 2),
                (0, 0, 3, 4, 4), (0, 0, 3, 4, 5), (0, 0, -3, -4, 4),
                (0, 0, long.MaxValue, 0, long.MaxValue - 1),
                (0, 0, long.MaxValue, 0, long.MaxValue),
                (long.MinValue, long.MinValue / 2, -1, 0, long.MaxValue - 1),
                (long.MaxValue, long.MaxValue / 2, 0, 0, long.MaxValue - 1),
                (long.MinValue, 0, -1, long.MaxValue, long.MaxValue),
                (long.MinValue / 2, long.MaxValue / 3, long.MaxValue / 2, long.MinValue / 3, long.MaxValue / 3),
                (long.MaxValue / 2, long.MinValue / 3, long.MinValue / 2, long.MaxValue / 3, 1)
            };
            ulong state = 0x9E3779B97F4A7C15UL;
            for (int i = cases.Count; i < 1_000_000; i++)
            {
                long x = Next(ref state, 1_000_000_000), z = Next(ref state, 1_000_000_000);
                long targetX = Next(ref state, 1_000_000_000), targetZ = Next(ref state, 1_000_000_000);
                long step = 1 + (long)(NextUnsigned(ref state) % 1_000_000_000UL);
                cases.Add((x, z, targetX, targetZ, step));
            }
            for (int i = 0; i < cases.Count; i++)
            {
                var c = cases[i];
                var current = new SimPoint(Fix64.FromRaw(c.X), Fix64.FromRaw(c.Z));
                var target = new SimPoint(Fix64.FromRaw(c.TargetX), Fix64.FromRaw(c.TargetZ));
                SimPoint fast, reference;
                try
                {
                    fast = FixMath.MoveTowards(current, target, Fix64.FromRaw(c.Step));
                    reference = FixMath.MoveTowardsBigInteger(current, target, Fix64.FromRaw(c.Step));
                }
                catch (Exception error)
                {
                    Assert.Fail("input " + i + " (" + c + ") threw " + error.GetType().Name + ": " + error.Message);
                    return;
                }
                Assert.That((fast.X.Raw, fast.Z.Raw), Is.EqualTo((reference.X.Raw, reference.Z.Raw)), "input " + i);
            }
        }

        private static int[] Counts(Battle simulation, int faction)
        {
            object world = typeof(Battle).GetField("world", Hidden).GetValue(simulation);
            var factions = (Array)world.GetType().GetField("Factions", Hidden).GetValue(world);
            object state = factions.GetValue(faction);
            return ((Array)state.GetType().GetField("VisibleCounts", Hidden).GetValue(state)).Cast<int>().ToArray();
        }

        private static ScenarioDefinition SmallVisibilityScenario()
        {
            var scenario = WeekOneScenario.Create();
            scenario.ScenarioId = "perf7-small-visibility";
            scenario.Map = new MapDefinition
            {
                WidthMeters = 40, HeightMeters = 24, CellSizeMeters = 2,
                WidthCells = 20, HeightCells = 12, DefaultPassable = true,
                BlockedCellIds = Array.Empty<int>()
            };
            scenario.Cores[0].Position = Point(2, 12);
            scenario.Cores[1].Position = Point(38, 12);
            scenario.Outposts[0].Position = Point(20, 4);
            scenario.Outposts[1].Position = Point(20, 20);
            for (int i = 0; i < scenario.Soldiers.Length; i++)
            {
                bool west = i < scenario.Soldiers.Length / 2;
                int slot = i % (scenario.Soldiers.Length / 2);
                int x = west ? 6 + (slot % 4) * 2 : 28 + (slot % 4) * 2;
                int z = 5 + (slot / 4) * 4;
                scenario.Soldiers[i].Position = Point(x, z);
            }
            scenario.Soldiers[0].Position = Point(9, 9);
            return scenario;
        }

        private static void SetSoldierPosition(Battle simulation, int index, SimPoint position)
        {
            object world = typeof(Battle).GetField("world", Hidden).GetValue(simulation);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Hidden).GetValue(world);
            object state = soldiers.GetValue(index);
            state.GetType().GetField("Position", Hidden).SetValue(state, position);
            soldiers.SetValue(state, index);
        }

        private static void InvokeUpdateVisibility(Battle simulation)
            => typeof(Battle).GetMethod("UpdateVisibility", Hidden).Invoke(simulation, null);

        private static SimPoint Point(int x, int z)
            => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        private static ulong NextUnsigned(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static long Next(ref ulong state, long magnitude)
            => (long)(NextUnsigned(ref state) % (ulong)(magnitude * 2 + 1)) - magnitude;
    }
}
