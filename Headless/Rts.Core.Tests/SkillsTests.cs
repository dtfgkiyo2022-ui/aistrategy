using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class SkillsTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static object World(Battle simulation) => typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
        private static void SetWorldField(Battle simulation, string name, object value) => WorldField(name).SetValue(World(simulation), value);
        private static FieldInfo WorldField(string name) => typeof(Rts.Simulation.WorldState).GetField(name, PrivateInstance | BindingFlags.Public);

        private static bool Contains(Battle simulation, SkillDefinition skill, SimPoint center, SkillAim aim, SimPoint point)
            => (bool)typeof(Battle).GetMethod("ShapeContains", PrivateInstance).Invoke(simulation, new object[] { skill, center, aim, point });

        private static ScenarioDefinition SkillScenario(SkillDefinition skill, UnitSkillKit kit, bool enabled = true)
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = enabled;
            source.Skills = enabled ? new[] { skill } : Array.Empty<SkillDefinition>();
            source.UnitSkillKits = enabled ? new[] { kit } : Array.Empty<UnitSkillKit>();
            return source;
        }

        private static SkillDefinition SelfSkill(int cost = 0, SkillResource resource = SkillResource.None)
            => new SkillDefinition { Id = 1, Resource = resource, Cost = cost, CooldownTicks = 10,
                Range = Fix64.FromInt(8), Targeting = SkillTargeting.Self, Shape = SkillShape.Single,
                Affects = SkillAffects.Self, Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Heal, Base = 1 } } };

        private static UnitSkillKit Kit(UnitKind kind, SkillResource resource = SkillResource.None, int max = 0, int skillId = 1)
            => new UnitSkillKit { UnitKind = kind, Resource = resource, ResourceMax = max, SkillIds = new[] { skillId, 0, 0, 0 } };

        [Test]
        public void SkillsExtensionRoundTripsDefinitionsAndKits()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            source.Skills = new[] { new SkillDefinition { Id = 1, Resource = SkillResource.Mana, Cost = 2, CooldownTicks = 7,
                Range = Fix64.FromInt(8), Targeting = SkillTargeting.Self, Shape = SkillShape.Circle, Radius = 3,
                Affects = SkillAffects.Self, Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Shield, Base = 10, DurationTicks = 5 } } } };
            source.UnitSkillKits = new[] { new UnitSkillKit { UnitKind = UnitKind.Infantry, Resource = SkillResource.Mana,
                ResourceMax = 20, RegenPerSecond = 2, SkillIds = new[] { 1, 0, 0, 0 } } };
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Economy.Skills, Is.True);
            Assert.That(decoded.Skills.Single().CooldownTicks, Is.EqualTo(7));
            Assert.That(decoded.Skills.Single().Effects.Single().Kind, Is.EqualTo(SkillEffectKind.Shield));
            Assert.That(decoded.UnitSkillKits.Single().SkillIds, Is.EqualTo(new[] { 1, 0, 0, 0 }));
        }

        [Test]
        public void SkillsOffDoesNotWriteTheExtension()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = false;
            source.Skills = Array.Empty<SkillDefinition>();
            source.UnitSkillKits = Array.Empty<UnitSkillKit>();
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(source)).Extensions.Any(x => x.Id == 119), Is.False);
        }

        [Test]
        public void LiveRulesEnableSkillsOnlyForLargeMapsUnlessExplicitlyDisabled()
        {
            var small = LiveGameRules.Create(1, false, true, false, false);
            Assert.That(small.Economy.Skills, Is.False);
            Assert.That(small.Skills, Is.Empty);
            Assert.That(small.UnitSkillKits, Is.Empty);

            var large = LiveGameRules.Create(1, true, true, false, false);
            Assert.That(large.Economy.Skills, Is.True);
            Assert.That(large.Skills, Is.Not.Empty);
            Assert.That(large.UnitSkillKits, Is.Not.Empty);

            var off = LiveGameRules.Create(1, true, true, false, false, skillsOff: true);
            Assert.That(off.Economy.Skills, Is.False);
            Assert.That(off.Skills, Is.Empty);
            Assert.That(off.UnitSkillKits, Is.Empty);
        }

        [Test]
        public void DirectionAimUsesLineLengthInsteadOfReferencePointRange()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            var skill = new SkillDefinition { Id = 1, CooldownTicks = 1, Range = Fix64.FromInt(1),
                Targeting = SkillTargeting.Direction, Shape = SkillShape.Line, Length = 10, Width = 2,
                Affects = SkillAffects.Enemies, Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Damage, Base = 1 } } };
            source.Skills = new[] { skill };
            source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            Assert.That(new Battle(source).TryBeginCast(0, 0, SkillAim.Along(new SimPoint(Fix64.FromInt(1), Fix64.FromInt(0)))),
                Is.EqualTo(SkillCastFailure.None));
        }

        [Test]
        public void RainHasOnlyItsDamageEffect()
        {
            var rules = LiveGameRules.Create(1, true, true, false, false);
            var rain = rules.Skills.Single(x => x.Id == 8);
            Assert.That(rain.Effects.Select(x => x.Kind), Is.EqualTo(new[] { SkillEffectKind.Damage }));
        }

        [Test]
        public void TryBeginCastUsesTheKitAndCooldown()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            source.Skills = new[] { new SkillDefinition { Id = 1, Resource = SkillResource.None, CooldownTicks = 10, Range = Fix64.FromInt(4),
                Targeting = SkillTargeting.Self, Shape = SkillShape.Single, Affects = SkillAffects.Self,
                Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Heal, Base = 1 } } } };
            var kind = source.Soldiers[0].Kind;
            source.UnitSkillKits = new[] { new UnitSkillKit { UnitKind = kind, SkillIds = new[] { 1, 0, 0, 0 } } };
            var simulation = new Battle(source);
            Assert.That(simulation.TryBeginCast(0, 0, SkillAim.AtPoint(source.Soldiers[0].Position)), Is.EqualTo(SkillCastFailure.None));
            Assert.That(simulation.TryBeginCast(0, 0, SkillAim.AtPoint(source.Soldiers[0].Position)), Is.EqualTo(SkillCastFailure.Busy));
        }

        [Test]
        public void SkillsOffRejectsCastWithoutAllocatingSkillRuntime()
        {
            var source = SkillScenario(SelfSkill(), Kit(LiveGameRules.Create(1, true, true, false, false).Soldiers[0].Kind), false);
            var simulation = new Battle(source);
            Assert.That(simulation.TryBeginCast(0, 0, default), Is.EqualTo(SkillCastFailure.InvalidSoldier));
        }

        [Test]
        public void InvalidSoldierIsRejected()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            source.Skills = new[] { SelfSkill() };
            source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            Assert.That(new Battle(source).TryBeginCast(-1, 0, default), Is.EqualTo(SkillCastFailure.InvalidSoldier));
        }

        [Test]
        public void MissingKitIsRejected()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            source.Skills = new[] { SelfSkill() };
            source.UnitSkillKits = Array.Empty<UnitSkillKit>();
            Assert.That(new Battle(source).TryBeginCast(0, 0, default), Is.EqualTo(SkillCastFailure.NoSkill));
        }

        [Test]
        public void EmptyKitSlotIsRejected()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            source.Skills = new[] { SelfSkill() };
            source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind, skillId: 0) };
            Assert.That(new Battle(source).TryBeginCast(0, 0, default), Is.EqualTo(SkillCastFailure.NoSkill));
        }

        [Test]
        public void ResourceShortageIsRejected()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            source.Skills = new[] { SelfSkill(5, SkillResource.Mana) };
            source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind, SkillResource.Mana, 0) };
            Assert.That(new Battle(source).TryBeginCast(0, 0, default), Is.EqualTo(SkillCastFailure.Resource));
        }

        [Test]
        public void PointTargetOutsideRangeIsRejected()
        {
            var skill = SelfSkill();
            skill.Targeting = SkillTargeting.Point;
            skill.Range = Fix64.FromInt(1);
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            source.Skills = new[] { skill };
            source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            var far = new SimPoint(Fix64.FromInt(1000), Fix64.FromInt(1000));
            Assert.That(new Battle(source).TryBeginCast(0, 0, SkillAim.AtPoint(far)), Is.EqualTo(SkillCastFailure.InvalidTarget));
        }

        [Test]
        public void AimFactoriesPreserveTheirCoordinates()
        {
            var point = new SimPoint(Fix64.FromInt(3), Fix64.FromInt(4));
            var target = SkillAim.AtSoldier(7);
            var atPoint = SkillAim.AtPoint(point);
            var along = SkillAim.Along(point);
            Assert.That(target.TargetSoldierIndex, Is.EqualTo(7));
            Assert.That(atPoint.Point, Is.EqualTo(point));
            Assert.That(along.Direction, Is.EqualTo(point));
        }

        [Test]
        public void AllEffectKindsRoundTrip()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            source.Skills = new[] { new SkillDefinition { Id = 1, CooldownTicks = 1, Effects = new[]
            {
                new SkillEffect { Kind = SkillEffectKind.Damage }, new SkillEffect { Kind = SkillEffectKind.Cc },
                new SkillEffect { Kind = SkillEffectKind.Shield }, new SkillEffect { Kind = SkillEffectKind.Heal },
                new SkillEffect { Kind = SkillEffectKind.DamageReduction }, new SkillEffect { Kind = SkillEffectKind.Invulnerable }
            } } };
            source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            var effects = ScenarioBinary.Decode(ScenarioBinary.Encode(source)).Skills.Single().Effects;
            Assert.That(effects.Select(x => x.Kind), Is.EqualTo(new[] { SkillEffectKind.Damage, SkillEffectKind.Cc,
                SkillEffectKind.Shield, SkillEffectKind.Heal, SkillEffectKind.DamageReduction, SkillEffectKind.Invulnerable }));
        }

        [Test]
        public void SkillEconomyDefaultsAreStable()
        {
            var rules = new EconomyRules();
            Assert.That(rules.SkillCcImmuneTicks, Is.EqualTo(20));
            Assert.That(rules.SkillRageDecayDelayTicks, Is.EqualTo(60));
            Assert.That(rules.SkillRageDecayPerSecond, Is.EqualTo(5));
            Assert.That(rules.SkillRageOnHitDealt, Is.EqualTo(4));
            Assert.That(rules.SkillRageOnHitTaken, Is.EqualTo(2));
        }

        [Test]
        public void LineUsesPerpendicularDistanceAndStopsAtTheEnd()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            var skill = new SkillDefinition { Id = 1, CooldownTicks = 1, Shape = SkillShape.Line, Length = 10, Width = 2 };
            source.Skills = new[] { skill }; source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            var simulation = new Battle(source);
            var center = new SimPoint(Fix64.FromInt(0), Fix64.FromInt(0));
            var direction = SkillAim.Along(new SimPoint(Fix64.FromInt(1), Fix64.FromInt(0)));
            Assert.That(Contains(simulation, skill, center, direction, new SimPoint(Fix64.FromInt(5), Fix64.FromRaw(65535))), Is.True);
            Assert.That(Contains(simulation, skill, center, direction, new SimPoint(Fix64.FromInt(5), Fix64.FromInt(2))), Is.False);
            Assert.That(Contains(simulation, skill, center, direction, new SimPoint(Fix64.FromInt(11), Fix64.FromInt(0))), Is.False);
        }

        [Test]
        public void FortyDegreeConeUsesTheIntegerBoundaryTable()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            var skill = new SkillDefinition { Id = 1, CooldownTicks = 1, Shape = SkillShape.Cone, Radius = 10, AngleDegrees = 40 };
            source.Skills = new[] { skill }; source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            var simulation = new Battle(source);
            var center = new SimPoint(Fix64.FromInt(0), Fix64.FromInt(0));
            var direction = SkillAim.Along(new SimPoint(Fix64.FromInt(1), Fix64.FromInt(0)));
            Assert.That(Contains(simulation, skill, center, direction, new SimPoint(Fix64.FromRaw(1000), Fix64.FromRaw(360))), Is.True);
            Assert.That(Contains(simulation, skill, center, direction, new SimPoint(Fix64.FromRaw(1000), Fix64.FromRaw(370))), Is.False);
        }

        [Test]
        public void RageDecayIsIncrementalAndStableWhenReadTwiceAtOneTick()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            source.Skills = new[] { SelfSkill(resource: SkillResource.Rage) };
            source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind, SkillResource.Rage, 100) };
            var simulation = new Battle(source);
            var world = World(simulation);
            WorldField("Tick").SetValue(world, 100L);
            ((int[])WorldField("SkillRageAmount").GetValue(world))[0] = 50;
            ((int[])WorldField("SkillLastCombatTick").GetValue(world))[0] = 0;
            ((int[])WorldField("SkillRageStampTick").GetValue(world))[0] = 0;
            int first = simulation.SkillRageForTest(0);
            int second = simulation.SkillRageForTest(0);
            Assert.That(first, Is.EqualTo(40));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void InvulnerableTargetRejectsCrowdControl()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            var skill = SelfSkill(); source.Skills = new[] { skill }; source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            var simulation = new Battle(source); var world = World(simulation);
            ((int[])WorldField("SkillInvulnerableUntil").GetValue(world))[1] = 10;
            var apply = typeof(Battle).GetMethod("ApplyEffectTo", PrivateInstance);
            apply.Invoke(simulation, new object[] { 0, skill, new SkillEffect { Kind = SkillEffectKind.Cc, CcKind = SkillCcKind.Stun, DurationTicks = 5 }, 1 });
            Assert.That(((long[])WorldField("SkillCcUntil").GetValue(world))[1 * 9], Is.EqualTo(0));
        }

        [Test]
        public void CastListIsEmptyAfterActivationAndInterruption()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            var skill = SelfSkill(); skill.CastTicks = 0; source.Skills = new[] { skill }; source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            var simulation = new Battle(source); simulation.TryBeginCast(0, 0, SkillAim.AtPoint(source.Soldiers[0].Position)); simulation.Step(1, Array.Empty<ScheduledInput>());
            Assert.That((int)WorldField("SkillCastingCount").GetValue(World(simulation)), Is.EqualTo(0));

            skill = SelfSkill(); skill.CastTicks = 5; source.Skills = new[] { skill }; source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            simulation = new Battle(source); simulation.TryBeginCast(0, 0, SkillAim.AtPoint(source.Soldiers[0].Position));
            ((long[])WorldField("SkillCcUntil").GetValue(World(simulation)))[0] = 10;
            simulation.Step(1, Array.Empty<ScheduledInput>());
            Assert.That((int)WorldField("SkillCastingCount").GetValue(World(simulation)), Is.EqualTo(0));
        }

        [Test]
        public void SkillStatsCountCastHitAndDamage()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.Skills = true;
            var skill = new SkillDefinition { Id = 1, CooldownTicks = 1, Range = Fix64.FromInt(8), Targeting = SkillTargeting.Self,
                Shape = SkillShape.Single, Affects = SkillAffects.Self, Effects = new[]
                {
                    new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.True, Base = 2 },
                    new SkillEffect { Kind = SkillEffectKind.Cc, CcKind = SkillCcKind.Stun, DurationTicks = 5 }
                } };
            source.Skills = new[] { skill }; source.UnitSkillKits = new[] { Kit(source.Soldiers[0].Kind) };
            var simulation = new Battle(source); simulation.TryBeginCast(0, 0, SkillAim.AtPoint(source.Soldiers[0].Position)); simulation.Step(1, Array.Empty<ScheduledInput>());
            var row = simulation.CaptureSkillStats().Single(x => x.FactionId == 1);
            Assert.That(row.CastCount, Is.EqualTo(1)); Assert.That(row.HitCount, Is.EqualTo(1)); Assert.That(row.Damage, Is.EqualTo(2));
        }
    }
}
