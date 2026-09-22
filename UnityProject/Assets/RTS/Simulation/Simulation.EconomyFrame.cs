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
                    villagers.Add(new VillagerView(v.Id, true, v.Position, Activity(v.Task), v.CarryKind, v.Carry, v.Hp));
                else if (IsVisibleTo(faction, v.Position))
                    villagers.Add(new VillagerView(0, false, v.Position, VillagerActivity.Idle, 0, 0, 0));
            }
            var buildings = new List<BuildingView>();
            int sizeMeters = rules.BarracksSizeCells * world.Config.Map.CellSizeMeters;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive) continue;
                bool own = b.FactionId == faction;
                if (!own && !BuildingVisibleTo(faction, b)) continue;
                buildings.Add(new BuildingView(b.Id, b.FactionId, b.Kind, FootprintCenter(b.OriginCell), sizeMeters,
                    own ? b.Hp : 0, own ? rules.BarracksHp : 0, b.Complete, own ? b.Progress : 0, rules.BarracksWork,
                    own ? b.Queued : 0, own ? b.TrainRemaining : 0));
            }
            var resources = new List<ResourceView>();
            foreach (var n in world.Nodes)
                if (n.Remaining > 0) resources.Add(new ResourceView(n.Definition.Id, n.Definition.Kind, n.Definition.Position, n.Remaining));
            int population = LivingVillagers(faction) + LivingSoldiers(faction);
            return new EconomyView(economy.Food, economy.Wood, population, rules.PopulationCap, economy.Queued, economy.TrainRemaining,
                !economy.AutoOff, rules.BarracksSizeCells, rules.BarracksWoodCost, rules.VillagerFoodCost, rules.InfantryFoodCost,
                rules.InfantryWoodCost, villagers, buildings, resources);
        }

        private static VillagerActivity Activity(VillagerTask task) => task switch
        {
            VillagerTask.ToNode => VillagerActivity.ToResource,
            VillagerTask.Gathering => VillagerActivity.Gathering,
            VillagerTask.ToDropOff => VillagerActivity.Returning,
            VillagerTask.ToBuild => VillagerActivity.ToBuild,
            VillagerTask.Building => VillagerActivity.Building,
            _ => VillagerActivity.Idle
        };
    }
}
