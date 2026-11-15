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
}
