using System.Globalization;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Language-aware top-bar text shared by the IMGUI and UI Toolkit renderers.</summary>
    public static class TopBarDisplayText
    {
        // Contracts fixes Primitive=0 and the fourteen selectable civilisations at 1..14.
        // Keeping both languages in this table means every display surface uses the same vocabulary.
        private static readonly string[] CivEnglish =
        {
            "primitive", "agrarian", "metallurgy", "forestry", "masonry", "caravan", "cavalry", "engineering",
            "academy", "cult", "fishing", "mountain", "tollgate", "metropolis", "sanctuary"
        };
        private static readonly string[] CivJapanese =
        {
            "原始", "農耕", "冶金", "森林・木工", "石工・城塞", "隊商・交易", "騎馬・機動", "工兵・架橋",
            "学府・技術", "教団・改宗", "漁労・港湾", "山岳・鉱夫", "関所・要塞", "都市・人口", "聖地・遺物"
        };
        private static readonly string[,] AgeEnglish =
        {
            { "primitive", "", "", "", "" },
            { "", "clearing", "city", "trade", "plenty" },
            { "", "copper", "iron", "steel", "mechanism" },
            { "", "logging", "woodworking", "bowcraft", "great tree" },
            { "", "quarrying", "walls", "fortress", "unbreachable" },
            { "", "peddling", "caravan", "market", "commerce" },
            { "", "nomadic", "cavalry", "gale", "grand expedition" },
            { "", "surveying", "bridgebuilding", "siege", "great works" },
            { "", "scribal", "academy", "invention", "wisdom" },
            { "", "prayer", "preaching", "holy war", "revelation" },
            { "", "fishing village", "harbor", "fleet", "open sea" },
            { "", "shaft", "mine", "gemstones", "mountain king" },
            { "", "gate", "toll", "fortress", "iron wall" },
            { "", "settlement", "town", "city", "metropolis" },
            { "", "pilgrimage", "sanctuary", "relic", "holy ground" }
        };
        private static readonly string[,] AgeJapanese =
        {
            { "原始", "", "", "", "" },
            { "", "開墾", "都市", "交易", "豊穣" },
            { "", "銅", "鉄", "鋼", "機巧" },
            { "", "伐採", "木工", "弓匠", "大樹" },
            { "", "石切り", "城壁", "城塞", "不落" },
            { "", "行商", "隊商", "市場", "通商" },
            { "", "遊牧", "騎兵", "疾風", "大遠征" },
            { "", "測量", "架橋", "攻城", "大工事" },
            { "", "書写", "学府", "発明", "叡智" },
            { "", "祈り", "布教", "聖戦", "天啓" },
            { "", "漁村", "港", "船団", "大海" },
            { "", "坑道", "鉱山", "宝玉", "山の王" },
            { "", "関所", "通行税", "要塞", "鉄壁" },
            { "", "集落", "町", "都市", "大都市" },
            { "", "巡礼", "聖域", "遺物", "神域" }
        };
        private static readonly string[] IntentEnglish =
        {
            "build a village", "make use of the land", "combine", "master", "finish the conflict"
        };
        private static readonly string[] IntentJapanese =
        {
            "村を作る", "土地を生かす", "組み合わせる", "極める", "決着をつける"
        };
        private static readonly string[] UnlockEnglish =
        {
            "villagers, gathering, barracks and infantry are available",
            "the civilisation's processing chain, one non-infantry unit and tier-1 processing are available",
            "the assembler, weapons and a second unit are available",
            "tier-3 processing, level-3 processing buildings, the castle and third-age technologies are available",
            "Total War and Grand Workshop are available"
        };
        private static readonly string[] UnlockJapanese =
        {
            "村人・採集・兵舎と歩兵が使えます",
            "文明の加工連鎖・歩兵以外の兵1種・加工の1段目が使えます",
            "組立所・武具・2つ目の兵が使えます",
            "加工の3段目・加工建物のLv3・城・3つ目の技術が使えます",
            "総力戦・大工房が使えます"
        };

        public static string Age(EconomyView economy)
        {
            if (economy == null || !economy.Ages) return "";
            string stage = UiText.T("Age ", "時代 ") + (economy.AdvanceRemaining > 0 ? economy.Age + 1 : economy.Age)
                + UiText.T("/", "／") + (economy.AgeFour ? "4" : "3") + "  ";
            if (economy.AdvanceRemaining > 0)
                return stage + UiText.T("Advancing to ", "進めている：") + AgeName(economy.AdvancingTo,
                    economy.Civ == CivKind.Primitive ? 1 : economy.Age + 1) + " " + Seconds(economy.AdvanceRemaining) + "  |  ";
            return stage + AgeName(economy.Civ, economy.Age) + "  |  ";
        }

        public static string AgeStage(EconomyView economy)
        {
            if (economy == null || !economy.Ages) return "";
            int age = economy.AdvanceRemaining > 0 ? economy.Age + 1 : economy.Age;
            string numeral = age <= 1 ? "I" : age == 2 ? "II" : age == 3 ? "III" : "IV";
            return UiText.T("AGE ", "段階 ") + numeral;
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

        public static string CivName(CivKind civ)
        {
            int index = CivIndex(civ);
            return UiText.T(CivEnglish[index] + " civilization", CivJapanese[index] + "の文明");
        }

        public static string AgeName(CivKind civ, int age)
        {
            if (age <= 0) return UiText.T("primitive age", "原始の時代");
            int index = CivIndex(civ);
            int clampedAge = age > 4 ? 4 : age;
            return CivName(civ) + UiText.T(" · " + AgeEnglish[index, clampedAge] + " age",
                "・" + AgeJapanese[index, clampedAge] + "の時代");
        }

        public static string AgeAdvanceMessage(CivKind civ, int age)
        {
            int clampedAge = age < 0 ? 0 : age > 4 ? 4 : age;
            return UiText.T("Age " + clampedAge + " reached: " + AgeName(civ, clampedAge) + " — " + IntentEnglish[clampedAge] + ". " + UnlockEnglish[clampedAge],
                "時代" + clampedAge + "に進みました：" + AgeName(civ, clampedAge) + " ― " + IntentJapanese[clampedAge] + "。" + UnlockJapanese[clampedAge]);
        }

        private static int CivIndex(CivKind civ)
        {
            int index = (int)civ;
            return index >= 0 && index < CivEnglish.Length ? index : 0;
        }
    }
}
