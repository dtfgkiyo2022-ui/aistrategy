using System;
using System.Collections.Generic;
using System.Globalization;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    public sealed partial class EconomyPanel
    {
        /// <summary>One row of the economy panel, shared by the IMGUI meaning and the Toolkit view.</summary>
        public sealed class EconomyAction
        {
            public string Id { get; private set; }
            public string Label { get; private set; }
            public string Reason { get; private set; }
            public bool Enabled { get; private set; }
            public bool Selected { get; private set; }
            public bool Message { get; private set; }

            public EconomyAction(string id, string label, bool enabled, string reason, bool selected, bool message)
            {
                Id = id;
                Label = label;
                Enabled = enabled;
                Reason = reason ?? "";
                Selected = selected;
                Message = message;
            }
        }

        /// <summary>The tab currently selected in either the legacy or the Toolkit panel.</summary>
        public Tab SelectedTab { get { return tab; } }

        /// <summary>The newest notice, or an empty string when there is no notice yet.</summary>
        public string LatestNotice { get { return notes.Count == 0 ? "" : notes[notes.Count - 1]; } }

        /// <summary>The placement explanation shown below the action rows.</summary>
        public string PlacementHint
        {
            get
            {
                return mode == Mode.None ? ""
                    : mode == Mode.Wall ? UiText.T("Drag near your base; a wall never shuts the way to the enemy.", "自陣の近くをドラッグ。敵への道を完全には塞げない")
                    : mode == Mode.Belt || mode == Mode.FastBelt ? UiText.T("Drag an L-shaped belt; Shift changes the bend order.", "ドラッグでL字ベルト。Shiftで曲がる順番を変更")
                    : mode == Mode.Sorter ? UiText.T("T changes the resource sent left.", "Tで左へ送る資源を変更")
                    : mode == Mode.RemoveBelt ? UiText.T("Click a belt of yours.", "外す自分のベルトをクリック")
                    : mode == Mode.RemoveArea ? UiText.T("Drag a rectangle to remove your belts and buildings.", "四角くドラッグして自軍のベルトと建物を撤去")
                    : mode == Mode.BlueprintSave ? UiText.T("Drag a rectangle to save the blueprint.", "四角くドラッグして設計図を保存")
                    : mode == Mode.BlueprintPaste ? UiText.T("R rotates the blueprint; click to place it.", "Rで設計図を回転、クリックで配置")
                    : mode == Mode.RequestLine ? UiText.T("Click a region or resource point for the line.", "ラインを頼む区域または資源地点をクリック")
                    : UiText.T("Click the ground (Esc cancels). R turns the output: ", "地面をクリック（Escで取消）。R で出口の向き：") + FacingName(facing);
            }
        }

        /// <summary>Changes tabs and cancels an unfinished ground placement.</summary>
        public void SelectToolkitTab(Tab next)
        {
            if (tab == next) return;
            tab = next;
            SetMode(Mode.None);
        }

        /// <summary>
        /// Builds the current row descriptions from the same economy snapshot and conditions used by the IMGUI panel.
        /// The returned objects are display data; they do not mutate the simulation.
        /// </summary>
        public IReadOnlyList<EconomyAction> GetToolkitActions()
        {
            var result = new List<EconomyAction>();
            var economy = Economy();
            if (economy == null) return result;
            switch (tab)
            {
                case Tab.Build: AddBuildActions(result, economy); break;
                case Tab.Make: AddMakeActions(result, economy); break;
                case Tab.Research: AddResearchActions(result, economy); break;
                case Tab.Policy: AddPolicyActions(result, economy); break;
                default: AddBlueprintActions(result); break;
            }
            return result;
        }

        /// <summary>Runs one action from <see cref="GetToolkitActions"/> through the existing economy command path.</summary>
        public void ExecuteToolkitAction(string actionId)
        {
            if (port == null || string.IsNullOrEmpty(actionId)) return;
            var economy = Economy();
            if (economy == null) return;

            if (actionId.StartsWith("build:", StringComparison.Ordinal))
            {
                var buildMode = ParseMode(actionId.Substring(6));
                if (buildMode != Mode.None) SetMode(buildMode);
                return;
            }
            if (actionId.StartsWith("line:", StringComparison.Ordinal))
            {
                if (Enum.TryParse(actionId.Substring(5), true, out ProcessingLineKind line))
                {
                    requestedLine = line;
                    SetMode(Mode.RequestLine);
                }
                return;
            }
            if (actionId == "blueprint:save") { BeginBlueprintSave(); return; }
            if (actionId == "make:villager") { Send(EconomyCommand.Train(faction, ++sequence, 0, UnitKind.Villager), UiText.T("Villager requested", "村人を依頼しました")); return; }
            if (actionId == "make:infantry") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Infantry, UiText.T("Infantry requested", "歩兵を依頼しました")); return; }
            if (actionId == "make:infantry-cancel") { CancelFromBuilding(economy, BuildingKind.Barracks, UiText.T("Last infantry cancelled", "最後の歩兵を取り消しました")); return; }
            if (actionId == "make:monk") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Monk, UiText.T("Monk requested", "僧侶を依頼しました")); return; }
            if (actionId == "make:scout") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Scout, UiText.T("Scout requested", "斥候を依頼しました")); return; }
            if (actionId == "make:archer-barracks") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Archer, UiText.T("Archer requested", "弓兵を依頼しました")); return; }
            if (actionId == "make:cavalry-barracks") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Cavalry, UiText.T("Cavalry requested", "騎兵を依頼しました")); return; }
            if (actionId == "make:ram") { TrainFromBuilding(economy, BuildingKind.SiegeWorkshop, UnitKind.Ram, UiText.T("Ram requested", "破城槌を依頼しました")); return; }
            if (actionId == "make:archer-range") { TrainFromBuilding(economy, BuildingKind.ArcheryRange, UnitKind.Archer, UiText.T("Archer requested", "弓兵を依頼しました")); return; }
            if (actionId == "make:cavalry-stable") { TrainFromBuilding(economy, BuildingKind.Stable, UnitKind.Cavalry, UiText.T("Cavalry requested", "騎兵を依頼しました")); return; }
            if (actionId.StartsWith("make:castle:", StringComparison.Ordinal))
            {
                var castleUnit = ParseUnit(actionId.Substring(12));
                TrainFromBuilding(economy, BuildingKind.Castle, castleUnit, UiText.T(castleUnit.ToString() + " requested", castleUnit == UnitKind.Infantry ? "歩兵を依頼しました" : castleUnit == UnitKind.Archer ? "弓兵を依頼しました" : castleUnit == UnitKind.Cavalry ? "騎兵を依頼しました" : "傭兵を依頼しました"));
                return;
            }
            if (actionId == "make:trade-route") { SendIdleToTrade(economy); return; }
            if (actionId.StartsWith("make:trade:", StringComparison.Ordinal))
            {
                var trade = actionId.Substring(11).Split('-');
                if (trade.Length == 2) Send(EconomyCommand.Trade(faction, ++sequence, ParseResource(trade[0]), ParseResource(trade[1])), UiText.T("Trade requested", "交換を依頼しました"));
                return;
            }
            if (actionId == "make:wood-gems") { Send(EconomyCommand.Trade(faction, ++sequence, ResourceKind.Wood, ResourceKind.Gems), UiText.T("Wood->Gems traded", "木材→宝石 を交換しました")); return; }
            if (actionId.StartsWith("make:idle:", StringComparison.Ordinal)) { SendIdle(economy, ParseResource(actionId.Substring(10))); return; }
            if (actionId == "make:idle-trade") { SendIdleToTrade(economy); return; }
            if (actionId == "make:carry")
            {
                bool farming = economy.Ages && economy.Civ == CivKind.Agrarian;
                var sourceBuilding = OwnBuilding(economy, farming ? BuildingKind.Farm : BuildingKind.Mine);
                if (sourceBuilding.HasValue) SendIdleTo(economy, EconomyTargetKind.Building, sourceBuilding.Value.Id, farming ? UiText.T("the farm", "農場") + UiText.T(" by hand", "から手で運ぶ") : UiText.T("the mine", "採掘場") + UiText.T(" by hand", "から手で運ぶ"));
                return;
            }
            if (actionId.StartsWith("make:advance:", StringComparison.Ordinal)) { SendAdvanceFromPrimitive(economy, (CivKind)ParseInt(actionId.Substring(13))); return; }
            if (actionId == "make:advance-age") { Send(EconomyCommand.Advance(faction, ++sequence, economy.Civ), UiText.T("Advance requested", "時代を進めるよう依頼しました")); return; }
            if (actionId == "research:build-blacksmith") { SetMode(Mode.Blacksmith); return; }
            if (actionId.StartsWith("research:", StringComparison.Ordinal))
            {
                var smith = OwnBuilding(economy, BuildingKind.Blacksmith);
                if (smith.HasValue) Send(EconomyCommand.Research(faction, ++sequence, smith.Value.Id, (TechKind)ParseInt(actionId.Substring(9))), UiText.T("Research requested", "研究を依頼しました"));
                return;
            }
            if (actionId == "policy:auto") { Send(EconomyCommand.Auto(faction, ++sequence, !economy.AutoEconomy), economy.AutoEconomy ? UiText.T("Auto economy off: villagers wait for you", "お任せ内政を切りました：村人は指示を待ちます") : UiText.T("Auto economy on", "お任せ内政を入れました")); return; }
            if (actionId.StartsWith("policy:set:", StringComparison.Ordinal)) { Send(EconomyCommand.SetPolicy(faction, ++sequence, (EconomyPolicy)ParseInt(actionId.Substring(11))), UiText.T("Economy policy changed", "内政の方針を変更しました")); return; }
            if (actionId == "policy:return") { Send(EconomyCommand.ReturnToAuto(faction, ++sequence), UiText.T("Everything you held goes back to the auto economy", "触った物をすべてお任せの内政に戻しました")); }
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : 0;
        }

        private static Mode ParseMode(string value)
        {
            switch (value)
            {
                case "barracks": return Mode.Barracks; case "house": return Mode.House; case "drop-site": return Mode.DropSite;
                case "tower": return Mode.Tower; case "wall": return Mode.Wall; case "market": return Mode.Market;
                case "siege": return Mode.SiegeWorkshop; case "range": return Mode.ArcheryRange; case "stable": return Mode.Stable;
                case "castle": return Mode.Castle; case "mine": return Mode.Mine; case "smelter": return Mode.Smelter;
                case "farm": return Mode.Farm; case "belt": return Mode.Belt; case "fast-belt": return Mode.FastBelt;
                case "splitter": return Mode.Splitter; case "sorter": return Mode.Sorter; case "underground": return Mode.Underground; case "storage": return Mode.Storage;
                case "remove-belt": return Mode.RemoveBelt;
                case "remove-area": return Mode.RemoveArea;
                case "blacksmith": return Mode.Blacksmith; default: return Mode.None;
            }
        }

        private static ProcessingLineKind ParseLine(string value)
        {
            ProcessingLineKind line;
            return Enum.TryParse(value, true, out line) ? line : ProcessingLineKind.CoreMetal;
        }

        private void AddBlueprintActions(List<EconomyAction> rows)
        {
            Add(rows, "blueprint:save", UiText.T("Save blueprint: drag rectangle", "設計図を保存：四角くドラッグ"));
            Add(rows, "blueprint:help", UiText.T("Choose a saved blueprint below to paste or delete it.", "下の一覧から設計図を貼り付け・削除できます。"), false, "", false, true);
        }

        private static UnitKind ParseUnit(string value)
        {
            switch (value) { case "infantry": return UnitKind.Infantry; case "archer": return UnitKind.Archer; case "cavalry": return UnitKind.Cavalry; default: return UnitKind.Mercenary; }
        }

        private static ResourceKind ParseResource(string value)
        {
            switch (value) { case "food": return ResourceKind.Food; case "wood": return ResourceKind.Wood; case "stone": return ResourceKind.Stone; case "gems": return ResourceKind.Gems; default: return ResourceKind.Wood; }
        }

        private void TrainFromBuilding(EconomyView economy, BuildingKind kind, UnitKind unit, string notice)
        {
            var building = OwnBuilding(economy, kind);
            if (building.HasValue && building.Value.Complete) Send(EconomyCommand.Train(faction, ++sequence, building.Value.Id, unit), notice);
        }

        private void CancelFromBuilding(EconomyView economy, BuildingKind kind, string notice)
        {
            var building = OwnBuilding(economy, kind);
            if (building.HasValue && building.Value.Complete) Send(EconomyCommand.CancelTrain(faction, ++sequence, building.Value.Id), notice);
        }

        private static void Add(List<EconomyAction> rows, string id, string label, bool enabled = true, string reason = "", bool selected = false, bool message = false)
        {
            rows.Add(new EconomyAction(id, label, enabled, reason, selected, message));
        }

        private static string NeedBuilding(bool exists, bool complete, string name)
        {
            return !exists ? name + UiText.T(" is needed", "が必要") : !complete ? name + UiText.T(" is building", "を建設中") : "";
        }

        private void AddBuildActions(List<EconomyAction> rows, EconomyView economy)
        {
            Add(rows, "build:barracks", UiText.T("Barracks (", "兵舎（木材 ") + economy.BarracksWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Barracks);
            if (economy.Ages) Add(rows, "build:house", UiText.T("House +pop (", "住居・人口+（木材 ") + economy.HouseWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.House);
            else
            {
                var barracks = OwnBuilding(economy, BuildingKind.Barracks);
                if (barracks.HasValue && !barracks.Value.Complete) Add(rows, "build:barracks-progress", UiText.T("Barracks: building ", "兵舎：建設中 ") + Percent(barracks.Value), false, "", false, true);
            }
            if (economy.Ages)
            {
                Add(rows, "build:drop-site", UiText.T("Drop-off (", "資源置き場（木") + economy.DropSiteWoodCost + UiText.T("W)", "）"), true, "", mode == Mode.DropSite);
                Add(rows, "build:tower", UiText.T("Tower (", "見張り塔（石") + economy.TowerStoneCost + UiText.T("S ", " 木") + economy.TowerWoodCost + UiText.T("W)", "）"), true, "", mode == Mode.Tower);
                Add(rows, "build:wall", mode == Mode.Wall ? UiText.T("Drag the wall", "壁をドラッグ") : UiText.T("Wall (", "壁（石") + economy.WallStoneCost + UiText.T("S/cell)", "／マス）"), true, "", mode == Mode.Wall);
                if (economy.Civ != CivKind.Primitive) Add(rows, "build:market", UiText.T("Market (", "市場（木材 ") + economy.MarketWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Market);
                if (economy.Age >= 2) Add(rows, "build:siege", UiText.T("Siege workshop (", "攻城工房（木材 ") + economy.WorkshopWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.SiegeWorkshop);
                if (economy.Age >= 2)
                {
                    Add(rows, "build:range", UiText.T("Archery range (", "射撃場（木材 ") + economy.RangeWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.ArcheryRange);
                    Add(rows, "build:stable", UiText.T("Stable (", "厩舎（木材 ") + economy.StableWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Stable);
                    Add(rows, "build:triangle", UiText.T("Archers beat infantry, cavalry beats archers, infantry beats cavalry", "相性：弓兵は歩兵に強く、騎兵は弓兵に強く、歩兵は騎兵に強い"), false, "", false, true);
                    if (economy.Age >= 3) Add(rows, "build:castle", UiText.T("Castle (", "城（石") + economy.CastleStoneCost + UiText.T("S ", " 木") + economy.CastleWoodCost + UiText.T("W)", "）"), true, "", mode == Mode.Castle);
                }
            }
            if (!economy.Industry) return;
            if (economy.ProcessingChain)
            {
                Add(rows, "line:CoreMetal", UiText.T("Ask for core metal line", "コア金属ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.CoreMetal);
                Add(rows, "line:Steel", UiText.T("Ask for steel line", "鋼のラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Steel);
            }
            if (economy.Ages && economy.Civ == CivKind.Forestry)
            {
                Add(rows, "line:CoreWood", UiText.T("Ask for core wood line", "コア木材ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.CoreWood);
                if (economy.Age >= 2) Add(rows, "line:BowGear", UiText.T("Ask for bow gear line", "弓具ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.BowGear);
            }
            if (!economy.Ages || economy.Civ == CivKind.Metallurgy)
            {
                Add(rows, "build:mine", UiText.T("Mine (", "採掘場（木材 ") + economy.MineWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Mine);
                Add(rows, "build:smelter", UiText.T("Smelter (", "精錬所（木材 ") + economy.SmelterWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Smelter);
            }
            else if (economy.Civ == CivKind.Agrarian)
            {
                Add(rows, "build:farm", UiText.T("Farm (", "農場（木材 ") + economy.FarmWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Farm);
                Add(rows, "build:farm-info", UiText.T("Faster by food and river", "食料の点と川の近くほど速い"), false, "", false, true);
            }
            else Add(rows, "build:industry-info", UiText.T("Mines and farms come with a civilisation", "採掘場・農場は文明に進んでから"), false, "", false, true);
            Add(rows, "build:belt", mode == Mode.Belt ? UiText.T("Drag on the ground", "地面をドラッグ") : UiText.T("Belt (", "ベルト（木材 ") + economy.BeltWoodCost + UiText.T("/cell)", "／マス）"), true, "", mode == Mode.Belt);
            if (economy.BeltComponents)
            {
                Add(rows, "build:fast-belt", UiText.T("Fast belt (", "速いベルト（木材 ") + economy.FastBeltWoodCost + UiText.T("/cell)", "／マス）"), true, "", mode == Mode.FastBelt);
                Add(rows, "build:splitter", UiText.T("Splitter", "分岐"), true, "", mode == Mode.Splitter);
                Add(rows, "build:sorter", UiText.T("Sorter: ore", "仕分け：鉱石"), true, "", mode == Mode.Sorter);
                Add(rows, "build:underground", UiText.T("Underground belt", "地下ベルト"), true, "", mode == Mode.Underground);
                Add(rows, "build:storage", UiText.T("Storage", "倉庫"), true, "", mode == Mode.Storage);
            }
            Add(rows, "build:remove-belt", UiText.T("Remove", "ベルトを外す"), true, "", mode == Mode.RemoveBelt);
            Add(rows, "build:remove-area", UiText.T("Remove rectangle", "四角く撤去"), true, "", mode == Mode.RemoveArea);
            Add(rows, "build:facing", UiText.T("R: ", "R：") + FacingName(facing), false, "", false, true);
            AddFlowActions(rows, economy);
        }

        private void AddFlowActions(List<EconomyAction> rows, EconomyView economy)
        {
            if (layer == null) return;
            for (int i = 0; i < economy.Buildings.Count; i++)
            {
                var building = economy.Buildings[i];
                if (building.FactionId != faction) continue;
                int percent = Mathf.RoundToInt(layer.BuildingUtilization(building.Id) * 100f);
                Add(rows, "flow:building:" + building.Id, EconomyLayer.BuildingName(building.Kind) + UiText.T(" utilization ", " 稼働率 ") + percent + "% / 60s", false, "", false, true);
            }
            for (int i = 0; i < economy.Belts.Count; i++)
            {
                var belt = economy.Belts[i];
                if (belt.FactionId != faction) continue;
                int delivered = layer.BeltDeliveriesPerMinute(belt.Cell);
                if (delivered > 0) Add(rows, "flow:belt:" + belt.Cell, UiText.T("Belt end ", "ベルト終端 ") + belt.Cell + UiText.T(": ", "：") + delivered + UiText.T("/min", "個/分"), false, "", false, true);
            }
        }

        private void AddMakeActions(List<EconomyAction> rows, EconomyView economy)
        {
            string queue = economy.VillagerQueued == 0 ? "" : " [" + economy.VillagerQueued + ", " + Seconds(economy.VillagerTrainRemaining) + "]";
            Add(rows, "make:villager", UiText.T("Villager (", "村人（食料 ") + economy.VillagerFoodCost + UiText.T(" food)", "）") + queue);
            var barracks = OwnBuilding(economy, BuildingKind.Barracks);
            bool barracksReady = barracks.HasValue && barracks.Value.Complete;
            string infantry = UiText.T("Infantry (", "歩兵（食") + economy.InfantryFoodCost + UiText.T("F ", " 木") + economy.InfantryWoodCost + (economy.InfantryMetalCost > 0 ? UiText.T("W ", " 金") + economy.InfantryMetalCost + UiText.T("M)", "）") : UiText.T("W)", "）"));
            Add(rows, "make:infantry", infantry, barracksReady, barracksReady ? "" : NeedBuilding(barracks.HasValue, barracksReady, UiText.T("Barracks", "兵舎")));
            if (barracksReady) Add(rows, "make:infantry-cancel", UiText.T("Undo", "取消"));
            if (!barracksReady && !economy.Ages) Add(rows, "make:infantry-info", UiText.T("Infantry: build a barracks", "歩兵：兵舎を建てると作れる"), false, "", false, true);
            if (economy.MonksEnabled) Add(rows, "make:monk", UiText.T("Monk (", "僧侶（食料 ") + economy.MonkFoodCost + UiText.T("F ", " 金") + economy.MonkGoldCost + UiText.T(" gold)", "）"), barracksReady, barracksReady ? "" : UiText.T("Build a barracks first", "兵舎を先に建てる"));
            if (economy.Ages)
            {
                Add(rows, "make:scout", UiText.T("Scout (", "斥候（食料 ") + economy.ScoutFoodCost + UiText.T(" food)", "）"), barracksReady, barracksReady ? "" : UiText.T("Build a barracks first", "兵舎を先に建てる"));
                if (economy.Age >= 2 && economy.Civ == CivKind.Agrarian) Add(rows, "make:archer-barracks", UiText.T("Archer (", "弓兵（食") + economy.ArcherFoodCost + UiText.T("F ", " 木") + economy.ArcherWoodCost + UiText.T("W)", "）"), barracksReady, barracksReady ? "" : UiText.T("Build a barracks first", "兵舎を先に建てる"));
                else if (economy.Age >= 2 && economy.Civ == CivKind.Metallurgy) Add(rows, "make:cavalry-barracks", UiText.T("Cavalry (", "騎兵（食") + economy.CavalryFoodCost + UiText.T("F ", " 木") + economy.CavalryWoodCost + UiText.T("W ", " 金") + economy.CavalryMetalCost + UiText.T("M)", "）"), barracksReady, barracksReady ? "" : UiText.T("Build a barracks first", "兵舎を先に建てる"));
                AddProducerRows(rows, economy, BuildingKind.SiegeWorkshop, "make:ram", UiText.T("Ram (", "破城槌（食") + economy.RamFoodCost + UiText.T("F ", " 木") + economy.RamWoodCost + UiText.T("W)", "）"), UnitKind.Ram);
                bool rangeExists = OwnBuilding(economy, BuildingKind.ArcheryRange).HasValue;
                bool stableExists = OwnBuilding(economy, BuildingKind.Stable).HasValue;
                if (rangeExists) AddProducerRows(rows, economy, BuildingKind.ArcheryRange, "make:archer-range", UiText.T("Archer (", "弓兵（食") + economy.ArcherFoodCost + UiText.T("F ", " 木") + economy.ArcherWoodCost + UiText.T("W)", "）"), UnitKind.Archer);
                if (stableExists) AddProducerRows(rows, economy, BuildingKind.Stable, "make:cavalry-stable", UiText.T("Cavalry (", "騎兵（食") + economy.CavalryFoodCost + UiText.T("F ", " 木") + economy.CavalryWoodCost + UiText.T("W ", " 金") + economy.CavalryMetalCost + UiText.T("M)", "）"), UnitKind.Cavalry);
                var castle = OwnBuilding(economy, BuildingKind.Castle);
                if (castle.HasValue)
                {
                    AddProducerRows(rows, economy, BuildingKind.Castle, "make:castle:infantry", UiText.T("Castle: infantry", "城：歩兵"), UnitKind.Infantry);
                    AddProducerRows(rows, economy, BuildingKind.Castle, "make:castle:archer", UiText.T("Castle: archer", "城：弓兵"), UnitKind.Archer);
                    AddProducerRows(rows, economy, BuildingKind.Castle, "make:castle:cavalry", UiText.T("Castle: cavalry", "城：騎兵"), UnitKind.Cavalry);
                    AddProducerRows(rows, economy, BuildingKind.Castle, "make:castle:mercenary", UiText.T("Castle: mercenary (", "城：傭兵（") + economy.MercenaryGemsCost + UiText.T(" Gems)", " 宝）"), UnitKind.Mercenary);
                }
                var market = OwnBuilding(economy, BuildingKind.Market);
                if (market.HasValue)
                {
                    Add(rows, "make:market-info", UiText.T("Market ", "市場 ") + economy.TradeLot + UiText.T("->", "→") + economy.TradeReturn, false, "", false, true);
                    AddProducerRows(rows, economy, BuildingKind.Market, "make:trade:food-wood", UiText.T("Food->Wood", "食料→木材"), 0);
                    AddProducerRows(rows, economy, BuildingKind.Market, "make:trade:wood-food", UiText.T("Wood->Food", "木材→食料"), 0);
                    AddProducerRows(rows, economy, BuildingKind.Market, "make:trade:wood-stone", UiText.T("Wood->Stone", "木材→石"), 0);
                    AddProducerRows(rows, economy, BuildingKind.Market, "make:wood-gems", UiText.T("Wood->Gems (", "木材→宝石（") + economy.GemsTradeReturn + UiText.T(")", "）"), 0);
                    Add(rows, "make:idle-trade", UiText.T("Idle -> trade route", "待機中の村人 → 交易路"), market.Value.Complete, market.Value.Complete ? "" : UiText.T("Build the market first", "市場を完成させる"));
                }
            }
            AddAgeActions(rows, economy);
            AddIdleActions(rows, economy);
        }

        private void AddProducerRows(List<EconomyAction> rows, EconomyView economy, BuildingKind kind, string id, string label, UnitKind unit)
        {
            var producer = OwnBuilding(economy, kind);
            bool ready = producer.HasValue && producer.Value.Complete;
            Add(rows, id, label, ready, ready ? "" : NeedBuilding(producer.HasValue, ready, Name(kind)));
        }

        private void AddAgeActions(List<EconomyAction> rows, EconomyView economy)
        {
            if (!economy.Ages) return;
            if (economy.Civ == CivKind.Primitive && economy.AdvanceRemaining == 0)
            {
                string cost = UiText.T(" (", "（食") + economy.AdvanceFoodCost + UiText.T("F ", " 木") + economy.AdvanceWoodCost + UiText.T("W)", "）")
                    + AgeClockLabel(economy, economy.AdvanceFoodCost, economy.AdvanceWoodCost);
                if (economy.ReservedCiv != CivKind.Primitive) Add(rows, "make:reserved", UiText.T("Reserved: ", "予約中：") + CivName(economy.ReservedCiv), false, "", false, true);
                if (!ExtraCivilisations)
                {
                    Add(rows, "make:advance:" + (int)CivKind.Agrarian, Star(economy, CivKind.Agrarian) + UiText.T("Advance: farming", "時代を進める：農耕") + cost);
                    Add(rows, "make:advance:" + (int)CivKind.Metallurgy, Star(economy, CivKind.Metallurgy) + UiText.T("Advance: metallurgy", "時代を進める：冶金") + cost);
                }
                else
                {
                    Add(rows, "make:advance-info", UiText.T("Advance into", "時代を進める：") + cost, false, "", false, true);
                    for (int civIndex = 0; civIndex < AllCivs.Length; civIndex++)
                    {
                        CivKind civ = AllCivs[civIndex];
                        Add(rows, "make:advance:" + (int)civ, Star(economy, civ) + CivShortName(civ));
                    }
                }
            }
            else if ((economy.Age == 1 || economy.Age == 2) && economy.AdvanceRemaining == 0)
            {
                int food = economy.Age == 1 ? economy.Age2FoodCost : economy.Age3FoodCost;
                int wood = economy.Age == 1 ? economy.Age2WoodCost : economy.Age3WoodCost;
                int gold = economy.NextAgeGoldCost;
                string next = UiText.T("Advance: ", "時代を進める：") + AgeName(economy.Civ, economy.Age + 1);
                string price = UiText.T(" (", "（食") + food + UiText.T("F ", " 木") + wood + (gold > 0 ? UiText.T("W ", " 金") + gold + UiText.T("G)", "）") : UiText.T("W)", "）"))
                    + AgeClockLabel(economy, food, wood);
                string why = economy.Food < food ? UiText.T("not enough food", "食料が足りない") : economy.Wood < wood ? UiText.T("not enough wood", "木材が足りない") : economy.Gold < gold ? UiText.T("not enough gold", "金が足りない") : economy.VillagerQueued > 0 ? UiText.T("villagers in training", "村人の訓練中") : "";
                Add(rows, "make:advance-age", next + price, string.IsNullOrEmpty(why), why);
            }
        }

        private string AgeClockLabel(EconomyView economy, int food, int wood)
        {
            if (economy.NextAgeClockTick <= 0 || view == null || view.LatestFrame == null) return "";
            long remaining = Math.Max(0, economy.NextAgeClockTick - view.LatestFrame.Tick);
            long seconds = (remaining + 19) / 20;
            string time = (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
            return UiText.T(" (auto in ", "（あと ") + time + UiText.T(" / now food ", " で自動／今なら 食料") + food
                + UiText.T(" wood ", " 木材") + wood + ")";
        }

        private void AddIdleActions(List<EconomyAction> rows, EconomyView economy)
        {
            if (economy.Ages)
            {
                Add(rows, "make:idle:food", UiText.T("Idle -> food", "待機 → 食料"));
                Add(rows, "make:idle:wood", UiText.T("Idle -> wood", "待機 → 木材"));
                Add(rows, "make:idle:stone", UiText.T("Idle -> stone", "待機 → 石"));
            }
            else
            {
                Add(rows, "make:idle:food", UiText.T("Idle -> food", "待機中の村人 → 食料"));
                Add(rows, "make:idle:wood", UiText.T("Idle -> wood", "待機中の村人 → 木材"));
            }
            if (!economy.Industry || (economy.Ages && economy.Civ == CivKind.Primitive)) return;
            bool farming = economy.Ages && economy.Civ == CivKind.Agrarian;
            var source = OwnBuilding(economy, farming ? BuildingKind.Farm : BuildingKind.Mine);
            Add(rows, "make:carry", UiText.T("Idle -> carry from ", "待機中の村人 → ") + (farming ? UiText.T("the farm", "農場") : UiText.T("the mine", "採掘場")) + UiText.T(" by hand", "から手で運ぶ"), source.HasValue && source.Value.Complete, source.HasValue && source.Value.Complete ? "" : UiText.T("Build the source first", "資源施設を完成させる"));
        }

        private void AddResearchActions(List<EconomyAction> rows, EconomyView economy)
        {
            if (!economy.Ages) { Add(rows, "research:none", UiText.T("No research on this map", "このマップには研究はありません"), false, "", false, true); return; }
            if (economy.Civ == CivKind.Primitive) { Add(rows, "research:primitive", UiText.T("Research comes with a civilisation", "研究は文明に進んでから"), false, "", false, true); return; }
            var smith = OwnBuilding(economy, BuildingKind.Blacksmith);
            if (!smith.HasValue) { Add(rows, "research:build-blacksmith", UiText.T("Blacksmith (", "鍛冶場（木材 ") + economy.BlacksmithWoodCost + UiText.T(" wood)", "）"), true, "", false); return; }
            Add(rows, "research:status", !smith.Value.Complete ? UiText.T("Blacksmith: building ", "鍛冶場：建設中 ") + Percent(smith.Value) : smith.Value.Researching != 0 ? UiText.T("Researching: ", "研究中：") + TechName(smith.Value.Researching) + " " + Seconds(smith.Value.ResearchRemaining) : UiText.T("Blacksmith: pick a tech", "鍛冶場：研究を選ぶ"), false, "", false, true);
            var techs = new List<TechKind> { TechKind.Weapons, TechKind.Armour, TechKind.Tools, TechKind.Carts };
            if (economy.Civ == CivKind.Agrarian) techs.Add(TechKind.Irrigation); else if (economy.Civ == CivKind.Metallurgy) techs.Add(TechKind.BlastFurnace);
            if (economy.Age >= 2) { techs.Add(TechKind.SteelWeapons); techs.Add(TechKind.SteelArmour); }
            if (economy.Age >= 3) { techs.Add(TechKind.Masonry); techs.Add(TechKind.Siegecraft); techs.Add(TechKind.Banking); techs.Add(TechKind.GemArmor); }
            for (int techIndex = 0; techIndex < techs.Count; techIndex++)
            {
                TechKind tech = techs[techIndex];
                int bit = (int)tech - 1;
                bool done = (economy.Techs & (1UL << bit)) != 0;
                string cost = bit < economy.TechFoodCosts.Count ? UiText.T(" F", " 食") + economy.TechFoodCosts[bit] + UiText.T(" W", " 木") + economy.TechWoodCosts[bit] + (bit < economy.TechMetalCosts.Count && economy.TechMetalCosts[bit] > 0 ? UiText.T(" M", " 金") + economy.TechMetalCosts[bit] : "") + (bit < economy.TechGemsCosts.Count && economy.TechGemsCosts[bit] > 0 ? UiText.T(" G", " 宝") + economy.TechGemsCosts[bit] : "") : "";
                string why = !smith.Value.Complete ? UiText.T("Blacksmith is building", "鍛冶場を建設中") : smith.Value.Researching != 0 ? UiText.T("Research in progress", "研究中") : done ? UiText.T("Already researched", "研究済み") : "";
                Add(rows, "research:" + (int)tech, (done ? UiText.T("Done: ", "済：") : "") + TechName(tech) + (done ? "" : cost), string.IsNullOrEmpty(why), why);
            }
        }

        private void AddPolicyActions(List<EconomyAction> rows, EconomyView economy)
        {
            Add(rows, "policy:auto", economy.AutoEconomy ? UiText.T("Auto economy: on", "お任せ内政：入") : UiText.T("Auto economy: off", "お任せ内政：切"), true, "", false);
            if (economy.CorePlayerHeld) Add(rows, "policy:core", UiText.T("Core: trained by hand", "コア：手動で村人を作っている"), false, "", false, true);
            if (!economy.Industry) return;
            Add(rows, "policy:set:" + (int)EconomyPolicy.Balanced, UiText.T("Balanced", "均衡"), true, "", economy.Policy == EconomyPolicy.Balanced);
            Add(rows, "policy:set:" + (int)EconomyPolicy.Military, UiText.T("Army first", "兵を優先"), true, "", economy.Policy == EconomyPolicy.Military);
            Add(rows, "policy:set:" + (int)EconomyPolicy.Growth, UiText.T("Economy first", "内政を優先"), true, "", economy.Policy == EconomyPolicy.Growth);
            Add(rows, "policy:return", UiText.T("Hand everything you touched back to the auto economy", "触った物をすべてお任せに戻す"));
        }
    }
}
