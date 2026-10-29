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
                if (world.Belts[cell].FactionId == 0 || world.Belts[cell].Component != BeltComponentKind.Inserter) continue;
                if (++world.Belts[cell].Progress < interval) continue;

                int rear = BeltPrevious(cell, world.Belts[cell].Facing);
                int front = BeltNext(cell, world.Belts[cell].Facing);
                if (rear < 0 || front < 0) continue;
                uint faction = world.Belts[cell].FactionId;
                ResourceKind selector = world.Belts[cell].SorterKind;
                InserterSourceKind sourceKind;
                int sourceId;
                ResourceKind item;
                // The core reserve is the automatic economy's rule (34.3); a player's inserter may empty the stock.
                int coreReserve = world.Belts[cell].Held ? 0 : world.Config.Economy.AiInserterCoreReserve;
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
            if (!HasCompleteSawmill(faction)) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Wood <= rules.AiInserterCoreReserve + rules.InserterWoodCost) return;

            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                ref var belt = ref world.Belts[cell];
                // A belt the player holds is never rebuilt by the automatic economy.
                if (belt.FactionId != faction || belt.Held || belt.Component != BeltComponentKind.None || belt.Item != 0) continue;
                int rear = BeltPrevious(cell, belt.Facing), front = BeltNext(cell, belt.Facing);
                if (rear < 0 || front < 0 || !FeedsOwnCore(rear, faction)) continue;
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
                if (belt.FactionId == faction && belt.Component != BeltComponentKind.Inserter && belt.Item != 0
                    && SelectorMatches(selector, belt.Item))
                {
                    sourceKind = InserterSourceKind.Belt;
                    sourceId = rear;
                    item = belt.Item;
                    return true;
                }
            }
            if (selector != 0 && FeedsOwnCore(rear, faction) && StockOf(world.Economies[faction - 1], selector) > coreReserve)
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
                if (belt.FactionId == faction && belt.Component != BeltComponentKind.Inserter && belt.Item == 0)
                {
                    belt.Item = item;
                    belt.Progress = 0;
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
            if (FeedsOwnCore(front, faction))
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
                world.Belts[sourceId].Item = 0;
                world.Belts[sourceId].Progress = 0;
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
                case ResourceKind.Steel: economy.Steel -= amount; break;
                case ResourceKind.BowGear: economy.BowGear -= amount; break;
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
