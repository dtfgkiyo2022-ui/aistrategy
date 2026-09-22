using System;
using System.Collections.Generic;
using System.Globalization;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// Ver.3 random map (technical-design-v3 2). Runs once before a match and returns an ordinary scenario; Step never
    /// calls it, and a replay stores the generated scenario rather than the seed, so replays never depend on this code.
    /// It is still deterministic on its own - integers and SplitMix64 only, draws in a fixed order - so two machines can
    /// build the same map from a seed and compare Config.Hash before an online match.
    /// Changing what a seed produces requires a new <see cref="Version"/>.
    /// </summary>
    public static partial class MapGenerator
    {
        public const string Version = "mapgen-1";

        private const int WidthMeters = 256, HeightMeters = 128, CellMeters = 2;
        private const int Columns = WidthMeters / CellMeters, Rows = HeightMeters / CellMeters;
        private const int MinCoreDistance = 150;
        private const int OutpostOffset = 32, OutpostJitter = 8, EdgeMargin = 8;
        private const int CoreClearance = 16, OutpostClearance = 10, NodeClearance = 3;
        private const int GuaranteedInner = 12, GuaranteedOuter = 30;
        private const int GuaranteedWood = 4, GuaranteedFood = 3, ScatteredWood = 16, ScatteredFood = 10;
        private const int WoodAmount = 300, FoodAmount = 200;
        private const int MaxBlockedPermille = 200;
        private const int ObstacleAttempts = 64, PlacementDraws = 100000;

        /// <summary>
        /// V3-2 (technical-design-v3 14): the mapgen-1 economy map of the same seed, unchanged, with ore points drawn after
        /// everything else. So the ground, the cores and the food and wood points of a seed are the same in both versions.
        /// </summary>
        public const string IndustryVersion = "mapgen-2";

        /// <summary>Metal per infantry on a mapgen-2 map (12.4): the line decides how fast the army grows.</summary>
        private const int InfantryMetal = 5;
        private const int GuaranteedOre = 2, ScatteredOre = 6, OreInner = 16, OreOuter = 36, OreAmount = 400;

        public static ScenarioDefinition Generate(ulong seed) => Generate(seed, false);

        public static ScenarioDefinition Generate(ulong seed, bool economy) => Generate(seed, economy, false);

        /// <summary>
        /// With <paramref name="economy"/>, the same ground (no extra draws) plus the V3-1 economy: rules enabled and three
        /// villagers behind each core. Soldiers stay the week-two set until production exists (V3-1 PR3).
        /// With <paramref name="industry"/> (needs the economy), mapgen-2: ore points and the V3-2 industry rules on top.
        /// </summary>
        public static ScenarioDefinition Generate(ulong seed, bool economy, bool industry)
        {
            if (industry && !economy) throw new ArgumentException("Industry needs the economy.");
            var rng = new SplitMix64(seed);
            // The Ver.1 base keeps unit parameters, rules, factions and the four armies per side that the automatic AI
            // is written for (north, south, reserve, scout). Only the ground under them changes.
            var s = WeekOneScenario.Create();
            s.ScenarioId = "gen1-" + seed.ToString(CultureInfo.InvariantCulture);
            s.Seed = seed;

            // 1. Cores: west half and east half, far enough apart.
            int wx, wz, ex, ez;
            do
            {
                wx = Snap(Range(rng, 20, 60)); wz = Snap(Range(rng, 20, 108));
                ex = Snap(Range(rng, 196, 236)); ez = Snap(Range(rng, 20, 108));
            } while (Square(ex - wx) + Square(ez - wz) < (long)MinCoreDistance * MinCoreDistance);
            s.Cores[0].Position = Point(wx, wz);
            s.Cores[1].Position = Point(ex, ez);

            // 3. Outposts either side of the line between the cores. The one with the larger z is "north" (id 1),
            // because the Ver.1 automatic AI and the presets name the two outposts north and south (9.2).
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

            // 4. Guaranteed resources near each core (the fairness rule of V3-1).
            var nodes = new List<ResourceNodeDefinition>();
            var usedCells = new HashSet<int>();
            for (int f = 0; f < 2; f++)
            {
                int cx = f == 0 ? wx : ex, cz = f == 0 ? wz : ez;
                for (int i = 0; i < GuaranteedWood + GuaranteedFood; i++)
                {
                    var kind = i < GuaranteedWood ? ResourceKind.Wood : ResourceKind.Food;
                    int nx = 0, nz = 0, draws = 0;
                    while (true)
                    {
                        if (++draws > PlacementDraws) throw new InvalidOperationException("Map generation could not place a guaranteed resource.");
                        int ox = Range(rng, -GuaranteedOuter, GuaranteedOuter), oz = Range(rng, -GuaranteedOuter, GuaranteedOuter);
                        long d2 = Square(ox) + Square(oz);
                        if (d2 < Square(GuaranteedInner) || d2 > Square(GuaranteedOuter)) continue;
                        nx = cx + ox; nz = cz + oz;
                        if (nx < 1 || nx >= WidthMeters - 1 || nz < 1 || nz >= HeightMeters - 1) continue;
                        nx = Snap(nx); nz = Snap(nz);
                        if (usedCells.Add(CellOf(nx, nz))) break;
                    }
                    nodes.Add(Node(nodes.Count + 1, kind, nx, nz));
                }
            }

            // 2 and 6. Obstacles, redrawn (not the cores or resources) until everything that matters is connected.
            var keepClear = new List<int[]>
            {
                new[] { wx, wz, CoreClearance }, new[] { ex, ez, CoreClearance },
                new[] { Coord(s.Outposts[0].Position.X), Coord(s.Outposts[0].Position.Z), OutpostClearance },
                new[] { Coord(s.Outposts[1].Position.X), Coord(s.Outposts[1].Position.Z), OutpostClearance }
            };
            foreach (var n in nodes) keepClear.Add(new[] { Coord(n.Position.X), Coord(n.Position.Z), NodeClearance });
            var mustReach = new List<int> { CellOf(ex, ez),
                CellOf(Coord(s.Outposts[0].Position.X), Coord(s.Outposts[0].Position.Z)),
                CellOf(Coord(s.Outposts[1].Position.X), Coord(s.Outposts[1].Position.Z)) };
            foreach (var n in nodes) mustReach.Add(CellOf(Coord(n.Position.X), Coord(n.Position.Z)));

            bool[] blocked = null, reachable = null;
            for (int attempt = 0; ; attempt++)
            {
                if (attempt == ObstacleAttempts) throw new InvalidOperationException("Map generation could not connect the map in " + ObstacleAttempts + " attempts.");
                blocked = Obstacles(rng, keepClear);
                reachable = Reachable(blocked, CellOf(wx, wz));
                bool connected = true;
                foreach (int cell in mustReach) if (!reachable[cell]) { connected = false; break; }
                if (connected) break;
            }
            var blockedIds = new List<int>();
            for (int i = 0; i < blocked.Length; i++) if (blocked[i]) blockedIds.Add(i);
            s.Map.BlockedCellIds = blockedIds.ToArray();

            // 5. Scattered resources on reachable ground, away from both starts.
            for (int i = 0; i < ScatteredWood + ScatteredFood; i++)
            {
                var kind = i < ScatteredWood ? ResourceKind.Wood : ResourceKind.Food;
                int draws = 0, cell;
                while (true)
                {
                    if (++draws > PlacementDraws) throw new InvalidOperationException("Map generation could not place a scattered resource.");
                    cell = Range(rng, 0, Columns * Rows - 1);
                    if (!reachable[cell] || usedCells.Contains(cell)) continue;
                    int x = (cell % Columns) * CellMeters + 1, z = (cell / Columns) * CellMeters + 1;
                    if (Square(x - wx) + Square(z - wz) <= Square(GuaranteedOuter) || Square(x - ex) + Square(z - ez) <= Square(GuaranteedOuter)) continue;
                    usedCells.Add(cell);
                    nodes.Add(Node(nodes.Count + 1, kind, x, z));
                    break;
                }
            }
            s.ResourceNodes = nodes.ToArray();

            // 7. The Ver.1 week-two soldiers (8 north, 8 south, 2 reserve, 2 scouts per side), set down next to their own
            // core instead of the fixed road positions. All of them fall inside the core's clear radius.
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
            if (economy)
            {
                s.ScenarioId = "gen1e-" + seed.ToString(CultureInfo.InvariantCulture);
                s.Economy = new EconomyRules { Enabled = true };
                // Soldiers are capped by the population now; the Ver.1 faction cap must not stop production first.
                s.Rules.FactionCap = s.Economy.PopulationCap;
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
            if (industry)
            {
                // Drawn last, so every earlier draw - and with it the whole mapgen-1 map - stays as it was.
                s.ScenarioId = "gen2i-" + seed.ToString(CultureInfo.InvariantCulture);
                s.Economy.Industry = true;
                s.Economy.InfantryMetalCost = InfantryMetal;
                for (int f = 0; f < 2; f++)
                {
                    int cx = f == 0 ? wx : ex, cz = f == 0 ? wz : ez;
                    for (int i = 0; i < GuaranteedOre; i++)
                    {
                        int draws = 0, nx, nz;
                        while (true)
                        {
                            if (++draws > PlacementDraws) throw new InvalidOperationException("Map generation could not place a guaranteed ore point.");
                            int ox = Range(rng, -OreOuter, OreOuter), oz = Range(rng, -OreOuter, OreOuter);
                            long d2 = Square(ox) + Square(oz);
                            if (d2 < Square(OreInner) || d2 > Square(OreOuter)) continue;
                            nx = cx + ox; nz = cz + oz;
                            if (nx < 1 || nx >= WidthMeters - 1 || nz < 1 || nz >= HeightMeters - 1) continue;
                            nx = Snap(nx); nz = Snap(nz);
                            int cell = CellOf(nx, nz);
                            if (!reachable[cell] || usedCells.Contains(cell)) continue;
                            usedCells.Add(cell);
                            break;
                        }
                        nodes.Add(Node(nodes.Count + 1, ResourceKind.Ore, nx, nz));
                    }
                }
                for (int i = 0; i < ScatteredOre; i++)
                {
                    int draws = 0, cell;
                    while (true)
                    {
                        if (++draws > PlacementDraws) throw new InvalidOperationException("Map generation could not place a scattered ore point.");
                        cell = Range(rng, 0, Columns * Rows - 1);
                        if (!reachable[cell] || usedCells.Contains(cell)) continue;
                        int x = (cell % Columns) * CellMeters + 1, z = (cell / Columns) * CellMeters + 1;
                        if (Square(x - wx) + Square(z - wz) <= Square(OreOuter) || Square(x - ex) + Square(z - ez) <= Square(OreOuter)) continue;
                        usedCells.Add(cell);
                        nodes.Add(Node(nodes.Count + 1, ResourceKind.Ore, x, z));
                        break;
                    }
                }
                s.ResourceNodes = nodes.ToArray();
            }
            return s;
        }

        private static bool[] Obstacles(SplitMix64 rng, List<int[]> keepClear)
        {
            var blocked = new bool[Columns * Rows];
            int limit = Columns * Rows * MaxBlockedPermille / 1000, total = 0;
            int count = Range(rng, 6, 10);
            for (int k = 0; k < count; k++)
            {
                int w = Range(rng, 4, 20), h = Range(rng, 4, 20);
                int x0 = Range(rng, 0, Columns - w), z0 = Range(rng, 0, Rows - h);
                for (int z = z0; z < z0 + h; z++)
                    for (int x = x0; x < x0 + w; x++)
                    {
                        int cell = z * Columns + x;
                        if (blocked[cell] || total >= limit || InClearZone(x, z, keepClear)) continue;
                        blocked[cell] = true;
                        total++;
                    }
            }
            return blocked;
        }

        private static bool InClearZone(int column, int row, List<int[]> keepClear)
        {
            int x = column * CellMeters + 1, z = row * CellMeters + 1;
            foreach (var c in keepClear)
                if (Square(x - c[0]) + Square(z - c[1]) <= Square(c[2])) return true;
            return false;
        }

        /// <summary>4-neighbour flood fill over passable cells, in a fixed order.</summary>
        public static bool[] Reachable(bool[] blocked, int start)
        {
            var seen = new bool[blocked.Length];
            if (blocked[start]) return seen;
            var queue = new Queue<int>();
            queue.Enqueue(start); seen[start] = true;
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(), x = cell % Columns, z = cell / Columns;
                if (z + 1 < Rows) Visit(cell + Columns);
                if (x + 1 < Columns) Visit(cell + 1);
                if (z > 0) Visit(cell - Columns);
                if (x > 0) Visit(cell - 1);
            }
            return seen;

            void Visit(int next)
            {
                if (seen[next] || blocked[next]) return;
                seen[next] = true;
                queue.Enqueue(next);
            }
        }

        private static ResourceNodeDefinition Node(int id, ResourceKind kind, int x, int z) => new ResourceNodeDefinition
        { Id = (uint)id, Kind = kind, Position = Point(x, z),
          Amount = kind == ResourceKind.Wood ? WoodAmount : kind == ResourceKind.Ore ? OreAmount : kind == ResourceKind.Stone ? StoneAmount : FoodAmount };

        /// <summary>Uniform integer in [min, max] (inclusive), by rejection sampling in SplitMix64.</summary>
        private static int Range(SplitMix64 rng, int min, int max) => min + (int)rng.NextUInt64((ulong)(max - min + 1));
        /// <summary>The centre of the 2 m cell containing this meter coordinate (always odd).</summary>
        private static int Snap(int meters) => meters / CellMeters * CellMeters + 1;
        private static int Clamp(int value, int size) => value < EdgeMargin ? EdgeMargin : value > size - EdgeMargin ? size - EdgeMargin : value;
        private static int CellOf(int x, int z) => z / CellMeters * Columns + x / CellMeters;
        private static int Coord(Fix64 value) => (int)(value.Raw / 65536);
        private static long Square(long v) => v * v;
        private static SimPoint Point(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        private static long SquareRoot(long value)
        {
            long lo = 0, hi = 1 << 20;
            while (lo < hi)
            {
                long mid = (lo + hi + 1) / 2;
                if (mid * mid <= value) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
    }
}
