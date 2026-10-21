using System;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        // This is an incremental cache: it is changed only when a footprint is placed, moved or removed.
        // It is deliberately outside WorldState because it is a derived LargeEconomy-only lookup.
        private int[] largeResourceNodeBlockers;

        private void InitializeLargeResourceNodeBlockers()
        {
            if (!LargeEconomyOn) return;
            largeResourceNodeBlockers = new int[world.Nodes.Length];
            for (int i = 0; i < world.BuildingCount; i++)
                AddLargeResourceNodeBlockers(world.Buildings[i], releaseVillagers: false);
        }

        private bool LargeResourceNodeBlocked(int nodeIndex)
            => LargeEconomyOn && largeResourceNodeBlockers != null
                && nodeIndex >= 0 && nodeIndex < largeResourceNodeBlockers.Length
                && largeResourceNodeBlockers[nodeIndex] > 0;

        private void AddLargeResourceNodeBlockers(BuildingState building, bool releaseVillagers = true)
        {
            if (!LargeEconomyOn || largeResourceNodeBlockers == null || !building.Alive) return;
            var footprint = Footprint(building);
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                if (Array.IndexOf(footprint, world.Map.Cell(world.Nodes[i].Definition.Position)) < 0) continue;
                largeResourceNodeBlockers[i] = checked(largeResourceNodeBlockers[i] + 1);
                if (releaseVillagers && largeResourceNodeBlockers[i] == 1) ReleaseLargeBlockedNode(i);
            }
        }

        private void RemoveLargeResourceNodeBlockers(BuildingState building)
        {
            if (!LargeEconomyOn || largeResourceNodeBlockers == null || !building.Alive) return;
            var footprint = Footprint(building);
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                if (Array.IndexOf(footprint, world.Map.Cell(world.Nodes[i].Definition.Position)) < 0) continue;
                if (largeResourceNodeBlockers[i] > 0) largeResourceNodeBlockers[i]--;
            }
        }

        private void ReleaseLargeBlockedNode(int nodeIndex)
        {
            uint nodeId = world.Nodes[nodeIndex].Definition.Id;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var villager = ref world.Villagers[i];
                if (!villager.Alive || villager.NodeId != nodeId
                    || (villager.Task != VillagerTask.ToNode && villager.Task != VillagerTask.Gathering)) continue;
                villager.NodeId = 0;
                villager.Route = Array.Empty<int>();
                villager.RouteCursor = 0;
                villager.RouteGoal = villager.Position;
                villager.Task = villager.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
            }
        }

        private void ReleaseVillagerFromBlockedLargeNode(ref VillagerState villager)
        {
            if (!LargeEconomyOn || villager.NodeId == 0 || villager.NodeId > world.Nodes.Length
                || !LargeResourceNodeBlocked((int)villager.NodeId - 1)
                || (villager.Task != VillagerTask.ToNode && villager.Task != VillagerTask.Gathering)) return;
            villager.NodeId = 0;
            villager.Route = Array.Empty<int>();
            villager.RouteCursor = 0;
            villager.RouteGoal = villager.Position;
            villager.Task = villager.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
        }
    }
}
