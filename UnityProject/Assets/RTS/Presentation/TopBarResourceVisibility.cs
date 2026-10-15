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
            BowGear,
            Rations,
            Bricks,
            Tack,
            Planks,
            Books,
            Incense,
            Charms,
            RepairKits,
            Tools,
            CutStone,
            Frames
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
            return economy != null && (economy.Civ == CivKind.Metallurgy || economy.Charcoal > 0 || CommonCraftFrom(economy, 1));
        }

        /// <summary>The common craft tree (33): every civilisation makes planks, charcoal and cut stone from its first
        /// age, and frames from its second. Before that they are only noise in the bar (10-16: it ran over three rows).</summary>
        private static bool CommonCraftFrom(EconomyView economy, int age)
        {
            return economy.CraftTree1Enabled && economy.Age >= age;
        }

        /// <summary>Ore and metal belong to the civilisations that dig (metallurgy, mountain); others see them once they
        /// hold some (trade, caravans).</summary>
        public static bool ShowOreAndMetal(EconomyView economy)
        {
            return economy != null && economy.Industry
                && (economy.Civ == CivKind.Metallurgy || economy.Civ == CivKind.Mountain || economy.Ore > 0 || economy.Metal > 0);
        }

        /// <summary>Gems: the mountain and caravan civilisations make them; others see them once they hold some.</summary>
        public static bool ShowGems(EconomyView economy)
        {
            return economy != null && economy.Ages
                && (economy.Civ == CivKind.Mountain || economy.Civ == CivKind.Caravan || economy.Gems > 0);
        }

        public static bool ShowSteel(EconomyView economy)
        {
            return economy != null && (economy.Civ == CivKind.Metallurgy || economy.Steel > 0);
        }

        public static bool ShowBowGear(EconomyView economy)
        {
            return economy != null && (economy.Civ == CivKind.Forestry || economy.BowGear > 0);
        }

        public static bool ShowRations(EconomyView economy)
        {
            return economy != null && economy.RationsEnabled && (economy.Civ == CivKind.Agrarian || economy.Rations > 0);
        }

        public static bool ShowBricks(EconomyView economy)
        {
            return economy != null && economy.BricksEnabled && (economy.Civ == CivKind.Masonry || economy.Bricks > 0);
        }

        public static bool ShowTack(EconomyView economy)
        {
            return economy != null && economy.TackEnabled && (economy.Civ == CivKind.Cavalry || economy.Tack > 0);
        }

        public static bool ShowPlanks(EconomyView economy)
        {
            return economy != null && ((economy.PlanksEnabled && (economy.Civ == CivKind.Bridge || economy.Planks > 0))
                || CommonCraftFrom(economy, 1) || economy.Planks > 0);
        }

        public static bool ShowBooks(EconomyView economy)
        {
            return economy != null && economy.BooksEnabled && (economy.Civ == CivKind.Academy || economy.Books > 0);
        }

        public static bool ShowIncense(EconomyView economy)
        {
            return economy != null && economy.IncenseEnabled && (economy.Civ == CivKind.Cult || economy.Incense > 0);
        }

        public static bool ShowCharms(EconomyView economy)
        {
            return economy != null && economy.CharmsEnabled && (economy.Civ == CivKind.Sanctuary || economy.Charms > 0);
        }

        public static bool ShowRepairKits(EconomyView economy)
        {
            return economy != null && economy.RepairKitsEnabled && (economy.Civ == CivKind.Tollgate || economy.RepairKits > 0);
        }

        public static bool ShowTools(EconomyView economy)
        {
            return economy != null && economy.ToolsEnabled && (economy.Civ == CivKind.Metropolis || economy.Tools > 0);
        }

        public static bool ShowCutStone(EconomyView economy)
        {
            return economy != null && (CommonCraftFrom(economy, 1) || economy.CutStone > 0);
        }

        public static bool ShowFrames(EconomyView economy)
        {
            return economy != null && (CommonCraftFrom(economy, 2) || economy.Frames > 0);
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
            if (ShowOreAndMetal(economy))
            {
                target.Add(new ResourceEntry(ResourceKind.Ore, UiText.T("Ore", "鉱石"), economy.Ore));
                target.Add(new ResourceEntry(ResourceKind.Metal, UiText.T("Metal", "金属"), economy.Metal));
            }
            if (economy.Ages)
                target.Add(new ResourceEntry(ResourceKind.Stone, UiText.T("Stone", "石"), economy.Stone));
            if (ShowGems(economy))
                target.Add(new ResourceEntry(ResourceKind.Gems, UiText.T("Gems", "宝石"), economy.Gems));
            if (ShowGold(economy))
                target.Add(new ResourceEntry(ResourceKind.Gold, UiText.T("Gold", "金"), economy.Gold));
            if (ShowCharcoal(economy))
                target.Add(new ResourceEntry(ResourceKind.Charcoal, UiText.T("Charcoal", "木炭"), economy.Charcoal));
            if (ShowSteel(economy))
                target.Add(new ResourceEntry(ResourceKind.Steel, UiText.T("Steel", "鋼"), economy.Steel));
            if (ShowRations(economy))
                target.Add(new ResourceEntry(ResourceKind.Rations, UiText.T("Rations", "糧食"), economy.Rations));
            if (ShowBricks(economy))
                target.Add(new ResourceEntry(ResourceKind.Bricks, UiText.T("Bricks", "煉瓦"), economy.Bricks));
            if (ShowTack(economy))
                target.Add(new ResourceEntry(ResourceKind.Tack, UiText.T("Tack", "馬具"), economy.Tack));
            if (ShowPlanks(economy))
                target.Add(new ResourceEntry(ResourceKind.Planks, UiText.T("Planks", "板"), economy.Planks));
            if (ShowCutStone(economy))
                target.Add(new ResourceEntry(ResourceKind.CutStone, UiText.T("Cut stone", "切石"), economy.CutStone));
            if (ShowFrames(economy))
                target.Add(new ResourceEntry(ResourceKind.Frames, UiText.T("Frames", "建材"), economy.Frames));
            if (ShowBooks(economy))
                target.Add(new ResourceEntry(ResourceKind.Books, UiText.T("Books", "書物"), economy.Books));
            if (ShowIncense(economy))
                target.Add(new ResourceEntry(ResourceKind.Incense, UiText.T("Incense", "香"), economy.Incense));
            if (ShowCharms(economy))
                target.Add(new ResourceEntry(ResourceKind.Charms, UiText.T("Charms", "護符"), economy.Charms));
            if (ShowRepairKits(economy))
                target.Add(new ResourceEntry(ResourceKind.RepairKits, UiText.T("Repair kits", "補修材"), economy.RepairKits));
            if (ShowTools(economy))
                target.Add(new ResourceEntry(ResourceKind.Tools, UiText.T("Tools", "工具"), economy.Tools));
            if (ShowBowGear(economy))
                target.Add(new ResourceEntry(ResourceKind.BowGear, UiText.T("Bow gear", "弓具"), economy.BowGear));
        }
    }
}
