using System;
using System.Collections.Generic;
using System.Globalization;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// S-2 large terrain map. This is a separate generator version: the old generators and their random streams are not
    /// called from here. The layout is point-symmetric around the centre of the map, which gives both factions the same
    /// terrain, resource and outpost distances without using floating point arithmetic.
    /// </summary>
    public static partial class MapGenerator
    {
        public const string LargeVersion = "mapgen-4";

        private const int LargeWidthMeters = 512, LargeHeightMeters = 256, LargeCellMeters = 2;
        private const int LargeColumns = LargeWidthMeters / LargeCellMeters, LargeRows = LargeHeightMeters / LargeCellMeters;
        private const int LargeMinCoreDistance = 300, LargeObstacleAttempts = 64;
        private const int LargeCoreClearance = 20, LargeOutpostClearance = 12;
        private const int LargeGuaranteedInner = 12, LargeGuaranteedOuter = 30;
        private const int LargeGuaranteedWood = 4, LargeGuaranteedFood = 3;
        private const int LargeClusterNodes = 8;
        private const int LargeResourceAmount = 300, LargeFoodAmount = 200, LargeOreAmount = 400, LargeStoneAmount = 300;

        /// <summary>Creates the S-2 512m x 256m economy map. Optional flags have the same meaning as GenerateTerrain.</summary>
        public static ScenarioDefinition GenerateLarge(ulong seed, bool gold = false, bool processingChain = false,
            bool masonry = false, bool bridge = false)
        {
            var rng = new SplitMix64(seed);
            var s = WeekOneScenario.Create();
            s.ScenarioId = "gen4l-" + seed.ToString(CultureInfo.InvariantCulture);
            s.Seed = seed;
            s.Map = new MapDefinition { WidthMeters = LargeWidthMeters, HeightMeters = LargeHeightMeters,
                CellSizeMeters = LargeCellMeters, WidthCells = LargeColumns, HeightCells = LargeRows,
                DefaultPassable = true };

            int westX = LargeSnap(48), westZ = LargeSnap(LargeRange(rng, 48, 208));
            int eastX = LargeWidthMeters - westX, eastZ = LargeHeightMeters - westZ;
            s.Cores[0].Position = LargePoint(westX, westZ);
            s.Cores[1].Position = LargePoint(eastX, eastZ);
            s.Cores[0].Hp = s.Cores[1].Hp = 6000;

            // Three paired objectives: near the north edge, in the middle band, and near the south edge. IDs are stable:
            // west/east for each pair. The opposing side's armies use the matching member of each pair.
            int[] outpostX = { LargeSnap(236), LargeSnap(236), LargeSnap(236) };
            int[] outpostZ = { LargeSnap(36), LargeSnap(126), LargeSnap(220) };
            s.Outposts = new OutpostDefinition[6];
            for (int pair = 0; pair < 3; pair++)
            {
                uint westId = (uint)(pair * 2 + 1), eastId = westId + 1;
                s.Outposts[westId - 1] = new OutpostDefinition { Id = westId, Position = LargePoint(outpostX[pair], outpostZ[pair]) };
                s.Outposts[eastId - 1] = new OutpostDefinition { Id = eastId,
                    Position = LargePoint(LargeWidthMeters - outpostX[pair], LargeHeightMeters - outpostZ[pair]) };
            }
            uint[][] homePosts = { new uint[] { 1, 3, 5 }, new uint[] { 2, 4, 6 } };
            for (int faction = 0; faction < 2; faction++)
            {
                for (int army = 0; army < 3; army++)
                    s.Armies[faction * 4 + army].HomeObjective = new PolicyGoal(GoalKind.Outpost, homePosts[faction][army], default);
                s.Armies[faction * 4 + 3].HomeObjective = new PolicyGoal(GoalKind.Core, (uint)faction + 1, default);
            }

            byte[] terrain = null;
            bool[] reachable = null;
            for (int attempt = 0; attempt < LargeObstacleAttempts; attempt++)
            {
                terrain = DrawLargeTerrain(rng, westX, westZ, eastX, eastZ, outpostX, outpostZ);
                var blocked = LargeBlocked(terrain);
                reachable = LargeReachable(blocked, LargeCell(westX, westZ));
                bool connected = reachable[LargeCell(eastX, eastZ)];
                for (int i = 0; connected && i < s.Outposts.Length; i++)
                    connected = reachable[LargeCell(Coord(s.Outposts[i].Position.X), Coord(s.Outposts[i].Position.Z))];
                if (connected) break;
                if (attempt == LargeObstacleAttempts - 1)
                    throw new InvalidOperationException("Large map generation could not connect the terrain in " + LargeObstacleAttempts + " attempts.");
            }
            s.Map.Terrain = terrain;
            var blockedIds = new List<int>();
            for (int i = 0; i < terrain.Length; i++) if (terrain[i] != 0) blockedIds.Add(i);
            s.Map.BlockedCellIds = blockedIds.ToArray();

            var used = new HashSet<int> { LargeCell(westX, westZ), LargeCell(eastX, eastZ) };
            for (int i = 0; i < s.Outposts.Length; i++) used.Add(LargeCell(Coord(s.Outposts[i].Position.X), Coord(s.Outposts[i].Position.Z)));
            var nodes = new List<ResourceNodeDefinition>();
            for (int i = 0; i < LargeGuaranteedWood + LargeGuaranteedFood; i++)
            {
                ResourceKind kind = i < LargeGuaranteedWood ? ResourceKind.Wood : ResourceKind.Food;
                for (int faction = 0; faction < 2; faction++)
                {
                    // Draw only the west member and mirror it. This makes the resource guarantee itself symmetric, not just
                    // the count of nearby nodes.
                    if (faction != 0) continue;
                    int cell = FindLargeCell(rng, reachable, used, c => InLargeRing(c, westX, westZ, LargeGuaranteedInner, LargeGuaranteedOuter)
                        && c % LargeColumns < LargeColumns / 2 - 8
                        && reachable[LargeMirror(c)] && !used.Contains(LargeMirror(c)));
                    int mirror = LargeMirror(cell);
                    if (!reachable[mirror] || used.Contains(mirror)) throw new InvalidOperationException("Large map resource mirror is unavailable.");
                    used.Add(mirror);
                    nodes.Add(LargeNode(nodes.Count + 1, kind, cell));
                    nodes.Add(LargeNode(nodes.Count + 1, kind, mirror));
                }
            }

            // Four remote clusters, each with eight nodes per side. They are paired by the same point symmetry as the
            // cores and outposts. The fourth cluster becomes gold when the gold flag is requested for Academy trials.
            int[] clusterX = { 150, 180, 170, 210 }, clusterZ = { 54, 90, 190, 128 };
            ResourceKind[] clusterKinds = { ResourceKind.Wood, ResourceKind.Ore, ResourceKind.Stone, gold ? ResourceKind.Gold : ResourceKind.Food };
            for (int cluster = 0; cluster < clusterX.Length; cluster++)
                for (int i = 0; i < LargeClusterNodes; i++)
                {
                    int cell = FindLargeCell(rng, reachable, used, c => NearLargeCluster(c, clusterX[cluster], clusterZ[cluster], 18)
                        && c % LargeColumns < LargeColumns / 2 - 4
                        && reachable[LargeMirror(c)] && !used.Contains(LargeMirror(c)));
                    int mirror = LargeMirror(cell);
                    if (!reachable[mirror] || used.Contains(mirror)) throw new InvalidOperationException("Large map cluster mirror is unavailable.");
                    used.Add(mirror);
                    nodes.Add(LargeNode(nodes.Count + 1, clusterKinds[cluster], cell));
                    nodes.Add(LargeNode(nodes.Count + 1, clusterKinds[cluster], mirror));
                }
            s.ResourceNodes = nodes.ToArray();

            PlaceLargeStartingUnits(s, westX, westZ, eastX, eastZ);
            s.Economy = new EconomyRules { Enabled = true, Industry = true, InfantryMetalCost = 5, Ages = true,
                ProcessingChain = processingChain, Bridge = bridge, GoldEnabled = gold,
                Age3GoldCostAgrarian = 0, Age3GoldCostMetallurgy = 200 };
            s.Economy.AutoVillagerTarget = 20;
            s.Economy.AdvanceFoodCost = 240; s.Economy.AdvanceWoodCost = 180;
            s.Economy.Age2FoodCost = 480; s.Economy.Age2WoodCost = 300;
            s.Economy.Age3FoodCost = 720; s.Economy.Age3WoodCost = 480;
            s.Economy.Masonry = masonry;
            s.Rules.FactionCap = s.Economy.PopulationCap;
            return s;
        }

        private static byte[] DrawLargeTerrain(SplitMix64 rng, int westX, int westZ, int eastX, int eastZ, int[] outpostX, int[] outpostZ)
        {
            var terrain = new byte[LargeColumns * LargeRows];
            var clear = new List<int[]> { new[] { westX, westZ, LargeCoreClearance }, new[] { eastX, eastZ, LargeCoreClearance } };
            for (int i = 0; i < outpostX.Length; i++)
            {
                clear.Add(new[] { outpostX[i], outpostZ[i], LargeOutpostClearance });
                clear.Add(new[] { LargeWidthMeters - outpostX[i], LargeHeightMeters - outpostZ[i], LargeOutpostClearance });
            }
            int limit = LargeColumns * LargeRows * 230 / 1000, total = 0;
            // A two-cell river divides the map. Fords line up with all three outpost pairs and are also mirrored.
            for (int row = 0; row < LargeRows; row++)
                for (int column = 127; column <= 128; column++)
                    LargeMark(terrain, column, row, TerrainKind.River, clear, ref total, limit);
            int[] fordRows = { outpostZ[0] / LargeCellMeters, outpostZ[1] / LargeCellMeters, outpostZ[2] / LargeCellMeters };
            for (int i = 0; i < fordRows.Length; i++)
                for (int mirrored = 0; mirrored < 2; mirrored++)
                for (int row = (mirrored == 0 ? fordRows[i] : LargeRows - 1 - fordRows[i]) - 3;
                    row <= (mirrored == 0 ? fordRows[i] : LargeRows - 1 - fordRows[i]) + 3; row++)
                {
                    if (row < 0 || row >= LargeRows) continue;
                    for (int column = 127; column <= 128; column++)
                    {
                        int cell = row * LargeColumns + column;
                        if (terrain[cell] == (byte)TerrainKind.River) { terrain[cell] = 0; total--; }
                    }
                }
            // One civilisation-leaning feature near each core, mirrored as a pair. The feature is outside the core
            // clearance but close enough to influence the first expansion, matching mapgen-3's idea at the new scale.
            TerrainKind leanKind = LargeRange(rng, 0, 1) == 0 ? TerrainKind.Mountain : TerrainKind.River;
            int leanX = (westX + LargeRange(rng, 28, 40)) / LargeCellMeters, leanZ = (westZ + LargeRange(rng, -18, 18)) / LargeCellMeters;
            int leanRx = leanKind == TerrainKind.Mountain ? LargeRange(rng, 3, 5) : LargeRange(rng, 2, 3);
            int leanRz = leanKind == TerrainKind.Mountain ? LargeRange(rng, 3, 5) : LargeRange(rng, 2, 3);
            LargeBlob(terrain, leanX, leanZ, leanRx, leanRz, leanKind, clear, ref total, limit);
            LargeBlob(terrain, LargeColumns - 1 - leanX, LargeRows - 1 - leanZ, leanRx, leanRz, leanKind, clear, ref total, limit);
            for (int k = 0; k < 6; k++)
            {
                int cx = LargeRange(rng, 24, LargeColumns / 2 - 24), cz = LargeRange(rng, 10, LargeRows - 11);
                int rx = LargeRange(rng, 3, 7), rz = LargeRange(rng, 3, 7);
                LargeBlob(terrain, cx, cz, rx, rz, TerrainKind.Mountain, clear, ref total, limit);
                LargeBlob(terrain, LargeColumns - 1 - cx, LargeRows - 1 - cz, rx, rz, TerrainKind.Mountain, clear, ref total, limit);
            }
            for (int k = 0; k < 10; k++)
            {
                int cx = LargeRange(rng, 18, LargeColumns / 2 - 18), cz = LargeRange(rng, 8, LargeRows - 9);
                int rx = LargeRange(rng, 3, 8), rz = LargeRange(rng, 3, 8);
                LargeBlob(terrain, cx, cz, rx, rz, TerrainKind.Forest, clear, ref total, limit);
                LargeBlob(terrain, LargeColumns - 1 - cx, LargeRows - 1 - cz, rx, rz, TerrainKind.Forest, clear, ref total, limit);
            }
            return terrain;
        }

        private static void LargeBlob(byte[] terrain, int cx, int cz, int rx, int rz, TerrainKind kind, List<int[]> clear,
            ref int total, int limit)
        {
            long rx2 = (long)rx * rx, rz2 = (long)rz * rz;
            for (int z = cz - rz; z <= cz + rz; z++) for (int x = cx - rx; x <= cx + rx; x++)
            {
                if (x < 0 || z < 0 || x >= LargeColumns || z >= LargeRows) continue;
                long dx = x - cx, dz = z - cz;
                if (dx * dx * rz2 + dz * dz * rx2 <= rx2 * rz2)
                    LargeMark(terrain, x, z, kind, clear, ref total, limit);
            }
        }

        private static void LargeMark(byte[] terrain, int column, int row, TerrainKind kind, List<int[]> clear, ref int total, int limit)
        {
            if (column < 0 || row < 0 || column >= LargeColumns || row >= LargeRows) return;
            int cell = row * LargeColumns + column;
            if (terrain[cell] != 0 || total >= limit || LargeInClear(column, row, clear)) return;
            terrain[cell] = (byte)kind; total++;
        }

        private static bool LargeInClear(int column, int row, List<int[]> clear)
        {
            int x = column * LargeCellMeters + 1, z = row * LargeCellMeters + 1;
            foreach (var c in clear) if (LargeSquare(x - c[0]) + LargeSquare(z - c[1]) <= LargeSquare(c[2])) return true;
            return false;
        }

        private static bool[] LargeBlocked(byte[] terrain)
        {
            var blocked = new bool[terrain.Length];
            for (int i = 0; i < terrain.Length; i++) blocked[i] = terrain[i] != 0;
            return blocked;
        }

        private static bool[] LargeReachable(bool[] blocked, int start)
        {
            var seen = new bool[blocked.Length];
            var queue = new int[blocked.Length]; int head = 0, tail = 0;
            if (start < 0 || start >= blocked.Length || blocked[start]) return seen;
            queue[tail++] = start; seen[start] = true;
            while (head < tail)
            {
                int cell = queue[head++], x = cell % LargeColumns, z = cell / LargeColumns;
                Visit(z + 1 < LargeRows ? cell + LargeColumns : -1);
                Visit(x + 1 < LargeColumns ? cell + 1 : -1);
                Visit(z > 0 ? cell - LargeColumns : -1);
                Visit(x > 0 ? cell - 1 : -1);
            }
            return seen;
            void Visit(int cell) { if (cell >= 0 && !blocked[cell] && !seen[cell]) { seen[cell] = true; queue[tail++] = cell; } }
        }

        private static int FindLargeCell(SplitMix64 rng, bool[] reachable, HashSet<int> used, Func<int, bool> fits)
        {
            for (int draw = 0; draw < LargeColumns * LargeRows * 4; draw++)
            {
                int cell = LargeRange(rng, 0, reachable.Length - 1);
                if (reachable[cell] && !used.Contains(cell) && fits(cell)) return cell;
            }
            for (int cell = 0; cell < reachable.Length; cell++) if (reachable[cell] && !used.Contains(cell) && fits(cell)) return cell;
            throw new InvalidOperationException("Large map generation could not place a resource.");
        }

        private static bool NearLargeCluster(int cell, int x, int z, int radius)
        {
            int cx = cell % LargeColumns, cz = cell / LargeColumns;
            return LargeSquare(cx - x / LargeCellMeters) + LargeSquare(cz - z / LargeCellMeters) <= LargeSquare(radius);
        }

        private static bool InLargeRing(int cell, int x, int z, int inner, int outer)
        {
            long dx = LargeCenterX(cell) - x, dz = LargeCenterZ(cell) - z;
            return LargeSquare(dx) + LargeSquare(dz) >= LargeSquare(inner) && LargeSquare(dx) + LargeSquare(dz) <= LargeSquare(outer);
        }

        private static int LargeMirror(int cell) => (LargeRows - 1 - cell / LargeColumns) * LargeColumns + (LargeColumns - 1 - cell % LargeColumns);
        private static int LargeCenterX(int cell) => cell % LargeColumns * LargeCellMeters + 1;
        private static int LargeCenterZ(int cell) => cell / LargeColumns * LargeCellMeters + 1;
        private static int LargeCell(int x, int z) => z / LargeCellMeters * LargeColumns + x / LargeCellMeters;
        private static int LargeRange(SplitMix64 rng, int min, int max) => min + (int)rng.NextUInt64((ulong)(max - min + 1));
        private static int LargeSnap(int meters) => meters / LargeCellMeters * LargeCellMeters + 1;
        private static long LargeSquare(long value) => value * value;
        private static SimPoint LargePoint(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        private static ResourceNodeDefinition LargeNode(int id, ResourceKind kind, int cell)
        {
            int amount = kind == ResourceKind.Food ? LargeFoodAmount : kind == ResourceKind.Ore ? LargeOreAmount
                : kind == ResourceKind.Stone ? LargeStoneAmount : kind == ResourceKind.Gold ? 400 : LargeResourceAmount;
            return new ResourceNodeDefinition { Id = (uint)id, Kind = kind,
                Position = LargePoint(LargeCenterX(cell), LargeCenterZ(cell)), Amount = amount };
        }

        private static void PlaceLargeStartingUnits(ScenarioDefinition s, int westX, int westZ, int eastX, int eastZ)
        {
            s.Soldiers = new SoldierDefinition[40];
            int[] counts = { 8, 8, 2, 2 }; uint id = 1;
            for (uint faction = 1; faction <= 2; faction++)
            {
                int cx = faction == 1 ? westX : eastX, cz = faction == 1 ? westZ : eastZ, toward = faction == 1 ? 1 : -1;
                for (int army = 0; army < 4; army++) for (int i = 0; i < counts[army]; i++)
                {
                    int baseX = cx + toward * (army == 2 ? 6 : army == 3 ? 10 : 0);
                    int baseZ = cz + (army == 0 ? 8 : army == 1 ? -9 : 0);
                    s.Soldiers[id - 1] = new SoldierDefinition { Id = id, FactionId = faction,
                        ArmyId = (faction - 1) * 4 + (uint)army + 1, Kind = army == 3 ? UnitKind.Scout : UnitKind.Infantry,
                        Alive = true, Hp = army == 3 ? 40 : 100, Position = LargePoint(baseX + toward * (i % 4), baseZ + i / 4) };
                    id++;
                }
            }
            s.Villagers = new VillagerDefinition[6];
            for (uint faction = 1; faction <= 2; faction++)
            {
                int cx = faction == 1 ? westX : eastX, cz = faction == 1 ? westZ : eastZ, away = faction == 1 ? -1 : 1;
                for (int i = 0; i < 3; i++) s.Villagers[(faction - 1) * 3 + i] = new VillagerDefinition {
                    Id = (faction - 1) * 3U + (uint)i + 1, FactionId = faction, Position = LargePoint(cx + away * 5, cz - 2 + i * 2) };
            }
        }

    }
}
