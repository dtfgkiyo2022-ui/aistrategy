using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    public enum SkillResource : byte { None, Mana, Energy, Rage, Health }
    public enum SkillTargeting : byte { Self, Unit, Point, Direction }
    public enum SkillShape : byte { Single, Circle, Cone, Line }
    public enum SkillAffects : byte { Enemies, Allies, Self, AlliesAndSelf }
    public enum SkillDamageType : byte { Physical, Magic, True }
    public enum SkillEffectKind : byte { Damage, Cc, Shield, Heal, DamageReduction, Invulnerable, Buff, Debuff, Camouflage, CcImmunity, Projectile, Move, Clone, Summon, Mark }
    public enum SkillCcKind : byte { Stun, Slow, Root, KnockUp, Silence, Taunt, Fear, Pull, Push }
    public enum SkillStat : byte { AttackSpeed, MoveSpeed, Armor, MagicResist, AttackDamage, AbilityPower, DamageTaken }
    public enum SkillAmountKind : byte { Fixed, Permille }
    public enum SkillEffectAppliesTo : byte { Inherit, Enemies, Allies }
    public enum PassiveKind : byte { None, Formation, Aim, Momentum, Fleet, Prayer, HardSkin }
    public enum SkillMoveKind : byte { None, Dash, Teleport, Leap, Backstep }
    public enum SkillConditionKind : byte { None, TargetCc, TargetHpBelowPermille, NearCasterClone, TargetMarkStacks }

    public sealed class SkillDefinition
    {
        public int Id;
        public string Name = "";
        public SkillResource Resource;
        public int Cost;
        public int CooldownTicks = 1;
        public int CastTicks;
        public Fix64 Range;
        public SkillTargeting Targeting;
        public bool TargetAllies;
        public SkillShape Shape;
        public int Radius;
        public int AngleDegrees = 90;
        public int Length;
        public int Width;
        public int Delay;
        public int PersistTicks;
        public int PulseIntervalTicks;
        public SkillAffects Affects;
        /// <summary>Combat4: when true, projectile and area effects are emitted from every live clone of the caster.</summary>
        public bool MirrorFromClones;
        public SkillEffect[] Effects = Array.Empty<SkillEffect>();
    }

    public sealed class SkillEffect
    {
        public SkillEffectKind Kind;
        public SkillEffectAppliesTo AppliesTo;
        public SkillDamageType DamageType;
        public int Base;
        public int AdPermille;
        public int ApPermille;
        public int TargetMaxHpPermille;
        public int TargetMissingHpPermille;
        public SkillCcKind CcKind;
        public int DurationTicks;
        public int Strength;
        public SkillStat Stat;
        public SkillAmountKind AmountKind;
        // Combat4 projectile data. All distances and speeds are simulation metres/Fix64; angles are integer degrees.
        public Fix64 ProjectileSpeed;
        public int ProjectileLength, ProjectileWidth, ProjectilePierceCount, ProjectileBounceCount, ProjectileBounceDistance;
        public bool ProjectileReturns, IgnoresTerrain;
        public int ProjectileFanCount, ProjectileFanAngleDegrees;
        // Combat4 movement data.
        public SkillMoveKind MoveKind;
        public Fix64 MoveDistance, MoveSpeed;
        public bool MoveStopsOnFirstHit;
        // Combat4 clone/summon data.
        public int CloneCount, CloneDurationTicks, SummonHp, SummonDamage, SummonAttackIntervalTicks, SummonDurationTicks;
        public UnitKind SummonUnitKind;
        // Combat4 mark/condition data. Nested effect arrays are intentionally bounded by scenario validation.
        public SkillConditionKind Condition;
        public int ConditionValue;
        public int ConditionMarkId, ConditionMarkStacks;
        public int MarkId, MarkMaxStacks, MarkDurationTicks;
        public int MarkBurstDamagePermille;
        public SkillEffect[] MarkBurstEffects = Array.Empty<SkillEffect>();
        public SkillEffect[] MarkTargetEffects = Array.Empty<SkillEffect>();
    }

    public sealed class UnitSkillKit
    {
        public UnitKind UnitKind;
        /// <summary>Zero is the normal shared kit; combat4 demo kits may be faction-specific.</summary>
        public uint FactionId;
        public SkillResource Resource;
        public int ResourceMax;
        public int RegenPerSecond;
        public SkillEffect Passive;
        public PassiveKind PassiveKind;
        public int[] SkillIds = new int[4];
    }

    public struct SkillAim
    {
        public int TargetSoldierIndex;
        public SimPoint Point;
        public SimPoint Direction;
        public SkillAim(int targetSoldierIndex) { TargetSoldierIndex = targetSoldierIndex; Point = default; Direction = default; }
        public SkillAim(SimPoint point, SimPoint direction) { TargetSoldierIndex = -1; Point = point; Direction = direction; }
        public static SkillAim AtSoldier(int index) => new SkillAim(index);
        public static SkillAim AtPoint(SimPoint point) => new SkillAim(-1, point, default);
        public static SkillAim Along(SimPoint direction) => new SkillAim(-1, default, direction);
        public SkillAim(int targetSoldierIndex, SimPoint point, SimPoint direction)
        { TargetSoldierIndex = targetSoldierIndex; Point = point; Direction = direction; }
    }

    public enum SkillCastFailure : byte
    {
        None, InvalidSoldier, NoSkill, Cooldown, Resource, CrowdControlled, OutOfRange, InvalidTarget, Busy, PoolFull
    }

    public readonly struct SkillStatsSnapshot
    {
        public readonly uint FactionId;
        public readonly int SkillId;
        public readonly long CastCount;
        public readonly long HitCount;
        public readonly long Damage;
        public readonly long CcTicks;
        public readonly long ShieldBlocked;

        public SkillStatsSnapshot(uint factionId, int skillId, long castCount, long hitCount, long damage, long ccTicks, long shieldBlocked)
        {
            FactionId = factionId; SkillId = skillId; CastCount = castCount; HitCount = hitCount;
            Damage = damage; CcTicks = ccTicks; ShieldBlocked = shieldBlocked;
        }
    }
}
