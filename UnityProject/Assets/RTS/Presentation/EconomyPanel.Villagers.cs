using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    public sealed partial class EconomyPanel
    {
        private const float VillagerOrderPickRadiusPixels = 54f;
        private int placementCancelledFrame = -1;

        public bool HasSelectedVillagers { get { return layer != null && layer.HasVillagerSelection; } }

        public string SelectedVillagerSummary
        {
            get
            {
                var economy = Economy();
                if (layer == null || economy == null || layer.SelectedVillagers.Count == 0)
                    return UiText.T("No villagers selected", "村人を選択していません");

                int idle = 0, build = 0, haul = 0, trade = 0, gather = 0;
                var gatherKinds = new Dictionary<ResourceKind, int>();
                int present = 0;
                foreach (var villager in economy.Villagers)
                {
                    if (!villager.IsOwn || !layer.SelectedVillagers.Contains(villager.Id)) continue;
                    present++;
                    switch (villager.Activity)
                    {
                        case VillagerActivity.Idle: idle++; break;
                        case VillagerActivity.ToBuild:
                        case VillagerActivity.Building: build++; break;
                        case VillagerActivity.Hauling: haul++; break;
                        case VillagerActivity.Trading: trade++; break;
                        case VillagerActivity.ToResource:
                        case VillagerActivity.Gathering:
                        case VillagerActivity.Returning:
                            gather++;
                            ResourceKind kind = villager.Carry > 0 ? villager.CarryKind : NearestResourceKind(villager, economy);
                            if (kind != 0)
                            {
                                gatherKinds.TryGetValue(kind, out int count);
                                gatherKinds[kind] = count + 1;
                            }
                            break;
                        default: idle++; break;
                    }
                }

                if (present == 0) return UiText.T("No villagers selected", "村人を選択していません");
                var parts = new List<string>();
                if (gather > 0)
                {
                    var resources = new List<string>();
                    AddResourceCount(resources, gatherKinds, ResourceKind.Food, UiText.T("food", "食料"));
                    AddResourceCount(resources, gatherKinds, ResourceKind.Wood, UiText.T("wood", "木材"));
                    AddResourceCount(resources, gatherKinds, ResourceKind.Stone, UiText.T("stone", "石"));
                    AddResourceCount(resources, gatherKinds, ResourceKind.Ore, UiText.T("ore", "鉱石"));
                    AddResourceCount(resources, gatherKinds, ResourceKind.Metal, UiText.T("metal", "金属"));
                    parts.Add(UiText.T("gathering", "採集") + (resources.Count == 0 ? " " + gather : "：" + string.Join(UiText.T(", ", "・"), resources)));
                }
                if (build > 0) parts.Add(UiText.T("building ", "建設 ") + build);
                if (haul > 0) parts.Add(UiText.T("hauling ", "運搬 ") + haul);
                if (trade > 0) parts.Add(UiText.T("trade route ", "交易 ") + trade);
                if (idle > 0) parts.Add(UiText.T("idle ", "待機 ") + idle);
                string detail = parts.Count == 0 ? UiText.T("job unknown", "仕事不明") : string.Join(UiText.T(", ", "・"), parts);
                return UiText.T("Selected villagers: ", "選択中の村人：") + present + UiText.T(" (", "（") + detail + "）";
            }
        }

        public void SelectIdleVillagersFromPanel()
        {
            if (layer == null) return;
            layer.SelectIdleVillagers();
            if (layer.HasVillagerSelection && view != null) view.Select(SelectionTarget.None);
            Note(UiText.T("Selected idle villagers: ", "待機中の村人を選択しました：") + layer.SelectedVillagers.Count);
        }

        /// <summary>ReturnToAuto is faction-wide in Contracts, so the label and notice say exactly what it does.</summary>
        public void ReturnSelectedVillagersToAuto()
        {
            if (layer == null || layer.SelectedVillagers.Count == 0)
            {
                Note(UiText.T("No villagers selected.", "村人を選択していません。"));
                return;
            }
            Send(EconomyCommand.ReturnToAuto(faction, ++sequence),
                UiText.T("All held economy work returned to auto (selected villagers included)", "選択中を含む、手動で触った内政全体をお任せに戻しました"));
        }

        /// <summary>Right-click translates only map targets into the existing economy commands.</summary>
        public bool TryIssueSelectedVillagerOrder(Camera camera, Vector2 screenPoint)
        {
            if (layer == null || layer.SelectedVillagers.Count == 0) return false;
            // While a building is being placed, the right button cancels the placement and must not also send an order.
            if (IsPlacing || placementCancelledFrame == Time.frameCount) return false;
            var economy = Economy();
            if (economy == null || port == null) return true;

            if (layer.TryPickResource(camera, screenPoint, VillagerOrderPickRadiusPixels, out var resource))
            {
                Send(EconomyCommand.Assign(faction, ++sequence, layer.SelectedVillagers,
                    EconomyTargetKind.ResourceNode, resource.Id),
                    SelectedCountNote() + UiText.T(" -> ", " → ") + ResourceName(resource.Kind));
                return true;
            }

            if (layer.TryPickOwnBuilding(camera, screenPoint, VillagerOrderPickRadiusPixels, out var building))
            {
                if (!building.Complete)
                {
                    Send(EconomyCommand.Assign(faction, ++sequence, layer.SelectedVillagers,
                        EconomyTargetKind.Building, building.Id),
                        SelectedCountNote() + UiText.T(" -> construction", " → 建設"));
                    return true;
                }

                if (building.Kind == BuildingKind.Market)
                {
                    Send(EconomyCommand.TradeRoute(faction, ++sequence, layer.SelectedVillagers),
                        SelectedCountNote() + UiText.T(" -> trade route", " → 交易路"));
                    return true;
                }

                if (building.Kind == BuildingKind.Caravanserai)
                {
                    if (!VillagerControlRules.CanUseCaravanserai(building.Complete, building.CaravanMarketId, building.CaravanOutpostId))
                    {
                        Note(UiText.T("The caravanserai is not linked to a usable market/outpost.", "隊商宿は使える市場・拠点とつながっていません。"));
                        return true;
                    }
                    Send(EconomyCommand.CaravanRoute(faction, ++sequence, building.Id, layer.SelectedVillagers),
                        SelectedCountNote() + UiText.T(" -> caravan route", " → 隊商路"));
                    return true;
                }

                if (VillagerControlRules.IsHaulSource(building.Kind))
                {
                    Send(EconomyCommand.Assign(faction, ++sequence, layer.SelectedVillagers,
                        EconomyTargetKind.Building, building.Id),
                        SelectedCountNote() + UiText.T(" -> haul from ", " → 運搬：") + EconomyLayer.BuildingName(building.Kind));
                    return true;
                }

                Note(UiText.T("Villagers cannot be assigned to that finished building.", "その完成した建物には村人を割り当てられません。"));
                return true;
            }

            Note(UiText.T("Villagers have no ground-walk command.", "地面へ歩かせる命令はありません。"));
            return true;
        }

        private string SelectedCountNote()
        {
            return layer.SelectedVillagers.Count + UiText.T(" villagers", "人の村人");
        }

        private static string ResourceName(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Food: return UiText.T("food", "食料");
                case ResourceKind.Wood: return UiText.T("wood", "木材");
                case ResourceKind.Stone: return UiText.T("stone", "石");
                case ResourceKind.Ore: return UiText.T("ore", "鉱石");
                case ResourceKind.Metal: return UiText.T("metal", "金属");
                case ResourceKind.Rations: return UiText.T("rations", "糧食");
                case ResourceKind.Bricks: return UiText.T("bricks", "煉瓦");
                default: return kind.ToString();
            }
        }

        private static void AddResourceCount(List<string> parts, Dictionary<ResourceKind, int> counts, ResourceKind kind, string name)
        {
            if (counts.TryGetValue(kind, out int count) && count > 0) parts.Add(name + count);
        }

        private static ResourceKind NearestResourceKind(VillagerView villager, EconomyView economy)
        {
            Vector3 position = EconomyLayer.ToWorld(villager.Position);
            float best = float.MaxValue;
            ResourceKind result = 0;
            foreach (var resource in economy.Resources)
            {
                float distance = (EconomyLayer.ToWorld(resource.Position) - position).sqrMagnitude;
                if (distance < best) { best = distance; result = resource.Kind; }
            }
            return result;
        }
    }
}
