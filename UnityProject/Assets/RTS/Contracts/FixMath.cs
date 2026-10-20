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
            if (TryMoveTowardsNarrow(current, target, step.Raw, dx, dz, out SimPoint narrow)) return narrow;
            return MoveTowardsBigInteger(current, target, step.Raw, dx, dz);
        }

        /// <summary>Reference implementation retained for exactness tests; the simulation never selects it directly.</summary>
        internal static SimPoint MoveTowardsBigInteger(SimPoint current, SimPoint target, Fix64 step)
        {
            if (step.Raw < 0) throw new ArgumentOutOfRangeException(nameof(step));
            long dx = checked(target.X.Raw - current.X.Raw);
            long dz = checked(target.Z.Raw - current.Z.Raw);
            return MoveTowardsBigInteger(current, target, step.Raw, dx, dz);
        }

        private static SimPoint MoveTowardsBigInteger(SimPoint current, SimPoint target, long stepRaw, long dx, long dz)
        {
            BigInteger squared = SquaredLength(dx, dz);
            if (squared.IsZero) return current;
            if (squared <= new BigInteger(stepRaw) * stepRaw) return target;
            BigInteger length = IntegerSqrt(squared);
            if (length * length < squared) length += 1;
            long x = (long)(new BigInteger(dx) * stepRaw / length);
            long z = (long)(new BigInteger(dz) * stepRaw / length);
            return new SimPoint(Fix64.FromRaw(checked(current.X.Raw + x)),
                Fix64.FromRaw(checked(current.Z.Raw + z)));
        }

        private static bool TryMoveTowardsNarrow(SimPoint current, SimPoint target, long stepRaw, long dx, long dz,
            out SimPoint result)
        {
            result = default;
            ulong step = (ulong)stepRaw;
            UInt128 squared;
            try { squared = UInt128.Add(UInt128.Multiply(Abs(dx), Abs(dx)), UInt128.Multiply(Abs(dz), Abs(dz))); }
            catch (OverflowException) { return false; }
            if (squared.IsZero) { result = current; return true; }
            if (squared.CompareTo(UInt128.Multiply(step, step)) <= 0) { result = target; return true; }
            ulong floor = UInt128.FloorSqrt(squared);
            if (UInt128.Multiply(floor, floor).CompareTo(squared) < 0)
            {
                if (floor == ulong.MaxValue) return false;
                floor++;
            }
            if (floor == 0) return false;
            UInt128 xNumerator = UInt128.Multiply(Abs(dx), step);
            UInt128 zNumerator = UInt128.Multiply(Abs(dz), step);
            if (xNumerator.Hi != 0 || zNumerator.Hi != 0 || xNumerator.Lo > long.MaxValue || zNumerator.Lo > long.MaxValue)
                return false;
            long x = (long)(xNumerator.Lo / floor);
            long z = (long)(zNumerator.Lo / floor);
            if (dx < 0) x = -x;
            if (dz < 0) z = -z;
            result = new SimPoint(Fix64.FromRaw(checked(current.X.Raw + x)),
                Fix64.FromRaw(checked(current.Z.Raw + z)));
            return true;
        }

        private static ulong Abs(long value) => value < 0 ? unchecked((ulong)(-(value + 1)) + 1UL) : (ulong)value;

        private readonly struct UInt128 : IComparable<UInt128>
        {
            internal readonly ulong Hi, Lo;
            internal UInt128(ulong hi, ulong lo) { Hi = hi; Lo = lo; }
            internal bool IsZero => Hi == 0 && Lo == 0;
            internal static UInt128 Multiply(ulong a, ulong b)
            {
                ulong a0 = a & 0xFFFFFFFFUL, a1 = a >> 32, b0 = b & 0xFFFFFFFFUL, b1 = b >> 32;
                ulong p00 = a0 * b0, p01 = a0 * b1, p10 = a1 * b0, p11 = a1 * b1;
                ulong middle = (p00 >> 32) + (p01 & 0xFFFFFFFFUL) + (p10 & 0xFFFFFFFFUL);
                return new UInt128(p11 + (p01 >> 32) + (p10 >> 32) + (middle >> 32),
                    (p00 & 0xFFFFFFFFUL) | (middle << 32));
            }
            internal static UInt128 Add(UInt128 a, UInt128 b)
            {
                ulong lo = unchecked(a.Lo + b.Lo), carry = lo < a.Lo ? 1UL : 0UL;
                ulong hi = unchecked(a.Hi + b.Hi);
                if (hi < a.Hi) throw new OverflowException();
                ulong withCarry = unchecked(hi + carry);
                if (withCarry < hi) throw new OverflowException();
                return new UInt128(withCarry, lo);
            }
            public int CompareTo(UInt128 other) => Hi != other.Hi ? Hi.CompareTo(other.Hi) : Lo.CompareTo(other.Lo);
            internal static ulong FloorSqrt(UInt128 value)
            {
                if (value.Hi == 0)
                {
                    ulong remaining = value.Lo, result = 0, bit = 1UL << 62;
                    while (bit > remaining) bit >>= 2;
                    while (bit != 0)
                    {
                        if (remaining >= result + bit) { remaining -= result + bit; result = (result >> 1) + bit; }
                        else result >>= 1;
                        bit >>= 2;
                    }
                    return result;
                }
                ulong low = 0, high = ulong.MaxValue;
                while (low < high)
                {
                    ulong distance = high - low;
                    ulong middle = low + (distance >> 1) + (distance & 1UL);
                    if (Multiply(middle, middle).CompareTo(value) <= 0) low = middle;
                    else high = middle - 1;
                }
                return low;
            }
        }

        private static BigInteger SquaredLength(long x, long z) =>
            new BigInteger(x) * x + new BigInteger(z) * z;
    }
}
