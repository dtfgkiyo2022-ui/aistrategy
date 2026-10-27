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
        /// <summary>Counts changes to route passability, so a reader can keep its own copy until the next one.
        /// Not part of the state: it only says when to rebuild a view.</summary>
        internal int PassableVersion { get; private set; }
        // Derived from immutable terrain only. FIFO eviction changes cost, never route choices.
        private readonly Dictionary<(uint Faction, int Target), int[]> routes = new Dictionary<(uint, int), int[]>();
        private readonly Queue<(uint Faction, int Target)> routeOrder = new Queue<(uint, int)>();
        // Reverse-BFS tables are work buffers. Route answers are still invalidated with the terrain, but their arrays can
        // be reused by the next search instead of allocating a map-sized int[] after every building change.
        private readonly Stack<int[]> routeBuffers = new Stack<int[]>();
        private readonly Dictionary<int, Stack<int[]>> sharedPathBuffers = new Dictionary<int, Stack<int[]>>();
        private int[] sharedRouteQueue;
        // FindPath is a pure function of the immutable terrain, the start cell and the exact goal point, and the decision
        // layer asks for the same routes again and again. Callers only read the returned array. FIFO eviction bounds the
        // memory and, like the routes above, changes cost only, never a result.
        private const int PathCacheLimit = 4096;
        private readonly Dictionary<(uint Faction, int Start, long GoalX, long GoalZ), int[]> paths = new Dictionary<(uint, int, long, long), int[]>();
        private readonly Queue<(uint Faction, int Start, long GoalX, long GoalZ)> pathOrder = new Queue<(uint, int, long, long)>();
        // Cell centres of the cached paths, kept and dropped together with them. Like a cached path, the array is
        // shared by every caller and must only be read.
        private readonly Dictionary<(uint Faction, int Start, long GoalX, long GoalZ), SimPoint[]> pathCenters = new Dictionary<(uint, int, long, long), SimPoint[]>();
        private readonly Dictionary<(uint Faction, int Start, long GoalX, long GoalZ), IReadOnlyList<SimPoint>> pathCenterViews = new Dictionary<(uint, int, long, long), IReadOnlyList<SimPoint>>();
        private static readonly int[] NoReplacements = new int[0];
        private static readonly SimPoint[] NoPointReplacements = new SimPoint[0];
        private readonly List<int> sharedPathScratch = new List<int>();
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
                next = routeBuffers.Count == 0 ? new int[passable.Length] : routeBuffers.Pop();
                Array.Fill(next, -1);
                if (sharedRouteQueue == null || sharedRouteQueue.Length < passable.Length)
                    sharedRouteQueue = new int[passable.Length];
                var queue = sharedRouteQueue; int head = 0, tail = 0;
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
                if (routeOrder.Count == 16)
                {
                    var evicted = routeOrder.Dequeue();
                    if (routes.Remove(evicted, out var buffer)) routeBuffers.Push(buffer);
                }
                routes.Add(routeKey, next); routeOrder.Enqueue(routeKey);
            }
            if (next[start] < 0) return Array.Empty<int>();
            sharedPathScratch.Clear();
            for (int cell = start; ; cell = next[cell])
            { sharedPathScratch.Add(cell); if (cell == target) break; }
            int[] result;
            if (!sharedPathBuffers.TryGetValue(sharedPathScratch.Count, out var available) || available.Count == 0)
                result = new int[sharedPathScratch.Count];
            else result = available.Pop();
            for (int i = 0; i < sharedPathScratch.Count; i++) result[i] = sharedPathScratch[i];
            return result;
        }

        internal void ReleaseSharedRoute(int[] path)
        {
            if (path == null || path.Length == 0) return;
            if (!sharedPathBuffers.TryGetValue(path.Length, out var available))
                sharedPathBuffers[path.Length] = available = new Stack<int[]>();
            available.Push(path);
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
            ClearSharedRoutes();
            paths.Clear(); pathOrder.Clear(); pathCenters.Clear(); pathCenterViews.Clear();
        }
        /// <summary>V3-16: the faction overlay changed (a tollgate appeared or went), so every cached route and path goes.</summary>
        internal void InvalidateRoutes()
        {
            PassableVersion++;
            ClearSharedRoutes();
            paths.Clear(); pathOrder.Clear(); pathCenters.Clear(); pathCenterViews.Clear();
        }

        private void ClearSharedRoutes()
        {
            foreach (var route in routes.Values) routeBuffers.Push(route);
            routes.Clear();
            routeOrder.Clear();
        }
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
                if (pathOrder.Count == PathCacheLimit) { var oldest = pathOrder.Dequeue(); paths.Remove(oldest); pathCenters.Remove(oldest); pathCenterViews.Remove(oldest); }
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
            if (paths.ContainsKey(key))
            {
                pathCenters[key] = centers;
                pathCenterViews[key] = new SharedPrefixList<SimPoint>(centers, centers.Length, NoReplacements, NoPointReplacements);
            }
            return centers;
        }

        /// <summary>Read-only centre view for decision DTOs. It is cached with the path so ContractList can share it.</summary>
        public IReadOnlyList<SimPoint> FindPathCenterView(int start, SimPoint goal, uint factionId)
        {
            var path = FindPath(start, goal, factionId);
            var key = (factionId, start, goal.X.Raw, goal.Z.Raw);
            if (pathCenterViews.TryGetValue(key, out var cached)) return cached;
            var centers = new SimPoint[path.Length];
            for (int i = 0; i < path.Length; i++) centers[i] = Center(path[i]);
            if (paths.ContainsKey(key))
            {
                pathCenters[key] = centers;
                var view = new SharedPrefixList<SimPoint>(centers, centers.Length, NoReplacements, NoPointReplacements);
                pathCenterViews[key] = view;
                return view;
            }
            return centers;
        }

        private int[] searchG, searchParent;
        private bool[] searchClosed;
        private struct PathHeapEntry
        {
            internal int F, H, Id;
            internal PathHeapEntry(int f, int h, int id) { F = f; H = h; Id = id; }
        }
        private PathHeapEntry[] pathHeap;
        private int[] pathHeapIndex;
        private int pathHeapCount;
        private readonly List<int> pathScratch = new List<int>();

        // This is the same total ordering as SortedSet<(int f, int h, int id)>.
        // The index table makes decrease-key remove the old tuple before the next Min,
        // so equal f/h values still come out in id order.
        private static bool HeapLess(PathHeapEntry a, PathHeapEntry b)
            => a.F < b.F || a.F == b.F && (a.H < b.H || a.H == b.H && a.Id < b.Id);

        private void HeapSwap(int a, int b)
        {
            var value = pathHeap[a]; pathHeap[a] = pathHeap[b]; pathHeap[b] = value;
            pathHeapIndex[pathHeap[a].Id] = a;
            pathHeapIndex[pathHeap[b].Id] = b;
        }

        private void HeapUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!HeapLess(pathHeap[index], pathHeap[parent])) break;
                HeapSwap(index, parent); index = parent;
            }
        }

        private void HeapDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1, right = left + 1, best = index;
                if (left < pathHeapCount && HeapLess(pathHeap[left], pathHeap[best])) best = left;
                if (right < pathHeapCount && HeapLess(pathHeap[right], pathHeap[best])) best = right;
                if (best == index) return;
                HeapSwap(index, best); index = best;
            }
        }

        private void HeapAddOrUpdate(int f, int h, int id)
        {
            int index = pathHeapIndex[id];
            if (index >= 0)
            {
                pathHeap[index] = new PathHeapEntry(f, h, id);
                HeapUp(index); HeapDown(pathHeapIndex[id]);
                return;
            }
            index = pathHeapCount++;
            pathHeap[index] = new PathHeapEntry(f, h, id);
            pathHeapIndex[id] = index;
            HeapUp(index);
        }

        private PathHeapEntry HeapPop()
        {
            var result = pathHeap[0];
            pathHeapIndex[result.Id] = -1;
            pathHeapCount--;
            if (pathHeapCount > 0)
            {
                pathHeap[0] = pathHeap[pathHeapCount];
                pathHeapIndex[pathHeap[0].Id] = 0;
                HeapDown(0);
            }
            return result;
        }

        private int[] FindPathCore(int start, SimPoint goal, uint factionId)
        {
            if (!IsPassableFor(start, factionId)) return Array.Empty<int>();
            int target = Cell(goal);
            // The three work tables are kept between searches (a search fills them again before reading), so a
            // cache miss no longer allocates three arrays the size of the map. One GridMap is stepped by one thread.
            if (searchG == null)
            {
                searchG = new int[passable.Length]; searchParent = new int[passable.Length]; searchClosed = new bool[passable.Length];
                pathHeap = new PathHeapEntry[passable.Length]; pathHeapIndex = new int[passable.Length];
            }
            var g = searchG; Array.Fill(g, int.MaxValue);
            var parent = searchParent; Array.Fill(parent, -1);
            var closed = searchClosed; Array.Clear(closed, 0, closed.Length);
            Array.Fill(pathHeapIndex, -1); pathHeapCount = 0;
            int heuristicCell = target >= 0 ? target : start;
            g[start] = 0; int startH = H(start, heuristicCell); HeapAddOrUpdate(startH, startH, start);
            int best = start, expanded = 0;
            long bestDistance = Distance(Center(start), goal);
            while (pathHeapCount > 0 && expanded < passable.Length)
            {
                var item = HeapPop();
                int id = item.Id; closed[id] = true; expanded++;
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
                    g[next] = g[id] + 1; parent[next] = id;
                    HeapAddOrUpdate(g[next] + h, h, next);
                }
            }
            pathScratch.Clear();
            for (int id = best; id >= 0; id = parent[id]) pathScratch.Add(id);
            pathScratch.Reverse(); return pathScratch.ToArray();
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
