using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private const int TollgateSearchPathMargin = 3;

        // Derived from the buildings (never saved): the faction owning a finished tollgate on each cell, 0 elsewhere.
        // Rebuilt at the start of every tick; the route caches are dropped only when it actually changes.
        private uint[] tollgateOwners;

        private void RefreshTollgateOwners()
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            var next = new uint[cells];
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!building.Alive || !building.Complete || building.Kind != BuildingKind.Tollgate) continue;
                foreach (int cell in Footprint(building)) if (cell >= 0 && cell < cells) next[cell] = building.FactionId;
            }
            bool changed = tollgateOwners == null || tollgateOwners.Length != cells;
            for (int i = 0; !changed && i < cells; i++) changed = tollgateOwners[i] != next[i];
            tollgateOwners = next;
            if (changed) world.Map.InvalidateRoutes();
        }

        private bool IsPassableForFaction(int cell, uint faction)
        {
            if (!world.Map.IsPassable(cell)) return false;
            if (faction < 1 || faction > 2 || tollgateOwners == null) return true;
            uint owner = tollgateOwners[cell];
            return owner == 0 || owner == faction;
        }

        private void TryPlaceTollgate(uint faction, EconomyCommand command)
        {
            var rules = world.Config.Economy;
            if (!TollgateAllowed(faction) || ActiveTollgateCount(faction) >= rules.TollgateMaxBuildings
                || (byte)command.Facing > 3 || world.Economies[faction - 1].Wood < rules.TollgateWoodCost
                || world.Economies[faction - 1].Stone < rules.TollgateStoneCost) return;
            int[] cells = TollgateFootprint(command.Cell, command.Facing);
            if (!TollgateSiteIsClear(faction, cells) || !KeepsMapConnected(faction, cells)) return;
            PlaceBuildingAt(faction, BuildingKind.Tollgate, command.Cell, command.Facing, 0);
            world.Buildings[world.BuildingCount - 1].Held = true;
        }

        private int ActiveTollgateCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Tollgate) count++;
            return count;
        }

        private bool TollgateSiteIsClear(uint faction, int[] cells)
        {
            if (cells == null || cells.Length != world.Config.Economy.TollgateLengthCells) return false;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            var seen = new HashSet<int>();
            int core = world.Map.Cell(OwnCore(faction).Definition.Position);
            foreach (int cell in cells)
            {
                if (cell < 0 || cell >= width * height || !seen.Add(cell) || !world.Map.IsPassable(cell)
                    || IsRiverCell(cell) || Chebyshev(cell, core) < CoreClearanceCells || IsNodeCell(cell) || InsideAnyCore(cell)) return false;
                if (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) return false;
                foreach (var node in world.Nodes)
                    if (Chebyshev(cell, world.Map.Cell(node.Definition.Position)) < NodeClearanceCells) return false;
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    if (!world.Buildings[i].Alive) continue;
                    foreach (int other in Footprint(world.Buildings[i]))
                        if (Chebyshev(cell, other) < BuildingClearanceCells) return false;
                }
            }
            return true;
        }

        /// <summary>V3-16 #1: the automatic choice is the narrowest cell pair on the own-core to enemy-core route.</summary>
        private void DecideTollgate(uint faction)
        {
            if (!TollgateAllowed(faction) || ActiveTollgateCount(faction) >= world.Config.Economy.TollgateMaxBuildings) return;
            var economy = world.Economies[faction - 1];
            var rules = world.Config.Economy;
            if (economy.Wood < rules.TollgateWoodCost || economy.Stone < rules.TollgateStoneCost) return;
            int start = world.Map.Cell(OwnCore(faction).Definition.Position);
            var enemy = world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position;
            int[] path = world.Map.FindPath(start, enemy, faction);
            if (path.Length < TollgateSearchPathMargin * 2 + 1) return;

            int[] chosen = null;
            Facing chosenFacing = Facing.North;
            int chosenNarrowness = int.MaxValue, chosenPathIndex = int.MaxValue, chosenOrigin = int.MaxValue;
            for (int i = TollgateSearchPathMargin; i < path.Length - TollgateSearchPathMargin; i++)
            {
                int pathCell = path[i];
                for (int f = 0; f < 4; f++)
                {
                    var facing = (Facing)f;
                    var cells = TollgateFootprint(pathCell, facing);
                    if (Array.IndexOf(cells, pathCell) < 0 || !TollgateSiteIsClear(faction, cells) || !KeepsMapConnected(faction, cells)) continue;
                    int narrowness = OpenNeighbours(cells);
                    if (narrowness < chosenNarrowness || narrowness == chosenNarrowness && i < chosenPathIndex
                        || narrowness == chosenNarrowness && i == chosenPathIndex && pathCell < chosenOrigin)
                    {
                        chosen = cells; chosenFacing = facing; chosenNarrowness = narrowness; chosenPathIndex = i; chosenOrigin = pathCell;
                    }
                }
            }
            if (chosen == null) return;
            PlaceBuildingAt(faction, BuildingKind.Tollgate, chosen[0], chosenFacing, 0);
            world.Buildings[world.BuildingCount - 1].Held = false;
        }

        private int OpenNeighbours(int[] cells)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, result = 0;
            foreach (int cell in cells)
            {
                int x = cell % width, z = cell / width;
                int[] neighbours = { z + 1 < height ? cell + width : -1, x + 1 < width ? cell + 1 : -1,
                    z > 0 ? cell - width : -1, x > 0 ? cell - 1 : -1 };
                foreach (int next in neighbours)
                    if (next >= 0 && Array.IndexOf(cells, next) < 0 && world.Map.IsPassable(next)) result++;
            }
            return result;
        }

        private bool KeepsMapConnected(uint faction, int[] closedCells)
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells, width = world.Config.Map.WidthCells;
            var closed = new bool[cells];
            foreach (int cell in closedCells) if (cell >= 0 && cell < cells) closed[cell] = true;
            var seen = new bool[cells];
            var queue = new int[cells];
            int head = 0, tail = 0, start = world.Map.Cell(OwnCore(faction).Definition.Position);
            if (!world.Map.IsPassable(start) || closed[start]) return false;
            queue[tail++] = start; seen[start] = true;
            while (head < tail)
            {
                int cell = queue[head++], x = cell % width;
                Visit(cell + width); if (x + 1 < width) Visit(cell + 1); Visit(cell - width); if (x > 0) Visit(cell - 1);
            }
            bool Reached(SimPoint point) { int cell = world.Map.Cell(point); return cell >= 0 && seen[cell]; }
            if (!Reached(world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position)) return false;
            foreach (var post in world.Outposts) if (!Reached(post.Definition.Position)) return false;
            foreach (var node in world.Nodes)
            {
                int cell = world.Map.Cell(node.Definition.Position);
                if (cell < 0) return false;
                if (closed[cell] || !world.Map.IsPassable(cell)) continue;
                if (!seen[cell]) return false;
            }
            return true;
            void Visit(int next)
            {
                if (next < 0 || next >= cells || seen[next] || closed[next] || !world.Map.IsPassable(next)) return;
                seen[next] = true; queue[tail++] = next;
            }
        }
    }
}
