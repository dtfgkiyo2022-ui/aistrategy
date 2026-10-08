using System;

namespace Rts.Presentation
{
    /// <summary>Pure calculations used by the unit display; it has no Unity or simulation dependency.</summary>
    internal static class UnitMotionMath
    {
        public static float FacingAngleDegrees(float fromX, float fromZ, float toX, float toZ, float fallback)
        {
            double x = toX - fromX;
            double z = toZ - fromZ;
            if (x * x + z * z < 0.000001) return fallback;
            return (float)(Math.Atan2(x, z) * 180.0 / Math.PI);
        }

        public static float WalkPlaybackRate(float distance, float seconds, float baseSpeed, float variation,
            float minimumRate, float maximumRate)
        {
            float actualSpeed = seconds > 0.0001f ? distance / seconds : 0f;
            return Clamp(actualSpeed / Math.Max(0.0001f, baseSpeed) * variation, minimumRate, maximumRate);
        }

        public static void Interpolate(float fromX, float fromZ, float toX, float toZ, float alpha,
            out float x, out float z)
        {
            float t = Clamp(alpha, 0f, 1f);
            x = fromX + (toX - fromX) * t;
            z = fromZ + (toZ - fromZ) * t;
        }

        public static float SpeedVariation(ulong id, float maximumVariation)
        {
            float normalized = (Mix(id ^ 0xD1B54A32D192ED03UL) & 0xFFFF) / 65535f;
            return 1f - maximumVariation + normalized * maximumVariation * 2f;
        }

        public static void FormationOffset(ulong id, float radius, out float x, out float z)
        {
            ulong hash = Mix(id ^ 0x9E3779B97F4A7C15UL);
            double angle = (hash & 0xFFFF) / 65536.0 * Math.PI * 2.0;
            double distance = Math.Sqrt(((hash >> 16) & 0xFFFF) / 65535.0) * radius;
            x = (float)(Math.Cos(angle) * distance);
            z = (float)(Math.Sin(angle) * distance);
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
