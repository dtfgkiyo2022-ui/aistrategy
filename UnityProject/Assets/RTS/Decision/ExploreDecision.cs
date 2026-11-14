using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Decision
{
    /// <summary>One temporary chunk exclusion after a scout sees an enemy.</summary>
    public readonly struct ExploreAvoidChunk
    {
        public int X { get; }
        public int Z { get; }
        public long UntilTick { get; }

        public ExploreAvoidChunk(int x, int z, long untilTick)
        {
            X = x;
            Z = z;
            UntilTick = untilTick;
        }
    }

    /// <summary>A chunk already assigned to another large-map scout during this decision.</summary>
    public readonly struct ExploreReservedChunk
    {
        public int X { get; }
        public int Z { get; }

        public ExploreReservedChunk(int x, int z)
        {
            X = x;
            Z = z;
        }
    }

    /// <summary>The deterministic patrol target selected from one faction's explored map.</summary>
    public readonly struct ExploreTarget
    {
        public bool HasTarget { get; }
        public int Cell { get; }
        public int ChunkX { get; }
        public int ChunkZ { get; }
        public SimPoint Position { get; }

        public ExploreTarget(bool hasTarget, int cell, int chunkX, int chunkZ, SimPoint position)
        {
            HasTarget = hasTarget;
            Cell = cell;
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            Position = position;
        }
    }

    /// <summary>
    /// Selects the nearest still-useful exploration chunk. It uses only integer cell arithmetic, so the result is
    /// independent of Unity and of floating-point rounding.
    /// </summary>
    public static class ExploreDecision
    {
        public static ExploreTarget NextTarget(
            IReadOnlyList<bool> explored,
            int widthCells,
            int heightCells,
            int cellSizeMeters,
            int chunkCells,
            int minUnexploredPermille,
            int fallbackUnexploredPermille,
            int lastUsefulUnexploredPermille,
            int finalUnexploredPermille,
            SimPoint core,
            IReadOnlyList<bool> passable,
            IReadOnlyList<ExploreAvoidChunk> avoid,
            long tick,
            int avoidChunks, bool sideFair = false)
        {
            return NextTarget(explored, null, widthCells, heightCells, cellSizeMeters, chunkCells,
                minUnexploredPermille, fallbackUnexploredPermille, lastUsefulUnexploredPermille,
                finalUnexploredPermille, core, passable, avoid, Array.Empty<ExploreReservedChunk>(), tick, avoidChunks, sideFair);
        }

        /// <summary>
        /// Selects a target using per-chunk explored counts maintained by the simulation. The boolean map is still
        /// used only for the candidate chunk's passable target cell, so a large map is not rescanned on every retarget.
        /// </summary>
        public static ExploreTarget NextTarget(
            IReadOnlyList<bool> explored,
            IReadOnlyList<int> exploredChunkCounts,
            int widthCells,
            int heightCells,
            int cellSizeMeters,
            int chunkCells,
            int minUnexploredPermille,
            int fallbackUnexploredPermille,
            int lastUsefulUnexploredPermille,
            int finalUnexploredPermille,
            SimPoint core,
            IReadOnlyList<bool> passable,
            IReadOnlyList<ExploreAvoidChunk> avoid,
            IReadOnlyList<ExploreReservedChunk> reserved,
            long tick,
            int avoidChunks, bool sideFair = false)
        {
            if (explored == null || passable == null || widthCells <= 0 || heightCells <= 0 || cellSizeMeters <= 0
                || explored.Count != widthCells * heightCells || passable.Count != explored.Count || chunkCells <= 0
                || minUnexploredPermille < 0 || minUnexploredPermille > 1000
                || fallbackUnexploredPermille < 0 || fallbackUnexploredPermille > minUnexploredPermille
                || lastUsefulUnexploredPermille < 0 || lastUsefulUnexploredPermille > fallbackUnexploredPermille
                || finalUnexploredPermille < 0 || finalUnexploredPermille > lastUsefulUnexploredPermille)
                return default;

            int coreCell = Cell(core, widthCells, heightCells, cellSizeMeters);
            int coreX = coreCell < 0 ? 0 : coreCell % widthCells;
            int coreZ = coreCell < 0 ? 0 : coreCell / widthCells;
            int chunkCountX = (widthCells + chunkCells - 1) / chunkCells;
            int chunkCountZ = (heightCells + chunkCells - 1) / chunkCells;
            int chunkCount = chunkCountX * chunkCountZ;
            IReadOnlyList<int> counts = exploredChunkCounts;
            if (exploredChunkCounts == null)
            {
                var calculated = new int[chunkCount];
                for (int cell = 0; cell < explored.Count; cell++)
                    if (explored[cell]) calculated[(cell / widthCells / chunkCells) * chunkCountX + (cell % widthCells) / chunkCells]++;
                counts = calculated;
            }
            if (exploredChunkCounts != null && exploredChunkCounts.Count != chunkCount) return default;
            int[] thresholds = { minUnexploredPermille, fallbackUnexploredPermille,
                lastUsefulUnexploredPermille, finalUnexploredPermille };
            for (int thresholdIndex = 0; thresholdIndex < thresholds.Length; thresholdIndex++)
            {
                int threshold = thresholds[thresholdIndex];
                bool found = false;
                long bestDistance = 0;
                int bestChunkNumber = 0;
                int bestCell = -1, bestChunkX = 0, bestChunkZ = 0;
                for (int chunkZ = 0; chunkZ < chunkCountZ; chunkZ++)
                for (int chunkX = 0; chunkX < chunkCountX; chunkX++)
                {
                    int minX = chunkX * chunkCells, minZ = chunkZ * chunkCells;
                    int maxX = Math.Min(widthCells, minX + chunkCells), maxZ = Math.Min(heightCells, minZ + chunkCells);
                    int total = (maxX - minX) * (maxZ - minZ), unexplored = total - counts[chunkZ * chunkCountX + chunkX];
                    if ((long)unexplored * 1000L < (long)threshold * total
                        || IsAvoided(avoid, chunkX, chunkZ, tick, avoidChunks)
                        || IsReserved(reserved, chunkX, chunkZ)) continue;

                    int centreX2 = minX + maxX - 1, centreZ2 = minZ + maxZ - 1;
                    int chunkCentreX2 = minX + maxX, chunkCentreZ2 = minZ + maxZ;
                    int targetCell = -1;
                    long targetDistance = 0;
                    for (int z = minZ; z < maxZ; z++)
                    for (int x = minX; x < maxX; x++)
                    {
                        int cell = z * widthCells + x;
                        if (!passable[cell]) continue;
                        long dx = 2L * x - centreX2, dz = 2L * z - centreZ2;
                        long distance = dx * dx + dz * dz;
                        bool tieBetter = false;
                        if (sideFair && distance == targetDistance && targetCell >= 0)
                        {
                            int sx = Math.Sign((widthCells - 1) - centreX2), sz = Math.Sign((heightCells - 1) - centreZ2);
                            if (sx == 0) sx = 1; if (sz == 0) sz = 1;
                            long forward = dx * sx + dz * sz, lateral = -dx * sz + dz * sx;
                            long oldDx = 2L * (targetCell % widthCells) - centreX2;
                            long oldDz = 2L * (targetCell / widthCells) - centreZ2;
                            long oldForward = oldDx * sx + oldDz * sz, oldLateral = -oldDx * sz + oldDz * sx;
                            tieBetter = forward > oldForward || forward == oldForward && (lateral > oldLateral || lateral == oldLateral && cell < targetCell);
                        }
                        if (targetCell < 0 || distance < targetDistance || distance == targetDistance && (sideFair ? tieBetter : cell < targetCell))
                        { targetCell = cell; targetDistance = distance; }
                    }
                    if (targetCell < 0) continue;

                    long coreDx = (long)chunkCentreX2 - (2L * coreX + 1), coreDz = (long)chunkCentreZ2 - (2L * coreZ + 1);
                    long distanceFromCore = coreDx * coreDx + coreDz * coreDz;
                    int chunkNumber = chunkZ * chunkCountX + chunkX;
                    bool chunkTieBetter = false;
                    if (sideFair && found && distanceFromCore == bestDistance)
                    {
                        int sx = Math.Sign((widthCells * 2 - 1) - (2 * coreX + 1)), sz = Math.Sign((heightCells * 2 - 1) - (2 * coreZ + 1));
                        if (sx == 0) sx = 1; if (sz == 0) sz = 1;
                        long forward = coreDx * sx + coreDz * sz, lateral = -coreDx * sz + coreDz * sx;
                        int oldX = bestChunkX * chunkCells + Math.Min(widthCells, bestChunkX * chunkCells + chunkCells);
                        int oldZ = bestChunkZ * chunkCells + Math.Min(heightCells, bestChunkZ * chunkCells + chunkCells);
                        long oldDx = (long)oldX - (2L * coreX + 1), oldDz = (long)oldZ - (2L * coreZ + 1);
                        long oldForward = oldDx * sx + oldDz * sz, oldLateral = -oldDx * sz + oldDz * sx;
                        chunkTieBetter = forward > oldForward || forward == oldForward && (lateral > oldLateral || lateral == oldLateral && chunkNumber < bestChunkNumber);
                    }
                    if (!found || distanceFromCore < bestDistance || distanceFromCore == bestDistance && (sideFair ? chunkTieBetter : chunkNumber < bestChunkNumber))
                    {
                        found = true; bestDistance = distanceFromCore; bestChunkNumber = chunkNumber;
                        bestCell = targetCell; bestChunkX = chunkX; bestChunkZ = chunkZ;
                    }
                }
                if (found)
                {
                    int xCell = bestCell % widthCells, zCell = bestCell / widthCells;
                    long sizeRaw = Fix64.FromInt(cellSizeMeters).Raw;
                    return new ExploreTarget(true, bestCell, bestChunkX, bestChunkZ,
                        new SimPoint(Fix64.FromRaw(xCell * sizeRaw + sizeRaw / 2),
                            Fix64.FromRaw(zCell * sizeRaw + sizeRaw / 2)));
                }
            }

            return default;
        }

        public static ExploreTarget NextTarget(
            IReadOnlyList<bool> explored,
            int widthCells,
            int heightCells,
            int cellSizeMeters,
            int chunkCells,
            int minUnexploredPermille,
            int fallbackUnexploredPermille,
            int lastUsefulUnexploredPermille,
            int finalUnexploredPermille,
            SimPoint core,
            IReadOnlyList<bool> passable,
            IReadOnlyList<ExploreAvoidChunk> avoid,
            IReadOnlyList<ExploreReservedChunk> reserved,
            long tick,
            int avoidChunks)
        {
            return NextTarget(explored, null, widthCells, heightCells, cellSizeMeters, chunkCells,
                minUnexploredPermille, fallbackUnexploredPermille, lastUsefulUnexploredPermille,
                finalUnexploredPermille, core, passable, avoid, reserved, tick, avoidChunks);
        }

        public static ExploreTarget NextTarget(
            IReadOnlyList<bool> explored,
            int widthCells,
            int heightCells,
            int cellSizeMeters,
            int chunkCells,
            int minUnexploredPermille,
            SimPoint core,
            IReadOnlyList<bool> passable,
            IReadOnlyList<ExploreAvoidChunk> avoid,
            long tick,
            int avoidChunks)
        {
            return NextTarget(explored, widthCells, heightCells, cellSizeMeters, chunkCells, minUnexploredPermille,
                Math.Min(minUnexploredPermille, 250), Math.Min(minUnexploredPermille, 100),
                minUnexploredPermille == 0 ? 0 : 1, core, passable, avoid, tick, avoidChunks);
        }

        private static bool IsReserved(IReadOnlyList<ExploreReservedChunk> reserved, int x, int z)
        {
            if (reserved == null) return false;
            for (int i = 0; i < reserved.Count; i++)
                if (reserved[i].X == x && reserved[i].Z == z) return true;
            return false;
        }

        private static bool IsAvoided(IReadOnlyList<ExploreAvoidChunk> avoid, int x, int z, long tick, int radius)
        {
            if (avoid == null) return false;
            for (int i = 0; i < avoid.Count; i++)
            {
                var value = avoid[i];
                if (tick <= value.UntilTick && Math.Abs(value.X - x) <= radius && Math.Abs(value.Z - z) <= radius) return true;
            }
            return false;
        }

        private static int Cell(SimPoint point, int width, int height, int cellSizeMeters)
        {
            long sizeRaw = Fix64.FromInt(cellSizeMeters).Raw;
            if (point.X.Raw < 0 || point.Z.Raw < 0 || point.X.Raw >= width * sizeRaw || point.Z.Raw >= height * sizeRaw) return -1;
            return checked((int)(point.Z.Raw / sizeRaw) * width + (int)(point.X.Raw / sizeRaw));
        }
    }
}
