using System;
using System.Collections.Generic;
using System.Globalization;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-4 terrain map (technical-design-v3 28.2), mapgen-3. The same cores and outposts as mapgen-1 for a seed, then a
    /// river with fords across the middle, mountains and forests instead of the plain rectangles, one feature near each
    /// core that leans it toward a civilisation (a mountain for metallurgy, a pond for farming), and resources along the
    /// terrain: wood at forest edges, food on river banks, ore at the foot of mountains. Integers and SplitMix64 only.
    /// </summary>
    public static partial class MapGenerator
    {
        public const string TerrainVersion = "mapgen-3";

        private const int RiverMinColumn = 45, RiverMaxColumn = 83, RiverWidth = 2, FordRows = 4, ExtraFords = 2;
        private const int MaxTerrainPermille = 250;
        private const int LeanInner = 20, LeanOuter = 36, PondInner = 16, PondOuter = 30;
        private const int EdgeWood = 2, EdgeOre = 2, LeanOre = 3, PondFood = 3, RiverFood = 6;
        private const int TerrainScatteredWood = 8, TerrainScatteredFood = 5;

        /// <summary>What the ground near a core favours (28.2 step 4).</summary>
        public enum CoreLean : byte { Mountain = 0, River = 1 }

        /// <summary>The terrain map of <paramref name="seed"/>: economy, industry and terrain on.</summary>
        public static ScenarioDefinition GenerateTerrain(ulong seed) => GenerateTerrain(seed, out _);

        public static ScenarioDefinition GenerateTerrain(ulong seed, out CoreLean[] leans)
        {
            var rng = new SplitMix64(seed);
            var s = WeekOneScenario.Create();
            s.ScenarioId = "gen3t-" + seed.ToString(CultureInfo.InvariantCulture);
            s.Seed = seed;

            // 1. Cores and outposts, drawn exactly as mapgen-1 draws them.
            int wx, wz, ex, ez;
            do
            {
                wx = Snap(Range(rng, 20, 60)); wz = Snap(Range(rng, 20, 108));
                ex = Snap(Range(rng, 196, 236)); ez = Snap(Range(rng, 20, 108));
            } while (Square(ex - wx) + Square(ez - wz) < (long)MinCoreDistance * MinCoreDistance);
            s.Cores[0].Position = Point(wx, wz);
            s.Cores[1].Position = Point(ex, ez);
            long dx = ex - wx, dz = ez - wz, length = SquareRoot(dx * dx + dz * dz);
            int px = (int)(-dz * OutpostOffset / length), pz = (int)(dx * OutpostOffset / length);
            int mx = (wx + ex) / 2, mz = (wz + ez) / 2;
            int ax = Clamp(mx + px + Range(rng, -OutpostJitter, OutpostJitter), WidthMeters);
            int az = Clamp(mz + pz + Range(rng, -OutpostJitter, OutpostJitter), HeightMeters);
            int bx = Clamp(mx - px + Range(rng, -OutpostJitter, OutpostJitter), WidthMeters);
            int bz = Clamp(mz - pz + Range(rng, -OutpostJitter, OutpostJitter), HeightMeters);
            bool aNorth = az >= bz;
            s.Outposts[0].Position = aNorth ? Point(Snap(ax), Snap(az)) : Point(Snap(bx), Snap(bz));
            s.Outposts[1].Position = aNorth ? Point(Snap(bx), Snap(bz)) : Point(Snap(ax), Snap(az));
            for (uint f = 1; f <= 2; f++)
                for (int a = 0; a < 2; a++)
                    s.Armies[(f - 1) * 4 + a].HomeObjective = new PolicyGoal(GoalKind.Outpost, (uint)a + 1, default);
            int[] coreX = { wx, ex }, coreZ = { wz, ez };
            int[] postX = { Coord(s.Outposts[0].Position.X), Coord(s.Outposts[1].Position.X) };
            int[] postZ = { Coord(s.Outposts[0].Position.Z), Coord(s.Outposts[1].Position.Z) };

            // 4. The lean of each core, drawn once: the terrain may be redrawn below, the leans stay.
            leans = new[] { (CoreLean)Range(rng, 0, 1), (CoreLean)Range(rng, 0, 1) };

            // 2, 3, 4 and 6. Terrain, redrawn until the cores and outposts connect.
            byte[] terrain = null;
            bool[] reachable = null;
            var features = new List<int[]>(); // kind, centre column, centre row: whose edges carry resources
            for (int attempt = 0; ; attempt++)
            {
                if (attempt == ObstacleAttempts) throw new InvalidOperationException("Map generation could not connect the terrain in " + ObstacleAttempts + " attempts.");
                features.Clear();
                terrain = DrawTerrain(rng, coreX, coreZ, postX, postZ, leans, features);
                var blocked = new bool[terrain.Length];
                for (int i = 0; i < terrain.Length; i++) blocked[i] = terrain[i] != 0;
                reachable = Reachable(blocked, CellOf(wx, wz));
                if (reachable[CellOf(ex, ez)] && reachable[CellOf(postX[0], postZ[0])] && reachable[CellOf(postX[1], postZ[1])]) break;
            }
            s.Map.Terrain = terrain;
            var blockedIds = new List<int>();
            for (int i = 0; i < terrain.Length; i++) if (terrain[i] != 0) blockedIds.Add(i);
            s.Map.BlockedCellIds = blockedIds.ToArray();

            // 5. Resources: the V3-1 guaranteed ring, then along the terrain, then a few scattered.
            var nodes = new List<ResourceNodeDefinition>();
            var used = new HashSet<int>();
            for (int f = 0; f < 2; f++)
                for (int i = 0; i < GuaranteedWood + GuaranteedFood; i++)
                {
                    var kind = i < GuaranteedWood ? ResourceKind.Wood : ResourceKind.Food;
                    int cell = DrawCell(rng, reachable, used, c => InRing(c, coreX[f], coreZ[f], GuaranteedInner, GuaranteedOuter));
                    nodes.Add(Node(nodes.Count + 1, kind, CenterX(cell), CenterZ(cell)));
                }
            foreach (var feature in features)
            {
                var kind = (TerrainKind)feature[0];
                int count = feature[3];
                var resource = kind == TerrainKind.Forest ? ResourceKind.Wood : kind == TerrainKind.Mountain ? ResourceKind.Ore : ResourceKind.Food;
                var edge = EdgeCells(terrain, reachable, used, kind, feature[1], feature[2], feature[4]);
                for (int i = 0; i < count && edge.Count > 0; i++)
                {
                    int pick = Range(rng, 0, edge.Count - 1);
                    int cell = edge[pick];
                    edge.RemoveAt(pick);
                    used.Add(cell);
                    nodes.Add(Node(nodes.Count + 1, resource, CenterX(cell), CenterZ(cell)));
                }
            }
            for (int i = 0; i < TerrainScatteredWood + TerrainScatteredFood; i++)
            {
                var kind = i < TerrainScatteredWood ? ResourceKind.Wood : ResourceKind.Food;
                int cell = DrawCell(rng, reachable, used, c => !InRing(c, coreX[0], coreZ[0], 0, GuaranteedOuter) && !InRing(c, coreX[1], coreZ[1], 0, GuaranteedOuter));
                nodes.Add(Node(nodes.Count + 1, kind, CenterX(cell), CenterZ(cell)));
            }
            s.ResourceNodes = nodes.ToArray();

            // 7. Soldiers and villagers as on the economy maps; the rules of mapgen-2.
            PlaceStartingUnits(s, wx, wz, ex, ez);
            s.Economy = new EconomyRules { Enabled = true, Industry = true, InfantryMetalCost = InfantryMetal };
            s.Rules.FactionCap = s.Economy.PopulationCap;
            return s;
        }

        private static byte[] DrawTerrain(SplitMix64 rng, int[] coreX, int[] coreZ, int[] postX, int[] postZ, CoreLean[] leans, List<int[]> features)
        {
            var terrain = new byte[Columns * Rows];
            var clear = new List<int[]>
            {
                new[] { coreX[0], coreZ[0], CoreClearance }, new[] { coreX[1], coreZ[1], CoreClearance },
                new[] { postX[0], postZ[0], OutpostClearance }, new[] { postX[1], postZ[1], OutpostClearance }
            };
            int limit = Columns * Rows * MaxTerrainPermille / 1000, total = 0;

            // The river: north to south, drifting a column at most every four rows, two cells wide.
            int column = Range(rng, RiverMinColumn, RiverMaxColumn - RiverWidth);
            var riverColumn = new int[Rows];
            for (int row = 0; row < Rows; row++)
            {
                if (row % 4 == 0 && row > 0) column = Math.Max(RiverMinColumn, Math.Min(RiverMaxColumn - RiverWidth, column + Range(rng, -1, 1)));
                riverColumn[row] = column;
                for (int w = 0; w < RiverWidth; w++) Mark(terrain, column + w, row, TerrainKind.River, clear, ref total, limit);
            }
            // Fords: one on each outpost's row, and two more anywhere.
            var fords = new List<int> { postZ[0] / CellMeters, postZ[1] / CellMeters };
            for (int i = 0; i < ExtraFords; i++) fords.Add(Range(rng, 0, Rows - 1));
            foreach (int ford in fords)
                for (int row = ford - FordRows / 2; row < ford - FordRows / 2 + FordRows; row++)
                {
                    if (row < 0 || row >= Rows) continue;
                    for (int w = 0; w < RiverWidth; w++)
                    {
                        int cell = row * Columns + riverColumn[row] + w;
                        if (terrain[cell] == (byte)TerrainKind.River) { terrain[cell] = 0; total--; }
                    }
                }
            // Food anywhere along the banks: the search reaches the whole map.
            features.Add(new[] { (int)TerrainKind.River, riverColumn[Rows / 2], Rows / 2, RiverFood, Columns });

            // The feature that leans each core.
            for (int f = 0; f < 2; f++)
            {
                bool mountain = leans[f] == CoreLean.Mountain;
                int inner = mountain ? LeanInner : PondInner, outer = mountain ? LeanOuter : PondOuter;
                int ox, oz;
                do { ox = Range(rng, -outer, outer); oz = Range(rng, -outer, outer); }
                while (Square(ox) + Square(oz) < Square(inner) || Square(ox) + Square(oz) > Square(outer));
                int cx = Math.Max(0, Math.Min(Columns - 1, (coreX[f] + ox) / CellMeters)), cz = Math.Max(0, Math.Min(Rows - 1, (coreZ[f] + oz) / CellMeters));
                int rx = mountain ? Range(rng, 3, 5) : Range(rng, 2, 3), rz = mountain ? Range(rng, 3, 5) : Range(rng, 2, 3);
                Blob(terrain, cx, cz, rx, rz, mountain ? TerrainKind.Mountain : TerrainKind.River, clear, ref total, limit);
                features.Add(new[] { (int)(mountain ? TerrainKind.Mountain : TerrainKind.River), cx, cz, mountain ? LeanOre : PondFood, Math.Max(rx, rz) + 2 });
            }

            // Mountains, then forests, anywhere.
            int mountains = Range(rng, 3, 5), forests = Range(rng, 5, 8);
            for (int k = 0; k < mountains + forests; k++)
            {
                var kind = k < mountains ? TerrainKind.Mountain : TerrainKind.Forest;
                int cx = Range(rng, 0, Columns - 1), cz = Range(rng, 0, Rows - 1), rx = Range(rng, 2, 6), rz = Range(rng, 2, 6);
                Blob(terrain, cx, cz, rx, rz, kind, clear, ref total, limit);
                features.Add(new[] { (int)kind, cx, cz, kind == TerrainKind.Mountain ? EdgeOre : EdgeWood, Math.Max(rx, rz) + 2 });
            }
            return terrain;
        }

        /// <summary>An axis-aligned ellipse of one kind, only over plain ground outside the clear zones.</summary>
        private static void Blob(byte[] terrain, int cx, int cz, int rx, int rz, TerrainKind kind, List<int[]> clear, ref int total, int limit)
        {
            long rx2 = (long)rx * rx, rz2 = (long)rz * rz;
            for (int z = cz - rz; z <= cz + rz; z++)
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x < 0 || z < 0 || x >= Columns || z >= Rows) continue;
                    long ddx = x - cx, ddz = z - cz;
                    if (ddx * ddx * rz2 + ddz * ddz * rx2 > rx2 * rz2) continue;
                    Mark(terrain, x, z, kind, clear, ref total, limit);
                }
        }

        private static void Mark(byte[] terrain, int column, int row, TerrainKind kind, List<int[]> clear, ref int total, int limit)
        {
            if (column < 0 || row < 0 || column >= Columns || row >= Rows) return;
            int cell = row * Columns + column;
            if (terrain[cell] != 0 || total >= limit || InClearZone(column, row, clear)) return;
            terrain[cell] = (byte)kind;
            total++;
        }

        /// <summary>
        /// Open, reachable, unused cells next to terrain of <paramref name="kind"/> within <paramref name="radius"/> cells of
        /// the feature centre, in cell order (the draw picks among them).
        /// </summary>
        private static List<int> EdgeCells(byte[] terrain, bool[] reachable, HashSet<int> used, TerrainKind kind, int cx, int cz, int radius)
        {
            var cells = new List<int>();
            for (int z = Math.Max(0, cz - radius); z <= Math.Min(Rows - 1, cz + radius); z++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(Columns - 1, cx + radius); x++)
                {
                    int cell = z * Columns + x;
                    if (terrain[cell] != 0 || !reachable[cell] || used.Contains(cell)) continue;
                    if (Next(x, z + 1) || Next(x + 1, z) || Next(x, z - 1) || Next(x - 1, z)) cells.Add(cell);
                }
            return cells;
            bool Next(int x, int z) => x >= 0 && z >= 0 && x < Columns && z < Rows && terrain[z * Columns + x] == (byte)kind;
        }

        private static int DrawCell(SplitMix64 rng, bool[] reachable, HashSet<int> used, Func<int, bool> fits)
        {
            for (int draws = 0; draws < PlacementDraws; draws++)
            {
                int cell = Range(rng, 0, Columns * Rows - 1);
                if (!reachable[cell] || used.Contains(cell) || !fits(cell)) continue;
                used.Add(cell);
                return cell;
            }
            throw new InvalidOperationException("Map generation could not place a resource on the terrain map.");
        }

        private static bool InRing(int cell, int x, int z, int inner, int outer)
        {
            long d = Square(CenterX(cell) - x) + Square(CenterZ(cell) - z);
            return d >= Square(inner) && d <= Square(outer);
        }

        /// <summary>The week-two soldiers and three villagers per side, set down next to their own core as on mapgen-1.</summary>
        private static void PlaceStartingUnits(ScenarioDefinition s, int wx, int wz, int ex, int ez)
        {
            s.Soldiers = new SoldierDefinition[40];
            int[] counts = { 8, 8, 2, 2 };
            uint id = 1;
            for (uint f = 1; f <= 2; f++)
            {
                int cx = f == 1 ? wx : ex, cz = f == 1 ? wz : ez, toward = f == 1 ? 1 : -1;
                for (int a = 0; a < 4; a++)
                    for (int i = 0; i < counts[a]; i++)
                    {
                        int baseX = cx + toward * (a == 2 ? 6 : a == 3 ? 10 : 0);
                        int baseZ = cz + (a == 0 ? 8 : a == 1 ? -9 : 0);
                        s.Soldiers[id - 1] = new SoldierDefinition { Id = id, FactionId = f, ArmyId = (f - 1) * 4 + (uint)a + 1,
                            Kind = a == 3 ? UnitKind.Scout : UnitKind.Infantry, Alive = true, Hp = a == 3 ? 40 : 100,
                            Position = Point(baseX + toward * (i % 4), baseZ + i / 4) };
                        id++;
                    }
            }
            s.Villagers = new VillagerDefinition[6];
            for (uint f = 1; f <= 2; f++)
            {
                int cx = f == 1 ? wx : ex, cz = f == 1 ? wz : ez, away = f == 1 ? -1 : 1;
                for (int k = 0; k < 3; k++)
                {
                    uint vid = (f - 1) * 3 + (uint)k + 1;
                    s.Villagers[vid - 1] = new VillagerDefinition { Id = vid, FactionId = f, Position = Point(cx + away * 5, cz - 2 + k * 2) };
                }
            }
        }

        private static int CenterX(int cell) => (cell % Columns) * CellMeters + 1;
        private static int CenterZ(int cell) => (cell / Columns) * CellMeters + 1;
    }
}
