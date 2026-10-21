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
        public int[] Stock = new int[6];
        public int[] WorkerRoundTripTicks = new int[6];
        public int[] KnownResourceCounts = new int[6];
        public LargeEconomyPlanStage CurrentStage = LargeEconomyPlanStage.GrowVillage;
        public long ElapsedTicks, VillagerGrowthStalledTicks;
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
        public int ConstructionWoodPerMinute, ConstructionStonePerMinute;
        public bool HouseWoodPriority;

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
        public static LargeEconomyPlanStage AdvanceStage(EconomyRules rules, LargeEconomyPlanStage current,
            int villagers, long elapsedTicks, long villagerGrowthStalledTicks, int metalIncomePerMinute)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var stage = current < LargeEconomyPlanStage.GrowVillage ? LargeEconomyPlanStage.GrowVillage : current;
            int villagerTarget = Math.Max(0, rules.AutoVillagerTarget);
            if (stage == LargeEconomyPlanStage.GrowVillage
                && (villagers >= villagerTarget
                    || elapsedTicks >= Math.Max(0, rules.LargeEconomyStage1TimeoutTicks)
                    || elapsedTicks >= Math.Max(0, rules.LargeEconomyVillagerStallMinimumTicks)
                    && villagerGrowthStalledTicks >= Math.Max(0, rules.LargeEconomyVillagerStallTicks)))
                stage = LargeEconomyPlanStage.BuildChain;
            int target = Math.Max(0, rules.LargeEconomyTargetSoldiersPerMinute);
            int metalThreshold = checked(target * 5 * Math.Max(0, rules.LargeEconomyStage3MetalPermille) / 1000);
            if (stage == LargeEconomyPlanStage.BuildChain
                && (metalIncomePerMinute >= metalThreshold
                    || elapsedTicks >= Math.Max(0, rules.LargeEconomyStage2TimeoutTicks)))
                stage = LargeEconomyPlanStage.ProduceArmy;
            return stage;
        }

        public static LargeEconomyPlan Build(EconomyRules rules, LargeEconomyPlanningInput input)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (input == null) throw new ArgumentNullException(nameof(input));
            var income = CopySix(input.IncomePerMinute);
            var stock = CopySix(input.Stock);
            var roundTrips = CopySix(input.WorkerRoundTripTicks);
            var known = CopySix(input.KnownResourceCounts);
            int r = Math.Min(income[0] / 50, Math.Min(income[1] / 20, income[5] / 5));
            int targetRate = Math.Max(0, rules.LargeEconomyTargetSoldiersPerMinute);
            r = Math.Min(Math.Max(0, r), Math.Max(0, rules.LargeEconomyPlanMaxR));
            var stage = AdvanceStage(rules, input.CurrentStage, input.Villagers, input.ElapsedTicks,
                input.VillagerGrowthStalledTicks, income[5]);
            int stagePermille = stage == LargeEconomyPlanStage.GrowVillage
                ? Math.Max(1, rules.LargeEconomyStage1TargetPermille) : 1000;
            int rPlus = Math.Min(targetRate, CeilDiv(checked(targetRate * stagePermille), 1000));
            int villagers = Math.Max(0, input.Villagers);
            int soldiers = Math.Max(0, input.Soldiers);
            int targetVillagers = Math.Max(0, rules.AutoVillagerTarget);
            int defence = Math.Max(0, rules.LargeEconomyMinimumDefenceSoldiers);

            var required = new int[6];
            required[0] = checked(50 * rPlus);
            required[1] = checked(20 * rPlus);
            required[2] = 0;
            required[3] = checked(10 * rPlus);
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
            target[2] = Math.Max(stage == LargeEconomyPlanStage.GrowVillage ? 2 : 0, CeilDiv(required[3], 30));
            target[3] = Math.Max(stage == LargeEconomyPlanStage.GrowVillage ? 1 : 0, CeilDiv(required[5], 20));
            target[4] = stage == LargeEconomyPlanStage.GrowVillage ? 1 : Math.Max(1, CeilDiv(targetRate, 60) + 1);
            int populationNeeded = checked(targetVillagers + defence);
            int housing = Math.Max(1, rules.HousePopulation);
            target[5] = CeilDiv(Math.Max(0, populationNeeded - Math.Max(0, rules.BasePopulation)), housing);
            target[6] = stage == LargeEconomyPlanStage.ProduceArmy
                ? Math.Min(Math.Max(0, rules.TownMaxBuildings), CeilDiv(targetVillagers, Math.Max(1, rules.LargeTownVillagersPerTown))) : 0;
            target[7] = stage == LargeEconomyPlanStage.ProduceArmy
                ? Math.Min(Math.Max(0, rules.LargeDropSiteMax), Math.Max(1, CeilDiv(target[1] + target[2], 4))) : 0;

            int missingFarm = Math.Max(0, target[0] - current[0]);
            int missingLumber = Math.Max(0, target[1] - current[1]);
            int missingMine = Math.Max(0, target[2] - current[2]);
            int missingSmelter = Math.Max(0, target[3] - current[3]);
            int missingBarracks = Math.Max(0, target[4] - current[4]);
            int missingHouse = Math.Max(0, target[5] - current[5]);
            int missingTown = Math.Max(0, target[6] - current[6]);
            int missingDropSite = Math.Max(0, target[7] - current[7]);
            long constructionWood = (long)missingFarm * rules.FarmWoodCost
                + (long)missingLumber * rules.LumberCampWoodCost
                + (long)missingMine * (rules.CommonDrill ? rules.DrillWoodCost : rules.MineWoodCost)
                + (long)missingSmelter * rules.SmelterWoodCost
                + (long)missingBarracks * rules.BarracksWoodCost
                + (long)missingHouse * rules.HouseWoodCost
                + (long)missingTown * rules.TownWoodCost
                + (long)missingDropSite * rules.DropSiteWoodCost;
            constructionWood += (long)(missingMine + missingSmelter) * Math.Max(0, rules.LargeEconomyConstructionBeltCells) * Math.Max(0, rules.BeltWoodCost);
            long constructionStone = (long)missingMine * (rules.CommonDrill ? rules.DrillStoneCost : 0)
                + (long)missingTown * rules.TownStoneCost;
            int horizon = Math.Max(1, rules.LargeEconomyConstructionHorizonTicks);
            int constructionWoodPerMinute = checked((int)Math.Min(int.MaxValue, (constructionWood * 1200 + horizon - 1) / horizon));
            int constructionStonePerMinute = checked((int)Math.Min(int.MaxValue, (constructionStone * 1200 + horizon - 1) / horizon));
            required[1] = checked(required[1] + constructionWoodPerMinute);
            required[2] = constructionStonePerMinute;

            var workers = new int[6];
            int farmOutput = checked(current[0] * Math.Max(0, rules.LargeFarmFoodPerMinute));
            int lumberOutput = checked(current[1] * 60);
            workers[0] = WorkerCount(required[0] - farmOutput, roundTrips[0]);
            workers[1] = WorkerCount(required[1] - lumberOutput, roundTrips[1]);
            workers[2] = WorkerCount(required[2], roundTrips[2]);
            int mineOutput = checked(current[2] * 30);
            workers[3] = WorkerCount(required[3] - mineOutput, roundTrips[3]);
            workers[4] = 0;
            workers[5] = 0;
            RedirectOverfullWorkers(rules, stock, income, required, known, workers);
            LimitWorkersToPopulation(rules, villagers, stock, known, required, income, workers);
            int assigned = 0;
            for (int i = 0; i < workers.Length; i++) assigned = checked(assigned + workers[i]);
            // 36.5.3.4: do not leave villagers idle; fallback is only to a resource the faction knows.
            int spare = Math.Max(0, villagers - assigned);
            int fallback = -1;
            for (int resource = 0; resource < 4; resource++)
                if (known[resource] > 0 && stock[resource] <= Math.Max(0, rules.LargeEconomyStockOverflow)) { fallback = resource; break; }
            if (fallback >= 0) workers[fallback] = checked(workers[fallback] + spare);
            bool housePriority = missingHouse > 0
                && villagers + soldiers + 2 * Math.Max(1, rules.LargeVillagerBatch)
                    >= Math.Max(0, rules.BasePopulation) + current[5] * Math.Max(1, rules.HousePopulation);

            return new LargeEconomyPlan
            {
                Stage = stage, R = r, RPlus = rPlus, IncomePerMinute = income,
                RequiredPerMinute = required, WorkerTargets = workers, TargetFacilities = target,
                CurrentFacilities = current, VillagerTarget = targetVillagers, MinimumDefenceSoldiers = defence,
                ConstructionWoodPerMinute = constructionWoodPerMinute, ConstructionStonePerMinute = constructionStonePerMinute,
                HouseWoodPriority = housePriority
            };
        }

        private static void RedirectOverfullWorkers(EconomyRules rules, int[] stock, int[] income, int[] required,
            int[] known, int[] workers)
        {
            int moved = 0;
            int[] sources = { 0, 1, 2, 3 };
            foreach (int source in sources)
            {
                if (stock[source] <= Math.Max(0, rules.LargeEconomyStockOverflow)) continue;
                moved = checked(moved + workers[source]);
                workers[source] = 0;
            }
            while (moved > 0)
            {
                int best = -1, bestGap = 0;
                for (int resource = 0; resource < 4; resource++)
                {
                    if (known[resource] <= 0 || stock[resource] > Math.Max(0, rules.LargeEconomyStockOverflow)) continue;
                    int gap = Math.Max(0, required[resource] - income[resource]);
                    if (gap > bestGap) { bestGap = gap; best = resource; }
                }
                if (best < 0) break;
                workers[best]++; moved--;
            }
        }

        private static void LimitWorkersToPopulation(EconomyRules rules, int villagers, int[] stock, int[] known, int[] required, int[] income, int[] desired)
        {
            var result = new int[6];
            int remaining = Math.Max(0, villagers);
            // Keep the economy alive while construction resources are being gathered. These are not new
            // scenario numbers: they are the existing minimum worker policy expressed through the planner.
            int[] minimum = { Math.Max(3, rules.LargeVillagersPerFarm), 2,
                Math.Max(2, rules.LargeTownStoneGatherers), 2, 0, 0 };
            for (int resource = 0; resource < 4 && remaining > 0; resource++)
            {
                if (known[resource] <= 0 || stock[resource] > Math.Max(0, rules.LargeEconomyStockOverflow) || desired[resource] <= 0) continue;
                int count = Math.Min(Math.Min(minimum[resource], desired[resource]), remaining);
                result[resource] = count;
                remaining -= count;
            }
            while (remaining > 0)
            {
                int best = -1, bestGap = 0;
                for (int resource = 0; resource < 4; resource++)
                {
                    if (known[resource] <= 0 || stock[resource] > Math.Max(0, rules.LargeEconomyStockOverflow)
                        || result[resource] >= desired[resource]) continue;
                    int gap = Math.Max(0, required[resource] - income[resource]);
                    if (gap > bestGap || gap == bestGap && best < 0) { bestGap = gap; best = resource; }
                }
                if (best < 0) break;
                result[best]++; remaining--;
            }
            for (int resource = 0; resource < result.Length; resource++) desired[resource] = result[resource];
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
