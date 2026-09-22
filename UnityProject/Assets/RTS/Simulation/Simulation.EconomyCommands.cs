using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// Ver.3 direct economy operations (technical-design-v3 6), applied in the commands phase of the tick they arrive.
    /// Each one is checked against the true state - ownership, cost, room, a legal footprint - and a failed check changes
    /// nothing. The log holds the operation, not the outcome; replay reaches the same outcome from the same state.
    /// </summary>
    public sealed partial class Simulation
    {
        private void ApplyEconomyCommand(EconomyCommand c)
        {
            if (!EconomyOn) return;
            uint faction = c.FactionId;
            if (OwnCore(faction).Hp <= 0) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            switch (c.Kind)
            {
                case EconomyCommandKind.SetAutoEconomy:
                    economy.AutoOff = !c.Enabled;
                    return;
                case EconomyCommandKind.PlaceBuilding:
                {
                    if (c.Building != BuildingKind.Barracks || economy.Wood < rules.BarracksWoodCost) return;
                    int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, size = rules.BarracksSizeCells;
                    if (c.Cell < 0 || c.Cell >= width * height || c.Cell % width + size > width || c.Cell / width + size > height) return;
                    if (!SiteIsClear(c.Cell, world.Map.Cell(OwnCore(faction).Definition.Position)) || !KeepsMapConnected(faction, c.Cell)) return;
                    PlaceBarracksAt(faction, c.Cell);
                    return;
                }
                case EconomyCommandKind.Train:
                {
                    int population = LivingVillagers(faction) + LivingSoldiers(faction) + economy.Queued + QueuedInfantry(faction);
                    if (population >= rules.PopulationCap) return;
                    if (c.ProducerId == 0)
                    {
                        if (c.Unit != UnitKind.Villager || economy.Queued >= rules.QueueLimit || economy.Food < rules.VillagerFoodCost) return;
                        economy.Food = checked(economy.Food - rules.VillagerFoodCost);
                        if (economy.Queued == 0) economy.TrainRemaining = rules.VillagerTrainTicks;
                        economy.Queued++;
                        return;
                    }
                    if (!OwnBuilding(faction, c.ProducerId, out int index)) return;
                    ref var b = ref world.Buildings[index];
                    if (!b.Complete || c.Unit != UnitKind.Infantry || b.Queued >= rules.QueueLimit || !HasInfantryRoom(faction)
                        || economy.Food < rules.InfantryFoodCost || economy.Wood < rules.InfantryWoodCost) return;
                    economy.Food = checked(economy.Food - rules.InfantryFoodCost);
                    economy.Wood = checked(economy.Wood - rules.InfantryWoodCost);
                    if (b.Queued == 0) b.TrainRemaining = rules.InfantryTrainTicks;
                    b.Queued++;
                    return;
                }
                case EconomyCommandKind.CancelTrain:
                {
                    // The last one queued goes, and its cost comes back. Cancelling the only one also stops its clock.
                    if (c.ProducerId == 0)
                    {
                        if (economy.Queued == 0) return;
                        economy.Queued--;
                        economy.Food = checked(economy.Food + rules.VillagerFoodCost);
                        if (economy.Queued == 0) economy.TrainRemaining = 0;
                        return;
                    }
                    if (!OwnBuilding(faction, c.ProducerId, out int index)) return;
                    ref var b = ref world.Buildings[index];
                    if (b.Queued == 0) return;
                    b.Queued--;
                    economy.Food = checked(economy.Food + rules.InfantryFoodCost);
                    economy.Wood = checked(economy.Wood + rules.InfantryWoodCost);
                    if (b.Queued == 0) b.TrainRemaining = 0;
                    return;
                }
                case EconomyCommandKind.AssignVillagers:
                {
                    int nodeIndex = -1, buildingIndex = -1;
                    if (c.TargetKind == EconomyTargetKind.ResourceNode)
                    {
                        if (c.TargetId == 0 || c.TargetId > world.Nodes.Length || world.Nodes[c.TargetId - 1].Remaining <= 0) return;
                        nodeIndex = (int)c.TargetId - 1;
                    }
                    else if (c.TargetKind == EconomyTargetKind.Building)
                    {
                        if (!OwnBuilding(faction, c.TargetId, out buildingIndex) || world.Buildings[buildingIndex].Complete) return;
                    }
                    else return;
                    foreach (uint id in c.VillagerIds)
                    {
                        if (id == 0 || id > world.VillagerCount) continue;
                        ref var v = ref world.Villagers[id - 1];
                        if (!v.Alive || v.FactionId != faction) continue;
                        if (nodeIndex >= 0)
                        {
                            var kind = world.Nodes[nodeIndex].Definition.Kind;
                            v.NodeId = c.TargetId;
                            // A load of the other resource goes home first, so the load stays one kind.
                            v.Task = v.Carry > 0 && v.CarryKind != kind ? VillagerTask.ToDropOff : VillagerTask.ToNode;
                        }
                        else
                        {
                            v.Task = VillagerTask.ToBuild;
                            v.BuildingId = c.TargetId;
                        }
                    }
                    return;
                }
            }
        }

        private bool OwnBuilding(uint faction, uint id, out int index)
        {
            index = (int)id - 1;
            return id != 0 && id <= world.BuildingCount && world.Buildings[index].Alive && world.Buildings[index].FactionId == faction;
        }
    }
}
