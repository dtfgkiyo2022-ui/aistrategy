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
        // Technical-design-v3 37.5.1: one strength per civilisation for ages 2-4 (AgeStrengths). Rows follow CivEnglish.
        private static readonly string[,] StrengthEnglish =
        {
            { "", "", "", "", "" },
            { "", "", "farms work 1.2x faster", "rations heal 15 HP", "population cap +20" },
            { "", "", "smelters work 1.25x faster", "forged infantry attack +1", "siege parts take 0.7x the time" },
            { "", "", "lumber camps work 1.25x faster", "archers gain +1 range", "buildings cost 0.8x wood" },
            { "", "", "walls have 1.2x HP", "towers hit +2", "the core has 1.3x max HP" },
            { "", "", "trade goods add 5 instead of 4", "market exchanges return +10%", "caravan rewards are 1.5x" },
            { "", "", "stables train in 0.8x the time", "cavalry move 10% faster", "heavy cavalry have +15% HP" },
            { "", "", "bridges take 0.7x the work", "rams deal +20% to buildings", "buildings take 0.8x the work" },
            { "", "", "research takes 0.85x the time", "books discount research to 0.6x", "age-four research costs half" },
            { "", "", "conversion is 20% faster", "incense heals 30 HP", "monks have 1.5x HP" },
            { "", "", "fishing is 1.2x faster", "preserves add 4 food instead of 3", "villagers carry 2 more food" },
            { "", "", "mines and drills work 1.2x faster", "barracks mercenaries cost 8 gems", "mercenaries attack +2" },
            { "", "", "tolls are 1.5x", "tollgates have 1.3x HP", "repair kits mend 60 HP" },
            { "", "", "town villagers train in 0.8x the time", "one more town allowed", "tools cut training time to 0.4x" },
            { "", "", "pilgrimage income is 1.2x", "charms give 1.3x HP", "charms give 1.5x HP" }
        };
        private static readonly string[,] StrengthJapanese =
        {
            { "", "", "", "", "" },
            { "", "", "農場の作る速さが 1.2 倍", "糧食の回復が HP 15 に", "人口の上限 +20" },
            { "", "", "精錬所の作る速さが 1.25 倍", "鍛えた歩兵の攻撃 +1", "攻城部品の作る時間が 0.7 倍" },
            { "", "", "木こり小屋の作る速さが 1.25 倍", "弓兵の射程 +1", "建物の木の費用が 0.8 倍" },
            { "", "", "壁の HP が 1.2 倍", "塔の一撃 +2", "コアの最大 HP が 1.3 倍" },
            { "", "", "交易品で増える量が 4→5", "市場の交換の取り分 +10%", "隊商の報酬が 1.5 倍" },
            { "", "", "厩舎の訓練時間が 0.8 倍", "騎兵の速さ +10%", "重騎兵の HP +15%" },
            { "", "", "橋の建てる手間が 0.7 倍", "破城槌の対建物 +20%", "建物の建てる手間が 0.8 倍" },
            { "", "", "研究の時間が 0.85 倍", "書物の割引が 0.7→0.6", "時代4 の研究の値段が半分" },
            { "", "", "改宗の速さ +20%", "香の回復が HP 30 に", "僧侶の HP が 1.5 倍" },
            { "", "", "漁の速さが 1.2 倍", "保存食で増える食料が 3→4", "食料を運ぶ量 +2" },
            { "", "", "坑道・採掘機の速さが 1.2 倍", "兵舎の傭兵の宝石が 10→8", "傭兵の攻撃 +2" },
            { "", "", "通行料が 1.5 倍", "関所の HP が 1.3 倍", "補修材の修理が 40→60" },
            { "", "", "町の村人の訓練時間が 0.8 倍", "町の数の上限 +1", "道具の割引で訓練時間が 0.5→0.4" },
            { "", "", "巡礼の収入が 1.2 倍", "護符で生まれる兵の HP が 1.3 倍", "護符で生まれる兵の HP が 1.5 倍" }
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

        public static string AgeAdvanceMessage(CivKind civ, int age) => AgeAdvanceMessage(civ, age, false);

        /// <summary>The centre notice; with AgeStrengths it also names the civilisation's strength for that age.</summary>
        public static string AgeAdvanceMessage(CivKind civ, int age, bool ageStrengths)
        {
            int clampedAge = age < 0 ? 0 : age > 4 ? 4 : age;
            string strength = ageStrengths ? AgeStrength(civ, clampedAge) : "";
            return UiText.T("Age " + clampedAge + " reached: " + AgeName(civ, clampedAge) + " — " + IntentEnglish[clampedAge] + ". " + UnlockEnglish[clampedAge]
                    + (strength.Length > 0 ? ". Strength: " + strength : ""),
                "時代" + clampedAge + "に進みました：" + AgeName(civ, clampedAge) + " ― " + IntentJapanese[clampedAge] + "。" + UnlockJapanese[clampedAge]
                    + (strength.Length > 0 ? "。強み：" + strength : ""));
        }

        /// <summary>AgeStrengths: the strength a civilisation gains on reaching the age; empty below age 2.</summary>
        public static string AgeStrength(CivKind civ, int age)
        {
            int index = CivIndex(civ), clampedAge = age < 0 ? 0 : age > 4 ? 4 : age;
            return UiText.T(StrengthEnglish[index, clampedAge], StrengthJapanese[index, clampedAge]);
        }

        public static string CraftTree4Products()
            => UiText.T("CraftTree4: Siege engine, Elite kit", "加工4段目：攻城機関・精鋭キット");

        public static string CannonName() => UiText.T("Cannon", "大砲");

        private static int CivIndex(CivKind civ)
        {
            int index = (int)civ;
            return index >= 0 && index < CivEnglish.Length ? index : 0;
        }
    }
}
