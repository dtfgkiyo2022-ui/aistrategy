using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed class GoldPlacementReport
    {
        public ulong Seed { get; }
        public bool Placed { get; }
        public string FailureReason { get; }
        public int[] GoldCells { get; }
        public int WestNearestGoldPathMeters { get; }
        public int EastNearestGoldPathMeters { get; }
        public int NearestGoldPathDifferenceMeters { get; }

        internal GoldPlacementReport(ulong seed, bool placed, string failureReason, int[] goldCells, int westNearestGoldPathMeters, int eastNearestGoldPathMeters)
        {
            Seed = seed; Placed = placed; FailureReason = failureReason; GoldCells = goldCells ?? Array.Empty<int>();
            WestNearestGoldPathMeters = westNearestGoldPathMeters;
            EastNearestGoldPathMeters = eastNearestGoldPathMeters;
            NearestGoldPathDifferenceMeters = Math.Abs(westNearestGoldPathMeters - eastNearestGoldPathMeters);
        }
    }

    /// <summary>Optional V3-5 gold placement. It is deliberately a post-pass so the old terrain draw is untouched.</summary>
    public static partial class MapGenerator
    {
        private const ulong GoldRandomSalt = 0x47504C4143454D31UL;
        private const int GoldMiddleMinX = 16, GoldMiddleMaxX = 112, GoldMiddleMinZ = 4, GoldMiddleMaxZ = 60;
        private const int GoldNorthMaxZ = 25, GoldSouthMinZ = 38;
        private const int GoldDropSiteSearchCells = 7, GoldContestRadiusCells = 8;
        private const int BuildingClearanceCells = 2, CoreClearanceCells = 4;
        [Flags]
        private enum GoldRelaxation
        {
            None = 0, Group1Candidate = 1, Group2Candidate = 2,
            Detour = 4, PlacementFairness = 8, GuaranteedOuter = 16
        }

        /// <summary>Generates the unchanged terrain first, then optionally appends an all-or-nothing gold layout.</summary>
        public static ScenarioDefinition GenerateTerrain(ulong seed, bool gold)
        {
            var scenario = GenerateTerrain(seed, out _);
            if (gold) TryAddGold(scenario, seed, out _);
            return scenario;
        }

        public static GoldPlacementReport MeasureGoldPlacement(ulong seed)
        {
            var scenario = GenerateTerrain(seed, out _);
            bool placed = TryAddGold(scenario, seed, out string reason);
            var cells = new List<int>();
            if (placed) foreach (var node in scenario.ResourceNodes) if (node.Kind == ResourceKind.Gold) cells.Add(CellOf(scenario, node.Position));
            int west = 0, east = 0;
            if (placed)
            {
                var blocked = new bool[scenario.Map.WidthCells * scenario.Map.HeightCells];
                foreach (int id in scenario.Map.BlockedCellIds) blocked[id] = true;
                west = Nearest(Distances(scenario, blocked, CellOf(scenario, scenario.Cores[0].Position), null), cells.ToArray()) * scenario.Map.CellSizeMeters;
                east = Nearest(Distances(scenario, blocked, CellOf(scenario, scenario.Cores[1].Position), null), cells.ToArray()) * scenario.Map.CellSizeMeters;
            }
            return new GoldPlacementReport(seed, placed, reason, cells.ToArray(), west, east);
        }

        private static bool TryAddGold(ScenarioDefinition scenario, ulong seed, out string failureReason)
            => TryAddGold(scenario, seed, GoldRelaxation.None, out failureReason);

        private static bool TryAddGold(ScenarioDefinition scenario, ulong seed, GoldRelaxation relaxation, out string failureReason)
        {
            failureReason = null;
            // This stream is separate from the terrain stream. Candidate ordering itself is fixed by cell id; the
            // stream is consumed once to make the post-pass's random source explicit and stable for future variants.
            var goldRandom = new SplitMix64(seed ^ GoldRandomSalt);
            goldRandom.NextUInt64();

            var blocked = new bool[scenario.Map.WidthCells * scenario.Map.HeightCells];
            foreach (int id in scenario.Map.BlockedCellIds) blocked[id] = true;
            var occupied = new HashSet<int>();
            foreach (var node in scenario.ResourceNodes) occupied.Add(CellOf(scenario, node.Position));

            var first = FindPair(scenario, blocked, occupied, false, null, relaxation, out string firstReason);
            if (first == null) { failureReason = firstReason; return false; }

            var central = new bool[blocked.Length];
            foreach (int cell in first)
            {
                int cx = cell % scenario.Map.WidthCells, cz = cell / scenario.Map.WidthCells;
                for (int z = Math.Max(0, cz - GoldContestRadiusCells); z <= Math.Min(scenario.Map.HeightCells - 1, cz + GoldContestRadiusCells); z++)
                    for (int x = Math.Max(0, cx - GoldContestRadiusCells); x <= Math.Min(scenario.Map.WidthCells - 1, cx + GoldContestRadiusCells); x++)
                        if (Square(x - cx) + Square(z - cz) <= Square(GoldContestRadiusCells)) central[z * scenario.Map.WidthCells + x] = true;
            }

            var second = FindPair(scenario, blocked, occupied, true,
                (relaxation & GoldRelaxation.Detour) != 0 ? null : central, relaxation, out string secondReason);
            if (second == null) { failureReason = secondReason; return false; }

            // Commit only after both pairs have passed every condition. This is the atomic failure rule.
            var nodes = new List<ResourceNodeDefinition>(scenario.ResourceNodes);
            foreach (int cell in first) nodes.Add(GoldNode(nodes.Count + 1, scenario, cell));
            foreach (int cell in second) nodes.Add(GoldNode(nodes.Count + 1, scenario, cell));
            scenario.ResourceNodes = nodes.ToArray();
            scenario.Economy.GoldEnabled = true;
            scenario.Economy.Age3GoldCostAgrarian = 0;
            scenario.Economy.Age3GoldCostMetallurgy = 200;
            failureReason = "placed";
            return true;
        }

        private static int[] FindPair(ScenarioDefinition s, bool[] blocked, HashSet<int> occupied, bool detour, bool[] forbidden,
            GoldRelaxation relaxation, out string failureReason)
        {
            bool sawBasicCandidate = false, sawReachablePair = false, sawOutsidePair = false,
                sawPlacementPair = false;
            int cells = s.Map.WidthCells * s.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                int x = cell % s.Map.WidthCells, z = cell / s.Map.WidthCells;
                if (!IsPlainPassable(s, blocked, cell) || occupied.Contains(cell)) continue;
                bool relaxCandidate = detour ? (relaxation & GoldRelaxation.Group2Candidate) != 0
                    : (relaxation & GoldRelaxation.Group1Candidate) != 0;
                if (!relaxCandidate && (detour ? !(z <= GoldNorthMaxZ) : !(x >= GoldMiddleMinX && x <= GoldMiddleMaxX && z >= GoldMiddleMinZ && z <= GoldMiddleMaxZ))) continue;
                if (!detour && !relaxCandidate && !MountainEdge(s, cell)) continue;
                int mirror = (s.Map.HeightCells - 1 - z) * s.Map.WidthCells + (s.Map.WidthCells - 1 - x);
                if (cell >= mirror || occupied.Contains(mirror) || !IsPairCandidate(s, blocked, mirror, detour, occupied, relaxation)) continue;
                if (forbidden != null && (forbidden[cell] || forbidden[mirror])) continue;
                if (detour && (relaxation & GoldRelaxation.Group2Candidate) == 0 && mirror / s.Map.WidthCells < GoldSouthMinZ) continue;
                sawBasicCandidate = true;
                var pair = new[] { cell, mirror };
                var west = Distances(s, blocked, CellOf(s, s.Cores[0].Position), forbidden);
                var east = Distances(s, blocked, CellOf(s, s.Cores[1].Position), forbidden);
                if (Nearest(west, pair) < 0 || Nearest(east, pair) < 0) continue;
                sawReachablePair = true;
                if ((relaxation & GoldRelaxation.GuaranteedOuter) == 0 && !OutsideGuaranteedOuter(s, pair)) continue;
                sawOutsidePair = true;
                if ((relaxation & GoldRelaxation.PlacementFairness) == 0 && !HasPlacementFairness(s, blocked, occupied, pair)) continue;
                sawPlacementPair = true;
                failureReason = null;
                return pair;
            }
            failureReason = !sawBasicCandidate ? (detour ? "組2の候補がない" : "組1の候補がない")
                : !sawReachablePair ? (detour ? "迂回の条件" : "そのほか（経路がない）")
                : !sawOutsidePair ? "保証の範囲の外"
                : !sawPlacementPair ? "資源置き場（両陣営1か所以上、納品距離の差）"
                : "そのほか";
            return null;
        }

        private static bool IsPairCandidate(ScenarioDefinition s, bool[] blocked, int cell, bool detour, HashSet<int> occupied, GoldRelaxation relaxation)
        {
            if (cell < 0 || cell >= blocked.Length || occupied.Contains(cell) || !IsPlainPassable(s, blocked, cell)) return false;
            int x = cell % s.Map.WidthCells, z = cell / s.Map.WidthCells;
            // The first point anchors the pair at a mountain edge. Its point-symmetric mate only needs to be a
            // reachable middle plain cell because mapgen-3's terrain is intentionally not point-symmetric.
            if (detour && (relaxation & GoldRelaxation.Group2Candidate) != 0) return true;
            if (!detour && (relaxation & GoldRelaxation.Group1Candidate) != 0) return true;
            return detour ? z >= GoldSouthMinZ : (x >= GoldMiddleMinX && x <= GoldMiddleMaxX && z >= GoldMiddleMinZ && z <= GoldMiddleMaxZ);
        }

        private static bool IsPlainPassable(ScenarioDefinition s, bool[] blocked, int cell)
            => cell >= 0 && cell < blocked.Length && !blocked[cell] && (s.Map.Terrain.Length == 0 || s.Map.Terrain[cell] == (byte)TerrainKind.Plain);

        private static bool MountainEdge(ScenarioDefinition s, int cell)
        {
            int x = cell % s.Map.WidthCells, z = cell / s.Map.WidthCells;
            return (x > 0 && TerrainAt(s, x - 1, z) == TerrainKind.Mountain)
                || (x + 1 < s.Map.WidthCells && TerrainAt(s, x + 1, z) == TerrainKind.Mountain)
                || (z > 0 && TerrainAt(s, x, z - 1) == TerrainKind.Mountain)
                || (z + 1 < s.Map.HeightCells && TerrainAt(s, x, z + 1) == TerrainKind.Mountain);
        }

        private static TerrainKind TerrainAt(ScenarioDefinition s, int x, int z)
            => s.Map.Terrain.Length == 0 ? TerrainKind.Plain : (TerrainKind)s.Map.Terrain[z * s.Map.WidthCells + x];

        private static bool OutsideGuaranteedOuter(ScenarioDefinition s, int[] pair)
        {
            foreach (int cell in pair)
                foreach (var core in s.Cores)
                {
                    int cx = (int)(core.Position.X.Raw / 65536) / s.Map.CellSizeMeters;
                    int cz = (int)(core.Position.Z.Raw / 65536) / s.Map.CellSizeMeters;
                    int x = cell % s.Map.WidthCells, z = cell / s.Map.WidthCells;
                    if (Square((x * s.Map.CellSizeMeters + 1) - (cx * s.Map.CellSizeMeters + 1))
                        + Square((z * s.Map.CellSizeMeters + 1) - (cz * s.Map.CellSizeMeters + 1)) <= Square(30)) return false;
                }
            return true;
        }

        private static bool HasPlacementFairness(ScenarioDefinition s, bool[] blocked, HashSet<int> occupied, int[] pair)
        {
            long[] bestDistance = { long.MaxValue, long.MaxValue };
            for (int f = 0; f < 2; f++)
                foreach (int gold in pair)
                {
                    var sites = PlacementSites(s, blocked, occupied, pair, f, gold);
                    if (sites.Count == 0) return false;
                    long best = long.MaxValue;
                    var distances = Distances(s, blocked, gold, null);
                    foreach (int site in sites)
                    {
                        int work = NearestWorkCell(s, blocked, site, 2);
                        if (work >= 0 && distances[work] >= 0) best = Math.Min(best, distances[work]);
                    }
                    if (best == long.MaxValue) return false;
                    bestDistance[f] = Math.Min(bestDistance[f], best);
                }
            if (bestDistance[0] == long.MaxValue || bestDistance[1] == long.MaxValue) return false;
            return true;
        }

        private static List<int> PlacementSites(ScenarioDefinition s, bool[] blocked, HashSet<int> occupied, int[] pair, int faction, int gold)
        {
            var result = new List<int>();
            int width = s.Map.WidthCells, centreX = gold % width, centreZ = gold / width, core = CellOf(s, s.Cores[faction].Position);
            for (int r = 0; r <= GoldDropSiteSearchCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = centreX + dx - 1, z0 = centreZ + dz - 1;
                        if (x0 < 0 || z0 < 0 || x0 + 2 > width || z0 + 2 > s.Map.HeightCells) continue;
                        int origin = z0 * width + x0;
                        if (PlacementIsClear(s, blocked, occupied, pair, origin, core, 2) && KeepsConnected(s, blocked, origin, 2, core)) result.Add(origin);
                    }
            return result;
        }

        private static bool PlacementIsClear(ScenarioDefinition s, bool[] blocked, HashSet<int> occupied, int[] pair, int origin, int core, int size)
        {
            int width = s.Map.WidthCells;
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    int cell = origin + z * width + x;
                    if (!IsPlainPassable(s, blocked, cell) || Chebyshev(cell, core, width) < CoreClearanceCells) return false;
                    foreach (int node in occupied) if (Chebyshev(cell, node, width) < BuildingClearanceCells) return false;
                    foreach (int node in pair) if (Chebyshev(cell, node, width) < BuildingClearanceCells) return false;
                }
            return true;
        }

        private static bool KeepsConnected(ScenarioDefinition s, bool[] blocked, int origin, int size, int core)
        {
            int cells = blocked.Length, width = s.Map.WidthCells;
            var closed = new bool[cells];
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++) closed[origin + z * width + x] = true;
            var seen = Reach(s, blocked, core, closed);
            if (!seen[CellOf(s, s.Cores[1].Position)]) return false;
            foreach (var outpost in s.Outposts) if (!seen[CellOf(s, outpost.Position)]) return false;
            foreach (var node in s.ResourceNodes) { int cell = CellOf(s, node.Position); if (!closed[cell] && !seen[cell]) return false; }
            return true;
        }

        private static int[] Distances(ScenarioDefinition s, bool[] blocked, int start, bool[] forbidden)
        {
            return Distances(s, blocked, start, forbidden, null);
        }

        private static int[] Distances(ScenarioDefinition s, bool[] blocked, int start, bool[] forbidden, bool[] closed)
        {
            int width = s.Map.WidthCells, cells = blocked.Length;
            var distance = new int[cells]; Array.Fill(distance, -1);
            if (start < 0 || blocked[start] || forbidden != null && forbidden[start] || closed != null && closed[start]) return distance;
            var queue = new int[cells]; int head = 0, tail = 0; queue[tail++] = start; distance[start] = 0;
            while (head < tail)
            {
                int cell = queue[head++], x = cell % width;
                Visit(cell + width); if (x + 1 < width) Visit(cell + 1); Visit(cell - width); if (x > 0) Visit(cell - 1);
            }
            return distance;
            void Visit(int next)
            {
                if (next < 0 || next >= cells || distance[next] >= 0 || blocked[next] || forbidden != null && forbidden[next] || closed != null && closed[next]) return;
                distance[next] = distance[queue[head - 1]] + 1; queue[tail++] = next;
            }
        }

        private static bool[] Reach(ScenarioDefinition s, bool[] blocked, int start, bool[] closed)
        {
            var distance = Distances(s, blocked, start, null, closed);
            var reached = new bool[distance.Length];
            for (int i = 0; i < distance.Length; i++) reached[i] = distance[i] >= 0;
            return reached;
        }

        private static int Nearest(int[] distances, int[] cells)
        {
            int best = -1;
            foreach (int cell in cells) if (distances[cell] >= 0 && (best < 0 || distances[cell] < best)) best = distances[cell];
            return best;
        }

        private static int NearestWorkCell(ScenarioDefinition s, bool[] blocked, int origin, int size)
        {
            int width = s.Map.WidthCells, cells = blocked.Length;
            long centreX = (long)(origin % width) * s.Map.CellSizeMeters * 65536L + (long)s.Map.CellSizeMeters * 65536L * (size - 1) / 2;
            long centreZ = (long)(origin / width) * s.Map.CellSizeMeters * 65536L + (long)s.Map.CellSizeMeters * 65536L * (size - 1) / 2;
            int best = -1; long bestDistance = 0;
            for (int cell = 0; cell < cells; cell++)
            {
                int x = cell % width, z = cell / width;
                if (blocked[cell] || (x >= origin % width && x < origin % width + size && z >= origin / width && z < origin / width + size)) continue;
                long dx = x * s.Map.CellSizeMeters * 65536L + s.Map.CellSizeMeters * 32768L - centreX;
                long dz = z * s.Map.CellSizeMeters * 65536L + s.Map.CellSizeMeters * 32768L - centreZ;
                long distance = checked(dx * dx + dz * dz);
                if (best < 0 || distance < bestDistance) { best = cell; bestDistance = distance; }
            }
            return best;
        }

        private static int CellOf(ScenarioDefinition s, SimPoint p)
            => (int)(p.Z.Raw / 65536 / s.Map.CellSizeMeters) * s.Map.WidthCells + (int)(p.X.Raw / 65536 / s.Map.CellSizeMeters);

        private static int Chebyshev(int a, int b, int width)
            => Math.Max(Math.Abs(a % width - b % width), Math.Abs(a / width - b / width));

        private static ResourceNodeDefinition GoldNode(int id, ScenarioDefinition s, int cell)
            => new ResourceNodeDefinition { Id = (uint)id, Kind = ResourceKind.Gold,
                Position = new SimPoint(Fix64.FromInt((cell % s.Map.WidthCells) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2),
                    Fix64.FromInt((cell / s.Map.WidthCells) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2)), Amount = s.Economy.GoldAmount };
    }
}
