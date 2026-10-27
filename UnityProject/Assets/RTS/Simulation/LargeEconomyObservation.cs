using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    internal static class LargeEconomyUseKind
    {
        internal const int House = 0;
        internal const int Barracks = 1;
        internal const int Infantry = 2;
        internal const int ProcessingBuilding = 3;
        internal const int FactoryTemplate = 4;
        internal const int Belt = 5;
        internal const int Town = 6;
        internal const int DropSite = 7;
        internal const int Research = 8;
        internal const int AgeUp = 9;
        internal const int Other = 10;
        internal const int Count = 11;
        internal static readonly string[] Names =
        {
            "house", "barracks", "infantry", "processingBuilding", "factoryTemplate", "belt",
            "town", "dropSite", "research", "ageUp", "other"
        };
    }

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
        public int[] TownConnectedLines = Array.Empty<int>();
        public int CoreNearBuildings, CoreFarBuildings;
        public int Houses, Barracks, DropSites, Farms, ConnectedFarms, BeltCells, BeltDelivered, LumberCamps, OtherBuildings;
        public int WoodMachines, StoneMachines, WoodMachinesActive, StoneMachinesActive, WoodMachineOutputFull, StoneMachineOutputFull;
        public int MinimumDropSiteDistanceMeters, CoreHp, EnemySoldiersAtCore;
        public int[] Workers = new int[6];
        public int[] ToResourceWorkers = new int[6];
        public int[] GatheringWorkers = new int[6];
        public int[] ToDropOffWorkers = new int[6];
        public int[] WorkerRoundTripTicks = new int[6];
        public int[] WorkerRoundTripMeters = new int[6];
        public int[] Income = new int[6];
        public int[] BeltIncome = new int[6];
        // Measurement-only breakdown; these fields never feed automatic decisions.
        public int Builders, TownBuilders, FactoryBuilders, OtherBuilders, MovingVillagers, Haulers, IdleWorkers;
        public int[,] Uses = new int[LargeEconomyUseKind.Count, 2];
        public int TownResourceIncome;
        public int BarracksResourceWaiting, BarracksFoodWaiting, BarracksWoodWaiting, BarracksMetalWaiting;
        public int BarracksPopulationWaiting, BarracksTraining;
        public int AiFactoryConnectedLines, AiFactoryPrimitiveConnectedLines, AiFactoryAge1ConnectedLines;
        public int AiFactoryAge2MaterialConnectedLines, AiFactoryAge2MetalConnectedLines, AiFactoryAge3PartsConnectedLines;
        public int AiFactoryBarracksResourceWaiting;
        public int PlanksProduced, PlanksConsumed, CutStoneProduced, CutStoneConsumed;
        public int StoneToolsProduced, StoneToolsConsumed, JerkyProduced, JerkyConsumed;
        public int FramesProduced, FramesConsumed, ArmsKitProduced, ArmsKitConsumed;
        public int SiegePartsProduced, SiegePartsConsumed, MetalProduced, MetalConsumed;
        public int AiFactorySkippedUnknownResource, AiFactorySkippedNoSite, AiFactorySkippedNoRoute;
        public int AiExpandTownSkippedUnknownResource, AiExpandTownSkippedNoSite, AiExpandTownSkippedWood, AiExpandTownSkippedStone;
        public int SawmillsCompleted, SawmillsActive, SawmillPlanks;
        public int PlanksUsedWeapons, PlanksUsedConstruction, PlanksUsedScholarly, PlanksUsedBridges, PlanksUsedOther;
        public int ArmsProduced, ArmsUsedBySoldiers, ArmsStock;
    }

    public sealed partial class Simulation
    {
        // CraftTrace-only counters. They are not part of WorldState, decisions, diagnostics, or replay hashes.
        private const int CraftTraceWeapons = 0, CraftTraceConstruction = 1, CraftTraceScholarly = 2,
            CraftTraceBridges = 3, CraftTraceOther = 4;
        private readonly int[,] craftTracePlankUses = new int[2, 5];
        private void RecordCraftTracePlanks(uint faction, int kind, int amount)
        {
            if (faction < 1 || faction > 2 || amount == 0) return;
            craftTracePlankUses[faction - 1, kind] = checked(craftTracePlankUses[faction - 1, kind] + amount);
        }

        private int ConstructionUseKind(BuildingKind kind)
        {
            if (kind == BuildingKind.House || kind == BuildingKind.GrandHouse) return LargeEconomyUseKind.House;
            if (kind == BuildingKind.Barracks) return LargeEconomyUseKind.Barracks;
            if (kind == BuildingKind.Town) return LargeEconomyUseKind.Town;
            if (kind == BuildingKind.DropSite) return LargeEconomyUseKind.DropSite;
            if (AiFactoryOn && automaticEconomyAction && (kind == BuildingKind.Hut || IsCraftProcessingBuilding(kind))) return LargeEconomyUseKind.FactoryTemplate;
            if (IsCraftProcessingBuilding(kind)) return LargeEconomyUseKind.ProcessingBuilding;
            return LargeEconomyUseKind.Other;
        }

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
                var townCenters = new List<SimPoint>();
                var dropSites = new List<SimPoint>();
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var b = world.Buildings[i];
                    if (!IsActualBuilding(b) || b.FactionId != faction) continue;
                    if (InRange(BuildingCenter(b), core.Definition.Position, Fix64.FromInt(40))) value.CoreNearBuildings++;
                    else value.CoreFarBuildings++;
                    if (b.Kind == BuildingKind.Town)
                    {
                        value.PlannedTowns++;
                        if (b.Complete) value.CompletedTowns++; else value.TownsBuilding++;
                        value.TownVillagersQueued = checked(value.TownVillagersQueued + b.Queued);
                        townCenters.Add(BuildingCenter(b));
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
                    if (b.Kind == BuildingKind.Sawmill && b.Complete)
                    {
                        value.SawmillsCompleted++;
                        if (b.Input > 0 || b.Timer > 0) value.SawmillsActive++;
                        value.SawmillPlanks = checked(value.SawmillPlanks + b.Output);
                    }
                    int machineResource = b.Kind == BuildingKind.Quarry ? 2
                        : b.Kind == BuildingKind.Drill && b.NodeId > 0 && b.NodeId <= world.Nodes.Length
                            ? LargeWorkResourceIndex(world.Nodes[b.NodeId - 1].Definition.Kind) : -1;
                    if (machineResource == 1)
                    {
                        value.WoodMachines++;
                        if (b.Output < world.Config.Economy.BufferLimit && b.NodeId > 0 && b.NodeId <= world.Nodes.Length && world.Nodes[b.NodeId - 1].Remaining > 0) value.WoodMachinesActive++;
                        if (b.Output >= world.Config.Economy.BufferLimit) value.WoodMachineOutputFull++;
                    }
                    else if (machineResource == 2)
                    {
                        value.StoneMachines++;
                        if (b.Output < world.Config.Economy.BufferLimit && b.NodeId > 0 && b.NodeId <= world.Nodes.Length && world.Nodes[b.NodeId - 1].Remaining > 0) value.StoneMachinesActive++;
                        if (b.Output >= world.Config.Economy.BufferLimit) value.StoneMachineOutputFull++;
                    }
                }
                value.TownConnectedLines = TownConnectedLineCounts(faction, townCenters);
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
                if (AiFactoryOn)
                {
                    ref var factoryEconomy = ref world.Economies[faction - 1];
                    value.AiFactoryBarracksResourceWaiting = 0;
                    for (int i = 0; i < world.BuildingCount; i++)
                    {
                        var barracks = world.Buildings[i];
                        if (IsActualBuilding(barracks) && barracks.FactionId == faction && barracks.Kind == BuildingKind.Barracks
                            && AiFactoryBarracksResourceWaiting(faction, barracks)) value.AiFactoryBarracksResourceWaiting++;
                    }
                    for (int i = 0; i < world.ProcessingLines.Length; i++)
                    {
                        var line = world.ProcessingLines[i];
                        if (line.FactionId != faction || !LineBeltsWhole(i)) continue;
                        value.AiFactoryConnectedLines++;
                        if (line.Kind == ProcessingLineKind.Planks || line.Kind == ProcessingLineKind.CutStone || line.Kind == ProcessingLineKind.PrimitiveCraft)
                            value.AiFactoryPrimitiveConnectedLines++;
                        else if (line.Kind == ProcessingLineKind.Frames)
                            value.AiFactoryAge2MaterialConnectedLines++;
                        else if (line.Kind == ProcessingLineKind.CoreMetal)
                            value.AiFactoryAge2MetalConnectedLines++;
                        else if (line.Kind == ProcessingLineKind.Assembler && line.AssemblerId != 0
                            && line.AssemblerId <= world.BuildingCount && world.Buildings[line.AssemblerId - 1].Recipe == RecipeKind.SiegeParts)
                            value.AiFactoryAge3PartsConnectedLines++;
                    }
                    // The age-1 counter is deliberately kept separate from primitive and later templates.
                    value.AiFactoryAge1ConnectedLines = value.AiFactoryConnectedLines
                        - value.AiFactoryPrimitiveConnectedLines - value.AiFactoryAge2MaterialConnectedLines
                        - value.AiFactoryAge2MetalConnectedLines - value.AiFactoryAge3PartsConnectedLines;
                    value.PlanksProduced = factoryEconomy.PlanksProduced; value.PlanksConsumed = factoryEconomy.PlanksConsumed;
                    value.CutStoneProduced = factoryEconomy.CutStoneProduced; value.CutStoneConsumed = factoryEconomy.CutStoneConsumed;
                    value.StoneToolsProduced = factoryEconomy.StoneToolsProduced; value.StoneToolsConsumed = factoryEconomy.StoneToolsConsumed;
                    value.JerkyProduced = factoryEconomy.JerkyProduced; value.JerkyConsumed = factoryEconomy.JerkyConsumed;
                    value.FramesProduced = factoryEconomy.FramesProduced; value.FramesConsumed = factoryEconomy.FramesConsumed;
                    value.ArmsKitProduced = factoryEconomy.ArmsKitProduced; value.ArmsKitConsumed = factoryEconomy.ArmsKitConsumed;
                    value.SiegePartsProduced = factoryEconomy.SiegePartsProduced; value.SiegePartsConsumed = factoryEconomy.SiegePartsConsumed;
                    value.MetalProduced = factoryEconomy.MetalProduced; value.MetalConsumed = factoryEconomy.MetalConsumed;
                    value.AiFactorySkippedUnknownResource = factoryEconomy.AiFactorySkippedUnknownResource;
                    value.AiFactorySkippedNoSite = factoryEconomy.AiFactorySkippedNoSite;
                    value.AiFactorySkippedNoRoute = factoryEconomy.AiFactorySkippedNoRoute;
                }
                if (AiExpandOn)
                {
                    value.AiExpandTownSkippedUnknownResource = aiExpandTownSkippedUnknownResource[faction - 1];
                    value.AiExpandTownSkippedNoSite = aiExpandTownSkippedNoSite[faction - 1];
                    value.AiExpandTownSkippedWood = aiExpandTownSkippedWood[faction - 1];
                    value.AiExpandTownSkippedStone = aiExpandTownSkippedStone[faction - 1];
                }
                value.ConnectedFarms = largeConnectedFarms[faction - 1];
                value.BeltDelivered = largeBeltDelivered[faction - 1];
                for (int resource = 0; resource < 6; resource++)
                {
                    value.Income[resource] = largeDelivered[(int)(faction - 1) * 6 + resource];
                    value.BeltIncome[resource] = largeBeltIncome[(int)(faction - 1) * 6 + resource];
                    value.WorkerRoundTripTicks[resource] = WorkerDistanceTicks(faction, resource);
                    value.WorkerRoundTripMeters[resource] = WorkerDistanceMeters(faction, resource);
                }
                for (int i = 0; i < world.VillagerCount; i++)
                {
                    var villager = world.Villagers[i];
                    if (!villager.Alive || villager.FactionId != faction) continue;
                    int resource = villager.NodeId > 0 && villager.NodeId <= world.Nodes.Length
                        ? LargeWorkResourceIndex(world.Nodes[villager.NodeId - 1].Definition.Kind) : -1;
                    if (resource >= 0)
                    {
                        if (villager.Task == VillagerTask.ToNode) value.ToResourceWorkers[resource]++;
                        else if (villager.Task == VillagerTask.Gathering) value.GatheringWorkers[resource]++;
                        else if (villager.Task == VillagerTask.ToDropOff) value.ToDropOffWorkers[resource]++;
                    }
                    if (villager.Task == VillagerTask.ToBuild || villager.Task == VillagerTask.Building)
                    {
                        value.Builders++;
                        if (villager.BuildingId > 0 && villager.BuildingId <= world.BuildingCount)
                        {
                            var building = world.Buildings[villager.BuildingId - 1];
                            if (building.Kind == BuildingKind.Town) value.TownBuilders++;
                            else if (AiFactoryOn && (building.Kind == BuildingKind.Hut || IsCraftProcessingBuilding(building.Kind))) value.FactoryBuilders++;
                            else value.OtherBuilders++;
                        }
                        else value.OtherBuilders++;
                    }
                    else if (villager.Task == VillagerTask.ToPickup || villager.Task == VillagerTask.ToDeliver) value.Haulers++;
                    else if (villager.Task == VillagerTask.Idle) value.IdleWorkers++;
                    if (villager.IsMoving) value.MovingVillagers++;
                }
                for (int resource = 0; resource < 6; resource++)
                    value.Workers[resource] = value.ToResourceWorkers[resource] + value.GatheringWorkers[resource] + value.ToDropOffWorkers[resource];
                for (int kind = 0; kind < LargeEconomyUseKind.Count; kind++)
                    for (int resource = 0; resource < 2; resource++)
                        value.Uses[kind, resource] = largeResourceUses[faction - 1, kind, resource];
                value.TownResourceIncome = largeTownDelivered[faction - 1];
                value.PlanksUsedWeapons = craftTracePlankUses[faction - 1, CraftTraceWeapons];
                value.PlanksUsedConstruction = craftTracePlankUses[faction - 1, CraftTraceConstruction];
                value.PlanksUsedScholarly = craftTracePlankUses[faction - 1, CraftTraceScholarly];
                value.PlanksUsedBridges = craftTracePlankUses[faction - 1, CraftTraceBridges];
                value.PlanksUsedOther = craftTracePlankUses[faction - 1, CraftTraceOther];
                var capturedEconomy = world.Economies[faction - 1];
                value.ArmsProduced = checked(capturedEconomy.ArmsKitProduced + capturedEconomy.FineArmsKitProduced);
                value.ArmsUsedBySoldiers = checked(capturedEconomy.ArmsKitConsumed + capturedEconomy.FineArmsKitConsumed);
                value.ArmsStock = checked(capturedEconomy.ArmsKit + capturedEconomy.FineArmsKit);
                result.Factions[faction - 1] = value;
            }
            return result;
        }

        private int[] TownConnectedLineCounts(uint faction, List<SimPoint> towns)
        {
            if (towns.Count == 0) return Array.Empty<int>();
            var counts = new int[towns.Count];
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction || !LineBeltsWhole(i) || line.BeltCells == null || line.BeltCells.Length == 0) continue;
                SimPoint endpoint = world.Map.Center(line.BeltCells[line.BeltCells.Length - 1]);
                for (int town = 0; town < towns.Count; town++)
                    if (InRange(endpoint, towns[town], world.Config.Rules.CoreRadius)) counts[town]++;
            }
            return counts;
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

        private int WorkerDistanceMeters(uint faction, int resource)
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
                sum = checked(sum + (long)Math.Max(0, routeLength - 1) * world.Config.Map.CellSizeMeters);
                count++;
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

        private void RecordLargeBeltDelivery(uint faction, ResourceKind kind, int amount)
        {
            if (LargeEconomyOn && faction >= 1 && faction <= 2 && amount > 0)
            {
                largeBeltDelivered[faction - 1] = checked(largeBeltDelivered[faction - 1] + 1);
                int resource = LargeWorkResourceIndex(kind);
                if (resource >= 0) largeBeltIncome[(int)(faction - 1) * 6 + resource] = checked(largeBeltIncome[(int)(faction - 1) * 6 + resource] + amount);
            }
        }

        private void RecordLargeUse(uint faction, ResourceKind resource, int kind, int amount)
        {
            if (!LargeEconomyOn || faction < 1 || faction > 2 || amount <= 0 || kind < 0 || kind >= LargeEconomyUseKind.Count) return;
            int resourceIndex = resource == ResourceKind.Wood ? 0 : resource == ResourceKind.Stone ? 1 : -1;
            if (resourceIndex >= 0) largeResourceUses[faction - 1, kind, resourceIndex] = checked(largeResourceUses[faction - 1, kind, resourceIndex] + amount);
        }
    }
}
