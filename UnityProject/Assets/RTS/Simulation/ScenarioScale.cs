using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// Measurement helper for stage 5: repeats every soldier of a scenario <c>factor</c> times and raises the faction and
    /// army caps to match. Integer meters only, so it stays inside the deterministic rules. It is a test fixture, not a
    /// game rule: nothing in a normal match calls it.
    /// </summary>
    public static class ScenarioScale
    {
        // Copies are spread along z so that no two start on the same spot.
        private const int CopySpacingMeters = 2;

        public static ScenarioDefinition Multiply(ScenarioDefinition s, int factor)
        {
            if (factor < 1) throw new System.ArgumentOutOfRangeException(nameof(factor));
            if (factor == 1) return s;
            s.ScenarioId = s.ScenarioId + "-x" + factor;
            s.Rules.FactionCap *= factor;
            for (int i = 0; i < s.Armies.Length; i++) s.Armies[i].Capacity *= factor;
            var scaled = new SoldierDefinition[s.Soldiers.Length * factor];
            uint id = 1;
            // Army by army, so that ids stay grouped the way the base scenario groups them.
            for (int a = 0; a < s.Armies.Length; a++)
            {
                uint armyId = s.Armies[a].Id;
                for (int copy = 0; copy < factor; copy++)
                {
                    int shift = (copy - (factor - 1) / 2) * CopySpacingMeters;
                    foreach (var d in s.Soldiers)
                    {
                        if (d.ArmyId != armyId) continue;
                        scaled[id - 1] = new SoldierDefinition
                        {
                            Id = id, FactionId = d.FactionId, ArmyId = d.ArmyId, Kind = d.Kind, Alive = d.Alive, Hp = d.Hp,
                            Position = new SimPoint(d.Position.X, d.Position.Z + Fix64.FromInt(shift))
                        };
                        id++;
                    }
                }
            }
            s.Soldiers = scaled;
            return s;
        }

        /// <summary>
        /// Applies independent measurement multipliers to the live economy. This is intentionally an authoring-side
        /// fixture: normal matches never call it, and the normal rules remain unchanged.
        /// </summary>
        public static ScenarioDefinition MultiplyForMeasurement(ScenarioDefinition s, int villagerFactor, int soldierFactor)
        {
            if (s == null) throw new System.ArgumentNullException(nameof(s));
            if (villagerFactor < 1) throw new System.ArgumentOutOfRangeException(nameof(villagerFactor));
            if (soldierFactor < 1) throw new System.ArgumentOutOfRangeException(nameof(soldierFactor));
            if (villagerFactor == 1 && soldierFactor == 1) return s;
            if (!s.Economy.Enabled) throw new System.InvalidOperationException("測定倍率には有効な経済シナリオが必要です。");

            s.ScenarioId = s.ScenarioId + "-v" + villagerFactor + "-a" + soldierFactor;
            int populationFactor = System.Math.Max(villagerFactor, soldierFactor);
            s.Economy.MeasurementScale = true;
            s.Economy.AutoVillagerTarget = checked(s.Economy.AutoVillagerTarget * villagerFactor);
            s.Economy.PopulationCap = checked(s.Economy.PopulationCap * populationFactor);
            // Each barracks may keep a proportionally larger automatic infantry queue. This is the measurement
            // substitute for a barracks-count cap, while leaving the building placement rules untouched.
            s.Economy.AutoInfantryQueue = checked(s.Economy.AutoInfantryQueue * soldierFactor);
            s.Rules.FactionCap = checked(s.Rules.FactionCap * soldierFactor);
            for (int i = 0; i < s.Armies.Length; i++)
                s.Armies[i].Capacity = checked(s.Armies[i].Capacity * soldierFactor);

            if (soldierFactor == 1) return s;
            var scaled = new SoldierDefinition[checked(s.Soldiers.Length * soldierFactor)];
            var placementMap = soldierFactor > 16 ? new GridMap(s.Map) : null;
            var usedCells = soldierFactor > 16 ? new HashSet<int>() : null;
            uint id = 1;
            for (int a = 0; a < s.Armies.Length; a++)
            {
                uint armyId = s.Armies[a].Id;
                for (int copy = 0; copy < soldierFactor; copy++)
                {
                    int shift = (copy - (soldierFactor - 1) / 2) * CopySpacingMeters;
                    foreach (var d in s.Soldiers)
                    {
                        if (d.ArmyId != armyId) continue;
                        SimPoint position = placementMap == null ? new SimPoint(d.Position.X, d.Position.Z + Fix64.FromInt(shift))
                            : UniqueNearbyPassablePosition(s.Map, placementMap, usedCells, d.Position);
                        scaled[id - 1] = new SoldierDefinition
                        {
                            Id = id, FactionId = d.FactionId, ArmyId = d.ArmyId, Kind = d.Kind, Alive = d.Alive, Hp = d.Hp,
                            Position = position
                        };
                        id++;
                    }
                }
            }
            s.Soldiers = scaled;
            return s;
        }

        private static SimPoint UniqueNearbyPassablePosition(MapDefinition map, GridMap grid, HashSet<int> used, SimPoint origin)
        {
            int originCell = grid.Cell(origin);
            int originX = originCell % map.WidthCells, originZ = originCell / map.WidthCells;
            for (int radius = 0; radius < System.Math.Max(map.WidthCells, map.HeightCells); radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != radius) continue;
                        int x = originX + dx, z = originZ + dz;
                        if (x < 0 || z < 0 || x >= map.WidthCells || z >= map.HeightCells) continue;
                        int cell = z * map.WidthCells + x;
                        if (!grid.IsPassable(cell) || !used.Add(cell)) continue;
                        return grid.Center(cell);
                    }
            throw new System.InvalidOperationException("測定用の兵配置場所が不足しています。");
        }
    }
}
