using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>Read-only, non-canonical counters for LargeEconomy measurement. It never feeds simulation decisions.</summary>
    public sealed class LargeEconomyObservation
    {
        public long Tick;
        public LargeEconomyFactionObservation[] Factions = Array.Empty<LargeEconomyFactionObservation>();
    }

    public sealed class LargeEconomyFactionObservation
    {
        public uint FactionId;
        public int Villagers, Soldiers;
        public int PlannedTowns, TownsBuilding, CompletedTowns, TownVillagersQueued, TownVillagersProduced;
        public int Houses, Barracks, DropSites, Farms, BeltCells, BeltDelivered, LumberCamps, OtherBuildings;
        public int MinimumDropSiteDistanceMeters, CoreHp, EnemySoldiersAtCore;
        public int[] Workers = new int[6];
        public int[] WorkerRoundTripTicks = new int[6];
        public int[] Income = new int[6];
        public int TownResourceIncome;
        public int BarracksResourceWaiting, BarracksFoodWaiting, BarracksWoodWaiting, BarracksMetalWaiting;
        public int BarracksPopulationWaiting, BarracksTraining;
    }

    public sealed partial class Simulation
    {
        /// <summary>Captures LargeEconomy counters at an observation point; no canonical state is changed.</summary>
        public LargeEconomyObservation CaptureLargeEconomyObservation()
        {
            var result = new LargeEconomyObservation { Tick = world.Tick, Factions = new LargeEconomyFactionObservation[2] };
            for (uint faction = 1; faction <= 2; faction++)
            {
                var value = new LargeEconomyFactionObservation { FactionId = faction };
                value.Villagers = LivingVillagers(faction);
                value.Soldiers = LivingSoldiers(faction);
                var core = OwnCore(faction);
                value.CoreHp = core.Hp;
                foreach (int soldierIndex in world.SoldierTraversal)
                {
                    var soldier = world.Soldiers[soldierIndex];
                    if (soldier.Alive && soldier.Initial.FactionId != faction
                        && InRange(soldier.Position, core.Definition.Position, world.Config.Rules.CoreRadius))
                        value.EnemySoldiersAtCore++;
                }
                value.TownVillagersProduced = largeTownVillagersProduced[faction - 1];
                var dropSites = new List<SimPoint>();
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var b = world.Buildings[i];
                    if (!IsActualBuilding(b) || b.FactionId != faction) continue;
                    if (b.Kind == BuildingKind.Town)
                    {
                        value.PlannedTowns++;
                        if (b.Complete) value.CompletedTowns++; else value.TownsBuilding++;
                        value.TownVillagersQueued = checked(value.TownVillagersQueued + b.Queued);
                    }
                    else if (b.Kind == BuildingKind.House) value.Houses++;
                    else if (b.Kind == BuildingKind.Farm) value.Farms++;
                    else if (b.Kind == BuildingKind.Barracks)
                    {
                        value.Barracks++;
                        if (!b.Complete || b.Held) continue;
                        if (b.TrainRemaining > 0 || b.Queued > 0) value.BarracksTraining++;
                        else if (BarracksResourceBlocked(faction))
                        {
                            value.BarracksResourceWaiting++;
                            ref var economy = ref world.Economies[faction - 1];
                            if (economy.Food < InfantryFoodFor(faction)) value.BarracksFoodWaiting++;
                            if (economy.Wood < InfantryWoodFor(faction)) value.BarracksWoodWaiting++;
                            if (economy.Metal < InfantryMetalFor(faction)) value.BarracksMetalWaiting++;
                        }
                        else if (BarracksPopulationBlocked(faction)) value.BarracksPopulationWaiting++;
                    }
                    else if (b.Kind == BuildingKind.DropSite)
                    {
                        value.DropSites++;
                        dropSites.Add(BuildingCenter(b));
                    }
                    else if (b.Kind == BuildingKind.LumberCamp) value.LumberCamps++;
                    else value.OtherBuildings++;
                }
                if (dropSites.Count >= 2)
                {
                    var best = new System.Numerics.BigInteger(long.MaxValue);
                    for (int i = 0; i < dropSites.Count; i++)
                        for (int j = i + 1; j < dropSites.Count; j++)
                        {
                            var distance = DistanceSquared(dropSites[i], dropSites[j]);
                            if (distance < best) best = distance;
                        }
                    value.MinimumDropSiteDistanceMeters = checked((int)((long)FixMath.IntegerSqrt(best) / 65536L));
                }
                for (int cell = 0; cell < world.Belts.Length; cell++)
                    if (world.Belts[cell].FactionId == faction) value.BeltCells++;
                value.BeltDelivered = largeBeltDelivered[faction - 1];
                for (int resource = 0; resource < 6; resource++)
                {
                    value.Income[resource] = largeDelivered[(int)(faction - 1) * 6 + resource];
                    value.Workers[resource] = WorkerCount(faction, resource);
                    value.WorkerRoundTripTicks[resource] = WorkerDistanceTicks(faction, resource);
                }
                value.TownResourceIncome = largeTownDelivered[faction - 1];
                result.Factions[faction - 1] = value;
            }
            return result;
        }

        private bool BarracksResourceBlocked(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            return economy.Food < InfantryFoodFor(faction) || economy.Wood < InfantryWoodFor(faction)
                || economy.Metal < InfantryMetalFor(faction);
        }

        private bool BarracksPopulationBlocked(uint faction)
        {
            int population = LivingVillagers(faction) + LivingSoldiers(faction) + world.Economies[faction - 1].Queued
                + QueuedInfantry(faction) + QueuedTownVillagers(faction);
            return population >= PopCapFor(faction) || !HasInfantryRoom(faction);
        }

        private int WorkerDistanceTicks(uint faction, int resource)
        {
            long sum = 0; int count = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.NodeId == 0 || v.NodeId > world.Nodes.Length
                    || v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering && v.Task != VillagerTask.ToDropOff
                    || LargeWorkResourceIndex(world.Nodes[v.NodeId - 1].Definition.Kind) != resource) continue;
                var drop = DropOff(v).point;
                int routeLength = world.Map.SharedRoute(world.Map.Cell(world.Nodes[v.NodeId - 1].Definition.Position), drop, faction).Length;
                if (routeLength <= 0) continue;
                long distanceRaw = checked((long)Math.Max(0, routeLength - 1) * world.Config.Map.CellSizeMeters * 65536L);
                long ticks = checked((distanceRaw * 2 + world.VillagerStep.Raw - 1) / world.VillagerStep.Raw);
                sum = checked(sum + ticks); count++;
            }
            return count == 0 ? 0 : checked((int)(sum / count));
        }

        private int WorkerCount(uint faction, int resource)
        {
            int count = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && v.NodeId > 0 && v.NodeId <= world.Nodes.Length
                    && (v.Task == VillagerTask.ToNode || v.Task == VillagerTask.Gathering || v.Task == VillagerTask.ToDropOff)
                    && LargeWorkResourceIndex(world.Nodes[v.NodeId - 1].Definition.Kind) == resource) count++;
            }
            return count;
        }

        private void RecordLargeDelivery(VillagerState villager, ResourceKind kind, int amount)
        {
            if (!LargeEconomyOn || amount <= 0) return;
            int resource = LargeWorkResourceIndex(kind);
            if (resource < 0) return;
            if (villager.FactionId < 1 || villager.FactionId > 2 || villager.NodeId == 0 || villager.NodeId > world.Nodes.Length) return;
            int index = checked((int)(villager.FactionId - 1) * 6 + resource);
            largeDelivered[index] = checked(largeDelivered[index] + amount);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var town = world.Buildings[i];
                if (!IsActualBuilding(town) || !town.Complete || town.FactionId != villager.FactionId || town.Kind != BuildingKind.Town) continue;
                if (InRange(BuildingCenter(town), world.Nodes[villager.NodeId - 1].Definition.Position,
                    Fix64.FromInt(world.Config.Economy.TownResourceReach)))
                {
                    largeTownDelivered[villager.FactionId - 1] = checked(largeTownDelivered[villager.FactionId - 1] + amount);
                    break;
                }
            }
        }

        private void RecordLargeBeltDelivery(uint faction)
        {
            if (LargeEconomyOn && faction >= 1 && faction <= 2)
                largeBeltDelivered[faction - 1] = checked(largeBeltDelivered[faction - 1] + 1);
        }
    }
}
