using System;
using System.Numerics;

namespace Rts.Contracts
{
    public static class FixMath
    {
        /// <summary>Floor square root by integer binary search. Negative input is invalid.</summary>
        public static BigInteger IntegerSqrt(BigInteger value)
        {
            if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));
            BigInteger low = 0;
            BigInteger high = value + 1;
            while (high - low > 1)
            {
                BigInteger middle = (low + high) / 2;
                if (middle * middle <= value) low = middle;
                else high = middle;
            }
            return low;
        }

        public static Fix64 Sqrt(Fix64 value) =>
            Fix64.FromBigInteger(IntegerSqrt(new BigInteger(value.Raw) * 65536));

        /// <summary>Returns negative/zero/positive for distance less than/equal to/greater than radius.</summary>
        public static int CompareDistanceSquared(long dxRaw, long dzRaw, long radiusRaw)
        {
            if (radiusRaw < 0) throw new ArgumentOutOfRangeException(nameof(radiusRaw));
            return SquaredLength(dxRaw, dzRaw).CompareTo(new BigInteger(radiusRaw) * radiusRaw);
        }

        /// <summary>
        /// Takes at most step toward target, truncating each axis toward zero.
        /// Negative steps are invalid. Coordinate differences and sums are checked long.
        /// Sub-Raw movement stops; arrival returns the exact target.
        /// </summary>
        public static SimPoint MoveTowards(SimPoint current, SimPoint target, Fix64 step)
        {
            if (step.Raw < 0) throw new ArgumentOutOfRangeException(nameof(step));
            long dx = checked(target.X.Raw - current.X.Raw);
            long dz = checked(target.Z.Raw - current.Z.Raw);
            BigInteger squared = SquaredLength(dx, dz);
            if (squared.IsZero) return current;
            if (squared <= new BigInteger(step.Raw) * step.Raw) return target;
            BigInteger length = IntegerSqrt(squared);
            if (length * length < squared) length += 1;
            long x = (long)(new BigInteger(dx) * step.Raw / length);
            long z = (long)(new BigInteger(dz) * step.Raw / length);
            return new SimPoint(Fix64.FromRaw(checked(current.X.Raw + x)),
                Fix64.FromRaw(checked(current.Z.Raw + z)));
        }

        private static BigInteger SquaredLength(long x, long z) =>
            new BigInteger(x) * x + new BigInteger(z) * z;
    }
}
