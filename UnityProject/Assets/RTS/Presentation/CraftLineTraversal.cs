using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Display-side belt graph traversal. It mirrors only the public EconomyView belt contract.</summary>
    public static class CraftLineTraversal
    {
        public sealed class Scratch
        {
            internal readonly Dictionary<int, BeltView> ByCell = new Dictionary<int, BeltView>();
            internal readonly List<int> Starts = new List<int>();
            internal readonly List<int> Targets = new List<int>(3);
            internal readonly HashSet<int> Visited = new HashSet<int>();
            internal readonly Queue<int> Frontier = new Queue<int>();
        }

        public static void CollectConnectedBeltCells(IReadOnlyList<BeltView> belts, int startCell, int width, int height,
            ISet<int> result)
        {
            if (result == null || belts == null || width <= 0 || height <= 0) return;
            var byCell = new Dictionary<int, BeltView>();
            for (int i = 0; i < belts.Count; i++) byCell[belts[i].Cell] = belts[i];
            if (!byCell.ContainsKey(startCell)) return;
            var frontier = new Queue<int>();
            frontier.Enqueue(startCell);
            while (frontier.Count > 0)
            {
                int cell = frontier.Dequeue();
                if (!result.Add(cell)) continue;
                BeltView current = byCell[cell];
                var targets = new List<int>(3);
                AddOutputTargets(current, width, height, targets);
                foreach (int target in targets) if (byCell.ContainsKey(target) && !result.Contains(target)) frontier.Enqueue(target);
                foreach (var pair in byCell)
                {
                    if (pair.Key == cell) continue;
                    targets.Clear();
                    AddOutputTargets(pair.Value, width, height, targets);
                    for (int i = 0; i < targets.Count; i++)
                        if (targets[i] == cell && !result.Contains(pair.Key)) { frontier.Enqueue(pair.Key); break; }
                }
            }
        }

        public static void FindBeltCellsForBuilding(BuildingView building, EconomyView economy, int width, int height,
            ISet<int> beltCells, ISet<uint> connectedBuildings)
        {
            FindBeltCellsForBuilding(building, economy, width, height, beltCells, connectedBuildings, new Scratch());
        }

        public static void FindBeltCellsForBuilding(BuildingView building, EconomyView economy, int width, int height,
            ISet<int> beltCells, ISet<uint> connectedBuildings, Scratch scratch)
        {
            if (economy == null || beltCells == null || connectedBuildings == null || scratch == null) return;
            connectedBuildings.Add(building.Id);
            var byCell = scratch.ByCell;
            byCell.Clear();
            for (int i = 0; i < economy.Belts.Count; i++)
                if (economy.Belts[i].FactionId == building.FactionId) byCell[economy.Belts[i].Cell] = economy.Belts[i];
            var starts = scratch.Starts;
            starts.Clear();
            var targets = scratch.Targets;
            targets.Clear();
            for (int i = 0; i < economy.Belts.Count; i++)
            {
                var belt = economy.Belts[i];
                if (belt.FactionId != building.FactionId) continue;
                targets.Clear();
                AddOutputTargets(belt, width, height, targets);
                for (int j = 0; j < targets.Count; j++)
                    if (InsideFootprint(targets[j], building, width)) { starts.Add(belt.Cell); break; }
            }
            int origin = CellOf(building, width);
            int size = SizeCells(building);
            int port = OutputPort(origin, size, building.Facing, width, height);
            if (byCell.ContainsKey(port)) starts.Add(port);

            var visited = scratch.Visited;
            visited.Clear();
            var frontier = scratch.Frontier;
            frontier.Clear();
            for (int i = 0; i < starts.Count; i++) frontier.Enqueue(starts[i]);
            targets.Clear();
            while (frontier.Count > 0)
            {
                int cell = frontier.Dequeue();
                if (!byCell.ContainsKey(cell) || !visited.Add(cell)) continue;
                beltCells.Add(cell);
                targets.Clear();
                AddOutputTargets(byCell[cell], width, height, targets);
                for (int i = 0; i < targets.Count; i++)
                {
                    int target = targets[i];
                    if (InsideAnyBuilding(target, economy.Buildings, width, building.FactionId, out var targetBuilding))
                        connectedBuildings.Add(targetBuilding.Id);
                    if (byCell.ContainsKey(target)) frontier.Enqueue(target);
                }
                foreach (var pair in byCell)
                {
                    targets.Clear();
                    AddOutputTargets(pair.Value, width, height, targets);
                    for (int i = 0; i < targets.Count; i++)
                        if (targets[i] == cell) { frontier.Enqueue(pair.Key); break; }
                }
            }
        }

        private static void AddOutputTargets(BeltView belt, int width, int height, List<int> targets)
        {
            if (belt.Component == BeltComponentKind.UndergroundEntrance || belt.Component == BeltComponentKind.UndergroundExit)
                if (InBounds(belt.PairCell, width, height)) targets.Add(belt.PairCell);
            if (belt.Component != BeltComponentKind.UndergroundEntrance)
                AddUnique(targets, NextCell(belt.Cell, belt.Facing, width, height));
            if (belt.Component == BeltComponentKind.Splitter || belt.Component == BeltComponentKind.Sorter)
                AddUnique(targets, NextCell(belt.Cell, TurnLeft(belt.Facing), width, height));
        }

        private static bool InsideAnyBuilding(int cell, IReadOnlyList<BuildingView> buildings, int width, uint factionId, out BuildingView found)
        {
            for (int i = 0; i < buildings.Count; i++)
                if (buildings[i].FactionId == factionId && InsideFootprint(cell, buildings[i], width)) { found = buildings[i]; return true; }
            found = default(BuildingView);
            return false;
        }

        private static bool InsideFootprint(int cell, BuildingView building, int width)
        {
            int origin = CellOf(building, width), size = SizeCells(building);
            int x = cell % width, z = cell / width, ox = origin % width, oz = origin / width;
            return x >= ox && x < ox + size && z >= oz && z < oz + size;
        }

        private static int CellOf(BuildingView building, int width)
        {
            int x = (int)(building.Center.X.Raw / 65536f / 2f);
            int z = (int)(building.Center.Z.Raw / 65536f / 2f);
            return z * width + x;
        }

        private static int SizeCells(BuildingView building)
        {
            return building.SizeMeters <= 0 ? 1 : System.Math.Max(1, (int)(building.SizeMeters / 2f + 0.5f));
        }

        private static int OutputPort(int origin, int size, Facing facing, int width, int height)
        {
            int x = origin % width, z = origin / width;
            if (facing == Facing.North) { x += size / 2; z += size; }
            else if (facing == Facing.East) { x += size; z += size / 2; }
            else if (facing == Facing.South) { x += size / 2; z--; }
            else { x--; z += size / 2; }
            return x < 0 || z < 0 || x >= width || z >= height ? -1 : z * width + x;
        }

        private static int NextCell(int cell, Facing facing, int width, int height)
        {
            int x = cell % width, z = cell / width;
            if (facing == Facing.North) z++; else if (facing == Facing.South) z--; else if (facing == Facing.East) x++; else x--;
            return x < 0 || z < 0 || x >= width || z >= height ? -1 : z * width + x;
        }

        private static Facing TurnLeft(Facing facing) { return (Facing)(((int)facing + 3) % 4); }

        private static bool InBounds(int cell, int width, int height) { return cell >= 0 && cell < width * height; }

        private static void AddUnique(List<int> values, int value)
        {
            if (value >= 0 && !values.Contains(value)) values.Add(value);
        }
    }
}
