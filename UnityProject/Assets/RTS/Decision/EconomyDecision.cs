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
        /// <summary>The numbers a policy sets for the automatic economy (technical-design-v3 20). The steps stay the same.</summary>
        public readonly struct Plan
        {
            public int VillagerTarget { get; }
            public int InfantryQueue { get; }
            /// <summary>Food gatherers kept per wood gatherer.</summary>
            public int FoodPerWood { get; }
            public Plan(int villagerTarget, int infantryQueue, int foodPerWood) { VillagerTarget = villagerTarget; InfantryQueue = infantryQueue; FoodPerWood = foodPerWood; }
        }

        /// <summary>
        /// Balanced keeps the scenario's own numbers, so a match without a policy runs as before. Military trains fewer
        /// villagers and keeps a longer infantry queue with more on food; Growth trains many villagers, one infantry at a
        /// time, and gathers food and wood evenly.
        /// </summary>
        public static Plan PlanFor(EconomyPolicy policy, int villagerTarget, int infantryQueue)
        {
            switch (policy)
            {
                case EconomyPolicy.Military: return new Plan(7, 4, 3);
                case EconomyPolicy.Growth: return new Plan(18, 1, 1);
                default: return new Plan(villagerTarget, infantryQueue, 2);
            }
        }

        /// <summary>
        /// V3-4 (technical-design-v3 29): the civilisation that suits the ground around the core. Every core has
        /// <paramref name="guaranteedFood"/> food points by the fairness rule, so only the food beyond them speaks for
        /// farming; ore points speak for metallurgy. A tie goes to farming.
        /// </summary>
        public static CivKind ChooseCiv(int orePointsNear, int foodPointsNear, int guaranteedFood)
            => orePointsNear > foodPointsNear - guaranteedFood ? CivKind.Metallurgy : CivKind.Agrarian;

        /// <summary>
        /// V3-7 #3: compares the three terrain scores. Food points guaranteed to every faction are removed from
        /// the agrarian score; forest points are already filtered by the simulation to points with a usable camp site.
        /// Ties are deliberately stable: agrarian wins first, then metallurgy, then forestry. The first rule keeps
        /// the old two-score tie (agrarian) unchanged even when the forest score is zero.
        /// </summary>
        public static CivKind ChooseCiv(int orePointsNear, int foodPointsNear, int forestPointsNear, int guaranteedFood)
        {
            int agrarian = foodPointsNear > guaranteedFood ? foodPointsNear - guaranteedFood : 0;
            if (agrarian >= orePointsNear && agrarian >= forestPointsNear) return CivKind.Agrarian;
            if (orePointsNear >= forestPointsNear) return CivKind.Metallurgy;
            return CivKind.Forestry;
        }

        /// <summary>
        /// V3-8 #2: compares ore, food beyond the guaranteed points, usable forest points and usable stone points.
        /// Ties keep the older order first (agrarian, metallurgy, forestry), then masonry. Keeping masonry last makes a
        /// zero masonry score leave the three-way decision byte-for-byte equivalent to the preceding overload.
        /// </summary>
        public static CivKind ChooseCiv(int orePointsNear, int foodPointsNear, int forestPointsNear, int stonePointsNear, int guaranteedFood)
        {
            int agrarian = foodPointsNear > guaranteedFood ? foodPointsNear - guaranteedFood : 0;
            if (agrarian >= orePointsNear && agrarian >= forestPointsNear && agrarian >= stonePointsNear) return CivKind.Agrarian;
            if (orePointsNear >= forestPointsNear && orePointsNear >= stonePointsNear) return CivKind.Metallurgy;
            if (forestPointsNear >= stonePointsNear) return CivKind.Forestry;
            return CivKind.Masonry;
        }

        /// <summary>
        /// V3-11 #4: compares the four terrain scores and the engineer score. The engineer score is already a
        /// small, thresholded integer (not metres or raw path cells). Ties keep the requested stable order:
        /// agrarian, metallurgy, forestry, masonry, then bridge.
        /// </summary>
        public static CivKind ChooseCiv(int orePointsNear, int foodPointsNear, int forestPointsNear, int stonePointsNear,
            int bridgePointsNear, int guaranteedFood)
        {
            int agrarian = foodPointsNear > guaranteedFood ? foodPointsNear - guaranteedFood : 0;
            if (agrarian >= orePointsNear && agrarian >= forestPointsNear && agrarian >= stonePointsNear && agrarian >= bridgePointsNear)
                return CivKind.Agrarian;
            if (orePointsNear >= forestPointsNear && orePointsNear >= stonePointsNear && orePointsNear >= bridgePointsNear)
                return CivKind.Metallurgy;
            if (forestPointsNear >= stonePointsNear && forestPointsNear >= bridgePointsNear) return CivKind.Forestry;
            if (stonePointsNear >= bridgePointsNear) return CivKind.Masonry;
            return CivKind.Bridge;
        }

        /// <summary>Step 1: one villager at a time, until the target, while food and population allow.</summary>
        public static bool ShouldTrainVillager(int villagers, int queued, int target, int food, int cost, int population, int cap, int queueLimit)
            => queued == 0 && queued < queueLimit && villagers + queued < target && food >= cost && population + queued < cap;

        /// <summary>Step 2: one barracks, once there is wood for it.</summary>
        public static bool ShouldBuildBarracks(bool hasBarracks, int wood, int cost) => !hasBarracks && wood >= cost;

        /// <summary>Step 3: keep a short queue at a finished barracks while food, wood, population and army room allow.</summary>
        public static bool ShouldTrainInfantry(bool barracksReady, int queued, int queueTarget, int food, int wood, int foodCost, int woodCost,
            int population, int cap, bool armyRoom)
            => barracksReady && queued < queueTarget && food >= foodCost && wood >= woodCost && population < cap && armyRoom;

        /// <summary>
        /// Step 4: which resource the next idle villager gathers. Keeps the gatherers near two on food for each one on
        /// wood; counting gatherers rather than stock keeps a batch of idle villagers from all picking the same kind.
        /// </summary>
        public static ResourceKind KindToGather(int foodGatherers, int woodGatherers) => KindToGather(foodGatherers, woodGatherers, 2);

        /// <summary>Step 4 with the policy's food-per-wood ratio.</summary>
        public static ResourceKind KindToGather(int foodGatherers, int woodGatherers, int foodPerWood)
            => foodGatherers <= foodPerWood * woodGatherers ? ResourceKind.Food : ResourceKind.Wood;

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
