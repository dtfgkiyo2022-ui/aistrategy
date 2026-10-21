using System;
using System.Globalization;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>One display-only table for selected building descriptions.</summary>
    public static class SelectionInfoText
    {
        public static string ObjectiveTitle(KnownObjective objective, uint viewerFactionId)
        {
            if (objective.Kind == GoalKind.Core)
                return objective.OwnerFactionId == viewerFactionId
                    ? UiText.T("Your Core", "自分のコア")
                    : UiText.T("Enemy Core", "敵のコア");

            if (objective.Kind != GoalKind.Outpost)
                return "";

            if (!objective.IsOwnerKnown || objective.OwnerFactionId == 0)
                return UiText.T("Outpost (neutral)", "前哨地（中立）");
            return objective.OwnerFactionId == viewerFactionId
                ? UiText.T("Outpost (own)", "前哨地（自分）")
                : UiText.T("Outpost (enemy)", "前哨地（敵）");
        }

        public static string ObjectiveDetail(KnownObjective objective, uint viewerFactionId, long currentTick,
            int fallbackCoreMaxHp = 0)
        {
            if (objective.Kind == GoalKind.Core)
            {
                if (!objective.IsHpKnown)
                {
                    long ageTicks = Math.Max(0L, currentTick - objective.LastSeenTick);
                    long ageSeconds = ageTicks / 20L;
                    return UiText.T("HP: unknown (last seen " + ageSeconds.ToString(CultureInfo.InvariantCulture) + "s ago)",
                        "HP：不明（最後に見たのは " + ageSeconds.ToString(CultureInfo.InvariantCulture) + "秒前）");
                }

                int maxHp = objective.MaxHp > 0 ? objective.MaxHp : fallbackCoreMaxHp;
                if (maxHp <= 0)
                    return UiText.T("HP " + objective.Hp.ToString("N0", CultureInfo.InvariantCulture),
                        "HP " + objective.Hp.ToString("N0", CultureInfo.InvariantCulture));
                int hp = Math.Max(0, Math.Min(objective.Hp, maxHp));
                int percent = (int)(((long)hp * 100L + maxHp / 2L) / maxHp);
                return UiText.T(
                    "HP " + hp.ToString("N0", CultureInfo.InvariantCulture) + " / " + maxHp.ToString("N0", CultureInfo.InvariantCulture) + " (" + percent + "%)",
                    "HP " + hp.ToString("N0", CultureInfo.InvariantCulture) + " / " + maxHp.ToString("N0", CultureInfo.InvariantCulture) + "（" + percent + "%）");
            }

            if (objective.Kind == GoalKind.Outpost && objective.CapturingFactionId != 0 && objective.CaptureDurationTicks > 0)
            {
                int percent = (int)Math.Max(0, Math.Min(100,
                    ((long)Math.Max(0, objective.CaptureTicks) * 100L + objective.CaptureDurationTicks / 2L) / objective.CaptureDurationTicks));
                string faction = objective.CapturingFactionId == viewerFactionId
                    ? UiText.T("own", "自分")
                    : UiText.T("enemy", "敵");
                return UiText.T("Capturing: " + faction + " " + percent + "%", "占領中：" + faction + " " + percent + "%");
            }

            return "";
        }

        public static string BuildingReservationStatus(BuildingView building, EconomyView economy)
        {
            if (!building.Reserved) return "";
            int wood = economy == null ? 0 : economy.BarracksWoodCost;
            int stone = 0;
            if (building.Kind == BuildingKind.Tower) { wood = economy.TowerWoodCost; stone = economy.TowerStoneCost; }
            else if (building.Kind == BuildingKind.Castle) { wood = economy.CastleWoodCost; stone = economy.CastleStoneCost; }
            else if (building.Kind == BuildingKind.Drill) { wood = economy.DrillWoodCost; stone = economy.DrillStoneCost; }
            int missingWood = System.Math.Max(0, wood - (economy == null ? 0 : economy.Wood));
            int missingStone = System.Math.Max(0, stone - (economy == null ? 0 : economy.Stone));
            return UiText.T("Reserved: waiting for resources (", "予約中：資源待ち（") +
                UiText.T("wood ", "木材 ") + missingWood + UiText.T(", stone ", "、石 ") + missingStone +
                UiText.T(", queue ", "、順番 ") + building.ReservationOrder + UiText.T(")", "）");
        }

        public static string ActiveAgeStrengths(EconomyView economy)
            => economy == null ? "" : ActiveAgeStrengths(economy.Civ, economy.Age, economy.AgeStrengths);

        public static string ActiveAgeStrengths(CivKind civ, int age, bool enabled)
        {
            if (!enabled || age < 2) return "";
            string title = UiText.T("Active age strengths", "発動中の時代強み");
            string result = title;
            int last = Math.Min(4, age);
            for (int strengthAge = 2; strengthAge <= last; strengthAge++)
                result += "\n" + UiText.T("Age " + strengthAge + ": ", "時代" + strengthAge + "：")
                    + TopBarDisplayText.AgeStrength(civ, strengthAge);
            return result;
        }
        public static string BeltComponentDescription(BeltComponentKind kind)
        {
            return kind == BeltComponentKind.Inserter
                ? UiText.T("Takes a selected resource from behind and feeds a belt, building, storage, or the core.", "選択資源を後ろから取り、ベルト・建物・倉庫・コアへ投入する。")
                : "";
        }

        public static string BuildingDescription(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Barracks: return UiText.T("Trains infantry and scouts.", "歩兵と斥候を訓練する。");
                case BuildingKind.Mine: return UiText.T("Extracts ore from a nearby node.", "近くの鉱石ノードから鉱石を採る。");
                case BuildingKind.Smelter: return UiText.T("Smelts ore into metal.", "鉱石を金属に精錬する。");
                case BuildingKind.Farm: return UiText.T("Produces food over time.", "時間をかけて食料を作る。");
                case BuildingKind.House: return UiText.T("Raises the population cap.", "人口上限を増やす。");
                case BuildingKind.DropSite: return UiText.T("Villagers unload gathered resources here.", "村人が集めた資源をここへ下ろす。");
                case BuildingKind.Wall: return UiText.T("Blocks movement and protects the settlement.", "移動を止め、拠点を守る。");
                case BuildingKind.Tower: return UiText.T("Defends the area with ranged attacks.", "遠隔攻撃で周囲を守る。");
                case BuildingKind.Blacksmith: return UiText.T("Researches military technology.", "軍事技術を研究する。");
                case BuildingKind.Market: return UiText.T("Trades resources and supports trade routes.", "資源を交換し、交易路を支える。");
                case BuildingKind.SiegeWorkshop: return UiText.T("Builds siege engines.", "攻城兵器を作る。");
                case BuildingKind.ArcheryRange: return UiText.T("Trains archers.", "弓兵を訓練する。");
                case BuildingKind.Stable: return UiText.T("Trains cavalry.", "騎兵を訓練する。");
                case BuildingKind.Castle: return UiText.T("A strong late-age defence and military centre.", "後の時代の強力な防衛・軍事拠点。");
                case BuildingKind.CharcoalKiln: return UiText.T("Wood → charcoal.", "木材 → 木炭。");
                case BuildingKind.Steelworks: return UiText.T("Metal + charcoal → steel.", "金属＋木炭 → 鋼。");
                case BuildingKind.LumberCamp: return UiText.T("Gathers wood from a nearby node.", "近くの木材ノードから木材を採る。");
                case BuildingKind.Fletcher: return UiText.T("Wood + food → bow gear.", "木材＋食料 → 弓具。");
                case BuildingKind.Quarry: return UiText.T("Gathers stone from a nearby node.", "近くの石ノードから石を採る。");
                case BuildingKind.Caravanserai: return UiText.T("Moves villagers on a linked caravan route.", "つながった隊商路で村人を運ぶ。");
                case BuildingKind.EngineerCamp: return UiText.T("Supports engineering and construction work.", "工兵と建設作業を支える。");
                case BuildingKind.Bridge: return UiText.T("Crosses a river and carries a belt route.", "川を渡り、ベルトの経路を通す。");
                case BuildingKind.Academy: return UiText.T("Provides civilisation-specific learning.", "文明固有の学術を担う。");
                case BuildingKind.Monastery: return UiText.T("Trains monks and provides religious effects.", "僧侶を訓練し、宗教効果を与える。");
                case BuildingKind.Harbor: return UiText.T("Supports fishing and water-side economy.", "漁業と水辺の内政を支える。");
                case BuildingKind.MineShaft: return UiText.T("Extends access to a deep resource site.", "深い資源地への採取を広げる。");
                case BuildingKind.Tollgate: return UiText.T("Controls a route and collects its fee.", "経路を管理し、通行料を得る。");
                case BuildingKind.GrandHouse: return UiText.T("A larger home for a growing civilisation.", "発展した文明の大きな住居。");
                case BuildingKind.Shrine: return UiText.T("Supports sanctuary effects near an outpost.", "拠点の近くで聖域効果を支える。");
                case BuildingKind.Town: return UiText.T("A regional centre that supports nearby buildings.", "周辺の建物を支える地域の中心。");
                case BuildingKind.Storage: return UiText.T("Stores one resource type up to the economy capacity; a connected exit belt unloads villagers toward the core or a processing building.", "資源を1種類まで経済の容量だけためる。出口のベルトがコアか加工の建物へつながると、村人がここへ荷を下ろす。");
                case BuildingKind.Mill: return UiText.T("Processes food into rations.", "食料を糧食に加工する。");
                case BuildingKind.BrickKiln: return UiText.T("Stone + charcoal → bricks.", "石＋木炭 → 煉瓦。");
                case BuildingKind.Saddlery: return UiText.T("Food + wood → tack.", "食料＋木材 → 馬具。");
                case BuildingKind.Sawmill: return UiText.T("Wood → planks; planks feed joinery and assembler lines.", "木材2 → 板1。板材は木工所（建材）・組立所（武具・学術品）の材料。");
                case BuildingKind.Scriptorium: return UiText.T("Gold + wood → books for research.", "金＋木材 → 研究用の本。");
                case BuildingKind.DryingRack: return UiText.T("Food → preserves.", "食料 → 保存食。");
                case BuildingKind.Lapidary: return UiText.T("Stone + ore → gems.", "石＋鉱石 → 宝石。");
                case BuildingKind.Weaver: return UiText.T("Wood → trade goods.", "木材 → 交易品。");
                case BuildingKind.IncenseHouse: return UiText.T("Wood + food → incense.", "木材＋食料 → 香。");
                case BuildingKind.CharmWorkshop: return UiText.T("Wood → charms.", "木材 → 護符。");
                case BuildingKind.RepairYard: return UiText.T("Wood → repair kits.", "木材 → 補修材。");
                case BuildingKind.ToolShop: return UiText.T("Wood → tools; each tool makes villagers eat 0.7× and work in half the time.", "木材2 → 工具1（都市の文明）。工具を1つ使うと、村人の食料が0.7倍・作る時間が半分。");
                case BuildingKind.Stonecutter: return UiText.T("Stone → cut stone.", "石2 → 切石1。切石は木工所（建材）・組立所（精密部品）の材料。");
                case BuildingKind.Joinery: return UiText.T("Planks + cut stone → frames.", "板2＋切石1 → 建材1。建物を建てるとき建材1で木材0.7倍・建てる手間0.7倍。");
                case BuildingKind.Assembler: return UiText.T("Makes the chosen recipe: frames, arms kits, scholarly goods (age 2), siege parts or mechanisms (age 3).", "選んだレシピを作る：建材・武具・学術品（時代2）、攻城部品・精密部品（時代3）。");
                case BuildingKind.Drill: return UiText.T("Digs the wood, stone, ore or gold point under it without villagers and sends it out of its exit.", "足元の資源の点（木・石・鉱石・金）を村人なしで掘り、出口からベルトへ出す。");
                default: return "";
            }
        }

        public static string UnitStrength(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Infantry: return UiText.T("strong against cavalry; weak against archers", "騎兵に強い・弓兵に弱い");
                case UnitKind.Archer: return UiText.T("strong against infantry; weak against cavalry", "歩兵に強い・騎兵に弱い");
                case UnitKind.Cavalry: return UiText.T("strong against archers; weak against infantry", "弓兵に強い・歩兵に弱い");
                default: return UiText.T("no basic counter shown", "基本相性なし");
            }
        }
    }
}
