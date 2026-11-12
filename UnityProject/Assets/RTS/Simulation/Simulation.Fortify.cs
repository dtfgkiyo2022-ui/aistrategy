using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        public FortifyMetricsView FortifyMetrics(uint faction)
        {
            if (faction < 1 || faction > world.Economies.Length) throw new ArgumentOutOfRangeException(nameof(faction));
            var e = world.Economies[faction - 1];
            return new FortifyMetricsView(e.FortifyWallCells, e.FortifyGateCells, e.FortifyDestroyed, e.FortifyCollapsed,
                e.FortifyBreachUses, e.FortifyRepairHp, e.FortifyRetargets, e.FortifyRepairWood, e.FortifyRepairStone,
                e.FortifyRepairPlanks, e.FortifyRepairBricks, e.FortifyRepairSiegeParts, e.FortifyRepairFrames,
                e.FortifyRepairFittings, e.FortifyRepairRope, e.FortifyWallOverBelt, e.FortifyRingGaps,
                e.FortifyRingTerrainGaps, e.FortifyRingBuildingGaps, e.FortifyRingResourceGaps, e.FortifyRingPlannedGaps);
        }

        private bool OwnFortificationAtCell(uint faction, int cell)
        {
            int index = FortificationAtCell(cell);
            return index >= 0 && world.Buildings[index].FactionId == faction;
        }

        // Belts are laid only on open cells, so with Fortify a blocked belt cell has a wall or gate on it.
        // Raid target searches call this for every candidate, so it must not walk the building list.
        private bool BeltUnderFortification(int cell)
            => FortifyOn && !world.Map.IsPassable(cell);

        private void AddFortifyWallOverBelt(uint faction, int cell)
        {
            if (!FortifyOn || cell < 0 || cell >= world.Belts.Length || world.Belts[cell].FactionId == 0) return;
            world.Economies[faction - 1].FortifyWallOverBelt = checked(world.Economies[faction - 1].FortifyWallOverBelt + 1);
        }

        private void RemoveFortifyWallOverBelt(BuildingState building)
        {
            if (!FortifyOn || (building.Kind != BuildingKind.Wall && building.Kind != BuildingKind.Gate)) return;
            foreach (int cell in Footprint(building))
            {
                if (cell < 0 || cell >= world.Belts.Length || world.Belts[cell].FactionId == 0) continue;
                ref var economy = ref world.Economies[building.FactionId - 1];
                if (economy.FortifyWallOverBelt > 0) economy.FortifyWallOverBelt--;
            }
        }

        private int CountFortifyRingGaps(uint faction)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position);
            int centreX = core % width, centreZ = core / width, radius = world.Config.Economy.AiWallRadiusCells;
            int gaps = 0, terrainGaps = 0, buildingGaps = 0, resourceGaps = 0, plannedGaps = 0;
            for (int z = Math.Max(0, centreZ - radius); z <= Math.Min(height - 1, centreZ + radius); z++)
                for (int x = Math.Max(0, centreX - radius); x <= Math.Min(width - 1, centreX + radius); x++)
                {
                    if (Math.Max(Math.Abs(x - centreX), Math.Abs(z - centreZ)) != radius) continue;
                    int cell = z * width + x;
                    if (FortificationAtCell(cell) >= 0) continue;
                    bool occupied = false;
                    for (int i = 0; i < world.BuildingCount && !occupied; i++)
                        if (IsActualBuilding(world.Buildings[i]) && Array.IndexOf(Footprint(world.Buildings[i]), cell) >= 0) occupied = true;
                    if (occupied) { buildingGaps++; continue; }
                    if (!world.Map.IsPassable(cell)) { terrainGaps++; continue; }
                    if (IsReservedBuildingCell(cell)) { plannedGaps++; gaps++; continue; }
                    if (IsNodeCell(cell)) { resourceGaps++; gaps++; continue; }
                    gaps++;
                }
            world.Economies[faction - 1].FortifyRingTerrainGaps = terrainGaps;
            world.Economies[faction - 1].FortifyRingBuildingGaps = buildingGaps;
            world.Economies[faction - 1].FortifyRingResourceGaps = resourceGaps;
            world.Economies[faction - 1].FortifyRingPlannedGaps = plannedGaps;
            return gaps;
        }

        private void DecideFortify(uint faction)
        {
            var economy = world.Economies[faction - 1];
            var rules = world.Config.Economy;
            bool stageTwoFortify = rules.AiLevelAtLeast(faction, 2);
            if (stageTwoFortify && world.Tick >= rules.LatePushAfterTicks) return;
            int stoneReserve = stageTwoFortify ? 0 : rules.AiWallStoneReserve;
            if (economy.Age < 1 || economy.Stone <= stoneReserve)
            {
                if (economy.Age >= 1 && stageTwoFortify) world.Economies[faction - 1].FortifyRingGaps = CountFortifyRingGaps(faction);
                return;
            }
            int core = world.Map.Cell(OwnCore(faction).Definition.Position);
            int enemyCore = world.Map.Cell(world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position);
            int[] route = world.Map.FindPath(core, world.Map.Center(enemyCore), faction);
            if (route.Length == 0) route = world.Map.FindBreakablePath(core, world.Map.Center(enemyCore), faction);
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int radius = rules.AiWallRadiusCells;
            int cx = core % width, cz = core / width;
            var gates = new List<int>();
            var walls = new List<int>();
            for (int z = Math.Max(0, cz - radius); z <= Math.Min(height - 1, cz + radius); z++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); x++)
                {
                    if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) != radius) continue;
                    int cell = z * width + x;
                    bool routeCell = Array.IndexOf(route, cell) >= 0;
                    bool sideMiddle = x == cx && (z == cz - radius || z == cz + radius)
                        || z == cz && (x == cx - radius || x == cx + radius);
                    if (routeCell || sideMiddle) gates.Add(cell); else walls.Add(cell);
                }
            // Review (11-04): count only cells actually placed, so the ring keeps growing past its first cells.
            int placed = 0;
            for (int i = 0; i < gates.Count && placed < rules.AiWallCellsPerDecision; i++)
            {
                if (world.Economies[faction - 1].Stone - stoneReserve < rules.GateStoneCost) break;
                int before = world.Economies[faction - 1].FortifyGateCells;
                TryPlaceGate(faction, EconomyCommand.PlaceGate(faction, 0, gates[i]));
                if (world.Economies[faction - 1].FortifyGateCells != before) placed++;
            }
            for (int i = 0; i < walls.Count && placed < rules.AiWallCellsPerDecision; i++)
            {
                if (world.Economies[faction - 1].Stone - stoneReserve < rules.FortifyWallStoneCost) break;
                int before = world.Economies[faction - 1].FortifyWallCells;
                PlaceWalls(faction, EconomyCommand.PlaceWall(faction, 0, new[] { walls[i] }));
                if (world.Economies[faction - 1].FortifyWallCells != before) placed++;
            }
            world.Economies[faction - 1].FortifyRingGaps = CountFortifyRingGaps(faction);
        }

        private bool StageTwoFortifyStoneNeeded(uint faction, int stoneWorkers)
        {
            if (!FortifyOn || !world.Config.Economy.AiLevelAtLeast(faction, 2)) return false;
            var economy = world.Economies[faction - 1];
            var rules = world.Config.Economy;
            if (economy.Age < 1 || world.Tick >= rules.LatePushAfterTicks || economy.FortifyRingGaps <= 0) return false;
            int buildableGaps = Math.Max(0, economy.FortifyRingGaps - economy.FortifyRingResourceGaps - economy.FortifyRingPlannedGaps);
            int target = checked(buildableGaps * rules.GateStoneCost + rules.AiWallStoneReserve);
            return economy.Stone < target && stoneWorkers < 32;
        }

    }
}
