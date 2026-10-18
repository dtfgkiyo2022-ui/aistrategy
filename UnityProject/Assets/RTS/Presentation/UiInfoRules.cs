using System;

namespace Rts.Presentation
{
    /// <summary>Pure display calculations shared by the world UI and EditMode tests.</summary>
    public static class UiInfoRules
    {
        public static float ConstructionRatio(int progress, int work)
        {
            if (work <= 0) return 0f;
            return Math.Max(0f, Math.Min(1f, progress / (float)work));
        }

        public static float BuildingDisplayHeight(float fullHeight, bool finished, bool reserved, int progress, int work)
        {
            if (reserved || finished) return fullHeight;
            return 0.6f + (fullHeight - 0.6f) * ConstructionRatio(progress, work);
        }
    }

    public readonly struct ClearanceCorner
    {
        public int X { get; }
        public int Z { get; }

        public ClearanceCorner(int x, int z)
        {
            X = x;
            Z = z;
        }
    }

    public static class CoreClearanceGeometry
    {
        /// <summary>Returns SW, SE, NE, NW corners of the same Chebyshev square used by NearCore.</summary>
        public static ClearanceCorner[] CalculateCorners(int centerCell, int clearanceCells, int mapWidth)
        {
            if (centerCell < 0 || clearanceCells <= 0 || mapWidth <= 0)
                return Array.Empty<ClearanceCorner>();
            int centerX = centerCell % mapWidth;
            int centerZ = centerCell / mapWidth;
            int radius = clearanceCells - 1;
            int minX = centerX - radius, maxX = centerX + radius;
            int minZ = centerZ - radius, maxZ = centerZ + radius;
            return new[]
            {
                new ClearanceCorner(minX, minZ), new ClearanceCorner(maxX, minZ),
                new ClearanceCorner(maxX, maxZ), new ClearanceCorner(minX, maxZ)
            };
        }
    }
}
