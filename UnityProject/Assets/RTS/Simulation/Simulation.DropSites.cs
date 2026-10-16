using System;
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
        private long storageConnectivityTick = long.MinValue;
        private bool[] storageConnectivity;
        // Per storage and resource kind, worked out on first use in a tick: 0 unknown, 1 feeds a building, 2 does not.
        private byte[][] storageProcessingConnectivity;

        private int AliveDropSites(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && b.Kind == BuildingKind.DropSite) count++;
            }
            return count;
        }

        /// <summary>Where <paramref name="v"/> unloads now, and how close it must come.</summary>
        private (SimPoint point, Fix64 reach, uint storageId) DropOff(VillagerState v)
        {
            var rules = world.Config.Economy;
            var point = OwnCore(v.FactionId).Definition.Position;
            var reach = world.Config.Rules.CoreRadius + rules.DropOffMargin;
            if (ProcessingOn && v.HaulTo > 0 && v.HaulTo <= world.BuildingCount)
            {
                var target = world.Buildings[v.HaulTo - 1];
                if (target.Alive && target.Complete && target.FactionId == v.FactionId && target.Kind == BuildingKind.CharcoalKiln)
                    return (world.Map.Center(target.WorkCell), GatherReach, 0);
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
            if (!AgesOn) return (point, reach, 0);
            BigInteger best = DistanceSquared(v.Position, point);
            uint storageId = 0;
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
            if (AiBeltsOn && IsRawResource(v.CarryKind))
            {
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var storage = world.Buildings[i];
                    if (!storage.Alive || !storage.Complete || storage.FactionId != v.FactionId || storage.Kind != BuildingKind.Storage
                        || !StorageCanAccept(storage, v.CarryKind) || !StorageFeedsResource(storage, v.FactionId, v.CarryKind)) continue;
                    var spot = world.Map.Center(storage.WorkCell);
                    var distance = DistanceSquared(v.Position, spot);
                    if (distance >= best) continue;
                    best = distance;
                    point = spot;
                    reach = GatherReach;
                    storageId = storage.Id;
                }
            }
            return (point, reach, storageId);
        }

        private static bool IsRawResource(ResourceKind kind)
            => kind == ResourceKind.Food || kind == ResourceKind.Wood || kind == ResourceKind.Stone
                || kind == ResourceKind.Gold || kind == ResourceKind.Ore;

        /// <summary>Returns true only when the storage output already has a directed belt path into this faction's core.</summary>
        private bool StorageFeedsCore(BuildingState storage, uint faction)
        {
            if (!storage.Alive || !storage.Complete || storage.FactionId != faction || storage.Kind != BuildingKind.Storage) return false;
            RefreshStorageConnectivity();
            return storage.Id < storageConnectivity.Length && storageConnectivity[storage.Id];
        }

        private bool StorageFeedsResource(BuildingState storage, uint faction, ResourceKind item)
        {
            if (StorageFeedsCore(storage, faction)) return true;
            if (!StorageFeedsProcessingOn || (byte)item >= 29) return false;
            RefreshStorageConnectivity();
            if (storage.Id >= storageProcessingConnectivity.Length || !IsRawResource(item)) return false;
            var byResource = storageProcessingConnectivity[storage.Id];
            if (byResource == null) return false;
            if (byResource[(byte)item] == 0)
                byResource[(byte)item] = StorageHasBeltPathToBuilding(storage, faction, item) ? (byte)1 : (byte)2;
            return byResource[(byte)item] == 1;
        }

        private void RefreshStorageConnectivity()
        {
            if (storageConnectivityTick == world.Tick) return;
            storageConnectivityTick = world.Tick;
            storageConnectivity = new bool[Math.Max(1, world.NextBuildingId)];
            storageProcessingConnectivity = StorageFeedsProcessingOn
                ? new byte[Math.Max(1, world.NextBuildingId)][]
                : null;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var storage = world.Buildings[i];
                if (storage.Alive && storage.Complete && storage.Kind == BuildingKind.Storage)
                {
                    storageConnectivity[storage.Id] = StorageHasBeltPathToCore(storage, storage.FactionId);
                    if (StorageFeedsProcessingOn) storageProcessingConnectivity[storage.Id] = new byte[29];
                }
            }
        }

        private bool StorageHasBeltPathToCore(BuildingState storage, uint faction)
        {
            int start = OutputCell(storage);
            if (start < 0 || start >= world.Belts.Length || world.Belts[start].FactionId != faction) return false;
            var seen = new bool[world.Belts.Length];
            var queue = new int[world.Belts.Length];
            int head = 0, tail = 0;
            seen[start] = true;
            queue[tail++] = start;
            while (head < tail)
            {
                int cell = queue[head++];
                ref var belt = ref world.Belts[cell];
                if (Visit(BeltOutputCell(cell, ref belt))) return true;
                if (belt.Component == BeltComponentKind.UndergroundEntrance && Visit(belt.PairCell)) return true;
                if (belt.Component == BeltComponentKind.Splitter && Visit(BeltNext(cell, TurnLeft(belt.Facing)))) return true;
            }
            return false;

            bool Visit(int next)
            {
                if (next < 0 || next >= world.Belts.Length) return false;
                if (FeedsOwnCore(next, faction) && world.Belts[next].FactionId == 0) return true;
                if (!seen[next] && world.Belts[next].FactionId == faction)
                {
                    seen[next] = true;
                    queue[tail++] = next;
                }
                return false;
            }
        }

        private bool StorageHasBeltPathToBuilding(BuildingState storage, uint faction, ResourceKind item)
        {
            int start = OutputCell(storage);
            if (start < 0 || start >= world.Belts.Length || world.Belts[start].FactionId != faction) return false;
            var seen = new bool[world.Belts.Length];
            var queue = new int[world.Belts.Length];
            int head = 0, tail = 0;
            seen[start] = true;
            queue[tail++] = start;
            while (head < tail)
            {
                int cell = queue[head++];
                ref var belt = ref world.Belts[cell];
                if (Visit(BeltOutputCellForItem(cell, ref belt, item))) return true;
                if (belt.Component == BeltComponentKind.Splitter)
                {
                    if (Visit(BeltNext(cell, belt.Facing))) return true;
                    if (Visit(BeltNext(cell, TurnLeft(belt.Facing)))) return true;
                }
            }
            return false;

            bool Visit(int next)
            {
                if (next < 0 || next >= world.Belts.Length) return false;
                if (world.Belts[next].FactionId == 0 && BeltCanFeedBuilding(next, faction, item)) return true;
                if (!seen[next] && world.Belts[next].FactionId == faction)
                {
                    seen[next] = true;
                    queue[tail++] = next;
                }
                return false;
            }
        }

        private int BeltOutputCellForItem(int cell, ref BeltState belt, ResourceKind item)
        {
            if (!world.Config.Economy.BeltComponents || belt.Component == BeltComponentKind.None)
                return BeltNext(cell, belt.Facing);
            if (belt.Component == BeltComponentKind.UndergroundEntrance) return belt.PairCell;
            var direction = belt.Facing;
            if (belt.Component == BeltComponentKind.Sorter && item == belt.SorterKind)
                direction = TurnLeft(belt.Facing);
            return BeltNext(cell, direction);
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
            if (DropSiteLimitOn && ownDropSites >= rules.DropSiteMax)
            {
                DecideAiRawBelts(faction);
                return;
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
                if (!ResourceKnownToFaction(faction, n)) continue;
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
            if (DropSiteLimitOn && AliveDropSites(faction) >= rules.DropSiteMax) return;
            int origin = FindSiteNear(faction, rules.DropSiteSizeCells, world.Map.Cell(world.Nodes[bestNode].Definition.Position));
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.DropSite, origin, Facing.North, 0);
        }

        /// <summary>
        /// With the drop-site cap reached, give a distant raw point a receiver and route its output to the core.
        /// Existing raw-resource receivers are preferred where a civilisation has them; otherwise a storage is used.
        /// </summary>
        private void DecideAiRawBelts(uint faction)
        {
            if (!AiBeltsOn || !DropSiteLimitOn) return;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.AutoOff || economy.Wood < world.Config.Economy.AiBeltWoodReserve) return;
            var core = OwnCore(faction).Definition.Position;
            var gatherers = new int[world.Nodes.Length];
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && v.NodeId > 0 && v.NodeId <= world.Nodes.Length
                    && (v.Task == VillagerTask.ToNode || v.Task == VillagerTask.Gathering || v.Task == VillagerTask.ToDropOff))
                    gatherers[v.NodeId - 1]++;
            }
            int best = -1;
            for (int n = 0; n < world.Nodes.Length; n++)
            {
                var node = world.Nodes[n];
                if (node.Remaining <= 0 || gatherers[n] == 0 && !CommonDrillOn || InRange(node.Definition.Position, core, Fix64.FromInt(FarGatherMeters))) continue;
                if (!RawReceiverAllowed(faction, node.Definition.Kind)) continue;
                if (best >= 0 && (gatherers[n] < gatherers[best]
                    || (gatherers[n] == gatherers[best] && node.Definition.Id >= world.Nodes[best].Definition.Id))) continue;
                best = n;
            }
            if (best < 0) return;
            var kind = RawReceiverKind(faction, world.Nodes[best].Definition.Kind);
            uint receiver = FindRawReceiver(faction, kind, world.Nodes[best].Definition.Id);
            if (receiver == 0)
            {
                if (economy.Wood < RawReceiverWoodCost(kind) + world.Config.Economy.AiBeltWoodReserve) return;
                int preferred = world.Map.Cell(world.Nodes[best].Definition.Position);
                receiver = kind == BuildingKind.Drill ? PlaceDrill(faction, preferred)
                    : kind == BuildingKind.LumberCamp ? PlaceLumberCamp(faction, preferred)
                    : kind == BuildingKind.Quarry ? PlaceQuarry(faction, preferred)
                    : kind == BuildingKind.Mine ? PlaceMine(faction, preferred) : PlaceStorage(faction, preferred, world.Nodes[best].Definition.Id);
                if (receiver == 0) return;
            }
            var building = world.Buildings[receiver - 1];
            if (!building.Alive || !building.Complete || building.Held) return;
            var taken = TakenForOtherLines(faction, -1);
            var route = BeltRoute(faction, OutputCell(building), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null || !CanLayAiBelts(faction, route.cells)) return;
            LayRoutes(faction, route);
        }

        private bool RawReceiverAllowed(uint faction, ResourceKind kind)
            => world.Config.Economy.BeltComponents && (CommonDrillOn ? kind == ResourceKind.Wood || kind == ResourceKind.Stone
                || kind == ResourceKind.Ore || kind == ResourceKind.Gold : IsRawResource(kind));

        private BuildingKind RawReceiverKind(uint faction, ResourceKind kind)
            => CommonDrillOn && (kind == ResourceKind.Wood || kind == ResourceKind.Stone || kind == ResourceKind.Ore || kind == ResourceKind.Gold) ? BuildingKind.Drill
                : kind == ResourceKind.Wood && ForestryAllowed(faction) ? BuildingKind.LumberCamp
                : kind == ResourceKind.Stone && MasonryAllowed(faction) ? BuildingKind.Quarry
                : kind == ResourceKind.Ore && MetalworkAllowed(faction) ? BuildingKind.Mine
                : BuildingKind.Storage;

        private int RawReceiverWoodCost(BuildingKind kind)
            => kind == BuildingKind.Drill ? world.Config.Economy.DrillWoodCost
                : kind == BuildingKind.LumberCamp ? world.Config.Economy.LumberCampWoodCost
                : kind == BuildingKind.Quarry ? world.Config.Economy.QuarryWoodCost
                : kind == BuildingKind.Mine ? world.Config.Economy.MineWoodCost : world.Config.Economy.StorageWoodCost;

        private uint FindRawReceiver(uint faction, BuildingKind kind, uint nodeId)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && b.Kind == kind && b.NodeId == nodeId) return b.Id;
            }
            return 0;
        }

        private uint PlaceStorage(uint faction, int preferredCell, uint nodeId)
        {
            if (!world.Config.Economy.BeltComponents) return 0;
            int size = world.Config.Economy.StorageSizeCells;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(OwnCore(faction).Definition.Position);
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) return 0;
            var core = OwnCore(faction).Definition.Position;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                PlaceBuildingAt(faction, BuildingKind.Storage, origin, side, 0);
                world.Buildings[world.BuildingCount - 1].NodeId = nodeId;
                return world.NextBuildingId - 1;
            }
            return 0;
        }

        private uint PlaceDrill(uint faction, int preferredCell = -1)
        {
            var core = preferredCell >= 0 ? world.Map.Center(preferredCell) : OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.DrillSizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            var order = new int[world.Nodes.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = DistanceSquared(world.Nodes[a].Definition.Position, core).CompareTo(DistanceSquared(world.Nodes[b].Definition.Position, core));
                return c != 0 ? c : world.Nodes[a].Definition.Id.CompareTo(world.Nodes[b].Definition.Id);
            });
            foreach (int n in order)
            {
                var node = world.Nodes[n];
                if (!ResourceKnownToFaction(faction, n) || node.Remaining <= 0
                    || (node.Definition.Kind != ResourceKind.Wood && node.Definition.Kind != ResourceKind.Stone
                        && node.Definition.Kind != ResourceKind.Ore && node.Definition.Kind != ResourceKind.Gold)) continue;
                bool goldUsable = node.Definition.Kind != ResourceKind.Gold
                    || GoldTargetUsable(faction, node.Definition.Id, world.Map.Cell(OwnCore(faction).Definition.Position));
                if (!goldUsable) continue;
                int cell = world.Map.Cell(node.Definition.Position), nx = cell % width, nz = cell / width;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x0 = nx - dx, z0 = nz - dz;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!DrillSiteIsClear(faction, origin, out uint nodeId, true) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            PlaceBuildingAt(faction, BuildingKind.Drill, origin, side, nodeId);
                            return world.NextBuildingId - 1;
                        }
                    }
            }
            return 0;
        }

        private bool CanLayAiBelts(uint faction, int[] route)
        {
            ref var economy = ref world.Economies[faction - 1];
            int missing = 0;
            var seen = new bool[world.Belts.Length];
            foreach (int cell in route)
                if (!seen[cell] && world.Belts[cell].FactionId != faction) { seen[cell] = true; missing++; }
            return economy.Wood - missing * world.Config.Economy.BeltWoodCost >= world.Config.Economy.AiBeltWoodReserve;
        }
    }
}
