namespace Rts.Presentation
{
    /// <summary>Display-only rules for the temporary HP text over a structure.</summary>
    public static class BuildingHealthDisplayRules
    {
        public const float VisibleSeconds = 6f;

        public static string Format(int hp, int maxHp) => "体力 " + hp + " / " + maxHp;

        public static bool IsVisible(float now, float lastHit)
        {
            return lastHit >= 0f && now - lastHit < VisibleSeconds;
        }
    }
}
