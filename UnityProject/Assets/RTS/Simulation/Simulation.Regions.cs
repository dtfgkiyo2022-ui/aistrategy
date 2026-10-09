using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private bool RegionsOn => world.Config.Economy.Regions;

        private void InitializeRegions()
        {
            if (!RegionsOn) return;
            var regions = new List<RegionState>();
            for (uint id = 1; id <= world.Cores.Length; id++)
            {
                var core = world.Cores[id - 1].Definition;
                regions.Add(new RegionState { Id = (uint)regions.Count + 1, CenterId = core.Id,
                    CenterKind = RegionCenterKind.Core, Center = core.Position, HasCenter = true });
            }
            for (uint id = 1; id <= world.Outposts.Length; id++)
            {
                var post = world.Outposts[id - 1].Definition;
                regions.Add(new RegionState { Id = (uint)regions.Count + 1, CenterId = post.Id,
                    CenterKind = RegionCenterKind.Outpost, Center = post.Position, HasCenter = true });
            }
            world.Regions = regions.ToArray();
            world.CellRegions = new uint[world.Config.Map.WidthCells * world.Config.Map.HeightCells];
            ResizeRegionFactionState();
            RebuildRegionCells();
        }

        private void ResizeRegionFactionState()
        {
            if (!RegionsOn) return;
            for (int i = 0; i < world.Factions.Length; i++)
            {
                var faction = world.Factions[i];
                var human = new bool[world.Regions.Length];
                var policies = new EconomyPolicy[world.Regions.Length];
                if (faction.RegionHuman != null) Array.Copy(faction.RegionHuman, human, Math.Min(faction.RegionHuman.Length, human.Length));
                if (faction.RegionEconomyPolicies != null) Array.Copy(faction.RegionEconomyPolicies, policies, Math.Min(faction.RegionEconomyPolicies.Length, policies.Length));
                faction.RegionHuman = human;
                faction.RegionEconomyPolicies = policies;
                world.Factions[i] = faction;
            }
        }

        private void RebuildRegionCells()
        {
            if (!RegionsOn) return;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            {
                int cell = z * width + x;
                var point = world.Map.Center(cell);
                uint best = 0;
                long bestDistance = 0;
                for (int i = 0; i < world.Regions.Length; i++)
                {
                    var region = world.Regions[i];
                    if (!region.HasCenter) continue;
                    long dx = point.X.Raw - region.Center.X.Raw;
                    long dz = point.Z.Raw - region.Center.Z.Raw;
                    long distance = checked(dx * dx + dz * dz);
                    if (best == 0 || distance < bestDistance || distance == bestDistance && region.Id < best)
                    { best = region.Id; bestDistance = distance; }
                }
                world.CellRegions[cell] = best;
            }
        }

        private void AddTownRegion(BuildingState town)
        {
            if (!RegionsOn || town.Kind != BuildingKind.Town) return;
            for (int i = 0; i < world.Regions.Length; i++)
                if (world.Regions[i].CenterKind == RegionCenterKind.Town && world.Regions[i].CenterId == town.Id) return;
            var regions = new RegionState[world.Regions.Length + 1];
            Array.Copy(world.Regions, regions, world.Regions.Length);
            regions[regions.Length - 1] = new RegionState { Id = (uint)regions.Length, CenterId = town.Id,
                CenterKind = RegionCenterKind.Town, Center = BuildingCenter(town), HasCenter = true };
            world.Regions = regions;
            ResizeRegionFactionState();
            RebuildRegionCells();
        }

        private void RemoveRegionCenter(BuildingState building)
        {
            if (!RegionsOn || building.Kind != BuildingKind.Town) return;
            for (int i = 0; i < world.Regions.Length; i++)
                if (world.Regions[i].CenterKind == RegionCenterKind.Town && world.Regions[i].CenterId == building.Id)
                {
                    var region = world.Regions[i];
                    region.HasCenter = false;
                    world.Regions[i] = region;
                    return; // The cell table deliberately remains unchanged after a centre disappears.
                }
        }

        private uint RegionForCell(int cell)
            => RegionsOn && cell >= 0 && cell < world.CellRegions.Length ? world.CellRegions[cell] : 0;

        private uint RegionForPoint(SimPoint point) => RegionForCell(world.Map.Cell(point));

        private uint RegionForBuilding(BuildingState building) => RegionForPoint(BuildingCenter(building));

        private uint RegionForArmy(ArmyState army)
        {
            foreach (uint id in army.SoldierIds)
                if (world.Soldiers[id - 1].Alive) return RegionForPoint(world.Soldiers[id - 1].Position);
            var home = army.Definition.HomeObjective;
            if (home.Kind == GoalKind.None)
                return RegionForPoint(world.Cores[world.Factions[army.Definition.FactionId - 1].CoreId - 1].Definition.Position);
            return RegionForPoint(GoalPosition(home));
        }

        private bool HumanRegion(uint faction, uint regionId)
            => RegionsOn && regionId > 0 && regionId <= world.Regions.Length && world.Factions[faction - 1].RegionHuman[regionId - 1];

        private bool HumanRegionAt(uint faction, SimPoint point) => HumanRegion(faction, RegionForPoint(point));

        private bool RegionAllowsAiBuilding(uint faction, int origin, int size)
            => !HumanRegionAt(faction, FootprintCenter(origin, size));

        private bool RegionBlocksAiArmy(ArmyState army)
        {
            if (!RegionsOn) return false;
            if (!HumanRegion(army.Definition.FactionId, RegionForArmy(army))) return false;
            return !commandStates.Any(c => c.Status == CommandStatus.Executing && c.Order.Source == CommandSource.Human
                && Combat(c.Order) && c.Armies.Any(e => e.ArmyId == army.Definition.Id && e.Active && !e.Finished));
        }

        private uint[] RegionArmyIds(ScopeKey scope)
        {
            if (!RegionsOn || scope.Kind != ScopeKind.Region || !ValidRegion(scope.Id)) return Array.Empty<uint>();
            return world.Armies.Where(a => a.Definition.FactionId == scope.FactionId && RegionForArmy(a) == scope.Id)
                .Select(a => a.Definition.Id).OrderBy(id => id).ToArray();
        }

        private PolicyOrder[] PolicyOrdersForDecision(uint faction, IReadOnlyList<CommandState> active)
        {
            var result = new List<PolicyOrder>();
            foreach (var command in active)
            {
                var order = command.Order;
                if (order.Target.Kind != ScopeKind.Region)
                {
                    result.Add(order);
                    continue;
                }
                foreach (var post in world.Outposts.OrderBy(o => o.Definition.Id))
                    if (RegionForPoint(post.Definition.Position) == order.Target.Id)
                        result.Add(new PolicyOrder(order.CommandId, order.BatchId, order.Source,
                            new ScopeKey(faction, ScopeKind.Outpost, post.Definition.Id), order.Kind, order.Goal,
                            order.Priority, order.AllowedLoss, order.End, order.ReservePermille, order.TargetRevision,
                            order.Parents, order.ObservedTick, order.Expiration));
            }
            return result.ToArray();
        }

        private bool ValidRegion(uint id) => RegionsOn && id > 0 && id <= world.Regions.Length && world.Regions[id - 1].Id == id;

        private void SetRegionControl(uint faction, uint regionId, RegionControl control)
        {
            if (!RegionsOn || !ValidRegion(regionId)) return;
            var state = world.Factions[faction - 1];
            state.RegionHuman[regionId - 1] = control == RegionControl.Human;
            world.Factions[faction - 1] = state;
            if (control == RegionControl.Ai) ReturnRegionToAuto(faction, regionId);
        }

        private void SetRegionEconomyPolicy(uint faction, uint regionId, EconomyPolicy policy)
        {
            if (!RegionsOn || !ValidRegion(regionId) || (byte)policy > 2) return;
            world.Factions[faction - 1].RegionEconomyPolicies[regionId - 1] = policy;
        }

        private void ReturnRegionToAuto(uint faction, uint regionId)
        {
            if (!IndustryOn && !RegionsOn) return;
            for (int i = 0; i < world.VillagerCount; i++)
                if (world.Villagers[i].FactionId == faction && RegionForPoint(world.Villagers[i].Position) == regionId) world.Villagers[i].Held = false;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].FactionId == faction && RegionForBuilding(world.Buildings[i]) == regionId) world.Buildings[i].Held = false;
            for (int i = 0; i < world.Belts.Length; i++)
                if (world.Belts[i].FactionId == faction && RegionForCell(i) == regionId) world.Belts[i].Held = false;
            var core = world.Factions[faction - 1].CoreId;
            if (RegionForPoint(world.Cores[core - 1].Definition.Position) == regionId) world.Economies[faction - 1].CoreHeld = false;
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction) continue;
                uint building = line.MineId != 0 ? line.MineId : line.SmelterId != 0 ? line.SmelterId
                    : line.KilnId != 0 ? line.KilnId : line.SteelworksId != 0 ? line.SteelworksId
                    : line.LumberCampId != 0 ? line.LumberCampId : line.FletcherId;
                if (building != 0 && building <= world.BuildingCount && RegionForBuilding(world.Buildings[building - 1]) == regionId)
                { line.Manager = LineManager.Automatic; world.ProcessingLines[i] = line; }
            }
        }

        private RegionView[] RegionViewsFor(uint faction)
        {
            if (!RegionsOn) return Array.Empty<RegionView>();
            var result = new RegionView[world.Regions.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var region = world.Regions[i];
                var military = commandStates.Where(c => c.Status == CommandStatus.Executing && c.Order.Target.FactionId == faction
                    && c.Order.Target.Kind == ScopeKind.Region && c.Order.Target.Id == region.Id && Combat(c.Order))
                    .OrderBy(c => SourcePriority(c.Order.Source)).ThenByDescending(c => c.LogIndex).FirstOrDefault();
                result[i] = new RegionView(region.Id, region.HasCenter ? region.CenterKind : RegionCenterKind.None,
                    region.HasCenter ? region.CenterId : 0, region.Center, world.Factions[faction - 1].RegionHuman[i] ? RegionControl.Human : RegionControl.Ai,
                    military == null ? (PolicyKind)0 : military.Order.Kind, world.Factions[faction - 1].RegionEconomyPolicies[i]);
            }
            return result;
        }

        private uint[] CellRegionsForFrame() => RegionsOn ? (uint[])world.CellRegions.Clone() : Array.Empty<uint>();

        private bool CanAiAssign(VillagerState villager) => !HumanRegionAt(villager.FactionId, villager.Position);
    }
}
