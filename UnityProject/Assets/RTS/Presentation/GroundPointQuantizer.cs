using System;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Input-side conversion of a clicked ground position to a fixed-point map coordinate (1/256 m grid).</summary>
    public static class GroundPointQuantizer
    {
        private const int StepsPerMeter = 256;

        public static bool TryQuantize(float x, float z, float mapWidthMeters, float mapHeightMeters, out SimPoint point)
        {
            point = default(SimPoint);
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z)) return false;
            if (x < 0f || z < 0f || x >= mapWidthMeters || z >= mapHeightMeters) return false;
            long qx = (long)Math.Round((double)x * StepsPerMeter, MidpointRounding.AwayFromZero);
            long qz = (long)Math.Round((double)z * StepsPerMeter, MidpointRounding.AwayFromZero);
            if (qx >= (long)(mapWidthMeters * StepsPerMeter) || qz >= (long)(mapHeightMeters * StepsPerMeter)) return false;
            point = new SimPoint(Fix64.FromRatio(qx, StepsPerMeter), Fix64.FromRatio(qz, StepsPerMeter));
            return true;
        }
    }
}
