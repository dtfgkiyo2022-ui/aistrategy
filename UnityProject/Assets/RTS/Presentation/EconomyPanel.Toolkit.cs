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
            AddSelectedBuildingActions(result, economy);
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
            GameAudio.Ui(SoundCue.Click);
            var economy = Economy();
            if (economy == null) return;
            foreach (var action in GetToolkitActions())
                if (action.Id == actionId && !action.Enabled && !action.Message &&
                    ((action.Reason ?? "").Contains("not enough") || (action.Reason ?? "").Contains("足り")))
                { GameAudio.Ui(SoundCue.ResourceShortage); return; }

            if (actionId.StartsWith("upgrade:", StringComparison.Ordinal))
            {
                uint buildingId;
                if (uint.TryParse(actionId.Substring(8), out buildingId))
                    Send(EconomyCommand.UpgradeBuilding(faction, ++sequence, buildingId), UiText.T("Upgrade requested", "段を上げるよう頼みました"));
                return;
            }
            if (actionId.StartsWith("recipe:", StringComparison.Ordinal))
            {
                var parts = actionId.Substring(7).Split(':');
                uint assemblerId;
                RecipeKind recipe;
                if (parts.Length == 2 && uint.TryParse(parts[0], out assemblerId) && Enum.TryParse(parts[1], true, out recipe))
                    Send(EconomyCommand.SetRecipe(faction, ++sequence, assemblerId, recipe), UiText.T("Assembler recipe requested", "組立所のレシピを切り替えるよう頼みました"));
                return;
            }
            if (actionId.StartsWith("build:", StringComparison.Ordinal))
            {
                var buildMode = ParseMode(actionId.Substring(6));
                if (buildMode != Mode.None) SetMode(buildMode);
                return;
            }
            if (actionId == "building:move") { BeginSelectedBuildingMove(); return; }
            if (actionId == "building:remove") { RemoveSelectedBuilding(); return; }
            if (actionId == "building:cancel-reservation") { CancelSelectedBuildingReservation(); return; }
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
            if (actionId == "villagers:select-idle") { SelectIdleVillagersFromPanel(); return; }
            if (actionId == "villagers:return-auto") { ReturnSelectedVillagersToAuto(); return; }
            if (actionId == "make:villager") { Send(EconomyCommand.Train(faction, ++sequence, 0, UnitKind.Villager), UiText.T("Villager requested", "村人を依頼しました")); return; }
            if (actionId == "make:infantry") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Infantry, UiText.T("Infantry requested", "歩兵を依頼しました")); return; }
            if (actionId == "make:infantry-cancel") { CancelFromBuilding(economy, BuildingKind.Barracks, UiText.T("Last infantry cancelled", "最後の歩兵を取り消しました")); return; }
            if (actionId == "make:monk") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Monk, UiText.T("Monk requested", "僧侶を依頼しました")); return; }
            if (actionId == "make:scout") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Scout, UiText.T("Scout requested", "斥候を依頼しました")); return; }
            if (actionId == "make:archer-barracks") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Archer, UiText.T("Archer requested", "弓兵を依頼しました")); return; }
            if (actionId == "make:cavalry-barracks") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Cavalry, UiText.T("Cavalry requested", "騎兵を依頼しました")); return; }
            if (actionId == "make:mercenary-barracks") { TrainFromBuilding(economy, BuildingKind.Barracks, UnitKind.Mercenary, UiText.T("Mercenary requested", "傭兵を依頼しました")); return; }
            if (actionId == "make:ram") { TrainFromBuilding(economy, BuildingKind.SiegeWorkshop, UnitKind.Ram, UiText.T("Ram requested", "破城槌を依頼しました")); return; }
            if (actionId == "make:archer-range") { TrainFromBuilding(economy, BuildingKind.ArcheryRange, UnitKind.Archer, UiText.T("Archer requested", "弓兵を依頼しました")); return; }
            if (actionId == "make:cavalry-stable") { TrainFromBuilding(economy, BuildingKind.Stable, UnitKind.Cavalry, UiText.T("Cavalry requested", "騎兵を依頼しました")); return; }
            if (actionId == "make:heavy-cavalry") { TrainFromBuilding(economy, BuildingKind.Stable, UnitKind.HeavyCavalry, UiText.T("Heavy cavalry requested", "重騎兵を依頼しました")); return; }
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
            if (actionId.StartsWith("make:goods:", StringComparison.Ordinal))
            {
                var goodsTrade = actionId.Substring(11).Split('-');
                if (goodsTrade.Length == 2) Send(EconomyCommand.Trade(faction, ++sequence, ParseResource(goodsTrade[0]), ParseResource(goodsTrade[1])), UiText.T("Goods trade requested", "加工品の売却を依頼しました"));
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
            if (actionId == "make:advance-now") { Send(EconomyCommand.AdvanceNow(faction, ++sequence, economy.Civ == CivKind.Primitive ? economy.ReservedCiv : economy.Civ), AgeClockUxUiText.AdvanceNow(economy.AdvanceFoodCost, economy.AdvanceWoodCost, economy.NextAgeGoldCost)); return; }
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
                case "castle": return Mode.Castle; case "mine": return Mode.Mine; case "drill": return Mode.Drill; case "smelter": return Mode.Smelter;
                case "farm": return Mode.Farm; case "mill": return Mode.Mill; case "brick-kiln": return Mode.BrickKiln; case "saddlery": return Mode.Saddlery; case "sawmill": return Mode.Sawmill; case "stonecutter": return Mode.Stonecutter; case "joinery": return Mode.Joinery; case "assembler": return Mode.Assembler; case "scriptorium": return Mode.Scriptorium; case "drying-rack": return Mode.DryingRack; case "lapidary": return Mode.Lapidary; case "weaver": return Mode.Weaver; case "charm-workshop": return Mode.CharmWorkshop; case "repair-yard": return Mode.RepairYard; case "tool-shop": return Mode.ToolShop; case "belt": return Mode.Belt; case "fast-belt": return Mode.FastBelt;
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
            switch (value) { case "infantry": return UnitKind.Infantry; case "archer": return UnitKind.Archer; case "cavalry": return UnitKind.Cavalry; case "heavy-cavalry": return UnitKind.HeavyCavalry; default: return UnitKind.Mercenary; }
        }

        private static ResourceKind ParseResource(string value)
        {
            if (string.Equals(value, "armskit", StringComparison.OrdinalIgnoreCase)) return ResourceKind.ArmsKit;
            if (string.Equals(value, "scholarly", StringComparison.OrdinalIgnoreCase)) return ResourceKind.Scholarly;
            if (string.Equals(value, "finearmskit", StringComparison.OrdinalIgnoreCase)) return ResourceKind.FineArmsKit;
            if (string.Equals(value, "siegeparts", StringComparison.OrdinalIgnoreCase)) return ResourceKind.SiegeParts;
            if (string.Equals(value, "mechanism", StringComparison.OrdinalIgnoreCase)) return ResourceKind.Mechanism;
            if (value == "cutstone" || value == "切石") return ResourceKind.CutStone;
            if (value == "frames" || value == "建材") return ResourceKind.Frames;
            if (value == "preserves" || value == "保存食") return ResourceKind.Preserves;
            if (value == "tack" || value == "馬具") return ResourceKind.Tack;
            if (value == "planks" || value == "板") return ResourceKind.Planks;
            if (value == "books" || value == "書物") return ResourceKind.Books;
            switch (value) { case "food": return ResourceKind.Food; case "wood": return ResourceKind.Wood; case "stone": return ResourceKind.Stone; case "gems": return ResourceKind.Gems; case "steel": case "鋼": return ResourceKind.Steel; case "bowgear": case "弓具": return ResourceKind.BowGear; case "rations": case "糧食": return ResourceKind.Rations; case "bricks": case "煉瓦": return ResourceKind.Bricks; case "incense": case "香": return ResourceKind.Incense; case "charms": case "護符": return ResourceKind.Charms; case "repairkits": case "補修材": return ResourceKind.RepairKits; case "tools": case "工具": return ResourceKind.Tools; default: return ResourceKind.Wood; }
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
            Add(rows, "build:barracks", UiText.T("Barracks (", "兵舎（木材 ") + economy.BarracksWoodCost + UiText.T(" wood)", "）") + CraftBuildingCost(economy, BuildingKind.Barracks), true, "", mode == Mode.Barracks);
            if (economy.Ages) Add(rows, "build:house", UiText.T("House +pop (", "住居・人口+（木材 ") + economy.HouseWoodCost + UiText.T(" wood)", "）") + CraftBuildingCost(economy, BuildingKind.House), true, "", mode == Mode.House);
            else
            {
                var barracks = OwnBuilding(economy, BuildingKind.Barracks);
                if (barracks.HasValue && !barracks.Value.Complete) Add(rows, "build:barracks-progress", UiText.T("Barracks: building ", "兵舎：建設中 ") + Percent(barracks.Value), false, "", false, true);
            }
            if (economy.Ages)
            {
                bool dropSiteCapped = economy.DropSiteMax > 0 && economy.DropSiteCount >= economy.DropSiteMax;
                if (dropSiteCapped && mode == Mode.DropSite) SetMode(Mode.None);
                Add(rows, "build:drop-site", UiText.DropSiteLabel(economy.DropSiteCount, economy.DropSiteMax, economy.DropSiteWoodCost),
                    !dropSiteCapped, dropSiteCapped ? UiText.T("Drop-site limit reached", "集積所の上限に達しています") : "", mode == Mode.DropSite);
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
            if (economy.RationsEnabled && economy.Civ == CivKind.Agrarian && economy.Age >= 1)
            {
                Add(rows, "build:mill", UiText.T("Mill (", "製粉所（木材 ") + economy.MillWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Mill);
                Add(rows, "line:Rations", UiText.T("Ask for rations line", "糧食のラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Rations);
            }
            if (economy.BricksEnabled && economy.Civ == CivKind.Masonry && economy.Age >= 1)
            {
                Add(rows, "build:brick-kiln", UiText.T("Brick kiln", "煉瓦窯"), true, "", mode == Mode.BrickKiln);
                Add(rows, "line:Bricks", UiText.T("Ask for bricks line", "煉瓦ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Bricks);
            }
            if (economy.TackEnabled && economy.Civ == CivKind.Cavalry && economy.Age >= 1)
            {
                Add(rows, "build:saddlery", UiText.T("Saddlery (", "馬具工房（木材 ") + economy.SaddleryWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Saddlery);
                Add(rows, "line:Tack", UiText.T("Ask for tack line", "馬具ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Tack);
            }
            if (economy.PlanksEnabled && economy.Civ == CivKind.Bridge && economy.Age >= 1 && !economy.CraftTree1Enabled)
            {
                Add(rows, "build:sawmill", UiText.T("Sawmill (", "製材所（木材 ") + economy.SawmillWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Sawmill);
                Add(rows, "line:Planks", UiText.T("Ask for planks line", "板材ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Planks);
            }
            if (economy.CraftDepthEnabled)
            {
                if (economy.Age >= 1)
                    Add(rows, "build:metalworks", UiText.T("Metalworks (100 wood)", "金属加工場（木材100）"), true, "", mode == Mode.Metalworks);
                if (economy.Age >= 2)
                    Add(rows, "build:rope-hut", UiText.T("Rope hut (60 wood)", "縄工房（木材60）"), true, "", mode == Mode.RopeHut);
                Add(rows, "line:Fittings", UiText.T("Ask for fittings line", "金具ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Fittings);
                Add(rows, "line:Rope", UiText.T("Ask for rope line", "ロープラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Rope);
            }
            if (economy.CraftTree1Enabled && economy.Age >= 1)
            {
                Add(rows, "build:sawmill", UiText.T("Sawmill (", "製材所（木材 ") + economy.SawmillWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Sawmill);
                Add(rows, "build:stonecutter", UiText.T("Stonecutter (", "石工場（木材 ") + economy.StonecutterWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Stonecutter);
                Add(rows, "line:CutStone", UiText.T("Ask for cut-stone line", "切石ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.CutStone);
                if (economy.Age >= 2)
                {
                    Add(rows, "build:joinery", UiText.T("Joinery (", "木工所（木材 ") + economy.JoineryWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Joinery);
                    Add(rows, "line:Frames", UiText.T("Ask for frames line", "建材ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Frames);
                }
                Add(rows, "line:Planks", UiText.T("Ask for planks line", "板材ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Planks);
            }
            if (economy.PrimitiveCraftEnabled && economy.Age == 0)
                Add(rows, "build:hut", UiText.T("Hut (", "作業小屋（木材 ") + economy.HutWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Hut);
            if (economy.CraftTree1Enabled && economy.Age >= 1)
            {
                for (int buildingIndex = 0; buildingIndex < economy.Buildings.Count; buildingIndex++)
                {
                    var building = economy.Buildings[buildingIndex];
                    bool processing = building.Kind == BuildingKind.Sawmill || building.Kind == BuildingKind.CharcoalKiln
                        || building.Kind == BuildingKind.Stonecutter || building.Kind == BuildingKind.Joinery
                        || building.Kind == BuildingKind.Mine || building.Kind == BuildingKind.Smelter
                        || building.Kind == BuildingKind.Steelworks || building.Kind == BuildingKind.Fletcher
                        || building.Kind == BuildingKind.BrickKiln || building.Kind == BuildingKind.Saddlery
                        || building.Kind == BuildingKind.Scriptorium || building.Kind == BuildingKind.DryingRack
                        || building.Kind == BuildingKind.Lapidary || building.Kind == BuildingKind.Weaver
                        || building.Kind == BuildingKind.IncenseHouse || building.Kind == BuildingKind.CharmWorkshop
                        || building.Kind == BuildingKind.Mill || building.Kind == BuildingKind.RepairYard
                        || building.Kind == BuildingKind.ToolShop;
                    if (building.FactionId != faction || !building.Complete || building.Level >= 2 || !processing) continue;
                    bool affordable = economy.Frames >= economy.Level2FramesCost;
                    Add(rows, "upgrade:" + building.Id, UiText.T("Upgrade " + EconomyLayer.BuildingName(building.Kind) + " (", "Lv1→Lv2 " + EconomyLayer.BuildingName(building.Kind) + "（") + economy.Level2FramesCost + UiText.T(" frames)", "建材）"), affordable,
                        affordable ? "" : UiText.T("Not enough frames", "建材が足りません"));
                }
            }
            if (economy.BooksEnabled && economy.Civ == CivKind.Academy && economy.Age >= 1)
            {
                Add(rows, "build:scriptorium", UiText.T("Scriptorium (", "写本所（木材 ") + economy.ScriptoriumWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Scriptorium);
                Add(rows, "line:Books", UiText.T("Ask for books line", "書物ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Books);
            }
            if (economy.CraftTree2Enabled && economy.Age >= 2)
            {
                Add(rows, "build:assembler", UiText.T("Assembler (" + economy.AssemblerWoodCost + " wood)", "組立所（木材 " + economy.AssemblerWoodCost + "）"), true, "", mode == Mode.Assembler);
                for (int i = 0; i < economy.Buildings.Count; i++)
                {
                    var assembler = economy.Buildings[i];
                    if (assembler.FactionId != faction || assembler.Kind != BuildingKind.Assembler || !assembler.Complete) continue;
                    Add(rows, "recipe:" + assembler.Id + ":Frames", UiText.T("Assembler " + assembler.Id + ": Frames (plank 1 + cut stone/brick 1, 80t)", "組立所 " + assembler.Id + "：建材（板1＋切石/煉瓦1、80t）"), true, "", assembler.Recipe == RecipeKind.Frames);
                    bool armsMaterial = economy.Planks >= economy.ArmsKitPlankInput && (economy.Charcoal >= economy.ArmsKitCharcoalInput || economy.Steel >= economy.ArmsKitSteelInput);
                    Add(rows, "recipe:" + assembler.Id + ":ArmsKit", UiText.T("Assembler " + assembler.Id + ": Arms kit (plank 1 + charcoal 2 or steel 1, " + economy.ArmsKitTicks + "t)", "組立所 " + assembler.Id + "：武具キット（板1＋木炭2または鋼1、" + economy.ArmsKitTicks + "t）"), true, armsMaterial ? "" : UiText.T("Missing planks and charcoal/steel", "板材と木炭/鋼が不足"), assembler.Recipe == RecipeKind.ArmsKit);
                    bool scholarlyMaterial = economy.Planks >= economy.ScholarlyPlankInput && economy.Charcoal >= economy.ScholarlyCharcoalInput && economy.Food >= economy.ScholarlyFoodInput;
                    Add(rows, "recipe:" + assembler.Id + ":Scholarly", UiText.T("Assembler " + assembler.Id + ": Scholarly (plank 1 + charcoal 1 + food 2, " + economy.ScholarlyTicks + "t)", "組立所 " + assembler.Id + "：学術資材（板1＋木炭1＋食料2、" + economy.ScholarlyTicks + "t）"), true, scholarlyMaterial ? "" : UiText.T("Missing planks, charcoal, or food", "板材・木炭・食料が不足"), assembler.Recipe == RecipeKind.Scholarly);
                    if (economy.CraftTree4Enabled)
                    {
                        Add(rows, "recipe:" + assembler.Id + ":SiegeEngine", UiText.T("Assembler " + assembler.Id + ": Siege engine (siege parts 2 + mechanism 1, " + economy.SiegeEngineTicks + "t)", "組立所 " + assembler.Id + "：攻城機関（攻城部品2＋精密部品1、" + economy.SiegeEngineTicks + "t）"), true, "", assembler.Recipe == RecipeKind.SiegeEngine);
                        Add(rows, "recipe:" + assembler.Id + ":EliteKit", UiText.T("Assembler " + assembler.Id + ": Elite kit (arms kit 2 + mechanism 1 + metal 5, " + economy.EliteKitTicks + "t)", "組立所 " + assembler.Id + "：精鋭キット（武具キット2＋精密部品1＋金属5、" + economy.EliteKitTicks + "t）"), true, "", assembler.Recipe == RecipeKind.EliteKit);
                    }
                    if (economy.CraftTree3Enabled)
                    {
                        Add(rows, "recipe:" + assembler.Id + ":SiegeParts", UiText.T("Assembler " + assembler.Id + ": Siege parts (frames 1 + arms kit 1, " + economy.SiegePartsTicks + "t)", "組立所 " + assembler.Id + "：攻城部品（建材1＋武具1、" + economy.SiegePartsTicks + "t）"), true, "", assembler.Recipe == RecipeKind.SiegeParts);
                        Add(rows, "recipe:" + assembler.Id + ":Mechanism", UiText.T("Assembler " + assembler.Id + ": Mechanism (arms kit 1 + scholarly 1 + cut stone 1, " + economy.MechanismTicks + "t)", "組立所 " + assembler.Id + "：精密部品（武具1＋学術品1＋切石1、" + economy.MechanismTicks + "t）"), true, "", assembler.Recipe == RecipeKind.Mechanism);
                    }
                }
            }
            if (economy.PreservesEnabled && economy.Civ == CivKind.Fishing && economy.Age >= 1)
            {
                Add(rows, "build:drying-rack", UiText.T("Drying rack (", "干し場（木材 ") + economy.DryingRackWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.DryingRack);
                Add(rows, "line:Preserves", UiText.T("Ask for preserves line", "保存食ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Preserves);
            }
            if (economy.JewelsEnabled && economy.Civ == CivKind.Mountain && economy.Age >= 1)
            {
                Add(rows, "build:lapidary", UiText.T("Lapidary (", "研磨所（木材 ") + economy.LapidaryWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Lapidary);
                Add(rows, "line:Jewels", UiText.T("Ask for jewels line", "宝石のラインを依頼"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Jewels);
            }
            if (economy.TradeGoodsEnabled && economy.Civ == CivKind.Caravan && economy.Age >= 1)
            {
                Add(rows, "build:weaver", UiText.T("Weaver (", "織物工房（木材 ") + economy.WeaverWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Weaver);
                Add(rows, "line:TradeGoods", UiText.T("Ask for trade goods line", "交易品ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.TradeGoods);
            }
            if (economy.IncenseEnabled && economy.Civ == CivKind.Cult && economy.Age >= 1)
            {
                Add(rows, "build:incense-house", UiText.T("Incense house (", "香房（木材 ") + economy.IncenseHouseWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.IncenseHouse);
                Add(rows, "line:Incense", UiText.T("Ask for incense line", "香ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Incense);
            }
            if (economy.CharmsEnabled && economy.Civ == CivKind.Sanctuary && economy.Age >= 1)
            {
                Add(rows, "build:charm-workshop", UiText.T("Charm workshop (", "護符工房（木材 ") + economy.CharmWorkshopWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.CharmWorkshop);
                Add(rows, "line:Charms", UiText.T("Ask for charms line", "護符ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Charms);
            }
            if (economy.RepairKitsEnabled && economy.Civ == CivKind.Tollgate && economy.Age >= 1)
            {
                Add(rows, "build:repair-yard", UiText.T("Repair yard (", "修繕所（木材 ") + economy.RepairYardWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.RepairYard);
                Add(rows, "line:RepairKits", UiText.T("Ask for repair kits line", "補修材ラインを頼む"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.RepairKits);
            }
            if (economy.ToolsEnabled && economy.Civ == CivKind.Metropolis && economy.Age >= 1)
            {
                Add(rows, "build:tool-shop", UiText.T("Tool shop (", "工具工房（木材 ") + economy.ToolShopWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.ToolShop);
                Add(rows, "line:Tools", UiText.T("Ask for tools line", "工具ラインを指定"), true, "", mode == Mode.RequestLine && requestedLine == ProcessingLineKind.Tools);
            }
            if (!economy.Industry)
            {
                ApplyReservationLabels(rows, economy);
                return;
            }
            if (economy.CommonDrill)
                Add(rows, "build:drill", UiText.T("Drill (wood " + economy.DrillWoodCost + ", stone " + economy.DrillStoneCost + ")", "採掘機（木材 " + economy.DrillWoodCost + "、石 " + economy.DrillStoneCost + "）"), true, "", mode == Mode.Drill);
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
            else if (economy.FarmSizeCells > 0)
            {
                Add(rows, "build:farm", UiText.T("Farm (", "農場（木材 ") + economy.FarmWoodCost + UiText.T(" wood)", "）"), true, "", mode == Mode.Farm);
                Add(rows, "build:farm-info", UiText.T("Faster by food and river", "食料の点と川の近くほど速い"), false, "", false, true);
            }
            else Add(rows, "build:industry-info", UiText.T("Mines and farms come with a civilisation", "採掘場・農場は文明に進んでから"), false, "", false, true);
            Add(rows, "build:belt", mode == Mode.Belt ? UiText.T("Drag on the ground", "地面をドラッグ") : UiText.T("Belt (", "ベルト（木材 ") + economy.BeltWoodCost + UiText.T("/cell)", "／マス）") + CraftBeltCost(economy, false), true, "", mode == Mode.Belt);
            if (economy.BeltComponents)
            {
                Add(rows, "build:fast-belt", UiText.T("Fast belt (", "速いベルト（木材 ") + economy.FastBeltWoodCost + UiText.T("/cell)", "／マス）") + CraftBeltCost(economy, true), true, "", mode == Mode.FastBelt);
                Add(rows, "build:splitter", UiText.T("Splitter", "分岐") + CraftMaterial(economy.SplitterPlankCost), true, "", mode == Mode.Splitter);
                Add(rows, "build:sorter", UiText.T("Sorter: ore", "仕分け：鉱石") + CraftMaterial(economy.SorterPlankCost), true, "", mode == Mode.Sorter);
                Add(rows, "build:underground", UiText.T("Underground belt", "地下ベルト") + CraftMaterial(economy.UndergroundBeltPlankCost), true, "", mode == Mode.Underground);
                Add(rows, "build:storage", UiText.T("Storage", "倉庫"), true, "", mode == Mode.Storage);
                Add(rows, "build:storage-info", UiText.StorageDescription(), false, "", false, true);
            }
            Add(rows, "build:remove-belt", UiText.T("Remove", "ベルトを外す"), true, "", mode == Mode.RemoveBelt);
            Add(rows, "build:remove-area", UiText.T("Remove rectangle", "四角く撤去"), true, "", mode == Mode.RemoveArea);
            Add(rows, "build:facing", UiText.T("R: ", "R：") + FacingName(facing), false, "", false, true);
            AddFlowActions(rows, economy);
            ApplyReservationLabels(rows, economy);
        }

        private void AddSelectedBuildingActions(List<EconomyAction> rows, EconomyView economy)
        {
            BuildingView selected;
            if (!TrySelectedBuilding(out selected)) return;
            var state = BuildingRelocationUiRules.Evaluate(economy.EasyRebuild, faction, selected);
            if (state.CanMove)
            {
                Add(rows, "building:move", UiText.T("Move (V)", "移動（V）"), true, "", mode == Mode.MoveBuilding);
                Add(rows, "building:remove", UiText.T("Remove (Del)", "取り壊し（Del）"));
                Add(rows, "building:remove-info", UiText.T("Paid building materials are refunded.", "支払った建築材料が戻ります。"), false, "", false, true);
            }
            else if (state.MoveBlockedByKind)
            {
                Add(rows, "building:move-unavailable", UiText.T("Move (V)", "移動（V）"), false,
                    UiText.T("This building type cannot be moved.", "この種類の建物は移動できません。"));
                Add(rows, "building:remove", UiText.T("Remove (Del)", "取り壊し（Del）"));
            }
            else if (state.CanCancelReservation)
                Add(rows, "building:cancel-reservation", UiText.T("Cancel reservation", "予約を消す"));
        }

        public void BeginSelectedBuildingMove()
        {
            BuildingView selected;
            var economy = Economy();
            if (economy == null || !TrySelectedBuilding(out selected)) return;
            if (BuildingRelocationUiRules.Evaluate(economy.EasyRebuild, faction, selected).CanMove)
            {
                SetMode(Mode.MoveBuilding);
                Note(UiText.T("Choose a new place for the building.", "建物の新しい置き場所を選んでください。"));
            }
            else if (selected.Complete && BuildingRelocationUiRules.Evaluate(economy.EasyRebuild, faction, selected).MoveBlockedByKind)
                Note(UiText.T("This building type cannot be moved.", "この種類の建物は移動できません。"));
        }

        public void RemoveSelectedBuilding()
        {
            BuildingView selected;
            var economy = Economy();
            if (economy == null || !TrySelectedBuilding(out selected)) return;
            if (BuildingRelocationUiRules.Evaluate(economy.EasyRebuild, faction, selected).CanRemove)
                Send(BuildingRelocationUiRules.CreateCommand(BuildingRelocationUiAction.Remove,
                    faction, ++sequence, selected.Id, 0, Facing.North), RemovalNotice(selected, economy));
        }

        public void CancelSelectedBuildingReservation()
        {
            BuildingView selected;
            var economy = Economy();
            if (economy == null || !TrySelectedBuilding(out selected)) return;
            if (BuildingRelocationUiRules.Evaluate(economy.EasyRebuild, faction, selected).CanCancelReservation)
                Send(BuildingRelocationUiRules.CreateCommand(BuildingRelocationUiAction.CancelReservation,
                    faction, ++sequence, selected.Id, 0, Facing.North), UiText.T("Reservation cancelled.", "予約を取り消しました。"));
        }

        private void ApplyReservationLabels(List<EconomyAction> rows, EconomyView economy)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                BuildingKind kind;
                if (!TryReservationKind(rows[i].Id, out kind) || !ReservationLabel(economy, kind)) continue;
                var row = rows[i];
                rows[i] = new EconomyAction(row.Id, row.Label + UiText.T(" [reserve]", "【予約可】"), row.Enabled,
                    row.Reason, row.Selected, row.Message);
            }
        }

        private static bool TryReservationKind(string id, out BuildingKind kind)
        {
            kind = BuildingKind.Barracks;
            switch (id)
            {
                case "build:barracks": kind = BuildingKind.Barracks; return true;
                case "build:house": kind = BuildingKind.House; return true;
                case "build:drop-site": kind = BuildingKind.DropSite; return true;
                case "build:tower": kind = BuildingKind.Tower; return true;
                case "build:market": kind = BuildingKind.Market; return true;
                case "build:siege": kind = BuildingKind.SiegeWorkshop; return true;
                case "build:range": kind = BuildingKind.ArcheryRange; return true;
                case "build:stable": kind = BuildingKind.Stable; return true;
                case "build:castle": kind = BuildingKind.Castle; return true;
                case "build:mine": kind = BuildingKind.Mine; return true;
                case "build:smelter": kind = BuildingKind.Smelter; return true;
                case "build:farm": kind = BuildingKind.Farm; return true;
                case "build:drill": kind = BuildingKind.Drill; return true;
                case "build:saddlery": kind = BuildingKind.Saddlery; return true;
                case "build:sawmill": kind = BuildingKind.Sawmill; return true;
                case "build:hut": kind = BuildingKind.Hut; return true;
                case "build:stonecutter": kind = BuildingKind.Stonecutter; return true;
                case "build:joinery": kind = BuildingKind.Joinery; return true;
                case "build:metalworks": kind = BuildingKind.Metalworks; return true;
                case "build:rope-hut": kind = BuildingKind.RopeHut; return true;
                case "build:scriptorium": kind = BuildingKind.Scriptorium; return true;
                case "build:drying-rack": kind = BuildingKind.DryingRack; return true;
                case "build:lapidary": kind = BuildingKind.Lapidary; return true;
                case "build:weaver": kind = BuildingKind.Weaver; return true;
                case "build:incense-house": kind = BuildingKind.IncenseHouse; return true;
                case "build:charm-workshop": kind = BuildingKind.CharmWorkshop; return true;
                case "build:repair-yard": kind = BuildingKind.RepairYard; return true;
                case "build:tool-shop": kind = BuildingKind.ToolShop; return true;
                case "build:storage": kind = BuildingKind.Storage; return true;
                default: return false;
            }
        }

        private bool ReservationLabel(EconomyView economy, BuildingKind kind)
        {
            if (!economy.BuildingReservations) return false;
            int wood = kind == BuildingKind.Barracks ? economy.BarracksWoodCost : kind == BuildingKind.House ? economy.HouseWoodCost
                : kind == BuildingKind.Tower ? economy.TowerWoodCost : kind == BuildingKind.Castle ? economy.CastleWoodCost
                : kind == BuildingKind.Mine ? economy.MineWoodCost : kind == BuildingKind.Drill ? economy.DrillWoodCost
                : kind == BuildingKind.Farm ? economy.FarmWoodCost : kind == BuildingKind.Smelter ? economy.SmelterWoodCost
                : kind == BuildingKind.Storage ? economy.StorageWoodCost : 0;
            int stone = kind == BuildingKind.Tower ? economy.TowerStoneCost : kind == BuildingKind.Castle ? economy.CastleStoneCost
                : kind == BuildingKind.Drill ? economy.DrillStoneCost : 0;
            return economy.Wood < wood || economy.Stone < stone;
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
            bool hasSelectedVillagers = layer != null && layer.HasVillagerSelection;
            Add(rows, "villagers:summary", SelectedVillagerSummary, false, "", false, true);
            Add(rows, "villagers:select-idle", UiText.T("Select all idle villagers (H)", "待機中の村人をすべて選ぶ（H）"));
            Add(rows, "villagers:return-auto", UiText.T("Return selected to auto (whole economy)", "選択中をお任せに戻す（内政全体）"), hasSelectedVillagers,
                hasSelectedVillagers ? "" : UiText.T("Select villagers first", "先に村人を選択してください"));
            string queue = economy.VillagerQueued == 0 ? "" : " [" + economy.VillagerQueued + ", " + Seconds(economy.VillagerTrainRemaining) + "]";
            Add(rows, "make:villager", UiText.T("Villager (", "村人（食料 ") + economy.VillagerFoodCost + UiText.T(" food)", "）") + queue);
            var barracks = OwnBuilding(economy, BuildingKind.Barracks);
            bool barracksReady = barracks.HasValue && barracks.Value.Complete;
             string infantry = TrainingCostLabel(economy, UnitKind.Infantry, "Infantry", "歩兵", economy.InfantryFoodCost, economy.InfantryWoodCost, economy.InfantryMetalCost);
            Add(rows, "make:infantry", infantry, barracksReady, barracksReady ? "" : NeedBuilding(barracks.HasValue, barracksReady, UiText.T("Barracks", "兵舎")));
            if (barracksReady) Add(rows, "make:infantry-cancel", UiText.T("Undo", "取消"));
            if (!barracksReady && !economy.Ages) Add(rows, "make:infantry-info", UiText.T("Infantry: build a barracks", "歩兵：兵舎を建てると作れる"), false, "", false, true);
            if (economy.MonksEnabled) Add(rows, "make:monk", UiText.T("Monk (", "僧侶（食料 ") + economy.MonkFoodCost + UiText.T("F ", " 金") + economy.MonkGoldCost + UiText.T(" gold)", "）"), barracksReady, barracksReady ? "" : UiText.T("Build a barracks first", "兵舎を先に建てる"));
            if (economy.Ages)
            {
                Add(rows, "make:scout", UiText.T("Scout (", "斥候（食料 ") + economy.ScoutFoodCost + UiText.T(" food)", "）"), barracksReady, barracksReady ? "" : UiText.T("Build a barracks first", "兵舎を先に建てる"));
                 if (economy.Age >= 2 && economy.Civ == CivKind.Agrarian) Add(rows, "make:archer-barracks", TrainingCostLabel(economy, UnitKind.Archer, "Archer", "弓兵", economy.ArcherFoodCost, economy.ArcherWoodCost), barracksReady, barracksReady ? "" : UiText.T("Build a barracks first", "兵舎を先に建てる"));
                 else if (economy.Age >= 2 && economy.Civ == CivKind.Metallurgy) Add(rows, "make:cavalry-barracks", TrainingCostLabel(economy, UnitKind.Cavalry, "Cavalry", "騎兵", economy.CavalryFoodCost, economy.CavalryWoodCost, economy.CavalryMetalCost), barracksReady, barracksReady ? "" : UiText.T("Build a barracks first", "兵舎を先に建てる"));
                else if (economy.Age >= 2 && economy.Civ == CivKind.Mountain) AddProducerRows(rows, economy, BuildingKind.Barracks, "make:mercenary-barracks", UiText.T("Mercenary (", "傭兵（食料 ") + economy.JewelMercenaryFood + UiText.T("F, ", "、宝石 ") + economy.JewelMercenaryGems + UiText.T(" Gems)", "）"), UnitKind.Mercenary);
                 AddProducerRows(rows, economy, BuildingKind.SiegeWorkshop, "make:ram", TrainingCostLabel(economy, UnitKind.Ram, "Ram", "破城槌", economy.RamFoodCost, economy.RamWoodCost), UnitKind.Ram);
                bool rangeExists = OwnBuilding(economy, BuildingKind.ArcheryRange).HasValue;
                bool stableExists = OwnBuilding(economy, BuildingKind.Stable).HasValue;
                 if (rangeExists) AddProducerRows(rows, economy, BuildingKind.ArcheryRange, "make:archer-range", TrainingCostLabel(economy, UnitKind.Archer, "Archer", "弓兵", economy.ArcherFoodCost, economy.ArcherWoodCost), UnitKind.Archer);
                 if (stableExists) AddProducerRows(rows, economy, BuildingKind.Stable, "make:cavalry-stable", TrainingCostLabel(economy, UnitKind.Cavalry, "Cavalry", "騎兵", economy.CavalryFoodCost, economy.CavalryWoodCost, economy.CavalryMetalCost), UnitKind.Cavalry);
                 if (economy.CraftRequiredEnabled && economy.Age >= 2 && ArmsKitStock(economy) == 0)
                     Add(rows, "make:arms-kit-info", UiText.T("Not enough arms kits: make arms kits (1 plank + 2 charcoal) at an assembler", "武具が足りません：組立所で武具キット（板1＋木炭2）を作ってください"), false, "", false, true);
                if (stableExists && economy.TackEnabled)
                {
                    AddProducerRows(rows, economy, BuildingKind.Stable, "make:heavy-cavalry", UiText.T("Heavy cavalry", "重騎兵") + " (tack " + economy.HeavyCavalryTackCost + ", food " + economy.HeavyCavalryFoodCost + ")", UnitKind.HeavyCavalry);
                    if (economy.Tack < economy.HeavyCavalryTackCost)
                        Add(rows, "make:heavy-cavalry-info", UiText.T("Heavy cavalry unavailable: produce more tack", "重騎兵は馬具不足：馬具を増産してください"), false, "", false, true);
                }
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
                    if (economy.GoodsMarketEnabled && economy.GoodsTradeLot > 0)
                        foreach (var goods in GoodsKindsForDisplay)
                            if (GoodsStock(economy, goods) >= economy.GoodsTradeLot)
                                foreach (var take in new[] { ResourceKind.Food, ResourceKind.Wood, ResourceKind.Stone })
                                    AddProducerRows(rows, economy, BuildingKind.Market, "make:goods:" + goods.ToString().ToLowerInvariant() + "-" + take.ToString().ToLowerInvariant(), GoodsTradeLabel(goods, take, economy.GoodsTradeLot) + " (" + GoodsPrice(economy, goods) + ")", 0);
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

        private static int ArmsKitStock(EconomyView economy)
        {
            return economy.ArmsKit + economy.FineArmsKit + (economy.CraftTree4Enabled ? economy.EliteKit : 0);
        }

        private static bool UsesArmsKitForDisplay(UnitKind unit)
        {
            return unit != UnitKind.Villager && unit != UnitKind.Scout && unit != UnitKind.Monk;
        }

        private static string TrainingCostLabel(EconomyView economy, UnitKind unit, string englishName, string japaneseName, int food, int wood, int metal = 0)
        {
            if (economy.CraftRequiredEnabled && economy.Age >= 2 && UsesArmsKitForDisplay(unit))
                return UiText.T(englishName + " (" + food + "F 1 Arms)", japaneseName + "（食" + food + " 武具1）");
            return UiText.T(englishName + " (" + food + "F " + wood + "W" + (metal > 0 ? " " + metal + "M" : "") + ")",
                japaneseName + "（食" + food + " 木" + wood + (metal > 0 ? " 金" + metal : "") + "）");
        }

        private void AddAgeActions(List<EconomyAction> rows, EconomyView economy)
        {
            if (!economy.Ages) return;
            if (economy.Civ == CivKind.Primitive && economy.AdvanceRemaining == 0)
            {
                string cost = UiText.T(" (", "（食") + economy.AdvanceFoodCost + UiText.T("F ", " 木") + economy.AdvanceWoodCost + UiText.T("W)", "）")
                    + AgeClockLabel(economy, economy.AdvanceFoodCost, economy.AdvanceWoodCost);
                if (economy.AgeClockUx && AgeClockUxUiText.IsChoiceWindow(economy.NextAgeClockTick, economy.CivChoiceDeadlineTick, view.LatestFrame.Tick))
                    Add(rows, "make:civ-choice-deadline", AgeClockUxUiText.ChoiceDeadline(economy.CivChoiceDeadlineTick, view.LatestFrame.Tick), false, "", false, true);
                if (economy.ReservedCiv != CivKind.Primitive)
                {
                    Add(rows, "make:reserved", economy.AgeClockUx
                        ? AgeClockUxUiText.Reservation(CivName(economy.ReservedCiv), economy.NextAgeClockTick, view.LatestFrame.Tick)
                        : UiText.T("Reserved: ", "予約中：") + CivName(economy.ReservedCiv), false, "", false, true);
                    if (economy.AgeClockUx)
                        Add(rows, "make:advance-now", AgeClockUxUiText.AdvanceNow(economy.AdvanceFoodCost, economy.AdvanceWoodCost), economy.ReservedCiv != CivKind.Primitive);
                }
                if (!ExtraCivilisations)
                {
                    Add(rows, "make:advance:" + (int)CivKind.Agrarian, Star(economy, CivKind.Agrarian) + (economy.AgeClockUx ? UiText.T("Reserve: farming", "予約：農耕") : UiText.T("Advance: farming", "時代を進める：農耕")) + cost);
                    Add(rows, "make:advance:" + (int)CivKind.Metallurgy, Star(economy, CivKind.Metallurgy) + (economy.AgeClockUx ? UiText.T("Reserve: metallurgy", "予約：冶金") : UiText.T("Advance: metallurgy", "時代を進める：冶金")) + cost);
                }
                else
                {
                    Add(rows, "make:advance-info", UiText.T("Advance into", "時代を進める：") + cost, false, "", false, true);
                    for (int civIndex = 0; civIndex < AllCivs.Length; civIndex++)
                    {
                        CivKind civ = AllCivs[civIndex];
                    Add(rows, "make:advance:" + (int)civ, Star(economy, civ) + (economy.AgeClockUx ? UiText.T("Reserve: ", "予約：") : "") + CivShortName(civ));
                    }
                }
            }
            else if ((economy.Age == 1 || economy.Age == 2 || (economy.Age == 3 && economy.AgeFour)) && economy.AdvanceRemaining == 0)
            {
                int food = economy.Age == 1 ? economy.Age2FoodCost : economy.Age == 2 ? economy.Age3FoodCost : economy.Age4FoodCost;
                int wood = economy.Age == 1 ? economy.Age2WoodCost : economy.Age == 2 ? economy.Age3WoodCost : economy.Age4WoodCost;
                int gold = economy.NextAgeGoldCost;
                string next = UiText.T("Advance: ", "時代を進める：") + TopBarDisplayText.AgeName(economy.Civ, economy.Age + 1);
                string price = UiText.T(" (", "（食") + food + UiText.T("F ", " 木") + wood + (gold > 0 ? UiText.T("W ", " 金") + gold + UiText.T("G)", "）") : UiText.T("W)", "）"))
                    + AgeClockLabel(economy, food, wood);
                string why = economy.Food < food ? UiText.T("not enough food", "食料が足りない") : economy.Wood < wood ? UiText.T("not enough wood", "木材が足りない") : economy.Gold < gold ? UiText.T("not enough gold", "金が足りない") : economy.VillagerQueued > 0 ? UiText.T("villagers in training", "村人の訓練中") : "";
                Add(rows, economy.AgeClockUx ? "make:advance-now" : "make:advance-age", economy.AgeClockUx ? AgeClockUxUiText.AdvanceNow(food, wood, gold) : next + price, economy.AgeClockUx || string.IsNullOrEmpty(why), why);
            }
        }

        private string AgeClockLabel(EconomyView economy, int food, int wood)
        {
            if (economy.NextAgeClockTick <= 0 || view == null || view.LatestFrame == null) return "";
            long remaining = Math.Max(0, economy.NextAgeClockTick - view.LatestFrame.Tick);
            long seconds = (remaining + 19) / 20;
            string time = (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
            // The price before this label is already what it costs now; the label adds when it becomes free.
            return UiText.T(" (free in ", "（あと ") + time + UiText.T(")", " で自動）");
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
        if (!smith.HasValue) { Add(rows, "research:build-blacksmith", UiText.T("Blacksmith (", "鍛冶場（木材 ") + economy.BlacksmithWoodCost + UiText.T(" wood)", "）") + CraftBuildingCost(economy, BuildingKind.Blacksmith), true, "", false); return; }
            Add(rows, "research:status", !smith.Value.Complete ? UiText.T("Blacksmith: building ", "鍛冶場：建設中 ") + Percent(smith.Value) : smith.Value.Researching != 0 ? UiText.T("Researching: ", "研究中：") + TechName(smith.Value.Researching) + " " + Seconds(smith.Value.ResearchRemaining) : UiText.T("Blacksmith: pick a tech", "鍛冶場：研究を選ぶ"), false, "", false, true);
            var techs = new List<TechKind> { TechKind.Weapons, TechKind.Armour, TechKind.Tools, TechKind.Carts };
            if (economy.Civ == CivKind.Agrarian) techs.Add(TechKind.Irrigation); else if (economy.Civ == CivKind.Metallurgy) techs.Add(TechKind.BlastFurnace);
            if (economy.Age >= 2) { techs.Add(TechKind.SteelWeapons); techs.Add(TechKind.SteelArmour); }
            if (economy.Age >= 3) { techs.Add(TechKind.Masonry); techs.Add(TechKind.Siegecraft); techs.Add(TechKind.Banking); techs.Add(TechKind.GemArmor); }
            if (economy.Age >= 4 && economy.AgeFour) { techs.Add(TechKind.TotalWar); techs.Add(TechKind.GrandWorkshop); }
            for (int techIndex = 0; techIndex < techs.Count; techIndex++)
            {
                TechKind tech = techs[techIndex];
                int bit = (int)tech - 1;
                bool done = (economy.Techs & (1UL << bit)) != 0;
            string cost = bit < economy.TechFoodCosts.Count ? UiText.T(" F", " 食") + economy.TechFoodCosts[bit] + UiText.T(" W", " 木") + economy.TechWoodCosts[bit] + (bit < economy.TechMetalCosts.Count && economy.TechMetalCosts[bit] > 0 ? UiText.T(" M", " 金") + economy.TechMetalCosts[bit] : "") + (bit < economy.TechGemsCosts.Count && economy.TechGemsCosts[bit] > 0 ? UiText.T(" G", " 宝") + economy.TechGemsCosts[bit] : "") + CraftScholarly(economy.ResearchScholarlyCost) : "";
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
