using System;
using System.Collections.Generic;

namespace Rts.Decision
{
    /// <summary>Deterministic four-way route distance for decision-time terrain measurements.</summary>
    public static class RouteDistance
    {
        /// <summary>Reusable, single-threaded storage for repeated route measurements on one map.</summary>
        public sealed class Workspace
        {
            internal bool[] Open;
            internal int[] Distance;
            internal int[] Queue;

            public int[] Distances => Distance;
            public int LastVisitedCells { get; internal set; }

            internal void Ensure(int count)
            {
                if (Open != null && Open.Length == count) return;
                Open = new bool[count];
                Distance = new int[count];
                Queue = new int[count];
            }
        }

        /// <summary>Fills a reusable four-way distance map on the same passable cells used by <see cref="Measure"/>.</summary>
        public static void FillDistances(int width, int height, bool[] basePassable, int start, Workspace workspace)
        {
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width));
            int count = checked(width * height);
            if (basePassable == null || basePassable.Length != count)
                throw new ArgumentException("Base passability must cover the map.", nameof(basePassable));
            workspace = workspace ?? new Workspace();
            workspace.Ensure(count);
            var distance = workspace.Distance;
            Array.Fill(distance, -1);
            workspace.LastVisitedCells = 0;
            if (start < 0 || start >= count || !basePassable[start]) return;
            var queue = workspace.Queue;
            int head = 0, tail = 0;
            queue[tail++] = start;
            distance[start] = 0;
            while (head < tail)
            {
                int cell = queue[head++], nextDistance = distance[cell] + 1;
                Visit(cell + width < count ? cell + width : -1, nextDistance);
                Visit(cell % width + 1 < width ? cell + 1 : -1, nextDistance);
                Visit(cell - width >= 0 ? cell - width : -1, nextDistance);
                Visit(cell % width > 0 ? cell - 1 : -1, nextDistance);
            }
            workspace.LastVisitedCells = tail;

            void Visit(int cell, int distanceValue)
            {
                if (cell < 0 || distance[cell] >= 0 || !basePassable[cell]) return;
                distance[cell] = distanceValue;
                queue[tail++] = cell;
            }
        }

        /// <summary>
        /// Returns the number of four-way cell edges, or -1 when the goal cannot be reached. The base passability,
        /// known-cell mask, and temporary openings are inputs only; no map object or caller-owned array is changed.
        /// </summary>
        public static int Measure(int width, int height, bool[] basePassable, bool[] known, int start, int goal,
            IReadOnlyList<int> temporarilyOpen)
            => MeasureCore(width, height, basePassable, known, start, goal, temporarilyOpen, null, out _);

        /// <summary>Same deterministic route measurement using caller-owned scratch arrays.</summary>
        public static int Measure(int width, int height, bool[] basePassable, bool[] known, int start, int goal,
            IReadOnlyList<int> temporarilyOpen, Workspace workspace)
            => MeasureCore(width, height, basePassable, known, start, goal, temporarilyOpen, workspace, out _);

        /// <summary>Same measurement with the number of dequeued cells reported for diagnostics.</summary>
        public static int Measure(int width, int height, bool[] basePassable, bool[] known, int start, int goal,
            IReadOnlyList<int> temporarilyOpen, out int visitedCells)
            => MeasureCore(width, height, basePassable, known, start, goal, temporarilyOpen, null, out visitedCells);

        /// <summary>Same diagnostic measurement using caller-owned scratch arrays.</summary>
        public static int Measure(int width, int height, bool[] basePassable, bool[] known, int start, int goal,
            IReadOnlyList<int> temporarilyOpen, Workspace workspace, out int visitedCells)
            => MeasureCore(width, height, basePassable, known, start, goal, temporarilyOpen, workspace, out visitedCells);

        private static int MeasureCore(int width, int height, bool[] basePassable, bool[] known, int start, int goal,
            IReadOnlyList<int> temporarilyOpen, Workspace workspace, out int visitedCells)
        {
            visitedCells = 0;
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width));
            int count = checked(width * height);
            if (basePassable == null || basePassable.Length != count)
                throw new ArgumentException("Base passability must cover the map.", nameof(basePassable));
            if (known != null && known.Length != count)
                throw new ArgumentException("Known cells must cover the map.", nameof(known));
            if (start < 0 || start >= count || goal < 0 || goal >= count) return -1;

            workspace = workspace ?? new Workspace();
            workspace.Ensure(count);
            var open = workspace.Open;
            Array.Clear(open, 0, open.Length);
            if (temporarilyOpen != null)
                for (int i = 0; i < temporarilyOpen.Count; i++)
                {
                    int cell = temporarilyOpen[i];
                    if (cell < 0 || cell >= count) throw new ArgumentOutOfRangeException(nameof(temporarilyOpen));
                    open[cell] = true;
                }
            if (!Allowed(start) || !Allowed(goal)) return -1;
            if (start == goal) return 0;

            var distance = workspace.Distance;
            Array.Fill(distance, -1);
            var queue = workspace.Queue;
            int head = 0, tail = 0;
            queue[tail++] = start;
            distance[start] = 0;
            while (head < tail)
            {
                int cell = queue[head++], nextDistance = distance[cell] + 1;
                visitedCells++;
                int x = cell % width, z = cell / width;
                if (Visit(z + 1 < height ? cell + width : -1, nextDistance)) return nextDistance;
                if (Visit(x + 1 < width ? cell + 1 : -1, nextDistance)) return nextDistance;
                if (Visit(z > 0 ? cell - width : -1, nextDistance)) return nextDistance;
                if (Visit(x > 0 ? cell - 1 : -1, nextDistance)) return nextDistance;
            }
            return -1;

            bool Visit(int cell, int nextDistance)
            {
                if (cell < 0 || distance[cell] >= 0 || !Allowed(cell)) return false;
                distance[cell] = nextDistance;
                if (cell == goal) return true;
                queue[tail++] = cell;
                return false;
            }

            bool Allowed(int cell) => (known == null || known[cell]) && (basePassable[cell] || open[cell]);
        }
    }
}
