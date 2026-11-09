using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>V3-34.3 inserters. They are one-cell belt components and run after belts and industry.</summary>
    public sealed partial class Simulation
    {
        private bool InsertersOn => IndustryOn && world.Config.Economy.BeltComponents && world.Config.Economy.Inserters;

        private enum InserterSourceKind : byte { None, Building, Belt, Core }

        private void AdvanceInserters()
        {
            if (!InsertersOn) return;
            int interval = world.Config.Economy.InserterIntervalTicks;
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                if (world.Belts[cell].FactionId == 0 || !IsInserterComponent(world.Belts[cell].Component)) continue;
                if (++world.Belts[cell].Progress < interval) continue;

                int reach = world.Belts[cell].Component == BeltComponentKind.LongInserter ? 2 : 1;
                int rear = BeltPrevious(cell, world.Belts[cell].Facing);
                int front = BeltNext(cell, world.Belts[cell].Facing);
                if (reach == 2)
                {
                    rear = rear < 0 ? -1 : BeltPrevious(rear, world.Belts[cell].Facing);
                    front = front < 0 ? -1 : BeltNext(front, world.Belts[cell].Facing);
                }
                if (rear < 0 || front < 0) continue;
                uint faction = world.Belts[cell].FactionId;
                ResourceKind selector = world.Belts[cell].SorterKind;
                InserterSourceKind sourceKind;
                int sourceId;
                ResourceKind item;
                // The core reserve is the automatic economy's rule (34.3); a player's inserter may empty the stock.
                int coreReserve = world.Belts[cell].Held ? 0 : world.Config.Economy.AiInserterCoreReserve;
                if (CarriedInputsOn && !world.Belts[cell].Held && selector != 0) coreReserve = CarriedOutletReserve(faction, selector);
                if (!TryGetInserterSource(rear, faction, selector, coreReserve, out sourceKind, out sourceId, out item)) continue;
                if (!TryDeliverInserter(front, faction, item)) continue;
                ConsumeInserterSource(sourceKind, sourceId, item);
                world.Belts[cell].Progress = 0;
            }
        }

        /// <summary>
        /// V3-34.3 AI: when an existing belt line can carry a completed sawmill's wood, install one
        /// deterministic core-fed inserter at its first cell outside the core. The direct state update
        /// is the automatic-economy equivalent of PlaceInserter and is therefore part of the replayed state.
        /// </summary>
        private void DecideInserters(uint faction)
        {
            if (!InsertersOn || faction == 0 || faction > world.Economies.Length) return;
            if (CarriedInputsOn) DecideCarriedSupplyRoutes(faction);
            if (AiInserterPartsOn) DecideAutomaticSplitterPriority(faction);
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            if (HasCompleteSawmill(faction) && economy.Wood > rules.AiInserterCoreReserve + rules.InserterWoodCost)
            {
                for (int cell = 0; cell < world.Belts.Length; cell++)
                {
                    ref var belt = ref world.Belts[cell];
                    // A belt the player holds is never rebuilt by the automatic economy.
                    if (belt.FactionId != faction || belt.Held || belt.Component != BeltComponentKind.None || belt.Item != 0) continue;
                    int rear = BeltPrevious(cell, belt.Facing), front = BeltNext(cell, belt.Facing);
                    if (rear < 0 || front < 0 || !(FeedsOwnCore(rear, faction)
                        || CarriedInputsOn && FeedsCompletedTown(rear, faction))) continue;
                    if (!BeltCanReceive(front, faction) && !CanFeedSawmill(front, faction)) continue;
                    if (world.Economies[faction - 1].Age >= 2 && CraftDepthOn && economy.Fittings < 1) continue;

                    belt.Component = BeltComponentKind.Inserter;
                    belt.SorterKind = ResourceKind.Wood;
                    belt.Progress = 0;
                    economy.Wood -= rules.InserterWoodCost;
                    if (world.Economies[faction - 1].Age >= 2 && CraftDepthOn)
                    {
                        economy.Fittings--; economy.FittingsConsumed++;
                    }
                    InvalidateBeltCaches();
                    return;
                }
            }
            if (AiInserterPartsOn && DecideAiProcessingInserter(faction)) return;
        }

        private void DecideAutomaticSplitterPriority(uint faction)
        {
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                ref var splitter = ref world.Belts[cell];
                if (splitter.FactionId != faction || splitter.Held || splitter.Component != BeltComponentKind.Splitter) continue;
                int forward = BeltNext(cell, splitter.Facing);
                int side = BeltNext(cell, TurnLeft(splitter.Facing));
                bool forwardIndustry = AiBranchReaches(faction, forward, true);
                bool sideIndustry = AiBranchReaches(faction, side, true);
                bool forwardReceiver = AiBranchReaches(faction, forward, false);
                bool sideReceiver = AiBranchReaches(faction, side, false);
                BeltPriority wanted = sideIndustry && !forwardIndustry ? BeltPriority.Left
                    : forwardIndustry && !sideIndustry ? BeltPriority.Right : BeltPriority.None;
                if (wanted != BeltPriority.None && splitter.Priority != wanted)
                {
                    splitter.Priority = wanted;
                    if (AiBeltPartsOn && faction <= 2) aiBeltDiagnostics[faction - 1].InserterPriorityPlaced++;
                    return;
                }
            }
        }

        private bool AiBranchReaches(uint faction, int cell, bool industry)
        {
            int limit = Math.Max(16, world.Config.Economy.AiMergeMaxCells);
            for (int step = 0; step < limit && cell >= 0 && cell < world.Belts.Length; step++)
            {
                if (AiCellHasBuilding(cell, faction, industry)) return true;
                if (industry && AiCellOnProcessingLine(cell, faction)) return true;
                if (!industry && (FeedsOwnCore(cell, faction) || AiCellHasReceiver(cell, faction))) return true;
                var belt = world.Belts[cell];
                if (belt.FactionId != faction || belt.Held) return false;
                int next = AiInserterPathNext(cell, belt);
                if (next == cell) return false;
                cell = next;
            }
            return false;
        }

        private bool AiCellOnProcessingLine(int cell, uint faction)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction || line.Manager != LineManager.Automatic || AiProcessingBuildingId(line) == 0) continue;
                for (int j = 0; j < line.BeltCells.Length; j++)
                    if (line.BeltCells[j] == cell) return true;
            }
            return false;
        }

        private bool AiCellHasBuilding(int cell, uint faction, bool industry)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.FactionId != faction || !CellInBuilding(building, cell)) continue;
                return industry ? IsAiProcessingBuilding(building.Kind) : AiCellHasReceiverKind(building.Kind);
            }
            return false;
        }

        private static bool IsAiProcessingBuilding(BuildingKind kind)
            => HasIndustryOutput(kind) || kind == BuildingKind.Assembler || kind == BuildingKind.Smelter || kind == BuildingKind.Metalworks
                || kind == BuildingKind.RopeHut || kind == BuildingKind.GearWorkshop || kind == BuildingKind.CharcoalKiln
                || kind == BuildingKind.Steelworks || kind == BuildingKind.Fletcher || kind == BuildingKind.Mill
                || kind == BuildingKind.Saddlery || kind == BuildingKind.Stonecutter || kind == BuildingKind.Joinery
                || kind == BuildingKind.BrickKiln || kind == BuildingKind.Scriptorium || kind == BuildingKind.DryingRack
                || kind == BuildingKind.Lapidary || kind == BuildingKind.Weaver || kind == BuildingKind.IncenseHouse
                || kind == BuildingKind.CharmWorkshop || kind == BuildingKind.RepairYard || kind == BuildingKind.ToolShop;

        private static bool AiCellHasReceiverKind(BuildingKind kind)
            => kind == BuildingKind.DropSite || kind == BuildingKind.Storage || kind == BuildingKind.Town;

        private bool AiCellHasReceiver(int cell, uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (IsActualBuilding(building) && building.Complete && building.FactionId == faction
                    && AiCellHasReceiverKind(building.Kind) && CellInBuilding(building, cell)) return true;
            }
            return false;
        }

        private bool DecideAiProcessingInserter(uint faction)
        {
            var diagnostic = AiBeltPartsOn && faction <= 2 ? aiBeltDiagnostics[faction - 1] : null;
            for (int lineIndex = 0; lineIndex < world.ProcessingLines.Length; lineIndex++)
            {
                var line = world.ProcessingLines[lineIndex];
                if (line.FactionId != faction || line.Manager != LineManager.Automatic || line.BeltCells == null || line.BeltCells.Length == 0) continue;
                uint buildingId = AiProcessingBuildingId(line);
                if (buildingId == 0 || buildingId > world.BuildingCount) continue;
                var building = world.Buildings[buildingId - 1];
                if (!IsActualBuilding(building) || !building.Complete || !TryAiProcessingMaterial(building, out var material)) continue;
                bool lineReachesBuilding = false;
                for (int i = 0; i < line.BeltCells.Length; i++)
                    if (CellInBuilding(building, line.BeltCells[i])) { lineReachesBuilding = true; break; }
                if (AiLineHasInserter(line, material)) continue;

                int normalCell = -1, longCell = -1;
                for (int i = 0; i < line.BeltCells.Length; i++)
                {
                    int cell = line.BeltCells[i];
                    if (cell < 0 || cell >= world.Belts.Length) continue;
                    var belt = world.Belts[cell];
                    if (belt.FactionId != faction) continue;
                    int rear = BeltPrevious(cell, belt.Facing);
                    if (rear < 0 || !FeedsOwnCore(rear, faction)) continue;
                    int front = BeltNext(cell, belt.Facing);
                    if (belt.Component == BeltComponentKind.None && belt.Item == 0
                        && (lineReachesBuilding || AiBeltPathReachesBuilding(front, belt.Facing, faction, building))) { normalCell = cell; break; }
                    int candidate = front;
                    if (candidate >= 0 && candidate < world.Belts.Length)
                    {
                        var longBelt = world.Belts[candidate];
                        int longFront = BeltNext(candidate, belt.Facing);
                        if (longBelt.FactionId == faction && !longBelt.Held && longBelt.Component == BeltComponentKind.None
                            && longBelt.Item == 0 && (lineReachesBuilding || AiBeltPathReachesBuilding(longFront, belt.Facing, faction, building))) longCell = candidate;
                    }
                }

                var d = diagnostic;
                bool hasCandidate = normalCell >= 0 || longCell >= 0;
                bool useLong = normalCell < 0 && longCell >= 0;
                if (!hasCandidate)
                {
                    if (d != null) { d.InserterFilterNoLine++; d.InserterFilterNoSpace++; d.InserterLongNoLine++; d.InserterLongNoSpace++; }
                    continue;
                }
                if (d != null)
                {
                    if (useLong) d.InserterLongConditions++;
                    else d.InserterFilterConditions++;
                }
                int stock = StockOf(world.Economies[faction - 1], material);
                int requiredStock = world.Config.Economy.AiInserterCoreReserve + world.Config.Economy.AiInserterPartsMinStock;
                if (stock < requiredStock)
                {
                    if (d != null)
                    {
                        if (useLong) d.InserterLongLowStock++;
                        else d.InserterFilterLowStock++;
                    }
                    continue;
                }
                int cellToUse = normalCell >= 0 ? normalCell : longCell;
                BeltComponentKind component = normalCell >= 0 ? BeltComponentKind.FilterInserter : BeltComponentKind.LongInserter;
                Facing facing = world.Belts[cellToUse].Facing;
                if (!LayAutomaticInserterPart(faction, cellToUse, facing, material, component))
                {
                    if (d != null)
                    {
                        if (component == BeltComponentKind.LongInserter) d.InserterLongUnaffordable++;
                        else d.InserterFilterUnaffordable++;
                    }
                    continue;
                }
                if (d != null)
                {
                    if (component == BeltComponentKind.LongInserter) d.InserterLongPlaced++;
                    else d.InserterFilterPlaced++;
                }
                return true;
            }
            for (int buildingIndex = 0; buildingIndex < world.BuildingCount; buildingIndex++)
            {
                var building = world.Buildings[buildingIndex];
                if (!IsActualBuilding(building) || !building.Complete || building.FactionId != faction
                    || !IsAiProcessingBuilding(building.Kind) || !IsAutomaticProcessingBuilding(faction, building.Id)
                    || !TryAiProcessingMaterial(building, out var material)) continue;
                for (int cell = 0; cell < world.Belts.Length; cell++)
                {
                    var belt = world.Belts[cell];
                    if (belt.FactionId != faction) continue;
                    int rear = BeltPrevious(cell, belt.Facing);
                    if (rear < 0 || !FeedsOwnCore(rear, faction)) continue;
                    int front = BeltNext(cell, belt.Facing);
                    int placeCell = -1;
                    BeltComponentKind component = BeltComponentKind.FilterInserter;
                    if (belt.Component == BeltComponentKind.None && belt.Item == 0
                        && AiBeltPathReachesBuilding(front, belt.Facing, faction, building)) placeCell = cell;
                    else if (front >= 0 && front < world.Belts.Length)
                    {
                        var longBelt = world.Belts[front];
                        int longFront = BeltNext(front, belt.Facing);
                        if (!longBelt.Held && longBelt.Component == BeltComponentKind.None && longBelt.Item == 0
                            && AiBeltPathReachesBuilding(longFront, belt.Facing, faction, building))
                        {
                            placeCell = front;
                            component = BeltComponentKind.LongInserter;
                        }
                    }
                    if (placeCell < 0) continue;
                    int stock = StockOf(world.Economies[faction - 1], material);
                    int requiredStock = world.Config.Economy.AiInserterCoreReserve + world.Config.Economy.AiInserterPartsMinStock;
                    if (stock < requiredStock)
                    {
                        if (diagnostic != null)
                        {
                            if (component == BeltComponentKind.LongInserter) diagnostic.InserterLongLowStock++;
                            else diagnostic.InserterFilterLowStock++;
                        }
                        continue;
                    }
                    if (!LayAutomaticInserterPart(faction, placeCell, world.Belts[placeCell].Facing, material, component)) continue;
                    if (component == BeltComponentKind.LongInserter)
                    {
                        if (diagnostic != null) { diagnostic.InserterLongConditions++; diagnostic.InserterLongPlaced++; }
                    }
                    else
                    {
                        if (diagnostic != null) { diagnostic.InserterFilterConditions++; diagnostic.InserterFilterPlaced++; }
                    }
                    return true;
                }
            }
            return false;
        }

        private bool IsAutomaticProcessingBuilding(uint faction, uint buildingId)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction || line.Manager != LineManager.Automatic) continue;
                if (AiProcessingBuildingId(line) == buildingId) return true;
            }
            return false;
        }

        private bool AiBeltPathReachesBuilding(int start, Facing facing, uint faction, BuildingState target)
        {
            int limit = Math.Max(16, world.Config.Economy.AiMergeMaxCells);
            int cell = start;
            for (int step = 0; step < limit && cell >= 0 && cell < world.Belts.Length; step++)
            {
                if (CellInBuilding(target, cell)) return true;
                var belt = world.Belts[cell];
                if (belt.FactionId != faction || belt.Held) return false;
                int next = AiInserterPathNext(cell, belt);
                if (next == cell) return false;
                cell = next;
            }
            return CellInBuilding(target, cell);
        }

        private int AiInserterPathNext(int cell, BeltState belt)
            => IsInserterComponent(belt.Component) ? BeltNext(cell, belt.Facing) : BeltPrimaryNext(cell, belt);

        private static uint AiProcessingBuildingId(ProcessingLineState line)
            => line.SawmillId != 0 ? line.SawmillId : line.AssemblerId != 0 ? line.AssemblerId : line.SmelterId != 0 ? line.SmelterId
                : line.MetalworksId != 0 ? line.MetalworksId : line.RopeHutId != 0 ? line.RopeHutId : line.GearWorkshopId != 0 ? line.GearWorkshopId
                : line.KilnId != 0 ? line.KilnId : line.SteelworksId != 0 ? line.SteelworksId : line.StonecutterId != 0 ? line.StonecutterId
                : line.JoineryId != 0 ? line.JoineryId : line.BrickKilnId != 0 ? line.BrickKilnId : line.ScriptoriumId != 0 ? line.ScriptoriumId
                : line.DryingRackId != 0 ? line.DryingRackId : line.MillId != 0 ? line.MillId : line.LapidaryId != 0 ? line.LapidaryId : line.WeaverId != 0 ? line.WeaverId
                : line.IncenseHouseId != 0 ? line.IncenseHouseId : line.CharmWorkshopId != 0 ? line.CharmWorkshopId : line.RepairYardId != 0 ? line.RepairYardId
                : line.ToolShopId;

        private bool TryAiProcessingMaterial(BuildingState building, out ResourceKind material)
        {
            material = 0;
            switch (building.Kind)
            {
                case BuildingKind.Sawmill: material = ResourceKind.Wood; break;
                case BuildingKind.Smelter: material = ResourceKind.Ore; break;
                case BuildingKind.Metalworks: material = ResourceKind.Metal; break;
                case BuildingKind.RopeHut: case BuildingKind.CharcoalKiln: case BuildingKind.IncenseHouse:
                case BuildingKind.CharmWorkshop: case BuildingKind.RepairYard: case BuildingKind.ToolShop: material = ResourceKind.Wood; break;
                case BuildingKind.GearWorkshop: case BuildingKind.Steelworks: material = ResourceKind.Metal; break;
                case BuildingKind.Stonecutter: material = ResourceKind.Stone; break;
                case BuildingKind.Joinery: material = ResourceKind.Planks; break;
                case BuildingKind.BrickKiln: material = ResourceKind.Stone; break;
                case BuildingKind.Scriptorium: material = ResourceKind.Gold; break;
                case BuildingKind.DryingRack: case BuildingKind.Mill: material = ResourceKind.Food; break;
                case BuildingKind.Lapidary: material = ResourceKind.Stone; break;
                case BuildingKind.Weaver: material = ResourceKind.Wood; break;
                case BuildingKind.Assembler:
                    material = building.Recipe == RecipeKind.SiegeParts ? ResourceKind.Frames
                        : building.Recipe == RecipeKind.Mechanism ? ResourceKind.ArmsKit
                        : building.Recipe == RecipeKind.SiegeEngine ? ResourceKind.SiegeParts
                        : building.Recipe == RecipeKind.EliteKit ? ResourceKind.ArmsKit : ResourceKind.Planks;
                    break;
                default: return false;
            }
            return material != 0;
        }

        private bool AiLineHasInserter(ProcessingLineState line, ResourceKind material)
        {
            for (int i = 0; i < line.BeltCells.Length; i++)
            {
                var belt = world.Belts[line.BeltCells[i]];
                if ((belt.Component == BeltComponentKind.FilterInserter || belt.Component == BeltComponentKind.LongInserter)
                    && (belt.SorterKind == 0 || belt.SorterKind == material)) return true;
            }
            return false;
        }

        private bool LayAutomaticInserterPart(uint faction, int cell, Facing facing, ResourceKind material, BeltComponentKind component)
        {
            if (!AiInserterPartsOn || (component != BeltComponentKind.FilterInserter && component != BeltComponentKind.LongInserter)
                || cell < 0 || cell >= world.Belts.Length) return false;
            ref var belt = ref world.Belts[cell];
            if (belt.FactionId != faction || belt.Held || belt.Component != BeltComponentKind.None || belt.Item != 0 || belt.Facing != facing) return false;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int gearCost = component == BeltComponentKind.FilterInserter ? rules.FilterInserterGears : rules.LongInserterGears;
            int fittingCost = CraftDepthOn && economy.Age >= 2 ? 1 : 0;
            if (economy.Wood < rules.InserterWoodCost || economy.Gears < gearCost || economy.Fittings < fittingCost) return false;
            economy.Wood = checked(economy.Wood - rules.InserterWoodCost);
            RecordLargeUse(faction, ResourceKind.Wood, LargeEconomyUseKind.Belt, rules.InserterWoodCost);
            economy.Gears = checked(economy.Gears - gearCost); economy.GearsConsumed = checked(economy.GearsConsumed + gearCost);
            if (fittingCost > 0)
            {
                economy.Fittings--; economy.FittingsConsumed = checked(economy.FittingsConsumed + fittingCost);
                RecordCraftTraceResourceUse(faction, ResourceKind.Fittings, CraftTraceUseCost, fittingCost);
            }
            belt.Component = component; belt.SorterKind = material; belt.Progress = 0;
            RecordAiBeltPartPlaced(faction, component);
            InvalidateBeltCaches();
            return true;
        }

        private bool HasCompleteSawmill(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (IsActualBuilding(building) && building.Complete && building.FactionId == faction && building.Kind == BuildingKind.Sawmill)
                    return true;
            }
            return false;
        }

        private bool CanFeedSawmill(int cell, uint faction)
        {
            int width = world.Config.Map.WidthCells;
            int x = cell % width, z = cell / width;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.FactionId != faction || building.Kind != BuildingKind.Sawmill) continue;
                int size = SizeOf(building.Kind), x0 = building.OriginCell % width, z0 = building.OriginCell / width;
                if (x >= x0 && x < x0 + size && z >= z0 && z < z0 + size
                    && building.Input < world.Config.Economy.BufferLimit)
                    return true;
            }
            return false;
        }

        private int BeltPrevious(int cell, Facing facing)
        {
            switch (facing)
            {
                case Facing.North: return cell / world.Config.Map.WidthCells > 0 ? cell - world.Config.Map.WidthCells : -1;
                case Facing.East: return cell % world.Config.Map.WidthCells > 0 ? cell - 1 : -1;
                case Facing.South: return cell / world.Config.Map.WidthCells + 1 < world.Config.Map.HeightCells ? cell + world.Config.Map.WidthCells : -1;
                default: return cell % world.Config.Map.WidthCells + 1 < world.Config.Map.WidthCells ? cell + 1 : -1;
            }
        }

        private bool TryGetInserterSource(int rear, uint faction, ResourceKind selector, int coreReserve,
            out InserterSourceKind sourceKind, out int sourceId, out ResourceKind item)
        {
            sourceKind = InserterSourceKind.None;
            sourceId = 0;
            item = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.FactionId != faction || !CellInBuilding(building, rear)) continue;
                ResourceKind kind = building.Kind == BuildingKind.Storage ? building.StorageKind
                    : building.Kind == BuildingKind.Drill && building.NodeId > 0 && building.NodeId <= world.Nodes.Length
                        ? world.Nodes[building.NodeId - 1].Definition.Kind
                        : OutputKind(building.Kind);
                int count = building.Kind == BuildingKind.Storage ? building.StorageCount : building.Output;
                if (count <= 0 || !SelectorMatches(selector, kind)) continue;
                sourceKind = InserterSourceKind.Building;
                sourceId = i + 1;
                item = kind;
                return true;
            }
            if (rear >= 0 && rear < world.Belts.Length)
            {
                var belt = world.Belts[rear];
                if (belt.FactionId == faction && !IsInserterComponent(belt.Component) && belt.Item != 0
                    && SelectorMatches(selector, belt.Item))
                {
                    sourceKind = InserterSourceKind.Belt;
                    sourceId = rear;
                    item = belt.Item;
                    return true;
                }
            }
            if (selector != 0 && (FeedsOwnCore(rear, faction)
                || CarriedInputsOn && FeedsCompletedTown(rear, faction))
                && StockOf(world.Economies[faction - 1], selector) > coreReserve)
            {
                sourceKind = InserterSourceKind.Core;
                sourceId = (int)faction;
                item = selector;
                return true;
            }
            return false;
        }

        private bool TryDeliverInserter(int front, uint faction, ResourceKind item)
        {
            if (front >= 0 && front < world.Belts.Length)
            {
                ref var belt = ref world.Belts[front];
                if (belt.FactionId == faction && BeltCanReceiveLoad(belt, item))
                {
                    PutOnBelt(ref belt, item, 1);
                    return true;
                }
            }
            if (TryFeedBuilding(front, faction, item)) return true;
            // Processing targets share the same acceptance table used by villager hauling. Keep this
            // fallback for a component cell whose destination is a building footprint rather than a belt.
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var building = ref world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.FactionId != faction || !CellInBuilding(building, front)) continue;
                if (building.Kind == BuildingKind.Smelter && item == ResourceKind.Ore
                    && building.Input < world.Config.Economy.BufferLimit)
                {
                    building.Input++;
                    return true;
                }
                if (PutIntoProcessingTarget(ref building, item, 1) > 0) return true;
            }
            if (FeedsOwnCore(front, faction) || CarriedInputsOn && FeedsCompletedTown(front, faction))
            {
                AddStock(faction, item, 1);
                return true;
            }
            return false;
        }

        private void ConsumeInserterSource(InserterSourceKind sourceKind, int sourceId, ResourceKind item)
        {
            if (sourceKind == InserterSourceKind.Belt)
            {
                TakeFromBelt(ref world.Belts[sourceId], 1);
                return;
            }
            if (sourceKind == InserterSourceKind.Core)
            {
                SpendStock((uint)sourceId, item, 1);
                return;
            }
            ref var building = ref world.Buildings[sourceId - 1];
            if (building.Kind == BuildingKind.Storage)
            {
                building.StorageCount--;
                building.Input = building.StorageCount;
                building.Output = building.StorageCount;
                if (building.StorageCount == 0) building.StorageKind = 0;
            }
            else building.Output--;
        }

        private bool CellInBuilding(BuildingState building, int cell)
        {
            int width = world.Config.Map.WidthCells;
            int size = SizeOf(building.Kind);
            int x = cell % width, z = cell / width;
            int x0 = building.OriginCell % width, z0 = building.OriginCell / width;
            return x >= x0 && x < x0 + size && z >= z0 && z < z0 + size;
        }

        private static bool SelectorMatches(ResourceKind selector, ResourceKind item)
            => selector == 0 || selector == item;

        private void SpendStock(uint faction, ResourceKind kind, int amount)
        {
            ref var economy = ref world.Economies[faction - 1];
            switch (kind)
            {
                case ResourceKind.Food: economy.Food -= amount; break;
                case ResourceKind.Wood: economy.Wood -= amount; break;
                case ResourceKind.Stone: economy.Stone -= amount; break;
                case ResourceKind.Ore: economy.Ore -= amount; break;
                case ResourceKind.Metal:
                    economy.Metal -= amount;
                    if (AiFactoryOn) economy.MetalConsumed = checked(economy.MetalConsumed + amount);
                    break;
                case ResourceKind.Gems: economy.Gems -= amount; break;
                case ResourceKind.Gold: economy.Gold -= amount; break;
                case ResourceKind.Charcoal: economy.Charcoal -= amount; break;
                case ResourceKind.Sand: economy.Sand -= amount; break;
                case ResourceKind.Glass: economy.Glass -= amount; economy.GlassConsumed = checked(economy.GlassConsumed + amount); break;
                case ResourceKind.Lens: economy.Lens -= amount; economy.LensConsumed = checked(economy.LensConsumed + amount); break;
                case ResourceKind.Precision: economy.Precision -= amount; economy.PrecisionConsumed = checked(economy.PrecisionConsumed + amount); break;
                case ResourceKind.Steel: economy.Steel -= amount; break;
                case ResourceKind.BowGear: economy.BowGear -= amount; break;
                case ResourceKind.Gears: economy.Gears -= amount; economy.GearsConsumed = checked(economy.GearsConsumed + amount); break;
                case ResourceKind.Rations: economy.Rations -= amount; break;
                case ResourceKind.Bricks: economy.Bricks -= amount; break;
                case ResourceKind.Tack: economy.Tack -= amount; break;
                case ResourceKind.Planks: economy.Planks -= amount; break;
                case ResourceKind.Books: economy.Books -= amount; break;
                case ResourceKind.Incense: economy.Incense -= amount; break;
                case ResourceKind.Charms: economy.Charms -= amount; break;
                case ResourceKind.RepairKits: economy.RepairKits -= amount; break;
                case ResourceKind.Tools: economy.Tools -= amount; break;
                case ResourceKind.Fittings: economy.Fittings -= amount; economy.FittingsConsumed = checked(economy.FittingsConsumed + amount); RecordCraftTraceResourceUse(faction, ResourceKind.Fittings, CraftTraceUseCost, amount); break;
                case ResourceKind.Rope: economy.Rope -= amount; economy.RopeConsumed = checked(economy.RopeConsumed + amount); RecordCraftTraceResourceUse(faction, ResourceKind.Rope, CraftTraceUseCost, amount); break;
                case ResourceKind.CutStone: economy.CutStone -= amount; break;
                case ResourceKind.Frames: economy.Frames -= amount; break;
                case ResourceKind.ArmsKit: economy.ArmsKit -= amount; break;
                case ResourceKind.Scholarly: economy.Scholarly -= amount; break;
                case ResourceKind.FineArmsKit: economy.FineArmsKit -= amount; break;
                case ResourceKind.SiegeParts: economy.SiegeParts -= amount; break;
                case ResourceKind.Mechanism: economy.Mechanism -= amount; break;
            }
        }
    }
}
