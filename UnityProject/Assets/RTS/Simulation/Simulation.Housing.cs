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

        /// <summary>
        /// S-4b: with ArmyGrowth the ceiling is its fixed PopulationCap (200); otherwise the ages still lift the ceiling
        /// as before, so EconomyScale never lowers the cap.
        /// </summary>
        private int ScaledHousingCeiling(int age)
        {
            var rules = world.Config.Economy;
            return EconomyScaleOn && rules.ArmyGrowth ? rules.PopulationCap : rules.PopulationCap + AgeRoom(age);
        }

        /// <summary>V3-5 (32 #7, #10): how much the ages lift the population ceiling - the second and the third each add their bonus.</summary>
        private int AgeRoom(int age)
        {
            var rules = world.Config.Economy;
            return (age >= 2 ? rules.Age2PopulationBonus : 0) + (age >= 3 ? rules.Age3PopulationBonus : 0);
        }

        private int PopCapFor(uint faction)
        {
            var rules = world.Config.Economy;
            if (!AgesOn) return rules.PopulationCap;
            int ceiling = ScaledHousingCeiling(world.Economies[faction - 1].Age);
            int houses = 0, grandHouses = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.House) houses++;
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.GrandHouse) grandHouses++;
            }
            return Math.Min(ceiling, rules.BasePopulation + rules.HousePopulation * houses + rules.HousePopulation * 3 * grandHouses);
        }

        /// <summary>
        /// AI phase: one house at a time, near the core, once the population (with the queues) comes within HouseMargin
        /// of the cap and a house can still raise it.
        /// </summary>
        private void DecideHouse(uint faction)
        {
            // Saving to advance comes first: the base that makes it save already has its villagers.
            if (!AgesOn || SavingToAdvance(faction)) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int cap = PopCapFor(faction);
            int ceiling = ScaledHousingCeiling(economy.Age);
            bool metropolis = MetropolisAllowed(faction);
            BuildingKind kind = metropolis ? BuildingKind.GrandHouse : BuildingKind.House;
            int size = metropolis ? rules.GrandHouseSizeCells : rules.HouseSizeCells;
            int wood = metropolis ? rules.GrandHouseWoodCost : rules.HouseWoodCost;
            if (cap >= ceiling || economy.Wood < wood) return;
            int population = LivingVillagers(faction) + LivingSoldiers(faction) + economy.Queued + QueuedInfantry(faction) + QueuedTownVillagers(faction);
            // S-4b: several barracks and a larger villager target fill the cap faster, so the scaled economy starts the
            // next house earlier. Waiting for the villager target here froze the population at the base cap.
            if (population + (EconomyScaleOn ? ScaledHouseMargin : HouseMargin) < cap) return;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && !b.Complete && b.FactionId == faction && b.Kind == kind) return; // one is on its way
            }
            int searchRadius = EconomyScaleOn ? Math.Max(world.Config.Map.WidthCells, world.Config.Map.HeightCells) : SiteSearchRadiusCells;
            int origin = FindSiteNear(faction, size, world.Map.Cell(OwnCore(faction).Definition.Position), searchRadius);
            if (origin >= 0) PlaceBuildingAt(faction, kind, origin, Facing.North, 0);
        }
    }
}
