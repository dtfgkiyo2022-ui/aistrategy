using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// technical-design-v3 32 #15 (V3-5): villagers repair a finished building that has been hurt. The damage comes from
    /// enemy soldiers set down beside it, the way the raid tests do it, so every tick here is one the simulation produced.
    /// The runs stay short and watch every tick: a repair is undone by the next blow, so sampling now and then misses it.
    /// </summary>
    public sealed class RepairTests
    {
        /// <summary>Enemy soldiers of the raiding army are set down where <paramref name="place"/> says, as in EconomyTests.</summary>
        private static ScenarioDefinition WithRaiders(ScenarioDefinition scenario, Func<int, SimPoint> place)
        {
            int k = 0;
            for (int i = 0; i < scenario.Soldiers.Length; i++)
                if (scenario.Soldiers[i].FactionId == 2 && scenario.Soldiers[i].ArmyId == 5) scenario.Soldiers[i].Position = place(k++);
            return scenario;
        }

        /// <summary>Where the west puts its barracks on <paramref name="scenario"/>, found on a run of its own.</summary>
        private static int BarracksOrigin(ScenarioDefinition scenario, int ticks)
        {
            var probe = new Battle(scenario);
            for (long t = 1; t <= ticks; t++) probe.Step(t, Array.Empty<ScheduledInput>());
            var b = probe.Capture(1).Economy.Buildings.FirstOrDefault(v => v.FactionId == 1 && v.Kind == BuildingKind.Barracks);
            return b.Id == 0 ? -1 : (int)(b.Center.Z.Raw / 65536 / 2) * 128 + (int)(b.Center.X.Raw / 65536 / 2);
        }

        /// <summary>Runs the raid and reports the lowest the barracks fell to and how often its HP climbed again.</summary>
        private static (long lowest, int climbs, long full) Raid(ScenarioDefinition scenario, int originCell, int ticks)
        {
            int x0 = originCell % 128 * 2 - 2, z0 = originCell / 128 * 2 - 2;
            var s = WithRaiders(scenario, k => new SimPoint(Fix64.FromInt(x0 + 1 + k % 4), Fix64.FromInt(z0 + 7)));
            var sim = new Battle(s);
            long lowest = long.MaxValue, previous = -1;
            int climbs = 0;
            for (long t = 1; t <= ticks && !sim.Capture(1).Result.HasEnded; t++)
            {
                sim.Step(t, Array.Empty<ScheduledInput>());
                var b = sim.Capture(1).Economy.Buildings.FirstOrDefault(v => v.FactionId == 1 && v.Kind == BuildingKind.Barracks && v.Complete);
                if (b.Id == 0) continue;
                if (b.Hp < lowest) lowest = b.Hp;
                if (previous >= 0 && b.Hp > previous) climbs++;
                previous = b.Hp;
            }
            return (lowest, climbs, s.Economy.BarracksHp);
        }

        [Test]
        public void ARaidedBarracksIsRepairedWhileTheRaidGoesOn()
        {
            var scenario = MapGenerator.GenerateTerrain(2);
            // Any damage is worth repairing here: a raid of this size takes off a few percent, under the default bar.
            scenario.Economy.RepairAtPermille = 1000;
            int origin = BarracksOrigin(MapGenerator.GenerateTerrain(2), 400);
            Assume.That(origin, Is.GreaterThanOrEqualTo(0), "the west placed a barracks");
            var (lowest, climbs, full) = Raid(scenario, origin, 1200);
            TestContext.WriteLine("seed 2: barracks fell to " + lowest + " of " + full + ", HP climbed " + climbs + " times");
            Assert.That(lowest, Is.LessThan(full), "the raiders struck it");
            Assert.That(climbs, Is.GreaterThan(0), "and villagers put HP back into it");
            Assert.That(lowest + climbs, Is.LessThanOrEqualTo(full + 1200), "sanity: nothing ran away with the numbers");
        }

        [Test]
        public void AMapWithoutAgesNeverRepairs()
        {
            var scenario = MapGenerator.Generate(1, true);
            int origin = BarracksOrigin(MapGenerator.Generate(1, true), 400);
            Assume.That(origin, Is.GreaterThanOrEqualTo(0));
            var (lowest, climbs, full) = Raid(scenario, origin, 1200);
            TestContext.WriteLine("ageless map: barracks fell to " + lowest + " of " + full + ", HP climbed " + climbs + " times");
            Assume.That(lowest, Is.LessThan(full), "the raiders struck it there too");
            Assert.That(climbs, Is.EqualTo(0), "no building on an ageless map ever gains HP back");
        }
    }
}
