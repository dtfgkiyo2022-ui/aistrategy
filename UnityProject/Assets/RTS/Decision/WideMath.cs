using System;
using System.Numerics;

namespace Rts.Decision
{
    /// <summary>
    /// An unsigned 128-bit integer for squared distances and their comparisons. The decision layer used BigInteger
    /// for these, which allocates on almost every operation once a value passes 31 bits; this is a value type built from
    /// two ulongs, plain integer arithmetic only (no float, double or decimal), and exact for every input it accepts.
    /// An operation whose result does not fit throws OverflowException, so it is exact or it is an error, never wrong.
    /// </summary>
    public readonly struct Wide : IComparable<Wide>, IEquatable<Wide>
    {
        public readonly ulong Hi, Lo;
        public Wide(ulong hi, ulong lo) { Hi = hi; Lo = lo; }

        public static Wide FromUInt64(ulong value) => new Wide(0, value);

        /// <summary>Exact 64 x 64 -> 128 bit product, by 32-bit limbs.</summary>
        public static Wide Multiply(ulong a, ulong b)
        {
            ulong a0 = a & 0xFFFFFFFFUL, a1 = a >> 32, b0 = b & 0xFFFFFFFFUL, b1 = b >> 32;
            ulong p00 = a0 * b0, p01 = a0 * b1, p10 = a1 * b0, p11 = a1 * b1;
            ulong middle = (p00 >> 32) + (p01 & 0xFFFFFFFFUL) + (p10 & 0xFFFFFFFFUL);
            ulong lo = (p00 & 0xFFFFFFFFUL) | (middle << 32);
            ulong hi = p11 + (p01 >> 32) + (p10 >> 32) + (middle >> 32);
            return new Wide(hi, lo);
        }

        public static Wide Add(Wide a, Wide b)
        {
            ulong lo = unchecked(a.Lo + b.Lo);
            ulong carry = lo < a.Lo ? 1UL : 0UL;
            ulong hi = unchecked(a.Hi + b.Hi);
            bool overflow = hi < a.Hi;
            ulong withCarry = unchecked(hi + carry);
            if (overflow || withCarry < hi) throw new OverflowException("Wide addition exceeds 128 bits.");
            return new Wide(withCarry, lo);
        }

        /// <summary>a - b for a &gt;= b; a negative result is an error.</summary>
        public static Wide Subtract(Wide a, Wide b)
        {
            if (a.CompareTo(b) < 0) throw new OverflowException("Wide subtraction would be negative.");
            ulong lo = unchecked(a.Lo - b.Lo);
            ulong borrow = a.Lo < b.Lo ? 1UL : 0UL;
            return new Wide(unchecked(a.Hi - b.Hi - borrow), lo);
        }

        /// <summary>Absolute value of a signed difference of two longs, which always fits 64 unsigned bits.</summary>
        public static ulong AbsDifference(long a, long b) =>
            a >= b ? unchecked((ulong)a - (ulong)b) : unchecked((ulong)b - (ulong)a);

        public static ulong Abs(long value) => value < 0 ? unchecked((ulong)(-(value + 1)) + 1UL) : (ulong)value;

        /// <summary>Exact squared distance of a coordinate difference pair given as magnitudes.</summary>
        public static Wide SumOfSquares(ulong x, ulong z) => Add(Multiply(x, x), Multiply(z, z));

        public int CompareTo(Wide other) => Hi != other.Hi ? Hi.CompareTo(other.Hi) : Lo.CompareTo(other.Lo);
        public bool Equals(Wide other) => Hi == other.Hi && Lo == other.Lo;
        public override bool Equals(object obj) => obj is Wide other && Equals(other);
        public override int GetHashCode() => unchecked((int)(Hi ^ (Hi >> 32) ^ Lo ^ (Lo >> 32)));
        public static bool operator <(Wide a, Wide b) => a.CompareTo(b) < 0;
        public static bool operator >(Wide a, Wide b) => a.CompareTo(b) > 0;
        public static bool operator <=(Wide a, Wide b) => a.CompareTo(b) <= 0;
        public static bool operator >=(Wide a, Wide b) => a.CompareTo(b) >= 0;

        public BigInteger ToBigInteger() => (new BigInteger(Hi) << 64) + new BigInteger(Lo);

        /// <summary>Floor square root, digit by digit on 64-bit values (exact, no estimate); wider values use BigInteger.</summary>
        public static ulong FloorSqrt(ulong value)
        {
            ulong result = 0, bit = 1UL << 62;
            while (bit > value) bit >>= 2;
            while (bit != 0)
            {
                if (value >= result + bit) { value -= result + bit; result = (result >> 1) + bit; }
                else result >>= 1;
                bit >>= 2;
            }
            return result;
        }
    }
}
