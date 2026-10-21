using System.Globalization;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Language-aware top-bar text shared by the IMGUI and UI Toolkit renderers.</summary>
    public static class TopBarDisplayText
    {
        private static readonly string[] CivEnglish =
        {
            "primitive", "agrarian", "metallurgy", "forestry", "masonry", "caravan", "cavalry", "engineering",
            "academy", "cult", "fishing", "mountain", "tollgate", "metropolis", "sanctuary"
        };
        private static readonly string[] CivJapanese =
        {
            "\u539f\u59cb", "\u8fb2\u8015", "\u51b6\u91d1", "\u6797\u696d", "\u77f3\u5de5", "\u968a\u5546", "\u9a0e\u5175", "\u6a4b\u6881",
            "\u5b66\u8853", "\u6559\u56e3", "\u6f01\u696d", "\u5c71\u5cb3", "\u95a2\u6240", "\u90fd\u5e02", "\u8056\u5730"
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
            { "\u539f\u59cb", "", "", "", "" },
            { "", "\u958b\u62d3", "\u90fd\u5e02", "\u4ea4\u6613", "\u8c4a\u7a63" },
            { "", "\u9285", "\u9244", "\u92fc", "\u6a5f\u69cb" },
            { "", "\u4f10\u63a1", "\u6728\u5de5", "\u5f13\u8853", "\u5927\u6a39" },
            { "", "\u63a1\u77f3", "\u58c1", "\u8981\u585e", "\u4e0d\u843d" },
            { "", "\u884c\u5546", "\u968a\u5546", "\u5e02\u5834", "\u5546\u696d" },
            { "", "\u904a\u7267", "\u9a0e\u5175", "\u65cb\u98a8", "\u5927\u9060\u5f81" },
            { "", "\u8abf\u67fb", "\u6a4b\u6881", "\u653b\u57ce", "\u5927\u5de5\u4e8b" },
            { "", "\u66f8\u8a18", "\u5b66\u9662", "\u767a\u660e", "\u77e5\u6167" },
            { "", "\u7948\u308a", "\u8aac\u6559", "\u8056\u6226", "\u5553\u793a" },
            { "", "\u6f01\u6751", "\u6e2f", "\u8239\u56e3", "\u5927\u6d77" },
            { "", "\u5761\u9053", "\u9271\u5c71", "\u5b9d\u77f3", "\u5c71\u738b" },
            { "", "\u9580", "\u901a\u884c\u6599", "\u8981\u585e", "\u9244\u58c1" },
            { "", "\u96c6\u843d", "\u753a", "\u90fd\u5e02", "\u5927\u90fd\u5e02" },
            { "", "\u5de1\u793c", "\u8056\u57df", "\u907a\u7269", "\u795e\u57df" }
        };
        private static readonly string[] IntentEnglish = { "build a village", "make use of the land", "combine", "master", "finish the conflict" };
        private static readonly string[] IntentJapanese = { "村を作る", "土地を活かす", "組み合わせる", "極める", "争いを終える" };
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
            "住民・採集・兵舎・歩兵が使えます",
            "文明の加工連鎖・歩兵以外の兵1種・加工1段階が使えます",
            "組立所・武具・2つ目の兵が使えます",
            "加工3段階・加工建物Lv3・城・第3時代の技術が使えます",
            "総力戦と大工房が使えます"
        };
        private static readonly string[,] StrengthEnglish =
        {
            { "", "", "", "", "" },
            { "", "", "farms work 1.2x faster", "rations heal 15 HP", "population cap +20" },
            { "", "", "smelters work 1.25x faster", "forged infantry attack +1", "siege parts take 0.7x time" },
            { "", "", "lumber camps work 1.25x faster", "archers gain +1 range", "building wood costs 0.8x" },
            { "", "", "walls gain 1.2x HP", "towers deal +2 damage", "core gains 1.3x max HP" },
            { "", "", "trade goods give +5", "market returns +10%", "caravan rewards 1.5x" },
            { "", "", "stables train 1.25x faster", "cavalry move 10% faster", "heavy cavalry gain 15% HP" },
            { "", "", "bridges take 0.7x work", "rams deal +20% to buildings", "buildings take 0.8x work" },
            { "", "", "research takes 0.85x time", "scholarly goods discount to 0.6x", "age-four research costs half" },
            { "", "", "conversion is 20% faster", "incense heals 30 HP", "monks gain 1.5x HP" },
            { "", "", "fishing is 1.2x faster", "preserves give +1 food", "food carry capacity +2" },
            { "", "", "mines work 1.2x faster", "barracks mercenaries cost 8 gems", "mercenaries deal +2 damage" },
            { "", "", "tolls are 1.5x", "tollgates gain 1.3x HP", "repair kits repair 60 HP" },
            { "", "", "town villagers train 1.25x faster", "town limit +1", "tool training time is 0.4x" },
            { "", "", "pilgrimage income 1.2x", "charms grant 1.3x HP", "holy ground - charmed soldiers are born with 1.5x HP" }
        };
        private static readonly string[,] StrengthJapanese =
        {
            { "", "", "", "", "" },
            { "", "", "農耕：農場の作業速度 1.2 倍", "糧食：回復 HP 15", "人口上限 +20" },
            { "", "", "冶金：溶鉱炉の作業速度 1.25 倍", "鍛えた歩兵の攻撃 +1", "攻城部品の所要時間 0.7 倍" },
            { "", "", "林業：木こり場の作業速度 1.25 倍", "弓兵の射程 +1", "建物の木材コスト 0.8 倍" },
            { "", "", "石工：壁の HP 1.2 倍", "塔の攻撃 +2", "コア最大 HP 1.3 倍" },
            { "", "", "隊商：交易品の価値 +5", "市場の返却 +10%", "隊商報酬 1.5 倍" },
            { "", "", "騎兵：騎兵訓練時間 0.8 倍", "騎兵の速度 +10%", "重騎兵の HP +15%" },
            { "", "", "橋梁：橋の作業時間 0.7 倍", "破城槌の対建物攻撃 +20%", "建物作業時間 0.8 倍" },
            { "", "", "学術：研究時間 0.85 倍", "学術品の割引 0.6 倍", "時代4研究コスト半減" },
            { "", "", "教団：改宗時間 0.8 倍", "香の回復 30 HP", "僧侶の HP 1.5 倍" },
            { "", "", "漁業：漁の速度 1.2 倍", "保存食 +1", "食料運搬 +2" },
            { "", "", "山岳：採掘速度 1.2 倍", "傭兵の宝石コスト 8", "傭兵の攻撃 +2" },
            { "", "", "関所：通行料 1.5 倍", "関所の HP 1.3 倍", "修理キットの回復 60 HP" },
            { "", "", "都市：町の住民訓練時間 0.8 倍", "町の上限 +1", "道具訓練時間 0.4 倍" },
            { "", "", "聖地：巡礼収入 1.2 倍", "護符の HP 1.3 倍", "神域 ― 護符で生まれる兵の HP が 1.5 倍" }
        };

        public static string Age(EconomyView economy)
        {
            if (economy == null || !economy.Ages) return "";
            string stage = UiText.T("Age ", "時代 ") + (economy.AdvanceRemaining > 0 ? economy.Age + 1 : economy.Age)
                + UiText.T("/", "/") + (economy.AgeFour ? "4" : "3") + "  ";
            if (economy.AdvanceRemaining > 0)
                return stage + UiText.T("Advancing to ", "進行中：") + AgeName(economy.AdvancingTo,
                    economy.Civ == CivKind.Primitive ? 1 : economy.Age + 1) + " " + Seconds(economy.AdvanceRemaining) + "  |  ";
            return stage + AgeName(economy.Civ, economy.Age) + "  |  ";
        }

        public static string AgeStage(EconomyView economy)
        {
            if (economy == null || !economy.Ages) return "";
            int age = economy.AdvanceRemaining > 0 ? economy.Age + 1 : economy.Age;
            string numeral = age <= 1 ? "I" : age == 2 ? "II" : age == 3 ? "III" : "IV";
            return UiText.T("AGE ", "時代 ") + numeral;
        }

        public static string Population(int population, int populationCap, int idle)
            => UiText.T("Pop ", "人口 ") + FormatCount(population) + "/" + FormatCount(populationCap)
                + UiText.T("  Idle ", "  待機 ") + FormatCount(idle);

        public static string FormatCount(int value) => value.ToString(CultureInfo.InvariantCulture);
        public static string Seconds(long ticks) => (ticks < 0 ? 0 : ticks / 20f).ToString("0.0", CultureInfo.InvariantCulture) + "s";

        public static string CivName(CivKind civ)
        {
            int index = CivIndex(civ);
            return UiText.T(CivEnglish[index] + " civilization", CivJapanese[index] + "の文明");
        }

        public static string AgeName(CivKind civ, int age)
        {
            if (age <= 0) return UiText.T("primitive age", "原始の時代");
            int index = CivIndex(civ), clampedAge = age > 4 ? 4 : age;
            return CivName(civ) + UiText.T(" - " + AgeEnglish[index, clampedAge] + " age", "・" + AgeJapanese[index, clampedAge] + "の時代");
        }

        public static string AgeAdvanceMessage(CivKind civ, int age) => AgeAdvanceMessage(civ, age, false);

        public static string AgeAdvanceMessage(CivKind civ, int age, bool ageStrengths)
        {
            int clampedAge = age < 0 ? 0 : age > 4 ? 4 : age, civIndex = CivIndex(civ);
            string strengthEn = ageStrengths ? " Strength: " + StrengthEnglish[civIndex, clampedAge] : "";
            string strengthJa = ageStrengths ? " 強み：" + StrengthJapanese[civIndex, clampedAge] : "";
            return UiText.T("Age " + clampedAge + " reached: " + AgeName(civ, clampedAge) + " - " + IntentEnglish[clampedAge] + ". " + UnlockEnglish[clampedAge] + strengthEn,
                "時代" + clampedAge + "に進みました：" + AgeName(civ, clampedAge) + " ― " + IntentJapanese[clampedAge] + "。" + UnlockJapanese[clampedAge] + strengthJa);
        }

        public static string AgeStrength(CivKind civ, int age)
        {
            int index = CivIndex(civ), clampedAge = age < 0 ? 0 : age > 4 ? 4 : age;
            return UiText.T(StrengthEnglish[index, clampedAge], StrengthJapanese[index, clampedAge]);
        }

        private static int CivIndex(CivKind civ)
        {
            int index = (int)civ;
            return index >= 0 && index < CivEnglish.Length ? index : 0;
        }
    }
}
