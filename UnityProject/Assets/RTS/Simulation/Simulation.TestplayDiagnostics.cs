using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed class Testplay1DropSiteMetric
    {
        public uint BuildingId { get; internal set; }
        public long BuiltTick { get; internal set; }
        public long FirstDeliveryTick { get; internal set; }
        public int Deliveries { get; internal set; }
        public int NearestResourceCells { get; internal set; }
    }

    public sealed class Testplay1Diagnostics
    {
        public long Tick { get; internal set; }
        public long[] VillagerTaskTicks { get; internal set; } = Array.Empty<long>();
        public long[] VillagerGatherWaitTicks { get; internal set; } = Array.Empty<long>();
        public long[] VillagerDropOffTicks { get; internal set; } = Array.Empty<long>();
        public long[] VillagerBeltWaitTicks { get; internal set; } = Array.Empty<long>();
        public long[] VillagerAtResourceNotGatheringTicks { get; internal set; } = Array.Empty<long>();
        public long[] ScoutOnlyExploreReachedStoppedTicks { get; internal set; } = Array.Empty<long>();
        public long[] ScoutOnlyNoPatrolGoalStoppedTicks { get; internal set; } = Array.Empty<long>();
        public long[] ScoutOnlyPathFailureStoppedTicks { get; internal set; } = Array.Empty<long>();
        public long[] MixedArmyScoutStoppedTicks { get; internal set; } = Array.Empty<long>();
        public int[] DropSiteCount { get; internal set; } = Array.Empty<int>();
        public int[] DropSiteUnusedCount { get; internal set; } = Array.Empty<int>();
        public int[] DropSiteDeliveries { get; internal set; } = Array.Empty<int>();
        public long[] ReceiverDeliveries { get; internal set; } = Array.Empty<long>();
        public Testplay1DropSiteMetric[] DropSites { get; internal set; } = Array.Empty<Testplay1DropSiteMetric>();
    }

    public sealed partial class Simulation
    {
        private const int TestplayFactionCount = 2;
        private const int TestplayResourceCount = 6;
        private const int TestplayTaskStateCount = 4;
        private const int TestplayReceiverCount = 6;
        private readonly long[] testplayVillagerTaskTicks = new long[TestplayFactionCount * TestplayResourceCount * TestplayTaskStateCount];
        private readonly long[] testplayGatherWaitTicks = new long[TestplayFactionCount * TestplayResourceCount];
        private readonly long[] testplayDropOffTicks = new long[TestplayFactionCount * TestplayResourceCount];
        private readonly long[] testplayBeltWaitTicks = new long[TestplayFactionCount * TestplayResourceCount];
        private readonly long[] testplayAtResourceNotGatheringTicks = new long[TestplayFactionCount * TestplayResourceCount];
        private readonly long[] testplayScoutOnlyReachedStoppedTicks = new long[TestplayFactionCount];
        private readonly long[] testplayScoutOnlyNoPatrolGoalStoppedTicks = new long[TestplayFactionCount];
        private readonly long[] testplayScoutOnlyPathFailureStoppedTicks = new long[TestplayFactionCount];
        private readonly long[] testplayMixedArmyScoutStoppedTicks = new long[TestplayFactionCount];
        private readonly long[] testplayReceiverDeliveries = new long[TestplayFactionCount * TestplayReceiverCount];
        private int[] testplayDropSiteDeliveries = Array.Empty<int>();
        private long[] testplayDropSiteBuiltTicks = Array.Empty<long>();
        private long[] testplayDropSiteFirstDeliveryTicks = Array.Empty<long>();
        private int[] testplayDropSiteNearestResourceCells = Array.Empty<int>();

        private void RecordTestplayVillagerState(VillagerState villager)
        {
            if (!LargeEconomyOn || villager.FactionId < 1 || villager.FactionId > TestplayFactionCount
                || villager.NodeId == 0 || villager.NodeId > world.Nodes.Length) return;
            int resource = LargeWorkResourceIndex(world.Nodes[villager.NodeId - 1].Definition.Kind);
            if (resource < 0) return;
            int faction = (int)villager.FactionId - 1;
            int state = villager.Task == VillagerTask.ToNode ? 0
                : villager.Task == VillagerTask.Gathering ? 1
                : villager.Task == VillagerTask.ToDropOff ? 2 : 3;
            testplayVillagerTaskTicks[(faction * TestplayResourceCount + resource) * TestplayTaskStateCount + state]++;
            if (villager.Task == VillagerTask.Gathering && world.Tick < villager.NextGatherTick)
                testplayGatherWaitTicks[faction * TestplayResourceCount + resource]++;
            if (villager.Task == VillagerTask.ToDropOff)
            {
                testplayDropOffTicks[faction * TestplayResourceCount + resource]++;
                if (villager.BeltWaitUntilTick > 0)
                    testplayBeltWaitTicks[faction * TestplayResourceCount + resource]++;
            }
            if (InRange(villager.Position, world.Nodes[villager.NodeId - 1].Definition.Position, GatherReach)
                && villager.Task != VillagerTask.Gathering)
                testplayAtResourceNotGatheringTicks[faction * TestplayResourceCount + resource]++;
        }

        private void RecordTestplayScoutTick()
        {
            if (!LargeEconomyOn) return;
            for (int i = 0; i < world.Armies.Length; i++)
            {
                var army = world.Armies[i];
                int faction = (int)army.Definition.FactionId - 1;
                if (faction < 0 || faction >= TestplayFactionCount || army.SoldierIds == null) continue;
                bool anyScout = false, allLiveScouts = true;
                int stoppedScouts = 0;
                foreach (uint id in army.SoldierIds)
                {
                    if (id == 0 || id > world.Soldiers.Length) continue;
                    var soldier = world.Soldiers[id - 1];
                    if (!soldier.Alive) continue;
                    if (soldier.Initial.Kind != UnitKind.Scout) allLiveScouts = false;
                    else
                    {
                        anyScout = true;
                        if (!soldier.IsMoving) stoppedScouts++;
                    }
                }
                if (!anyScout || stoppedScouts == 0) continue;
                if (!allLiveScouts)
                {
                    testplayMixedArmyScoutStoppedTicks[faction] += stoppedScouts;
                    continue;
                }
                if (LargeExploreReached((uint)faction + 1)) testplayScoutOnlyReachedStoppedTicks[faction] += stoppedScouts;
                else if (army.PathImpossible) testplayScoutOnlyPathFailureStoppedTicks[faction] += stoppedScouts;
                else if (!army.ExploreHasGoal) testplayScoutOnlyNoPatrolGoalStoppedTicks[faction] += stoppedScouts;
            }
        }

        private void EnsureTestplayDropSiteCapacity(uint id)
        {
            int needed = checked((int)id + 1);
            if (testplayDropSiteDeliveries.Length >= needed) return;
            int old = testplayDropSiteBuiltTicks.Length;
            Array.Resize(ref testplayDropSiteDeliveries, needed);
            Array.Resize(ref testplayDropSiteBuiltTicks, needed);
            Array.Resize(ref testplayDropSiteFirstDeliveryTicks, needed);
            Array.Resize(ref testplayDropSiteNearestResourceCells, needed);
            for (int i = old; i < needed; i++)
            {
                testplayDropSiteBuiltTicks[i] = -1;
                testplayDropSiteFirstDeliveryTicks[i] = -1;
                testplayDropSiteNearestResourceCells[i] = -1;
            }
        }

        private void RecordTestplayDropSiteSnapshot()
        {
            if (!LargeEconomyOn) return;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.Kind != BuildingKind.DropSite) continue;
                EnsureTestplayDropSiteCapacity(building.Id);
                int index = (int)building.Id;
                if (testplayDropSiteBuiltTicks[index] >= 0) continue;
                testplayDropSiteBuiltTicks[index] = world.Tick;
                int origin = world.Map.Cell(BuildingCenter(building));
                int width = world.Config.Map.WidthCells;
                int ox = origin % width, oz = origin / width, best = int.MaxValue;
                for (int n = 0; n < world.Nodes.Length; n++)
                {
                    int cell = world.Map.Cell(world.Nodes[n].Definition.Position);
                    int distance = Math.Abs(cell % width - ox) + Math.Abs(cell / width - oz);
                    if (distance < best) best = distance;
                }
                testplayDropSiteNearestResourceCells[index] = best == int.MaxValue ? -1 : best;
            }
        }

        private int TestplayDropDestination(VillagerState villager, (SimPoint point, Fix64 reach, uint storageId) drop)
        {
            if (drop.storageId != 0) return (int)drop.storageId;
            if (ProcessingKilnTarget(villager) || (GlassworksOn && villager.HaulTo != 0)) return -2;
            int bestId = 0;
            BigInteger bestDistance = -1;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.FactionId != villager.FactionId) continue;
                if (building.Kind != BuildingKind.DropSite && building.Kind != BuildingKind.Town) continue;
                if (!InRange(BuildingCenter(building), drop.point, Fix64.FromInt(8))) continue;
                var distance = DistanceSquared(BuildingCenter(building), drop.point);
                if (bestId == 0 || distance < bestDistance)
                {
                    bestId = (int)building.Id;
                    bestDistance = distance;
                }
            }
            return bestId;
        }

        private void RecordTestplayDelivery(VillagerState villager,
            (SimPoint point, Fix64 reach, uint storageId) drop, int amount)
        {
            if (!LargeEconomyOn || amount <= 0) return;
            int destination = TestplayDropDestination(villager, drop);
            int factionOffset = checked((int)(villager.FactionId - 1) * TestplayReceiverCount);
            int receiver = destination == 0 ? 0 : destination == -3 ? 2 : destination == -2 ? 4 : 5;
            if (destination > 0 && destination <= world.BuildingCount)
                receiver = world.Buildings[destination - 1].Kind == BuildingKind.DropSite ? 1 : 3;
            testplayReceiverDeliveries[factionOffset + receiver] = checked(testplayReceiverDeliveries[factionOffset + receiver] + amount);
            if (destination <= 0 || destination >= testplayDropSiteDeliveries.Length
                || world.Buildings[destination - 1].Kind != BuildingKind.DropSite) return;
            EnsureTestplayDropSiteCapacity((uint)destination);
            if (testplayDropSiteBuiltTicks[destination] >= 0)
            {
                testplayDropSiteDeliveries[destination] = checked(testplayDropSiteDeliveries[destination] + amount);
                if (testplayDropSiteFirstDeliveryTicks[destination] < 0)
                    testplayDropSiteFirstDeliveryTicks[destination] = world.Tick;
            }
        }

        public Testplay1Diagnostics CaptureTestplay1Diagnostics()
        {
            var result = new Testplay1Diagnostics
            {
                Tick = world.Tick,
                VillagerTaskTicks = (long[])testplayVillagerTaskTicks.Clone(),
                VillagerGatherWaitTicks = (long[])testplayGatherWaitTicks.Clone(),
                VillagerDropOffTicks = (long[])testplayDropOffTicks.Clone(),
                VillagerBeltWaitTicks = (long[])testplayBeltWaitTicks.Clone(),
                VillagerAtResourceNotGatheringTicks = (long[])testplayAtResourceNotGatheringTicks.Clone(),
                ScoutOnlyExploreReachedStoppedTicks = (long[])testplayScoutOnlyReachedStoppedTicks.Clone(),
                ScoutOnlyNoPatrolGoalStoppedTicks = (long[])testplayScoutOnlyNoPatrolGoalStoppedTicks.Clone(),
                ScoutOnlyPathFailureStoppedTicks = (long[])testplayScoutOnlyPathFailureStoppedTicks.Clone(),
                MixedArmyScoutStoppedTicks = (long[])testplayMixedArmyScoutStoppedTicks.Clone(),
            };
            var sites = new List<Testplay1DropSiteMetric>();
            var count = new int[TestplayFactionCount];
            var unused = new int[TestplayFactionCount];
            var deliveries = new int[TestplayFactionCount];
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.Kind != BuildingKind.DropSite
                    || building.FactionId < 1 || building.FactionId > TestplayFactionCount) continue;
                EnsureTestplayDropSiteCapacity(building.Id);
                int index = (int)building.Id;
                int faction = (int)building.FactionId - 1;
                count[faction]++;
                deliveries[faction] = checked(deliveries[faction] + testplayDropSiteDeliveries[index]);
                if (testplayDropSiteDeliveries[index] == 0) unused[faction]++;
                sites.Add(new Testplay1DropSiteMetric
                {
                    BuildingId = building.Id,
                    BuiltTick = testplayDropSiteBuiltTicks[index],
                    FirstDeliveryTick = testplayDropSiteFirstDeliveryTicks[index],
                    Deliveries = testplayDropSiteDeliveries[index],
                    NearestResourceCells = testplayDropSiteNearestResourceCells[index]
                });
            }
            result.DropSiteCount = count;
            result.DropSiteUnusedCount = unused;
            result.DropSiteDeliveries = deliveries;
            result.ReceiverDeliveries = (long[])testplayReceiverDeliveries.Clone();
            result.DropSites = sites.ToArray();
            return result;
        }
    }
}
