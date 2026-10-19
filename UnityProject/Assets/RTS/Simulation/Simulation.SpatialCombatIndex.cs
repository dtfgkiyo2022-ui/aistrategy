using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// A reusable spatial index for the searches that happen in the combat phase.  The cell size is deliberately a
    /// fixed simulation constant: it is larger than a normal map cell, but small enough for the largest combat ranges.
    /// Every chain is appended while walking the source array in ID order, so enumeration within a cell is stable.
    /// </summary>
    public sealed partial class Simulation
    {
        private const int SpatialCombatCellSizeMeters = 16;

        private int spatialWidth, spatialHeight;
        private int[][] spatialSoldierHeads, spatialSoldierTails;
        private int[] spatialSoldierNext;
        // The old all-faction chain is kept only for the deterministic comparison path.  It is never used by the game path.
        private int[] legacySpatialSoldierHeads, legacySpatialSoldierTails, legacySpatialSoldierNext;
        private int[] spatialVillagerHeads, spatialVillagerTails, spatialVillagerNext;
        private int[] spatialBuildingHeads, spatialBuildingTails, spatialBuildingNext;
        private int[] spatialBeltHeads, spatialBeltTails, spatialBeltNext;
        private int[] spatialEnemyCandidates, spatialEnemyIndices, spatialVillagerCandidates, spatialBuildingCandidates, spatialBeltCandidates;
        private int[][] combatContactToSoldier;
        private const int SpatialEnemyQueryCacheSlots = 32;
        private SpatialEnemyQueryCache[][] spatialEnemyQueryCache;
        private int[] spatialEnemyQueryCacheCount, spatialEnemyQueryCacheNext;
        private long spatialEnemyQueryHits, spatialEnemyQueryMisses;
        private long spatialCellRaw, buildingSearchPaddingRaw;

        private void InitializeSpatialCombatIndex()
        {
            spatialWidth = checked((world.Config.Map.WidthMeters + SpatialCombatCellSizeMeters - 1) / SpatialCombatCellSizeMeters);
            spatialHeight = checked((world.Config.Map.HeightMeters + SpatialCombatCellSizeMeters - 1) / SpatialCombatCellSizeMeters);
            int cells = checked(spatialWidth * spatialHeight);
            spatialCellRaw = Fix64.FromInt(SpatialCombatCellSizeMeters).Raw;
            spatialSoldierHeads = new[] { new int[cells], new int[cells] };
            spatialSoldierTails = new[] { new int[cells], new int[cells] };
            if (useLegacySpatialCombatIndex)
            {
                legacySpatialSoldierHeads = new int[cells]; legacySpatialSoldierTails = new int[cells];
                legacySpatialSoldierNext = new int[world.SoldierCount];
            }
            spatialVillagerHeads = new int[cells]; spatialVillagerTails = new int[cells];
            spatialBuildingHeads = new int[cells]; spatialBuildingTails = new int[cells];
            spatialBeltHeads = new int[cells]; spatialBeltTails = new int[cells];
            spatialSoldierNext = new int[world.SoldierCount];
            spatialVillagerNext = new int[world.VillagerCount];
            spatialBuildingNext = new int[Math.Max(1, world.Buildings.Length)];
            spatialBeltNext = new int[world.Belts.Length];
            spatialEnemyCandidates = new int[Math.Max(1, world.SoldierCount)];
            spatialEnemyIndices = new int[Math.Max(1, world.SoldierCount)];
            spatialEnemyBuffer = new VisibleEnemy[Math.Max(1, world.SoldierCount)];
            spatialVillagerCandidates = new int[Math.Max(1, world.VillagerCount)];
            spatialBuildingCandidates = new int[Math.Max(1, world.Buildings.Length)];
            spatialBeltCandidates = new int[Math.Max(1, world.Belts.Length)];
            combatContactToSoldier = new[] { new int[world.SoldierCount + 1], new int[world.SoldierCount + 1] };
            Array.Fill(combatContactToSoldier[0], -1);
            Array.Fill(combatContactToSoldier[1], -1);
            spatialEnemyQueryCache = new[] { new SpatialEnemyQueryCache[SpatialEnemyQueryCacheSlots], new SpatialEnemyQueryCache[SpatialEnemyQueryCacheSlots] };
            spatialEnemyQueryCacheCount = new int[2];
            spatialEnemyQueryCacheNext = new int[2];
            for (int faction = 0; faction < spatialEnemyQueryCache.Length; faction++)
                for (int i = 0; i < spatialEnemyQueryCache[faction].Length; i++)
                    spatialEnemyQueryCache[faction][i].Values = new VisibleEnemy[Math.Max(1, world.SoldierCount)];

            int maxFootprintCells = 1;
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
                maxFootprintCells = Math.Max(maxFootprintCells, SizeOf(kind));
            maxFootprintCells = Math.Max(maxFootprintCells, world.Config.Economy.MaxBridgeLength);
            buildingSearchPaddingRaw = Fix64.FromInt(checked(maxFootprintCells * world.Config.Map.CellSizeMeters)).Raw;
            ClearSpatialHeads();
        }

        private void EnsureSpatialCombatBuffers()
        {
            if (spatialSoldierNext.Length < world.Soldiers.Length)
            {
                spatialSoldierNext = new int[world.Soldiers.Length];
                if (useLegacySpatialCombatIndex) legacySpatialSoldierNext = new int[world.Soldiers.Length];
                spatialEnemyCandidates = new int[world.Soldiers.Length];
                spatialEnemyIndices = new int[world.Soldiers.Length];
                spatialEnemyBuffer = new VisibleEnemy[world.Soldiers.Length];
                for (int faction = 0; faction < combatContactToSoldier.Length; faction++)
                {
                    var old = combatContactToSoldier[faction];
                    combatContactToSoldier[faction] = new int[world.Soldiers.Length + 1];
                    Array.Fill(combatContactToSoldier[faction], -1);
                    Array.Copy(old, combatContactToSoldier[faction], old.Length);
                }
            }
            if (spatialVillagerNext.Length < world.Villagers.Length)
                spatialVillagerNext = new int[world.Villagers.Length];
            if (spatialVillagerCandidates.Length < world.Villagers.Length)
                spatialVillagerCandidates = new int[world.Villagers.Length];
            if (spatialBuildingNext.Length < world.Buildings.Length)
            {
                spatialBuildingNext = new int[world.Buildings.Length];
                spatialBuildingCandidates = new int[world.Buildings.Length];
            }
        }

        private void RebuildSpatialCombatIndex()
        {
            EnsureSpatialCombatBuffers();
            ClearSpatialHeads();
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Alive)
                {
                    int faction = checked((int)world.Soldiers[i].Initial.FactionId) - 1;
                    if (faction >= 0 && faction < spatialSoldierHeads.Length)
                        AppendSpatial(spatialSoldierHeads[faction], spatialSoldierTails[faction], spatialSoldierNext, i, world.Soldiers[i].Position);
                    if (useLegacySpatialCombatIndex)
                        AppendSpatial(legacySpatialSoldierHeads, legacySpatialSoldierTails, legacySpatialSoldierNext, i, world.Soldiers[i].Position);
                }
            for (int i = 0; i < world.VillagerCount; i++)
                if (world.Villagers[i].Alive) AppendSpatial(spatialVillagerHeads, spatialVillagerTails, spatialVillagerNext, i, world.Villagers[i].Position);
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive) AppendSpatial(spatialBuildingHeads, spatialBuildingTails, spatialBuildingNext, i,
                    BuildingIndexPosition(world.Buildings[i]));
            for (int i = 0; i < world.Belts.Length; i++)
                if (world.Belts[i].FactionId != 0) AppendSpatial(spatialBeltHeads, spatialBeltTails, spatialBeltNext, i, world.Map.Center(i));
            for (int faction = 0; faction < spatialEnemyQueryCache.Length; faction++)
            {
                spatialEnemyQueryCacheCount[faction] = 0;
                spatialEnemyQueryCacheNext[faction] = 0;
            }
            spatialEnemyQueryHits = spatialEnemyQueryMisses = 0;
        }

        private SimPoint BuildingIndexPosition(BuildingState building)
        {
            if (building.Kind == BuildingKind.Bridge || building.Kind == BuildingKind.Tollgate)
                return NearestFootprintPoint(building, world.Map.Center(building.OriginCell));
            return FootprintCenter(building.OriginCell, SizeOf(building.Kind));
        }

        private void ClearSpatialHeads()
        {
            for (int faction = 0; faction < spatialSoldierHeads.Length; faction++)
            {
                Array.Fill(spatialSoldierHeads[faction], -1); Array.Fill(spatialSoldierTails[faction], -1);
            }
            if (legacySpatialSoldierHeads != null)
            {
                Array.Fill(legacySpatialSoldierHeads, -1); Array.Fill(legacySpatialSoldierTails, -1);
            }
            Array.Fill(spatialVillagerHeads, -1); Array.Fill(spatialVillagerTails, -1);
            Array.Fill(spatialBuildingHeads, -1); Array.Fill(spatialBuildingTails, -1);
            Array.Fill(spatialBeltHeads, -1); Array.Fill(spatialBeltTails, -1);
        }

        private void AppendSpatial(int[] heads, int[] tails, int[] next, int index, SimPoint point)
        {
            if (!SpatialCell(point, out int cell)) { next[index] = -1; return; }
            next[index] = -1;
            if (tails[cell] < 0) heads[cell] = tails[cell] = index;
            else { next[tails[cell]] = index; tails[cell] = index; }
        }

        private bool SpatialCell(SimPoint point, out int cell)
        {
            long x = point.X.Raw / spatialCellRaw, z = point.Z.Raw / spatialCellRaw;
            if (x < 0 || x >= spatialWidth || z < 0 || z >= spatialHeight) { cell = -1; return false; }
            cell = checked((int)(z * spatialWidth + x));
            return true;
        }

        private int QuerySpatial(int[] heads, int[] next, SimPoint point, Fix64 range, long paddingRaw, int[] result)
        {
            long minXRaw = point.X.Raw - range.Raw - paddingRaw, maxXRaw = point.X.Raw + range.Raw + paddingRaw;
            long minZRaw = point.Z.Raw - range.Raw - paddingRaw, maxZRaw = point.Z.Raw + range.Raw + paddingRaw;
            int minX = Math.Max(0, (int)Math.Min(spatialWidth - 1, minXRaw / spatialCellRaw));
            int maxX = Math.Min(spatialWidth - 1, (int)Math.Max(0, maxXRaw / spatialCellRaw));
            int minZ = Math.Max(0, (int)Math.Min(spatialHeight - 1, minZRaw / spatialCellRaw));
            int maxZ = Math.Min(spatialHeight - 1, (int)Math.Max(0, maxZRaw / spatialCellRaw));
            int count = 0;
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    for (int i = heads[z * spatialWidth + x]; i >= 0; i = next[i])
                        result[count++] = i;
            return count;
        }

        private int QueryVisibleEnemies(uint faction, SimPoint point, Fix64 range)
        {
            if (!useLegacySpatialCombatIndex && TryCachedVisibleEnemies(faction, point, range, out int cachedCount))
                return cachedCount;

            int enemyFaction = checked((int)(faction == 1 ? 2U : 1U)) - 1;
            int candidates = useLegacySpatialCombatIndex
                ? QuerySpatial(legacySpatialSoldierHeads, legacySpatialSoldierNext, point, range, 0, spatialEnemyIndices)
                : QuerySpatial(spatialSoldierHeads[enemyFaction], spatialSoldierNext, point, range, 0, spatialEnemyIndices);
            int count = 0;
            for (int n = 0; n < candidates; n++)
            {
                int i = spatialEnemyIndices[n];
                var soldier = world.Soldiers[i];
                if (!soldier.Alive || soldier.Initial.FactionId == faction || !IsVisibleTo(faction, soldier.Position)) continue;
                uint contact = world.Factions[faction - 1].ContactIds[i];
                if (contact == 0) continue;
                byte kind = CultOn && soldier.Class != 0 ? (byte)soldier.Class : (byte)soldier.Initial.Kind;
                spatialEnemyCandidates[count] = i;
                spatialEnemyBuffer[count++] = new VisibleEnemy(contact, soldier.Position, kind);
            }
            if (!useLegacySpatialCombatIndex) StoreCachedVisibleEnemies(faction, point, range, count);
            return count;
        }

        private bool TryCachedVisibleEnemies(uint faction, SimPoint point, Fix64 range, out int count)
        {
            int factionIndex = checked((int)faction) - 1;
            var entries = spatialEnemyQueryCache[factionIndex];
            for (int i = 0; i < spatialEnemyQueryCacheCount[factionIndex]; i++)
            {
                ref var entry = ref entries[i];
                if (entry.PointXRaw != point.X.Raw || entry.PointZRaw != point.Z.Raw || entry.RangeRaw != range.Raw) continue;
                count = entry.Count;
                Array.Copy(entry.Values, spatialEnemyBuffer, count);
                spatialEnemyQueryHits++;
                return true;
            }
            spatialEnemyQueryMisses++;
            count = 0;
            return false;
        }

        private void StoreCachedVisibleEnemies(uint faction, SimPoint point, Fix64 range, int count)
        {
            int factionIndex = checked((int)faction) - 1;
            int slot;
            if (spatialEnemyQueryCacheCount[factionIndex] < SpatialEnemyQueryCacheSlots)
                slot = spatialEnemyQueryCacheCount[factionIndex]++;
            else
            {
                slot = spatialEnemyQueryCacheNext[factionIndex];
                spatialEnemyQueryCacheNext[factionIndex] = (slot + 1) % SpatialEnemyQueryCacheSlots;
            }
            ref var entry = ref spatialEnemyQueryCache[factionIndex][slot];
            // The slots are sized for the starting soldiers; reinforcements can make a query larger than that.
            if (entry.Values.Length < count) entry.Values = new VisibleEnemy[spatialEnemyBuffer.Length];
            entry.PointXRaw = point.X.Raw; entry.PointZRaw = point.Z.Raw; entry.RangeRaw = range.Raw; entry.Count = count;
            Array.Copy(spatialEnemyBuffer, entry.Values, count);
        }

        private struct SpatialEnemyQueryCache
        {
            internal long PointXRaw, PointZRaw, RangeRaw;
            internal int Count;
            internal VisibleEnemy[] Values;
        }

        // Test diagnostics only.  These counters are reset at the beginning of every combat-index rebuild.
        internal long SpatialEnemyQueryHits => spatialEnemyQueryHits;
        internal long SpatialEnemyQueryMisses => spatialEnemyQueryMisses;
        internal int QueryVisibleEnemiesForTest(uint faction, SimPoint point, Fix64 range) => QueryVisibleEnemies(faction, point, range);
        internal void RebuildSpatialCombatIndexForTest() => RebuildSpatialCombatIndex();
        internal VisibleEnemy[] QueryVisibleEnemiesSnapshotForTest(uint faction, SimPoint point, Fix64 range)
        {
            int count = QueryVisibleEnemies(faction, point, range);
            var result = new VisibleEnemy[count];
            Array.Copy(spatialEnemyBuffer, result, count);
            return result;
        }

        private VisibleEnemy[] spatialEnemyBuffer;

        private int QueryRaidVillagers(SimPoint point, Fix64 range)
            => QuerySpatial(spatialVillagerHeads, spatialVillagerNext, point, range, 0, spatialVillagerCandidates);

        private int QueryRaidBuildings(SimPoint point, Fix64 range)
            => QuerySpatial(spatialBuildingHeads, spatialBuildingNext, point, range, buildingSearchPaddingRaw, spatialBuildingCandidates);

        private int QueryRaidBelts(SimPoint point, Fix64 range)
            => QuerySpatial(spatialBeltHeads, spatialBeltNext, point, range, 0, spatialBeltCandidates);

        private void RememberCombatContact(uint faction, uint contact, int soldierIndex)
        {
            if (contact < combatContactToSoldier[faction - 1].Length)
                combatContactToSoldier[faction - 1][contact] = soldierIndex;
        }

        private bool TryCombatContact(uint faction, uint contact, out int soldierIndex)
        {
            soldierIndex = -1;
            if (contact == 0 || contact >= combatContactToSoldier[faction - 1].Length) return false;
            soldierIndex = combatContactToSoldier[faction - 1][contact];
            return soldierIndex >= 0 && world.Factions[faction - 1].ContactIds[soldierIndex] == contact;
        }
    }
}
