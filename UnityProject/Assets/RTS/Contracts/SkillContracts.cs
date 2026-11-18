using System;

namespace Rts.Contracts
{
    /// <summary>How a CastSkill command supplies its target.</summary>
    public enum SkillCommandAimKind : byte { None, Soldier, Point }

    /// <summary>Replay-safe target data for a CastSkill economy command.</summary>
    public readonly struct SkillCommandAim
    {
        public SkillCommandAimKind Kind { get; }
        public uint SoldierId { get; }
        public SimPoint Point { get; }

        public SkillCommandAim(SkillCommandAimKind kind, uint soldierId, SimPoint point)
        { Kind = kind; SoldierId = soldierId; Point = point; }

        public static SkillCommandAim None => new SkillCommandAim(SkillCommandAimKind.None, 0, default(SimPoint));
        public static SkillCommandAim AtSoldier(uint soldierId) => new SkillCommandAim(SkillCommandAimKind.Soldier, soldierId, default(SimPoint));
        public static SkillCommandAim AtPoint(SimPoint point) => new SkillCommandAim(SkillCommandAimKind.Point, 0, point);
    }

    /// <summary>A visible delayed or persistent skill area.</summary>
    public readonly struct SkillAreaView
    {
        public uint FactionId { get; }
        public int SkillId { get; }
        public SimPoint Center { get; }
        public byte Shape { get; }
        public int Radius { get; }
        public int Length { get; }
        public int Width { get; }
        public SimPoint Direction { get; }
        public long StartTick { get; }
        public long EndTick { get; }

        public SkillAreaView(uint factionId, int skillId, SimPoint center, byte shape, int radius, int length, int width,
            SimPoint direction, long startTick, long endTick)
        {
            FactionId = factionId; SkillId = skillId; Center = center; Shape = shape; Radius = radius;
            Length = length; Width = width; Direction = direction; StartTick = startTick; EndTick = endTick;
        }
    }

    public readonly struct SkillProjectileView
    {
        public uint FactionId { get; }
        public int SkillId { get; }
        public SimPoint Position { get; }
        public SimPoint Direction { get; }
        public int Length { get; }
        public int Width { get; }

        public SkillProjectileView(uint factionId, int skillId, SimPoint position, SimPoint direction, int length, int width)
        { FactionId = factionId; SkillId = skillId; Position = position; Direction = direction; Length = length; Width = width; }
    }

    public readonly struct SkillCloneView
    {
        public uint FactionId { get; }
        public SimPoint Position { get; }
        public int OwnerSoldierId { get; }
        public long EndTick { get; }

        public SkillCloneView(uint factionId, SimPoint position, int ownerSoldierId, long endTick)
        { FactionId = factionId; Position = position; OwnerSoldierId = ownerSoldierId; EndTick = endTick; }
    }

    public readonly struct SkillMoveTrailView
    {
        public uint FactionId { get; }
        public int SkillId { get; }
        public SimPoint From { get; }
        public SimPoint To { get; }
        public long Tick { get; }

        public SkillMoveTrailView(uint factionId, int skillId, SimPoint from, SimPoint to, long tick)
        { FactionId = factionId; SkillId = skillId; From = from; To = to; Tick = tick; }
    }
}
