using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private enum FluidFailureKind : byte { Site, Water, Route, Wood }

        private int[] fluidPipeOwner;
        private int[] fluidQueue;
        private int[] fluidPathParent, fluidPathQueue, fluidPathSeen;
        private int fluidPathStamp;
        private int[] fluidPipeSeen, fluidNetworkAtCell, fluidOldNetworkAtCell, fluidOverlapCounts;
        private int[] fluidOverlapTargets, fluidOverlapTargetSeen;
        private int fluidOverlapTargetStamp;
        private int fluidPipeStamp;
        private int[] fluidBuildingOccupancy;
        private bool fluidBuildingOccupancyDirty = true;
        private int[] fluidLiquidBuildings;
        private int fluidLiquidBuildingCount;
        private readonly bool[] fluidDecisionPending = new bool[2];

        private bool IsFluidBuilding(BuildingKind kind)
            => kind == BuildingKind.FluidTank || kind == BuildingKind.WaterPump
                || kind == BuildingKind.Well || kind == BuildingKind.Brewery;

        private bool IsFluidNetworkBuilding(BuildingKind kind)
            => IsFluidBuilding(kind) || kind == BuildingKind.Smelter || kind == BuildingKind.BlastFurnace
                || kind == BuildingKind.GlassWorks || kind == BuildingKind.BrickKiln;

        private void EnsureFluidBuildingOccupancy()
        {
            int cells = checked(world.Config.Map.WidthCells * world.Config.Map.HeightCells);
            if (fluidBuildingOccupancy == null || fluidBuildingOccupancy.Length != cells)
                fluidBuildingOccupancy = new int[cells];
            if (!fluidBuildingOccupancyDirty) return;
            Array.Fill(fluidBuildingOccupancy, -1);
            int width = world.Config.Map.WidthCells;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!building.Alive || building.OriginCell < 0) continue;
                if (building.Kind == BuildingKind.Bridge)
                {
                    if (building.BridgeCells == null) continue;
                    for (int j = 0; j < building.BridgeCells.Length; j++) fluidBuildingOccupancy[building.BridgeCells[j]] = i;
                    continue;
                }
                int size = SizeOf(building.Kind), x0 = building.OriginCell % width, z0 = building.OriginCell / width;
                for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
                    fluidBuildingOccupancy[building.OriginCell + z * width + x] = i;
            }
            fluidBuildingOccupancyDirty = false;
        }

        private void EnsureFluidPipeOwner()
        {
            int cells = checked(world.Config.Map.WidthCells * world.Config.Map.HeightCells);
            if (fluidPipeOwner == null || fluidPipeOwner.Length != cells)
            {
                fluidPipeOwner = new int[cells];
                Array.Fill(fluidPipeOwner, -1);
                for (int cell = 0; cell < cells; cell++)
                    if (world.Pipes[cell].FactionId != 0) fluidPipeOwner[cell] = (int)world.Pipes[cell].FactionId;
            }
        }

        private IReadOnlyList<FluidNetworkView> FluidNetworkViewsFor(uint faction)
        {
            var result = new List<FluidNetworkView>();
            if (!FluidsOn) return result;
            for (int i = 0; i < world.FluidNetworks.Length; i++)
            {
                var n = world.FluidNetworks[i];
                if (n.FactionId == faction) result.Add(new FluidNetworkView(n.Number, n.Kind, n.Amount, n.Capacity,
                    n.PipeCells == null ? 0 : n.PipeCells.Length, n.PipeCells ?? Array.Empty<int>()));
            }
            return result;
        }

        private void MarkFluidsDirty()
        {
            if (FluidsOn) world.FluidsDirty = true;
        }

        private bool IsFluidRouteCell(int cell, uint faction)
        {
            if (cell < 0 || cell >= world.Config.Map.WidthCells * world.Config.Map.HeightCells || IsRiverCell(cell)) return false;
            if (FluidPipeAt(cell, faction)) return true;
            if (!world.Map.IsPassable(cell)) return false;
            if (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) return false;
            EnsureFluidBuildingOccupancy();
            return fluidBuildingOccupancy[cell] < 0;
        }

        private bool FluidPipeAtAny(int cell)
        {
            if (cell < 0 || cell >= world.Config.Map.WidthCells * world.Config.Map.HeightCells) return false;
            EnsureFluidPipeOwner();
            return fluidPipeOwner[cell] > 0;
        }

        private bool AppendFluidRouteCell(List<int> cells, int cell, uint faction)
        {
            if (!IsFluidRouteCell(cell, faction)) return false;
            if (cells.Contains(cell)) return true;
            if (!FluidPipeAt(cell, faction)) cells.Add(cell);
            return true;
        }

        private bool TryPlaceFluidRoute(uint faction, int sourceOrigin, int sourceSize, int targetOrigin, int targetSize)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int[] source = { sourceOrigin - 1, sourceOrigin + sourceSize, sourceOrigin - width, sourceOrigin + sourceSize * width };
            int[] target = { targetOrigin - 1, targetOrigin + targetSize, targetOrigin - width, targetOrigin + targetSize * width };
            int cellsCount = checked(width * height);
            if (fluidPathParent == null || fluidPathParent.Length != cellsCount)
            {
                fluidPathParent = new int[cellsCount]; fluidPathQueue = new int[cellsCount]; fluidPathSeen = new int[cellsCount]; fluidPathStamp = 0;
            }
            int stamp = checked(++fluidPathStamp), head = 0, tail = 0, goal = -1;
            for (int si = 0; si < source.Length; si++)
            {
                int start = source[si];
                if (start < 0 || start >= cellsCount || fluidPathSeen[start] == stamp || !IsFluidRouteCell(start, faction)) continue;
                fluidPathQueue[tail++] = start; fluidPathSeen[start] = stamp; fluidPathParent[start] = -1;
            }
            int[] next = new int[4];
            while (head < tail && goal < 0)
            {
                int cell = fluidPathQueue[head++], x = cell % width;
                if (IsFluidRouteTarget(cell, target, faction)) { goal = cell; break; }
                next[0] = cell - width; next[1] = x + 1 < width ? cell + 1 : -1;
                next[2] = cell + width; next[3] = x > 0 ? cell - 1 : -1;
                for (int ni = 0; ni < next.Length; ni++)
                {
                    int candidate = next[ni];
                    if (candidate < 0 || candidate >= cellsCount || fluidPathSeen[candidate] == stamp || !IsFluidRouteCell(candidate, faction)) continue;
                    fluidPathSeen[candidate] = stamp; fluidPathParent[candidate] = cell; fluidPathQueue[tail++] = candidate;
                }
            }
            if (goal < 0) return false;
                var cells = new List<int>();
                for (int cell = goal; cell >= 0; cell = fluidPathParent[cell]) AppendFluidRouteCell(cells, cell, faction);
                cells.Reverse();
                if (cells.Count == 0) return false;
                ApplyFluidCommand(faction, EconomyCommand.PlacePipe(faction, (ulong)(world.Tick + cells[0] + 1), cells.ToArray()));
                if (FluidPipeAtAny(cells[0])) return true;
            // A newly placed pipe is an obstacle to the movement path cache. If the fluid-only search started on an
            // existing pipe and the cached terrain cut through that pipe, retry from an open port with the shared map
            // pathfinder. This is still bounded by the few building-edge ports, not a map-wide scan per tick.
            for (int si = 0; si < source.Length; si++)
            {
                if (!IsFluidRouteCell(source[si], faction) || !world.Map.IsPassable(source[si])) continue;
                for (int ti = 0; ti < target.Length; ti++)
                {
                    if (!IsFluidRouteCell(target[ti], faction) || !world.Map.IsPassable(target[ti])) continue;
                    var path = world.Map.FindPath(source[si], world.Map.Center(target[ti]), faction);
                    if (path.Length == 0) continue;
                    var fallback = new List<int>(path.Length);
                    for (int pi = 0; pi < path.Length; pi++) AppendFluidRouteCell(fallback, path[pi], faction);
                    if (fallback.Count == 0) continue;
                    ApplyFluidCommand(faction, EconomyCommand.PlacePipe(faction, (ulong)(world.Tick + fallback[0] + 1), fallback.ToArray()));
                    if (FluidPipeAtAny(fallback[0])) return true;
                }
            }
            return false;
        }

        private bool IsFluidRouteTarget(int cell, int[] targets, uint faction)
        {
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] == cell) return IsFluidRouteCell(cell, faction);
            return false;
        }

        private void DecideFluids(uint faction)
        {
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            bool hasCoolingBuilding = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && IsCoolingBuilding(b.Kind)) { hasCoolingBuilding = true; break; }
            }
            int pumps = CountFluidSourceBuildings(faction, BuildingKind.WaterPump), wells = CountFluidSourceBuildings(faction, BuildingKind.Well);
            int sourceKind = pumps > 0 ? 1 : wells > 0 ? 2 : FindAiPumpSite(faction) >= 0 ? 1 : 2;
            int breweries = CountPlannedBuildings(faction, BuildingKind.Brewery);
            bool breweryPreparation = economy.Age >= 2 && (!AgeClockOn || world.Tick >= Math.Max(0, AgeClockTickForAge(2) - 1200));
            int desiredBreweries = economy.Age < 2 ? 0 : breweryPreparation
                ? Math.Min(rules.FluidAiBreweryMaxLines, (LivelihoodNeed(faction, ResourceKind.Ale, LivingVillagers(faction)) * 5 + 59) / 60)
                : Math.Min(1, rules.FluidAiBreweryMaxLines);
            int completedBreweries = CountBuildings(faction, BuildingKind.Brewery);
            int waterNeed = checked(completedBreweries * 75 + ConnectedCoolingWaterPerMinute(faction));
            int completePumps = CountBuildings(faction, BuildingKind.WaterPump), completeWells = CountBuildings(faction, BuildingKind.Well);
            int waterSupply = checked(completePumps * (1200 / Math.Max(1, rules.FluidPumpTicksPerWater))
                + completeWells * (1200 / Math.Max(1, rules.FluidWellTicksPerWater)));
            if (waterNeed > waterSupply || (pumps + wells == 0 && (desiredBreweries > 0 || hasCoolingBuilding)))
            {
                if (sourceKind == 1 && pumps < rules.FluidAiPumpMaxBuildings)
                    TryAiFluidPlacement(faction, BuildingKind.WaterPump, 1, true);
                else if (sourceKind == 2 && wells < rules.FluidAiWellMaxBuildings)
                    TryAiFluidPlacement(faction, BuildingKind.Well, 2, false);
                else RecordFluidFailure(faction, FluidFailureKind.Water);
            }
            int source = -1, sourceSize = 0, sourceNetwork = -1;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && (b.Kind == BuildingKind.WaterPump || b.Kind == BuildingKind.Well))
                { source = b.OriginCell; sourceSize = SizeOf(b.Kind); break; }
            }
            if (source < 0) return;
            EnsureAiSourcePipe(faction, source, sourceSize);
            if (world.FluidsDirty) RebuildFluidNetworks();
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && b.OriginCell == source
                    && (b.Kind == BuildingKind.WaterPump || b.Kind == BuildingKind.Well)) { sourceNetwork = b.FluidNetworkId; break; }
            }
            if (breweries < desiredBreweries)
            {
                if (!TryAiFluidPlacementNearNetwork(faction, BuildingKind.Brewery, 2, sourceNetwork)
                    && !TryAiFluidPlacementNearSource(faction, BuildingKind.Brewery, 2, source, sourceSize))
                    RecordFluidFailure(faction, FluidFailureKind.Site);
            }
            int routes = 0;
            int coolingRoutes = 0;
            for (int targetPass = 0; targetPass < 2 && routes < rules.FluidAiPipeRoutesPerDecision; targetPass++)
            for (int i = 0; i < world.BuildingCount && routes < rules.FluidAiPipeRoutesPerDecision; i++)
            {
                var b = world.Buildings[i];
                bool brewery = b.Kind == BuildingKind.Brewery;
                bool cooling = b.Kind == BuildingKind.Smelter || b.Kind == BuildingKind.BlastFurnace
                    || b.Kind == BuildingKind.GlassWorks || b.Kind == BuildingKind.BrickKiln;
                if (!b.Alive || b.FactionId != faction || targetPass == 0 && !brewery || targetPass == 1 && !cooling) continue;
                if (targetPass == 1 && coolingRoutes > 0) continue;
                if (b.FluidNetworkId >= 0 && b.FluidNetworkId == sourceNetwork) continue;
                if (TryPlaceFluidRoute(faction, source, sourceSize, b.OriginCell, SizeOf(b.Kind)))
                {
                    routes++;
                    if (targetPass == 1) coolingRoutes++;
                }
                else if (targetPass == 1 && TryAiCoolingWell(faction, b.OriginCell, SizeOf(b.Kind)))
                {
                    routes++;
                    coolingRoutes++;
                }
                else RecordFluidFailure(faction, FluidFailureKind.Route);
            }
        }

        private bool TryAiCoolingWell(uint faction, int targetOrigin, int targetSize)
        {
            if (CountFluidSourceBuildings(faction, BuildingKind.Well) >= world.Config.Economy.FluidAiWellMaxBuildings) return false;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int tx = targetOrigin % width, tz = targetOrigin / width;
            for (int radius = 0; radius <= 12; radius++)
            {
                for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dz) != radius) continue;
                    int x = tx + dx, z = tz + dz;
                    if (x < 0 || z < 0 || x + 2 > width || z + 2 > height) continue;
                    int origin = z * width + x;
                    if (!FluidSiteIsClear(origin, world.Map.Cell(OwnCore(faction).Definition.Position), 2)) continue;
                    if (CountFluidSourceBuildings(faction, BuildingKind.Well) >= world.Config.Economy.FluidAiWellMaxBuildings) return false;
                    int before = CountFluidSourceBuildings(faction, BuildingKind.Well);
                    ApplyEconomyCommand(EconomyCommand.Place(faction, (ulong)(world.Tick + origin + 1), BuildingKind.Well, origin));
                    if (CountFluidSourceBuildings(faction, BuildingKind.Well) <= before) continue;
                    for (int i = 0; i < world.BuildingCount; i++)
                    {
                        var well = world.Buildings[i];
                        if (!well.Alive || well.FactionId != faction || well.Kind != BuildingKind.Well || well.OriginCell != origin) continue;
                        EnsureAiSourcePipe(faction, origin, 2);
                        return TryPlaceFluidRoute(faction, origin, 2, targetOrigin, targetSize);
                    }
                }
            }
            return false;
        }

        private int CountFluidSourceBuildings(uint faction, BuildingKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == kind) count++;
            return count;
        }

        private int FindAiPumpSite(uint faction)
        {
            int width = world.Config.Map.WidthCells, cells = checked(width * world.Config.Map.HeightCells);
            int core = world.Map.Cell(OwnCore(faction).Definition.Position), best = -1, bestDistance = int.MaxValue;
            for (int cell = 0; cell < cells; cell++)
            {
                int distance = Chebyshev(cell, core);
                if (distance > 40 || distance > bestDistance || !TouchesRiver(cell, 1)) continue;
                if (!FluidSiteIsClear(cell, core, 1) || !AiSourcePipeSiteAvailable(cell, 1)) continue;
                if (distance < bestDistance || best < 0) { best = cell; bestDistance = distance; }
            }
            return best;
        }

        private int ConnectedCoolingWaterPerMinute(uint faction)
        {
            int result = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && IsCoolingBuilding(b.Kind) && b.FluidNetworkId >= 0)
                    result = checked(result + 15);
            }
            return result;
        }

        private static bool IsCoolingBuilding(BuildingKind kind)
            => kind == BuildingKind.Smelter || kind == BuildingKind.BlastFurnace
                || kind == BuildingKind.GlassWorks || kind == BuildingKind.BrickKiln;

        private void RecordFluidFailure(uint faction, FluidFailureKind kind)
        {
            ref var economy = ref world.Economies[faction - 1];
            if (kind == FluidFailureKind.Site) economy.FluidSiteFailures++;
            else if (kind == FluidFailureKind.Water) economy.FluidWaterFailures++;
            else if (kind == FluidFailureKind.Route) economy.FluidRouteFailures++;
            else economy.FluidWoodFailures++;
        }

        private bool FluidSiteIsClear(int origin, int core, int size)
        {
            int clearance = world.Config.Economy.AdjacentBuildings
                ? world.Config.Economy.AdjacentBuildingClearanceCells : BuildingClearanceCells;
            return SiteIsClear(origin, core, size, clearance);
        }

        private void EnsureAiSourcePipe(uint faction, int source, int sourceSize)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int[] adjacent = { source - 1, source + sourceSize, source - width, source + sourceSize * width };
            EnsureFluidBuildingOccupancy();
            for (int i = 0; i < adjacent.Length; i++)
            {
                int cell = adjacent[i];
                if (cell < 0 || cell >= width * height || IsRiverCell(cell) || !world.Map.IsPassable(cell)
                    || world.Belts.Length != 0 && world.Belts[cell].FactionId != 0 || fluidBuildingOccupancy[cell] >= 0) continue;
                ApplyFluidCommand(faction, EconomyCommand.PlacePipe(faction, (ulong)(world.Tick + cell + 1), new[] { cell }));
                if (FluidPipeAt(cell, faction)) return;
            }
        }

        private bool AiSourcePipeSiteAvailable(int source, int sourceSize)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int[] adjacent = { source - 1, source + sourceSize, source - width, source + sourceSize * width };
            EnsureFluidBuildingOccupancy();
            for (int i = 0; i < adjacent.Length; i++)
            {
                int cell = adjacent[i];
                if (cell < 0 || cell >= width * height || IsRiverCell(cell) || !world.Map.IsPassable(cell)
                    || world.Belts.Length != 0 && world.Belts[cell].FactionId != 0 || fluidBuildingOccupancy[cell] >= 0
                    || IsReservedBuildingCell(cell) || IsNodeCell(cell) || InsideAnyCore(cell)) continue;
                return true;
            }
            return false;
        }

        private bool TryAiFluidPlacementNearNetwork(uint faction, BuildingKind kind, int size, int networkId)
        {
            if (networkId < 0) return false;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            for (int cell = 0; cell < world.Pipes.Length; cell++)
            {
                if (!FluidPipeAt(cell, faction) || fluidNetworkAtCell[cell] != networkId) continue;
                int px = cell % width, pz = cell / width;
                for (int z = pz - size; z <= pz + 1; z++) for (int x = px - size; x <= px + 1; x++)
                {
                    if (x < 0 || z < 0 || x + size > width || z + size > height) continue;
                    int origin = z * width + x;
                    if (!FluidSiteIsClear(origin, world.Map.Cell(OwnCore(faction).Definition.Position), size)
                        || !FluidAiSiteTouchesPipe(faction, origin, size)) continue;
                    int before = CountPlannedBuildings(faction, kind);
                    ApplyEconomyCommand(EconomyCommand.Place(faction, (ulong)(world.Tick + origin + 1), kind, origin));
                    if (CountPlannedBuildings(faction, kind) > before) return true;
                }
            }
            return false;
        }

        private bool TryAiFluidPlacementNearSource(uint faction, BuildingKind kind, int size, int source, int sourceSize)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int sx = source % width, sz = source / width;
            for (int radius = 1; radius <= 30; radius++)
            {
                for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dz) != radius) continue;
                    int x = sx + dx, z = sz + dz;
                    if (x < 0 || z < 0 || x + size > width || z + size > height) continue;
                    int origin = z * width + x;
                    if (!FluidSiteIsClear(origin, world.Map.Cell(OwnCore(faction).Definition.Position), size)
                        || !FluidAiSiteTouchesPipe(faction, origin, size)) continue;
                    int before = CountPlannedBuildings(faction, kind);
                    ApplyEconomyCommand(EconomyCommand.Place(faction, (ulong)(world.Tick + origin + 1), kind, origin));
                    if (CountPlannedBuildings(faction, kind) > before) return true;
                }
            }
            return false;
        }

        private bool FluidAiSiteTouchesPipe(uint faction, int origin, int size)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int x0 = origin % width, z0 = origin / width;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int cell = origin + z * width + x;
                if (x == 0 && x0 > 0 && FluidPipeAt(cell - 1, faction) || x + 1 == size && x0 + size < width && FluidPipeAt(cell + 1, faction)
                    || z == 0 && z0 > 0 && FluidPipeAt(cell - width, faction) || z + 1 == size && z0 + size < height && FluidPipeAt(cell + width, faction)) return true;
            }
            return false;
        }

        private void TryAiFluidPlacement(uint faction, BuildingKind kind, int size, bool river)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position);
            int cx = core % width, cz = core / width;
            int maxRadius = river ? 40 : 120;
            for (int radius = 0; radius <= maxRadius; radius++)
            {
                for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dz) != radius) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x + size > width || z + size > height) continue;
                    int origin = z * width + x;
                    if (river && !TouchesRiver(origin, size)) continue;
                    if (river && !AiSourcePipeSiteAvailable(origin, size)) continue;
                    if (!FluidSiteIsClear(origin, core, size)) continue;
                    var command = EconomyCommand.Place(faction, (ulong)(world.Tick + radius + 1), kind, origin);
                    int beforeCount = CountPlannedBuildings(faction, kind);
                    ApplyEconomyCommand(command);
                    if (CountPlannedBuildings(faction, kind) > beforeCount) return;
                }
            }
            RecordFluidFailure(faction, FluidFailureKind.Site);
        }

        private void ApplyFluidCommand(uint faction, EconomyCommand command)
        {
            if (command.Kind == EconomyCommandKind.RemovePipe)
            {
                if (command.Cell < 0 || command.Cell >= world.Pipes.Length) return;
                if (world.Pipes[command.Cell].FactionId != faction) return;
                world.Pipes[command.Cell] = default;
                EnsureFluidPipeOwner();
                fluidPipeOwner[command.Cell] = -1;
                MarkFluidsDirty();
                return;
            }
            var cells = command.Cells;
            if (cells == null || cells.Count == 0) return;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            EnsureFluidPipeOwner();
            EnsureFluidBuildingOccupancy();
            var e = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            bool placedAny = false;
            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                // Placed cells are caught by FluidPipeAtAny, so no map-sized "seen" array per command (IL2CPP garbage).
                if (cell < 0 || cell >= width * height || IsRiverCell(cell) || FluidPipeAtAny(cell)) continue;
                if (!world.Map.IsPassable(cell) || world.Belts.Length != 0 && world.Belts[cell].FactionId != 0
                    || IsReservedBuildingCell(cell) || fluidBuildingOccupancy[cell] >= 0 || IsNodeCell(cell) || InsideAnyCore(cell))
                {
                    if (automaticEconomyAction) RecordFluidFailure(faction, FluidFailureKind.Site);
                    continue;
                }
                if (economy.Wood < e.FluidPipeWoodCost || economy.Stone < e.FluidPipeStoneCost)
                {
                    if (automaticEconomyAction) RecordFluidFailure(faction, FluidFailureKind.Wood);
                    break;
                }
                economy.Wood = checked(economy.Wood - e.FluidPipeWoodCost);
                economy.Stone = checked(economy.Stone - e.FluidPipeStoneCost);
                RecordLargeUse(faction, ResourceKind.Wood, LargeEconomyUseKind.Belt, e.FluidPipeWoodCost);
                RecordLargeUse(faction, ResourceKind.Stone, LargeEconomyUseKind.Belt, e.FluidPipeStoneCost);
                world.Pipes[cell] = new PipeState { FactionId = faction, Hp = e.FluidPipeHp, Held = !automaticEconomyAction };
                fluidPipeOwner[cell] = (int)faction;
                if (automaticEconomyAction) economy.FluidPipeCells++;
                placedAny = true;
            }
            if (placedAny) MarkFluidsDirty();
        }

        private bool TouchesRiver(int origin, int size)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int x0 = origin % width, z0 = origin / width;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int cell = origin + z * width + x;
                if (x == 0 && x0 > 0 && IsRiverCell(cell - 1) || x + 1 == size && x0 + size < width && IsRiverCell(cell + 1)
                    || z == 0 && z0 > 0 && IsRiverCell(cell - width) || z + 1 == size && z0 + size < height && IsRiverCell(cell + width)) return true;
            }
            return false;
        }

        private void AdvanceFluids()
        {
            if (!FluidsOn) return;
            if (world.FluidsDirty) RebuildFluidNetworks();
            for (int li = 0; li < fluidLiquidBuildingCount; li++)
            {
                int i = fluidLiquidBuildings[li];
                ref var b = ref world.Buildings[i];
                int network = FindFluidNetwork(b.FactionId, b.FluidNetworkId);
                if (network < 0) continue;
                ref var n = ref world.FluidNetworks[network];
                if (b.Kind == BuildingKind.WaterPump && world.Tick % world.Config.Economy.FluidPumpTicksPerWater == 0)
                    AddFluid(ref n, FluidKind.Water, 1, b.FactionId);
                else if (b.Kind == BuildingKind.Well && world.Tick % world.Config.Economy.FluidWellTicksPerWater == 0)
                    AddFluid(ref n, FluidKind.Water, 1, b.FactionId);
                else if (b.Kind == BuildingKind.Brewery)
                    AdvanceBrewery(ref b, ref n);
            }
        }

        private void AdvanceBrewery(ref BuildingState b, ref FluidNetworkState n)
        {
            ref var economy = ref world.Economies[b.FactionId - 1];
            var e = world.Config.Economy;
            if (b.Timer > 0) { b.Timer--; if (b.Timer == 0) { b.Input = Math.Max(0, b.Input - e.FluidBreweryFoodInput); b.Output++; economy.Ale++; economy.AleProduced++; } return; }
            if (b.Output >= e.FluidBreweryOutputCapacity) return;
            bool carriedFood = CarriedInputsOn;
            if (carriedFood ? b.Input < e.FluidBreweryFoodInput : economy.Food < e.FluidBreweryFoodInput)
            {
                if (AiReasonsOn && !b.Held) { b.AiReason = AiReason.MaterialFor; b.AiReasonValue = (int)ResourceKind.Food; }
                return;
            }
            if (n.Kind != FluidKind.Water)
            {
                if (AiReasonsOn && !b.Held) { b.AiReason = n.Kind == FluidKind.None ? AiReason.FluidWaterShortage : AiReason.FluidWrongLiquid; b.AiReasonValue = 0; }
                return;
            }
            if (n.Amount < e.FluidBreweryWaterInput)
            {
                if (AiReasonsOn && !b.Held) { b.AiReason = AiReason.FluidWaterShortage; b.AiReasonValue = n.Amount; }
                return;
            }
            if (!carriedFood) economy.Food -= e.FluidBreweryFoodInput;
            n.Amount -= e.FluidBreweryWaterInput;
            if (n.Amount == 0) n.Kind = FluidKind.None;
            if (!carriedFood) b.Input += e.FluidBreweryFoodInput;
            b.Timer = e.FluidBreweryTicks;
            if (AiReasonsOn && !b.Held) { b.AiReason = AiReason.None; b.AiReasonValue = 0; }
        }

        private void AddFluid(ref FluidNetworkState network, FluidKind kind, int amount, uint faction)
        {
            if (network.Kind != FluidKind.None && network.Kind != kind) return;
            network.Kind = kind;
            int add = Math.Min(amount, Math.Max(0, network.Capacity - network.Amount));
            network.Amount += add;
            if (add > 0 && kind == FluidKind.Water) world.Economies[faction - 1].WaterProduced += add;
        }

        private int FluidCoolingTicks(ref BuildingState building, int baseTicks)
        {
            building.WaterCooled = false;
            if (!FluidsOn || (building.Kind != BuildingKind.Smelter && building.Kind != BuildingKind.BlastFurnace
                && building.Kind != BuildingKind.GlassWorks && building.Kind != BuildingKind.BrickKiln)) return baseTicks;
            int index = FindFluidNetwork(building.FactionId, building.FluidNetworkId);
            if (index < 0) return baseTicks;
            ref var network = ref world.FluidNetworks[index];
            int water = world.Config.Economy.FluidCoolingWater;
            if (network.Kind != FluidKind.Water || network.Amount < water) return baseTicks;
            network.Amount -= water;
            if (network.Amount == 0) network.Kind = FluidKind.None;
            building.WaterCooled = true;
            world.Economies[building.FactionId - 1].CoolingCount++;
            return Math.Max(1, checked((baseTicks * world.Config.Economy.FluidCoolingTimePermille + 999) / 1000));
        }

        private int FindFluidNetwork(uint faction, int number)
        {
            for (int i = 0; i < world.FluidNetworks.Length; i++)
                if (world.FluidNetworks[i].FactionId == faction && world.FluidNetworks[i].Number == number) return i;
            return -1;
        }

        private bool FluidPipeAt(int cell, uint faction)
        {
            if (cell < 0 || cell >= world.Config.Map.WidthCells * world.Config.Map.HeightCells) return false;
            EnsureFluidPipeOwner();
            return fluidPipeOwner[cell] == (int)faction;
        }

        private void RebuildFluidNetworks()
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int cells = checked(width * height);
            if (fluidPipeOwner == null || fluidPipeOwner.Length != cells) fluidPipeOwner = new int[cells];
            if (fluidQueue == null || fluidQueue.Length != cells) fluidQueue = new int[cells];
            if (fluidPipeSeen == null || fluidPipeSeen.Length != cells) fluidPipeSeen = new int[cells];
            if (fluidNetworkAtCell == null || fluidNetworkAtCell.Length != cells) fluidNetworkAtCell = new int[cells];
            if (fluidOldNetworkAtCell == null || fluidOldNetworkAtCell.Length != cells) fluidOldNetworkAtCell = new int[cells];
            Array.Fill(fluidPipeOwner, -1); Array.Fill(fluidNetworkAtCell, -1); Array.Fill(fluidOldNetworkAtCell, -1);
            var old = world.FluidNetworks ?? Array.Empty<FluidNetworkState>();
            for (int j = 0; j < old.Length; j++)
                if (old[j].PipeCells != null) for (int k = 0; k < old[j].PipeCells.Length; k++) fluidOldNetworkAtCell[old[j].PipeCells[k]] = j;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                b.FluidNetworkId = -1; b.WaterCooled = false;
            }
            for (int cell = 0; cell < cells; cell++)
                if (world.Pipes[cell].FactionId != 0) fluidPipeOwner[cell] = (int)world.Pipes[cell].FactionId;
            var components = new List<FluidNetworkState>();
            var componentFaction = new List<uint>();
            int stamp = checked(++fluidPipeStamp);
            for (int start = 0; start < cells; start++)
            {
                int owner = fluidPipeOwner[start];
                if (owner < 0 || fluidPipeSeen[start] == stamp) continue;
                var pipeCells = new List<int>(); int head = 0, tail = 0;
                uint faction = (uint)owner;
                fluidQueue[tail++] = start; fluidPipeSeen[start] = stamp;
                while (head < tail)
                {
                    int cell = fluidQueue[head++]; pipeCells.Add(cell);
                    int x = cell % width, z = cell / width;
                    if (x > 0) VisitFluidPipe(cell - 1, ref tail, stamp, faction);
                    if (x + 1 < width) VisitFluidPipe(cell + 1, ref tail, stamp, faction);
                    if (z > 0) VisitFluidPipe(cell - width, ref tail, stamp, faction);
                    if (z + 1 < height) VisitFluidPipe(cell + width, ref tail, stamp, faction);
                }
                pipeCells.Sort();
                int component = components.Count;
                for (int k = 0; k < pipeCells.Count; k++) fluidNetworkAtCell[pipeCells[k]] = component;
                components.Add(new FluidNetworkState { FactionId = faction, Number = pipeCells[0], Kind = FluidKind.None,
                    Amount = 0, Capacity = pipeCells.Count * world.Config.Economy.FluidPipeCapacity, PipeCells = pipeCells.ToArray() });
                componentFaction.Add(faction);
            }
            int[] parent = new int[components.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            for (int bi = 0; bi < world.BuildingCount; bi++)
            {
                var b = world.Buildings[bi];
                if (!b.Alive || !b.Complete || !IsFluidNetworkBuilding(b.Kind)) continue;
                int first = FirstFluidPipeComponent(b, width, height);
                if (first >= 0 && componentFaction[first] != b.FactionId)
                {
                    first = -1;
                    for (int component = 0; component < components.Count; component++)
                        if (componentFaction[component] == b.FactionId && BuildingTouchesComponent(b, component, width, height)) { first = component; break; }
                }
                if (first < 0) continue;
                for (int component = 0; component < components.Count; component++)
                {
                    if (component == first || componentFaction[component] != b.FactionId) continue;
                    if (!BuildingTouchesComponent(b, component, width, height)) continue;
                    int left = NetworkRoot(parent, first), right = NetworkRoot(parent, component);
                    if (left != right) parent[right] = left;
                }
            }
            var mergedCells = new List<int>[components.Count]; var tankCounts = new int[components.Count];
            for (int i = 0; i < components.Count; i++)
            {
                int root = NetworkRoot(parent, i);
                if (mergedCells[root] == null) mergedCells[root] = new List<int>();
                mergedCells[root].AddRange(components[i].PipeCells);
            }
            for (int bi = 0; bi < world.BuildingCount; bi++)
            {
                var b = world.Buildings[bi];
                if (b.Alive && b.Complete && b.Kind == BuildingKind.FluidTank)
                {
                    int component = FirstFluidPipeComponent(b, width, height);
                    if (component >= 0) tankCounts[NetworkRoot(parent, component)]++;
                }
            }
            var next = new List<FluidNetworkState>(); var rootToNext = new int[components.Count]; Array.Fill(rootToNext, -1);
            for (int root = 0; root < mergedCells.Length; root++)
            {
                if (mergedCells[root] == null) continue;
                mergedCells[root].Sort();
                rootToNext[root] = next.Count;
                next.Add(new FluidNetworkState { FactionId = componentFaction[root], Number = mergedCells[root][0], Kind = FluidKind.None,
                    Amount = 0, Capacity = checked(mergedCells[root].Count * world.Config.Economy.FluidPipeCapacity
                        + tankCounts[root] * world.Config.Economy.FluidTankCapacity), PipeCells = mergedCells[root].ToArray() });
            }
            int[] componentToNext = new int[components.Count];
            for (int i = 0; i < components.Count; i++) componentToNext[i] = rootToNext[NetworkRoot(parent, i)];
            Array.Fill(fluidNetworkAtCell, -1);
            for (int i = 0; i < next.Count; i++)
                for (int k = 0; k < next[i].PipeCells.Length; k++) fluidNetworkAtCell[next[i].PipeCells[k]] = i;
            int overlapLength = checked(old.Length * next.Count);
            if (fluidOverlapCounts == null || fluidOverlapCounts.Length < overlapLength) fluidOverlapCounts = new int[overlapLength];
            Array.Clear(fluidOverlapCounts, 0, overlapLength);
            if (fluidOverlapTargets == null || fluidOverlapTargets.Length < next.Count) fluidOverlapTargets = new int[next.Count];
            if (fluidOverlapTargetSeen == null || fluidOverlapTargetSeen.Length < next.Count) fluidOverlapTargetSeen = new int[next.Count];
            for (int i = 0; i < next.Count; i++)
                for (int k = 0; k < next[i].PipeCells.Length; k++)
                {
                    int oldIndex = fluidOldNetworkAtCell[next[i].PipeCells[k]];
                    if (oldIndex >= 0) fluidOverlapCounts[oldIndex * next.Count + i]++;
                }
            for (int j = 0; j < old.Length; j++)
            {
                if (old[j].PipeCells == null || old[j].PipeCells.Length == 0 || old[j].Amount <= 0) continue;
                int targetStamp = checked(++fluidOverlapTargetStamp), targetCount = 0;
                for (int k = 0; k < old[j].PipeCells.Length; k++)
                {
                    int cell = old[j].PipeCells[k];
                    if (cell < 0 || cell >= fluidNetworkAtCell.Length) continue;
                    int target = fluidNetworkAtCell[cell];
                    if (target < 0 || fluidOverlapTargetSeen[target] == targetStamp) continue;
                    fluidOverlapTargetSeen[target] = targetStamp;
                    fluidOverlapTargets[targetCount++] = target;
                }
                int assigned = 0, smallest = -1;
                for (int ti = 0; ti < targetCount; ti++)
                {
                    int i = fluidOverlapTargets[ti];
                    int overlap = fluidOverlapCounts[j * next.Count + i];
                    if (overlap == 0 || next[i].FactionId != old[j].FactionId) continue;
                    int share = old[j].Amount * overlap / old[j].PipeCells.Length;
                    assigned += share;
                    var network = next[i];
                    if (network.Kind == FluidKind.None) network.Kind = old[j].Kind;
                    network.Amount = Math.Min(network.Capacity, network.Amount + share); next[i] = network;
                    if (smallest < 0 || next[i].Number < next[smallest].Number) smallest = i;
                }
                if (smallest >= 0)
                {
                    var network = next[smallest];
                    network.Amount = Math.Min(network.Capacity, network.Amount + old[j].Amount - assigned); next[smallest] = network;
                }
            }
            next.Sort((a, b) => a.Number != b.Number ? a.Number.CompareTo(b.Number) : a.FactionId.CompareTo(b.FactionId));
            Array.Fill(fluidNetworkAtCell, -1);
            for (int i = 0; i < next.Count; i++) for (int k = 0; k < next[i].PipeCells.Length; k++) fluidNetworkAtCell[next[i].PipeCells[k]] = next[i].Number;
            world.FluidNetworks = next.ToArray();
            for (int bi = 0; bi < world.BuildingCount; bi++)
            {
                ref var b = ref world.Buildings[bi];
                if (!b.Alive || !b.Complete || !IsFluidNetworkBuilding(b.Kind)) continue;
                int network = FirstFluidNetworkNumber(b, width, height);
                if (network >= 0 && FindFluidNetwork(b.FactionId, network) < 0) network = -1;
                b.FluidNetworkId = network;
            }
            fluidLiquidBuildingCount = 0;
            if (fluidLiquidBuildings == null || fluidLiquidBuildings.Length < world.BuildingCount) fluidLiquidBuildings = new int[world.BuildingCount];
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].Complete && IsFluidBuilding(world.Buildings[i].Kind)) fluidLiquidBuildings[fluidLiquidBuildingCount++] = i;
            world.FluidsDirty = false;
        }

        private void VisitFluidPipe(int cell, ref int tail, int stamp, uint faction)
        {
            int owner = fluidPipeOwner[cell];
            if (owner == (int)faction && fluidPipeSeen[cell] != stamp)
            { fluidPipeSeen[cell] = stamp; fluidQueue[tail++] = cell; }
        }

        private int FirstFluidPipeComponent(BuildingState building, int width, int height)
        {
            int size = SizeOf(building.Kind), x0 = building.OriginCell % width, z0 = building.OriginCell / width;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int cell = building.OriginCell + z * width + x;
                if (x == 0 && x0 > 0 && fluidNetworkAtCell[cell - 1] >= 0) return fluidNetworkAtCell[cell - 1];
                if (x + 1 == size && x0 + size < width && fluidNetworkAtCell[cell + 1] >= 0) return fluidNetworkAtCell[cell + 1];
                if (z == 0 && z0 > 0 && fluidNetworkAtCell[cell - width] >= 0) return fluidNetworkAtCell[cell - width];
                if (z + 1 == size && z0 + size < height && fluidNetworkAtCell[cell + width] >= 0) return fluidNetworkAtCell[cell + width];
            }
            return -1;
        }

        private bool BuildingTouchesComponent(BuildingState building, int component, int width, int height)
        {
            int size = SizeOf(building.Kind), x0 = building.OriginCell % width, z0 = building.OriginCell / width;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int cell = building.OriginCell + z * width + x;
                if (x == 0 && x0 > 0 && fluidNetworkAtCell[cell - 1] == component || x + 1 == size && x0 + size < width && fluidNetworkAtCell[cell + 1] == component
                    || z == 0 && z0 > 0 && fluidNetworkAtCell[cell - width] == component || z + 1 == size && z0 + size < height && fluidNetworkAtCell[cell + width] == component) return true;
            }
            return false;
        }

        private int FirstFluidNetworkNumber(BuildingState building, int width, int height)
        {
            int size = SizeOf(building.Kind), x0 = building.OriginCell % width, z0 = building.OriginCell / width;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int cell = building.OriginCell + z * width + x;
                if (x == 0 && x0 > 0 && fluidNetworkAtCell[cell - 1] >= 0) return fluidNetworkAtCell[cell - 1];
                if (x + 1 == size && x0 + size < width && fluidNetworkAtCell[cell + 1] >= 0) return fluidNetworkAtCell[cell + 1];
                if (z == 0 && z0 > 0 && fluidNetworkAtCell[cell - width] >= 0) return fluidNetworkAtCell[cell - width];
                if (z + 1 == size && z0 + size < height && fluidNetworkAtCell[cell + width] >= 0) return fluidNetworkAtCell[cell + width];
            }
            return -1;
        }

        private static int NetworkRoot(int[] parent, int value)
        {
            while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; }
            return value;
        }
    }
}
