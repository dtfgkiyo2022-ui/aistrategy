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
                e.FortifyRepairFittings, e.FortifyRepairRope);
        }

        private void DecideFortify(uint faction)
        {
            var economy = world.Economies[faction - 1];
            var rules = world.Config.Economy;
            if (economy.Age < 1 || economy.Stone <= rules.AiWallStoneReserve) return;
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
                if (world.Economies[faction - 1].Stone - rules.AiWallStoneReserve < rules.GateStoneCost) break;
                int before = world.Economies[faction - 1].FortifyGateCells;
                TryPlaceGate(faction, EconomyCommand.PlaceGate(faction, 0, gates[i]));
                if (world.Economies[faction - 1].FortifyGateCells != before) placed++;
            }
            for (int i = 0; i < walls.Count && placed < rules.AiWallCellsPerDecision; i++)
            {
                if (world.Economies[faction - 1].Stone - rules.AiWallStoneReserve < rules.FortifyWallStoneCost) break;
                int before = world.Economies[faction - 1].FortifyWallCells;
                PlaceWalls(faction, EconomyCommand.PlaceWall(faction, 0, new[] { walls[i] }));
                if (world.Economies[faction - 1].FortifyWallCells != before) placed++;
            }
        }
    }
}
