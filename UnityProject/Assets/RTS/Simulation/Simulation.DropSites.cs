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

        /// <summary>Early arms: at most this many own drop-offs, and none by a point with less than this left.</summary>
        private const int EarlyDropSiteLimit = 8, EarlyDropSiteMinRemaining = 80;

        /// <summary>Where <paramref name="v"/> unloads now, and how close it must come.</summary>
        private (SimPoint point, Fix64 reach) DropOff(VillagerState v)
        {
            var rules = world.Config.Economy;
            var point = OwnCore(v.FactionId).Definition.Position;
            var reach = world.Config.Rules.CoreRadius + rules.DropOffMargin;
            if (ProcessingOn && v.HaulTo > 0 && v.HaulTo <= world.BuildingCount)
            {
                var target = world.Buildings[v.HaulTo - 1];
                if (target.Alive && target.Complete && target.FactionId == v.FactionId && target.Kind == BuildingKind.CharcoalKiln)
                    return (world.Map.Center(target.WorkCell), GatherReach);
            }
            if (FishingAllowed(v.FactionId) && v.CarryKind == ResourceKind.Food && v.NodeId > 0 && v.NodeId <= world.Nodes.Length
                && world.Nodes[v.NodeId - 1].Fishing)
            {
                var fishDrop = point;
                var fishReach = reach;
                var fishBest = DistanceSquared(v.Position, point);
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var harbor = world.Buildings[i];
                    if (!harbor.Alive || !harbor.Complete || harbor.FactionId != v.FactionId || harbor.Kind != BuildingKind.Harbor) continue;
                    var spot = world.Map.Center(harbor.WorkCell);
                    var distance = DistanceSquared(v.Position, spot);
                    if (distance < fishBest) { fishBest = distance; fishDrop = spot; fishReach = rules.DropOffMargin; }
                }
                point = fishDrop; reach = fishReach;
            }
            if (TownsOn)
            {
                var townBest = DistanceSquared(v.Position, point);
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var town = world.Buildings[i];
                    if (!town.Alive || !town.Complete || town.FactionId != v.FactionId || town.Kind != BuildingKind.Town) continue;
                    var spot = world.Map.Center(town.WorkCell);
                    var distance = DistanceSquared(v.Position, spot);
                    if (distance < townBest)
                    {
                        townBest = distance;
                        point = spot;
                        reach = rules.DropOffMargin;
                    }
                }
            }
            if (!AgesOn) return (point, reach);
            BigInteger best = DistanceSquared(v.Position, point);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || !b.Complete || b.FactionId != v.FactionId
                    || (b.Kind != BuildingKind.DropSite && !(TownsOn && b.Kind == BuildingKind.Town))) continue;
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
            // Not while the civilisation's own line is still waiting for its wood. While saving to advance, only from the
            // wood beyond the advance price: the drop-off used to wait for the whole saving, and when food was what was
            // missing, villagers walked to far food with a thousand wood in store and the age never came (10-10).
            if (!AgesOn || (world.Economies[faction - 1].Civ != CivKind.Primitive && !CivLineStarted(faction))) return;
            var rules = world.Config.Economy;
            int reserved = SavingToAdvance(faction) ? AdvancePrice(faction, world.Economies[faction - 1]).wood : 0;
            if (world.Economies[faction - 1].Wood - reserved < rules.DropSiteWoodCost) return;
            int ownDropSites = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.FactionId != faction || b.Kind != BuildingKind.DropSite) continue;
                if (!b.Complete) return;
                ownDropSites++;
            }
            // Early arms: with gathering twice as fast the points empty quickly, and a drop-off for every next point
            // reached 21 in fifteen minutes and took the wood the houses and soldiers needed (10-10).
            if (EarlyArmsOn && ownDropSites >= EarlyDropSiteLimit) return;
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
                // Early arms: not by a point that is nearly gathered out.
                if (EarlyArmsOn && world.Nodes[n].Remaining < EarlyDropSiteMinRemaining) continue;
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
