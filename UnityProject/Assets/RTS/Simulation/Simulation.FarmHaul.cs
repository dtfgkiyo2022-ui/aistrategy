using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private const int FarmHaulFarm = 0, FarmHaulMine = 1, FarmHaulLumberCamp = 2, FarmHaulOther = 3,
            FarmHaulCategoryCount = 4;
        private readonly long[,] farmHaulObservedTicks = new long[2, FarmHaulCategoryCount];
        private readonly long[,] farmHaulWaitingTicks = new long[2, FarmHaulCategoryCount];

        private bool FarmHaulIdleOn => EconomyOn && world.Config.Economy.FarmHaulIdle;

        private const int FarmGatherRouteTries = 4;

        /// <summary>The nearest known food or wood node the hauler can actually walk to (cached route check,
        /// at most a few candidates). Without this a hauler kept picking an unreachable nearest node forever.</summary>
        private int NearestFarmGatherNode(BuildingState farm, VillagerState villager)
        {
            uint faction = villager.FactionId;
            SimPoint origin = world.Map.Center(farm.WorkCell);
            int e0 = -1, e1 = -1, e2 = -1;
            for (int attempt = 0; attempt < FarmGatherRouteTries; attempt++)
            {
                int node = NearestFarmGatherNodeExcept(origin, faction, e0, e1, e2);
                if (node < 0) return -1;
                if (FarmGatherNodeReachable(villager, node)) return node;
                if (attempt == 0) e0 = node; else if (attempt == 1) e1 = node; else e2 = node;
            }
            return -1;
        }

        /// <summary>The same test AiExpandWorkRouteTimedOut uses to give a node up, so a hauler never picks a node
        /// that the timeout would drop again (which made it pick, walk, give up and pick the same node forever).</summary>
        private bool FarmGatherNodeReachable(VillagerState villager, int nodeIndex)
        {
            if (!AiExpandOn) return true;
            var node = world.Nodes[nodeIndex];
            var probe = new VillagerState { FactionId = villager.FactionId, Position = node.Definition.Position,
                CarryKind = node.Definition.Kind, Task = VillagerTask.ToDropOff };
            var drop = DropOff(probe).point;
            // NearestWorkNode also needs the way from the node to its drop-off; without it the hauler kept a node
            // the AI itself never assigns (AiExpandTests.UnreachableLargeWorkPointIsNotKeptAsTheWoodAssignment).
            if (!GatherSpreadOn)
                return AiExpandWorkRouteAvailable(villager.Position, node.Definition.Position, villager.FactionId, node.Definition.Kind)
                    && AiExpandWorkRouteAvailable(node.Definition.Position, drop, villager.FactionId, node.Definition.Kind);
            var core = world.Cores[world.Factions[villager.FactionId - 1].CoreId - 1].Definition.Position;
            return AiExpandWorkRouteAvailable(core, node.Definition.Position, villager.FactionId, node.Definition.Kind)
                && AiExpandWorkRouteAvailableFromActualOrigin(villager.Position, node.Definition.Position, villager.FactionId, node.Definition.Kind)
                && AiExpandWorkRouteAvailable(node.Definition.Position, drop, villager.FactionId, node.Definition.Kind);
        }

        private int NearestFarmGatherNodeExcept(SimPoint origin, uint faction, int e0, int e1, int e2)
        {
            int best = -1;
            long bestDistance = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (i == e0 || i == e1 || i == e2) continue;
                if ((node.Definition.Kind != ResourceKind.Food && node.Definition.Kind != ResourceKind.Wood)
                    || node.Remaining <= 0 || !ResourceKnownToFaction(faction, i)) continue;
                long dx = node.Definition.Position.X.Raw - origin.X.Raw;
                long dz = node.Definition.Position.Z.Raw - origin.Z.Raw;
                long distance = checked(dx * dx + dz * dz);
                if (best < 0 || distance < bestDistance
                    || distance == bestDistance && node.Definition.Id < world.Nodes[best].Definition.Id)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private bool TryStartFarmHaulGathering(ref VillagerState villager, BuildingState farm)
        {
            if (!FarmHaulIdleOn || farm.Kind != BuildingKind.Farm || villager.Held
                || villager.Carry != 0 || farm.Output >= CarryFor(villager.FactionId)) return false;
            int node = NearestFarmGatherNode(farm, villager);
            if (node < 0) return false;
            villager.HaulTo = 0;
            villager.HaulNodeId = 0;
            villager.HaulKind = 0;
            SetWorkNode(ref villager, node);
            return true;
        }

        private bool ContinueFarmHaulAfterDropOff(ref VillagerState villager)
        {
            if (!FarmHaulIdleOn || villager.Held || villager.HaulFrom == 0 || villager.HaulFrom > world.BuildingCount)
                return false;
            var farm = world.Buildings[villager.HaulFrom - 1];
            if (!IsActualBuilding(farm) || !farm.Complete || farm.Kind != BuildingKind.Farm) return false;
            if (farm.Output >= CarryFor(villager.FactionId))
            {
                villager.NodeId = 0;
                ReleaseVillagerRoute(ref villager);
                villager.RouteCursor = 0;
                villager.RouteGoal = villager.Position;
                villager.Task = VillagerTask.ToPickup;
                return true;
            }
            int node = NearestFarmGatherNode(farm, villager);
            if (node >= 0)
            {
                villager.HaulTo = 0;
                villager.HaulNodeId = 0;
                villager.HaulKind = 0;
                SetWorkNode(ref villager, node);
                return true;
            }
            // No food or wood point remains: keep the old farm-hauler wait at the outlet.
            villager.NodeId = 0;
            ReleaseVillagerRoute(ref villager);
            villager.RouteCursor = 0;
            villager.RouteGoal = villager.Position;
            villager.Task = VillagerTask.ToPickup;
            return true;
        }

        private static int FarmHaulCategory(BuildingKind kind)
            => kind == BuildingKind.Farm ? FarmHaulFarm
                : kind == BuildingKind.Mine ? FarmHaulMine
                : kind == BuildingKind.LumberCamp ? FarmHaulLumberCamp : FarmHaulOther;

        private void RecordHaulObservation(VillagerState villager)
        {
            if (!EconomyOn || villager.FactionId < 1 || villager.FactionId > 2 || villager.HaulFrom == 0
                || villager.HaulFrom > world.BuildingCount || villager.Task != VillagerTask.ToPickup) return;
            var building = world.Buildings[villager.HaulFrom - 1];
            if (!building.Alive) return;
            int category = FarmHaulCategory(building.Kind), faction = (int)villager.FactionId - 1;
            farmHaulObservedTicks[faction, category]++;
            if (!villager.IsMoving) farmHaulWaitingTicks[faction, category]++;
        }

        internal long FarmHaulObservedTicks(uint faction, int category)
            => faction >= 1 && faction <= 2 && category >= 0 && category < FarmHaulCategoryCount
                ? farmHaulObservedTicks[faction - 1, category] : 0;

        internal long FarmHaulWaitingTicks(uint faction, int category)
            => faction >= 1 && faction <= 2 && category >= 0 && category < FarmHaulCategoryCount
                ? farmHaulWaitingTicks[faction - 1, category] : 0;

        internal int FarmOutletFood(uint faction)
        {
            if (faction < 1 || faction > 2) return 0;
            int total = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (building.Alive && building.Complete && building.FactionId == faction && building.Kind == BuildingKind.Farm)
                    total += building.Output;
            }
            return total;
        }

        internal int ConnectedFarmCount(uint faction)
            => faction >= 1 && faction <= 2 ? largeConnectedFarms[faction - 1] : 0;

        internal int FarmHaulCategoryCountValue => FarmHaulCategoryCount;
    }
}
