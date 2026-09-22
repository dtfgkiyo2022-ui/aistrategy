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
    /// technical-design-v3 32.11 (V3-5): what the automatic economy reaches when both sides are left alone. It replaces the
    /// tests that pinned one seed each (towers shooting on seed 6, research on seed 7, the city age on seed 7): every
    /// change to the AI moves which ground shows what, so the whole set of terrain seeds is looked at together.
    /// </summary>
    public sealed class AutoPlayTests
    {
        [Test]
        public void AcrossTheTerrainSeedsTheAutomaticEconomyReachesTheContent()
        {
            int decisive = 0, maxAge = 0, seedsWithTechs = 0, markets = 0, workshops = 0, towers = 0, archers = 0, cavalry = 0, rams = 0;
            long shots = 0;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var s = MapGenerator.GenerateTerrain(seed);
                var sim = new Battle(s);
                for (long t = 1; t <= 40000 && !sim.Capture(1).Result.HasEnded; t++) sim.Step(t, Array.Empty<ScheduledInput>());
                var f = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
                long Number(string key) => long.Parse(f[key], CultureInfo.InvariantCulture);
                if (sim.Capture(1).Result.HasEnded) decisive++;
                for (int b = 1; b <= Number("Buildings.Count"); b++)
                    switch (f["Buildings[" + b + "].Kind"])
                    {
                        case "8": towers++; shots += Number("Buildings[" + b + "].Shots"); break;
                        case "10": markets++; break;
                        case "11": workshops++; break;
                    }
                for (int i = 1; i < Number("NextSoldierId"); i++)
                    switch (f["Soldiers[" + i + "].Class"])
                    {
                        case "5": archers++; break;
                        case "6": cavalry++; break;
                        case "7": rams++; break;
                    }
                var west = sim.Capture(1).Economy;
                var east = sim.Capture(2).Economy;
                maxAge = Math.Max(maxAge, Math.Max(west.Age, east.Age));
                if (west.Techs + east.Techs > 0) seedsWithTechs++;
            }
            TestContext.WriteLine($"8 seeds to 40000: decisive {decisive}, highest age {maxAge}, seeds with research {seedsWithTechs}, "
                + $"towers {towers} (shots {shots}), markets {markets}, workshops {workshops}, archers {archers}, cavalry {cavalry}, rams {rams}");
            // Measured (32.11): decisive 6, highest age 3, research on 5 seeds, 21 towers with 374 shots, 3 markets,
            // 1 workshop, 2 archers. The bars sit under those, so ordinary drift does not fail the test.
            Assert.That(decisive, Is.GreaterThanOrEqualTo(4), "most matches are decided");
            Assert.That(maxAge, Is.GreaterThanOrEqualTo(2), "a side reaches the second age at least");
            Assert.That(seedsWithTechs, Is.GreaterThanOrEqualTo(3), "research happens on several grounds");
            Assert.That(towers, Is.GreaterThan(0), "towers are built");
            Assert.That(shots, Is.GreaterThan(0), "and they shoot");
            Assert.That(markets, Is.GreaterThan(0), "a market is built somewhere");
        }
    }
}
