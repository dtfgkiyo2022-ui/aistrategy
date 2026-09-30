using System;
using System.Collections.Generic;

namespace Rts.Decision
{
    /// <summary>Deterministic four-way route distance for decision-time terrain measurements.</summary>
    public static class RouteDistance
    {
        /// <summary>
        /// Returns the number of four-way cell edges, or -1 when the goal cannot be reached. The base passability,
        /// known-cell mask, and temporary openings are inputs only; no map object or caller-owned array is changed.
        /// </summary>
        public static int Measure(int width, int height, bool[] basePassable, bool[] known, int start, int goal,
            IReadOnlyList<int> temporarilyOpen)
        {
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width));
            int count = checked(width * height);
            if (basePassable == null || basePassable.Length != count)
                throw new ArgumentException("Base passability must cover the map.", nameof(basePassable));
            if (known != null && known.Length != count)
                throw new ArgumentException("Known cells must cover the map.", nameof(known));
            if (start < 0 || start >= count || goal < 0 || goal >= count) return -1;

            var open = new bool[count];
            if (temporarilyOpen != null)
                for (int i = 0; i < temporarilyOpen.Count; i++)
                {
                    int cell = temporarilyOpen[i];
                    if (cell < 0 || cell >= count) throw new ArgumentOutOfRangeException(nameof(temporarilyOpen));
                    open[cell] = true;
                }
            if (!Allowed(start) || !Allowed(goal)) return -1;
            if (start == goal) return 0;

            var distance = new int[count];
            Array.Fill(distance, -1);
            var queue = new int[count];
            int head = 0, tail = 0;
            queue[tail++] = start;
            distance[start] = 0;
            while (head < tail)
            {
                int cell = queue[head++], nextDistance = distance[cell] + 1;
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
