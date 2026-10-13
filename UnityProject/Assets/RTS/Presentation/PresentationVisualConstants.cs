using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Display-only timing and carry-marker constants. They never feed the simulation.</summary>
    internal static class PresentationVisualConstants
    {
        public const float CombatHoldSeconds = 1.2f;
        public const float DeathSeconds = 1.5f;
        public const float ArrowSeconds = 0.3f;
        public const float CarryBoxSize = 0.35f;
        public const float CarryBoxHeight = 1.65f;
        public const float UnitWalkBaseSpeed = 1.25f;
        public const float UnitWalkSpeedVariation = 0.08f;
        public const float UnitWalkPlaybackMinimum = 0.5f;
        public const float UnitWalkPlaybackMaximum = 2f;

        public static Color CarryColor(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Food: return new Color(1f, 0.85f, 0.12f);
                case ResourceKind.Wood: return new Color(0.45f, 0.24f, 0.1f);
                case ResourceKind.Stone: return new Color(0.55f, 0.56f, 0.6f);
                case ResourceKind.Ore: return new Color(0.32f, 0.42f, 0.52f);
                case ResourceKind.Metal: return new Color(0.82f, 0.86f, 0.92f);
            case ResourceKind.Rations: return new Color(0.86f, 0.68f, 0.24f);
            case ResourceKind.Tack: return new Color(0.46f, 0.28f, 0.12f);
                case ResourceKind.Preserves: return new Color(0.92f, 0.72f, 0.28f);
                case ResourceKind.Bricks: return new Color(0.72f, 0.28f, 0.16f);
                case ResourceKind.TradeGoods: return new Color(0.78f, 0.54f, 0.30f);
                default: return Color.white;
            }
        }
    }
}
