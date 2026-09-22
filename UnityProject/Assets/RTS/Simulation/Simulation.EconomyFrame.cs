using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// Ver.3: the economy part of a faction frame (V3-1 PR5). Own villagers and buildings in full; enemy villagers and
    /// buildings only while the faction sees them, and enemy villagers as bare positions, as enemy soldiers are shown.
    /// Resources are public in V3-1. The frame is display data only and is never read back into the simulation.
    /// </summary>
    public sealed partial class Simulation
    {
        private EconomyView EconomyViewFor(uint faction)
        {
            if (!EconomyOn) return null;
            var rules = world.Config.Economy;
            var economy = world.Economies[faction - 1];
            var villagers = new List<VillagerView>();
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (!v.Alive) continue;
                if (v.FactionId == faction)
                    villagers.Add(new VillagerView(v.Id, true, v.Position, Activity(v.Task), v.CarryKind, v.Carry, v.Hp, v.Held));
                else if (IsVisibleTo(faction, v.Position))
                    villagers.Add(new VillagerView(0, false, v.Position, VillagerActivity.Idle, 0, 0, 0));
            }
            var buildings = new List<BuildingView>();
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive) continue;
                bool own = b.FactionId == faction;
                if (!own && !BuildingVisibleTo(faction, b)) continue;
                int size = SizeOf(b.Kind);
                buildings.Add(new BuildingView(b.Id, b.FactionId, b.Kind, FootprintCenter(b.OriginCell, size), size * world.Config.Map.CellSizeMeters,
                    own ? b.Hp : 0, own ? HpOf(b.Kind) : 0, b.Complete, own ? b.Progress : 0, WorkOf(b.Kind),
                    own ? b.Queued : 0, own ? b.TrainRemaining : 0, b.Facing, own ? b.Input : 0, own ? b.Output : 0, own && b.Held));
            }
            var resources = new List<ResourceView>();
            foreach (var n in world.Nodes)
                if (n.Remaining > 0) resources.Add(new ResourceView(n.Definition.Id, n.Definition.Kind, n.Definition.Position, n.Remaining));
            int population = LivingVillagers(faction) + LivingSoldiers(faction);
            // V3-2: own belts in full, enemy belts on cells the faction sees now (without what they carry).
            var belts = new List<BeltView>();
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                var b = world.Belts[cell];
                if (b.FactionId == 0) continue;
                bool own = b.FactionId == faction;
                if (!own && !world.Factions[faction - 1].VisibleCells[cell]) continue;
                belts.Add(new BeltView(cell, b.FactionId, b.Facing, own ? b.Item : 0, own ? b.Progress : 0, own && b.Held));
            }
            return new EconomyView(economy.Food, economy.Wood, population, rules.PopulationCap, economy.Queued, economy.TrainRemaining,
                !economy.AutoOff, rules.BarracksSizeCells, rules.BarracksWoodCost, rules.VillagerFoodCost, InfantryFoodFor(faction),
                InfantryWoodFor(faction), villagers, buildings, resources,
                rules.Industry, economy.Ore, economy.Metal, rules.BeltWoodCost, rules.BeltTicksPerCell, belts,
                InfantryMetalFor(faction), rules.MineWoodCost, rules.SmelterWoodCost, rules.MineSizeCells, rules.SmelterSizeCells, economy.CoreHeld, economy.Policy,
                rules.Ages, economy.Civ, economy.AdvancingTo, economy.AdvanceRemaining, rules.AdvanceFoodCost, rules.AdvanceWoodCost,
                rules.FarmWoodCost, rules.FarmSizeCells, rules.Ages ? rules.ScoutFoodCost : 0);
        }

        private static VillagerActivity Activity(VillagerTask task) => task switch
        {
            VillagerTask.ToNode => VillagerActivity.ToResource,
            VillagerTask.Gathering => VillagerActivity.Gathering,
            VillagerTask.ToDropOff => VillagerActivity.Returning,
            VillagerTask.ToBuild => VillagerActivity.ToBuild,
            VillagerTask.Building => VillagerActivity.Building,
            VillagerTask.ToPickup => VillagerActivity.Hauling,
            VillagerTask.ToDeliver => VillagerActivity.Hauling,
            _ => VillagerActivity.Idle
        };
    }
}
