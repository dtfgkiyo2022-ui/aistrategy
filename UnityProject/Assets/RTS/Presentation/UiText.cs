using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>
    /// The on-screen language, English or Japanese, until real localisation (string tables) comes later. Every panel asks
    /// for a line in both languages at the place it draws it, so switching takes effect on the next frame. Plain C#: the
    /// host loads and saves the choice.
    /// </summary>
    public static class UiText
    {
        public static string AiReasonToggle(bool enabled)
            => T(enabled ? "AI reasons: on" : "AI reasons: off", enabled ? "お任せの理由：表示" : "お任せの理由：非表示");

        public static string AiReasonLine(AiReason reason, int value)
            => T("Auto: " + reason + " (" + value + ")", "お任せ：" + AiReasonJapanese(reason, value));

        public static string AiStoppedLine(AiReason reason, int value)
            => T("Stopped: " + reason + " (" + value + ")", "止まっている：" + AiReasonJapanese(reason, value));

        /// <summary>38.25 その4：加工の建物で 1 回分に足りない材料と、それをどう届けているか。</summary>
        public static string MissingMaterialLine(ResourceKind material, MaterialSupplyView supply)
        {
            string en, ja;
            switch (supply)
            {
                case MaterialSupplyView.Line: en = "coming by its line's belt"; ja = "線のベルトで届く"; break;
                case MaterialSupplyView.Belt: en = "coming by a supply belt"; ja = "供給のベルトで運んでいる"; break;
                case MaterialSupplyView.Hauler: en = "a villager is carrying it"; ja = "運び役が向かっている"; break;
                case MaterialSupplyView.Waiting: en = "the supply belt could not be laid; trying again later"; ja = "供給のベルトが引けず、あとでもう一度試す"; break;
                default: en = "nothing brings it"; ja = "運ぶ手段なし"; break;
            }
            return T("Missing material: " + material + " (" + en + ")", "足りない材料：" + AiResourceName(material) + "（" + ja + "）");
        }

        /// <summary>38.25 その4：取り出し口（コア・町）から加工の建物へ材料を運ぶ供給のベルト。</summary>
        public static string SupplyBeltLine(ResourceKind material, uint buildingId)
            => T("Auto: supply belt carrying " + material + " to building #" + buildingId,
                "お任せ：" + AiResourceName(material) + "を建物（番号 " + buildingId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）へ運ぶ供給のベルト");

        public static string AiReasonJapanese(AiReason reason, int value)
        {
            string n = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            switch (reason)
            {
                case AiReason.PopulationCap: return "人口上限まであと" + n;
                case AiReason.FoodIncome: return "食料を増やす（在庫" + n + "）";
                case AiReason.BarracksTarget: return "兵舎を" + n + "棟まで増やす";
                case AiReason.FarResource: return "採り場が遠い（" + n + "マス）";
                case AiReason.TownExpand: return "拠点を広げる（時代" + n + "）";
                case AiReason.MaterialFor: return AiResourceName((ResourceKind)value) + "を作るため";
                case AiReason.MetalLine: return "金属の線" + n + "本目";
                case AiReason.MaterialForAssemblers: return "組立所" + n + "棟分の材料";
                case AiReason.AssemblerRecipe: return AiRecipeName((RecipeKind)value) + "を作る組立所";
                case AiReason.ResourceBelt: return AiResourceName((ResourceKind)value) + "の採り場から";
                case AiReason.ProcessingBelt: return AiLineName((ProcessingLineKind)value) + "の線";
                case AiReason.ReservationWait: return AiResourceName((ResourceKind)value) + "がそろったら建てる";
                case AiReason.Unspecified: return "理由なしで置いた";
                case AiReason.Stopped: return "現在停止中";
                case AiReason.ByproductStall: return "鉱さいがたまって止まっている";
                case AiReason.Defense: return value > 0 ? "守りを固める（" + n + "基まで）" : "守りを固める";
                case AiReason.NewUnitKind: return "新しい兵種を出す";
                case AiReason.Research: return "研究のため";
                case AiReason.RiverCrossing: return "川を渡る（" + n + "マス）";
                default: return "理由なし";
            }
        }

        private static string AiResourceName(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Food: return "食料";
                case ResourceKind.Wood: return "木材";
                case ResourceKind.Ore: return "鉱石";
                case ResourceKind.Metal: return "金属";
                case ResourceKind.Stone: return "石";
                case ResourceKind.Gems: return "宝石";
                case ResourceKind.Gold: return "金";
                case ResourceKind.Charcoal: return "木炭";
                case ResourceKind.Steel: return "鋼";
                case ResourceKind.BowGear: return "弓具";
                case ResourceKind.Rations: return "糧食";
                case ResourceKind.Bricks: return "煉瓦";
                case ResourceKind.Tack: return "馬具";
                case ResourceKind.Planks: return "板材";
                case ResourceKind.Books: return "書物";
                case ResourceKind.Preserves: return "保存食";
                case ResourceKind.TradeGoods: return "交易品";
                case ResourceKind.Incense: return "香";
                case ResourceKind.Charms: return "護符";
                case ResourceKind.RepairKits: return "補修材";
                case ResourceKind.Tools: return "工具";
                case ResourceKind.CutStone: return "切り石";
                case ResourceKind.Frames: return "建材枠";
                case ResourceKind.ArmsKit: return "武具";
                case ResourceKind.Scholarly: return "学術品";
                case ResourceKind.FineArmsKit: return "良い武具";
                case ResourceKind.SiegeParts: return "攻城部品";
                case ResourceKind.Mechanism: return "精密部品";
                case ResourceKind.SiegeEngine: return "攻城機関";
                case ResourceKind.EliteKit: return "精鋭キット";
                case ResourceKind.StoneTools: return "石器";
                case ResourceKind.Jerky: return "干し肉";
                case ResourceKind.Fittings: return "金具";
                case ResourceKind.Rope: return "縄";
                case ResourceKind.Gears: return "歯車";
                case ResourceKind.Precision: return "精密部品";
                default: return "資源";
            }
        }

        private static string AiRecipeName(RecipeKind recipe)
        {
            switch (recipe)
            {
                case RecipeKind.Frames: return "建材枠";
                case RecipeKind.ArmsKit: return "武具";
                case RecipeKind.Scholarly: return "学術品";
                case RecipeKind.SiegeParts: return "攻城部品";
                case RecipeKind.Mechanism: return "精密部品";
                case RecipeKind.SiegeEngine: return "攻城機関";
                case RecipeKind.EliteKit: return "精鋭キット";
                case RecipeKind.StoneTools: return "石器";
                case RecipeKind.Jerky: return "干し肉";
                case RecipeKind.Precision: return "精密部品";
                default: return "品物";
            }
        }

        private static string AiLineName(ProcessingLineKind kind)
        {
            switch (kind)
            {
                case ProcessingLineKind.CoreMetal: return "金属";
                case ProcessingLineKind.Steel: return "鋼";
                case ProcessingLineKind.CoreWood: return "木材";
                case ProcessingLineKind.BowGear: return "弓具";
                case ProcessingLineKind.Rations: return "糧食";
                case ProcessingLineKind.Bricks: return "煉瓦";
                case ProcessingLineKind.Tack: return "馬具";
                case ProcessingLineKind.Planks: return "板材";
                case ProcessingLineKind.Books: return "書物";
                case ProcessingLineKind.Preserves: return "保存食";
                case ProcessingLineKind.Jewels: return "宝飾";
                case ProcessingLineKind.TradeGoods: return "交易品";
                case ProcessingLineKind.Incense: return "香";
                case ProcessingLineKind.Charms: return "護符";
                case ProcessingLineKind.RepairKits: return "補修材";
                case ProcessingLineKind.Tools: return "工具";
                case ProcessingLineKind.CutStone: return "切り石";
                case ProcessingLineKind.Frames: return "建材枠";
                case ProcessingLineKind.Assembler: return "組立所";
                case ProcessingLineKind.PrimitiveCraft: return "原始の加工";
                case ProcessingLineKind.Fittings: return "金具";
                case ProcessingLineKind.Rope: return "縄";
                case ProcessingLineKind.Gears: return "歯車";
                default: return "加工";
            }
        }
        public static bool Japanese;

        public static string T(string english, string japanese) { return Japanese ? japanese : english; }

        /// <summary>11-01: the mark over a fight - what we brought down and what we lost there.</summary>
        public static string CombatMark(int enemyLost, int ownLost)
        {
            return Japanese
                ? "交戦中　<color=#ffe98c>倒した " + enemyLost + "</color>・<color=#ff6a4a>失った " + ownLost + "</color>"
                : "Fighting  <color=#ffe98c>downed " + enemyLost + "</color> / <color=#ff6a4a>lost " + ownLost + "</color>";
        }

        /// <summary>11-02: the fight's mark with the buildings and belt cells each side lost; none shown when both are 0.</summary>
        public static string CombatMark(int enemyLost, int ownLost, int enemyStructuresLost, int ownStructuresLost)
        {
            string mark = CombatMark(enemyLost, ownLost);
            if (enemyStructuresLost == 0 && ownStructuresLost == 0) return mark;
            return mark + (Japanese
                ? "\n<color=#ffe98c>壊した建物 " + enemyStructuresLost + "</color>・<color=#ff6a4a>壊された " + ownStructuresLost + "</color>"
                : "\n<color=#ffe98c>razed" + enemyStructuresLost + "</color> / <color=#ff6a4a>lost buildings " + ownStructuresLost + "</color>");
        }

        public static string DropSiteLabel(int count, int max, int woodCost)
        {
            string countText = max > 0 ? count + "/" + max : "";
            return Japanese ? "集積所 " + countText + "（木" + woodCost + "）"
                : "Drop-off " + countText + " (" + woodCost + " wood)";
        }

        public static string StorageDescription()
            => T("Villagers can unload here; the exit belt sends it to the core or, when enabled, to a processing building",
                "村人が荷を下ろせる（出口からベルトで送る。旗が有効なら加工の建物へも送る）");

        public static string CraftTree4ProductDescription()
            => T("Age 4: Siege engine = 2 siege parts + 1 mechanism; Elite kit = 2 arms kits + 1 mechanism + 5 metal.",
                "時代4：攻城機関は攻城部品2＋精密部品1。精鋭キットは武具キット2＋精密部品1＋金属5。");

        public static string CannonDescription()
            => T("Cannon: SiegeWorkshop unit; consumes 1 siege engine, 100 food and 100 wood. Range 8; deals double ram building damage.",
                "大砲：攻城工房で生産。攻城機関1・食料100・木材100を使う。射程8、建物には破城槌の2倍の攻撃。");

        public static string PrimitiveCraftHutDescription()
            => T("Hut: primitive workbench. Stone tools use 1 stone + 1 wood; jerky uses 3 food and improves soldier HP.",
                "作業小屋：原始時代の作業台。石器は石1＋木1、干し肉は食料3で作り、兵のHPを高める。");

        public static string PrimitiveCraftGoodsLabel()
            => T("Stone tools / Jerky", "石器／干し肉");
    }
}
