using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// The played game (LiveMatchHost) turns every implemented rule on (10-09). Two automatic sides play it here: the
    /// rules pass validation together, the match runs without an error, and two runs stay identical.
    /// </summary>
    public sealed class LiveGameRulesTests
    {
        [Test]
        public void PlayedRulesTurnOnTheNewRules()
        {
            var e = LiveGameRules.Create(3, false, false, false, false).Economy;
            Assert.That(e.ProcessingChain && e.BeltComponents && e.Regions && e.Towns, Is.True);
            Assert.That(e.LineRebuildDelayTicks, Is.EqualTo(200));
            Assert.That(e.Masonry, Is.False, "the thirteen civilisations come only with allCivilisations");
            var all = LiveGameRules.Create(3, true, true, false, false).Economy;
            Assert.That(all.BeltComponents && all.Masonry && all.Sanctuary && all.Towns, Is.True);
        }

        [TestCase(3UL, false)]
        [TestCase(11UL, false)]
        [TestCase(5UL, true)]
        public void AutomaticSidesPlayThePlayedRulesTheSameWayTwice(ulong seed, bool largeMap)
        {
            var left = new Battle(LiveGameRules.Create(seed, largeMap, true, false, false));
            var right = new Battle(LiveGameRules.Create(seed, largeMap, true, false, false));
            long tick = 1;
            for (; tick <= 12000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                if (tick % 200 == 0)
                    Assert.That(Hash(left), Is.EqualTo(Hash(right)), "tick " + tick);
            }
            Assert.That(Hash(left), Is.EqualTo(Hash(right)), "last tick");
            var buildings = left.Capture(1).Economy.Buildings.Concat(left.Capture(2).Economy.Buildings).ToArray();
            TestContext.WriteLine($"seed {seed} large {largeMap}: ticks {tick - 1}, ages {left.Capture(1).Economy.Age}/"
                + $"{left.Capture(2).Economy.Age}, steelworks {buildings.Count(b => b.Kind == BuildingKind.Steelworks)}, "
                + $"towns {buildings.Count(b => b.Kind == BuildingKind.Town)}, buildings {buildings.Length}");
        }

        private static string Hash(Battle sim) => Convert.ToHexString(ReplayBinary.Hash(sim.CaptureDiagnostic().CanonicalState));
    }
}
