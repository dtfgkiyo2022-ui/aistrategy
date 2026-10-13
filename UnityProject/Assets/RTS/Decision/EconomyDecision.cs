using Rts.Contracts;
using System.Collections.Generic;

namespace Rts.Decision
{
    /// <summary>
    /// The minimal automatic economy (technical-design-v3 5.4). Pure rules over the faction's own stock, its own
    /// villagers and the public resource points; nothing here sees the enemy. Kept out of the simulation so the same
    /// rules can later sit behind a policy the player chooses.
    /// </summary>
    public static class EconomyDecision
    {
        /// <summary>A civilisation candidate and the score used to compare it.</summary>
        public readonly struct CivScore
        {
            public CivKind Civ { get; }
            public int Score { get; }
            public int Priority { get; }
            public int ScoreCap { get; }
            public bool Enabled { get; }

            public CivScore(CivKind civ, int score, int priority)
                : this(civ, score, priority, 1000, true)
            {
            }

            /// <summary>A civilisation score with the cap used by the variety rule.</summary>
            public CivScore(CivKind civ, int score, int priority, int scoreCap, bool enabled)
            {
                Civ = civ;
                Score = score;
                Priority = priority;
                ScoreCap = scoreCap;
                Enabled = enabled;
            }
        }

        /// <summary>Returns the highest-scoring candidate, keeping the explicit priority on ties.</summary>
        public static CivKind ChooseCiv(IReadOnlyList<CivScore> candidates)
        {
            if (candidates == null || candidates.Count == 0) return CivKind.Primitive;
            CivScore best = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                CivScore candidate = candidates[i];
                if (candidate.Score > best.Score || candidate.Score == best.Score && candidate.Priority < best.Priority)
                    best = candidate;
            }
            return best.Civ;
        }

        /// <summary>
        /// CivVariety compares unlike terrain scales in permille, then picks one of the near-best candidates with a
        /// scenario-seed choice. The input is already in explicit priority order; no collection enumeration decides it.
        /// </summary>
        public static CivKind ChooseCivVariety(IReadOnlyList<CivScore> candidates, ulong scenarioSeed, uint faction,
            int windowPermille)
        {
            if (candidates == null || candidates.Count == 0) return CivKind.Primitive;
            int window = System.Math.Max(0, System.Math.Min(1000, windowPermille));
            int bestPermille = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!candidate.Enabled || candidate.Score <= 0 || candidate.ScoreCap <= 0) continue;
                int permille = NormalizedScorePermille(candidate.Score, candidate.ScoreCap);
                if (permille > bestPermille) bestPermille = permille;
            }
            // No ground fits any civilisation: keep the historical answer (the first by priority) instead of none.
            if (bestPermille <= 0) return ChooseCiv(candidates);

            int count = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.Enabled && candidate.Score > 0 && candidate.ScoreCap > 0
                    && NormalizedScorePermille(candidate.Score, candidate.ScoreCap) >= bestPermille - window)
                    count++;
            }
            if (count == 0) return CivKind.Primitive;
            int chosen = count == 1 ? 0 : (int)(MixCivVarietySeed(scenarioSeed, faction) % (ulong)count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!candidate.Enabled || candidate.Score <= 0 || candidate.ScoreCap <= 0
                    || NormalizedScorePermille(candidate.Score, candidate.ScoreCap) < bestPermille - window) continue;
                if (chosen-- == 0) return candidate.Civ;
            }
            return CivKind.Primitive;
        }

        /// <summary>Converts a raw score to a capped integer permille without floating point.</summary>
        public static int NormalizedScorePermille(int score, int scoreCap)
        {
            if (score <= 0 || scoreCap <= 0) return 0;
            return (int)System.Math.Min(1000L, (long)score * 1000L / scoreCap);
        }

        private static ulong MixCivVarietySeed(ulong seed, uint faction)
        {
            unchecked
            {
                ulong z = seed + 0x9E3779B97F4A7C15UL + faction * 0xD1B54A32D192ED03UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

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

        /// <summary>S-4b villager target curve. The city civilisation multiplier is applied after the curve.</summary>
        public static int VillagerTarget(bool economyScale, int age, int defaultTarget, bool metropolis)
        {
            int target = economyScale ? age >= 2 ? 60 : age == 1 ? 40 : 20 : defaultTarget;
            return metropolis ? checked(target * 3 / 2) : target;
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
            => ChooseCiv(new[]
            {
                new CivScore(CivKind.Agrarian, foodPointsNear > guaranteedFood ? foodPointsNear - guaranteedFood : 0, 0),
                new CivScore(CivKind.Metallurgy, orePointsNear, 1),
                new CivScore(CivKind.Forestry, forestPointsNear, 2)
            });

        /// <summary>
        /// V3-8 #2: compares ore, food beyond the guaranteed points, usable forest points and usable stone points.
        /// Ties keep the older order first (agrarian, metallurgy, forestry), then masonry. Keeping masonry last makes a
        /// zero masonry score leave the three-way decision byte-for-byte equivalent to the preceding overload.
        /// </summary>
        public static CivKind ChooseCiv(int orePointsNear, int foodPointsNear, int forestPointsNear, int stonePointsNear, int guaranteedFood)
            => ChooseCiv(new[]
            {
                new CivScore(CivKind.Agrarian, foodPointsNear > guaranteedFood ? foodPointsNear - guaranteedFood : 0, 0),
                new CivScore(CivKind.Metallurgy, orePointsNear, 1),
                new CivScore(CivKind.Forestry, forestPointsNear, 2),
                new CivScore(CivKind.Masonry, stonePointsNear, 3)
            });

        /// <summary>
        /// V3-9 #3: compares the terrain scores including the number of usable caravan outposts. A zero caravan
        /// score never steals an older tie, preserving the preceding four-way ordering when caravan terrain is absent.
        /// </summary>
        public static CivKind ChooseCiv(int orePointsNear, int foodPointsNear, int forestPointsNear, int stonePointsNear,
            int caravanPointsNear, int guaranteedFood)
            => ChooseCiv(new[]
            {
                new CivScore(CivKind.Agrarian, foodPointsNear > guaranteedFood ? foodPointsNear - guaranteedFood : 0, 0),
                new CivScore(CivKind.Metallurgy, orePointsNear, 1),
                new CivScore(CivKind.Forestry, forestPointsNear, 2),
                new CivScore(CivKind.Masonry, stonePointsNear, 3),
                new CivScore(CivKind.Caravan, caravanPointsNear, 4)
            });

        /// <summary>
        /// V3-10 #3: adds the cavalry mobility score (already converted to a small 0-5 range). Ties keep the older
        /// order agrarian &gt; metallurgy &gt; forestry &gt; masonry &gt; caravan &gt; cavalry, so a zero score never steals one.
        /// </summary>
        public static CivKind ChooseCiv(int orePointsNear, int foodPointsNear, int forestPointsNear, int stonePointsNear,
            int caravanPointsNear, int cavalryPointsNear, int guaranteedFood)
            => ChooseCiv(new[]
            {
                new CivScore(CivKind.Agrarian, foodPointsNear > guaranteedFood ? foodPointsNear - guaranteedFood : 0, 0),
                new CivScore(CivKind.Metallurgy, orePointsNear, 1),
                new CivScore(CivKind.Forestry, forestPointsNear, 2),
                new CivScore(CivKind.Masonry, stonePointsNear, 3),
                new CivScore(CivKind.Caravan, caravanPointsNear, 4),
                new CivScore(CivKind.Cavalry, cavalryPointsNear, 5)
            });

        /// <summary>
        /// V3-11 #4: adds the engineer score (the best one-bridge shortening, already tiered to 0-3). Ties keep the
        /// older order agrarian &gt; metallurgy &gt; forestry &gt; masonry &gt; caravan &gt; cavalry &gt; bridge.
        /// </summary>
        public static CivKind ChooseCiv(int orePointsNear, int foodPointsNear, int forestPointsNear, int stonePointsNear,
            int caravanPointsNear, int cavalryPointsNear, int bridgePointsNear, int guaranteedFood)
            => ChooseCiv(new[]
            {
                new CivScore(CivKind.Agrarian, foodPointsNear > guaranteedFood ? foodPointsNear - guaranteedFood : 0, 0),
                new CivScore(CivKind.Metallurgy, orePointsNear, 1),
                new CivScore(CivKind.Forestry, forestPointsNear, 2),
                new CivScore(CivKind.Masonry, stonePointsNear, 3),
                new CivScore(CivKind.Caravan, caravanPointsNear, 4),
                new CivScore(CivKind.Cavalry, cavalryPointsNear, 5),
                new CivScore(CivKind.Bridge, bridgePointsNear, 6)
            });

        /// <summary>Step 1: one villager at a time, until the target, while food and population allow.</summary>
        public static bool ShouldTrainVillager(int villagers, int queued, int target, int food, int cost, int population, int cap, int queueLimit)
            => queued == 0 && queued < queueLimit && villagers + queued < target && food >= cost && population + queued < cap;

        /// <summary>Step 2: one barracks, once there is wood for it.</summary>
        public static bool ShouldBuildBarracks(bool hasBarracks, int wood, int cost) => !hasBarracks && wood >= cost;

        /// <summary>Scaled Step 2: reach the requested barracks count once wood can pay for the next one.</summary>
        public static bool ShouldBuildBarracks(int current, int target, int wood, int cost)
            => current < target && wood >= cost;

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
        /// S-4b: the scaled economy keeps a little more food available for the larger villager and infantry lines.
        /// The policy's ratio is never reduced, and the old ratio is returned when the flag is off.
        /// </summary>
        public static int FoodPerWood(bool economyScale, int policyRatio)
            => economyScale ? System.Math.Max(3, policyRatio) : policyRatio;

        /// <summary>S-4b: scale the agrarian food-source count without changing the old economy.</summary>
        public static int FoodSourceTarget(bool economyScale, int defaultTarget)
            => economyScale ? checked(defaultTarget * 2) : defaultTarget;

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
