using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-2 automatic economy for industry (technical-design-v3 13): one fixed line, mine -> smelter -> core. A mine on the
    /// ore point nearest the core, a smelter between them, then belts on the shortest open route between the ports; until
    /// the line is whole, two villagers carry by hand. Every choice is a fixed-order search over the true own state, the
    /// public resource points and the terrain, so the same state always gives the same line.
    /// </summary>
    public sealed partial class Simulation
    {
        private const int Haulers = 2;
        // V3-11 #2 provisional policy: one cell is two metres on the default map, so the bridge must save 4 m.
        private const int BridgeShorteningThresholdCells = 2;
        private const int BridgeDangerMeters = 16;
        private const long BridgeRebuildWaitTicks = 200;
        private static readonly Facing[] Sides = { Facing.North, Facing.East, Facing.South, Facing.West };

        /// <summary>AI phase, after the barracks and infantry (13 steps 1-4).</summary>
        private void DecideIndustry(uint faction)
        {
            if (FarmingAllowed(faction)) { DecideFarms(faction); return; }
            if (ForestryAllowed(faction))
            {
                if (world.Economies[faction - 1].Age < 2) { DecideLumberCamp(faction); return; }
                DecideForestryLines(faction);
                return;
            }
            if (MasonryAllowed(faction)) { DecideQuarry(faction); return; }
            if (BridgeAllowed(faction)) { DecideEngineerCamp(faction); return; }
            if (!IndustryOn || !MetalworkAllowed(faction)) return;
            // ProcessingChain is an opt-in extension. Keep the V3-2/V3-5 core-line decision path byte-for-byte
            // equivalent on every map that does not carry the new flag.
            if (!ProcessingOn) { DecideLegacyIndustry(faction); return; }
            ref var economy = ref world.Economies[faction - 1];
            int coreIndex = GetOrCreateLine(faction, ProcessingLineKind.CoreMetal);
            ref var coreLine = ref world.ProcessingLines[coreIndex];
            if (!IsAutoLine(coreIndex)) return;
            if (!BuildingReady(coreLine.MineId))
            {
                if (BuildingPending(coreLine.MineId)) return;
                if (economy.Wood < world.Config.Economy.MineWoodCost) return;
                uint id = PlaceMine(faction);
                if (id != 0) SetLineBuilding(coreIndex, BuildingKind.Mine, id);
                return;
            }
            if (!BuildingReady(coreLine.SmelterId))
            {
                if (BuildingPending(coreLine.SmelterId)) return;
                if (economy.Wood < world.Config.Economy.SmelterWoodCost) return;
                uint id = PlaceSmelter(faction, world.Buildings[coreLine.MineId - 1]);
                if (id != 0) SetLineBuilding(coreIndex, BuildingKind.Smelter, id);
                return;
            }
            var coreMine = world.Buildings[coreLine.MineId - 1];
            var coreSmelter = world.Buildings[coreLine.SmelterId - 1];
            if (!LayCoreLine(faction, coreIndex, coreMine, coreSmelter)) SetLineHaulers(faction, coreLine, Haulers);

            if (!ProcessingAvailable(faction) || !LineBeltsWhole(coreIndex) || !LineBuildingsReady(coreLine)) return;
            int steelIndex = GetOrCreateLine(faction, ProcessingLineKind.Steel);
            ref var steelLine = ref world.ProcessingLines[steelIndex];
            if (!IsAutoLine(steelIndex)) return;
            // One AI cycle advances exactly one stage of the steel line. A lack of wood leaves that stage pending.
            if (!BuildingReady(steelLine.MineId))
            {
                if (BuildingPending(steelLine.MineId)) return;
                if (economy.Wood < world.Config.Economy.MineWoodCost) return;
                uint id = PlaceMine(faction);
                if (id != 0) SetLineBuilding(steelIndex, BuildingKind.Mine, id);
                return;
            }
            if (!BuildingReady(steelLine.SmelterId))
            {
                if (BuildingPending(steelLine.SmelterId)) return;
                if (economy.Wood < world.Config.Economy.SmelterWoodCost) return;
                uint id = PlaceSmelter(faction, world.Buildings[steelLine.MineId - 1]);
                if (id != 0) SetLineBuilding(steelIndex, BuildingKind.Smelter, id);
                return;
            }
            if (!BuildingReady(steelLine.KilnId))
            {
                if (BuildingPending(steelLine.KilnId)) return;
                if (economy.Wood < world.Config.Economy.CharcoalKilnWoodCost) return;
                uint id = PlaceKiln(faction);
                if (id != 0) SetLineBuilding(steelIndex, BuildingKind.CharcoalKiln, id);
                return;
            }
            if (!BuildingReady(steelLine.SteelworksId))
            {
                if (BuildingPending(steelLine.SteelworksId)) return;
                if (economy.Wood < world.Config.Economy.SteelworksWoodCost) return;
                uint id = PlaceSteelworks(faction, steelLine);
                if (id != 0) SetLineBuilding(steelIndex, BuildingKind.Steelworks, id);
                return;
            }
            var steelMine = world.Buildings[steelLine.MineId - 1];
            var steelSmelter = world.Buildings[steelLine.SmelterId - 1];
            var kiln = world.Buildings[steelLine.KilnId - 1];
            var steelworks = world.Buildings[steelLine.SteelworksId - 1];
            LaySteelLine(faction, steelIndex, steelMine, steelSmelter, kiln, steelworks);
            // Unlike the core line, this branch always needs its two wood carriers: the kiln has no automatic
            // resource input, even after all four belt routes are complete.
            SetLineHaulers(faction, steelLine, Haulers);
        }

        private void DecideLegacyIndustry(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int mine = OwnBuildingIndex(faction, BuildingKind.Mine), smelter = OwnBuildingIndex(faction, BuildingKind.Smelter);
            if (mine < 0)
            {
                if (economy.Wood >= world.Config.Economy.MineWoodCost) PlaceMine(faction);
                return;
            }
            if (smelter < 0)
            {
                if (economy.Wood >= world.Config.Economy.SmelterWoodCost) PlaceSmelter(faction, world.Buildings[mine]);
                return;
            }
            var m = world.Buildings[mine];
            var s = world.Buildings[smelter];
            if (!m.Complete || !s.Complete) return;
            if (m.Held || s.Held) { SetHaulers(faction, m.Id, s.Id, 0); return; }
            bool whole = LayLineLegacy(faction, m, s);
            SetHaulers(faction, m.Id, s.Id, whole ? 0 : Haulers);
        }

        /// <summary>
        /// V3-7 first pass: one lumber camp on the nearest usable wood point, then one existing farm-style route to
        /// the core. No terrain score is used here; the later terrain pass owns that decision.
        /// </summary>
        private void DecideLumberCamp(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int camp = OwnBuildingIndex(faction, BuildingKind.LumberCamp);
            if (camp < 0)
            {
                if (economy.Wood >= world.Config.Economy.LumberCampWoodCost) PlaceLumberCamp(faction);
                return;
            }
            var b = world.Buildings[camp];
            if (!b.Complete || b.Held) return;
            var taken = new bool[world.Belts.Length];
            var route = BeltRoute(faction, OutputCell(b), taken, next => FeedsOwnCore(next, faction));
            bool whole = false;
            if (route.cells != null)
            {
                foreach (int c in route.cells) taken[c] = true;
                whole = LayBelts(faction, route.cells, route.facings);
            }
            SetFarmHauler(faction, b.Id, whole ? 0 : 1);
        }

        /// <summary>
        /// V3-8 first pass: one quarry on the nearest usable stone point, then the same core route and hand-haul fallback
        /// as a lumber camp. Terrain quality is deliberately not scored until the second masonry pass.
        /// </summary>
        private void DecideQuarry(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int quarry = OwnBuildingIndex(faction, BuildingKind.Quarry);
            if (quarry < 0)
            {
                if (economy.Wood >= world.Config.Economy.QuarryWoodCost) PlaceQuarry(faction);
                return;
            }
            var b = world.Buildings[quarry];
            if (!b.Complete || b.Held) return;
            var taken = new bool[world.Belts.Length];
            var route = BeltRoute(faction, OutputCell(b), taken, next => FeedsOwnCore(next, faction));
            bool whole = false;
            if (route.cells != null)
            {
                foreach (int c in route.cells) taken[c] = true;
                whole = LayBelts(faction, route.cells, route.facings);
            }
            SetFarmHauler(faction, b.Id, whole ? 0 : 1);
        }

        /// <summary>V3-11: build the camp, then choose and build one useful bridge at a time.</summary>
        private void DecideEngineerCamp(uint faction)
        {
            int camp = OwnBuildingIndex(faction, BuildingKind.EngineerCamp);
            if (camp < 0)
            {
                ref var economy = ref world.Economies[faction - 1];
                if (economy.Wood < world.Config.Economy.EngineerCampWoodCost) return;
                int origin = FindSite(faction, world.Config.Economy.EngineerCampSizeCells);
                if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.EngineerCamp, origin, Facing.North, 0);
                return;
            }
            // Any human bridge operation owns the bridge policy for the rest of this match. This covers a manually
            // removed bridge too: the automatic economy must not replace it at a different location.
            if (HasManualBridge(faction) || HasAutomaticBridge(faction)) return;
            if (AutomaticBridgeRebuildBlocked(faction)) return;
            ref var bridgeEconomy = ref world.Economies[faction - 1];
            if (bridgeEconomy.Wood < world.Config.Economy.BridgeWoodCost) return;
            if (!TryFindBridgeCandidate(faction, out int[] cells, out Facing facing, out int workCell)) return;
            if (!TryValidateBridge(faction, cells, facing, out cells, out workCell)) return;
            PlaceBuildingAt(faction, BuildingKind.Bridge, cells[0], facing, 0, cells, workCell);
        }

        private bool HasManualBridge(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.FactionId == faction && b.Kind == BuildingKind.Bridge && b.Held) return true;
            }
            return false;
        }

        private bool HasAutomaticBridge(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && b.Kind == BuildingKind.Bridge && !b.Held) return true;
            }
            return false;
        }

        private bool AutomaticBridgeRebuildBlocked(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.FactionId == faction && b.Kind == BuildingKind.Bridge && !b.Alive && !b.Held && b.DestroyedTick > 0
                    && world.Tick - b.DestroyedTick < BridgeRebuildWaitTicks) return true;
            }
            return false;
        }

        private bool TryFindBridgeCandidate(uint faction, out int[] bestCells, out Facing bestFacing, out int bestWorkCell)
        {
            bestCells = null; bestFacing = Facing.North; bestWorkCell = -1;
            var destinations = BridgeDestinations(faction);
            if (destinations.Count < 2) return false;
            var observedEnemyCells = ObservedEnemyCells(faction);
            int bestAdjusted = 0, bestOwn = 0, bestEnemy = 0, bestFirst = int.MaxValue, bestLength = int.MaxValue;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int max = world.Config.Economy.MaxBridgeLength;
            // Positive directions and z/x order are the deterministic candidate order. Reverse input is equivalent
            // for placement, and the first candidate wins every remaining tie.
            int[] deltas = { 1, width };
            for (int direction = 0; direction < deltas.Length; direction++)
                for (int z = 0; z < height; z++)
                    for (int x = 0; x < width; x++)
                        {
                            int first = z * width + x, delta = deltas[direction];
                            if (!IsRiverCell(first) || world.Map.IsPassable(first)) continue;
                            int before = first - delta;
                            if (before < 0 || before >= width * height || !world.Map.IsPassable(before) || IsRiverCell(before)) continue;
                            int length = 0, last = first;
                            while (length < max && last >= 0 && last < width * height && IsRiverCell(last) && !world.Map.IsPassable(last))
                            {
                                if (direction == 0 && last / width != z || direction == 1 && last % width != x) break;
                                length++; last += delta;
                            }
                            if (length == 0 || length > max || last < 0 || last >= width * height || !world.Map.IsPassable(last) || IsRiverCell(last)) continue;
                            var candidate = new int[length];
                            for (int i = 0; i < length; i++) candidate[i] = first + delta * i;
                            if (!TryValidateBridge(faction, candidate, direction == 0 ? Facing.East : Facing.South, out _, out int workCell)) continue;
                            if (ObservedEnemyNear(faction, workCell)) continue;
                            if (BridgeOnRebuildCooldown(faction, candidate)) continue;
                            int ownSaving = MaximumBridgeSaving(candidate, destinations, destinations);
                            if (ownSaving < BridgeShorteningThresholdCells) continue;
                            int enemySaving = MaximumBridgeSaving(candidate, observedEnemyCells, destinations);
                            int adjusted = checked(ownSaving - enemySaving / 2); // enemy shortcut is a discount, based only on observed enemies
                            if (adjusted <= 0) continue;
                            if (bestCells == null || adjusted > bestAdjusted || adjusted == bestAdjusted && ownSaving > bestOwn
                                || adjusted == bestAdjusted && ownSaving == bestOwn && enemySaving < bestEnemy
                                || adjusted == bestAdjusted && ownSaving == bestOwn && enemySaving == bestEnemy && candidate[0] < bestFirst
                                || adjusted == bestAdjusted && ownSaving == bestOwn && enemySaving == bestEnemy && candidate[0] == bestFirst && length < bestLength)
                            {
                                bestCells = candidate; bestFacing = direction == 0 ? Facing.East : Facing.South; bestWorkCell = workCell;
                                bestAdjusted = adjusted; bestOwn = ownSaving; bestEnemy = enemySaving; bestFirst = candidate[0]; bestLength = length;
                            }
                        }
            return bestCells != null;
        }

        private List<int> BridgeDestinations(uint faction)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            void Add(int cell)
            {
                if (cell >= 0 && world.Map.IsPassable(cell) && seen.Add(cell)) result.Add(cell);
            }
            Add(world.Map.Cell(OwnCore(faction).Definition.Position));
            // The enemy core is a fixed, known objective and makes a bridge useful for the actual offensive route;
            // enemy positions are still consulted only through ObservedEnemyCells below.
            Add(world.Map.Cell(world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position));
            foreach (var outpost in world.Outposts)
                if (outpost.OwnerFactionId == faction) Add(world.Map.Cell(outpost.Definition.Position));
            var explored = world.Factions[faction - 1].ExploredCells;
            foreach (var node in world.Nodes)
            {
                int cell = world.Map.Cell(node.Definition.Position);
                if (node.Remaining > 0 && cell >= 0 && explored[cell]) Add(cell);
            }
            return result;
        }

        private List<int> ObservedEnemyCells(uint faction)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            var visible = world.Factions[faction - 1].VisibleCells;
            for (int i = 0; i < world.SoldierCount; i++)
            {
                var soldier = world.Soldiers[i];
                if (!soldier.Alive || soldier.Initial.FactionId == faction) continue;
                int cell = world.Map.Cell(soldier.Position);
                if (cell >= 0 && visible[cell] && world.Map.IsPassable(cell) && seen.Add(cell)) result.Add(cell);
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var villager = world.Villagers[i];
                if (!villager.Alive || villager.FactionId == faction) continue;
                int cell = world.Map.Cell(villager.Position);
                if (cell >= 0 && visible[cell] && world.Map.IsPassable(cell) && seen.Add(cell)) result.Add(cell);
            }
            return result;
        }

        private int MaximumBridgeSaving(int[] bridgeCells, List<int> sources, List<int> destinations)
        {
            if (sources.Count == 0) return 0;
            int best = 0, mapCells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            var without = new int[sources.Count * destinations.Count];
            for (int s = 0; s < sources.Count; s++)
                for (int d = 0; d < destinations.Count; d++)
                    without[s * destinations.Count + d] = sources[s] == destinations[d] ? 0 : BridgeRouteLength(sources[s], destinations[d]);
            foreach (int cell in bridgeCells) world.Map.SetPassable(cell, true);
            try
            {
                for (int s = 0; s < sources.Count; s++)
                    for (int d = 0; d < destinations.Count; d++)
                    {
                        int before = without[s * destinations.Count + d];
                        if (before == 0) continue;
                        int after = BridgeRouteLength(sources[s], destinations[d]);
                        if (after < 0) continue;
                        int saving = before < 0 ? checked(mapCells - after) : before - after;
                        if (saving > best) best = saving;
                    }
            }
            finally { foreach (int cell in bridgeCells) world.Map.SetPassable(cell, false); }
            return best;
        }

        private int BridgeRouteLength(int start, int destination)
        {
            var route = world.Map.SharedRoute(start, world.Map.Center(destination));
            return route.Length == 0 ? -1 : route.Length - 1;
        }

        private bool ObservedEnemyNear(uint faction, int cell)
        {
            long radius = Fix64.FromInt(BridgeDangerMeters).Raw;
            long limit = checked(radius * radius);
            var point = world.Map.Center(cell);
            var visible = world.Factions[faction - 1].VisibleCells;
            for (int i = 0; i < world.SoldierCount; i++)
            {
                var enemy = world.Soldiers[i];
                if (!enemy.Alive || enemy.Initial.FactionId == faction) continue;
                int enemyCell = world.Map.Cell(enemy.Position);
                if (enemyCell < 0 || !visible[enemyCell]) continue;
                long dx = enemy.Position.X.Raw - point.X.Raw, dz = enemy.Position.Z.Raw - point.Z.Raw;
                if (checked(dx * dx + dz * dz) <= limit) return true;
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var enemy = world.Villagers[i];
                if (!enemy.Alive || enemy.FactionId == faction) continue;
                int enemyCell = world.Map.Cell(enemy.Position);
                if (enemyCell < 0 || !visible[enemyCell]) continue;
                long dx = enemy.Position.X.Raw - point.X.Raw, dz = enemy.Position.Z.Raw - point.Z.Raw;
                if (checked(dx * dx + dz * dz) <= limit) return true;
            }
            return false;
        }

        private bool BridgeOnRebuildCooldown(uint faction, int[] cells)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.FactionId != faction || b.Kind != BuildingKind.Bridge || b.Alive || b.Held || b.DestroyedTick <= 0
                    || world.Tick - b.DestroyedTick >= BridgeRebuildWaitTicks) continue;
                var old = b.BridgeCells ?? Array.Empty<int>();
                if (old.Length != cells.Length) continue;
                bool same = true;
                foreach (int cell in cells) if (Array.IndexOf(old, cell) < 0) { same = false; break; }
                if (same) return true;
            }
            return false;
        }

        /// <summary>V3-7 #2: after the first camp, keep a separate core-wood line and a bow-gear line.</summary>
        private void DecideForestryLines(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int coreIndex = GetOrCreateLine(faction, ProcessingLineKind.CoreWood);
            ref var coreLine = ref world.ProcessingLines[coreIndex];
            if (!BindForestryCamp(faction, coreIndex, ref coreLine, 0)) return;
            if (IsAutoLine(coreIndex))
            {
                var coreCamp = world.Buildings[coreLine.LumberCampId - 1];
                if (!coreCamp.Complete) return;
                bool coreWhole = LayForestryCoreLine(faction, coreIndex, coreCamp);
                SetLineHaulers(faction, coreLine, coreWhole ? 0 : 1);
            }

            uint bowCampId = FindCamp(faction, coreLine.LumberCampId);
            if (bowCampId == 0)
            {
                // Keep the market, the next age and ordinary construction funded; only the surplus opens the second line.
                if (economy.Food < rules.MarketFoodFloor || economy.Wood < rules.MarketWoodReserve + rules.LumberCampWoodCost + rules.FletcherWoodCost) return;
                PlaceLumberCamp(faction);
                return;
            }
            int bowIndex = GetOrCreateLine(faction, ProcessingLineKind.BowGear);
            ref var bowLine = ref world.ProcessingLines[bowIndex];
            if (bowLine.LumberCampId == 0 || !BuildingAlive(bowLine.LumberCampId)) SetLineBuilding(bowIndex, BuildingKind.LumberCamp, bowCampId);
            if (bowLine.Manager == LineManager.Automatic && world.Buildings[bowLine.LumberCampId - 1].Held) bowLine.Manager = LineManager.Manual;
            if (!IsAutoLine(bowIndex)) return;
            if (!BuildingReady(bowLine.FletcherId))
            {
                if (BuildingPending(bowLine.FletcherId)) return;
                if (economy.Wood < rules.FletcherWoodCost) return;
                uint id = PlaceFletcher(faction, world.Buildings[bowLine.LumberCampId - 1]);
                if (id != 0) SetLineBuilding(bowIndex, BuildingKind.Fletcher, id);
                return;
            }
            var bowCamp = world.Buildings[bowLine.LumberCampId - 1];
            var fletcher = world.Buildings[bowLine.FletcherId - 1];
            if (!bowCamp.Complete || !fletcher.Complete) return;
            bool bowWhole = LayBowLine(faction, bowIndex, bowCamp, fletcher);
            SetLineHaulers(faction, bowLine, bowWhole ? 0 : 1);
        }

        private bool BindForestryCamp(uint faction, int lineIndex, ref ProcessingLineState line, uint exclude)
        {
            if (line.LumberCampId != 0 && BuildingAlive(line.LumberCampId)) return true;
            uint id = FindCamp(faction, exclude);
            if (id == 0)
            {
                DecideLumberCamp(faction);
                return false;
            }
            SetLineBuilding(lineIndex, BuildingKind.LumberCamp, id);
            line = world.ProcessingLines[lineIndex];
            if (world.Buildings[id - 1].Held) line.Manager = LineManager.Manual;
            return true;
        }

        private uint FindCamp(uint faction, uint exclude)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && b.Kind == BuildingKind.LumberCamp && b.Id != exclude) return b.Id;
            }
            return 0;
        }

        private bool BuildingAlive(uint id) => id != 0 && id <= world.BuildingCount && world.Buildings[id - 1].Alive;

        private uint PlaceFletcher(uint faction, BuildingState camp)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = world.Map.Cell(FootprintCenter(camp.OriginCell, SizeOf(camp.Kind)));
            int origin = FindProcessingSite(faction, centre, world.Config.Economy.FletcherSizeCells);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, world.Config.Economy.FletcherSizeCells), core))
            {
                if (!PortIsOpen(OutputCell(origin, world.Config.Economy.FletcherSizeCells, side), faction)) continue;
                PlaceBuildingAt(faction, BuildingKind.Fletcher, origin, side, 0);
                return world.NextBuildingId - 1;
            }
            return 0;
        }

        private bool LayForestryCoreLine(uint faction, int lineIndex, BuildingState camp)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(camp), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private bool LayBowLine(uint faction, int lineIndex, BuildingState camp, BuildingState fletcher)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var toFletcher = BeltRoute(faction, OutputCell(camp), taken, next => InFootprint(fletcher, next));
            if (toFletcher.cells == null) return false;
            foreach (int c in toFletcher.cells) taken[c] = true;
            var toCore = BeltRoute(faction, OutputCell(fletcher), taken, next => FeedsOwnCore(next, faction));
            if (toCore.cells == null) return false;
            SetLineBelts(lineIndex, toFletcher, toCore);
            return LayRoutes(faction, toFletcher, toCore);
        }

        /// <summary>Places the camp over the nearest remaining wood point, with deterministic footprint and port order.</summary>
        private uint PlaceLumberCamp(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.LumberCampSizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
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
                if (node.Definition.Kind != ResourceKind.Wood || node.Remaining <= 0) continue;
                int cell = world.Map.Cell(node.Definition.Position), nx = cell % width, nz = cell / width;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x0 = nx - dx, z0 = nz - dz;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!LumberCampSiteIsClear(origin, out uint nodeId) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            PlaceBuildingAt(faction, BuildingKind.LumberCamp, origin, side, nodeId);
                            return world.NextBuildingId - 1;
                        }
                    }
            }
            return 0;
        }

        /// <summary>Places the quarry over the nearest remaining stone point, with the same fixed search as a lumber camp.</summary>
        private uint PlaceQuarry(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.QuarrySizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
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
                if (node.Definition.Kind != ResourceKind.Stone || node.Remaining <= 0) continue;
                int cell = world.Map.Cell(node.Definition.Position), nx = cell % width, nz = cell / width;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x0 = nx - dx, z0 = nz - dz;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!QuarrySiteIsClear(origin, out uint nodeId) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            PlaceBuildingAt(faction, BuildingKind.Quarry, origin, side, nodeId);
                            return world.NextBuildingId - 1;
                        }
                    }
            }
            return 0;
        }

        private int OwnBuildingIndex(uint faction, BuildingKind kind)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && b.Kind == kind) return i;
            }
            return -1;
        }

        /// <summary>Ore points by distance to the core, then id; on each, the four footprints and the sides toward the core first.</summary>
        private uint PlaceMine(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.MineSizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            var order = new int[world.Nodes.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = DistanceSquared(world.Nodes[a].Definition.Position, core).CompareTo(DistanceSquared(world.Nodes[b].Definition.Position, core));
                return c != 0 ? c : a.CompareTo(b);
            });
            foreach (int n in order)
            {
                var node = world.Nodes[n];
                if (node.Definition.Kind != ResourceKind.Ore || node.Remaining <= 0) continue;
                int cell = world.Map.Cell(node.Definition.Position), nx = cell % width, nz = cell / width;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x0 = nx - dx, z0 = nz - dz;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!MineSiteIsClear(origin, out uint id) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            PlaceBuildingAt(faction, BuildingKind.Mine, origin, side, id);
                            return world.NextBuildingId - 1;
                        }
                    }
            }
            return 0;
        }

        /// <summary>Rings around the point halfway from the mine to the core, like the barracks search.</summary>
        private uint PlaceSmelter(uint faction, BuildingState mine)
        {
            var core = OwnCore(faction).Definition.Position;
            var from = FootprintCenter(mine.OriginCell, SizeOf(mine.Kind));
            var middle = new SimPoint(Fix64.FromRaw((from.X.Raw + core.X.Raw) / 2), Fix64.FromRaw((from.Z.Raw + core.Z.Raw) / 2));
            int size = world.Config.Economy.SmelterSizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int centre = world.Map.Cell(middle), cx = centre % width, cz = centre / width, coreCell = world.Map.Cell(core);
            for (int r = 0; r <= SiteSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!SiteIsClear(origin, coreCell, size) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            PlaceBuildingAt(faction, BuildingKind.Smelter, origin, side, 0);
                            return world.NextBuildingId - 1;
                        }
                    }
            return 0;
        }

        /// <summary>Places the charcoal kiln in the same deterministic rings as a barracks, around the nearest wood point.</summary>
        private uint PlaceKiln(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
            int bestNode = -1;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (node.Definition.Kind != ResourceKind.Wood || node.Remaining <= 0) continue;
                if (bestNode < 0 || DistanceSquared(node.Definition.Position, core) < DistanceSquared(world.Nodes[bestNode].Definition.Position, core)
                    || DistanceSquared(node.Definition.Position, core) == DistanceSquared(world.Nodes[bestNode].Definition.Position, core)
                    && node.Definition.Id < world.Nodes[bestNode].Definition.Id) bestNode = i;
            }
            if (bestNode < 0) return 0;
            int centre = world.Map.Cell(world.Nodes[bestNode].Definition.Position);
            int origin = FindProcessingSite(faction, centre, world.Config.Economy.CharcoalKilnSizeCells);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, world.Config.Economy.CharcoalKilnSizeCells), core))
            {
                if (!PortIsOpen(OutputCell(origin, world.Config.Economy.CharcoalKilnSizeCells, side), faction)) continue;
                PlaceBuildingAt(faction, BuildingKind.CharcoalKiln, origin, side, 0);
                return world.NextBuildingId - 1;
            }
            return 0;
        }

        /// <summary>Places the steelworks at the midpoint of the dedicated smelter, kiln and core.</summary>
        private uint PlaceSteelworks(uint faction, ProcessingLineState line)
        {
            var core = OwnCore(faction).Definition.Position;
            var smelter = world.Buildings[line.SmelterId - 1];
            var kiln = world.Buildings[line.KilnId - 1];
            var smelterPoint = FootprintCenter(smelter.OriginCell, SizeOf(smelter.Kind));
            var kilnPoint = FootprintCenter(kiln.OriginCell, SizeOf(kiln.Kind));
            var middle = new SimPoint(Fix64.FromRaw((smelterPoint.X.Raw + kilnPoint.X.Raw + core.X.Raw) / 3),
                Fix64.FromRaw((smelterPoint.Z.Raw + kilnPoint.Z.Raw + core.Z.Raw) / 3));
            int centre = world.Map.Cell(middle);
            int origin = FindProcessingSite(faction, centre, world.Config.Economy.SteelworksSizeCells);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, world.Config.Economy.SteelworksSizeCells), core))
            {
                if (!PortIsOpen(OutputCell(origin, world.Config.Economy.SteelworksSizeCells, side), faction)) continue;
                PlaceBuildingAt(faction, BuildingKind.Steelworks, origin, side, 0);
                return world.NextBuildingId - 1;
            }
            return 0;
        }

        private int FindProcessingSite(uint faction, int centre, int size)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int coreCell = world.Map.Cell(OwnCore(faction).Definition.Position), cx = centre % width, cz = centre / width;
            for (int r = 0; r <= SiteSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (SiteIsClear(origin, coreCell, size) && KeepsMapConnected(faction, origin, size)) return origin;
                    }
            return -1;
        }

        /// <summary>The four sides, the one pointing most toward <paramref name="to"/> first (ties in N, E, S, W order).</summary>
        private static Facing[] SidesToward(SimPoint from, SimPoint to)
        {
            long dx = (to.X.Raw - from.X.Raw) / 65536, dz = (to.Z.Raw - from.Z.Raw) / 65536;
            var sides = (Facing[])Sides.Clone();
            long Score(Facing f) => f == Facing.North ? dz : f == Facing.East ? dx : f == Facing.South ? -dz : -dx;
            Array.Sort(sides, (a, b) => { int c = Score(b).CompareTo(Score(a)); return c != 0 ? c : a.CompareTo(b); });
            return sides;
        }

        /// <summary>A cell a belt could start on: on the map, open, not a resource point, outside every core, no other side's belt.</summary>
        private bool PortIsOpen(int cell, uint faction)
            => cell >= 0 && world.Map.IsPassable(cell) && !IsNodeCell(cell) && !InsideAnyCore(cell)
               && (world.Belts[cell].FactionId == 0 || (world.Belts[cell].FactionId == faction && !world.Belts[cell].Held));

        private bool LayLineLegacy(uint faction, BuildingState mine, BuildingState smelter)
        {
            int cells = world.Belts.Length;
            var taken = new bool[cells];
            var toSmelter = BeltRoute(faction, OutputCell(mine), taken, next => InFootprint(smelter, next));
            if (toSmelter.cells == null) return false;
            foreach (int c in toSmelter.cells) taken[c] = true;
            var toCore = BeltRoute(faction, OutputCell(smelter), taken, next => FeedsOwnCore(next, faction));
            if (toCore.cells == null) return false;
            return LayRoutes(faction, toSmelter, toCore);
        }

        private bool LayCoreLine(uint faction, int lineIndex, BuildingState mine, BuildingState smelter)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var toSmelter = BeltRoute(faction, OutputCell(mine), taken, next => InFootprint(smelter, next));
            if (toSmelter.cells == null) return false;
            foreach (int c in toSmelter.cells) taken[c] = true;
            var toCore = BeltRoute(faction, OutputCell(smelter), taken, next => FeedsOwnCore(next, faction));
            if (toCore.cells == null) return false;
            SetLineBelts(lineIndex, toSmelter, toCore);
            return LayRoutes(faction, toSmelter, toCore);
        }

        private bool LaySteelLine(uint faction, int lineIndex, BuildingState mine, BuildingState smelter, BuildingState kiln, BuildingState steelworks)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var toSmelter = BeltRoute(faction, OutputCell(mine), taken, next => InFootprint(smelter, next));
            if (toSmelter.cells == null) return false;
            foreach (int c in toSmelter.cells) taken[c] = true;
            var metal = BeltRoute(faction, OutputCell(smelter), taken, next => InFootprint(steelworks, next));
            if (metal.cells == null) return false;
            foreach (int c in metal.cells) taken[c] = true;
            Facing metalSide = metal.facings[metal.facings.Length - 1];
            var charcoal = BeltRoute(faction, OutputCell(kiln), taken,
                (from, next) => InFootprint(steelworks, next) && FacingFrom(from, next) != metalSide);
            if (charcoal.cells == null) return false;
            foreach (int c in charcoal.cells) taken[c] = true;
            var steel = BeltRoute(faction, OutputCell(steelworks), taken, next => FeedsOwnCore(next, faction));
            if (steel.cells == null) return false;
            SetLineBelts(lineIndex, toSmelter, metal, charcoal, steel);
            return LayRoutes(faction, toSmelter, metal, charcoal, steel);
        }

        private bool[] TakenForOtherLines(uint faction, int currentLine)
        {
            var taken = new bool[world.Belts.Length];
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                if (i == currentLine || world.ProcessingLines[i].FactionId != faction) continue;
                foreach (int cell in world.ProcessingLines[i].BeltCells)
                    if (cell >= 0 && cell < taken.Length && world.Belts[cell].FactionId == faction) taken[cell] = true;
            }
            return taken;
        }

        private bool LayRoutes(uint faction, params (int[] cells, Facing[] facings)[] routes)
        {
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0;
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) owned++;
            bool whole = true;
            foreach (var (route, facings) in routes)
                for (int i = 0; i < route.Length; i++)
                {
                    ref var belt = ref world.Belts[route[i]];
                    if (belt.FactionId == faction) { if (belt.Facing != facings[i]) whole = false; continue; }
                    whole = false;
                    if (owned >= rules.BeltLimit || economy.Wood < rules.BeltWoodCost) continue;
                    economy.Wood = checked(economy.Wood - rules.BeltWoodCost);
                    belt = new BeltState { FactionId = faction, Facing = facings[i], Hp = rules.BeltHp };
                    owned++;
                    world.BeltOrder = null;
                }
            return whole;
        }

        private Facing FacingFrom(int from, int to)
        {
            int width = world.Config.Map.WidthCells;
            return to == from + width ? Facing.North : to == from + 1 ? Facing.East : to == from - width ? Facing.South : Facing.West;
        }

        private bool InFootprint(BuildingState b, int cell)
        {
            int width = world.Config.Map.WidthCells, size = SizeOf(b.Kind), x0 = b.OriginCell % width, z0 = b.OriginCell / width;
            int x = cell % width, z = cell / width;
            return x >= x0 && x < x0 + size && z >= z0 && z < z0 + size;
        }

        /// <summary>Breadth-first from <paramref name="start"/> in N, E, S, W order; null when no route exists.</summary>
        private (int[] cells, Facing[] facings) BeltRoute(uint faction, int start, bool[] taken, Func<int, bool> into)
            => BeltRoute(faction, start, taken, (from, next) => into(next));

        private (int[] cells, Facing[] facings) BeltRoute(uint faction, int start, bool[] taken, Func<int, int, bool> into)
        {
            if (!PortIsOpen(start, faction) || taken[start]) return (null, null);
            int cells = world.Belts.Length;
            var from = new int[cells];
            for (int i = 0; i < cells; i++) from[i] = -2;
            var queue = new int[cells];
            int head = 0, tail = 0;
            queue[tail++] = start; from[start] = -1;
            while (head < tail)
            {
                int cell = queue[head++];
                foreach (var side in Sides)
                {
                    int next = BeltNext(cell, side);
                    if (next < 0) continue;
                    if (into(cell, next))
                    {
                        int length = 0;
                        for (int c = cell; c != -1; c = from[c]) length++;
                        var path = new int[length];
                        var facings = new Facing[length];
                        int k = length - 1;
                        for (int c = cell; c != -1; c = from[c]) path[k--] = c;
                        for (int i = 0; i + 1 < length; i++)
                            foreach (var d in Sides) if (BeltNext(path[i], d) == path[i + 1]) { facings[i] = d; break; }
                        facings[length - 1] = side;
                        return (path, facings);
                    }
                    if (from[next] != -2 || taken[next] || !PortIsOpen(next, faction)) continue;
                    from[next] = cell;
                    queue[tail++] = next;
                }
            }
            return (null, null);
        }

        private const int FarmTarget = 2, FarmSearchRadiusCells = 12;

        /// <summary>
        /// V3-4 agrarian (technical-design-v3 29): up to FarmTarget farms on the quickest ground near the core, each with
        /// a belt line into the core; until a farm's line is whole, one villager carries its food by hand. A farm the
        /// player placed or runs is left to them.
        /// </summary>
        private void DecideFarms(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int farms = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Farm) farms++;
            if (farms < FarmTarget && economy.Wood >= world.Config.Economy.FarmWoodCost && PlaceFarm(faction)) return;
            var taken = new bool[world.Belts.Length];
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.FactionId != faction || b.Kind != BuildingKind.Farm || !b.Complete || b.Held) continue;
                var route = BeltRoute(faction, OutputCell(b), taken, next => FeedsOwnCore(next, faction));
                bool whole = false;
                if (route.cells != null)
                {
                    foreach (int c in route.cells) taken[c] = true;
                    whole = LayBelts(faction, route.cells, route.facings);
                }
                SetFarmHauler(faction, b.Id, whole ? 0 : 1);
            }
        }

        /// <summary>Clear footprints in rings around the core; the quickest ground wins (then the ring order).</summary>
        private bool PlaceFarm(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.FarmSizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int coreCell = world.Map.Cell(core), cx = coreCell % width, cz = coreCell / width;
            var candidates = new System.Collections.Generic.List<(int interval, int order, int origin, Facing side)>();
            int order = 0;
            for (int r = 2; r <= FarmSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!SiteIsClear(origin, coreCell, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            candidates.Add((FarmInterval(origin), order++, origin, side));
                            break;
                        }
                    }
            candidates.Sort((a, b) => a.interval != b.interval ? a.interval.CompareTo(b.interval) : a.order.CompareTo(b.order));
            foreach (var c in candidates)
            {
                if (!KeepsMapConnected(faction, c.origin, size)) continue;
                PlaceBuildingAt(faction, BuildingKind.Farm, c.origin, c.side, 0);
                return true;
            }
            return false;
        }

        /// <summary>Lays the missing belts of one route while wood lasts; true when every cell carries an own belt along it.</summary>
        private bool LayBelts(uint faction, int[] route, Facing[] facings)
        {
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0;
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) owned++;
            bool whole = true;
            for (int i = 0; i < route.Length; i++)
            {
                ref var belt = ref world.Belts[route[i]];
                if (belt.FactionId == faction) { if (belt.Facing != facings[i]) whole = false; continue; }
                whole = false;
                if (owned >= rules.BeltLimit || economy.Wood < rules.BeltWoodCost) continue;
                economy.Wood = checked(economy.Wood - rules.BeltWoodCost);
                belt = new BeltState { FactionId = faction, Facing = facings[i], Hp = rules.BeltHp };
                owned++;
                world.BeltOrder = null;
            }
            return whole;
        }

        /// <summary>One carrier per farm while its line is not whole (the nearest gatherer or idle villager), none after.</summary>
        private void SetFarmHauler(uint faction, uint farm, int wanted)
        {
            int current = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || v.HaulFrom != farm) continue;
                if (wanted == 0) StopHauling(ref v); else current++;
            }
            if (current >= wanted) return;
            var spot = world.Map.Center(world.Buildings[farm - 1].WorkCell);
            int best = -1;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || v.HaulFrom != 0 || v.Carry > 0
                    || (v.Task != VillagerTask.Idle && v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering)) continue;
                if (best < 0 || DistanceSquared(v.Position, spot) < DistanceSquared(world.Villagers[best].Position, spot)) best = i;
            }
            if (best < 0) return;
            ref var chosen = ref world.Villagers[best];
            chosen.NodeId = 0;
            chosen.HaulFrom = farm;
            chosen.Task = VillagerTask.ToPickup;
        }

        /// <summary>
        /// Keeps <paramref name="wanted"/> villagers carrying by hand: the first from the mine, the second from the smelter.
        /// New carriers are the nearest to the mine among those gathering or idle (distance, then id); with none wanted,
        /// every carrier of this line stops.
        /// </summary>
        private void SetHaulers(uint faction, uint mine, uint smelter, int wanted)
        {
            int fromMine = 0, fromSmelter = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || (v.HaulFrom != mine && v.HaulFrom != smelter)) continue;
                if (wanted == 0) StopHauling(ref v);
                else if (v.HaulFrom == mine) fromMine++;
                else fromSmelter++;
            }
            var spot = world.Map.Center(world.Buildings[mine - 1].WorkCell);
            int wantMine = (wanted + 1) / 2, wantSmelter = wanted / 2;
            while (fromMine < wantMine || fromSmelter < wantSmelter)
            {
                int best = -1;
                for (int i = 0; i < world.VillagerCount; i++)
                {
                    var v = world.Villagers[i];
                    if (!v.Alive || v.FactionId != faction || v.Held || v.HaulFrom != 0 || v.Carry > 0
                        || (v.Task != VillagerTask.Idle && v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering)) continue;
                    if (best < 0 || DistanceSquared(v.Position, spot) < DistanceSquared(world.Villagers[best].Position, spot)) best = i;
                }
                if (best < 0) return;
                ref var chosen = ref world.Villagers[best];
                chosen.NodeId = 0;
                bool toMine = fromMine < wantMine;
                chosen.HaulFrom = toMine ? mine : smelter;
                chosen.Task = VillagerTask.ToPickup;
                if (toMine) fromMine++; else fromSmelter++;
            }
        }
    }
}
