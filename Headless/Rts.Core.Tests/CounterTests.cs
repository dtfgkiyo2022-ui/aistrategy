using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>technical-design-v3 32 #13 (V3-5): the counter triangle among the line units.</summary>
    public sealed class CounterTests
    {
        [TestCase(UnitKind.Archer, UnitKind.Infantry)]
        [TestCase(UnitKind.Cavalry, UnitKind.Archer)]
        [TestCase(UnitKind.Infantry, UnitKind.Cavalry)]
        public void EachOfTheThreeBeatsTheNextOne(UnitKind attacker, UnitKind target)
        {
            Assert.That(CombatMath.Counters(attacker, target), Is.True);
            Assert.That(CombatMath.Counters(target, attacker), Is.False, "and the edge only goes one way");
            Assert.That(CombatMath.DamageAgainst(10, attacker, target, 500), Is.EqualTo(15));
            Assert.That(CombatMath.DamageAgainst(10, target, attacker, 500), Is.EqualTo(10));
        }

        [TestCase(UnitKind.Infantry, UnitKind.Infantry)]
        [TestCase(UnitKind.Archer, UnitKind.Archer)]
        [TestCase(UnitKind.Ram, UnitKind.Infantry)]
        [TestCase(UnitKind.Infantry, UnitKind.Ram)]
        [TestCase(UnitKind.Scout, UnitKind.Archer)]
        [TestCase(UnitKind.Archer, UnitKind.Scout)]
        public void OutsideTheTriangleNothingChanges(UnitKind attacker, UnitKind target)
        {
            Assert.That(CombatMath.Counters(attacker, target), Is.False);
            Assert.That(CombatMath.DamageAgainst(13, attacker, target, 500), Is.EqualTo(13));
        }

        /// <summary>A soldier with no class is the plain infantry of every older map, so those maps keep their numbers.</summary>
        [Test]
        public void ASoldierOfNoClassCountsAsInfantry()
        {
            Assert.That(CombatMath.Counters(UnitKind.Archer, 0), Is.True, "an archer still beats it");
            Assert.That(CombatMath.Counters(0, UnitKind.Cavalry), Is.True, "and it still beats cavalry");
            Assert.That(CombatMath.DamageAgainst(10, 0, 0, 500), Is.EqualTo(10), "two of them fight as before");
        }

        [Test]
        public void TheBonusIsRoundedDownAndCanBeTurnedOff()
        {
            Assert.That(CombatMath.DamageAgainst(7, UnitKind.Archer, UnitKind.Infantry, 500), Is.EqualTo(10), "7 * 1.5 = 10.5, down to 10");
            Assert.That(CombatMath.DamageAgainst(7, UnitKind.Archer, UnitKind.Infantry, 0), Is.EqualTo(7));
            Assert.That(CombatMath.DamageAgainst(0, UnitKind.Archer, UnitKind.Infantry, 500), Is.EqualTo(0));
        }

        /// <summary>The triangle is off on a map without ages: no soldier there has a class, so nothing about it changes.</summary>
        [Test]
        public void AMapWithoutAgesFightsExactlyAsBefore()
        {
            var s = MapGenerator.Generate(1, true, true);
            var sim = new Battle(s);
            for (long t = 1; t <= 600; t++) sim.Step(t, Array.Empty<ScheduledInput>());
            var f = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            var classes = f.Where(p => p.Key.EndsWith("].Class", StringComparison.Ordinal)).Select(p => p.Value).Distinct().ToArray();
            Assert.That(classes.All(v => v == "0"), Is.True, "no soldier on such a map has a class");
        }
    }
}
