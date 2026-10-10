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
            Assert.That(all.BeltComponents && all.Masonry && all.Sanctuary && all.Towns && all.EarlyArms && all.AgeClock, Is.True);
        }

        [TestCase(3UL, false)]
        [TestCase(11UL, false)]
        [TestCase(5UL, false)]
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
            // Ten minutes is enough for both sides to choose a civilisation. Food ran out before that on the large map (no
            // remote food once gold took its cluster) and drop-offs waited for the whole age saving (10-10).
            Assert.That(Math.Min(left.Capture(1).Economy.Age, left.Capture(2).Economy.Age), Is.GreaterThanOrEqualTo(1), "an age after ten minutes");
            TestContext.WriteLine($"seed {seed} large {largeMap}: ticks {tick - 1}, ages {left.Capture(1).Economy.Age}/"
                + $"{left.Capture(2).Economy.Age}, steelworks {buildings.Count(b => b.Kind == BuildingKind.Steelworks)}, "
                + $"towns {buildings.Count(b => b.Kind == BuildingKind.Town)}, buildings {buildings.Length}");
        }

        /// <summary>
        /// Early arms (10-10): from its first civilisation age a side fields its civilisation's second unit (archers or
        /// light cavalry), so a ten-minute army is no longer infantry alone. Seed 4 puts both sides in the cavalry
        /// civilisation by 5.5 minutes (measured).
        /// </summary>
        [Test]
        public void AutomaticSidesFieldMoreThanInfantryByTenMinutes()
        {
            var sim = new Battle(LiveGameRules.Create(4, false, true, false, false));
            for (long tick = 1; tick <= 12000 && !sim.Capture(1).Result.HasEnded; tick++)
                sim.Step(tick, Array.Empty<ScheduledInput>());
            int others = 0;
            for (uint faction = 1; faction <= 2; faction++)
                others += sim.Capture(faction).Units.Count(u => u.IsOwn
                    && (u.Kind == UnitKind.Archer || u.Kind == UnitKind.LightCavalry || u.Kind == UnitKind.Cavalry));
            Assert.That(others, Is.GreaterThan(0), "archers or riders after ten minutes");
            // A look at a soldier shows its side's civilisation (for its model), own and enemy alike.
            var frame = sim.Capture(1);
            Assert.That(frame.Units.Where(u => u.IsOwn).All(u => u.Civ == frame.Economy.Civ && u.Civ != CivKind.Primitive), Is.True, "own soldiers carry their civilisation");
            var enemyCiv = sim.Capture(2).Economy.Civ;
            Assert.That(frame.Units.Where(u => !u.IsOwn).All(u => u.Civ == enemyCiv), Is.True, "visible enemies carry theirs");
        }

        private static string Hash(Battle sim) => Convert.ToHexString(ReplayBinary.Hash(sim.CaptureDiagnostic().CanonicalState));
    }
}
