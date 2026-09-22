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
            int ceiling = rules.PopulationCap + AgeRoom(world.Economies[faction - 1].Age);
            int houses = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.House) houses++;
            }
            return Math.Min(ceiling, rules.BasePopulation + rules.HousePopulation * houses);
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
            int ceiling = rules.PopulationCap + AgeRoom(economy.Age);
            if (cap >= ceiling || economy.Wood < rules.HouseWoodCost) return;
            int population = LivingVillagers(faction) + LivingSoldiers(faction) + economy.Queued + QueuedInfantry(faction);
            if (population + HouseMargin < cap) return;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && !b.Complete && b.FactionId == faction && b.Kind == BuildingKind.House) return; // one is on its way
            }
            int origin = FindSite(faction, rules.HouseSizeCells);
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.House, origin, Facing.North, 0);
        }
    }
}
