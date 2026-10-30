using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 houses (technical-design-v3 32 #2). On a map with ages the population cap starts at BasePopulation and each
    /// finished house adds HousePopulation, never over PopulationCap; elsewhere the cap is PopulationCap as before.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>The automatic economy builds when fewer than this many places are left under the cap.</summary>
        private const int HouseMargin = 3;

        /// <summary>S-4b: the margin while EconomyScale is on.</summary>
        private const int ScaledHouseMargin = 10;

        private bool HouseFlowOn => EconomyOn && world.Config.Economy.HouseFlow;

        /// <summary>
        /// S-4b: with ArmyGrowth the ceiling is its fixed PopulationCap (200); otherwise the ages still lift the ceiling
        /// as before, so EconomyScale never lowers the cap.
        /// </summary>
        private int ScaledHousingCeiling(uint faction, int age)
        {
            var rules = world.Config.Economy;
            int ceiling = EconomyScaleOn && rules.ArmyGrowth ? rules.PopulationCap : rules.PopulationCap + AgeRoom(age);
            return AgeStrength(faction, CivKind.Agrarian, 4) ? checked(ceiling + rules.AgrarianPopulationCapBonus) : ceiling;
        }

        /// <summary>V3-5 (32 #7, #10): how much the ages lift the population ceiling - the second and the third each add their bonus.</summary>
        private int AgeRoom(int age)
        {
            var rules = world.Config.Economy;
            return (age >= 2 ? rules.Age2PopulationBonus : 0) + (age >= 3 ? rules.Age3PopulationBonus : 0)
                + (AgeFourOn && age >= 4 ? rules.Age4PopulationBonus : 0);
        }

        private int PopCapFor(uint faction)
        {
            var rules = world.Config.Economy;
            if (!AgesOn) return rules.PopulationCap;
            int ceiling = ScaledHousingCeiling(faction, world.Economies[faction - 1].Age);
            int houses = 0, grandHouses = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.House) houses++;
                if (IsActualBuilding(b) && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.GrandHouse) grandHouses++;
            }
            // Early arms: a house holds twice as many. One house at a time at 5 each could not keep up with several
            // barracks, and the army stopped at the cap (66 of 70 in a test play, 10-11).
            int perHouse = EarlyArmsOn ? rules.HousePopulation * 2 : rules.HousePopulation;
            return Math.Min(ceiling, rules.BasePopulation + perHouse * houses + perHouse * 3 * grandHouses);
        }

        /// <summary>
        /// AI phase: one house at a time, near the core, once the population (with the queues) comes within HouseMargin
        /// of the cap and a house can still raise it.
        /// </summary>
        private void DecideHouse(uint faction)
        {
            // Saving to advance comes first: the base that makes it save already has its villagers.
            if (!AgesOn) return;
            bool saving = false;
            MeasureSubphase("AI.DecideEconomy.DecideHouse.SavingToAdvance", () => saving = SavingToAdvance(faction));
            if (saving) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int cap = 0;
            MeasureSubphase("AI.DecideEconomy.DecideHouse.PopCap", () => cap = PopCapFor(faction));
            int ceiling = ScaledHousingCeiling(faction, economy.Age);
            bool metropolis = MetropolisAllowed(faction);
            BuildingKind kind = metropolis ? BuildingKind.GrandHouse : BuildingKind.House;
            int size = metropolis ? rules.GrandHouseSizeCells : rules.HouseSizeCells;
            int wood = metropolis ? rules.GrandHouseWoodCost : rules.HouseWoodCost;
            if (cap >= ceiling || economy.Wood < wood) return;
            int population = LivingVillagers(faction) + LivingSoldiers(faction) + economy.Queued + QueuedInfantry(faction) + QueuedTownVillagers(faction);
            // S-4b: several barracks and a larger villager target fill the cap faster, so the scaled economy starts the
            // next house earlier. Waiting for the villager target here froze the population at the base cap.
            int margin = EconomyScaleOn ? ScaledHouseMargin : HouseMargin;
            int concurrentLimit = 1;
            if (HouseFlowOn)
            {
                int cacheIndex = checked((int)faction - 1);
                if (world.Tick >= houseFlowNextMaintenanceTicks[cacheIndex])
                {
                    int refreshedProducers = 1; // the faction core
                    for (int i = 0; i < world.BuildingCount; i++)
                    {
                        var producer = world.Buildings[i];
                        if (!IsActualBuilding(producer) || producer.FactionId != faction) continue;
                        if (producer.Complete && (producer.Kind == BuildingKind.Town || producer.Kind == BuildingKind.Barracks)) refreshedProducers++;
                    }
                    houseFlowProducerCounts[cacheIndex] = refreshedProducers;
                    CancelStalledHouses(faction);
                    houseFlowNextMaintenanceTicks[cacheIndex] = checked(world.Tick + 20);
                }
                int producers = houseFlowProducerCounts[cacheIndex];
                margin = Math.Max(margin, checked(world.Config.Economy.HouseFlowMarginPerProducer * producers));
                concurrentLimit = Math.Min(world.Config.Economy.HouseFlowMaxConcurrent, 1 + producers / 3);
            }
            if (population + margin < cap) return;
            bool alreadyPlanned = false;
            MeasureSubphase("AI.DecideEconomy.DecideHouse.Existing", () =>
            {
                int building = 0;
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var b = world.Buildings[i];
                    if (HouseFlowOn)
                    {
                        // 38.10: only real sites count, and several may be on their way at once.
                        if (IsActualBuilding(b) && !b.Complete && b.FactionId == faction
                            && (b.Kind == BuildingKind.House || b.Kind == BuildingKind.GrandHouse) && ++building >= concurrentLimit)
                        { alreadyPlanned = true; return; }
                    }
                    else if (IsPlannedBuilding(b) && !b.Complete && b.FactionId == faction && b.Kind == kind)
                    { alreadyPlanned = true; return; } // one is on its way
                }
            });
            if (alreadyPlanned) return;
            int searchRadius = EconomyScaleOn ? Math.Max(world.Config.Map.WidthCells, world.Config.Map.HeightCells) : SiteSearchRadiusCells;
            int origin = -1;
            MeasureSubphase("AI.DecideEconomy.DecideHouse.FindSite", () =>
                origin = FindSiteNear(faction, size, world.Map.Cell(OwnCore(faction).Definition.Position), searchRadius));
            if (origin >= 0)
            {
                SetAiReason(AiReason.PopulationCap, Math.Max(1, cap - population));
                MeasureSubphase("AI.DecideEconomy.DecideHouse.Place", () => PlaceBuildingAt(faction, kind, origin, Facing.North, 0));
            }
        }

        private void CancelStalledHouses(uint faction)
        {
            var rules = world.Config.Economy;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || building.Complete || building.Progress != 0 || building.FactionId != faction
                    || (building.Kind != BuildingKind.House && building.Kind != BuildingKind.GrandHouse)
                    || !housePlacementTicks.TryGetValue(building.Id, out long placedTick)
                    || world.Tick - placedTick < rules.HouseFlowStallTicks) continue;
                CancelAutomaticHouse(i);
            }
        }

        private void CancelAutomaticHouse(int index)
        {
            ref var building = ref world.Buildings[index];
            uint faction = building.FactionId;
            MarkLinesForBuilding(faction, building.Id);
            if (EasyRebuildOn) RefundBuilding(faction, ref building);
            building.Alive = false;
            RemoveRegionCenter(building);
            foreach (int cell in Footprint(building)) world.Map.SetPassable(cell, true);
            housePlacementTicks.Remove(building.Id);
            TerrainChanged();
        }

        private void UpdateHouseFlowStopTicks()
        {
            // Observation only (never read by the simulation), so it also counts with the flag off for comparison.
            if (!EconomyOn || !world.Config.Economy.LargeEconomy) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                int population = LivingVillagers(faction) + LivingSoldiers(faction) + world.Economies[faction - 1].Queued
                    + QueuedInfantry(faction) + QueuedTownVillagers(faction);
                int limit = PopCapFor(faction);
                if (population >= limit - 2) houseLimitStopTicks[faction - 1] = checked(houseLimitStopTicks[faction - 1] + 1);
            }
        }
    }
}
