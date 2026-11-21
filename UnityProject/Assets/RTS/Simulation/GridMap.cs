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
        /// <summary>Optional breach-only terrain. It is consulted only by the explicit fallback route search.</summary>
        internal Func<int, uint, bool> BreakablePassability;
        internal bool SideFair;
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
        private const int ArmyFlowPathCacheLimit = 4096;
        private readonly Dictionary<(bool ArmyFlow, uint Faction, int Start, long GoalX, long GoalZ, int Lookahead), int[]> armyFlowPaths
            = new Dictionary<(bool, uint, int, long, long, int), int[]>();
        private readonly Queue<(bool ArmyFlow, uint Faction, int Start, long GoalX, long GoalZ, int Lookahead)> armyFlowPathOrder
            = new Queue<(bool, uint, int, long, long, int)>();
        private readonly Dictionary<int, Stack<int[]>> armyFlowSharedPathBuffers = new Dictionary<int, Stack<int[]>>();
        private readonly List<int> armyFlowPathScratch = new List<int>();
        private int[] armyFlowSearchG, armyFlowSearchParent, armyFlowPathHeap, armyFlowPathHeapIndex;
        private bool[] armyFlowSearchClosed;
        private int armyFlowPathHeapCount;
        private int armyFlowTieStart, armyFlowTieGoal;
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
                int routeDx = Math.Sign(start % width - target % width);
                int routeDz = Math.Sign(start / width - target / width);
                while (head < tail)
                {
                    int id = queue[head++], x = id % width, z = id / width;
                    if (!SideFair)
                    {
                        Visit(z + 1 < height ? id + width : -1, id);
                        Visit(x + 1 < width ? id + 1 : -1, id);
                        Visit(z > 0 ? id - width : -1, id);
                        Visit(x > 0 ? id - 1 : -1, id);
                    }
                    else if (routeDx > 0 && routeDz > 0)
                    {
                        Visit(x + 1 < width ? id + 1 : -1, id);
                        Visit(z + 1 < height ? id + width : -1, id);
                        Visit(x > 0 ? id - 1 : -1, id);
                        Visit(z > 0 ? id - width : -1, id);
                    }
                    else if (routeDx < 0 && routeDz < 0)
                    {
                        Visit(x > 0 ? id - 1 : -1, id);
                        Visit(z > 0 ? id - width : -1, id);
                        Visit(x + 1 < width ? id + 1 : -1, id);
                        Visit(z + 1 < height ? id + width : -1, id);
                    }
                    else if (routeDx > 0 && routeDz < 0)
                    {
                        Visit(x + 1 < width ? id + 1 : -1, id);
                        Visit(z > 0 ? id - width : -1, id);
                        Visit(x > 0 ? id - 1 : -1, id);
                        Visit(z + 1 < height ? id + width : -1, id);
                    }
                    else if (routeDx < 0 && routeDz > 0)
                    {
                        Visit(x > 0 ? id - 1 : -1, id);
                        Visit(z + 1 < height ? id + width : -1, id);
                        Visit(x + 1 < width ? id + 1 : -1, id);
                        Visit(z > 0 ? id - width : -1, id);
                    }
                    else if (routeDx > 0)
                    {
                        Visit(x + 1 < width ? id + 1 : -1, id);
                        Visit(z + 1 < height ? id + width : -1, id);
                        Visit(z > 0 ? id - width : -1, id);
                        Visit(x > 0 ? id - 1 : -1, id);
                    }
                    else if (routeDx < 0)
                    {
                        Visit(x > 0 ? id - 1 : -1, id);
                        Visit(z > 0 ? id - width : -1, id);
                        Visit(z + 1 < height ? id + width : -1, id);
                        Visit(x + 1 < width ? id + 1 : -1, id);
                    }
                    else if (routeDz > 0)
                    {
                        Visit(z + 1 < height ? id + width : -1, id);
                        Visit(x + 1 < width ? id + 1 : -1, id);
                        Visit(x > 0 ? id - 1 : -1, id);
                        Visit(z > 0 ? id - width : -1, id);
                    }
                    else
                    {
                        Visit(z > 0 ? id - width : -1, id);
                        Visit(x > 0 ? id - 1 : -1, id);
                        Visit(x + 1 < width ? id + 1 : -1, id);
                        Visit(z + 1 < height ? id + width : -1, id);
                    }
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

        public int[] SharedBreakableRoute(int start, SimPoint goal, uint factionId)
        {
            Measure?.Invoke("Pathfinding.Breach", true);
            try { return FindBreakablePathCore(start, goal, factionId); }
            finally { Measure?.Invoke("Pathfinding.Breach", false); }
        }

        public int[] FindBreakablePath(int start, SimPoint goal, uint factionId)
            => FindBreakablePathCore(start, goal, factionId);

        private bool IsBreakableFor(int cell, uint factionId)
            => IsPassableFor(cell, factionId) || BreakablePassability != null && BreakablePassability(cell, factionId);

        private int[] FindBreakablePathCore(int start, SimPoint goal, uint factionId)
        {
            if (!IsBreakableFor(start, factionId)) return System.Array.Empty<int>();
            int target = Cell(goal);
            if (!IsBreakableFor(target, factionId))
            {
                target = -1;
                long best = 0;
                for (int i = 0; i < passable.Length; i++)
                    if (IsBreakableFor(i, factionId))
                    {
                        long distance = Distance(Center(i), goal);
                        if (target < 0 || distance < best) { target = i; best = distance; }
                    }
            }
            if (target < 0) return System.Array.Empty<int>();
            var parent = new int[passable.Length];
            var queue = new int[passable.Length];
            System.Array.Fill(parent, -1);
            int head = 0, tail = 0;
            queue[tail++] = start; parent[start] = start;
            while (head < tail && parent[target] < 0)
            {
                int id = queue[head++], x = id % width, z = id / width;
                Visit(z + 1 < height ? id + width : -1, id);
                Visit(x + 1 < width ? id + 1 : -1, id);
                Visit(z > 0 ? id - width : -1, id);
                Visit(x > 0 ? id - 1 : -1, id);
            }
            if (parent[target] < 0) return System.Array.Empty<int>();
            var route = new List<int>();
            for (int cell = target; ; cell = parent[cell])
            {
                route.Add(cell);
                if (cell == start) break;
            }
            route.Reverse();
            return route.ToArray();
            void Visit(int cell, int parentCell)
            {
                if (cell < 0 || cell >= passable.Length || parent[cell] >= 0 || !IsBreakableFor(cell, factionId)) return;
                parent[cell] = parentCell;
                queue[tail++] = cell;
            }
        }

        public int[] SharedArmyFlowRoute(int start, SimPoint goal, uint factionId, int lookahead)
        {
            Measure?.Invoke("Pathfinding", true);
            try
            {
                var path = FindArmyFlowPathCore(start, goal, factionId, lookahead);
                if (path.Length == 0) return path;
                if (!armyFlowSharedPathBuffers.TryGetValue(path.Length, out var available) || available.Count == 0)
                    return (int[])path.Clone();
                var result = available.Pop();
                Array.Copy(path, result, path.Length);
                return result;
            }
            finally { Measure?.Invoke("Pathfinding", false); }
        }

        public int[] FindArmyFlowPath(int start, SimPoint goal, uint factionId)
            => FindArmyFlowPath(start, goal, factionId, 16);

        public int[] FindArmyFlowPath(int start, SimPoint goal, uint factionId, int lookahead)
        {
            Measure?.Invoke("Pathfinding", true);
            try { return FindArmyFlowPathCore(start, goal, factionId, lookahead); }
            finally { Measure?.Invoke("Pathfinding", false); }
        }

        private int[] FindArmyFlowPathCore(int start, SimPoint goal, uint factionId, int lookahead)
        {
            lookahead = Math.Max(1, lookahead);
            var key = (true, factionId, start, goal.X.Raw, goal.Z.Raw, lookahead);
            if (armyFlowPaths.TryGetValue(key, out var cached)) return cached;
            if (!IsPassableFor(start, factionId)) return Array.Empty<int>();
            int requestedTarget = Cell(goal);
            int target = requestedTarget;
            if (target >= 0 && !IsPassableFor(target, factionId)) target = -1;
            EnsureArmyFlowSearchBuffers();
            var g = armyFlowSearchG; Array.Fill(g, int.MaxValue);
            var parent = armyFlowSearchParent; Array.Fill(parent, -1);
            var closed = armyFlowSearchClosed; Array.Clear(closed, 0, closed.Length);
            Array.Fill(armyFlowPathHeapIndex, -1); armyFlowPathHeapCount = 0;
            int heuristicCell = requestedTarget >= 0 ? requestedTarget : start;
            armyFlowTieStart = start; armyFlowTieGoal = heuristicCell;
            g[start] = 0;
            int startH = ArmyFlowHeuristic(start, heuristicCell);
            ArmyFlowHeapAdd(startH, startH, start);
            int best = start, expanded = 0;
            long bestDistance = Distance(Center(start), goal);
            while (armyFlowPathHeapCount > 0 && expanded < passable.Length)
            {
                var item = ArmyFlowHeapPop();
                int id = item.Id; closed[id] = true; expanded++;
                long distance = Distance(Center(id), goal);
                if (distance < bestDistance || distance == bestDistance && (SideFair ? ArmyFlowTieBetter(id, best) : id < best))
                { best = id; bestDistance = distance; }
                if (id == target) { best = id; break; }
                int x = id % width, z = id / width;
                for (int direction = 0; direction < 8; direction++)
                {
                    int dx = direction == 0 || direction == 1 || direction == 7 ? -1 : direction == 2 || direction == 6 ? 0 : 1;
                    int dz = direction == 0 || direction == 7 ? 1 : direction == 1 || direction == 6 ? 0 : direction == 2 || direction == 3 ? -1 : 1;
                    // Order is north, north-east, east, south-east, south, south-west, west, north-west.
                    if (direction == 0) { dx = 0; dz = 1; }
                    else if (direction == 1) { dx = 1; dz = 1; }
                    else if (direction == 2) { dx = 1; dz = 0; }
                    else if (direction == 3) { dx = 1; dz = -1; }
                    else if (direction == 4) { dx = 0; dz = -1; }
                    else if (direction == 5) { dx = -1; dz = -1; }
                    else if (direction == 6) { dx = -1; dz = 0; }
                    else { dx = -1; dz = 1; }
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nx >= width || nz < 0 || nz >= height) continue;
                    int next = nz * width + nx;
                    if (!IsPassableFor(next, factionId) || closed[next]) continue;
                    if (dx != 0 && dz != 0 && (!IsPassableFor(z * width + nx, factionId)
                        || !IsPassableFor(nz * width + x, factionId))) continue;
                    int cost = dx == 0 || dz == 0 ? 10 : 14;
                    int nextG = checked(g[id] + cost);
                    if (g[next] <= nextG) continue;
                    int h = ArmyFlowHeuristic(next, heuristicCell);
                    g[next] = nextG; parent[next] = id;
                    ArmyFlowHeapAdd(checked(nextG + h), h, next);
                }
            }
            armyFlowPathScratch.Clear();
            for (int id = best; id >= 0; id = parent[id]) armyFlowPathScratch.Add(id);
            armyFlowPathScratch.Reverse();
            var route = ArmyFlowShortcut(armyFlowPathScratch, factionId, lookahead);
            if (armyFlowPathOrder.Count == ArmyFlowPathCacheLimit)
            {
                var oldest = armyFlowPathOrder.Dequeue(); armyFlowPaths.Remove(oldest);
            }
            armyFlowPaths.Add(key, route); armyFlowPathOrder.Enqueue(key);
            return route;
        }

        internal void ReleaseArmyFlowRoute(int[] path)
        {
            if (path == null || path.Length == 0) return;
            if (!armyFlowSharedPathBuffers.TryGetValue(path.Length, out var available))
                armyFlowSharedPathBuffers[path.Length] = available = new Stack<int[]>();
            available.Push(path);
        }

        private int[] ArmyFlowShortcut(IReadOnlyList<int> path, uint factionId, int lookahead)
        {
            if (path.Count < 3) { var shortPath = new int[path.Count]; for (int i = 0; i < path.Count; i++) shortPath[i] = path[i]; return shortPath; }
            var result = new List<int>(path.Count); int current = 0; result.Add(path[0]);
            while (current < path.Count - 1)
            {
                int last = Math.Min(path.Count - 1, current + lookahead), chosen = current + 1;
                for (int candidate = last; candidate > current; candidate--)
                    if (ArmyFlowClear(Center(path[current]), Center(path[candidate]), factionId)) { chosen = candidate; break; }
                result.Add(path[chosen]); current = chosen;
            }
            return result.ToArray();
        }

        private bool ArmyFlowClear(SimPoint from, SimPoint to, uint factionId)
            => IsPassableFor(Cell(to), factionId) && ClipMove(from, to, factionId).X.Raw == to.X.Raw
                && ClipMove(from, to, factionId).Z.Raw == to.Z.Raw;

        private int ArmyFlowHeuristic(int a, int b)
        {
            int dx = Math.Abs(a % width - b % width), dz = Math.Abs(a / width - b / width);
            return checked(10 * (dx + dz) - 6 * Math.Min(dx, dz));
        }

        private void EnsureArmyFlowSearchBuffers()
        {
            if (armyFlowSearchG != null) return;
            armyFlowSearchG = new int[passable.Length]; armyFlowSearchParent = new int[passable.Length];
            armyFlowSearchClosed = new bool[passable.Length]; armyFlowPathHeap = new int[passable.Length];
            armyFlowPathHeapF = new int[passable.Length]; armyFlowPathHeapH = new int[passable.Length];
            armyFlowPathHeapIndex = new int[passable.Length];
        }

        private void ArmyFlowHeapAdd(int f, int h, int id)
        {
            int index = armyFlowPathHeapIndex[id];
            if (index >= 0) { ArmyFlowHeapSet(index, f, h, id); ArmyFlowHeapUp(index); ArmyFlowHeapDown(armyFlowPathHeapIndex[id]); return; }
            index = armyFlowPathHeapCount++; armyFlowPathHeap[index] = id; armyFlowPathHeapIndex[id] = index;
            armyFlowPathHeapF[index] = f; armyFlowPathHeapH[index] = h; ArmyFlowHeapUp(index);
        }

        private int[] armyFlowPathHeapF, armyFlowPathHeapH;
        private void ArmyFlowHeapSet(int index, int f, int h, int id)
        { armyFlowPathHeap[index] = id; armyFlowPathHeapF[index] = f; armyFlowPathHeapH[index] = h; }
        private bool ArmyFlowHeapLess(int a, int b)
        {
            if (armyFlowPathHeapF[a] != armyFlowPathHeapF[b]) return armyFlowPathHeapF[a] < armyFlowPathHeapF[b];
            if (armyFlowPathHeapH[a] != armyFlowPathHeapH[b]) return armyFlowPathHeapH[a] < armyFlowPathHeapH[b];
            if (!SideFair) return armyFlowPathHeap[a] < armyFlowPathHeap[b];
            var at = ArmyFlowTie(armyFlowPathHeap[a]); var bt = ArmyFlowTie(armyFlowPathHeap[b]);
            return at.Forward != bt.Forward ? at.Forward > bt.Forward
                : at.Lateral != bt.Lateral ? at.Lateral > bt.Lateral : armyFlowPathHeap[a] < armyFlowPathHeap[b];
        }

        private (int Forward, int Lateral) ArmyFlowTie(int cell)
        {
            int sx = Math.Sign(armyFlowTieGoal % width - armyFlowTieStart % width);
            int sz = Math.Sign(armyFlowTieGoal / width - armyFlowTieStart / width);
            if (sx == 0) sx = 1;
            if (sz == 0) sz = 1;
            int dx = cell % width - armyFlowTieStart % width, dz = cell / width - armyFlowTieStart / width;
            return (dx * sx + dz * sz, -dx * sz + dz * sx);
        }

        private bool ArmyFlowTieBetter(int candidate, int current)
        {
            var a = ArmyFlowTie(candidate); var b = ArmyFlowTie(current);
            return a.Forward != b.Forward ? a.Forward > b.Forward
                : a.Lateral != b.Lateral ? a.Lateral > b.Lateral : candidate < current;
        }

        private void ArmyFlowHeapSwap(int a, int b)
        {
            int id = armyFlowPathHeap[a]; armyFlowPathHeap[a] = armyFlowPathHeap[b]; armyFlowPathHeap[b] = id;
            int f = armyFlowPathHeapF[a]; armyFlowPathHeapF[a] = armyFlowPathHeapF[b]; armyFlowPathHeapF[b] = f;
            int h = armyFlowPathHeapH[a]; armyFlowPathHeapH[a] = armyFlowPathHeapH[b]; armyFlowPathHeapH[b] = h;
            armyFlowPathHeapIndex[armyFlowPathHeap[a]] = a; armyFlowPathHeapIndex[armyFlowPathHeap[b]] = b;
        }
        private void ArmyFlowHeapUp(int index)
        { while (index > 0) { int parent = (index - 1) / 2; if (!ArmyFlowHeapLess(index, parent)) break; ArmyFlowHeapSwap(index, parent); index = parent; } }
        private void ArmyFlowHeapDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1, right = left + 1, best = index;
                if (left < armyFlowPathHeapCount && ArmyFlowHeapLess(left, best)) best = left;
                if (right < armyFlowPathHeapCount && ArmyFlowHeapLess(right, best)) best = right;
                if (best == index) return; ArmyFlowHeapSwap(index, best); index = best;
            }
        }
        private (int F, int H, int Id) ArmyFlowHeapPop()
        {
            int id = armyFlowPathHeap[0], f = armyFlowPathHeapF[0], h = armyFlowPathHeapH[0];
            armyFlowPathHeapIndex[id] = -1; armyFlowPathHeapCount--;
            if (armyFlowPathHeapCount > 0)
            {
                armyFlowPathHeap[0] = armyFlowPathHeap[armyFlowPathHeapCount]; armyFlowPathHeapF[0] = armyFlowPathHeapF[armyFlowPathHeapCount];
                armyFlowPathHeapH[0] = armyFlowPathHeapH[armyFlowPathHeapCount]; armyFlowPathHeapIndex[armyFlowPathHeap[0]] = 0; ArmyFlowHeapDown(0);
            }
            return (f, h, id);
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
            armyFlowPaths.Clear(); armyFlowPathOrder.Clear();
            ClearReachability();
        }
        /// <summary>V3-16: the faction overlay changed (a tollgate appeared or went), so every cached route and path goes.</summary>
        internal void InvalidateRoutes()
        {
            PassableVersion++;
            ClearSharedRoutes();
            paths.Clear(); pathOrder.Clear(); pathCenters.Clear(); pathCenterViews.Clear();
            armyFlowPaths.Clear(); armyFlowPathOrder.Clear();
            ClearReachability();
        }

        // 11-22 (design 93): FindPath toward a goal the start cannot reach (an enemy building's blocked cell, a walled
        // base) used to close every reachable cell to learn which one lies nearest the goal; late in a match that was
        // most of a tick. The nearest reachable cell is known in advance from the start's connected area, so the same
        // A* stops when it closes that cell. Up to that moment the search is unchanged, so the returned path is the
        // same, bit for bit. These tables follow the path cache: built lazily, cleared at the same two points (the
        // only places passability can change), and only looked up by key, never enumerated.
        internal bool UnreachedStop = true;
        private readonly Dictionary<uint, int[]> reachLabels = new Dictionary<uint, int[]>();
        private readonly Dictionary<uint, (int[] Cells, int[] Offsets)> reachAreas = new Dictionary<uint, (int[], int[])>();
        private readonly Dictionary<(uint Faction, int Label, long GoalX, long GoalZ), int[]> reachNearest
            = new Dictionary<(uint, int, long, long), int[]>();

        private void ClearReachability()
        {
            reachLabels.Clear(); reachAreas.Clear(); reachNearest.Clear();
        }

        /// <summary>Labels every cell with its 4-connected passable area for the faction (0 when not passable).</summary>
        private int[] ReachLabels(uint factionId)
        {
            if (reachLabels.TryGetValue(factionId, out var labels)) return labels;
            labels = new int[passable.Length];
            var order = new int[passable.Length];
            var offsets = new List<int> { 0, 0 };
            int count = 0, label = 0;
            for (int seed = 0; seed < passable.Length; seed++)
            {
                if (labels[seed] != 0 || !IsPassableFor(seed, factionId)) continue;
                label++;
                int head = count;
                labels[seed] = label; order[count++] = seed;
                while (head < count)
                {
                    int id = order[head++], x = id % width, z = id / width;
                    for (int side = 0; side < 4; side++)
                    {
                        int next = side == 0 ? (z + 1 < height ? id + width : -1) : side == 1 ? (x + 1 < width ? id + 1 : -1)
                            : side == 2 ? (z > 0 ? id - width : -1) : (x > 0 ? id - 1 : -1);
                        if (next < 0 || labels[next] != 0 || !IsPassableFor(next, factionId)) continue;
                        labels[next] = label; order[count++] = next;
                    }
                }
                offsets.Add(count);
            }
            reachLabels.Add(factionId, labels);
            reachAreas.Add(factionId, (order, offsets.ToArray()));
            return labels;
        }

        /// <summary>The cells of the start's area at the smallest distance to the goal (the A* tie-break picks one).</summary>
        private int[] NearestInArea(uint factionId, int label, SimPoint goal)
        {
            var key = (factionId, label, goal.X.Raw, goal.Z.Raw);
            if (reachNearest.TryGetValue(key, out var cells)) return cells;
            var area = reachAreas[factionId];
            long bestDistance = long.MaxValue;
            var found = new List<int>();
            for (int i = area.Offsets[label]; i < area.Offsets[label + 1]; i++)
            {
                int cell = area.Cells[i];
                long distance = Distance(Center(cell), goal);
                if (distance < bestDistance) { bestDistance = distance; found.Clear(); found.Add(cell); }
                else if (distance == bestDistance) found.Add(cell);
            }
            cells = found.ToArray();
            reachNearest.Add(key, cells);
            return cells;
        }

        /// <summary>
        /// -1 when the goal cell is reachable (the search stops there as before). Otherwise the cell a full search would
        /// end on: the nearest to the goal, ties broken exactly as the search breaks them.
        /// </summary>
        private int UnreachedStopCell(int start, int target, SimPoint goal, uint factionId)
        {
            if (!UnreachedStop) return -1;
            var labels = ReachLabels(factionId);
            int label = labels[start];
            if (target >= 0 && labels[target] == label) return -1;
            var nearest = NearestInArea(factionId, label, goal);
            int best = nearest[0];
            for (int i = 1; i < nearest.Length; i++)
                if (SideFair ? PathTieBetter(nearest[i], best) : nearest[i] < best) best = nearest[i];
            return best;
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
            int x = (int)(p.X.Raw / size), z = (int)(p.Z.Raw / size);
            // A rotated point on a cell boundary otherwise falls into the next cell. Choose the lower cell only on
            // the west/north half so Cell(p) and Cell(rot180(p)) are mirror cells under SideFair.
            long halfWidth = width * size / 2, halfHeight = height * size / 2;
            if (SideFair && p.X.Raw > 0 && p.X.Raw % size == 0
                && (p.X.Raw < halfWidth || p.X.Raw == halfWidth && p.Z.Raw < halfHeight)) x--;
            if (SideFair && p.Z.Raw > 0 && p.Z.Raw % size == 0
                && (p.Z.Raw < halfHeight || p.Z.Raw == halfHeight && p.X.Raw < halfWidth)) z--;
            return z * width + x;
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
        private int pathTieStart, pathTieGoal;
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
        private bool HeapLess(PathHeapEntry a, PathHeapEntry b)
        {
            if (a.F != b.F) return a.F < b.F;
            if (a.H != b.H) return a.H < b.H;
            if (!SideFair) return a.Id < b.Id;
            var at = PathTie(a.Id); var bt = PathTie(b.Id);
            return at.Forward != bt.Forward ? at.Forward > bt.Forward
                : at.Lateral != bt.Lateral ? at.Lateral > bt.Lateral : a.Id < b.Id;
        }

        private (int Forward, int Lateral) PathTie(int cell)
        {
            int sx = Math.Sign(pathTieGoal % width - pathTieStart % width);
            int sz = Math.Sign(pathTieGoal / width - pathTieStart / width);
            if (sx == 0) sx = 1;
            if (sz == 0) sz = 1;
            int dx = cell % width - pathTieStart % width, dz = cell / width - pathTieStart / width;
            return (dx * sx + dz * sz, -dx * sz + dz * sx);
        }

        private bool PathTieBetter(int candidate, int current)
        {
            var a = PathTie(candidate); var b = PathTie(current);
            return a.Forward != b.Forward ? a.Forward > b.Forward
                : a.Lateral != b.Lateral ? a.Lateral > b.Lateral : candidate < current;
        }

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
            pathTieStart = start; pathTieGoal = heuristicCell;
            // Design 93: after the tie inputs are set, because the stop cell uses the same tie-break.
            int stopCell = UnreachedStopCell(start, target, goal, factionId);
            g[start] = 0; int startH = H(start, heuristicCell); HeapAddOrUpdate(startH, startH, start);
            int best = start, expanded = 0;
            long bestDistance = Distance(Center(start), goal);
            while (pathHeapCount > 0 && expanded < passable.Length)
            {
                var item = HeapPop();
                int id = item.Id; closed[id] = true; expanded++;
                var distance = Distance(Center(id), goal);
                if (distance < bestDistance || (distance == bestDistance && (SideFair ? PathTieBetter(id, best) : id < best)))
                { best = id; bestDistance = distance; }
                if (id == target || id == stopCell) { best = id; break; }
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
