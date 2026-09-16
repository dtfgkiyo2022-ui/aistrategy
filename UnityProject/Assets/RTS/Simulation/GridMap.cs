using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>Immutable integer grid and deterministic, bounded army path search.</summary>
    public sealed class GridMap
    {
        internal Action<string, bool> Measure;
        private readonly int width, height;
        private readonly long size;
        private readonly bool[] passable;
        // Derived from immutable terrain only. FIFO eviction changes cost, never route choices.
        private readonly Dictionary<int, int[]> routes = new Dictionary<int, int[]>();
        private readonly Queue<int> routeOrder = new Queue<int>();
        public int[] SharedRoute(int start, SimPoint goal)
        {
            Measure?.Invoke("Pathfinding", true);
            try { return SharedRouteCore(start, goal); }
            finally { Measure?.Invoke("Pathfinding", false); }
        }
        private int[] SharedRouteCore(int start, SimPoint goal)
        {
            if (!IsPassable(start)) return Array.Empty<int>();
            int target = Cell(goal);
            if (!IsPassable(target))
            {
                target = -1;
                BigInteger best = 0;
                for (int i = 0; i < passable.Length; i++)
                    if (passable[i])
                    {
                        var d = Distance(Center(i), goal);
                        if (target < 0 || d < best) { target = i; best = d; }
                    }
            }
            if (target < 0) return Array.Empty<int>();
            if (!routes.TryGetValue(target, out var next))
            {
                // One reverse BFS per destination, shared by all soldiers. At most 8192 cells.
                next = new int[passable.Length]; Array.Fill(next, -1);
                var queue = new int[passable.Length]; int head = 0, tail = 0;
                queue[tail++] = target; next[target] = target;
                while (head < tail)
                {
                    int id = queue[head++], x = id % width, z = id / width;
                    Visit(z + 1 < height ? id + width : -1, id);
                    Visit(x + 1 < width ? id + 1 : -1, id);
                    Visit(z > 0 ? id - width : -1, id);
                    Visit(x > 0 ? id - 1 : -1, id);
                }
                void Visit(int cell, int parent)
                {
                    if (!IsPassable(cell) || next[cell] >= 0) return;
                    next[cell] = parent; queue[tail++] = cell;
                }
                if (routeOrder.Count == 16) routes.Remove(routeOrder.Dequeue());
                routes.Add(target, next); routeOrder.Enqueue(target);
            }
            if (next[start] < 0) return Array.Empty<int>();
            var path = new List<int>();
            for (int cell = start; ; cell = next[cell])
            { path.Add(cell); if (cell == target) break; }
            return path.ToArray();
        }
        public GridMap(MapDefinition map)
        {
            width = map.WidthCells; height = map.HeightCells;
            size = Fix64.FromInt(map.CellSizeMeters).Raw;
            passable = new bool[checked(width * height)];
            Array.Fill(passable, map.DefaultPassable);
            foreach (int id in map.BlockedCellIds) passable[id] = false;
        }
        public bool IsPassable(int cell) => cell >= 0 && cell < passable.Length && passable[cell];
        public int Cell(SimPoint p)
        {
            if (p.X.Raw < 0 || p.Z.Raw < 0 || p.X.Raw >= width * size || p.Z.Raw >= height * size) return -1;
            return (int)(p.Z.Raw / size) * width + (int)(p.X.Raw / size);
        }
        public SimPoint Center(int cell) => new SimPoint(Fix64.FromRaw(cell % width * size + size / 2), Fix64.FromRaw(cell / width * size + size / 2));
        private int H(int a, int b) => Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);
        public int[] FindPath(int start, SimPoint goal)
        {
            Measure?.Invoke("Pathfinding", true);
            try { return FindPathCore(start, goal); }
            finally { Measure?.Invoke("Pathfinding", false); }
        }
        private int[] FindPathCore(int start, SimPoint goal)
        {
            if (!IsPassable(start)) return Array.Empty<int>();
            int target = Cell(goal);
            var g = new int[passable.Length]; Array.Fill(g, int.MaxValue);
            var parent = new int[passable.Length]; Array.Fill(parent, -1);
            var closed = new bool[passable.Length];
            var open = new SortedSet<(int f, int h, int id)>();
            int heuristicCell = target >= 0 ? target : start;
            g[start] = 0; open.Add((H(start, heuristicCell), H(start, heuristicCell), start));
            int best = start, expanded = 0;
            BigInteger bestDistance = Distance(Center(start), goal);
            while (open.Count > 0 && expanded < 8192)
            {
                var item = open.Min; open.Remove(item);
                int id = item.id; closed[id] = true; expanded++;
                var distance = Distance(Center(id), goal);
                if (distance < bestDistance || (distance == bestDistance && id < best)) { best = id; bestDistance = distance; }
                if (id == target) { best = id; break; }
                int x = id % width, z = id / width;
                int[] neighbors = { z + 1 < height ? id + width : -1, x + 1 < width ? id + 1 : -1,
                    z > 0 ? id - width : -1, x > 0 ? id - 1 : -1 };
                foreach (int next in neighbors)
                {
                    if (!IsPassable(next) || closed[next] || g[next] <= g[id] + 1) continue;
                    int h = H(next, heuristicCell);
                    if (g[next] != int.MaxValue) open.Remove((g[next] + h, h, next));
                    g[next] = g[id] + 1; parent[next] = id; open.Add((g[next] + h, h, next));
                }
            }
            var path = new List<int>();
            for (int id = best; id >= 0; id = parent[id]) path.Add(id);
            path.Reverse(); return path.ToArray();
        }
        private static BigInteger Distance(SimPoint a, SimPoint b)
        {
            var x = new BigInteger(a.X.Raw) - b.X.Raw; var z = new BigInteger(a.Z.Raw) - b.Z.Raw;
            return x * x + z * z;
        }
        /// <summary>Walk every crossed boundary, checking both side cells at exact corners.</summary>
        public SimPoint ClipMove(SimPoint from, SimPoint to)
        {
            int cell = Cell(from); if (!IsPassable(cell)) return from;
            long dx = to.X.Raw - from.X.Raw, dz = to.Z.Raw - from.Z.Raw;
            int sx = Math.Sign(dx), sz = Math.Sign(dz), x = cell % width, z = cell / width;
            long ax = Math.Abs(dx), az = Math.Abs(dz);
            while (true)
            {
                long nx = sx == 0 ? long.MaxValue : (sx > 0 ? (x + 1) * size - from.X.Raw : from.X.Raw - x * size);
                long nz = sz == 0 ? long.MaxValue : (sz > 0 ? (z + 1) * size - from.Z.Raw : from.Z.Raw - z * size);
                bool crossX = sx != 0 && nx <= ax, crossZ = sz != 0 && nz <= az;
                if (!crossX && !crossZ) return to;
                int compare = !crossX ? 1 : !crossZ ? -1 : (new BigInteger(nx) * az).CompareTo(new BigInteger(nz) * ax);
                bool moveX = compare <= 0, moveZ = compare >= 0;
                bool validX = !moveX || (x + sx >= 0 && x + sx < width && IsPassable(z * width + x + sx));
                bool validZ = !moveZ || (z + sz >= 0 && z + sz < height && IsPassable((z + sz) * width + x));
                bool validDiagonal = !moveX || !moveZ || (validX && validZ && IsPassable((z + sz) * width + x + sx));
                if (!validX || !validZ || !validDiagonal)
                {
                    long n = moveX ? nx : nz, d = moveX ? ax : az;
                    long px = from.X.Raw + (long)(new BigInteger(dx) * n / d);
                    long pz = from.Z.Raw + (long)(new BigInteger(dz) * n / d);
                    if (moveX && sx > 0) px--; if (moveZ && sz > 0) pz--;
                    var result = new SimPoint(Fix64.FromRaw(px), Fix64.FromRaw(pz));
                    return IsPassable(Cell(result)) ? result : from;
                }
                if (moveX) x += sx; if (moveZ) z += sz;
            }
        }
    }
}
