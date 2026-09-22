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
                else if (b.Kind == BuildingKind.Smelter)
                {
                    if (b.Timer == 0 && b.Input >= rules.OrePerMetal && b.Output < rules.BufferLimit)
                    {
                        b.Input -= rules.OrePerMetal;
                        b.Timer = rules.SmeltTicks;
                    }
                    if (b.Timer > 0 && --b.Timer == 0) b.Output++;
                }
            }
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || !b.Complete || b.Output == 0) continue;
                int port = OutputCell(b);
                if (port < 0) continue;
                ref var belt = ref world.Belts[port];
                if (belt.FactionId != b.FactionId || belt.Item != 0) continue;
                belt.Item = OutputKind(b.Kind);
                belt.Progress = 0;
                b.Output--;
            }
        }

        private static ResourceKind OutputKind(BuildingKind kind) => kind == BuildingKind.Mine ? ResourceKind.Ore : ResourceKind.Metal;

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
                if (b.FactionId != faction || !b.Complete || b.Kind != BuildingKind.Smelter || item != ResourceKind.Ore || b.Input >= rules.BufferLimit) return false;
                b.Input++;
                return true;
            }
            return false;
        }

        /// <summary>
        /// A mine's footprint must cover exactly one resource point, an ore point with ore left, and otherwise be open
        /// ground with no belt and outside every core.
        /// </summary>
        private bool MineSiteIsClear(int origin, out uint nodeId)
        {
            nodeId = 0;
            var footprint = Footprint(origin, world.Config.Economy.MineSizeCells);
            foreach (int cell in footprint)
            {
                if (!world.Map.IsPassable(cell) || world.Belts[cell].FactionId != 0 || InsideAnyCore(cell)) return false;
                foreach (var node in world.Nodes)
                {
                    if (world.Map.Cell(node.Definition.Position) != cell) continue;
                    if (node.Definition.Kind != ResourceKind.Ore || node.Remaining <= 0 || nodeId != 0) return false;
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
                v.HaulFrom = 0; v.HaulTo = 0;
                v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
                return;
            }
            if (v.Task == VillagerTask.ToPickup)
            {
                if (!InRange(v.Position, world.Map.Center(source.WorkCell), GatherReach)) return;
                var kind = OutputKind(source.Kind);
                if (v.Carry > 0 && v.CarryKind != kind) { v.Task = VillagerTask.ToDropOff; return; }
                int take = Math.Min(source.Output, rules.CarryCapacity - v.Carry);
                source.Output -= take;
                v.Carry += take;
                v.CarryKind = kind;
                if (v.Carry == 0 || (v.Carry < rules.CarryCapacity && source.Output > 0)) return;
                v.HaulTo = source.Kind == BuildingKind.Mine ? OwnSmelter(v.FactionId) : 0;
                v.Task = v.HaulTo != 0 ? VillagerTask.ToDeliver : VillagerTask.ToDropOff;
                return;
            }
            ref var target = ref world.Buildings[v.HaulTo - 1];
            if (!target.Alive || !target.Complete) { v.HaulTo = 0; v.Task = VillagerTask.ToDropOff; return; }
            if (!InRange(v.Position, world.Map.Center(target.WorkCell), GatherReach)) return;
            int put = Math.Min(v.Carry, rules.BufferLimit - target.Input);
            target.Input += put;
            v.Carry -= put;
            if (v.Carry == 0) v.Task = VillagerTask.ToPickup;
        }

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
            v.HaulFrom = 0; v.HaulTo = 0;
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
