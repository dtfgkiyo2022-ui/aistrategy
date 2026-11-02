using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Deterministic display-only matching for enemy villager target positions.</summary>
    public static class EnemyVillagerVisualRules
    {
        private struct Pair
        {
            public float Distance;
            public int Old, Current;
        }

        private static readonly List<Pair> pairs = new List<Pair>();
        private static readonly System.Comparison<Pair> order = (a, b) =>
            a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
            : a.Old != b.Old ? a.Old.CompareTo(b.Old) : a.Current.CompareTo(b.Current);

        /// <summary>Pairs each current villager with the nearest previous figure (closest pairs first, ties by old then current
        /// index). Pairs farther than <paramref name="maxDistance"/> are never made; an unmatched current villager gets -1.
        /// Only the pairs inside the limit are sorted, so 300 villagers cost one pass over n×m plus a short sort (11-02).</summary>
        public static int[] Assign(IReadOnlyList<Vector3> previous, IReadOnlyList<Vector3> current, float maxDistance)
        {
            var result = new int[current.Count];
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            float limit = maxDistance * maxDistance;
            pairs.Clear();
            for (int oldIndex = 0; oldIndex < previous.Count; oldIndex++)
                for (int currentIndex = 0; currentIndex < current.Count; currentIndex++)
                {
                    float distance = (previous[oldIndex] - current[currentIndex]).sqrMagnitude;
                    if (distance <= limit) pairs.Add(new Pair { Distance = distance, Old = oldIndex, Current = currentIndex });
                }
            pairs.Sort(order);
            var oldUsed = new bool[previous.Count];
            foreach (var pair in pairs)
            {
                if (oldUsed[pair.Old] || result[pair.Current] >= 0) continue;
                oldUsed[pair.Old] = true;
                result[pair.Current] = pair.Old;
            }
            return result;
        }
    }
}
