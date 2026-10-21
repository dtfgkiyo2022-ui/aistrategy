using System;

namespace Rts.Simulation
{
    /// <summary>36.5.3の段階。値はシナリオのLargeEconomy欄からのみ決まる。</summary>
    public enum LargeEconomyPlanStage : byte
    {
        GrowVillage = 1,
        BuildChain = 2,
        ProduceArmy = 3
    }

    /// <summary>計画を作るための、直近の決定論的な観測値。</summary>
    public sealed class LargeEconomyPlanningInput
    {
        public int Villagers, Soldiers;
        public int CurrentFarms, CurrentLumberCamps, CurrentMines, CurrentSmelters;
        public int CurrentBarracks, CurrentHouses, CurrentTowns, CurrentDropSites;
        public int[] IncomePerMinute = new int[6];
        public int[] WorkerRoundTripTicks = new int[6];
        public int[] KnownResourceCounts = new int[6];
    }

    /// <summary>
    /// LargeEconomyの計画結果。配列は資源順 Food, Wood, Stone, Ore, Gold, Metal、施設順は
    /// Farm, LumberCamp, Mine, Smelter, Barracks, House, Town, DropSite で固定する。
    /// </summary>
    public sealed class LargeEconomyPlan
    {
        public LargeEconomyPlanStage Stage;
        public int R, RPlus;
        public int[] IncomePerMinute = new int[6];
        public int[] RequiredPerMinute = new int[6];
        public int[] WorkerTargets = new int[6];
        public int[] TargetFacilities = new int[8];
        public int[] CurrentFacilities = new int[8];
        public int VillagerTarget, MinimumDefenceSoldiers;

        public int TargetFarms => TargetFacilities[0];
        public int TargetLumberCamps => TargetFacilities[1];
        public int TargetMines => TargetFacilities[2];
        public int TargetSmelters => TargetFacilities[3];
        public int TargetBarracks => TargetFacilities[4];
        public int TargetHouses => TargetFacilities[5];
        public int TargetTowns => TargetFacilities[6];
        public int TargetDropSites => TargetFacilities[7];
    }

    /// <summary>
    /// 36.5.2の式を整数だけで計算する計画器。シミュレーション状態を参照せず、入力と欄だけから結果を返す。
    /// </summary>
    public static class LargeEconomyPlanner
    {
        public static LargeEconomyPlan Build(EconomyRules rules, LargeEconomyPlanningInput input)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (input == null) throw new ArgumentNullException(nameof(input));
            var income = CopySix(input.IncomePerMinute);
            var roundTrips = CopySix(input.WorkerRoundTripTicks);
            var known = CopySix(input.KnownResourceCounts);
            int r = Math.Min(income[0] / 50, Math.Min(income[1] / 20, income[5] / 5));
            r = Math.Min(Math.Max(0, r), Math.Max(0, rules.LargeEconomyPlanMaxR));
            int rPlus = Math.Min(Math.Max(0, rules.LargeEconomyPlanMaxR), checked(r + Math.Max(0, rules.LargeEconomyPlanRStep)));
            int villagers = Math.Max(0, input.Villagers);
            int targetVillagers = Math.Max(0, rules.AutoVillagerTarget);
            int defence = Math.Max(0, rules.LargeEconomyMinimumDefenceSoldiers);

            var required = new int[6];
            required[0] = checked(50 * rPlus);
            required[1] = checked(20 * rPlus);
            required[5] = checked(5 * rPlus);

            var current = new[]
            {
                Math.Max(0, input.CurrentFarms), Math.Max(0, input.CurrentLumberCamps),
                Math.Max(0, input.CurrentMines), Math.Max(0, input.CurrentSmelters),
                Math.Max(0, input.CurrentBarracks), Math.Max(0, input.CurrentHouses),
                Math.Max(0, input.CurrentTowns), Math.Max(0, input.CurrentDropSites)
            };
            var target = new int[8];
            target[0] = Math.Min(Math.Max(0, rules.LargeFarmMaximum), checked(CeilDiv(required[0], Math.Max(1, rules.LargeFarmFoodPerMinute)) + rules.LargeFarmReserve));
            target[0] = Math.Max(Math.Max(0, rules.LargeFarmMinimum), target[0]);
            target[1] = CeilDiv(required[1], 60);
            target[2] = CeilDiv(checked(10 * rPlus), 30);
            target[3] = CeilDiv(required[5], 20);
            target[4] = Math.Max(1, CeilDiv(rPlus, 60));
            int populationNeeded = checked(targetVillagers + defence);
            int housing = Math.Max(1, rules.HousePopulation);
            target[5] = CeilDiv(Math.Max(0, populationNeeded - Math.Max(0, rules.BasePopulation)), housing);
            target[6] = Math.Min(Math.Max(0, rules.TownMaxBuildings),
                CeilDiv(targetVillagers, Math.Max(1, rules.LargeTownVillagersPerTown)));
            target[7] = Math.Min(Math.Max(0, rules.LargeDropSiteMax),
                Math.Max(1, CeilDiv(target[1] + target[2], 4)));

            var workers = new int[6];
            int farmOutput = checked(target[0] * Math.Max(0, rules.LargeFarmFoodPerMinute));
            int lumberOutput = checked(target[1] * 60);
            workers[0] = WorkerCount(required[0] - farmOutput, roundTrips[0]);
            workers[1] = WorkerCount(required[1] - lumberOutput, roundTrips[1]);
            workers[2] = Math.Max(0, target[6] - current[6]) > 0 ? Math.Min(villagers, Math.Max(0, rules.LargeTownStoneGatherers)) : 0;
            workers[3] = 0; // 採掘機が鉱石を出す。金属は精錬所が変換する。
            workers[4] = 0;
            workers[5] = 0;
            for (int i = 0; i < workers.Length; i++) workers[i] = Math.Min(workers[i], villagers);
            int assigned = 0;
            for (int i = 0; i < workers.Length; i++) assigned = checked(assigned + workers[i]);
            // 36.5.3.4: 遊んでいる村人を出さない。余りは食料へ、見えている資源がある場合だけ割り当てる。
            int spare = Math.Max(0, villagers - assigned);
            if (known[0] > 0 || known[1] == 0) workers[0] = checked(workers[0] + spare);
            else workers[1] = checked(workers[1] + spare);
            workers[0] = Math.Min(workers[0], villagers);

            var stage = villagers < targetVillagers ? LargeEconomyPlanStage.GrowVillage : LargeEconomyPlanStage.BuildChain;
            if (stage == LargeEconomyPlanStage.BuildChain)
            {
                for (int i = 0; i < target.Length; i++)
                    if (current[i] < target[i]) { stage = LargeEconomyPlanStage.BuildChain; break; }
                // Towns and remote receivers are downstream of the production
                // chain; they must not keep the planner out of army production.
                bool chainReady = current[0] >= target[0] && current[1] >= target[1] && current[2] >= target[2]
                    && current[3] >= target[3] && current[4] >= target[4] && current[5] >= target[5];
                if (chainReady) stage = LargeEconomyPlanStage.ProduceArmy;
            }

            return new LargeEconomyPlan
            {
                Stage = stage, R = r, RPlus = rPlus, IncomePerMinute = income,
                RequiredPerMinute = required, WorkerTargets = workers, TargetFacilities = target,
                CurrentFacilities = current, VillagerTarget = targetVillagers, MinimumDefenceSoldiers = defence
            };
        }

        public static LargeEconomyPlan Build(EconomyRules rules, int villagers, int soldiers, int[] incomePerMinute,
            int[] workerRoundTripTicks, int farms, int lumberCamps, int mines, int smelters, int barracks,
            int houses, int towns, int dropSites, int[] knownResourceCounts = null)
        {
            return Build(rules, new LargeEconomyPlanningInput
            {
                Villagers = villagers, Soldiers = soldiers, IncomePerMinute = incomePerMinute,
                WorkerRoundTripTicks = workerRoundTripTicks, CurrentFarms = farms, CurrentLumberCamps = lumberCamps,
                CurrentMines = mines, CurrentSmelters = smelters, CurrentBarracks = barracks,
                CurrentHouses = houses, CurrentTowns = towns, CurrentDropSites = dropSites,
                KnownResourceCounts = knownResourceCounts ?? new int[6]
            });
        }

        private static int WorkerCount(int missing, int roundTripTicks)
        {
            if (missing <= 0) return 0;
            int perMinute = Math.Max(1, 12000 / (200 + Math.Max(0, roundTripTicks)));
            return CeilDiv(missing, perMinute);
        }

        private static int CeilDiv(int numerator, int denominator)
            => numerator <= 0 ? 0 : checked((numerator + denominator - 1) / denominator);

        private static int[] CopySix(int[] source)
        {
            var result = new int[6];
            if (source == null) return result;
            for (int i = 0; i < result.Length && i < source.Length; i++) result[i] = Math.Max(0, source[i]);
            return result;
        }
    }
}
