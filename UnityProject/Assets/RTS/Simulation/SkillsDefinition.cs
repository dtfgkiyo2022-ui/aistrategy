using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    public enum SkillResource : byte { None, Mana, Energy, Rage, Health }
    public enum SkillTargeting : byte { Self, Unit, Point, Direction }
    public enum SkillShape : byte { Single, Circle, Cone, Line }
    public enum SkillAffects : byte { Enemies, Allies, Self, AlliesAndSelf }
    public enum SkillDamageType : byte { Physical, Magic, True }
    public enum SkillEffectKind : byte { Damage, Cc, Shield, Heal, DamageReduction, Invulnerable, Buff, Debuff }
    public enum SkillCcKind : byte { Stun, Slow, Root, KnockUp, Silence, Taunt, Fear, Pull, Push }
    public enum SkillStat : byte { AttackSpeed, MoveSpeed, Armor, MagicResist, AttackDamage, AbilityPower }
    public enum SkillAmountKind : byte { Fixed, Permille }

    public sealed class SkillDefinition
    {
        public int Id;
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
        public SkillEffect[] Effects = Array.Empty<SkillEffect>();
    }

    public sealed class SkillEffect
    {
        public SkillEffectKind Kind;
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
    }

    public sealed class UnitSkillKit
    {
        public UnitKind UnitKind;
        public SkillResource Resource;
        public int ResourceMax;
        public int RegenPerSecond;
        public SkillEffect Passive;
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
