using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-2 mines and smelters (technical-design-v3 12.2). A mine stands on an ore point and digs it on its own; a smelter
    /// turns ore into metal. Each has an input and an output of BufferLimit items. Belts feed a building by pointing into
    /// its footprint; a building puts its output onto the own empty belt on the cell just outside the middle of the side it
    /// faces. Nothing here runs without industry.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Economy step, right after the belts: dig, smelt, then hand outputs to the belts.</summary>
        private void AdvanceIndustry()
        {
            if (!IndustryOn) return;
            var rules = world.Config.Economy;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || !b.Complete) continue;
                if (b.Kind == BuildingKind.Mine)
                {
                    ref var node = ref world.Nodes[b.NodeId - 1];
                    if (node.Remaining <= 0 || b.Output >= rules.BufferLimit) continue;
                    if (++b.Timer < rules.MineIntervalTicks) continue;
                    b.Timer = 0;
                    node.Remaining--;
                    b.Output++;
                }
                else if (b.Kind == BuildingKind.LumberCamp)
                {
                    ref var node = ref world.Nodes[b.NodeId - 1];
                    if (node.Remaining <= 0 || b.Output >= rules.BufferLimit) continue;
                    if (++b.Timer < rules.LumberCampIntervalTicks) continue;
                    b.Timer = 0;
                    node.Remaining--;
                    b.Output++;
                }
                else if (b.Kind == BuildingKind.Quarry)
                {
                    ref var node = ref world.Nodes[b.NodeId - 1];
                    if (node.Remaining <= 0 || b.Output >= rules.BufferLimit) continue;
                    if (++b.Timer < rules.QuarryIntervalTicks) continue;
                    b.Timer = 0;
                    node.Remaining--;
                    b.Output++;
                }
                else if (b.Kind == BuildingKind.Farm)
                {
                    // V3-4 (27): food from nothing, at the pace its ground set when it was placed.
                    if (b.Output >= rules.BufferLimit) continue;
                    if (++b.Timer < FarmTicksFor(b)) continue;
                    b.Timer = 0;
                    b.Output++;
                }
                else if (b.Kind == BuildingKind.Smelter)
                {
                    if (b.Timer == 0 && b.Input >= rules.OrePerMetal && b.Output < rules.BufferLimit)
                    {
                        b.Input -= rules.OrePerMetal;
                        b.Timer = SmeltTicksFor(b.FactionId);
                    }
                    if (b.Timer > 0 && --b.Timer == 0) b.Output++;
                }
                else if (ProcessingOn && b.Kind == BuildingKind.CharcoalKiln)
                {
                    if (b.Timer == 0 && b.Input >= 2 && b.Output < rules.BufferLimit)
                    {
                        b.Input -= 2;
                        b.Timer = rules.CharcoalTicks;
                    }
                    if (b.Timer > 0 && --b.Timer == 0) b.Output++;
                }
                else if (ProcessingOn && b.Kind == BuildingKind.Steelworks)
                {
                    // Both materials are consumed as one atomic operation on the same tick.
                    if (b.Timer == 0 && b.Input >= 1 && b.InputSecondary >= 1 && b.Output < rules.BufferLimit)
                    {
                        b.Input--;
                        b.InputSecondary--;
                        b.Timer = rules.SteelTicks;
                    }
                    if (b.Timer > 0 && --b.Timer == 0) b.Output++;
                }
                else if (ForestryOn && b.Kind == BuildingKind.Fletcher)
                {
                    // Wood and food are consumed together.  Food may arrive from the core stock,
                    // while belts and hand hauling can fill either input slot.
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.InputSecondary < rules.BufferLimit && economy.Food > 0)
                    {
                        economy.Food--; b.InputSecondary++;
                    }
                    if (b.Timer == 0 && b.Input >= rules.FletcherWoodInput && b.InputSecondary >= rules.FletcherFoodInput && b.Output < rules.BufferLimit)
                    {
                        b.Input -= rules.FletcherWoodInput;
                        b.InputSecondary -= rules.FletcherFoodInput;
                        b.Timer = rules.FletcherTicks;
                    }
                    if (b.Timer > 0 && --b.Timer == 0) b.Output++;
                }
                else if (world.Config.Economy.BeltComponents && b.Kind == BuildingKind.Storage)
                {
                    // Storage is passive: it accepts any one resource kind and releases one item per tick
                    // whenever its outward belt has room. The kind is retained until the last item leaves.
                }
            }
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || !b.Complete || b.Output == 0 && !(b.Kind == BuildingKind.Storage && b.StorageCount > 0)) continue;
                int port = OutputCell(b);
                if (port < 0) continue;
                ref var belt = ref world.Belts[port];
                if (belt.FactionId != b.FactionId || belt.Item != 0) continue;
                belt.Item = b.Kind == BuildingKind.Storage ? b.StorageKind : OutputKind(b.Kind);
                belt.Progress = 0;
                if (b.Kind == BuildingKind.Storage) { b.StorageCount--; b.Input = b.StorageCount; b.Output = b.StorageCount; if (b.StorageCount == 0) b.StorageKind = 0; }
                else b.Output--;
            }
        }

        private static ResourceKind OutputKind(BuildingKind kind)
            => kind == BuildingKind.Mine ? ResourceKind.Ore : kind == BuildingKind.LumberCamp ? ResourceKind.Wood : kind == BuildingKind.Quarry ? ResourceKind.Stone : kind == BuildingKind.Farm ? ResourceKind.Food
                : kind == BuildingKind.CharcoalKiln ? ResourceKind.Charcoal : kind == BuildingKind.Steelworks ? ResourceKind.Steel
            : kind == BuildingKind.Fletcher ? ResourceKind.BowGear : ResourceKind.Metal;

        /// <summary>The cell just outside the middle of the side the building faces, or -1 off the map.</summary>
        private int OutputCell(BuildingState b) => OutputCell(b.OriginCell, SizeOf(b.Kind), b.Facing);

        private int OutputCell(int origin, int size, Facing facing)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int x0 = origin % width, z0 = origin / width, x, z;
            switch (facing)
            {
                case Facing.North: x = x0 + size / 2; z = z0 + size; break;
                case Facing.East: x = x0 + size; z = z0 + size / 2; break;
                case Facing.South: x = x0 + size / 2; z = z0 - 1; break;
                default: x = x0 - 1; z = z0 + size / 2; break;
            }
            return x < 0 || z < 0 || x >= width || z >= height ? -1 : z * width + x;
        }

        /// <summary>
        /// A belt pointing into a footprint hands its item to that building if it is the own, finished building and takes
        /// the item: a smelter takes ore while its input has room. Everything else holds the item on the belt.
        /// </summary>
        private bool TryFeedBuilding(int cell, uint faction, ResourceKind item)
        {
            var rules = world.Config.Economy;
            int width = world.Config.Map.WidthCells, x = cell % width, z = cell / width;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive) continue;
                int size = SizeOf(b.Kind), x0 = b.OriginCell % width, z0 = b.OriginCell / width;
                if (x < x0 || x >= x0 + size || z < z0 || z >= z0 + size) continue;
                if (b.FactionId != faction || !b.Complete) return false;
                if (b.Kind == BuildingKind.Smelter && item == ResourceKind.Ore && b.Input < rules.BufferLimit) { b.Input++; return true; }
                if (ProcessingOn && b.Kind == BuildingKind.CharcoalKiln && item == ResourceKind.Wood && b.Input < rules.BufferLimit) { b.Input++; return true; }
                if (ProcessingOn && b.Kind == BuildingKind.Steelworks)
                {
                    if (item == ResourceKind.Metal && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Charcoal && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if (ForestryOn && b.Kind == BuildingKind.Fletcher)
                {
                    if (item == ResourceKind.Wood && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Food && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if (world.Config.Economy.BeltComponents && b.Kind == BuildingKind.Storage && b.StorageCount < rules.StorageCapacity
                    && (b.StorageCount == 0 || b.StorageKind == item))
                {
                    b.StorageKind = item; b.StorageCount++; b.Input = b.StorageCount; b.Output = b.StorageCount; return true;
                }
                return false;
            }
            return false;
        }

        /// <summary>
        /// A mine's footprint must cover exactly one resource point, an ore point with ore left, and otherwise be open
        /// ground with no belt and outside every core.
        /// </summary>
        private bool MineSiteIsClear(int origin, out uint nodeId)
            => ResourceBuildingSiteIsClear(origin, world.Config.Economy.MineSizeCells, ResourceKind.Ore, out nodeId);

        private bool LumberCampSiteIsClear(int origin, out uint nodeId)
            => ResourceBuildingSiteIsClear(origin, world.Config.Economy.LumberCampSizeCells, ResourceKind.Wood, out nodeId);

        private bool QuarrySiteIsClear(int origin, out uint nodeId)
            => ResourceBuildingSiteIsClear(origin, world.Config.Economy.QuarrySizeCells, ResourceKind.Stone, out nodeId);

        private bool ResourceBuildingSiteIsClear(int origin, int size, ResourceKind required, out uint nodeId)
        {
            nodeId = 0;
            var footprint = Footprint(origin, size);
            foreach (int cell in footprint)
            {
                if (!world.Map.IsPassable(cell) || world.Belts[cell].FactionId != 0 || InsideAnyCore(cell)) return false;
                foreach (var node in world.Nodes)
                {
                    if (world.Map.Cell(node.Definition.Position) != cell) continue;
                    if (node.Definition.Kind != required || node.Remaining <= 0 || nodeId != 0) return false;
                    nodeId = node.Definition.Id;
                }
            }
            return nodeId != 0;
        }

        /// <summary>
        /// Carrying by hand (12.3), economy step. At the source's work cell the villager takes what the output holds, up to
        /// a full load, and leaves as soon as it has a full load or the output is empty: ore from a mine goes to the own
        /// finished smelter with the lowest id (or to the core when there is none), metal goes to the core. At a smelter it
        /// puts in what the input has room for and waits with the rest. A source that is gone ends the carrying.
        /// </summary>
        private void Haul(ref VillagerState v)
        {
            var rules = world.Config.Economy;
            ref var source = ref world.Buildings[v.HaulFrom - 1];
            if (!source.Alive || !source.Complete)
            {
                v.HaulFrom = 0; v.HaulTo = 0; v.HaulNodeId = 0;
                v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
                return;
            }
            if (v.Task == VillagerTask.ToPickup)
            {
                if (!InRange(v.Position, world.Map.Center(source.WorkCell), GatherReach)) return;
                var kind = source.Kind == BuildingKind.Storage ? source.StorageKind : OutputKind(source.Kind);
                if (v.Carry > 0 && v.CarryKind != kind) { v.Task = VillagerTask.ToDropOff; return; }
                int available = source.Kind == BuildingKind.Storage ? source.StorageCount : source.Output;
                int take = Math.Min(available, CarryFor(v.FactionId) - v.Carry);
                if (source.Kind == BuildingKind.Storage) { source.StorageCount -= take; source.Input = source.StorageCount; source.Output = source.StorageCount; if (source.StorageCount == 0) source.StorageKind = 0; }
                else source.Output -= take;
                v.Carry += take;
                v.CarryKind = kind;
                if (v.Carry == 0 || (v.Carry < CarryFor(v.FactionId) && source.Output > 0)) return;
                // An explicit V3-6 destination (smelter metal or kiln charcoal to a steelworks) survives pickup;
                // the old automatic mine-to-smelter choice is used only when no destination was assigned.
                if (v.HaulTo == 0) v.HaulTo = source.Kind == BuildingKind.Mine ? OwnSmelter(v.FactionId) : 0;
                v.Task = v.HaulTo != 0 ? VillagerTask.ToDeliver : VillagerTask.ToDropOff;
                return;
            }
            ref var target = ref world.Buildings[v.HaulTo - 1];
            if (!target.Alive || !target.Complete) { v.HaulTo = 0; v.Task = VillagerTask.ToDropOff; return; }
            if (!InRange(v.Position, world.Map.Center(target.WorkCell), GatherReach)) return;
            int put = PutIntoProcessingTarget(ref target, v.CarryKind, v.Carry);
            v.Carry -= put;
            if (v.Carry == 0) v.Task = VillagerTask.ToPickup;
        }

        private int PutIntoProcessingTarget(ref BuildingState target, ResourceKind kind, int amount)
        {
            var rules = world.Config.Economy;
            if (target.Kind == BuildingKind.Smelter && kind == ResourceKind.Ore)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (ProcessingOn && target.Kind == BuildingKind.Steelworks)
            {
                if (kind == ResourceKind.Metal)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
                }
                if (kind == ResourceKind.Charcoal)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.InputSecondary); target.InputSecondary += put; return put;
                }
            }
            if (ForestryOn && target.Kind == BuildingKind.Fletcher && kind == ResourceKind.Wood)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (world.Config.Economy.BeltComponents && target.Kind == BuildingKind.Storage
                && target.StorageCount < rules.StorageCapacity && (target.StorageCount == 0 || target.StorageKind == kind))
            {
                int put = Math.Min(amount, rules.StorageCapacity - target.StorageCount);
                target.StorageKind = kind; target.StorageCount += put; target.Input = target.StorageCount; target.Output = target.StorageCount; return put;
            }
            return 0;
        }

        private bool CanHaulTo(BuildingKind source, BuildingKind destination, ResourceKind kind)
            => (world.Config.Economy.BeltComponents && destination == BuildingKind.Storage
                && kind != 0)
               || (ProcessingOn && destination == BuildingKind.Steelworks
               && ((source == BuildingKind.Smelter && kind == ResourceKind.Metal)
                   || (source == BuildingKind.CharcoalKiln && kind == ResourceKind.Charcoal)))
               || (ForestryOn && destination == BuildingKind.Fletcher && source == BuildingKind.LumberCamp && kind == ResourceKind.Wood);

        /// <summary>The own finished smelter with the lowest id, or 0.</summary>
        private uint OwnSmelter(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Smelter) return b.Id;
            }
            return 0;
        }

        /// <summary>Stops carrying by hand: a load in hand goes to the core first.</summary>
        private static void StopHauling(ref VillagerState v)
        {
            v.HaulFrom = 0; v.HaulTo = 0; v.HaulNodeId = 0;
            if (v.Task == VillagerTask.ToPickup || v.Task == VillagerTask.ToDeliver) v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
        }

        /// <summary>Villagers on their way to (or working) the ore point a new mine covers stop and look for other work.</summary>
        private void ReleaseNode(uint nodeId)
        {
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.NodeId != nodeId || (v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering)) continue;
                v.NodeId = 0;
                v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
            }
        }
    }
}
