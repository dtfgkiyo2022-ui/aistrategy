using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class SpatialCombatDeterminismTests
    {
        [Test]
        public void SpatialCombatIndexKeepsTwoIdenticalRunsBitStable()
        {
            var left = new Battle(LiveGameRules.Create(1, false, true, false, false));
            var right = new Battle(LiveGameRules.Create(1, false, true, false, false));
            for (long tick = 1; tick <= 1200; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
        }

        [Test]
        public void OptimizedSpatialCombatMatchesLegacyEveryTick()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            var optimized = new Battle(scenario);
            var legacy = new Battle(scenario, null, true, true);
            for (long tick = 1; tick <= 30000 && (!optimized.Capture(1).Result.HasEnded || !legacy.Capture(1).Result.HasEnded); tick++)
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
        public void FactionIndexKeepsLegacyEnemyOrderAndQueryCacheIsUsed()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            var optimized = new Battle(scenario);
            var legacy = new Battle(scenario, null, true, true);
            optimized.RebuildSpatialCombatIndexForTest();
            legacy.RebuildSpatialCombatIndexForTest();
            var point = optimized.Capture(1).Observation.OwnArmies.First().Position;
            var optimizedEnemies = optimized.QueryVisibleEnemiesSnapshotForTest(1, point, Fix64.FromInt(24));
            var legacyEnemies = legacy.QueryVisibleEnemiesSnapshotForTest(1, point, Fix64.FromInt(24));
            Assert.That(optimizedEnemies.Select(e => e.ContactId), Is.EqualTo(legacyEnemies.Select(e => e.ContactId)));
            Assert.That(optimizedEnemies.Select(e => e.Kind), Is.EqualTo(legacyEnemies.Select(e => e.Kind)));
            optimized.QueryVisibleEnemiesSnapshotForTest(1, point, Fix64.FromInt(24));
            Assert.That(optimized.SpatialEnemyQueryHits, Is.GreaterThan(0));
            Assert.That(optimized.SpatialEnemyQueryMisses, Is.GreaterThan(0));
            optimized.RebuildSpatialCombatIndexForTest();
            optimized.QueryVisibleEnemiesSnapshotForTest(1, point, Fix64.FromInt(24));
            Assert.That(optimized.SpatialEnemyQueryMisses, Is.EqualTo(1));
        }

        [Test]
        public void ScaledCombatMatchesLegacyAndRebuildDropsOldQueryResults()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            ScenarioScale.MultiplyForMeasurement(scenario, 4, 8);
            var optimized = new Battle(scenario);
            var legacy = new Battle(scenario, null, true);
            for (long tick = 1; tick <= 12000 && (!optimized.Capture(1).Result.HasEnded || !legacy.Capture(1).Result.HasEnded); tick++)
            {
                optimized.Step(tick, Array.Empty<ScheduledInput>());
                legacy.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(optimized.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(legacy.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
            Assert.That(optimized.Capture(1).Tick, Is.EqualTo(legacy.Capture(1).Tick));
            Assert.That(optimized.Capture(1).Result.WinnerFactionId, Is.EqualTo(legacy.Capture(1).Result.WinnerFactionId));
        }
    }
}
