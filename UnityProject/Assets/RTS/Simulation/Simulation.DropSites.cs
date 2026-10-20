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
                if (IsPlannedBuilding(b) && b.FactionId == faction && b.Kind == BuildingKind.DropSite) count++;
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
                if (IsActualBuilding(target) && target.Complete && target.FactionId == v.FactionId && target.Kind == BuildingKind.CharcoalKiln)
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
                if (!IsActualBuilding(harbor) || !harbor.Complete || harbor.FactionId != v.FactionId || harbor.Kind != BuildingKind.Harbor) continue;
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
                if (!IsActualBuilding(town) || !town.Complete || town.FactionId != v.FactionId || town.Kind != BuildingKind.Town) continue;
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
                if (!IsActualBuilding(b) || !b.Complete || b.FactionId != v.FactionId
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
                if (!IsActualBuilding(storage) || !storage.Complete || storage.FactionId != v.FactionId || storage.Kind != BuildingKind.Storage
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
                if (!IsActualBuilding(storage) || !storage.Complete || storage.FactionId != faction || storage.Kind != BuildingKind.Storage) return false;
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
                    if (IsActualBuilding(storage) && storage.Complete && storage.Kind == BuildingKind.Storage)
                {
                    storageConnectivity[storage.Id] = StorageHasBeltPathToCore(storage, storage.FactionId);
                    if (StorageFeedsProcessingOn) storageProcessingConnectivity[storage.Id] = new byte[29];
                }
            }
        }

        private bool StorageHasBeltPathToCore(BuildingState storage, uint faction)
        {
            int start = OutputCell(storage);
            if (start < 0 || start >= world.Belts.Length || !AiBeltCellKnown(faction, start) || world.Belts[start].FactionId != faction) return false;
            var seen = new bool[world.Belts.Length];
            var queue = new int[world.Belts.Length];
            int head = 0, tail = 0;
            seen[start] = true;
            queue[tail++] = start;
            while (head < tail)
            {
                int cell = queue[head++];
                if (!AiBeltCellKnown(faction, cell)) continue;
                ref var belt = ref world.Belts[cell];
                if (Visit(BeltOutputCell(cell, ref belt))) return true;
                if (belt.Component == BeltComponentKind.UndergroundEntrance && Visit(belt.PairCell)) return true;
                if (belt.Component == BeltComponentKind.Splitter && Visit(BeltNext(cell, TurnLeft(belt.Facing)))) return true;
            }
            return false;

            bool Visit(int next)
            {
                if (next < 0 || next >= world.Belts.Length) return false;
                if (!AiBeltCellKnown(faction, next)) return false;
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
            if (start < 0 || start >= world.Belts.Length || !AiBeltCellKnown(faction, start) || world.Belts[start].FactionId != faction) return false;
            var seen = new bool[world.Belts.Length];
            var queue = new int[world.Belts.Length];
            int head = 0, tail = 0;
            seen[start] = true;
            queue[tail++] = start;
            while (head < tail)
            {
                int cell = queue[head++];
                if (!AiBeltCellKnown(faction, cell)) continue;
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
                if (!AiBeltCellKnown(faction, next)) return false;
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
            if (LargeEconomyOn)
            {
                DecideLargeClusterDropSite(faction);
                return;
            }
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
                if (!IsActualBuilding(b) || b.FactionId != faction || b.Kind != BuildingKind.DropSite) continue;
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
                covered = IsActualBuilding(b) && b.FactionId == faction && b.Kind == BuildingKind.DropSite
                        && InRange(world.Map.Center(b.WorkCell), at, Fix64.FromInt(DropSiteCoverMeters));
                }
                if (!covered) bestNode = n;
            }
            if (bestNode < 0) return;
            if (DropSiteLimitOn && AliveDropSites(faction) >= rules.DropSiteMax) return;
            int origin = FindSiteNear(faction, rules.DropSiteSizeCells, world.Map.Cell(world.Nodes[bestNode].Definition.Position));
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.DropSite, origin, Facing.North, 0);
        }

        private void DecideLargeClusterDropSite(uint faction)
        {
            if (!AgesOn || world.Tick % world.Config.Economy.LargeClusterDecisionIntervalTicks != 0) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.AutoOff || economy.Wood < rules.DropSiteWoodCost) return;
            EnsureLargeResourceClusters();
            // The explicit large-map cap is shared with human placement. The cluster setting is kept as a second
            // guard so a scenario cannot make the automatic policy exceed the command-visible limit.
            int maxSites = Math.Min(rules.LargeDropSiteMax, rules.LargeResourceClusterMaxDropSites);
            if (AliveDropSites(faction) >= maxSites)
            {
                DecideAiRawBelts(faction);
                return;
            }
            var gatherers = new int[largeClusterNodes.Length];
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.NodeId == 0 || v.NodeId > world.Nodes.Length
                    || (v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering && v.Task != VillagerTask.ToDropOff)) continue;
                gatherers[largeNodeClusters[v.NodeId - 1]]++;
            }
            int best = -1, bestOrigin = -1, bestGatherers = 0, bestRoundTrip = 0, bestRemaining = -1; uint bestId = 0;
            for (int cluster = 0; cluster < largeClusterNodes.Length; cluster++)
            {
                int knownNode = FirstKnownRemainingNode(faction, cluster);
                if (knownNode < 0) continue;
                int remaining = ClusterRemainingFor(faction, cluster);
                if (remaining <= 0 || gatherers[cluster] < rules.LargeClusterMinGatherers) continue;
                var kind = largeClusterKinds[cluster];
                if (kind != ResourceKind.Food && kind != ResourceKind.Wood && kind != ResourceKind.Stone
                    && kind != ResourceKind.Ore && kind != ResourceKind.Gold && kind != ResourceKind.Metal) continue;
                var anchor = world.Nodes[knownNode].Definition.Position;
                var probe = new VillagerState { FactionId = faction, Position = anchor, CarryKind = kind, NodeId = world.Nodes[knownNode].Definition.Id,
                    Task = VillagerTask.ToDropOff };
                var drop = DropOff(probe).point;
                int roundTrip = LargeClusterRoundTripTicks(faction, cluster, drop);
                int initial = LargeClusterInitialRemaining(faction, cluster);
                bool belowSwitchThreshold = initial > 0
                    && (long)remaining * 1000L <= (long)initial * rules.LargeClusterSwitchPermille;
                // When a cluster is nearly exhausted, choose the next cluster before the current one runs dry. A
                // cluster that already has a local receiver is skipped in that advance window, so the slot goes to
                // another useful cluster rather than making a second receiver in the same place.
                if (LargeClusterHasDropSite(faction, cluster)) continue;
                if (roundTrip <= rules.LargeTargetRoundTripTicks && !belowSwitchThreshold) continue;
                uint id = world.Nodes[knownNode].Definition.Id;
                var preferredAnchor = world.Nodes[knownNode].Definition.Position;
                var preferredProbe = new VillagerState { FactionId = faction, Position = preferredAnchor, CarryKind = kind,
                    NodeId = id, Task = VillagerTask.ToDropOff };
                int preferredCell = LargeClusterFarthestNodeCell(faction, cluster, DropOff(preferredProbe).point);
                if (preferredCell < 0) preferredCell = world.Map.Cell(preferredAnchor);
                int origin = FindLargeDropSiteOrigin(faction, rules.DropSiteSizeCells, preferredCell);
                if (origin < 0) continue;
                // Serve the worst transport first, then preserve the most usable stock. This prevents a small,
                // nearly empty point from consuming a slot while a farther, richer cluster still walks to a base.
                if (best < 0 || roundTrip > bestRoundTrip
                    || roundTrip == bestRoundTrip && (remaining > bestRemaining
                        || remaining == bestRemaining && (gatherers[cluster] > bestGatherers
                            || gatherers[cluster] == bestGatherers && id < bestId)))
                {
                    best = cluster; bestOrigin = origin; bestGatherers = gatherers[cluster]; bestRoundTrip = roundTrip;
                    bestRemaining = remaining; bestId = id;
                }
            }
            if (best < 0) return;
            if (bestOrigin >= 0)
            {
                PlaceBuildingAt(faction, BuildingKind.DropSite, bestOrigin, Facing.North, 0);
                // A new local receiver changes the next deterministic cluster choice on this tick.
                largeWorkCountsTick = -1;
            }
        }

        private int LargeClusterInitialRemaining(uint faction, int cluster)
        {
            int remaining = 0;
            foreach (int nodeIndex in largeClusterNodes[cluster])
                if (ResourceKnownToFaction(faction, nodeIndex))
                    remaining = checked(remaining + Math.Max(0, world.Nodes[nodeIndex].Definition.Amount));
            return remaining;
        }

        private int ClusterRemainingFor(uint faction, int cluster)
        {
            int remaining = 0;
            foreach (int nodeIndex in largeClusterNodes[cluster])
                if (ResourceKnownToFaction(faction, nodeIndex))
                    remaining = checked(remaining + Math.Max(0, world.Nodes[nodeIndex].Remaining));
            return remaining;
        }

        private bool LargeClusterHasDropSite(uint faction, int cluster)
        {
            var spacing = Fix64.FromInt(world.Config.Economy.LargeDropSiteMinSpacingMeters);
            foreach (int nodeIndex in largeClusterNodes[cluster])
            {
                if (!ResourceKnownToFaction(faction, nodeIndex)) continue;
                var point = world.Nodes[nodeIndex].Definition.Position;
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var b = world.Buildings[i];
                    if (!IsPlannedBuilding(b) || b.FactionId != faction || b.Kind != BuildingKind.DropSite) continue;
                    if (InRange(point, BuildingCenter(b), spacing)) return true;
                }
            }
            return false;
        }

        /// <summary>LargeEconomy AI placement only: skip candidates near every own receiver and base, including reservations.</summary>
        private int FindLargeDropSiteOrigin(uint faction, int size, int centre, int searchRadius = 30)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position), cx = centre % width, cz = centre / width;
            for (int r = 0; r <= searchRadius; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (LargeEconomyOn && !ExploredFootprint(faction, origin, size)) continue;
                        if (!LargeDropSiteSpacingIsClear(faction, origin, size)) continue;
                        if (SiteIsClear(origin, core, size) && KeepsMapConnected(faction, origin, size)) return origin;
                    }
            return -1;
        }

        private bool LargeDropSiteSpacingIsClear(uint faction, int origin, int size)
        {
            var spacing = Fix64.FromInt(world.Config.Economy.LargeDropSiteMinSpacingMeters);
            var centre = FootprintCenter(origin, size);
            if (InRange(centre, OwnCore(faction).Definition.Position, spacing)) return false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                // Do not use IsActualBuilding here: an AI reservation is already a committed receiver location.
                if (!b.Alive || b.FactionId != faction || (b.Kind != BuildingKind.DropSite && b.Kind != BuildingKind.Town)) continue;
                if (InRange(centre, BuildingCenter(b), spacing)) return false;
            }
            return true;
        }

        private bool ExploredFootprint(uint faction, int origin, int size)
        {
            var explored = world.Factions[faction - 1].ExploredCells;
            foreach (int cell in Footprint(origin, size))
                if (cell < 0 || cell >= explored.Length || !explored[cell]) return false;
            return true;
        }

        private int LargeClusterRoundTripTicks(uint faction, int cluster, SimPoint drop)
        {
            long total = 0; int count = 0;
            foreach (int nodeIndex in largeClusterNodes[cluster])
            {
                var node = world.Nodes[nodeIndex];
                if (!ResourceKnownToFaction(faction, nodeIndex) || node.Remaining <= 0) continue;
                var route = world.Map.SharedRoute(world.Map.Cell(node.Definition.Position), drop, faction);
                if (route.Length <= 0) continue;
                long distanceRaw = checked((long)(route.Length - 1) * world.Config.Map.CellSizeMeters * 65536L);
                long oneWay = checked((distanceRaw * 2 + world.VillagerStep.Raw - 1) / world.VillagerStep.Raw);
                total = checked(total + oneWay * 2);
                count++;
            }
            return count == 0 ? int.MaxValue : checked((int)(total / count));
        }

        private int LargeClusterFarthestNodeCell(uint faction, int cluster, SimPoint drop)
        {
            int best = -1, bestLength = -1; uint bestId = 0;
            foreach (int nodeIndex in largeClusterNodes[cluster])
            {
                var node = world.Nodes[nodeIndex];
                if (!ResourceKnownToFaction(faction, nodeIndex) || node.Remaining <= 0) continue;
                int length = world.Map.SharedRoute(world.Map.Cell(node.Definition.Position), drop, faction).Length;
                if (length > bestLength || length == bestLength && node.Definition.Id < bestId)
                { best = nodeIndex; bestLength = length; bestId = node.Definition.Id; }
            }
            return best < 0 ? -1 : world.Map.Cell(world.Nodes[best].Definition.Position);
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
                if (!ResourceKnownToFaction(faction, n) || node.Remaining <= 0 || gatherers[n] == 0 && !CommonDrillOn
                    || InRange(node.Definition.Position, core, Fix64.FromInt(FarGatherMeters))) continue;
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
                if (!IsActualBuilding(building) || !building.Complete || building.Held) return;
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
                if (IsActualBuilding(b) && b.FactionId == faction && b.Kind == kind && b.NodeId == nodeId) return b.Id;
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
