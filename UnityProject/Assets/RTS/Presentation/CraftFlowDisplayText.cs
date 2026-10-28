using System;
using System.Globalization;
using System.Text;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Language-aware, display-only text for the CraftFlow contract fields.</summary>
    public static class CraftFlowDisplayText
    {
        public static string BuildingLines(BuildingView building)
        {
            if (building.ProducedPerMinute == 0 && building.InputsPerMinute.Count == 0
                && building.StarvedTicksLastMinute == 0 && building.BlockedTicksLastMinute == 0 && building.FullRatePerMinute == 0)
                return "";

            int starvedPercent = Percent(building.StarvedTicksLastMinute);
            int blockedPercent = Percent(building.BlockedTicksLastMinute);
            var inputs = new StringBuilder();
            for (int i = 0; i < building.InputsPerMinute.Count; i++)
            {
                if (i != 0) inputs.Append(UiText.T(" / ", "・"));
                var input = building.InputsPerMinute[i];
                inputs.Append(GoodsMarketUiText.GoodsName(input.Kind)).Append(' ').Append(input.CountPerMinute)
                    .Append(UiText.T("/min", "/分"));
            }
            if (inputs.Length == 0) inputs.Append(UiText.T("none", "なし"));

            return UiText.T(
                "Produced " + building.ProducedPerMinute + "/min (max " + building.FullRatePerMinute + "/min)\n" +
                "Materials: " + inputs + "\n" +
                "Waiting for material " + starvedPercent + "%  Output blocked " + blockedPercent + "%",
                "作った数 " + building.ProducedPerMinute + "/分（最大 " + building.FullRatePerMinute + "/分）\n" +
                "材料：" + inputs + "\n" +
                "材料待ち " + starvedPercent + "%・出口詰まり " + blockedPercent + "%");
        }

        public static bool IsWarning(BuildingView building)
            => Percent(building.StarvedTicksLastMinute) > 30 || Percent(building.BlockedTicksLastMinute) > 30;

        public static string Production(EconomyView economy)
        {
            if (economy == null || !economy.CraftFlowEnabled || economy.ProductionStatistics.Count == 0) return "";
            var text = new StringBuilder(UiText.T(" Production: ", " 生産："));
            bool first = true;
            foreach (var stat in economy.ProductionStatistics)
            {
                if (stat.ProducedPerMinute == 0 && stat.ConsumedPerMinute == 0) continue;
                if (!first) text.Append(UiText.T("; ", "・"));
                first = false;
                text.Append(GoodsMarketUiText.GoodsName(stat.Kind)).Append(' ')
                    .Append(stat.ProducedPerMinute).Append('/').Append(stat.ConsumedPerMinute)
                    .Append(UiText.T("/min", "/分"));
            }
            return first ? "" : text.ToString();
        }

        private static int Percent(int ticks)
            => Math.Max(0, Math.Min(100, (ticks * 100 + 600) / 1200));
    }
}
