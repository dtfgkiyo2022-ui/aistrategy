using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Pure labels shared by the two economy-panel frontends and EditMode tests.</summary>
    public static class GoodsMarketUiText
    {
        public static string GoodsTradeLabel(ResourceKind goods, ResourceKind take, int lot)
            => GoodsName(goods) + lot + UiText.T(" -> ", " → ") + ResourceName(take);

        public static string GoodsName(ResourceKind kind)
            => kind == ResourceKind.Steel ? UiText.T("Steel", "鋼") : kind == ResourceKind.BowGear ? UiText.T("Bow gear", "弓具")
                : kind == ResourceKind.Rations ? UiText.T("Rations", "糧食") : kind == ResourceKind.Bricks ? UiText.T("Bricks", "煉瓦")
                : kind == ResourceKind.Tack ? UiText.T("Tack", "馬具") : kind == ResourceKind.Planks ? UiText.T("Planks", "板材")
                : kind == ResourceKind.Books ? UiText.T("Books", "書物") : kind == ResourceKind.Incense ? UiText.T("Incense", "香")
                : kind == ResourceKind.Charms ? UiText.T("Charms", "護符") : kind == ResourceKind.RepairKits ? UiText.T("Repair kits", "補修材")
                : kind == ResourceKind.Tools ? UiText.T("Tools", "工具")
                : kind == ResourceKind.ArmsKit ? UiText.T("Arms kit", "Arms kit")
                : kind == ResourceKind.Scholarly ? UiText.T("Scholarly", "Scholarly")
                : kind == ResourceKind.FineArmsKit ? UiText.T("Fine arms kit", "Fine arms kit")
                : kind == ResourceKind.SiegeParts ? UiText.T("Siege parts", "攻城部品")
                : kind == ResourceKind.Mechanism ? UiText.T("Mechanism", "精密部品") : kind.ToString();

        private static string ResourceName(ResourceKind kind)
            => kind == ResourceKind.Food ? UiText.T("Food", "食料") : kind == ResourceKind.Wood ? UiText.T("Wood", "木材")
                : kind == ResourceKind.Stone ? UiText.T("Stone", "石") : kind.ToString();
    }
}
