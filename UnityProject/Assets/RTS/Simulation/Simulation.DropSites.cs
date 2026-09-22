using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 drop-offs (technical-design-v3 32 #3). A villager unloads at whichever is nearer: its core, or an own finished
    /// drop-off (the core wins a tie, then the lower id). Without ages only the core takes loads, as before.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>The automatic economy places a drop-off by a point this far from the core (m) with this many on it.</summary>
        private const int FarGatherMeters = 24, DropSiteGatherers = 2, DropSiteCoverMeters = 14;

        /// <summary>Where <paramref name="v"/> unloads now, and how close it must come.</summary>
        private (SimPoint point, Fix64 reach) DropOff(VillagerState v)
        {
            var rules = world.Config.Economy;
            var point = OwnCore(v.FactionId).Definition.Position;
            var reach = world.Config.Rules.CoreRadius + rules.DropOffMargin;
            if (!AgesOn) return (point, reach);
            BigInteger best = DistanceSquared(v.Position, point);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || !b.Complete || b.FactionId != v.FactionId || b.Kind != BuildingKind.DropSite) continue;
                var spot = world.Map.Center(b.WorkCell);
                var d = DistanceSquared(v.Position, spot);
                if (d >= best) continue;
                best = d;
                point = spot;
                reach = rules.DropOffMargin;
            }
            return (point, reach);
        }

        /// <summary>
        /// AI phase: one drop-off at a time, by the resource point with the most own gatherers (then the lower id) that
        /// lies FarGatherMeters or more from the core and has no own drop-off within DropSiteCoverMeters.
        /// </summary>
        private void DecideDropSite(uint faction)
        {
            if (!AgesOn || SavingToAdvance(faction)) return;
            var rules = world.Config.Economy;
            if (world.Economies[faction - 1].Wood < rules.DropSiteWoodCost) return;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && !b.Complete && b.FactionId == faction && b.Kind == BuildingKind.DropSite) return;
            }
            var core = OwnCore(faction).Definition.Position;
            var gatherers = new int[world.Nodes.Length];
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && v.NodeId != 0 && (v.Task == VillagerTask.ToNode || v.Task == VillagerTask.Gathering || v.Task == VillagerTask.ToDropOff))
                    gatherers[v.NodeId - 1]++;
            }
            int bestNode = -1;
            for (int n = 0; n < world.Nodes.Length; n++)
            {
                if (gatherers[n] < DropSiteGatherers || (bestNode >= 0 && gatherers[n] <= gatherers[bestNode])) continue;
                var at = world.Nodes[n].Definition.Position;
                if (InRange(at, core, Fix64.FromInt(FarGatherMeters))) continue;
                bool covered = false;
                for (int i = 0; i < world.BuildingCount && !covered; i++)
                {
                    var b = world.Buildings[i];
                    covered = b.Alive && b.FactionId == faction && b.Kind == BuildingKind.DropSite
                        && InRange(world.Map.Center(b.WorkCell), at, Fix64.FromInt(DropSiteCoverMeters));
                }
                if (!covered) bestNode = n;
            }
            if (bestNode < 0) return;
            int origin = FindSiteNear(faction, rules.DropSiteSizeCells, world.Map.Cell(world.Nodes[bestNode].Definition.Position));
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.DropSite, origin, Facing.North, 0);
        }
    }
}
