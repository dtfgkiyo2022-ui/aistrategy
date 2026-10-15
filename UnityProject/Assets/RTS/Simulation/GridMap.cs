using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>Integer grid and deterministic, bounded army path search. Ver.1 terrain never changes; Ver.3 buildings
    /// change it only through <see cref="SetPassable"/>, which drops every cache.</summary>
    public sealed class GridMap
    {
        internal Action<string, bool> Measure;
        /// <summary>Optional faction-specific overlay. It is unset for every old map and then the old terrain is used.</summary>
        internal Func<int, uint, bool> FactionPassability;
        private readonly int width, height;
        private readonly long size;
        private readonly bool[] passable;
        /// <summary>Counts the changes made through SetPassable, so a reader can keep its own copy until the next one.
        /// Not part of the state: it only says when to rebuild a view.</summary>
        internal int PassableVersion { get; private set; }
        // Derived from immutable terrain only. FIFO eviction changes cost, never route choices.
        private readonly Dictionary<(uint Faction, int Target), int[]> routes = new Dictionary<(uint, int), int[]>();
        private readonly Queue<(uint Faction, int Target)> routeOrder = new Queue<(uint, int)>();
        // FindPath is a pure function of the immutable terrain, the start cell and the exact goal point, and the decision
        // layer asks for the same routes again and again. Callers only read the returned array. FIFO eviction bounds the
        // memory and, like the routes above, changes cost only, never a result.
        private const int PathCacheLimit = 4096;
        private readonly Dictionary<(uint Faction, int Start, long GoalX, long GoalZ), int[]> paths = new Dictionary<(uint, int, long, long), int[]>();
        private readonly Queue<(uint Faction, int Start, long GoalX, long GoalZ)> pathOrder = new Queue<(uint, int, long, long)>();
        // Cell centres of the cached paths, kept and dropped together with them. Like a cached path, the array is
        // shared by every caller and must only be read.
        private readonly Dictionary<(uint Faction, int Start, long GoalX, long GoalZ), SimPoint[]> pathCenters = new Dictionary<(uint, int, long, long), SimPoint[]>();
        public int[] SharedRoute(int start, SimPoint goal)
            => SharedRoute(start, goal, 0);

        public int[] SharedRoute(int start, SimPoint goal, uint factionId)
        {
            Measure?.Invoke("Pathfinding", true);
            try { return SharedRouteCore(start, goal, factionId); }
            finally { Measure?.Invoke("Pathfinding", false); }
        }
        private int[] SharedRouteCore(int start, SimPoint goal, uint factionId)
        {
            if (!IsPassableFor(start, factionId)) return Array.Empty<int>();
            int target = Cell(goal);
            if (!IsPassableFor(target, factionId))
            {
                target = -1;
                long best = 0;
                for (int i = 0; i < passable.Length; i++)
                    if (IsPassableFor(i, factionId))
                    {
                        var d = Distance(Center(i), goal);
                        if (target < 0 || d < best) { target = i; best = d; }
                    }
            }
            if (target < 0) return Array.Empty<int>();
            var routeKey = (factionId, target);
            if (!routes.TryGetValue(routeKey, out var next))
            {
                // One reverse BFS per destination, shared by all soldiers. The scenario validator bounds the grid.
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
                    if (!IsPassableFor(cell, factionId) || next[cell] >= 0) return;
                    next[cell] = parent; queue[tail++] = cell;
                }
                if (routeOrder.Count == 16) routes.Remove(routeOrder.Dequeue());
                routes.Add(routeKey, next); routeOrder.Enqueue(routeKey);
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
        /// <summary>
        /// Ver.3: a building opens or closes a cell. Every cached route and path was derived from the old terrain, so all
        /// of them go. The simulation calls this at fixed points in a tick, so the same inputs clear the same caches.
        /// </summary>
        internal void SetPassable(int cell, bool value)
        {
            if (passable[cell] == value) return;
            passable[cell] = value;
            PassableVersion++;
            routes.Clear(); routeOrder.Clear();
            paths.Clear(); pathOrder.Clear(); pathCenters.Clear();
        }
        /// <summary>V3-16: the faction overlay changed (a tollgate appeared or went), so every cached route and path goes.</summary>
        internal void InvalidateRoutes() { routes.Clear(); routeOrder.Clear(); paths.Clear(); pathOrder.Clear(); pathCenters.Clear(); }
        public bool IsPassable(int cell) => cell >= 0 && cell < passable.Length && passable[cell];
        public bool IsPassableFor(int cell, uint factionId)
            => cell >= 0 && cell < passable.Length && (FactionPassability == null ? passable[cell] : FactionPassability(cell, factionId));
        public int Cell(SimPoint p)
        {
            if (p.X.Raw < 0 || p.Z.Raw < 0 || p.X.Raw >= width * size || p.Z.Raw >= height * size) return -1;
            return (int)(p.Z.Raw / size) * width + (int)(p.X.Raw / size);
        }
        public SimPoint Center(int cell) => new SimPoint(Fix64.FromRaw(cell % width * size + size / 2), Fix64.FromRaw(cell / width * size + size / 2));
        private int H(int a, int b) => Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);
        public int[] FindPath(int start, SimPoint goal)
            => FindPath(start, goal, 0);

        public int[] FindPath(int start, SimPoint goal, uint factionId)
        {
            Measure?.Invoke("Pathfinding", true);
            try
            {
                var key = (factionId, start, goal.X.Raw, goal.Z.Raw);
                if (paths.TryGetValue(key, out var cached)) return cached;
                var path = FindPathCore(start, goal, factionId);
                if (pathOrder.Count == PathCacheLimit) { var oldest = pathOrder.Dequeue(); paths.Remove(oldest); pathCenters.Remove(oldest); }
                paths.Add(key, path); pathOrder.Enqueue(key);
                return path;
            }
            finally { Measure?.Invoke("Pathfinding", false); }
        }
        /// <summary>The centres of FindPath's cells. The decision asks for them on every tick, for every army and objective.</summary>
        public SimPoint[] FindPathCenters(int start, SimPoint goal, uint factionId)
        {
            var path = FindPath(start, goal, factionId);
            var key = (factionId, start, goal.X.Raw, goal.Z.Raw);
            if (pathCenters.TryGetValue(key, out var cached)) return cached;
            var centers = new SimPoint[path.Length];
            for (int i = 0; i < path.Length; i++) centers[i] = Center(path[i]);
            if (paths.ContainsKey(key)) pathCenters[key] = centers;
            return centers;
        }

        private int[] searchG, searchParent;
        private bool[] searchClosed;

        private int[] FindPathCore(int start, SimPoint goal, uint factionId)
        {
            if (!IsPassableFor(start, factionId)) return Array.Empty<int>();
            int target = Cell(goal);
            // The three work tables are kept between searches (a search fills them again before reading), so a
            // cache miss no longer allocates three arrays the size of the map. One GridMap is stepped by one thread.
            if (searchG == null) { searchG = new int[passable.Length]; searchParent = new int[passable.Length]; searchClosed = new bool[passable.Length]; }
            var g = searchG; Array.Fill(g, int.MaxValue);
            var parent = searchParent; Array.Fill(parent, -1);
            var closed = searchClosed; Array.Clear(closed, 0, closed.Length);
            var open = new SortedSet<(int f, int h, int id)>();
            int heuristicCell = target >= 0 ? target : start;
            g[start] = 0; open.Add((H(start, heuristicCell), H(start, heuristicCell), start));
            int best = start, expanded = 0;
            long bestDistance = Distance(Center(start), goal);
            while (open.Count > 0 && expanded < passable.Length)
            {
                var item = open.Min; open.Remove(item);
                int id = item.id; closed[id] = true; expanded++;
                var distance = Distance(Center(id), goal);
                if (distance < bestDistance || (distance == bestDistance && id < best)) { best = id; bestDistance = distance; }
                if (id == target) { best = id; break; }
                int x = id % width, z = id / width;
                // Same neighbour order as before (north, east, south, west), without an array per expanded cell.
                for (int side = 0; side < 4; side++)
                {
                    int next = side == 0 ? (z + 1 < height ? id + width : -1) : side == 1 ? (x + 1 < width ? id + 1 : -1)
                        : side == 2 ? (z > 0 ? id - width : -1) : (x > 0 ? id - 1 : -1);
                    if (!IsPassableFor(next, factionId) || closed[next] || g[next] <= g[id] + 1) continue;
                    int h = H(next, heuristicCell);
                    if (g[next] != int.MaxValue) open.Remove((g[next] + h, h, next));
                    g[next] = g[id] + 1; parent[next] = id; open.Add((g[next] + h, h, next));
                }
            }
            var path = new List<int>();
            for (int id = best; id >= 0; id = parent[id]) path.Add(id);
            path.Reverse(); return path.ToArray();
        }
        // Squared distance in Fix64 raw units. Both points lie on the validated map, so every coordinate
        // difference is far below 2^31 and its square below 2^62; checked arithmetic turns any excess into an error.
        private static long Distance(SimPoint a, SimPoint b)
        {
            long x = a.X.Raw - b.X.Raw, z = a.Z.Raw - b.Z.Raw;
            return checked(x * x + z * z);
        }
        /// <summary>Walk every crossed boundary, checking both side cells at exact corners.</summary>
        public SimPoint ClipMove(SimPoint from, SimPoint to)
            => ClipMove(from, to, 0);

        public SimPoint ClipMove(SimPoint from, SimPoint to, uint factionId)
        {
            int cell = Cell(from); if (!IsPassableFor(cell, factionId)) return from;
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
                bool validX = !moveX || (x + sx >= 0 && x + sx < width && IsPassableFor(z * width + x + sx, factionId));
                bool validZ = !moveZ || (z + sz >= 0 && z + sz < height && IsPassableFor((z + sz) * width + x, factionId));
                bool validDiagonal = !moveX || !moveZ || (validX && validZ && IsPassableFor((z + sz) * width + x + sx, factionId));
                if (!validX || !validZ || !validDiagonal)
                {
                    long n = moveX ? nx : nz, d = moveX ? ax : az;
                    long px = from.X.Raw + checked(dx * n) / d;
                    long pz = from.Z.Raw + checked(dz * n) / d;
                    if (moveX && sx > 0) px--; if (moveZ && sz > 0) pz--;
                    var result = new SimPoint(Fix64.FromRaw(px), Fix64.FromRaw(pz));
                    return IsPassableFor(Cell(result), factionId) ? result : from;
                }
                if (moveX) x += sx; if (moveZ) z += sz;
            }
        }
    }
}
