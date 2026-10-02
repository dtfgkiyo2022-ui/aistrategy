using System;
using System.Collections.Generic;

namespace Rts.Decision
{
    /// <summary>
    /// V3-16 #3: the tollgate civilisation's terrain score. It is the cavalry score turned around: a narrow place on
    /// the observed route to an enemy objective is worth something only if closing it makes that route much longer
    /// while still leaving another way (a gate may not cut the owner's own map in two). Only cells the caller marks as
    /// known are used; the base arrays are never modified.
    /// </summary>
    public static class TollgateTerrainScoring
    {
        /// <summary>A closed place must lengthen the route by at least this many cell edges to count.</summary>
        public const int DetourThreshold = 6;
        /// <summary>The narrow cross line is at most two cells wide (the gate is a 1x2 building).</summary>
        public const int MaximumCrossSpan = 2;
        /// <summary>Two sites closer than this (Chebyshev, in cells) are one site.</summary>
        public const int SiteSeparation = 6;
        /// <summary>Path ends are not gate sites (the same margin DecideTollgate uses).</summary>
        public const int PathMargin = 3;

        /// <summary>Number of distinct, valuable, buildable choke sites on the known routes to the objectives.</summary>
        public static int CountSites(int width, int height, bool[] passable, bool[] known, int coreCell,
            IReadOnlyList<int> objectiveCells, int coreExclusionCells)
        {
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
            int count = checked(width * height);
            if (passable == null || passable.Length != count) throw new ArgumentException("Passability must cover the map.", nameof(passable));
            if (known == null || known.Length != count) throw new ArgumentException("Known cells must cover the map.", nameof(known));
            if (coreCell < 0 || coreCell >= count || !passable[coreCell] || !known[coreCell]) return 0;

            var objectives = new List<int>();
            for (int i = 0; i < (objectiveCells == null ? 0 : objectiveCells.Count); i++)
            {
                int cell = objectiveCells[i];
                if (cell < 0 || cell >= count || !passable[cell] || !known[cell] || cell == coreCell || objectives.Contains(cell)) continue;
                objectives.Add(cell);
            }
            objectives.Sort();

            var sites = new List<int>();
            var trial = (bool[])passable.Clone();
            for (int o = 0; o < objectives.Count; o++)
            {
                int[] path = FindPath(width, height, passable, known, coreCell, objectives[o]);
                if (path.Length == 0) continue;
                int baseDistance = path.Length - 1;

                // Group consecutive narrow path cells into runs and test only the middle of each run: every cell of a
                // run closes the same corridor, so one measurement per run keeps the cost to a few searches.
                int runStart = -1;
                for (int i = PathMargin; i <= path.Length - PathMargin; i++)
                {
                    bool narrow = i < path.Length - PathMargin && IsNarrow(width, height, passable, known, path, i, coreCell, coreExclusionCells);
                    if (narrow) { if (runStart < 0) runStart = i; continue; }
                    if (runStart < 0) continue;
                    int middle = path[(runStart + i - 1) / 2];
                    runStart = -1;
                    if (!ClosingIsWorthIt(width, height, passable, known, trial, coreCell, objectives[o], baseDistance, middle, path)) continue;
                    bool duplicate = false;
                    for (int s = 0; s < sites.Count && !duplicate; s++) duplicate = Chebyshev(width, sites[s], middle) < SiteSeparation;
                    if (!duplicate) sites.Add(middle);
                }
            }
            return sites.Count;
        }

        /// <summary>0 sites: 0, one site: 2, two or more: 3 (the same small scale the other terrain scores use).</summary>
        public static int Points(int sites) => sites >= 2 ? 3 : sites == 1 ? 2 : 0;

        public static int Score(int width, int height, bool[] passable, bool[] known, int coreCell,
            IReadOnlyList<int> objectiveCells, int coreExclusionCells)
            => Points(CountSites(width, height, passable, known, coreCell, objectiveCells, coreExclusionCells));

        private static bool ClosingIsWorthIt(int width, int height, bool[] passable, bool[] known, bool[] trial,
            int coreCell, int objective, int baseDistance, int cell, int[] path)
        {
            int index = Array.IndexOf(path, cell);
            if (index <= 0 || index + 1 >= path.Length) return false;
            bool alongX = path[index - 1] % width != path[index + 1] % width;
            var closed = new List<int> { cell };
            int dx = alongX ? 0 : 1, dz = alongX ? 1 : 0;
            int x = cell % width, z = cell / width;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int cx = x + dx * sign, cz = z + dz * sign;
                while (cx >= 0 && cz >= 0 && cx < width && cz < height && passable[cz * width + cx] && known[cz * width + cx])
                { closed.Add(cz * width + cx); cx += dx * sign; cz += dz * sign; }
            }
            for (int i = 0; i < closed.Count; i++) trial[closed[i]] = false;
            int alternative = RouteDistance.Measure(width, height, trial, known, coreCell, objective, null);
            for (int i = 0; i < closed.Count; i++) trial[closed[i]] = passable[closed[i]];
            // -1 means the closed line is the only way: such a gate would cut the owner's own map and cannot be built.
            return alternative >= 0 && alternative - baseDistance >= DetourThreshold;
        }

        private static bool IsNarrow(int width, int height, bool[] passable, bool[] known, int[] path, int index,
            int coreCell, int coreExclusionCells)
        {
            int cell = path[index];
            int cdx = cell % width - coreCell % width, cdz = cell / width - coreCell / width;
            if (Math.Max(Math.Abs(cdx), Math.Abs(cdz)) < coreExclusionCells) return false;
            bool alongX = path[index - 1] % width != path[index + 1] % width;
            int span = alongX ? Span(width, height, passable, known, cell, 0, 1) : Span(width, height, passable, known, cell, 1, 0);
            return span <= MaximumCrossSpan;
        }

        private static int Span(int width, int height, bool[] passable, bool[] known, int cell, int dx, int dz)
        {
            int x = cell % width, z = cell / width, result = 1;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int cx = x + dx * sign, cz = z + dz * sign;
                while (cx >= 0 && cz >= 0 && cx < width && cz < height && passable[cz * width + cx] && known[cz * width + cx])
                { result++; cx += dx * sign; cz += dz * sign; }
            }
            return result;
        }

        private static int Chebyshev(int width, int a, int b)
            => Math.Max(Math.Abs(a % width - b % width), Math.Abs(a / width - b / width));

        private static int[] FindPath(int width, int height, bool[] passable, bool[] known, int start, int goal)
        {
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
                    if (next < 0 || parent[next] >= 0 || !passable[next] || !known[next]) return;
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
