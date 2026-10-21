using System;

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
                BuildLargeEconomyPlan(faction, 0);
                largePlanLastUpdateTick[faction - 1] = world.Tick;
                for (int resource = 0; resource < 6; resource++)
                    largePlanDeliveredAtLastUpdate[faction - 1, resource] = largeDelivered[(int)(faction - 1) * 6 + resource];
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
                if (!ResourceKnownToFaction(faction, nodeIndex)) continue;
                int resource = LargeWorkResourceIndex(world.Nodes[nodeIndex].Definition.Kind);
                if (resource >= 0) known[resource]++;
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var villager = world.Villagers[i];
                if (!villager.Alive || villager.FactionId != faction || villager.NodeId == 0 || villager.NodeId > world.Nodes.Length
                    || villager.Task != VillagerTask.ToNode && villager.Task != VillagerTask.Gathering && villager.Task != VillagerTask.ToDropOff) continue;
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
                    case BuildingKind.Drill: mines++; break;
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
                    IncomePerMinute = income ?? new int[6], WorkerRoundTripTicks = roundTrips, KnownResourceCounts = known
                });
            RebalanceLargeWorkers(faction);
        }

        private void RebalanceLargeWorkers(uint faction)
        {
            var plan = largeEconomyPlans[faction - 1];
            if (plan == null) return;
            largeWorkCountsTick = -1;
            RefreshLargeWorkCounts();
            var kept = new int[6];
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var villager = ref world.Villagers[i];
                if (!villager.Alive || villager.FactionId != faction || villager.Held || villager.Carry > 0
                    || villager.Task == VillagerTask.ToBuild || villager.Task == VillagerTask.Building
                    || villager.Task == VillagerTask.ToPickup || villager.Task == VillagerTask.ToDeliver) continue;
                int resource = CurrentLargeWorkResource(villager);
                if (resource >= 0 && kept[resource] < plan.WorkerTargets[resource])
                {
                    kept[resource]++;
                    continue;
                }
                villager.Task = VillagerTask.Idle;
                villager.NodeId = 0;
                villager.Route = Array.Empty<int>();
                villager.RouteCursor = 0;
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
                if (node >= 0) { SetWorkNode(ref villager, node); kept[Math.Max(0, LargeWorkResourceIndex(world.Nodes[node].Definition.Kind))]++; }
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

        private bool LargePlanIndustryReady(uint faction)
        {
            var plan = LargePlanFor(faction);
            return plan == null
                || LargePlanBuildingCount(faction, BuildingKind.Farm) >= plan.TargetFarms
                && LargePlanBuildingCount(faction, BuildingKind.LumberCamp) >= plan.TargetLumberCamps
                && LargePlanBuildingCount(faction, BuildingKind.Mine) + LargePlanBuildingCount(faction, BuildingKind.Drill) >= plan.TargetMines
                && LargePlanBuildingCount(faction, BuildingKind.Smelter) >= plan.TargetSmelters;
        }

        /// <summary>36.5.3.5: use the existing placement routines, but stop after the next planned line item.</summary>
        private bool EnsureLargePlannedIndustry(uint faction)
        {
            var plan = LargePlanFor(faction);
            if (plan == null) return false;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            if (LargePlanBuildingCount(faction, BuildingKind.Farm) < plan.TargetFarms)
            {
                DecideFarms(faction);
                return true;
            }
            if (LargePlanBuildingCount(faction, BuildingKind.LumberCamp) < plan.TargetLumberCamps)
            {
                if (economy.Wood >= rules.LumberCampWoodCost) PlaceLumberCamp(faction);
                return true;
            }
            if (LargePlanBuildingCount(faction, BuildingKind.Mine) + LargePlanBuildingCount(faction, BuildingKind.Drill) < plan.TargetMines)
            {
                if (economy.Wood >= rules.MineWoodCost) PlaceMine(faction);
                return true;
            }
            if (LargePlanBuildingCount(faction, BuildingKind.Smelter) < plan.TargetSmelters)
            {
                if (economy.Wood >= rules.SmelterWoodCost)
                    for (int i = 0; i < world.BuildingCount; i++)
                    {
                        var mine = world.Buildings[i];
                        if (!IsPlannedBuilding(mine) || mine.FactionId != faction
                            || mine.Kind != BuildingKind.Mine && mine.Kind != BuildingKind.Drill) continue;
                        if (PlaceSmelter(faction, mine) != 0) break;
                    }
                return true;
            }
            return false;
        }

        private void WriteLargeEconomyPlan(StateWriter w, int faction)
        {
            var plan = largeEconomyPlans[faction - 1];
            if (plan == null) return;
            string prefix = "LargeEconomyPlan[" + faction.ToString(System.Globalization.CultureInfo.InvariantCulture) + "].";
            w.Value(prefix + "Stage", (byte)plan.Stage); w.Value(prefix + "R", plan.R); w.Value(prefix + "RPlus", plan.RPlus);
            w.Value(prefix + "VillagerTarget", plan.VillagerTarget); w.Value(prefix + "MinimumDefenceSoldiers", plan.MinimumDefenceSoldiers);
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
