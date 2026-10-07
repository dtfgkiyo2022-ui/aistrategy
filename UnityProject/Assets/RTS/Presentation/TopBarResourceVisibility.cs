using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Determines which optional stocks belong in the shared top bar.</summary>
    public static class TopBarResourceVisibility
    {
        public enum ResourceKind
        {
            Food,
            Wood,
            Ore,
            Metal,
            Stone,
            Gems,
            Gold,
            Charcoal,
            Steel,
            BowGear
        }

        public readonly struct ResourceEntry
        {
            public ResourceKind Kind { get; }
            public string Label { get; }
            public int Value { get; }

            public ResourceEntry(ResourceKind kind, string label, int value)
            {
                Kind = kind;
                Label = label;
                Value = value;
            }
        }

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

        /// <summary>
        /// Fills the shared top-bar resource list. This is deliberately plain C# so IMGUI and UI Toolkit use the same
        /// visibility rules and number source; the caller owns and reuses the list.
        /// </summary>
        public static void Fill(EconomyView economy, IList<ResourceEntry> target)
        {
            target.Clear();
            if (economy == null) return;

            target.Add(new ResourceEntry(ResourceKind.Food, UiText.T("Food", "食料"), economy.Food));
            target.Add(new ResourceEntry(ResourceKind.Wood, UiText.T("Wood", "木材"), economy.Wood));
            if (economy.Industry)
            {
                target.Add(new ResourceEntry(ResourceKind.Ore, UiText.T("Ore", "鉱石"), economy.Ore));
                target.Add(new ResourceEntry(ResourceKind.Metal, UiText.T("Metal", "金属"), economy.Metal));
            }
            if (economy.Ages)
            {
                target.Add(new ResourceEntry(ResourceKind.Stone, UiText.T("Stone", "石"), economy.Stone));
                target.Add(new ResourceEntry(ResourceKind.Gems, UiText.T("Gems", "宝石"), economy.Gems));
            }
            if (ShowGold(economy))
                target.Add(new ResourceEntry(ResourceKind.Gold, UiText.T("Gold", "金"), economy.Gold));
            if (ShowCharcoal(economy))
                target.Add(new ResourceEntry(ResourceKind.Charcoal, UiText.T("Charcoal", "木炭"), economy.Charcoal));
            if (ShowSteel(economy))
                target.Add(new ResourceEntry(ResourceKind.Steel, UiText.T("Steel", "鋼"), economy.Steel));
            if (ShowBowGear(economy))
                target.Add(new ResourceEntry(ResourceKind.BowGear, UiText.T("Bow gear", "弓具"), economy.BowGear));
        }
    }
}
