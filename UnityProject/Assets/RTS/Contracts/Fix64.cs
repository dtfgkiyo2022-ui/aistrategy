using System;
using System.Numerics;

namespace Rts.Contracts
{
    /// <summary>Q47.16 value. Arithmetic overflows throw; division truncates toward zero.</summary>
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        public long Raw { get; }

        public Fix64(long raw) { Raw = raw; }

        public static Fix64 FromRaw(long raw) => new Fix64(raw);
        public static Fix64 FromInt(long value) => new Fix64(checked(value * 65536L));
        public static Fix64 FromRatio(long numerator, long denominator) =>
            FromBigInteger(new BigInteger(numerator) * 65536 / denominator);

        internal static Fix64 FromBigInteger(BigInteger raw)
        {
            if (raw < long.MinValue || raw > long.MaxValue)
                throw new OverflowException("Fix64 result is outside the Raw range.");
            return FromRaw((long)raw);
        }

        public static Fix64 operator +(Fix64 a, Fix64 b) => FromRaw(checked(a.Raw + b.Raw));
        public static Fix64 operator -(Fix64 a, Fix64 b) => FromRaw(checked(a.Raw - b.Raw));
        public static Fix64 operator *(Fix64 a, Fix64 b) =>
            FromBigInteger(new BigInteger(a.Raw) * b.Raw / 65536);
        public static Fix64 operator /(Fix64 a, Fix64 b) =>
            FromBigInteger(new BigInteger(a.Raw) * 65536 / b.Raw);
        public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);
        public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
        public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
        public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
        public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;
        public bool Equals(Fix64 other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix64 other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public static bool operator ==(Fix64 left, Fix64 right) => left.Equals(right);
        public static bool operator !=(Fix64 left, Fix64 right) => !left.Equals(right);
        public override string ToString()
        {
            // Split before formatting so all 16 fractional digits survive even at long limits.
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            long whole = Math.Abs(Raw / 65536L);
            long fraction = Math.Abs(Raw % 65536L) * 152587890625L;
            string result = (Raw < 0 ? "-" : "") + whole.ToString(culture);
            return fraction == 0 ? result : result + "." + fraction.ToString("D16", culture).TrimEnd('0');
        }
    }

}
