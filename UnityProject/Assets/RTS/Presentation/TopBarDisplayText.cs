using System.Globalization;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Language-aware top-bar text shared by the IMGUI and UI Toolkit renderers.</summary>
    public static class TopBarDisplayText
    {
        public static string Age(EconomyView economy)
        {
            if (economy == null || !economy.Ages) return "";
            if (economy.AdvanceRemaining > 0)
                return UiText.T("Advancing to ", "進めている：") + AgeName(economy.AdvancingTo,
                    economy.Civ == CivKind.Primitive ? 1 : 2) + " " + Seconds(economy.AdvanceRemaining) + "  |  ";
            return AgeName(economy.Civ, economy.Age) + "  |  ";
        }

        public static string Population(int population, int populationCap, int idle)
        {
            return UiText.T("Pop ", "人口 ") + FormatCount(population) + "/" + FormatCount(populationCap)
                + UiText.T("  Idle ", "  待機 ") + FormatCount(idle);
        }

        public static string FormatCount(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Seconds(long ticks)
        {
            return (ticks < 0 ? 0 : ticks / 20f).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        public static string AgeName(CivKind civ, int age)
        {
            if (age < 2) return CivName(civ);
            if (civ == CivKind.Agrarian)
                return age == 2 ? UiText.T("farming, city age", "農耕の文明・都市の時代") : UiText.T("farming, trade age", "農耕の文明・交易の時代");
            if (civ == CivKind.Metallurgy)
                return age == 2 ? UiText.T("metallurgy, iron age", "冶金の文明・鉄の時代") : UiText.T("metallurgy, steel age", "冶金の文明・鋼の時代");
            return CivName(civ) + (age == 2 ? UiText.T(", second age", "・2つ目の時代") : UiText.T(", third age", "・3つ目の時代"));
        }

        private static string CivName(CivKind civ)
        {
            switch (civ)
            {
                case CivKind.Agrarian: return UiText.T("farming", "農耕の文明");
                case CivKind.Metallurgy: return UiText.T("metallurgy", "冶金の文明");
                case CivKind.Forestry: return UiText.T("forestry", "森林・木工の文明");
                case CivKind.Masonry: return UiText.T("masonry", "石工・城塞の文明");
                case CivKind.Caravan: return UiText.T("caravan", "隊商・交易の文明");
                case CivKind.Cavalry: return UiText.T("cavalry", "騎馬・機動の文明");
                case CivKind.Bridge: return UiText.T("engineering", "工兵・架橋の文明");
                case CivKind.Academy: return UiText.T("academy", "学府・技術の文明");
                case CivKind.Cult: return UiText.T("cult", "教団・改宗の文明");
                case CivKind.Fishing: return UiText.T("fishing", "漁労・港湾の文明");
                case CivKind.Mountain: return UiText.T("mountain", "山岳・鉱夫の文明");
                case CivKind.Tollgate: return UiText.T("tollgate", "関所・要塞の文明");
                case CivKind.Metropolis: return UiText.T("metropolis", "都市・人口の文明");
                case CivKind.Sanctuary: return UiText.T("sanctuary", "聖地・遺物の文明");
                default: return UiText.T("primitive age", "原始時代");
            }
        }
    }
}
