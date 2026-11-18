using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    internal static class NeutralCatalog
    {
        internal static NeutralCampDefinition[] Build(ScenarioDefinition scenario)
        {
            if (scenario.NeutralRules.Camps != null && scenario.NeutralRules.Camps.Length > 0)
            {
                var authored = new List<NeutralCampDefinition>(scenario.NeutralRules.Camps);
                bool hasGuardian = false;
                for (int i = 0; i < authored.Count; i++)
                    if (authored[i].Kind == NeutralKind.AugmentGuardian) { hasGuardian = true; break; }
                if (scenario.Economy.Augments && !hasGuardian)
                    authored.Add(new NeutralCampDefinition { Id = authored.Count == 0 ? 1U : authored[authored.Count - 1].Id + 1,
                        Kind = NeutralKind.AugmentGuardian, Position = new SimPoint(Fix64.FromInt(scenario.Map.WidthMeters / 2), Fix64.FromInt(scenario.Map.HeightMeters / 2)) });
                AppendGreatCamps(scenario, authored);
                return authored.ToArray();
            }
            int width = scenario.Map.WidthMeters, height = scenario.Map.HeightMeters;
            int n = (int)(scenario.Seed % 5UL);
            int mid = width / 2;
            var defs = new List<NeutralCampDefinition>(18);
            uint id = 1;
            AddPair(defs, ref id, NeutralKind.WolfPack, width, height, 30 + n, height / 4);
            AddPair(defs, ref id, NeutralKind.WolfPack, width, height, 42 + n, height * 3 / 4);
            AddPair(defs, ref id, NeutralKind.WolfPack, width, height, 66 + n, height / 2 - 18);
            AddPair(defs, ref id, NeutralKind.WolfPack, width, height, 66 + n, height / 2 + 18);
            AddPair(defs, ref id, NeutralKind.RockBeast, width, height, 86 + n, height / 3);
            AddPair(defs, ref id, NeutralKind.RockBeast, width, height, 86 + n, height * 2 / 3);
            AddPair(defs, ref id, NeutralKind.FlameHornBeast, width, height, width * 3 / 10, height / 2 - 16);
            AddPair(defs, ref id, NeutralKind.AzureSerpent, width, height, width * 3 / 10, height / 2 + 16);
            defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.ShellBeast, Position = new SimPoint(Fix64.FromInt(mid - 12), Fix64.FromInt(height / 2)) });
            defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.ShellBeast, Position = new SimPoint(Fix64.FromInt(mid + 12), Fix64.FromInt(height / 2)) });
            if (scenario.Economy.Augments)
                defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.AugmentGuardian, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(height / 2)) });
            if (scenario.Economy.GreatNeutrals)
            {
                defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.Dragon, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(height / 2 - 40)) });
                AddPair(defs, ref id, NeutralKind.SiegeBeast, width, height, width * 2 / 5, height / 2 - 30);
                defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.GreatLord, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(height / 2 + 40)) });
            }
            var result = defs.ToArray();
            MoveOffBlockedCells(scenario.Map, result, scenario.Economy != null && scenario.Economy.SideFair);
            return result;
        }

        /// <summary>Clear ground around a camp: every cell centre within this many metres must be passable.</summary>
        internal const int CampClearMeters = 4;

        /// <summary>
        /// 11-18 testplay: the fixed camp spots ignored the terrain, so a wolf pack could sit inside a mountain where no
        /// soldier can reach it. A generated camp on blocked ground moves by the nearest whole-cell shift that puts its
        /// surroundings (CampClearMeters) on passable ground; a west/east pair shifts as mirror images and a camp on the
        /// centre line only north/south, so both sides stay alike. Fixed order, integer arithmetic only.
        /// A camp already on clear ground, and every camp on a map without blocked cells, stays where it was.
        /// </summary>
        internal static void MoveOffBlockedCells(MapDefinition map, NeutralCampDefinition[] camps, bool sideFair)
        {
            if (map == null || map.BlockedCellIds == null || map.BlockedCellIds.Length == 0 || camps.Length == 0) return;
            int w = map.WidthCells, h = map.HeightCells, size = map.CellSizeMeters;
            if (w <= 0 || h <= 0 || size <= 0) return;
            var blocked = new bool[w * h];
            foreach (int cell in map.BlockedCellIds) if (cell >= 0 && cell < blocked.Length) blocked[cell] = true;
            int reach = (CampClearMeters + size - 1) / size;
            long clearSq = (long)CampClearMeters * CampClearMeters * 4;   // in half-metre units, compared with centre offsets
            bool Clear(int cx, int cz)
            {
                for (int dz = -reach; dz <= reach; dz++)
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        long ox = (long)dx * size * 2, oz = (long)dz * size * 2;
                        if (ox * ox + oz * oz > clearSq) continue;
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= w || z >= h || blocked[z * w + x]) return false;
                    }
                return true;
            }
            long rawSize = Fix64.FromInt(size).Raw;
            long widthRaw = Fix64.FromInt(map.WidthMeters).Raw;
            long fullW = w * rawSize, fullH = h * rawSize;
            bool ClearAt(SimPoint p, long dx, long dz)
            {
                long x = p.X.Raw + dx * rawSize, z = p.Z.Raw + dz * rawSize;
                if (x < 0 || z < 0 || x >= fullW || z >= fullH) return false;
                long cx = x / rawSize, cz = z / rawSize;
                // The same cell as GridMap.Cell: under SideFair a point on a cell boundary in the west/north half
                // belongs to the lower cell (camps sit on whole metres, which are boundaries for 2 m cells).
                if (sideFair && x > 0 && x % rawSize == 0 && (x < fullW / 2 || x == fullW / 2 && z < fullH / 2)) cx--;
                if (sideFair && z > 0 && z % rawSize == 0 && (z < fullH / 2 || z == fullH / 2 && x < fullW / 2)) cz--;
                return Clear((int)cx, (int)cz);
            }
            // Offsets in cells, nearest first; ties by dz, then dx (a fixed order, so the result never varies).
            const int MaxShiftCells = 40;
            var offsets = new List<(int dx, int dz)>();
            for (int dz = -MaxShiftCells; dz <= MaxShiftCells; dz++)
                for (int dx = -MaxShiftCells; dx <= MaxShiftCells; dx++)
                    if (dx * dx + dz * dz <= MaxShiftCells * MaxShiftCells) offsets.Add((dx, dz));
            offsets.Sort((a, b) =>
            {
                int c = (a.dx * a.dx + a.dz * a.dz).CompareTo(b.dx * b.dx + b.dz * b.dz);
                if (c != 0) return c;
                c = a.dz.CompareTo(b.dz);
                return c != 0 ? c : a.dx.CompareTo(b.dx);
            });
            // The camps mirror west and east (x + partner x = map width, same z), and the central ones sit on the mirror
            // line. A pair moves together as mirror images (west by dx, east by -dx); a camp on the line moves along z only.
            var done = new bool[camps.Length];
            for (int i = 0; i < camps.Length; i++)
            {
                if (done[i]) continue;
                done[i] = true;
                var a = camps[i].Position;
                int partner = -1;
                for (int j = i + 1; j < camps.Length && partner < 0; j++)
                    if (!done[j] && camps[j].Kind == camps[i].Kind && camps[j].Position.Z.Raw == a.Z.Raw
                        && camps[j].Position.X.Raw + a.X.Raw == widthRaw) partner = j;
                if (partner >= 0) done[partner] = true;
                bool onLine = partner < 0 && a.X.Raw * 2 == widthRaw;
                var b = partner >= 0 ? camps[partner].Position : default(SimPoint);
                // b is east of a when a is the west camp; the mirrored shift is -dx for the camp on the other side.
                if (ClearAt(a, 0, 0) && (partner < 0 || ClearAt(b, 0, 0))) continue;
                foreach (var (dx, dz) in offsets)
                {
                    if (onLine && dx != 0) continue;
                    if (!ClearAt(a, dx, dz) || partner >= 0 && !ClearAt(b, -dx, dz)) continue;
                    camps[i].Position = new SimPoint(Fix64.FromRaw(a.X.Raw + dx * rawSize), Fix64.FromRaw(a.Z.Raw + dz * rawSize));
                    if (partner >= 0)
                        camps[partner].Position = new SimPoint(Fix64.FromRaw(b.X.Raw - dx * rawSize), Fix64.FromRaw(b.Z.Raw + dz * rawSize));
                    break;
                }
            }
        }

        private static void AppendGreatCamps(ScenarioDefinition scenario, List<NeutralCampDefinition> camps)
        {
            if (!scenario.Economy.GreatNeutrals) return;
            bool dragon = false, siege = false, lord = false;
            for (int i = 0; i < camps.Count; i++)
            {
                dragon |= camps[i].Kind == NeutralKind.Dragon;
                siege |= camps[i].Kind == NeutralKind.SiegeBeast;
                lord |= camps[i].Kind == NeutralKind.GreatLord;
            }
            uint id = camps.Count == 0 ? 1U : camps[camps.Count - 1].Id + 1;
            int mid = scenario.Map.WidthMeters / 2, half = scenario.Map.HeightMeters / 2;
            if (!dragon) camps.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.Dragon, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(half - 40)) });
            if (!siege) { camps.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.SiegeBeast, Position = new SimPoint(Fix64.FromInt(scenario.Map.WidthMeters * 2 / 5), Fix64.FromInt(half - 30)) });
                camps.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.SiegeBeast, Position = new SimPoint(Fix64.FromInt(scenario.Map.WidthMeters * 3 / 5), Fix64.FromInt(half - 30)) }); }
            if (!lord) camps.Add(new NeutralCampDefinition { Id = id, Kind = NeutralKind.GreatLord, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(half + 40)) });
        }

        private static void AddPair(List<NeutralCampDefinition> result, ref uint id, NeutralKind kind,
            int width, int height, int westX, int z)
        {
            int eastX = width - westX;
            result.Add(new NeutralCampDefinition { Id = id++, Kind = kind, Position = new SimPoint(Fix64.FromInt(westX), Fix64.FromInt(Math.Max(4, Math.Min(height - 4, z)))) });
            result.Add(new NeutralCampDefinition { Id = id++, Kind = kind, Position = new SimPoint(Fix64.FromInt(eastX), Fix64.FromInt(Math.Max(4, Math.Min(height - 4, z)))) });
        }

        internal static void Stats(NeutralKind kind, out int count, out int hp, out int ad, out int interval,
            out int range, out int armor, out int mr)
        {
            switch (kind)
            {
                case NeutralKind.WolfPack: count = 3; hp = 180; ad = 8; interval = 20; range = 2; armor = 10; mr = 10; break;
                case NeutralKind.RockBeast: count = 1; hp = 600; ad = 14; interval = 30; range = 2; armor = 30; mr = 10; break;
                case NeutralKind.FlameHornBeast:
                case NeutralKind.AzureSerpent: count = 1; hp = 1400; ad = 30; interval = 30; range = 2; armor = 30; mr = 20; break;
                case NeutralKind.AugmentGuardian: count = 1; hp = 2500; ad = 40; interval = 30; range = 3; armor = 40; mr = 40; break;
                case NeutralKind.Dragon: count = 1; hp = 2200; ad = 35; interval = 30; range = 3; armor = 35; mr = 35; break;
                case NeutralKind.SiegeBeast: count = 1; hp = 1200; ad = 20; interval = 30; range = 2; armor = 50; mr = 20; break;
                case NeutralKind.GreatLord: count = 1; hp = 6000; ad = 60; interval = 30; range = 4; armor = 60; mr = 60; break;
                default: count = 1; hp = 900; ad = 0; interval = 30; range = 2; armor = 40; mr = 40; break;
            }
        }

        internal static int InitialDelay(NeutralRules rules, NeutralKind kind)
            => kind == NeutralKind.GreatLord ? 9000
            : kind == NeutralKind.Dragon ? 3600
            : kind == NeutralKind.SiegeBeast ? 2400
            : kind == NeutralKind.AugmentGuardian ? 6000
            : kind == NeutralKind.WolfPack ? rules.WolfInitialDelayTicks
            : kind == NeutralKind.RockBeast ? rules.RockInitialDelayTicks
            : kind == NeutralKind.ShellBeast ? rules.ShellInitialDelayTicks : rules.BlessingInitialDelayTicks;

        internal static int RespawnDelay(NeutralRules rules, NeutralKind kind)
            => kind == NeutralKind.GreatLord ? 6000
            : kind == NeutralKind.Dragon ? 3600
            : kind == NeutralKind.SiegeBeast ? 4800
            : kind == NeutralKind.AugmentGuardian ? 6000
            : kind == NeutralKind.WolfPack ? rules.WolfRespawnTicks
            : kind == NeutralKind.RockBeast ? rules.RockRespawnTicks
            : kind == NeutralKind.ShellBeast ? rules.ShellRespawnTicks : rules.BlessingRespawnTicks;

        internal static int SkillInterval(NeutralKind kind)
            => kind == NeutralKind.GreatLord ? 240
            : kind == NeutralKind.Dragon ? 240
            : kind == NeutralKind.SiegeBeast ? 200
            : kind == NeutralKind.AugmentGuardian ? 300
            : kind == NeutralKind.RockBeast ? 200
            : kind == NeutralKind.FlameHornBeast || kind == NeutralKind.AzureSerpent ? 240 : int.MaxValue;

        internal static DragonElement DragonElementFor(ulong seed, int spawnCount)
        {
            if (spawnCount < 1) return DragonElement.None;
            DragonElement previous = DragonElement.None;
            for (int count = 1; count <= spawnCount; count++)
            {
                ulong x = seed + unchecked((ulong)count) * 0x9E3779B97F4A7C15UL;
                x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL; x ^= x >> 27; x *= 0x94D049BB133111EBUL; x ^= x >> 31;
                int value = (int)(x % 4UL) + 1;
                var element = (DragonElement)value;
                if (element == previous) element = (DragonElement)(value % 4 + 1);
                previous = element;
            }
            return previous;
        }
    }
}
