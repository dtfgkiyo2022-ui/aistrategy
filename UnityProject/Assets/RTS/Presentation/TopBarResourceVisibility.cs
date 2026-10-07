using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Determines which optional stocks belong in the shared top bar.</summary>
    public static class TopBarResourceVisibility
    {
        public static bool ShowGold(EconomyView economy)
        {
            return economy != null && economy.Ages;
        }

        public static bool ShowCharcoal(EconomyView economy)
        {
            return economy != null && (economy.Civ == CivKind.Metallurgy || economy.Charcoal > 0);
        }

        public static bool ShowSteel(EconomyView economy)
        {
            return economy != null && (economy.Civ == CivKind.Metallurgy || economy.Steel > 0);
        }

        public static bool ShowBowGear(EconomyView economy)
        {
            return economy != null && (economy.Civ == CivKind.Forestry || economy.BowGear > 0);
        }
    }
}
