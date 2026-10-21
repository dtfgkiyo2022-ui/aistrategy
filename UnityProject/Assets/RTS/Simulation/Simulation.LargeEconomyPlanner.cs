using System;
using System.Collections.Generic;

using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        internal LargeEconomyPlan LargePlanFor(uint faction)
            => faction >= 1 && faction <= 2 ? largeEconomyPlans[faction - 1] : null;

        private void InitializeLargeEconomyPlans()
        {
            for (uint faction = 1; faction <= 2; faction++)
            {
                int index = (int)faction - 1;
                largePlanStages[index] = LargeEconomyPlanStage.GrowVillage;
                largePlanLastVillagers[index] = LivingVillagers(faction);
                largePlanLastVillagerGrowthTick[index] = world.Tick;
                BuildLargeEconomyPlan(faction, 0);
                largePlanLastUpdateTick[index] = world.Tick;
                for (int resource = 0; resource < 6; resource++)
                    largePlanDeliveredAtLastUpdate[index, resource] = largeDelivered[index * 6 + resource];
            }
        }

        private void UpdateLargeEconomyPlans()
        {
            var rules = world.Config.Economy;
            int interval = Math.Max(1, rules.LargeEconomyPlanIntervalTicks);
            for (uint faction = 1; faction <= 2; faction++)
            {
                int index = (int)faction - 1;
                if (largeEconomyPlans[index] != null && world.Tick - largePlanLastUpdateTick[index] < interval) continue;
                long elapsed = Math.Max(1, world.Tick - largePlanLastUpdateTick[index]);
                var income = new int[6];
                for (int resource = 0; resource < income.Length; resource++)
                {
                    int delivered = largeDelivered[index * 6 + resource];
                    int delta = Math.Max(0, delivered - largePlanDeliveredAtLastUpdate[index, resource]);
                    income[resource] = checked((int)Math.Min(int.MaxValue, (long)delta * 1200L / elapsed));
                    largePlanDeliveredAtLastUpdate[index, resource] = delivered;
                }
                int villagers = LivingVillagers(faction);
                if (villagers > largePlanLastVillagers[index])
                {
                    largePlanLastVillagers[index] = villagers;
                    largePlanLastVillagerGrowthTick[index] = world.Tick;
                }
                long stalled = Math.Max(0, world.Tick - largePlanLastVillagerGrowthTick[index]);
                largePlanStages[index] = LargeEconomyPlanner.AdvanceStage(world.Config.Economy, largePlanStages[index],
                    villagers, world.Tick, stalled, income[5]);
                BuildLargeEconomyPlan(faction, income);
                largePlanLastUpdateTick[index] = world.Tick;
            }
        }

        private void BuildLargeEconomyPlan(uint faction, int ignored)
        {
            BuildLargeEconomyPlan(faction, new int[6]);
        }

        private void BuildLargeEconomyPlan(uint faction, int[] income)
        {
            int[] roundTrips = new int[6], known = new int[6];
            long[] roundTripSum = new long[6]; int[] roundTripCount = new int[6];
            for (int nodeIndex = 0; nodeIndex < world.Nodes.Length; nodeIndex++)
            {
                if (LargeResourceNodeBlocked(nodeIndex) || !ResourceKnownToFaction(faction, nodeIndex)) continue;
                int resource = LargeWorkResourceIndex(world.Nodes[nodeIndex].Definition.Kind);
                if (resource >= 0) known[resource]++;
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var villager = world.Villagers[i];
                if (!villager.Alive || villager.FactionId != faction || villager.NodeId == 0 || villager.NodeId > world.Nodes.Length
                    || villager.Task != VillagerTask.ToNode && villager.Task != VillagerTask.Gathering && villager.Task != VillagerTask.ToDropOff) continue;
                if (LargeResourceNodeBlocked((int)villager.NodeId - 1)) continue;
                int resource = LargeWorkResourceIndex(world.Nodes[villager.NodeId - 1].Definition.Kind);
                if (resource < 0) continue;
                var drop = DropOff(villager).point;
                int routeLength = world.Map.SharedRoute(world.Map.Cell(world.Nodes[villager.NodeId - 1].Definition.Position), drop, faction).Length;
                if (routeLength <= 0) continue;
                long distanceRaw = checked((long)Math.Max(0, routeLength - 1) * world.Config.Map.CellSizeMeters * 65536L);
                int ticks = checked((int)((distanceRaw * 2 + world.VillagerStep.Raw - 1) / world.VillagerStep.Raw));
                roundTripSum[resource] = checked(roundTripSum[resource] + ticks);
                roundTripCount[resource]++;
            }
            for (int resource = 0; resource < roundTrips.Length; resource++)
                roundTrips[resource] = roundTripCount[resource] == 0 ? 0 : checked((int)(roundTripSum[resource] / roundTripCount[resource]));

            int farms = 0, lumber = 0, mines = 0, smelters = 0, barracks = 0, houses = 0, towns = 0, dropSites = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsPlannedBuilding(building) || building.FactionId != faction) continue;
                switch (building.Kind)
                {
                    case BuildingKind.Farm: farms++; break;
                    case BuildingKind.LumberCamp: lumber++; break;
                    case BuildingKind.Mine:
                    case BuildingKind.Drill:
                        if (IsOreMiningBuilding(building)) mines++;
                        break;
                    case BuildingKind.Smelter: smelters++; break;
                    case BuildingKind.Barracks: barracks++; break;
                    case BuildingKind.House: houses++; break;
                    case BuildingKind.Town: towns++; break;
                    case BuildingKind.DropSite: dropSites++; break;
                }
            }
            largeEconomyPlans[faction - 1] = LargeEconomyPlanner.Build(world.Config.Economy,
                new LargeEconomyPlanningInput
                {
                    Villagers = LivingVillagers(faction), Soldiers = LivingSoldiers(faction),
                    CurrentFarms = farms, CurrentLumberCamps = lumber, CurrentMines = mines, CurrentSmelters = smelters,
                    CurrentBarracks = barracks, CurrentHouses = houses, CurrentTowns = towns, CurrentDropSites = dropSites,
                    IncomePerMinute = income ?? new int[6],
                    Stock = new[] { world.Economies[faction - 1].Food, world.Economies[faction - 1].Wood,
                        world.Economies[faction - 1].Stone, world.Economies[faction - 1].Ore,
                        world.Economies[faction - 1].Gold, world.Economies[faction - 1].Metal },
                    WorkerRoundTripTicks = roundTrips, KnownResourceCounts = known,
                    CurrentStage = largePlanStages[faction - 1], ElapsedTicks = world.Tick,
                    VillagerGrowthStalledTicks = Math.Max(0, world.Tick - largePlanLastVillagerGrowthTick[faction - 1])
                });
            RebalanceLargeWorkers(faction);
        }

        private void RebalanceLargeWorkers(uint faction)
        {
            var plan = largeEconomyPlans[faction - 1];
            if (plan == null) return;
            largeWorkCountsTick = -1;
            RefreshLargeWorkCounts();
            largePlanWorkersRemoved[faction - 1] = 0;
            largePlanWorkersAssigned[faction - 1] = 0;
            var assigned = new int[6];
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var villager = world.Villagers[i];
                if (!villager.Alive || villager.FactionId != faction || villager.Held || villager.Carry > 0
                    || villager.Task == VillagerTask.ToBuild || villager.Task == VillagerTask.Building
                    || villager.Task == VillagerTask.ToPickup || villager.Task == VillagerTask.ToDeliver) continue;
                int resource = CurrentLargeWorkResource(villager);
                if (resource >= 0) assigned[resource]++;
            }
            int hysteresis = Math.Max(0, world.Config.Economy.LargeEconomyWorkerHysteresis);
            for (int resource = 0; resource < assigned.Length; resource++)
            {
                if (assigned[resource] <= plan.WorkerTargets[resource] + hysteresis) continue;
                int remove = assigned[resource] - plan.WorkerTargets[resource];
                for (int i = 0; i < world.VillagerCount && remove > 0; i++)
                {
                    ref var villager = ref world.Villagers[i];
                    if (!villager.Alive || villager.FactionId != faction || villager.Held || villager.Carry != 0
                        || villager.Task != VillagerTask.Gathering || CurrentLargeWorkResource(villager) != resource) continue;
                    villager.Task = VillagerTask.Idle;
                    villager.NodeId = 0;
                    villager.Route = Array.Empty<int>();
                    villager.RouteCursor = 0;
                    remove--;
                    largePlanWorkersRemoved[faction - 1]++;
                }
            }
            var kept = new int[6];
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var villager = world.Villagers[i];
                int resource = CurrentLargeWorkResource(villager);
                if (resource >= 0) kept[resource]++;
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var villager = ref world.Villagers[i];
                if (!villager.Alive || villager.FactionId != faction || villager.Held || villager.Carry > 0
                    || villager.Task != VillagerTask.Idle || villager.Task == VillagerTask.ToBuild) continue;
                int wanted = -1;
                for (int resource = 0; resource < 6; resource++)
                    if (kept[resource] < plan.WorkerTargets[resource]) { wanted = resource; break; }
                if (wanted < 0) wanted = 0;
                var kind = LargeWorkKindForResource(wanted);
                int node = NearestWorkNode(villager.Position, kind, faction);
                if (node < 0 && wanted != 0) node = NearestWorkNode(villager.Position, ResourceKind.Food, faction);
                if (node >= 0) { SetWorkNode(ref villager, node); kept[Math.Max(0, LargeWorkResourceIndex(world.Nodes[node].Definition.Kind))]++; largePlanWorkersAssigned[faction - 1]++; }
            }
        }

        private int CurrentLargeWorkResource(VillagerState villager)
        {
            if (villager.NodeId == 0 || villager.NodeId > world.Nodes.Length) return -1;
            if (villager.Task != VillagerTask.ToNode && villager.Task != VillagerTask.Gathering && villager.Task != VillagerTask.ToDropOff) return -1;
            return LargeWorkResourceIndex(world.Nodes[villager.NodeId - 1].Definition.Kind);
        }

        private static ResourceKind LargeWorkKindForResource(int resource)
            => resource == 0 ? ResourceKind.Food : resource == 1 ? ResourceKind.Wood : resource == 2 ? ResourceKind.Stone
                : resource == 3 ? ResourceKind.Ore : resource == 4 ? ResourceKind.Gold : ResourceKind.Metal;

        private int LargePlanBuildingCount(uint faction, BuildingKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsPlannedBuilding(b) && b.FactionId == faction && b.Kind == kind) count++;
            }
            return count;
        }

        private bool IsOreMiningBuilding(BuildingState building)
            => IsPlannedBuilding(building) && (building.Kind == BuildingKind.Mine || building.Kind == BuildingKind.Drill)
                && building.NodeId != 0 && building.NodeId <= world.Nodes.Length
                && world.Nodes[building.NodeId - 1].Definition.Kind == ResourceKind.Ore;

        private int LargePlanOreMiningBuildingCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].FactionId == faction && IsOreMiningBuilding(world.Buildings[i])) count++;
            return count;
        }

        private bool LargePlanIndustryReady(uint faction)
        {
            var plan = LargePlanFor(faction);
            return plan == null
                || LargePlanBuildingCount(faction, BuildingKind.Farm) >= plan.TargetFarms
                && LargePlanBuildingCount(faction, BuildingKind.LumberCamp) >= plan.TargetLumberCamps
                && LargePlanOreMiningBuildingCount(faction) >= plan.TargetMines
                && LargePlanBuildingCount(faction, BuildingKind.Smelter) >= plan.TargetSmelters;
        }

        /// <summary>36.5.3.5: use the existing placement routines, but stop after the next planned line item.</summary>
        private bool EnsureLargePlannedIndustry(uint faction)
        {
            var plan = LargePlanFor(faction);
            if (plan == null) return false;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int currentMines = LargePlanOreMiningBuildingCount(faction);
            int[] current =
            {
                LargePlanBuildingCount(faction, BuildingKind.Farm),
                LargePlanBuildingCount(faction, BuildingKind.LumberCamp),
                currentMines,
                LargePlanBuildingCount(faction, BuildingKind.Smelter)
            };
            int[] target = { plan.TargetFarms, plan.TargetLumberCamps, plan.TargetMines, plan.TargetSmelters };
            var candidates = new int[target.Length];
            int candidateCount = 0;
            for (int i = 0; i < target.Length; i++)
                if (current[i] < target[i] && target[i] > 0) candidates[candidateCount++] = i;
            Array.Sort(candidates, 0, candidateCount, Comparer<int>.Create((a, b) =>
            {
                long left = (long)current[a] * target[b], right = (long)current[b] * target[a];
                return left != right ? left.CompareTo(right) : a.CompareTo(b);
            }));
            bool resourceWaiting = false;
            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                int next = candidates[candidateIndex];
                bool placed = false;
                bool canAfford = true;
                if (next == 0)
                {
                    canAfford = economy.Wood >= rules.FarmWoodCost;
                    placed = canAfford && PlaceFarm(faction);
                }
                else if (next == 1)
                {
                    canAfford = economy.Wood >= rules.LumberCampWoodCost;
                    placed = canAfford && PlaceLumberCamp(faction) != 0;
                }
                else if (next == 2)
                {
                    canAfford = economy.Wood >= rules.MineWoodCost;
                    if (canAfford)
                    {
                        placed = rules.CommonDrill && economy.Stone >= rules.DrillStoneCost
                            && PlaceDrill(faction, -1, ResourceKind.Ore) != 0;
                        if (!placed) placed = PlaceMine(faction) != 0;
                    }
                }
                else if (next == 3)
                {
                    canAfford = economy.Wood >= rules.SmelterWoodCost;
                    if (canAfford)
                        for (int i = 0; i < world.BuildingCount; i++)
                        {
                            var mine = world.Buildings[i];
                            if (!IsOreMiningBuilding(mine) || mine.FactionId != faction) continue;
                            if (PlaceSmelter(faction, mine) != 0) { placed = true; break; }
                        }
                }
                if (placed) return true;
                if (canAfford) continue;
                resourceWaiting = true;
            }
            if (resourceWaiting) return true;
            return false;
        }

        private void WriteLargeEconomyPlan(StateWriter w, int faction)
        {
            var plan = largeEconomyPlans[faction - 1];
            if (plan == null) return;
            string prefix = "LargeEconomyPlan[" + faction.ToString(System.Globalization.CultureInfo.InvariantCulture) + "].";
            w.Value(prefix + "Stage", (byte)plan.Stage); w.Value(prefix + "R", plan.R); w.Value(prefix + "RPlus", plan.RPlus);
            w.Value(prefix + "VillagerTarget", plan.VillagerTarget); w.Value(prefix + "MinimumDefenceSoldiers", plan.MinimumDefenceSoldiers);
            w.Value(prefix + "StageLastVillagers", largePlanLastVillagers[faction - 1]);
            w.Value(prefix + "StageLastVillagerGrowthTick", largePlanLastVillagerGrowthTick[faction - 1]);
            w.Value(prefix + "StageCurrent", (byte)largePlanStages[faction - 1]);
            w.Value(prefix + "ConstructionWoodPerMinute", plan.ConstructionWoodPerMinute);
            w.Value(prefix + "ConstructionStonePerMinute", plan.ConstructionStonePerMinute);
            w.Value(prefix + "HouseWoodPriority", plan.HouseWoodPriority);
            for (int i = 0; i < 6; i++)
            {
                w.Value(prefix + "IncomePerMinute[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", plan.IncomePerMinute[i]);
                w.Value(prefix + "RequiredPerMinute[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", plan.RequiredPerMinute[i]);
                w.Value(prefix + "WorkerTargets[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", plan.WorkerTargets[i]);
            }
            for (int i = 0; i < plan.TargetFacilities.Length; i++)
            {
                string index = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                w.Value(prefix + "TargetFacilities[" + index + "]", plan.TargetFacilities[i]);
                w.Value(prefix + "CurrentFacilities[" + index + "]", plan.CurrentFacilities[i]);
            }
        }
    }
}
