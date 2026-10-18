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
        private int[] spatialSoldierHeads, spatialSoldierTails, spatialSoldierNext;
        private int[] spatialVillagerHeads, spatialVillagerTails, spatialVillagerNext;
        private int[] spatialBuildingHeads, spatialBuildingTails, spatialBuildingNext;
        private int[] spatialBeltHeads, spatialBeltTails, spatialBeltNext;
        private int[] spatialEnemyCandidates, spatialEnemyIndices, spatialVillagerCandidates, spatialBuildingCandidates, spatialBeltCandidates;
        private int[][] combatContactToSoldier;
        private long spatialCellRaw, buildingSearchPaddingRaw;

        private void InitializeSpatialCombatIndex()
        {
            spatialWidth = checked((world.Config.Map.WidthMeters + SpatialCombatCellSizeMeters - 1) / SpatialCombatCellSizeMeters);
            spatialHeight = checked((world.Config.Map.HeightMeters + SpatialCombatCellSizeMeters - 1) / SpatialCombatCellSizeMeters);
            int cells = checked(spatialWidth * spatialHeight);
            spatialCellRaw = Fix64.FromInt(SpatialCombatCellSizeMeters).Raw;
            spatialSoldierHeads = new int[cells]; spatialSoldierTails = new int[cells];
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
                if (world.Soldiers[i].Alive) AppendSpatial(spatialSoldierHeads, spatialSoldierTails, spatialSoldierNext, i, world.Soldiers[i].Position);
            for (int i = 0; i < world.VillagerCount; i++)
                if (world.Villagers[i].Alive) AppendSpatial(spatialVillagerHeads, spatialVillagerTails, spatialVillagerNext, i, world.Villagers[i].Position);
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive) AppendSpatial(spatialBuildingHeads, spatialBuildingTails, spatialBuildingNext, i,
                    BuildingIndexPosition(world.Buildings[i]));
            for (int i = 0; i < world.Belts.Length; i++)
                if (world.Belts[i].FactionId != 0) AppendSpatial(spatialBeltHeads, spatialBeltTails, spatialBeltNext, i, world.Map.Center(i));
        }

        private SimPoint BuildingIndexPosition(BuildingState building)
        {
            if (building.Kind == BuildingKind.Bridge || building.Kind == BuildingKind.Tollgate)
                return NearestFootprintPoint(building, world.Map.Center(building.OriginCell));
            return FootprintCenter(building.OriginCell, SizeOf(building.Kind));
        }

        private void ClearSpatialHeads()
        {
            Array.Fill(spatialSoldierHeads, -1); Array.Fill(spatialSoldierTails, -1);
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
            int candidates = QuerySpatial(spatialSoldierHeads, spatialSoldierNext, point, range, 0, spatialEnemyIndices);
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
            return count;
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
