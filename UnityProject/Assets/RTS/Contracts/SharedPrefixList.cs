using System;
using System.Collections;
using System.Collections.Generic;

namespace Rts.Contracts
{
    /// <summary>
    /// A read-only list made of a shared array prefix and a few entries that replace items of it. The creator promises
    /// that every shared item not replaced here never changes again, so frames can share one growing array instead of
    /// copying every command of the match each tick (10-09: that copying grew with the match and stalled long plays).
    /// Frames keep it as it is; any other list is still copied.
    /// </summary>
    public sealed class SharedPrefixList<T> : IReadOnlyList<T>
    {
        private readonly T[] shared;
        private readonly int count;
        private readonly int[] replacedIndices;
        private readonly T[] replacedItems;

        /// <param name="shared">Items 0..count-1; the ones not replaced must stay unchanged for as long as this list lives.</param>
        /// <param name="replacedIndices">Strictly increasing indices below count whose items come from replacedItems.</param>
        public SharedPrefixList(T[] shared, int count, IReadOnlyList<int> replacedIndices, IReadOnlyList<T> replacedItems)
        {
            if (shared == null) throw new ArgumentNullException(nameof(shared));
            if (replacedIndices == null) throw new ArgumentNullException(nameof(replacedIndices));
            if (replacedItems == null) throw new ArgumentNullException(nameof(replacedItems));
            if (count < 0 || count > shared.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (replacedIndices.Count != replacedItems.Count) throw new ArgumentException("Each replaced index needs one item.");
            this.shared = shared;
            this.count = count;
            this.replacedIndices = new int[replacedIndices.Count];
            this.replacedItems = new T[replacedItems.Count];
            for (int i = 0; i < this.replacedIndices.Length; i++)
            {
                int index = replacedIndices[i];
                if (index < 0 || index >= count || (i > 0 && index <= this.replacedIndices[i - 1]))
                    throw new ArgumentException("Replaced indices must be increasing and inside the list.");
                this.replacedIndices[i] = index;
                this.replacedItems[i] = replacedItems[i];
            }
        }

        public int Count => count;

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
                int replaced = Array.BinarySearch(replacedIndices, index);
                return replaced >= 0 ? replacedItems[replaced] : shared[index];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            int next = 0;
            for (int i = 0; i < count; i++)
            {
                if (next < replacedIndices.Length && replacedIndices[next] == i) yield return replacedItems[next++];
                else yield return shared[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
