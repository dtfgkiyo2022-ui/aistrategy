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
        private int OutputCell(BuildingState b)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, size = SizeOf(b.Kind);
            int x0 = b.OriginCell % width, z0 = b.OriginCell / width, x, z;
            switch (b.Facing)
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
