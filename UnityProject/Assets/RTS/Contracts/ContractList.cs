using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    internal static class ContractList
    {
        internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source is SharedPrefixList<T> shared) return shared; // already read-only; its creator keeps it unchanged
            var copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            return Array.AsReadOnly(copy);
        }
    }

}
