using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Pure, read-only placement checks used by both economy panel previews and EditMode tests.</summary>
    public static class AdjacentBuildingPlacementRules
    {
        public static bool CanPlace(EconomyView economy, IReadOnlyList<KnownObjective> objectives, uint factionId,
            BuildingKind kind, int origin, int size, Facing facing, int woodCost, int stoneCost)
        {
            if (economy == null || economy.Wood < woodCost || economy.Stone < stoneCost || size <= 0) return false;
            // These have their own site rules in the simulation (on a resource node, by the water, far from the core, in a
            // pass...). The general rule below would paint them red where they belong, so they keep the old cost-only colour.
            if (HasOwnSiteRule(kind)) return true;
            int width = economy.MapWidthCells, height = economy.MapHeightCells;
            if (width <= 0 || height <= 0 || origin < 0 || origin >= width * height
                || origin % width + size > width || origin / width + size > height) return false;

            var footprint = Footprint(origin, size, width);
            for (int i = 0; i < footprint.Length; i++)
            {
                int cell = footprint[i];
                if (!Passable(economy, cell) || River(economy, cell) || Belt(economy, cell)) return false;
                if (NearCore(economy, objectives, factionId, cell) || NearNode(economy, cell)) return false;
                for (int j = 0; j < economy.Buildings.Count; j++)
                {
                    var other = economy.Buildings[j];
                    int otherSize = CellSize(economy, other.SizeMeters);
                    int otherOrigin = OriginOf(economy, other.Center, otherSize);
                    foreach (int otherCell in Footprint(otherOrigin, otherSize, width))
                        if (Chebyshev(cell, otherCell, width) < economy.BuildingClearanceCells) return false;
                }
            }

            if (!economy.AdjacentBuildings) return true;
            for (int i = 0; i < economy.Buildings.Count; i++)
            {
                var other = economy.Buildings[i];
                int otherSize = CellSize(economy, other.SizeMeters);
                int otherOrigin = OriginOf(economy, other.Center, otherSize);
                int output = OutputCell(economy, otherOrigin, otherSize, other.Facing);
                if (Contains(footprint, output)) return false;
            }
            if (!HasOutput(kind)) return true;
            int candidateOutput = OutputCell(economy, origin, size, facing);
            if (candidateOutput < 0 || !Passable(economy, candidateOutput) || River(economy, candidateOutput)
                || Belt(economy, candidateOutput) || NearCore(economy, objectives, factionId, candidateOutput)
                || NearNode(economy, candidateOutput)) return false;
            for (int i = 0; i < economy.Buildings.Count; i++)
            {
                var other = economy.Buildings[i];
                int otherSize = CellSize(economy, other.SizeMeters);
                if (Contains(Footprint(OriginOf(economy, other.Center, otherSize), otherSize, width), candidateOutput)) return false;
            }
            return true;
        }

        private static bool Passable(EconomyView e, int cell)
            => e.PassableCells.Count == 0 || cell >= 0 && cell < e.PassableCells.Count && e.PassableCells[cell];

        private static bool River(EconomyView e, int cell)
            => cell >= 0 && cell < e.RiverCells.Count && e.RiverCells[cell];

        private static bool Belt(EconomyView e, int cell)
        {
            for (int i = 0; i < e.Belts.Count; i++) if (e.Belts[i].Cell == cell && e.Belts[i].FactionId != 0) return true;
            return false;
        }

        private static bool NearNode(EconomyView e, int cell)
        {
            for (int i = 0; i < e.Resources.Count; i++)
                if (Chebyshev(cell, CellOf(e, e.Resources[i].Position), e.MapWidthCells) < e.NodeClearanceCells) return true;
            return false;
        }

        private static bool NearCore(EconomyView e, IReadOnlyList<KnownObjective> objectives, uint factionId, int cell)
        {
            if (objectives == null) return false;
            for (int i = 0; i < objectives.Count; i++)
                if (objectives[i].Kind == GoalKind.Core && objectives[i].IsOwnerKnown
                    && objectives[i].OwnerFactionId == factionId
                    && Chebyshev(cell, CellOf(e, objectives[i].Position), e.MapWidthCells) < e.CoreClearanceCells) return true;
            return false;
        }

        private static int CellOf(EconomyView e, SimPoint point)
        {
            if (e.CellSizeMeters <= 0) return -1;
            long cellRaw = (long)e.CellSizeMeters * 65536L;
            return (int)(point.Z.Raw / cellRaw) * e.MapWidthCells + (int)(point.X.Raw / cellRaw);
        }

        private static int CellSize(EconomyView e, int sizeMeters)
            => e.CellSizeMeters <= 0 ? 0 : sizeMeters / e.CellSizeMeters;

        private static int OriginOf(EconomyView e, SimPoint center, int size)
        {
            long cellRaw = (long)e.CellSizeMeters * 65536L;
            int x = (int)((center.X.Raw - (long)size * cellRaw / 2) / cellRaw);
            int z = (int)((center.Z.Raw - (long)size * cellRaw / 2) / cellRaw);
            return z * e.MapWidthCells + x;
        }

        private static int[] Footprint(int origin, int size, int width)
        {
            var cells = new int[size * size];
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++) cells[z * size + x] = origin + z * width + x;
            return cells;
        }

        private static bool Contains(int[] cells, int target)
        {
            for (int i = 0; i < cells.Length; i++) if (cells[i] == target) return true;
            return false;
        }

        private static int Chebyshev(int a, int b, int width)
        {
            if (a < 0 || b < 0 || width <= 0) return int.MaxValue;
            return Math.Max(Math.Abs(a % width - b % width), Math.Abs(a / width - b / width));
        }

        private static int OutputCell(EconomyView e, int origin, int size, Facing facing)
        {
            int width = e.MapWidthCells, height = e.MapHeightCells, x0 = origin % width, z0 = origin / width;
            int x, z;
            switch (facing)
            {
                case Facing.North: x = x0 + size / 2; z = z0 + size; break;
                case Facing.East: x = x0 + size; z = z0 + size / 2; break;
                case Facing.South: x = x0 + size / 2; z = z0 - 1; break;
                default: x = x0 - 1; z = z0 + size / 2; break;
            }
            return x < 0 || z < 0 || x >= width || z >= height ? -1 : z * width + x;
        }

        private static bool HasOwnSiteRule(BuildingKind kind)
            => kind == BuildingKind.Mine || kind == BuildingKind.LumberCamp || kind == BuildingKind.Quarry
                || kind == BuildingKind.MineShaft || kind == BuildingKind.Harbor || kind == BuildingKind.Town
                || kind == BuildingKind.Caravanserai || kind == BuildingKind.Shrine || kind == BuildingKind.Tollgate
                || kind == BuildingKind.Bridge;

        private static bool HasOutput(BuildingKind kind)
            => kind == BuildingKind.Mine || kind == BuildingKind.Smelter || kind == BuildingKind.LumberCamp
                || kind == BuildingKind.Quarry || kind == BuildingKind.CharcoalKiln || kind == BuildingKind.Steelworks
                || kind == BuildingKind.Fletcher || kind == BuildingKind.Mill || kind == BuildingKind.BrickKiln
                || kind == BuildingKind.Saddlery || kind == BuildingKind.Sawmill || kind == BuildingKind.Scriptorium
                || kind == BuildingKind.DryingRack || kind == BuildingKind.Lapidary || kind == BuildingKind.Weaver
                || kind == BuildingKind.IncenseHouse || kind == BuildingKind.CharmWorkshop || kind == BuildingKind.RepairYard
                || kind == BuildingKind.ToolShop;
    }
}
