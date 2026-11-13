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
        private int[] fluidWaterNetworkAtCell;
        private int[] fluidOverlapTargets, fluidOverlapTargetSeen;
        private int fluidOverlapTargetStamp;
        private int fluidPipeStamp;
        private int[] fluidBuildingOccupancy;
        private bool fluidBuildingOccupancyDirty = true;
        private int[] fluidLiquidBuildings;
        private int fluidLiquidBuildingCount;
        private int[] fluidPathRoute;
        private int fluidPathRouteCount;
        private int[] fluidPathDistance;
        private readonly int[] fluidPathNextCells = new int[4];
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
            // 65.9: an industry building's output cell is kept for its belt (-2), so a pipe never blocks it.
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!building.Alive || building.OriginCell < 0 || !HasIndustryOutput(building.Kind)) continue;
                int output = OutputCell(building);
                if (output >= 0 && output < cells && fluidBuildingOccupancy[output] == -1) fluidBuildingOccupancy[output] = -2;
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
            if (FluidPipeAtAny(cell)) return false;
            if (!world.Map.IsPassable(cell)) return false;
            EnsureFluidBuildingOccupancy();
            return fluidBuildingOccupancy[cell] == -1;
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
            if (!TryFindFluidRoute(faction, sourceOrigin, sourceSize, targetOrigin, targetSize)) return false;
            int placed = ApplyFluidRoutePlan(faction);
            if (placed > 0) return true;
            if (world.FluidsDirty) RebuildFluidNetworks();
            return IsBuildingOnWaterNetwork(faction, targetOrigin, targetSize);
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
            if (world.FluidsDirty) RebuildFluidNetworks();

            // 65.9: one completed cooling building, in building-id order, is the whole cooling decision.
            // This deliberately runs before the brewery line so a distant cooling building is repaired locally.
            int coolingIndex = -1;
            uint coolingId = uint.MaxValue;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || !b.Complete || b.FactionId != faction || !IsCoolingBuilding(b.Kind)
                    || IsBuildingOnWaterNetwork(faction, b.OriginCell, SizeOf(b.Kind))) continue;
                if (b.Id < coolingId) { coolingId = b.Id; coolingIndex = i; }
            }
            if (coolingIndex >= 0)
            {
                var cooling = world.Buildings[coolingIndex];
                if (TryPlaceFluidRouteToWaterNetwork(faction, cooling.OriginCell, SizeOf(cooling.Kind))) return;
                if (TryAiCoolingWell(faction, cooling.OriginCell, SizeOf(cooling.Kind))) return;
                RecordFluidFailure(faction, FluidFailureKind.Route);
                return;
            }

            int breweries = CountPlannedBuildings(faction, BuildingKind.Brewery);
            int sourceNetwork = FindWaterSourceNetwork(faction);
            bool breweryPreparation = economy.Age >= 2 && (!AgeClockOn || world.Tick >= Math.Max(0, AgeClockTickForAge(2) - 1200));
            int desiredBreweries = economy.Age < 2 ? 0 : breweryPreparation
                ? Math.Min(rules.FluidAiBreweryMaxLines, (LivelihoodNeed(faction, ResourceKind.Ale, LivingVillagers(faction)) * 5 + 59) / 60)
                : Math.Min(1, rules.FluidAiBreweryMaxLines);
            // A core-side source is only the brewery bootstrap. Cooling sources are placed by TryAiCoolingWell.
            if (desiredBreweries > 0 && sourceNetwork < 0 && !HasPendingFluidSource(faction))
            {
                if (FindAiPumpSite(faction) >= 0 && CountFluidSourceBuildings(faction, BuildingKind.WaterPump) < rules.FluidAiPumpMaxBuildings)
                    TryAiFluidPlacement(faction, BuildingKind.WaterPump, 1, true);
                else if (CountFluidSourceBuildings(faction, BuildingKind.Well) < rules.FluidAiWellMaxBuildings)
                    TryAiFluidPlacement(faction, BuildingKind.Well, 2, false);
                else RecordFluidFailure(faction, FluidFailureKind.Water);
                return;
            }
            if (breweries < desiredBreweries)
            {
                if (!TryAiFluidPlacementNearNetwork(faction, BuildingKind.Brewery, 2, sourceNetwork)
                    )
                    RecordFluidFailure(faction, FluidFailureKind.Site);
            }
        }

        private int FindWaterSourceNetwork(uint faction)
        {
            int best = -1;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || !b.Complete || b.FactionId != faction
                    || (b.Kind != BuildingKind.WaterPump && b.Kind != BuildingKind.Well) || b.FluidNetworkId < 0) continue;
                if (best < 0 || b.FluidNetworkId < best) best = b.FluidNetworkId;
            }
            return best;
        }

        private bool IsBuildingOnWaterNetwork(uint faction, int origin, int size)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int x0 = origin % width, z0 = origin / width;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int cell = origin + z * width + x;
                if (x == 0 && x0 > 0 && FluidPipeAt(cell - 1, faction) && fluidWaterNetworkAtCell[cell - 1] >= 0
                    || x + 1 == size && x0 + size < width && FluidPipeAt(cell + 1, faction) && fluidWaterNetworkAtCell[cell + 1] >= 0
                    || z == 0 && z0 > 0 && FluidPipeAt(cell - width, faction) && fluidWaterNetworkAtCell[cell - width] >= 0
                    || z + 1 == size && z0 + size < height && FluidPipeAt(cell + width, faction) && fluidWaterNetworkAtCell[cell + width] >= 0) return true;
            }
            return false;
        }

        private bool TryPlaceFluidRouteToWaterNetwork(uint faction, int targetOrigin, int targetSize)
        {
            if (!TryFindFluidRouteToWaterNetwork(faction, targetOrigin, targetSize)) return false;
            bool hasNewCell = false;
            for (int i = 0; i < fluidPathRouteCount; i++)
                if (!FluidPipeAtAny(fluidPathRoute[i])) { hasNewCell = true; break; }
            int placed = ApplyFluidRoutePlan(faction);
            if (placed > 0) return true;
            if (hasNewCell)
            {
                // The route is valid but the first unpaid cell stopped the incremental placement.
                // Consume this decision; the next 600-tick/completion decision will retry from the partial route.
                RecordFluidFailure(faction, FluidFailureKind.Wood);
                return true;
            }
            if (world.FluidsDirty) RebuildFluidNetworks();
            return IsBuildingOnWaterNetwork(faction, targetOrigin, targetSize);
        }

        private bool TryFindFluidRouteToWaterNetwork(uint faction, int targetOrigin, int targetSize)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int cellsCount = checked(width * height);
            if (fluidPathParent == null || fluidPathParent.Length != cellsCount)
            {
                fluidPathParent = new int[cellsCount]; fluidPathQueue = new int[cellsCount];
                fluidPathSeen = new int[cellsCount]; fluidPathDistance = new int[cellsCount]; fluidPathStamp = 0;
            }
            if (fluidPathDistance == null || fluidPathDistance.Length != cellsCount) fluidPathDistance = new int[cellsCount];
            int x0 = targetOrigin % width, z0 = targetOrigin / width;
            int[] target = { targetOrigin - 1, targetOrigin + targetSize, targetOrigin - width, targetOrigin + targetSize * width };
            int stamp = checked(++fluidPathStamp), head = 0, tail = 0, goal = -1;
            for (int i = 0; i < target.Length; i++)
            {
                int cell = target[i];
                if (cell < 0 || cell >= cellsCount || i == 0 && x0 == 0 || i == 1 && x0 + targetSize >= width
                    || i == 2 && z0 == 0 || i == 3 && z0 + targetSize >= height
                    || fluidPathSeen[cell] == stamp || !IsFluidRoutePlanCell(cell, faction, -1, 0)) continue;
                fluidPathSeen[cell] = stamp; fluidPathParent[cell] = -1; fluidPathDistance[cell] = 0; fluidPathQueue[tail++] = cell;
            }
            while (head < tail)
            {
                int cell = fluidPathQueue[head++];
                if (FluidPipeAt(cell, faction) && fluidWaterNetworkAtCell[cell] >= 0) { goal = cell; break; }
                if (fluidPathDistance[cell] >= 20) continue;
                int x = cell % width;
                fluidPathNextCells[0] = cell - width; fluidPathNextCells[1] = x + 1 < width ? cell + 1 : -1;
                fluidPathNextCells[2] = cell + width; fluidPathNextCells[3] = x > 0 ? cell - 1 : -1;
                for (int i = 0; i < fluidPathNextCells.Length; i++)
                {
                    int candidate = fluidPathNextCells[i];
                    if (candidate < 0 || candidate >= cellsCount || fluidPathSeen[candidate] == stamp
                        || !IsFluidRoutePlanCell(candidate, faction, -1, 0)) continue;
                    fluidPathSeen[candidate] = stamp; fluidPathParent[candidate] = cell;
                    fluidPathDistance[candidate] = fluidPathDistance[cell] + 1; fluidPathQueue[tail++] = candidate;
                }
            }
            if (goal < 0) return false;
            EnsureFluidPathRouteCapacity();
            fluidPathRouteCount = 0;
            for (int cell = goal; cell >= 0; cell = fluidPathParent[cell]) fluidPathRoute[fluidPathRouteCount++] = cell;
            for (int i = 0, j = fluidPathRouteCount - 1; i < j; i++, j--)
            { int swap = fluidPathRoute[i]; fluidPathRoute[i] = fluidPathRoute[j]; fluidPathRoute[j] = swap; }
            return fluidPathRouteCount > 0;
        }

        private bool TryAiCoolingWell(uint faction, int targetOrigin, int targetSize)
        {
            if (HasPendingFluidSource(faction)) return false;
            var rules = world.Config.Economy;
            if (CountFluidSourceBuildings(faction, BuildingKind.WaterPump) < rules.FluidAiPumpMaxBuildings
                && TryAiCoolingSourceCandidate(faction, BuildingKind.WaterPump, targetOrigin, targetSize)) return true;
            if (CountFluidSourceBuildings(faction, BuildingKind.Well) < rules.FluidAiWellMaxBuildings
                && TryAiCoolingSourceCandidate(faction, BuildingKind.Well, targetOrigin, targetSize)) return true;
            RecordFluidFailure(faction, FluidFailureKind.Water);
            return false;
        }

        private bool TryAiCoolingSourceCandidate(uint faction, BuildingKind kind, int targetOrigin, int targetSize)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int tx = targetOrigin % width, tz = targetOrigin / width;
            int size = SizeOf(kind), maxRadius = 8;
            // 65.11: keep the source on the far side from the ore that feeds the building, so its belt corridor stays open.
            int anchor = FluidOreAnchor(faction, targetOrigin);
            int ax = anchor >= 0 ? anchor % width - tx : 0, az = anchor >= 0 ? anchor / width - tz : 0;
            int half = targetSize / 2;
            for (int radius = 0; radius <= maxRadius; radius++)
            {
                for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dz) != radius) continue;
                    int x = tx + dx, z = tz + dz;
                    if (x < 0 || z < 0 || x + size > width || z + size > height) continue;
                    if (anchor >= 0 && checked((dx + size / 2 - half) * ax + (dz + size / 2 - half) * az) > 0) continue;
                    int origin = z * width + x;
                    if (kind == BuildingKind.WaterPump && !TouchesRiver(origin, size)) continue;
                    if (!FluidSiteIsClear(origin, world.Map.Cell(OwnCore(faction).Definition.Position), size)) continue;
                    if (!TryFindFluidRoute(faction, origin, size, targetOrigin, targetSize, origin, size, 20)) continue;
                    int before = CountPlannedBuildings(faction, kind);
                    ApplyEconomyCommand(EconomyCommand.Place(faction, (ulong)(world.Tick + origin + 1), kind, origin));
                    if (CountPlannedBuildings(faction, kind) <= before) continue;
                    bool hasNewCell = false;
                    for (int i = 0; i < fluidPathRouteCount; i++)
                        if (!FluidPipeAtAny(fluidPathRoute[i])) { hasNewCell = true; break; }
                    if (ApplyFluidRoutePlan(faction) == 0 && hasNewCell) RecordFluidFailure(faction, FluidFailureKind.Wood);
                    return true;
                }
            }
            return false;
        }

        /// <summary>65.11: the cell the building's ore comes from — the nearest own mine or drill, else the nearest ore node.</summary>
        private int FluidOreAnchor(uint faction, int targetOrigin)
        {
            int best = -1, bestDistance = int.MaxValue;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.FactionId != faction || b.Kind != BuildingKind.Mine && b.Kind != BuildingKind.Drill) continue;
                int distance = Chebyshev(b.OriginCell, targetOrigin);
                if (distance < bestDistance) { best = b.OriginCell; bestDistance = distance; }
            }
            if (best >= 0) return best;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                if (world.Nodes[i].Definition.Kind != ResourceKind.Ore || world.Nodes[i].Remaining <= 0) continue;
                int cell = world.Map.Cell(world.Nodes[i].Definition.Position);
                int distance = Chebyshev(cell, targetOrigin);
                if (distance < bestDistance) { best = cell; bestDistance = distance; }
            }
            return best;
        }

        private int CountFluidSourceBuildings(uint faction, BuildingKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == kind) count++;
            return count;
        }

        private bool HasPendingFluidSource(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (building.Alive && building.FactionId == faction
                    && (building.Kind == BuildingKind.WaterPump || building.Kind == BuildingKind.Well)
                    && !building.Complete) return true;
            }
            return false;
        }

        private bool TryFindFluidRoute(uint faction, int sourceOrigin, int sourceSize, int targetOrigin, int targetSize,
            int blockedOrigin = -1, int blockedSize = 0, int maxDistance = int.MaxValue)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int[] source = { sourceOrigin - 1, sourceOrigin + sourceSize, sourceOrigin - width, sourceOrigin + sourceSize * width };
            int[] target = { targetOrigin - 1, targetOrigin + targetSize, targetOrigin - width, targetOrigin + targetSize * width };
            int cellsCount = checked(width * height);
            if (fluidPathParent == null || fluidPathParent.Length != cellsCount)
            {
                fluidPathParent = new int[cellsCount]; fluidPathQueue = new int[cellsCount]; fluidPathSeen = new int[cellsCount];
                fluidPathDistance = new int[cellsCount]; fluidPathStamp = 0;
            }
            if (fluidPathDistance == null || fluidPathDistance.Length != cellsCount) fluidPathDistance = new int[cellsCount];
            int stamp = checked(++fluidPathStamp), head = 0, tail = 0, goal = -1;
            for (int si = 0; si < source.Length; si++)
            {
                int start = source[si];
                if (start < 0 || start >= cellsCount || fluidPathSeen[start] == stamp
                    || !IsFluidRoutePlanCell(start, faction, blockedOrigin, blockedSize)) continue;
                fluidPathQueue[tail++] = start; fluidPathSeen[start] = stamp; fluidPathParent[start] = -1; fluidPathDistance[start] = 0;
            }
            while (head < tail && goal < 0)
            {
                int cell = fluidPathQueue[head++], x = cell % width;
                if (IsFluidRouteTarget(cell, target, faction)) { goal = cell; break; }
                if (fluidPathDistance[cell] >= maxDistance) continue;
                fluidPathNextCells[0] = cell - width; fluidPathNextCells[1] = x + 1 < width ? cell + 1 : -1;
                fluidPathNextCells[2] = cell + width; fluidPathNextCells[3] = x > 0 ? cell - 1 : -1;
                for (int ni = 0; ni < fluidPathNextCells.Length; ni++)
                {
                    int candidate = fluidPathNextCells[ni];
                    if (candidate < 0 || candidate >= cellsCount || fluidPathSeen[candidate] == stamp
                        || !IsFluidRoutePlanCell(candidate, faction, blockedOrigin, blockedSize)) continue;
                    fluidPathSeen[candidate] = stamp; fluidPathParent[candidate] = cell;
                    fluidPathDistance[candidate] = fluidPathDistance[cell] + 1; fluidPathQueue[tail++] = candidate;
                }
            }
            if (goal < 0) return false;
            EnsureFluidPathRouteCapacity();
            fluidPathRouteCount = 0;
            for (int cell = goal; cell >= 0; cell = fluidPathParent[cell]) fluidPathRoute[fluidPathRouteCount++] = cell;
            for (int i = 0, j = fluidPathRouteCount - 1; i < j; i++, j--)
            {
                int swap = fluidPathRoute[i]; fluidPathRoute[i] = fluidPathRoute[j]; fluidPathRoute[j] = swap;
            }
            return fluidPathRouteCount > 0;
        }

        private bool IsFluidRoutePlanCell(int cell, uint faction, int blockedOrigin, int blockedSize)
        {
            if (blockedOrigin >= 0 && blockedSize > 0)
            {
                int width = world.Config.Map.WidthCells;
                int x0 = blockedOrigin % width, z0 = blockedOrigin / width;
                int x = cell % width, z = cell / width;
                if (x >= x0 && x < x0 + blockedSize && z >= z0 && z < z0 + blockedSize) return false;
            }
            return IsFluidRouteCell(cell, faction);
        }

        private int FindAiPumpSite(uint faction)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position), cx = core % width, cz = core / width;
            for (int radius = 0; radius <= 40; radius++)
                for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dz) != radius) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= width || z >= height) continue;
                    int cell = z * width + x;
                    if (!TouchesRiver(cell, 1) || !FluidSiteIsClear(cell, core, 1) || !AiSourcePipeSiteAvailable(cell, 1)) continue;
                    return cell;
                }
            return -1;
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
                    || fluidBuildingOccupancy[cell] != -1) continue;
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
                    || fluidBuildingOccupancy[cell] != -1
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
            if ((kind == BuildingKind.WaterPump || kind == BuildingKind.Well) && HasPendingFluidSource(faction)) return;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position);
            int cx = core % width, cz = core / width;
            int maxRadius = 40;
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
                    if (kind == BuildingKind.WaterPump || kind == BuildingKind.Well)
                    {
                        if (!TryPlanFluidSourceRoute(faction, origin, size)) continue;
                    }
                    var command = EconomyCommand.Place(faction, (ulong)(world.Tick + radius + 1), kind, origin);
                    int beforeCount = CountPlannedBuildings(faction, kind);
                    ApplyEconomyCommand(command);
                    if (CountPlannedBuildings(faction, kind) > beforeCount)
                    {
                        if (kind == BuildingKind.WaterPump || kind == BuildingKind.Well) ApplyFluidRoutePlan(faction);
                        return;
                    }
                }
            }
            RecordFluidFailure(faction, FluidFailureKind.Site);
        }

        private bool TryPlanFluidSourceRoute(uint faction, int sourceOrigin, int sourceSize)
        {
            bool hasFluidTarget = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var target = world.Buildings[i];
                if (!target.Alive || target.Reserved || !target.Complete || target.FactionId != faction) continue;
                if (!IsCoolingBuilding(target.Kind) && target.Kind != BuildingKind.Brewery) continue;
                hasFluidTarget = true;
                if (TryFindFluidRoute(faction, sourceOrigin, sourceSize, target.OriginCell, SizeOf(target.Kind), sourceOrigin, sourceSize)) return true;
            }
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var source = world.Buildings[i];
                if (!source.Alive || source.Reserved || !source.Complete || source.FactionId != faction
                    || (source.Kind != BuildingKind.WaterPump && source.Kind != BuildingKind.Well)) continue;
                hasFluidTarget = true;
                if (TryFindFluidRoute(faction, sourceOrigin, sourceSize, source.OriginCell, SizeOf(source.Kind), sourceOrigin, sourceSize)) return true;
            }
            if (hasFluidTarget) return false;
            return TryFindAdjacentFluidPipePlan(faction, sourceOrigin, sourceSize);
        }

        private bool TryFindAdjacentFluidPipePlan(uint faction, int origin, int size)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int x0 = origin % width, z0 = origin / width;
            int[] adjacent = { origin - 1, origin + size, origin - width, origin + size * width };
            for (int i = 0; i < adjacent.Length; i++)
            {
                int cell = adjacent[i];
                if (cell < 0 || cell >= width * height || i == 0 && x0 == 0 || i == 1 && x0 + size >= width
                    || i == 2 && z0 == 0 || i == 3 && z0 + size >= height) continue;
                if (!IsFluidRouteCell(cell, faction) || FluidPipeAt(cell, faction)) continue;
                EnsureFluidPathRouteCapacity();
                fluidPathRouteCount = 1;
                fluidPathRoute[0] = cell;
                return true;
            }
            return false;
        }

        private void EnsureFluidPathRouteCapacity()
        {
            int cells = checked(world.Config.Map.WidthCells * world.Config.Map.HeightCells);
            if (fluidPathRoute == null || fluidPathRoute.Length != cells) fluidPathRoute = new int[cells];
        }

        private int ApplyFluidRoutePlan(uint faction)
        {
            if (fluidPathRouteCount <= 0) return 0;
            var route = new List<int>(fluidPathRouteCount);
            for (int i = 0; i < fluidPathRouteCount; i++) route.Add(fluidPathRoute[i]);
            int before = 0;
            for (int i = 0; i < route.Count; i++) if (FluidPipeAt(route[i], faction)) before++;
            ApplyFluidCommand(faction, EconomyCommand.PlacePipe(faction, (ulong)(world.Tick + route[0] + 1), route));
            int after = 0;
            for (int i = 0; i < route.Count; i++) if (FluidPipeAt(route[i], faction)) after++;
            return Math.Max(0, after - before);
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
                // 65.10: pipes run under belts, so a belt cell is fine.
                if (!world.Map.IsPassable(cell)
                    || IsReservedBuildingCell(cell) || fluidBuildingOccupancy[cell] != -1 || IsNodeCell(cell) || InsideAnyCore(cell))
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
            if (fluidWaterNetworkAtCell == null || fluidWaterNetworkAtCell.Length != cells) fluidWaterNetworkAtCell = new int[cells];
            if (fluidOldNetworkAtCell == null || fluidOldNetworkAtCell.Length != cells) fluidOldNetworkAtCell = new int[cells];
            Array.Fill(fluidPipeOwner, -1); Array.Fill(fluidNetworkAtCell, -1); Array.Fill(fluidWaterNetworkAtCell, -1); Array.Fill(fluidOldNetworkAtCell, -1);
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
            for (int bi = 0; bi < world.BuildingCount; bi++)
            {
                var b = world.Buildings[bi];
                if (!b.Alive || !b.Complete || (b.Kind != BuildingKind.WaterPump && b.Kind != BuildingKind.Well)
                    || b.FluidNetworkId < 0) continue;
                int network = FindFluidNetwork(b.FactionId, b.FluidNetworkId);
                if (network < 0) continue;
                for (int k = 0; k < world.FluidNetworks[network].PipeCells.Length; k++)
                    fluidWaterNetworkAtCell[world.FluidNetworks[network].PipeCells[k]] = b.FluidNetworkId;
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
