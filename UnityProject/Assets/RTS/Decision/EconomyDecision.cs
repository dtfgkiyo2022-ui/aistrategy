using Rts.Contracts;

namespace Rts.Decision
{
    /// <summary>
    /// The minimal automatic economy (technical-design-v3 5.4). Pure rules over the faction's own stock, its own
    /// villagers and the public resource points; nothing here sees the enemy. Kept out of the simulation so the same
    /// rules can later sit behind a policy the player chooses.
    /// </summary>
    public static class EconomyDecision
    {
        /// <summary>Step 1: one villager at a time, until the target, while food and population allow.</summary>
        public static bool ShouldTrainVillager(int villagers, int queued, int target, int food, int cost, int population, int cap, int queueLimit)
            => queued == 0 && queued < queueLimit && villagers + queued < target && food >= cost && population + queued < cap;

        /// <summary>
        /// Step 4: which resource the next idle villager gathers. Keeps the gatherers near two on food for each one on
        /// wood; counting gatherers rather than stock keeps a batch of idle villagers from all picking the same kind.
        /// </summary>
        public static ResourceKind KindToGather(int foodGatherers, int woodGatherers)
            => foodGatherers <= 2 * woodGatherers ? ResourceKind.Food : ResourceKind.Wood;

        /// <summary>
        /// Nearest point of this kind with something left, by squared distance, then by lower index (the ids are in
        /// index order). -1 when none is left. Coordinates are at most 1024 m, so the squares fit a long.
        /// </summary>
        public static int NearestNode(SimPoint from, SimPoint[] positions, ResourceKind[] kinds, int[] remaining, ResourceKind kind)
        {
            int best = -1;
            long bestDistance = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (kinds[i] != kind || remaining[i] <= 0) continue;
                long dx = positions[i].X.Raw - from.X.Raw, dz = positions[i].Z.Raw - from.Z.Raw;
                long distance = checked(dx * dx + dz * dz);
                if (best < 0 || distance < bestDistance) { best = i; bestDistance = distance; }
            }
            return best;
        }
    }
}
