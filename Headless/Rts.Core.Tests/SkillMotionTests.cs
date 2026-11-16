using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class SkillMotionTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(SkillDefinition skill)
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.Skills = true;
            scenario.Economy.AiLevelWest = 0;
            scenario.Economy.AiLevelEast = 0;
            scenario.Skills = new[] { skill };
            scenario.UnitSkillKits = new[]
            {
                new UnitSkillKit { UnitKind = scenario.Soldiers[0].Kind, FactionId = 1, SkillIds = new[] { skill.Id, 0, 0, 0 } }
            };
            for (int i = 0; i < scenario.Soldiers.Length; i++)
                if (scenario.Soldiers[i].Alive && scenario.Soldiers[i].FactionId == 2)
                {
                    scenario.Soldiers[i].Position = new SimPoint(
                        Fix64.FromRaw(scenario.Soldiers[0].Position.X.Raw + Fix64.FromInt(10).Raw),
                        scenario.Soldiers[0].Position.Z);
                    break;
                }
            return scenario;
        }

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", Flags).GetValue(simulation);

        private static int AliveCount(Battle simulation, string fieldName, int side)
        {
            var outer = (Array)World(simulation).GetType().GetField(fieldName, Flags).GetValue(World(simulation));
            var inner = (Array)outer.GetValue(side);
            var alive = inner.GetType().GetElementType().GetField("Alive", Flags);
            int count = 0;
            for (int i = 0; i < inner.Length; i++) if ((bool)alive.GetValue(inner.GetValue(i))) count++;
            return count;
        }

        private static int Enemy(Battle simulation, ScenarioDefinition scenario)
        {
            for (int i = 0; i < scenario.Soldiers.Length; i++)
                if (scenario.Soldiers[i].Alive && scenario.Soldiers[i].FactionId == 2) return i;
            Assert.Fail("enemy soldier missing");
            return -1;
        }

        [Test]
        public void ProjectileUsesFixedPointPoolAndPublishesView()
        {
            var skill = new SkillDefinition
            {
                Id = 1, CastTicks = 0, CooldownTicks = 20, Range = Fix64.FromInt(20),
                Targeting = SkillTargeting.Point, Shape = SkillShape.Line, Length = 10, Width = 2,
                Affects = SkillAffects.Enemies,
                Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Projectile, ProjectileSpeed = Fix64.FromInt(1), ProjectileLength = 8, ProjectileWidth = 1, ProjectilePierceCount = 3, IgnoresTerrain = true, Base = 3, DamageType = SkillDamageType.True } }
            };
            var scenario = Scenario(skill);
            var simulation = new Battle(scenario);
            var enemy = Enemy(simulation, scenario);
            var aim = SkillAim.AtPoint(scenario.Soldiers[enemy].Position);
            typeof(Battle).GetMethod("ApplySkill", Flags).Invoke(simulation, new object[] { 0, skill, aim });
            simulation.Step(1, Array.Empty<ScheduledInput>());
            Assert.That(AliveCount(simulation, "SkillProjectiles", 0) + AliveCount(simulation, "SkillProjectiles", 1), Is.GreaterThan(0));
            Assert.That(simulation.Capture(1).SkillProjectiles.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DashMovesOnTheTickTimeline()
        {
            var skill = new SkillDefinition
            {
                Id = 1, CastTicks = 0, CooldownTicks = 20, Range = Fix64.FromInt(20),
                Targeting = SkillTargeting.Direction, Shape = SkillShape.Line, Length = 10, Width = 2,
                Affects = SkillAffects.Enemies,
                Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Move, MoveKind = SkillMoveKind.Dash, MoveDistance = Fix64.FromInt(4), MoveSpeed = Fix64.FromInt(1) } }
            };
            var scenario = Scenario(skill);
            var simulation = new Battle(scenario);
            var start = scenario.Soldiers[0].Position;
            Assert.That(simulation.TryBeginCast(0, 0, SkillAim.Along(new SimPoint(Fix64.FromInt(1), Fix64.FromInt(0)))), Is.EqualTo(SkillCastFailure.None));
            simulation.Step(1, Array.Empty<ScheduledInput>());
            var frame = simulation.Capture(1);
            var unit = frame.Units.Single(x => x.Id == scenario.Soldiers[0].Id && x.IsOwn);
            Assert.That(unit.Position.X.Raw, Is.GreaterThan(start.X.Raw));
            Assert.That(frame.SkillMoveTrails.Count, Is.GreaterThan(0));
        }

        [Test]
        public void CloneAndSummonUseSeparateFixedPools()
        {
            var skill = new SkillDefinition
            {
                Id = 1, CastTicks = 0, CooldownTicks = 20, Targeting = SkillTargeting.Self, Shape = SkillShape.Single,
                Affects = SkillAffects.Self,
                Effects = new[]
                {
                    new SkillEffect { Kind = SkillEffectKind.Clone, CloneCount = 1, CloneDurationTicks = 50 },
                    new SkillEffect { Kind = SkillEffectKind.Summon, SummonHp = 20, SummonDamage = 2, SummonDurationTicks = 50, SummonAttackIntervalTicks = 5, SummonUnitKind = UnitKind.Infantry }
                }
            };
            var scenario = Scenario(skill);
            var simulation = new Battle(scenario);
            Assert.That(simulation.TryBeginCast(0, 0, SkillAim.AtPoint(scenario.Soldiers[0].Position)), Is.EqualTo(SkillCastFailure.None));
            simulation.Step(1, Array.Empty<ScheduledInput>());
            Assert.That(AliveCount(simulation, "SkillClones", 0), Is.EqualTo(1));
            Assert.That(AliveCount(simulation, "SkillSummons", 0), Is.EqualTo(1));
            Assert.That(simulation.Capture(1).SkillClones.Count, Is.EqualTo(1));
        }

        [Test]
        public void MarkStacksGateConditionAndAccumulateDamage()
        {
            var skill = new SkillDefinition
            {
                Id = 1, CastTicks = 0, CooldownTicks = 1, Range = Fix64.FromInt(20), Targeting = SkillTargeting.Unit,
                Shape = SkillShape.Single, Affects = SkillAffects.Enemies,
                Effects = new[]
                {
                    new SkillEffect { Kind = SkillEffectKind.Mark, MarkId = 7, MarkMaxStacks = 2, MarkDurationTicks = 30, MarkBurstDamagePermille = 1000 },
                    new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.True, Base = 5 },
                    new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.True, Base = 7, Condition = SkillConditionKind.TargetMarkStacks, ConditionMarkId = 7, ConditionMarkStacks = 2 }
                }
            };
            var scenario = Scenario(skill);
            var simulation = new Battle(scenario);
            int enemy = Enemy(simulation, scenario);
            var aim = SkillAim.AtSoldier(enemy);
            Assert.That(simulation.TryBeginCast(0, 0, aim), Is.EqualTo(SkillCastFailure.None));
            simulation.Step(1, Array.Empty<ScheduledInput>());
            Assert.That(simulation.TryBeginCast(0, 0, aim), Is.EqualTo(SkillCastFailure.None));
            simulation.Step(2, Array.Empty<ScheduledInput>());
            var stats = simulation.CaptureSkillStats().Single(x => x.FactionId == 1 && x.SkillId == 1);
            Assert.That(stats.Damage, Is.GreaterThan(10));
            Assert.That(stats.HitCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Combat4DefinitionsRoundTripNestedMarkEffects()
        {
            var skill = new SkillDefinition
            {
                Id = 1, Resource = SkillResource.Energy, Cost = 3, CooldownTicks = 4, Range = Fix64.FromInt(8),
                Targeting = SkillTargeting.Unit, Shape = SkillShape.Single, Affects = SkillAffects.Enemies,
                Effects = new[]
                {
                    new SkillEffect
                    {
                        Kind = SkillEffectKind.Mark, MarkId = 3, MarkMaxStacks = 4, MarkDurationTicks = 9,
                        Condition = SkillConditionKind.TargetMarkStacks, ConditionMarkId = 3, ConditionMarkStacks = 2,
                        MarkBurstEffects = new[] { new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.True, Base = 11 } }
                    }
                }
            };
            var scenario = Scenario(skill);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario));
            var effect = decoded.Skills.Single().Effects.Single();
            Assert.That(effect.ConditionMarkId, Is.EqualTo(3));
            Assert.That(effect.ConditionMarkStacks, Is.EqualTo(2));
            Assert.That(effect.MarkBurstEffects.Single().Base, Is.EqualTo(11));
        }
    }
}
