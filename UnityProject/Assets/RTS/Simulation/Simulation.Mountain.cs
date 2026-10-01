using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-15 #1: the mountain civilisation turns the edge of impassable mountains into a renewable, direct stock
    /// source. A shaft never claims a resource node and therefore never depletes stone or ore on the map.
    /// </summary>
    public sealed partial class Simulation
    {
        private void DecideMountain(uint faction)
        {
            var rules = world.Config.Economy;
            if (!MountainAllowed(faction) || MountainShaftCount(faction) >= MountainMaxBuildingsFor(faction)) return;
            if (world.Economies[faction - 1].Wood < rules.MountainWoodCost) return;
            int origin = FindMountainShaftSite(faction);
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.MineShaft, origin, Facing.North, 0);
        }

        private int MountainMaxBuildingsFor(uint faction)
            => world.Config.Economy.MountainMaxBuildings
                + (HasTech(faction, MountainTech.DeepShaft) ? world.Config.Economy.MountainDeepShaftMaxBuildingsBonus : 0);

        private int MountainShaftCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.MineShaft) count++;
            return count;
        }

        private int FindMountainShaftSite(uint faction)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int size = world.Config.Economy.MountainSizeCells;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position);
            int best = -1;
            BigInteger bestDistance = 0;
            for (int z = 0; z + size <= height; z++)
                for (int x = 0; x + size <= width; x++)
                {
                    int origin = z * width + x;
                    if (!MountainShaftSiteIsClear(faction, origin) || !KeepsMapConnected(faction, origin, size)) continue;
                    BigInteger distance = DistanceSquared(world.Map.Center(origin), world.Map.Center(core));
                    if (best < 0 || distance < bestDistance || distance == bestDistance && origin < best)
                    {
                        best = origin;
                        bestDistance = distance;
                    }
                }
            return best;
        }

        private bool MountainShaftSiteIsClear(uint faction, int origin)
        {
            if (!MountainAllowed(faction)) return false;
            int size = world.Config.Economy.MountainSizeCells;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            if (origin < 0 || origin >= width * height || origin % width + size > width || origin / width + size > height) return false;
            return SiteIsClear(origin, world.Map.Cell(OwnCore(faction).Definition.Position), size)
                && MountainAdjacentCount(origin, size) > 0;
        }

        /// <summary>Counts distinct four-neighbour mountain cells around the square footprint.</summary>
        private int MountainAdjacentCount(int origin, int size)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            var terrain = world.Config.Map.Terrain;
            if (terrain.Length == 0) return 0;
            var adjacent = new HashSet<int>();
            int x0 = origin % width, z0 = origin / width;
            for (int z = z0; z < z0 + size; z++)
                for (int x = x0; x < x0 + size; x++)
                {
                    AddIfMountain(x - 1, z);
                    AddIfMountain(x + 1, z);
                    AddIfMountain(x, z - 1);
                    AddIfMountain(x, z + 1);
                }
            return adjacent.Count;

            void AddIfMountain(int x, int z)
            {
                if (x < 0 || z < 0 || x >= width || z >= height) return;
                int cell = z * width + x;
                if (terrain[cell] == (byte)TerrainKind.Mountain) adjacent.Add(cell);
            }
        }

        /// <summary>
        /// Counts the observed, currently legal shaft edges used by the civilisation-choice score. The footprint,
        /// core clearance, resource/building clearance and passability are the same checks as a real shaft placement;
        /// the current civilisation is deliberately not checked because this runs before the choice is made.
        /// </summary>
        private int CountUsableMountainShaftEdgeUnits(uint faction, SimPoint core)
        {
            var explored = world.Factions[faction - 1].ExploredCells;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int size = world.Config.Economy.MountainSizeCells;
            int coreCell = world.Map.Cell(core);
            int edgeUnits = 0;
            int coreX = coreCell % width, coreZ = coreCell / width;
            int radius = CivStoneReach / world.Config.Map.CellSizeMeters + size + 1;
            int minX = Math.Max(0, coreX - radius), maxX = Math.Min(width - size, coreX + radius);
            int minZ = Math.Max(0, coreZ - radius), maxZ = Math.Min(height - size, coreZ + radius);
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    int origin = z * width + x;
                    if (!InRange(FootprintCenter(origin, size), core, Fix64.FromInt(CivStoneReach))) continue;
                    int adjacent = MountainAdjacentCount(origin, size);
                    if (adjacent == 0 || !MountainScoreCellsExplored(explored, origin, size, adjacent)) continue;
                    if (!SiteIsClear(origin, coreCell, size) || !KeepsMapConnected(faction, origin, size)) continue;
                    edgeUnits = checked(edgeUnits + Math.Min(adjacent, world.Config.Economy.MountainMaxAdjacentCells));
                }
            return edgeUnits;
        }

        private bool MountainScoreCellsExplored(bool[] explored, int origin, int size, int adjacent)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            foreach (int cell in Footprint(origin, size))
                if (cell < 0 || cell >= explored.Length || !explored[cell]) return false;

            int x0 = origin % width, z0 = origin / width;
            var mountainCells = new HashSet<int>();
            for (int z = z0; z < z0 + size; z++)
                for (int x = x0; x < x0 + size; x++)
                {
                    if (x > 0) AddMountainCell(mountainCells, (z * width) + x - 1);
                    if (x + 1 < width) AddMountainCell(mountainCells, (z * width) + x + 1);
                    if (z > 0) AddMountainCell(mountainCells, ((z - 1) * width) + x);
                    if (z + 1 < height) AddMountainCell(mountainCells, ((z + 1) * width) + x);
                }
            if (mountainCells.Count != adjacent) return false;
            foreach (int cell in mountainCells)
                if (!explored[cell]) return false;
            return true;

            void AddMountainCell(HashSet<int> cells, int cell)
            {
                if (world.Config.Map.Terrain[cell] == (byte)TerrainKind.Mountain) cells.Add(cell);
            }
        }

        private int MountainInterval(BuildingState shaft)
        {
            var rules = world.Config.Economy;
            int adjacent = Math.Min(MountainAdjacentCount(shaft.OriginCell, rules.MountainSizeCells), rules.MountainMaxAdjacentCells);
            int interval = Math.Max(rules.MountainMinIntervalTicks, rules.MountainBaseIntervalTicks - adjacent * rules.MountainIntervalStepTicks);
            if (HasTech(shaft.FactionId, MountainTech.DeepShaft))
                interval = Math.Max(1, checked(interval * rules.MountainDeepShaftIntervalPermille / 1000));
            return interval;
        }

        private void AdvanceMountain()
        {
            if (!MountainOn) return;
            var rules = world.Config.Economy;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var shaft = ref world.Buildings[i];
                if (!shaft.Alive || !shaft.Complete || shaft.Kind != BuildingKind.MineShaft || shaft.Researching != 0) continue;
                int interval = MountainInterval(shaft);
                if (++shaft.Timer < interval) continue;
                shaft.Timer = 0;
                ResourceKind kind = shaft.MountainOreNext ? ResourceKind.Ore : ResourceKind.Stone;
                AddStock(shaft.FactionId, kind, kind == ResourceKind.Ore ? rules.MountainOreYield : rules.MountainStoneYield);
                shaft.MountainOreNext = !shaft.MountainOreNext;
            }
        }

        private bool MountainFortified(uint faction, int origin, int size)
            => MountainAllowed(faction) && HasTech(faction, MountainTech.MountainFort)
                && MountainAdjacentCount(origin, size) > 0;

        private int MountainFortHp(int baseHp, uint faction, int origin, int size)
            => MountainFortified(faction, origin, size)
                ? checked(baseHp * world.Config.Economy.MountainFortHpPermille / 1000) : baseHp;

        private int MountainTowerRange(uint faction, int origin, int size)
            => world.Config.Economy.TowerRange + (MountainFortified(faction, origin, size) ? world.Config.Economy.MountainFortRangeBonus : 0);
    }
}
