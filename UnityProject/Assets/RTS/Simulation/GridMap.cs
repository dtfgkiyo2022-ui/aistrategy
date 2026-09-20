using System;
using System.Collections.Generic;
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
        // FindPath is a pure function of the immutable terrain, the start cell and the exact goal point, and the decision
        // layer asks for the same routes again and again. Callers only read the returned array. FIFO eviction bounds the
        // memory and, like the routes above, changes cost only, never a result.
        private const int PathCacheLimit = 4096;
        private readonly Dictionary<(int Start, long GoalX, long GoalZ), int[]> paths = new Dictionary<(int, long, long), int[]>();
        private readonly Queue<(int Start, long GoalX, long GoalZ)> pathOrder = new Queue<(int, long, long)>();
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
                long best = 0;
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
            // Every distance and interpolation below is exact 64-bit arithmetic. With each side under 2^30 raw units
            // (16384 m) a squared difference is under 2^60 and a product of a difference and a cell offset likewise.
            if ((long)width * size > (1L << 30) || (long)height * size > (1L << 30))
                throw new ArgumentException("The map is too large for exact 64-bit distance arithmetic.");
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
            try
            {
                var key = (start, goal.X.Raw, goal.Z.Raw);
                if (paths.TryGetValue(key, out var cached)) return cached;
                var path = FindPathCore(start, goal);
                if (pathOrder.Count == PathCacheLimit) paths.Remove(pathOrder.Dequeue());
                paths.Add(key, path); pathOrder.Enqueue(key);
                return path;
            }
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
            long bestDistance = Distance(Center(start), goal);
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
        // Squared distance in Fix64 raw units. Both points lie on the map (at most 8192 cells), so every coordinate
        // difference is far below 2^31 and its square below 2^62; checked arithmetic turns any excess into an error.
        private static long Distance(SimPoint a, SimPoint b)
        {
            long x = a.X.Raw - b.X.Raw, z = a.Z.Raw - b.Z.Raw;
            return checked(x * x + z * z);
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
                int compare = !crossX ? 1 : !crossZ ? -1 : checked(nx * az).CompareTo(checked(nz * ax));
                bool moveX = compare <= 0, moveZ = compare >= 0;
                bool validX = !moveX || (x + sx >= 0 && x + sx < width && IsPassable(z * width + x + sx));
                bool validZ = !moveZ || (z + sz >= 0 && z + sz < height && IsPassable((z + sz) * width + x));
                bool validDiagonal = !moveX || !moveZ || (validX && validZ && IsPassable((z + sz) * width + x + sx));
                if (!validX || !validZ || !validDiagonal)
                {
                    long n = moveX ? nx : nz, d = moveX ? ax : az;
                    long px = from.X.Raw + checked(dx * n) / d;
                    long pz = from.Z.Raw + checked(dz * n) / d;
                    if (moveX && sx > 0) px--; if (moveZ && sz > 0) pz--;
                    var result = new SimPoint(Fix64.FromRaw(px), Fix64.FromRaw(pz));
                    return IsPassable(Cell(result)) ? result : from;
                }
                if (moveX) x += sx; if (moveZ) z += sz;
            }
        }
    }
}
