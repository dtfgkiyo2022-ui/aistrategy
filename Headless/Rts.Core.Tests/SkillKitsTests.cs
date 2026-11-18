using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    public sealed class SkillKitsTests
    {
        [Test]
        public void LiveRulesProvideFiveKitsAndFourSlots()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);

            Assert.That(scenario.Skills, Has.Length.EqualTo(49));
            // Twelve entries: the original six plus the six C5.2.1 unique-unit kits.
            Assert.That(scenario.UnitSkillKits, Has.Length.EqualTo(12));
            Assert.That(scenario.UnitSkillKits.Select(x => x.SkillIds.Length), Is.All.EqualTo(4));
            Assert.That(scenario.UnitSkillKits.Select(x => x.PassiveKind), Is.EquivalentTo(new[]
                { PassiveKind.Formation, PassiveKind.Aim, PassiveKind.Momentum, PassiveKind.Momentum, PassiveKind.Fleet, PassiveKind.Prayer,
                    PassiveKind.HardSkin, PassiveKind.None, PassiveKind.Fleet, PassiveKind.BloodOath, PassiveKind.Fleet, PassiveKind.HardSkin }));
            var cavalry = scenario.UnitSkillKits.Single(x => x.UnitKind == UnitKind.Cavalry);
            var light = scenario.UnitSkillKits.Single(x => x.UnitKind == UnitKind.LightCavalry);
            Assert.That(light.SkillIds, Is.EqualTo(cavalry.SkillIds));
            Assert.That(scenario.Skills.SelectMany(x => x.Effects).Any(x => x.Kind == SkillEffectKind.CcImmunity), Is.True);
            Assert.That(scenario.Skills.SelectMany(x => x.Effects).Any(x => x.Kind == SkillEffectKind.Camouflage), Is.True);
        }

        [Test]
        public void Combat3SkillFieldsRoundTrip()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));

            Assert.That(decoded.Economy.SkillDamagePermille, Is.EqualTo(1000));
            Assert.That(decoded.Skills.Single(x => x.Id == 19).Effects.Single(x => x.Kind == SkillEffectKind.Heal).AppliesTo,
                Is.EqualTo(SkillEffectAppliesTo.Allies));
            Assert.That(decoded.Skills.SelectMany(x => x.Effects).Any(x => x.Stat == SkillStat.DamageTaken), Is.True);
            Assert.That(decoded.UnitSkillKits.Single(x => x.UnitKind == UnitKind.Monk).PassiveKind, Is.EqualTo(PassiveKind.Prayer));
        }

        [Test]
        public void CastSkillCommandCarriesSelectionAndAim()
        {
            var aim = SkillCommandAim.AtPoint(new SimPoint(Fix64.FromInt(3), Fix64.FromInt(4)));
            var command = EconomyCommand.CastSkill(1, 7, new[] { 11u, 4u }, 0, 2, aim);

            Assert.That(command.Kind, Is.EqualTo(EconomyCommandKind.CastSkill));
            Assert.That(command.SoldierIds, Is.EqualTo(new[] { 11u, 4u }));
            Assert.That(command.SkillSlot, Is.EqualTo(2));
            Assert.That(command.SkillAim.Kind, Is.EqualTo(SkillCommandAimKind.Point));
            Assert.That(command.SkillAim.Point, Is.EqualTo(aim.Point));
        }

        [Test]
        public void SkillFrameFieldsAreAppendOnlyAndVisible()
        {
            var unit = new RenderUnit(1, true, UnitKind.Monk, default, false, false, false, true, 10,
                skillResourceAmount: 20, skillResourceMax: 300, skillCcMask: 4, skillCastingSlot: 3);
            var area = new SkillAreaView(2, 8, default, 1, 4, 0, 0, default, 10, 30);
            Assert.That(unit.SkillResourceAmount, Is.EqualTo(20));
            Assert.That(unit.SkillResourceMax, Is.EqualTo(300));
            Assert.That(unit.SkillCcMask, Is.EqualTo((ushort)4));
            Assert.That(unit.SkillCastingSlot, Is.EqualTo(3));
            Assert.That(area.EndTick, Is.EqualTo(30));
        }
    }
}
