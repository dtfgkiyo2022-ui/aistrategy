using System;
using System.Collections.Generic;

namespace Rts.Decision
{
    /// <summary>
    /// The small, integer-only terrain score used by the cavalry civilisation. The caller supplies only cells it
    /// knows about; this class never fills in or guesses an unobserved part of the map.
    /// </summary>
    public readonly struct CavalryMobilityScore
    {
        public int ReachableObjectives { get; }
        public int ChokeDependentObjectives { get; }
        public int ChokeBlockedObjectives { get; }
        public int Points { get; }

        public CavalryMobilityScore(int reachableObjectives, int chokeDependentObjectives, int chokeBlockedObjectives, int points)
        {
            ReachableObjectives = reachableObjectives;
            ChokeDependentObjectives = chokeDependentObjectives;
            ChokeBlockedObjectives = chokeBlockedObjectives;
            Points = points;
        }
    }

    public static class CavalryTerrainScoring
    {
        // A one-cell corridor is not a useful cavalry route. A three-cell square is removed around its bottleneck,
        // rather than removing a single cell and allowing the path to slide into the neighbour.
        public const int ChokeBandRadius = 1;
        public const int MaximumPoints = 5;

        /// <summary>
        /// Scores the visible part of a map. Distances are BFS edge counts, not metres, so no floating-point value or
        /// collection enumeration order can affect the result. An objective must be outside the guaranteed core ring.
        /// </summary>
        public static CavalryMobilityScore Score(int width, int height, bool[] passable, bool[] observed, int coreCell,
            IReadOnlyList<int> objectiveCells, int coreExclusionCells = 15)
        {
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
            int count = checked(width * height);
            if (passable == null || passable.Length != count) throw new ArgumentException("Passability must cover the map.", nameof(passable));
            if (observed == null || observed.Length != count) throw new ArgumentException("Observation must cover the map.", nameof(observed));
            if (coreCell < 0 || coreCell >= count || !passable[coreCell] || !observed[coreCell])
                return new CavalryMobilityScore(0, 0, 0, 0);

            var objectives = new List<int>();
            for (int i = 0; i < (objectiveCells == null ? 0 : objectiveCells.Count); i++)
            {
                int cell = objectiveCells[i];
                if (cell < 0 || cell >= count || !passable[cell] || !observed[cell] || cell == coreCell) continue;
                int dx = cell % width - coreCell % width, dz = cell / width - coreCell / width;
                if (dx * dx + dz * dz <= coreExclusionCells * coreExclusionCells) continue;
                objectives.Add(cell);
            }
            objectives.Sort();

            int reachable = 0, dependent = 0, blocked = 0;
            for (int i = 0; i < objectives.Count; i++)
            {
                var path = FindPath(width, height, passable, observed, coreCell, objectives[i], null);
                int pathDistance = RouteDistance.Measure(width, height, passable, observed, coreCell, objectives[i], null);
                if (path.Length == 0 || pathDistance < 0) continue;
                reachable++;
                var banned = NarrowBand(width, height, passable, observed, path);
                if (banned == null) continue;
                var alternativePassable = (bool[])passable.Clone();
                for (int cell = 0; cell < alternativePassable.Length; cell++)
                    if (banned[cell]) alternativePassable[cell] = false;
                int alternativeDistance = RouteDistance.Measure(width, height, alternativePassable, observed,
                    coreCell, objectives[i], null);
                if (alternativeDistance < 0) blocked++;
                else if (alternativeDistance > pathDistance) dependent++;
            }

            // Convert route measurements to the same small score range as resource-point counts. Reachability is the
            // base; two independent destinations earn one extra point and three or more earn another. An objective
            // that depends on a choke does not earn the independence bonus, and a whole blocked alternative earns no
            // extra point. This prevents cell counts/metres from overwhelming the other civilisation scores.
            int points = Math.Min(3, reachable);
            if (reachable >= 2 && dependent == 0 && blocked == 0) points++;
            if (reachable >= 3 && dependent == 0 && blocked == 0) points++;
            return new CavalryMobilityScore(reachable, dependent, blocked, Math.Min(MaximumPoints, points));
        }

        private static bool[] NarrowBand(int width, int height, bool[] passable, bool[] observed, int[] path)
        {
            var banned = new bool[passable.Length];
            bool found = false;
            for (int i = 1; i + 1 < path.Length; i++)
            {
                int cell = path[i], previous = path[i - 1], next = path[i + 1];
                bool alongX = previous % width != next % width;
                int cross = alongX ? Span(width, height, passable, observed, cell, 0, 1) : Span(width, height, passable, observed, cell, 1, 0);
                if (cross > 2) continue;
                found = true;
                int cx = cell % width, cz = cell / width;
                for (int dz = -ChokeBandRadius; dz <= ChokeBandRadius; dz++)
                    for (int dx = -ChokeBandRadius; dx <= ChokeBandRadius; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x >= 0 && z >= 0 && x < width && z < height) banned[z * width + x] = true;
                    }
            }
            return found ? banned : null;
        }

        private static int Span(int width, int height, bool[] passable, bool[] observed, int cell, int dx, int dz)
        {
            int x = cell % width, z = cell / width, result = 1;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int cx = x + dx * sign, cz = z + dz * sign;
                while (cx >= 0 && cz >= 0 && cx < width && cz < height && passable[cz * width + cx] && observed[cz * width + cx])
                { result++; cx += dx * sign; cz += dz * sign; }
            }
            return result;
        }

        private static int[] FindPath(int width, int height, bool[] passable, bool[] observed, int start, int goal, bool[] banned)
        {
            if (banned != null && (banned[start] || banned[goal])) return Array.Empty<int>();
            int[] parent = new int[passable.Length], queue = new int[passable.Length];
            Array.Fill(parent, -1);
            int head = 0, tail = 0; queue[tail++] = start; parent[start] = start;
            while (head < tail)
            {
                int cell = queue[head++];
                if (cell == goal) break;
                int x = cell % width, z = cell / width;
                Visit(z + 1 < height ? cell + width : -1);
                Visit(x + 1 < width ? cell + 1 : -1);
                Visit(z > 0 ? cell - width : -1);
                Visit(x > 0 ? cell - 1 : -1);
                void Visit(int next)
                {
                    if (next < 0 || parent[next] >= 0 || !passable[next] || !observed[next] || banned != null && banned[next]) return;
                    parent[next] = cell; queue[tail++] = next;
                }
            }
            if (parent[goal] < 0) return Array.Empty<int>();
            var path = new List<int>();
            for (int cell = goal; ; cell = parent[cell]) { path.Add(cell); if (cell == start) break; }
            path.Reverse();
            return path.ToArray();
        }
    }
}
