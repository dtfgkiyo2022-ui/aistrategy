using System;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class SkillAutoAllTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);

        [Test]
        public void SkillAutoAllIsAnOptInExtension()
        {
            var enabled = LiveGameRules.Create(1, true, true, false, false);
            Assert.That(enabled.Economy.SkillAutoAll, Is.True);
            var enabledDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(enabled));
            Assert.That(enabledDecoded.Economy.SkillAutoAll, Is.True);
            Assert.That(Array.Exists(enabledDecoded.Extensions, x => x.Id == 137), Is.True);

            var disabled = LiveGameRules.Create(1, true, true, false, false, skillAutoAllOff: true);
            Assert.That(disabled.Economy.Skills, Is.True);
            Assert.That(disabled.Economy.SkillAutoAll, Is.False);
            var disabledDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(disabled));
            Assert.That(disabledDecoded.Economy.SkillAutoAll, Is.False);
            Assert.That(Array.Exists(disabledDecoded.Extensions, x => x.Id == 137), Is.False);
        }

        [Test]
        public void NeutralUnitAimIsAcceptedOnlyWhenSkillAutoAllIsOn()
        {
            var on = LiveGameRules.Create(2, true, true, false, false);
            var onSimulation = ConfigureNeutralTarget(on);
            Assert.That(onSimulation.TryBeginCast(0, 0, SkillAim.AtNeutral(1)), Is.EqualTo(SkillCastFailure.None));

            var off = LiveGameRules.Create(2, true, true, false, false, skillAutoAllOff: true);
            var offSimulation = ConfigureNeutralTarget(off);
            Assert.That(offSimulation.TryBeginCast(0, 0, SkillAim.AtNeutral(1)), Is.EqualTo(SkillCastFailure.InvalidTarget));
        }

        private static Battle ConfigureNeutralTarget(ScenarioDefinition scenario)
        {
            scenario.Economy.Skills = true;
            scenario.Skills = new[]
            {
                new SkillDefinition
                {
                    Id = 1, Resource = SkillResource.None, CooldownTicks = 1, Range = Fix64.FromInt(8),
                    Targeting = SkillTargeting.Unit, Shape = SkillShape.OneTarget, Affects = SkillAffects.Enemies,
                    Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Damage, Base = 1 } }
                }
            };
            scenario.UnitSkillKits = new[]
            {
                new UnitSkillKit { UnitKind = scenario.Soldiers[0].Kind, Resource = SkillResource.None, SkillIds = new[] { 1, 0, 0, 0 } }
            };
            var simulation = new Battle(scenario);
            var world = World(simulation);
            var neutrals = (Array)world.GetType().GetField("Neutrals", PrivateInstance | BindingFlags.Public).GetValue(world);
            object neutral = neutrals.GetValue(0);
            var neutralType = neutral.GetType();
            neutralType.GetField("Alive", PrivateInstance | BindingFlags.Public).SetValue(neutral, true);
            neutralType.GetField("OwnerFactionId", PrivateInstance | BindingFlags.Public).SetValue(neutral, (uint)0);
            neutralType.GetField("Position", PrivateInstance | BindingFlags.Public).SetValue(neutral, scenario.Soldiers[0].Position);
            neutrals.SetValue(neutral, 0);
            return simulation;
        }
    }
}
