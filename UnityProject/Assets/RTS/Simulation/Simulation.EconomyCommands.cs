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
                case EconomyCommandKind.PlaceBelt:
                    PlaceBelts(faction, c, IndustryOn);
                    return;
                case EconomyCommandKind.ReturnEconomyToAuto:
                    ReturnToAuto(faction);
                    return;
                case EconomyCommandKind.AdvanceAge:
                    if (CanAdvance(faction, c.Civ)) StartAdvance(faction, c.Civ);
                    return;
                case EconomyCommandKind.SetEconomyPolicy:
                    if (IndustryOn && (byte)c.Policy <= 2) economy.Policy = c.Policy;
                    return;
                case EconomyCommandKind.Research:
                    if (OwnBuilding(faction, c.ProducerId, out int smith)) StartResearch(faction, ref world.Buildings[smith], c.Tech, true);
                    return;
                case EconomyCommandKind.PlaceWall:
                    PlaceWalls(faction, c);
                    return;
                case EconomyCommandKind.RemoveBelt:
                    RemoveBelt(faction, c.Cell);
                    return;
                case EconomyCommandKind.PlaceBuilding:
                {
                    var kind = c.Building;
                    if (kind != BuildingKind.Barracks && !(IndustryOn && MetalworkAllowed(faction) && (kind == BuildingKind.Mine || kind == BuildingKind.Smelter))
                        && !(kind == BuildingKind.Farm && FarmingAllowed(faction)) && !((kind == BuildingKind.House || kind == BuildingKind.DropSite || kind == BuildingKind.Tower) && AgesOn)
                        && !(kind == BuildingKind.Blacksmith && AgesOn && world.Economies[faction - 1].Civ != CivKind.Primitive)) return;
                    if ((byte)c.Facing > 3 || economy.Wood < WoodOf(kind) || economy.Stone < StoneOf(kind)) return;
                    int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, size = SizeOf(kind);
                    if (c.Cell < 0 || c.Cell >= width * height || c.Cell % width + size > width || c.Cell / width + size > height) return;
                    uint node = 0;
                    bool clear = kind == BuildingKind.Mine ? MineSiteIsClear(c.Cell, out node)
                        : SiteIsClear(c.Cell, world.Map.Cell(OwnCore(faction).Definition.Position), size);
                    if (!clear || !KeepsMapConnected(faction, c.Cell, size)) return;
                    PlaceBuildingAt(faction, kind, c.Cell, kind == BuildingKind.Barracks ? Facing.North : c.Facing, node);
                    world.Buildings[world.BuildingCount - 1].Held = IndustryOn; // V3-3: the player's building
                    return;
                }
                case EconomyCommandKind.Train:
                {
                    int population = LivingVillagers(faction) + LivingSoldiers(faction) + economy.Queued + QueuedInfantry(faction);
                    if (population >= PopCapFor(faction)) return;
                    if (c.ProducerId == 0)
                    {
                        if (c.Unit != UnitKind.Villager || economy.Queued >= rules.QueueLimit || economy.Food < rules.VillagerFoodCost || economy.AdvanceRemaining > 0) return;
                        if (IndustryOn) economy.CoreHeld = true;
                        economy.Food = checked(economy.Food - rules.VillagerFoodCost);
                        if (economy.Queued == 0) economy.TrainRemaining = rules.VillagerTrainTicks;
                        economy.Queued++;
                        return;
                    }
                    if (!OwnBuilding(faction, c.ProducerId, out int index)) return;
                    ref var b = ref world.Buildings[index];
                    if (!b.Complete || !Trains(b, c.Unit) || b.Queued >= rules.QueueLimit || !HasRoomFor(faction, c.Unit) || !CanPay(faction, c.Unit)) return;
                    if (IndustryOn) b.Held = true;
                    Enqueue(faction, ref b, c.Unit);
                    return;
                }
                case EconomyCommandKind.CancelTrain:
                {
                    // The last one queued goes, and its cost comes back. Cancelling the only one also stops its clock.
                    if (c.ProducerId == 0)
                    {
                        if (economy.Queued == 0) return;
                        if (IndustryOn) economy.CoreHeld = true;
                        economy.Queued--;
                        economy.Food = checked(economy.Food + rules.VillagerFoodCost);
                        if (economy.Queued == 0) economy.TrainRemaining = 0;
                        return;
                    }
                    if (!OwnBuilding(faction, c.ProducerId, out int index)) return;
                    ref var b = ref world.Buildings[index];
                    if (b.Queued == 0) return;
                    if (IndustryOn) b.Held = true;
                    // Today's price back; advancing only ever lowers food and wood, and metal comes back only as paid.
                    CancelLast(faction, ref b);
                    return;
                }
                case EconomyCommandKind.AssignVillagers:
                {
                    int nodeIndex = -1, buildingIndex = -1;
                    bool haul = false;
                    if (c.TargetKind == EconomyTargetKind.ResourceNode)
                    {
                        if (c.TargetId == 0 || c.TargetId > world.Nodes.Length || world.Nodes[c.TargetId - 1].Remaining <= 0) return;
                        // A point under a footprint (a mine's) cannot be walked to.
                        if (!world.Map.IsPassable(world.Map.Cell(world.Nodes[c.TargetId - 1].Definition.Position))) return;
                        nodeIndex = (int)c.TargetId - 1;
                    }
                    else if (c.TargetKind == EconomyTargetKind.Building)
                    {
                        if (!OwnBuilding(faction, c.TargetId, out buildingIndex)) return;
                        var target = world.Buildings[buildingIndex];
                        // V3-2: a finished mine or smelter is a place to carry from by hand (12.3).
                        haul = target.Complete && (target.Kind == BuildingKind.Mine || target.Kind == BuildingKind.Smelter || target.Kind == BuildingKind.Farm);
                        if (target.Complete && !haul) return;
                    }
                    else return;
                    foreach (uint id in c.VillagerIds)
                    {
                        if (id == 0 || id > world.VillagerCount) continue;
                        ref var v = ref world.Villagers[id - 1];
                        if (!v.Alive || v.FactionId != faction) continue;
                        v.HaulFrom = 0; v.HaulTo = 0;
                        if (IndustryOn) v.Held = true;
                        if (haul)
                        {
                            v.NodeId = 0;
                            v.HaulFrom = c.TargetId;
                            v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.ToPickup;
                        }
                        else if (nodeIndex >= 0)
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

        /// <summary>V3-3 (19): every hold of the faction ends; each villager goes on with its task until it is idle again.</summary>
        private void ReturnToAuto(uint faction)
        {
            if (!IndustryOn) return;
            world.Economies[faction - 1].CoreHeld = false;
            for (int i = 0; i < world.VillagerCount; i++) if (world.Villagers[i].FactionId == faction) world.Villagers[i].Held = false;
            for (int i = 0; i < world.BuildingCount; i++) if (world.Buildings[i].FactionId == faction) world.Buildings[i].Held = false;
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) world.Belts[i].Held = false;
        }

        private bool OwnBuilding(uint faction, uint id, out int index)
        {
            index = (int)id - 1;
            return id != 0 && id <= world.BuildingCount && world.Buildings[index].Alive && world.Buildings[index].FactionId == faction;
        }
    }
}
