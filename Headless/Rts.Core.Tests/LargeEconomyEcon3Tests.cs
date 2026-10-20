using System;
using System.IO;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class LargeEconomyEcon3Tests
    {
        [Test]
        public void OrdinaryMapKeepsLargeEconomyDisabledAndCostsUnchanged()
        {
            var ordinary = LiveGameRules.Create(1, false, true, false, false);
            var large = LiveGameRules.Create(1, true, true, false, false);
            Assert.That(ordinary.Economy.LargeEconomy, Is.False);
            Assert.That((large.Economy.VillagerFoodCost, large.Economy.InfantryFoodCost, large.Economy.InfantryWoodCost,
                large.Economy.BarracksWoodCost, large.Economy.TownWoodCost, large.Economy.TownStoneCost, large.Economy.TownWork,
                large.Economy.HouseWoodCost, large.Economy.HouseWork),
                Is.EqualTo((ordinary.Economy.VillagerFoodCost, ordinary.Economy.InfantryFoodCost, ordinary.Economy.InfantryWoodCost,
                    ordinary.Economy.BarracksWoodCost, ordinary.Economy.TownWoodCost, ordinary.Economy.TownStoneCost, ordinary.Economy.TownWork,
                    ordinary.Economy.HouseWoodCost, ordinary.Economy.HouseWork)));
        }

        [Test]
        public void OrdinaryMapHashIsStableWhenLargeEconomyIsOff()
        {
            var left = new Battle(LiveGameRules.Create(5, false, true, false, false));
            var right = new Battle(LiveGameRules.Create(5, false, true, false, false));
            for (long tick = 1; tick <= 3000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                if (tick % 300 == 0)
                    Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                        Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void LargeEconomyFlagIsDeterministicAcrossTicks()
        {
            var left = new Battle(LiveGameRules.Create(9, true, false, false, false));
            var right = new Battle(LiveGameRules.Create(9, true, false, false, false));
            for (long tick = 1; tick <= 3000; tick++)
            {
                left.Step(tick, Array.Empty<Rts.Contracts.ScheduledInput>());
                right.Step(tick, Array.Empty<Rts.Contracts.ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void LargeEconomyRecordAndReplayHashesMatch()
        {
            var scenario = LiveGameRules.Create(1, true, false, false, false);
            var identity = new BuildIdentity();
            using (var stream = new MemoryStream())
            {
                var recorded = ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 1200, identity);
                Assert.That(recorded.IsFault, Is.False);
                stream.Position = 0;
                var replayed = ReplayRunner.Replay(stream, identity);
                Assert.That(replayed.FirstMismatchTick, Is.Null);
                Assert.That(replayed.IsFault, Is.False);
            }
        }
    }
}
