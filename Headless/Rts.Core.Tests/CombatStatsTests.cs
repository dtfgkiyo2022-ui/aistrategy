using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CombatStatsTests
    {
        [Test]
        public void DefenseFormulaUsesIntegerCeilingAndPercentageBeforeFlatPenetration()
        {
            Assert.That(CombatMath.DamageAfterDefense(10, 0, 0, 0), Is.EqualTo(10));
            Assert.That(CombatMath.DamageAfterDefense(10, 100, 0, 0), Is.EqualTo(5));
            Assert.That(CombatMath.EffectiveDefense(100, 500, 20), Is.EqualTo(30));
            Assert.That(CombatMath.DamageAfterDefense(1, 10000, 0, 0), Is.EqualTo(1));
        }

        [Test]
        public void CounterMultiplierIsAppliedBeforeDefense()
        {
            Assert.That(CombatMath.PhysicalDamageAgainst(10, UnitKind.Archer, UnitKind.Infantry, 500, 100, 0, 0), Is.EqualTo(8));
        }

        [Test]
        public void CombatStatsExtensionRoundTripsParametersAndTheFlag()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            Assert.That(source.Economy.CombatStats, Is.True);
            var infantry = source.UnitParameters.Single(p => p.Kind == UnitKind.Infantry);
            infantry.LifestealPermille = 125;
            source.UnitParameters[Array.FindIndex(source.UnitParameters, p => p.Kind == UnitKind.Infantry)] = infantry;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var decodedInfantry = decoded.UnitParameters.Single(p => p.Kind == UnitKind.Infantry);
            Assert.That(decoded.Economy.CombatStats, Is.True);
            Assert.That(decodedInfantry.Armor, Is.EqualTo(20));
            Assert.That(decodedInfantry.LifestealPermille, Is.EqualTo(125));
        }

        [Test]
        public void CombatStatsOffKeepsTheLegacyScenarioBytes()
        {
            var source = WeekOneScenario.Create();
            byte[] before = ScenarioBinary.Encode(source);
            source.Economy.CombatStats = false;
            byte[] after = ScenarioBinary.Encode(source);
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        public void LargeLiveRulesDefaultOnAndCliOffCanUseTheSameScenarioSwitch()
        {
            var on = LiveGameRules.Create(1, true, true, false, false);
            var off = LiveGameRules.Create(1, true, true, false, false, combatStatsOff: true);
            Assert.That(on.Economy.CombatStats, Is.True);
            Assert.That(off.Economy.CombatStats, Is.False);
        }
    }
}
