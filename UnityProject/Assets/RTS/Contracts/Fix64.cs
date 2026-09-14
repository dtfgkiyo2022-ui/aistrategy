using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    /// <summary>Q47.16 storage. Arithmetic belongs to Issue #6.</summary>
    public readonly struct Fix64 : IEquatable<Fix64>
    {
        public long Raw { get; }

        public Fix64(long raw) { Raw = raw; }

        public static Fix64 FromRaw(long raw) => new Fix64(raw);
        public static Fix64 FromInt(long value) => new Fix64(checked(value * 65536L));
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
