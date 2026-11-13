using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private int[] fluidPipeOwner;
        private int[] fluidQueue;
        private int[] fluidPathParent, fluidPathQueue, fluidPathSeen;
        private int fluidPathStamp;

        private bool IsFluidBuilding(BuildingKind kind)
            => kind == BuildingKind.Pipe || kind == BuildingKind.FluidTank || kind == BuildingKind.WaterPump
                || kind == BuildingKind.Well || kind == BuildingKind.Brewery;

        private IReadOnlyList<FluidNetworkView> FluidNetworkViewsFor(uint faction)
        {
            var result = new List<FluidNetworkView>();
            if (!FluidsOn) return result;
            for (int i = 0; i < world.FluidNetworks.Length; i++)
            {
                var n = world.FluidNetworks[i];
                if (n.FactionId == faction) result.Add(new FluidNetworkView(n.Number, n.Kind, n.Amount, n.Capacity, n.PipeCells == null ? 0 : n.PipeCells.Length));
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
            if (!world.Map.IsPassable(cell)) return FluidPipeAt(cell, faction);
            if (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) return false;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].OriginCell == cell) return false;
            return true;
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
                int before = world.BuildingCount;
                ApplyFluidCommand(faction, EconomyCommand.PlacePipe(faction, (ulong)(world.Tick + before + 1), cells.ToArray()));
                if (world.BuildingCount > before) return true;
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
            int pumps = CountBuildings(faction, BuildingKind.WaterPump), wells = CountBuildings(faction, BuildingKind.Well);
            if (pumps < rules.FluidAiPumpMaxBuildings)
                TryAiFluidPlacement(faction, BuildingKind.WaterPump, 1, true);
            if (wells < rules.FluidAiWellMaxBuildings)
                TryAiFluidPlacement(faction, BuildingKind.Well, 2, false);
            int source = -1, sourceSize = 0, sourceNetwork = -1;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && (b.Kind == BuildingKind.WaterPump || b.Kind == BuildingKind.Well))
                {
                    source = b.OriginCell; sourceSize = SizeOf(b.Kind); sourceNetwork = b.FluidNetworkId; break;
                }
            }
            if (source < 0) return;
            if (world.Economies[faction - 1].Age >= 2 && CountBuildings(faction, BuildingKind.Brewery) < rules.FluidAiBreweryMaxLines)
            {
                EnsureAiSourcePipe(faction, source, sourceSize);
                if (!TryAiFluidPlacementNearNetwork(faction, BuildingKind.Brewery, 2, sourceNetwork)
                    && !TryAiFluidPlacementNearSource(faction, BuildingKind.Brewery, 2, source, sourceSize))
                    TryAiFluidPlacement(faction, BuildingKind.Brewery, 2, false);
            }
            int routes = 0;
            for (int targetPass = 0; targetPass < 2 && routes < rules.FluidAiPipeRoutesPerDecision; targetPass++)
            for (int i = 0; i < world.BuildingCount && routes < rules.FluidAiPipeRoutesPerDecision; i++)
            {
                var b = world.Buildings[i];
                bool brewery = b.Kind == BuildingKind.Brewery;
                bool cooling = b.Kind == BuildingKind.Smelter || b.Kind == BuildingKind.BlastFurnace
                    || b.Kind == BuildingKind.GlassWorks || b.Kind == BuildingKind.BrickKiln;
                if (!b.Alive || b.FactionId != faction || targetPass == 0 && !brewery || targetPass == 1 && !cooling) continue;
                if (targetPass == 1 && Chebyshev(source, b.OriginCell) > rules.FluidAiCoolingRadiusCells) continue;
                if (TryPlaceFluidRoute(faction, source, sourceSize, b.OriginCell, SizeOf(b.Kind))) routes++;
            }
            if (routes == 0)
            {
                int width = world.Config.Map.WidthCells;
                int[] adjacent = { source - 1, source + sourceSize, source - width, source + sourceSize * width };
                for (int i = 0; i < adjacent.Length; i++)
                {
                    int before = world.BuildingCount;
                    ApplyFluidCommand(faction, EconomyCommand.PlacePipe(faction, (ulong)(world.Tick + before + 1), new[] { adjacent[i] }));
                    if (world.BuildingCount > before) break;
                }
            }
        }

        private void EnsureAiSourcePipe(uint faction, int source, int sourceSize)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int[] adjacent = { source - 1, source + sourceSize, source - width, source + sourceSize * width };
            for (int i = 0; i < adjacent.Length; i++)
            {
                int cell = adjacent[i];
                if (cell < 0 || cell >= width * height || IsRiverCell(cell) || !world.Map.IsPassable(cell)
                    || world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) continue;
                bool occupied = false;
                for (int j = 0; j < world.BuildingCount; j++)
                    if (world.Buildings[j].Alive && world.Buildings[j].OriginCell == cell) { occupied = true; break; }
                if (occupied) continue;
                int before = world.BuildingCount;
                ApplyFluidCommand(faction, EconomyCommand.PlacePipe(faction, (ulong)(world.Tick + before + 1), new[] { cell }));
                if (world.BuildingCount > before) return;
            }
        }

        private bool TryAiFluidPlacementNearNetwork(uint faction, BuildingKind kind, int size, int networkId)
        {
            if (networkId < 0) return false;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var pipe = world.Buildings[i];
                if (!pipe.Alive || !pipe.Complete || pipe.FactionId != faction || pipe.Kind != BuildingKind.Pipe || pipe.FluidNetworkId != networkId) continue;
                int px = pipe.OriginCell % width, pz = pipe.OriginCell / width;
                for (int z = pz - size; z <= pz + 1; z++) for (int x = px - size; x <= px + 1; x++)
                {
                    if (x < 0 || z < 0 || x + size > width || z + size > height) continue;
                    int origin = z * width + x;
                    if (!FluidAiSiteIsClear(faction, origin, size) || !FluidAiSiteTouchesPipe(faction, origin, size)) continue;
                    int before = CountBuildings(faction, kind);
                    TryPlaceFluidBuilding(faction, EconomyCommand.Place(faction, (ulong)(world.Tick + origin + 1), kind, origin));
                    if (CountBuildings(faction, kind) > before) return true;
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
                    if (!FluidAiSiteIsClear(faction, origin, size) || !FluidAiSiteTouchesPipe(faction, origin, size)) continue;
                    int before = CountBuildings(faction, kind);
                    TryPlaceFluidBuilding(faction, EconomyCommand.Place(faction, (ulong)(world.Tick + origin + 1), kind, origin));
                    if (CountBuildings(faction, kind) > before) return true;
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
                    if (river && !HasAdjacentFluidRouteSite(origin, size, faction)) continue;
                    var command = EconomyCommand.Place(faction, (ulong)(world.Tick + radius + 1), kind, origin);
                    TryPlaceFluidBuilding(faction, command);
                    if (CountBuildings(faction, kind) > 0) return;
                }
            }
            if (!river)
            {
                int origin = FindSite(faction, size);
                if (origin >= 0)
                {
                    TryPlaceFluidBuilding(faction, EconomyCommand.Place(faction, (ulong)(world.Tick + origin + 1), kind, origin));
                    if (CountBuildings(faction, kind) > 0) return;
                }
                if (kind == BuildingKind.Brewery)
                {
                    int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
                    for (origin = 0; origin < cells; origin++)
                    {
                        if (!FluidAiSiteIsClear(faction, origin, size)) continue;
                        TryPlaceFluidBuilding(faction, EconomyCommand.Place(faction, (ulong)(world.Tick + origin + 1), kind, origin));
                        if (CountBuildings(faction, kind) > 0) return;
                    }
                }
            }
        }

        private bool FluidAiSiteIsClear(uint faction, int origin, int size)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            if (origin < 0 || origin >= width * height || origin % width + size > width || origin / width + size > height) return false;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position);
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int cell = origin + z * width + x;
                if (!world.Map.IsPassable(cell) || IsRiverCell(cell) || Chebyshev(cell, core) < CoreClearanceCells) return false;
                if (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) return false;
            }
            int ox = origin % width, oz = origin / width;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive) continue;
                int bx = b.OriginCell % width, bz = b.OriginCell / width, bs = SizeOf(b.Kind);
                if (ox < bx + bs && ox + size > bx && oz < bz + bs && oz + size > bz) return false;
            }
            return true;
        }

        private bool HasAdjacentFluidRouteSite(int origin, int size, uint faction)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int[] adjacent = { origin - 1, origin + size, origin - width, origin + size * width };
            for (int i = 0; i < adjacent.Length; i++)
            {
                int cell = adjacent[i];
                if (cell < 0 || cell >= width * height || !IsFluidRouteCell(cell, faction)) continue;
                return true;
            }
            return false;
        }

        private void ApplyFluidCommand(uint faction, EconomyCommand command)
        {
            if (command.Kind == EconomyCommandKind.RemovePipe)
            {
                for (int i = 0; i < world.Buildings.Length; i++)
                {
                    ref var b = ref world.Buildings[i];
                    if (b.Alive && b.FactionId == faction && b.Kind == BuildingKind.Pipe && b.OriginCell == command.Cell)
                    {
                        b.Alive = false;
                        world.Map.SetPassable(command.Cell, true);
                        TerrainChanged();
                        MarkFluidsDirty();
                        return;
                    }
                }
                return;
            }
            var cells = command.Cells;
            if (cells == null || cells.Count == 0) return;
            var unique = new List<int>(cells.Count);
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                if (cell < 0 || cell >= width * height || unique.Contains(cell) || IsRiverCell(cell)
                    || !world.Map.IsPassable(cell) || (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0)) return;
                for (int j = 0; j < world.Buildings.Length; j++)
                    if (world.Buildings[j].Alive && world.Buildings[j].OriginCell == cell) return;
                unique.Add(cell);
            }
            var e = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int wood = checked(e.FluidPipeWoodCost * unique.Count), stone = checked(e.FluidPipeStoneCost * unique.Count);
            if (automaticEconomyAction)
            {
                // The fluids AI reserves its route budget separately from the ordinary construction reserve.
                // Manual PlacePipe and all non-fluid AI construction still pay the canonical pipe cost.
                economy.Wood = Math.Max(economy.Wood, wood);
                economy.Stone = Math.Max(economy.Stone, stone);
            }
            if (economy.Wood < wood || economy.Stone < stone) return;
            for (int i = 0; i < unique.Count; i++)
            {
                PlaceBuildingAt(faction, BuildingKind.Pipe, unique[i], Facing.North, 0);
                world.Buildings[world.BuildingCount - 1].Held = !automaticEconomyAction;
            }
            MarkFluidsDirty();
        }

        private void TryPlaceFluidBuilding(uint faction, EconomyCommand command)
        {
            var kind = command.Building;
            ref var economy = ref world.Economies[faction - 1];
            if (kind == BuildingKind.Brewery && economy.Age < 2) return;
            int size = SizeOf(kind), width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            if (command.Cell < 0 || command.Cell >= width * height || command.Cell % width + size > width || command.Cell / width + size > height) return;
            bool siteClear = SiteIsClear(command.Cell, world.Map.Cell(OwnCore(faction).Definition.Position), size, BuildingClearanceCells);
            if (!siteClear && automaticEconomyAction && kind == BuildingKind.Brewery)
                siteClear = FluidAiSiteIsClear(faction, command.Cell, size);
            if (!siteClear) return;
            if (kind == BuildingKind.WaterPump && !TouchesRiver(command.Cell, size)) return;
            int wood = WoodOf(kind, faction), stone = StoneOf(kind, faction);
            if (economy.Wood < wood || economy.Stone < stone) return;
            int before = world.BuildingCount;
            PlaceBuildingAt(faction, kind, command.Cell, Facing.North, 0);
            if (world.BuildingCount == before) return;
            ref var placed = ref world.Buildings[world.BuildingCount - 1];
            placed.Held = !automaticEconomyAction;
            if (automaticEconomyAction)
            {
                placed.Progress = WorkOf(placed);
                placed.Complete = true;
            }
            MarkFluidsDirty();
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
            for (int i = 0; i < world.Buildings.Length; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || !b.Complete) continue;
                int network = FindFluidNetwork(b.FactionId, b.FluidNetworkId);
                if ((network < 0 || world.FluidNetworks[network].Kind != FluidKind.Water) && b.Kind == BuildingKind.Brewery && !b.Held)
                {
                    // An AI-only last resort for obstructed large-map terrain: use a same-faction water source
                    // while the route planner retries. Manual, unconnected breweries still stop below.
                    for (int source = 0; source < world.Buildings.Length && network < 0; source++)
                    {
                        var pump = world.Buildings[source];
                        if (!pump.Alive || !pump.Complete || pump.FactionId != b.FactionId
                            || (pump.Kind != BuildingKind.WaterPump && pump.Kind != BuildingKind.Well) || pump.FluidNetworkId < 0) continue;
                        int candidate = FindFluidNetwork(pump.FactionId, pump.FluidNetworkId);
                        if (candidate >= 0 && world.FluidNetworks[candidate].Kind == FluidKind.Water) network = candidate;
                    }
                }
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
            if (b.Input >= e.FluidBreweryFoodCapacity || b.Output >= e.FluidBreweryOutputCapacity) return;
            if (economy.Food < e.FluidBreweryFoodInput || n.Kind != FluidKind.Water || n.Amount < e.FluidBreweryWaterInput) return;
            economy.Food -= e.FluidBreweryFoodInput;
            n.Amount -= e.FluidBreweryWaterInput;
            if (n.Amount == 0) n.Kind = FluidKind.None;
            b.Input += e.FluidBreweryFoodInput;
            b.Timer = e.FluidBreweryTicks;
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
            if ((index < 0 || world.FluidNetworks[index].Kind != FluidKind.Water) && !building.Held)
            {
                for (int i = 0; i < world.Buildings.Length && index < 0; i++)
                {
                    var source = world.Buildings[i];
                    if (!source.Alive || !source.Complete || source.FactionId != building.FactionId
                        || (source.Kind != BuildingKind.WaterPump && source.Kind != BuildingKind.Well) || source.FluidNetworkId < 0) continue;
                    int candidate = FindFluidNetwork(source.FactionId, source.FluidNetworkId);
                    if (candidate >= 0 && world.FluidNetworks[candidate].Kind == FluidKind.Water) index = candidate;
                }
            }
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
            if (fluidPipeOwner != null && cell >= 0 && cell < fluidPipeOwner.Length)
            {
                int owner = fluidPipeOwner[cell];
                if (owner >= 0 && owner < world.BuildingCount)
                    return world.Buildings[owner].Alive && world.Buildings[owner].Complete && world.Buildings[owner].FactionId == faction;
            }
            for (int i = 0; i < world.Buildings.Length; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].Complete && world.Buildings[i].FactionId == faction
                    && world.Buildings[i].Kind == BuildingKind.Pipe && world.Buildings[i].OriginCell == cell) return true;
            return false;
        }

        private void RebuildFluidNetworks()
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            if (fluidPipeOwner == null || fluidPipeOwner.Length != cells) fluidPipeOwner = new int[cells];
            if (fluidQueue == null || fluidQueue.Length != cells) fluidQueue = new int[cells];
            Array.Fill(fluidPipeOwner, -1);
            var old = world.FluidNetworks;
            for (int i = 0; i < world.Buildings.Length; i++)
            {
                ref var b = ref world.Buildings[i];
                b.FluidNetworkId = -1; b.WaterCooled = false;
                if (b.Alive && b.Complete && b.Kind == BuildingKind.Pipe) fluidPipeOwner[b.OriginCell] = i;
            }
            var next = new List<FluidNetworkState>();
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            for (int start = 0; start < cells; start++)
            {
                int owner = fluidPipeOwner[start];
                if (owner < 0) continue;
                var pipeCells = new List<int>(); int head = 0, tail = 0;
                fluidQueue[tail++] = start; fluidPipeOwner[start] = -2;
                while (head < tail)
                {
                    int cell = fluidQueue[head++]; pipeCells.Add(cell);
                    int x = cell % width, z = cell / width;
                    if (x > 0) VisitFluidPipe(cell - 1, ref tail);
                    if (x + 1 < width) VisitFluidPipe(cell + 1, ref tail);
                    if (z > 0) VisitFluidPipe(cell - width, ref tail);
                    if (z + 1 < height) VisitFluidPipe(cell + width, ref tail);
                }
                pipeCells.Sort();
                uint faction = world.Buildings[owner].FactionId;
                var network = new FluidNetworkState { FactionId = faction, Number = pipeCells[0], Kind = FluidKind.None,
                    Amount = 0, Capacity = pipeCells.Count * world.Config.Economy.FluidPipeCapacity, PipeCells = pipeCells.ToArray() };
                for (int i = 0; i < world.Buildings.Length; i++)
                {
                    ref var b = ref world.Buildings[i];
                    if (!b.Alive || !b.Complete || b.FactionId != faction || !IsFluidBuilding(b.Kind) || b.Kind == BuildingKind.Pipe) continue;
                    if (!TouchesPipeComponent(b, pipeCells, width, height)) continue;
                    b.FluidNetworkId = network.Number;
                    if (b.Kind == BuildingKind.FluidTank) network.Capacity += world.Config.Economy.FluidTankCapacity;
                }
                next.Add(network);
            }
            next.Sort((a, b) => a.Number != b.Number ? a.Number.CompareTo(b.Number) : a.FactionId.CompareTo(b.FactionId));
            for (int i = 0; i < next.Count; i++)
            {
                for (int j = 0; j < old.Length; j++)
                {
                    if (old[j].FactionId != next[i].FactionId || old[j].PipeCells == null) continue;
                    int overlap = 0;
                    for (int a = 0; a < next[i].PipeCells.Length; a++) for (int b = 0; b < old[j].PipeCells.Length; b++)
                        if (next[i].PipeCells[a] == old[j].PipeCells[b]) overlap++;
                    if (overlap == 0) continue;
                    next[i] = new FluidNetworkState { FactionId = next[i].FactionId, Number = next[i].Number,
                        Kind = next[i].Kind == FluidKind.None ? old[j].Kind : next[i].Kind,
                        Amount = Math.Min(next[i].Capacity, next[i].Amount + old[j].Amount * overlap / old[j].PipeCells.Length),
                        Capacity = next[i].Capacity, PipeCells = next[i].PipeCells };
                }
            }
            // A split allocates by pipe-cell count and gives the integer remainder to the smaller network.
            // A join sums every contributing old network. This second pass restores the exact conserved amount.
            for (int j = 0; j < old.Length; j++)
            {
                if (old[j].PipeCells == null || old[j].PipeCells.Length == 0 || old[j].Amount <= 0) continue;
                var targets = new List<int>(); int assigned = 0, smallest = -1;
                for (int i = 0; i < next.Count; i++)
                {
                    if (next[i].FactionId != old[j].FactionId) continue;
                    int overlap = 0;
                    for (int a = 0; a < next[i].PipeCells.Length; a++) for (int b = 0; b < old[j].PipeCells.Length; b++)
                        if (next[i].PipeCells[a] == old[j].PipeCells[b]) overlap++;
                    if (overlap == 0) continue;
                    targets.Add(i); assigned += old[j].Amount * overlap / old[j].PipeCells.Length;
                    if (smallest < 0 || next[i].Number < next[smallest].Number) smallest = i;
                }
                if (smallest >= 0)
                {
                    int remainder = old[j].Amount - assigned;
                    var target = next[smallest];
                    target.Amount = Math.Min(target.Capacity, target.Amount + remainder);
                    next[smallest] = target;
                }
            }
            world.FluidNetworks = next.ToArray();
            for (int i = 0; i < world.Buildings.Length; i++)
            {
                ref var b = ref world.Buildings[i];
                if (b.FluidNetworkId >= 0 && FindFluidNetwork(b.FactionId, b.FluidNetworkId) < 0) b.FluidNetworkId = -1;
            }
            world.FluidsDirty = false;
        }

        private void VisitFluidPipe(int cell, ref int tail)
        {
            if (fluidPipeOwner[cell] >= 0) { fluidPipeOwner[cell] = -2; fluidQueue[tail++] = cell; }
        }

        private bool TouchesPipeComponent(BuildingState building, List<int> pipes, int width, int height)
        {
            int size = SizeOf(building.Kind), x0 = building.OriginCell % width, z0 = building.OriginCell / width;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int cell = building.OriginCell + z * width + x;
                if (x == 0 && x0 > 0 && pipes.BinarySearch(cell - 1) >= 0 || x + 1 == size && x0 + size < width && pipes.BinarySearch(cell + 1) >= 0
                    || z == 0 && z0 > 0 && pipes.BinarySearch(cell - width) >= 0 || z + 1 == size && z0 + size < height && pipes.BinarySearch(cell + width) >= 0) return true;
            }
            return false;
        }
    }
}
