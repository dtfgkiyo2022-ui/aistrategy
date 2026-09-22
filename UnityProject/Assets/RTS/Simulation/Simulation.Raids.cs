using System;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// Ver.3 raids (technical-design-v3 5.5, V3-1 PR3b): enemy villagers and buildings can be attacked and destroyed.
    /// They are not contacts in the faction observation, so the Decision tactics never choose or chase them; a soldier
    /// the tactics left without a target strikes the nearest enemy villager, then building, that its faction can see and
    /// that is in range. The Decision order (soldier, then core) is untouched.
    /// </summary>
    public sealed partial class Simulation
    {
        internal const byte TargetVillager = 3, TargetBuilding = 4, TargetBelt = 5;

        private long[] villagerDamage = Array.Empty<long>();
        private long[] buildingDamage = Array.Empty<long>();
        private long[] beltDamage = Array.Empty<long>();

        /// <summary>Intents phase: fills only an empty target, and never for a soldier that is retreating.</summary>
        private void PickRaidTarget(ref SoldierState s)
        {
            if (!EconomyOn || s.TargetKind != 0 || s.IsRetreating) return;
            uint faction = s.Initial.FactionId;
            int best = -1; BigInteger bestDistance = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (!v.Alive || v.FactionId == faction || !IsVisibleTo(faction, v.Position) || !InRange(s.Position, v.Position, s.Parameters.Range)) continue;
                BigInteger d = DistanceSquared(s.Position, v.Position);
                if (best < 0 || d < bestDistance) { best = i; bestDistance = d; }
            }
            if (best >= 0) { s.TargetKind = TargetVillager; s.TargetId = world.Villagers[best].Id; return; }
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.FactionId == faction || !BuildingVisibleTo(faction, b) || !InRange(s.Position, NearestFootprintPoint(b, s.Position), s.Parameters.Range)) continue;
                BigInteger d = DistanceSquared(s.Position, NearestFootprintPoint(b, s.Position));
                if (best < 0 || d < bestDistance) { best = i; bestDistance = d; }
            }
            if (best >= 0) { s.TargetKind = TargetBuilding; s.TargetId = world.Buildings[best].Id; return; }
            // V3-5 (32 #4): last, an enemy belt cell in range - cutting the enemy's lines. Maps with ages only.
            if (!AgesOn) return;
            int cellBest = NearestEnemyBelt(faction, s.Position, s.Parameters.Range);
            if (cellBest >= 0) { s.TargetKind = TargetBelt; s.TargetId = (uint)cellBest + 1; }
        }

        /// <summary>The visible enemy belt cell nearest <paramref name="from"/> within <paramref name="range"/> (then the lower cell), or -1.</summary>
        private int NearestEnemyBelt(uint faction, SimPoint from, Fix64 range)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, cellSize = world.Config.Map.CellSizeMeters;
            int reach = (int)(range.Raw / 65536 / cellSize) + 1, here = world.Map.Cell(from), hx = here % width, hz = here / width;
            int best = -1; BigInteger bestDistance = 0;
            for (int z = Math.Max(0, hz - reach); z <= Math.Min(height - 1, hz + reach); z++)
                for (int x = Math.Max(0, hx - reach); x <= Math.Min(width - 1, hx + reach); x++)
                {
                    int cell = z * width + x;
                    var belt = world.Belts[cell];
                    if (belt.FactionId == 0 || belt.FactionId == faction || !world.Factions[faction - 1].VisibleCells[cell]) continue;
                    var centre = world.Map.Center(cell);
                    if (!InRange(from, centre, range)) continue;
                    BigInteger d = DistanceSquared(from, centre);
                    if (best < 0 || d < bestDistance) { best = cell; bestDistance = d; }
                }
            return best;
        }

        /// <summary>Attack phase: the engine re-checks the target like any other, then adds the damage.</summary>
        private void AddRaidDamage(ref SoldierState s)
        {
            uint faction = s.Initial.FactionId;
            if (s.TargetKind == TargetVillager)
            {
                int i = checked((int)s.TargetId - 1);
                var v = world.Villagers[i];
                if (!v.Alive || v.FactionId == faction || !IsVisibleTo(faction, v.Position) || !InRange(s.Position, v.Position, s.Parameters.Range)) return;
                villagerDamage[i] = checked(villagerDamage[i] + s.Parameters.Damage);
            }
            else if (s.TargetKind == TargetBelt)
            {
                int cell = checked((int)s.TargetId - 1);
                var belt = world.Belts[cell];
                if (belt.FactionId == 0 || belt.FactionId == faction || !world.Factions[faction - 1].VisibleCells[cell]
                    || !InRange(s.Position, world.Map.Center(cell), s.Parameters.Range)) return;
                beltDamage[cell] = checked(beltDamage[cell] + s.Parameters.Damage);
            }
            else
            {
                int i = checked((int)s.TargetId - 1);
                var b = world.Buildings[i];
                if (!b.Alive || b.FactionId == faction || !BuildingVisibleTo(faction, b) || !InRange(s.Position, NearestFootprintPoint(b, s.Position), s.Parameters.Range)) return;
                buildingDamage[i] = checked(buildingDamage[i] + s.Parameters.Damage);
            }
            s.NextAttackTick = checked(world.Tick + s.Parameters.AttackIntervalTicks);
            s.IsAttacking = true;
        }

        private void ClearRaidDamage()
        {
            if (villagerDamage.Length < world.Villagers.Length) villagerDamage = new long[world.Villagers.Length];
            if (buildingDamage.Length < world.Buildings.Length) buildingDamage = new long[world.Buildings.Length];
            if (beltDamage.Length < world.Belts.Length) beltDamage = new long[world.Belts.Length];
            Array.Clear(villagerDamage, 0, villagerDamage.Length);
            Array.Clear(buildingDamage, 0, buildingDamage.Length);
            Array.Clear(beltDamage, 0, beltDamage.Length);
        }

        private void ApplyRaidDamage()
        {
            for (int i = 0; i < world.VillagerCount; i++) world.Villagers[i].Hp = RemainingHp(world.Villagers[i].Hp, villagerDamage[i]);
            for (int i = 0; i < world.BuildingCount; i++) world.Buildings[i].Hp = RemainingHp(world.Buildings[i].Hp, buildingDamage[i]);
            for (int cell = 0; cell < world.Belts.Length; cell++)
                if (beltDamage[cell] > 0) world.Belts[cell].Hp = RemainingHp(world.Belts[cell].Hp, beltDamage[cell]);
        }

        /// <summary>
        /// Deaths phase. A dead villager keeps what it carried (the load is lost, not refunded). A destroyed building
        /// opens its footprint again, so every route is planned afresh, and its queue is lost.
        /// </summary>
        private void ResolveRaidDeaths()
        {
            for (int i = 0; i < world.VillagerCount; i++)
                if (world.Villagers[i].Alive && world.Villagers[i].Hp == 0) world.Villagers[i].Alive = false;
            bool opened = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || b.Hp != 0) continue;
                b.Alive = false;
                foreach (int cell in Footprint(b)) world.Map.SetPassable(cell, true);
                opened = true;
            }
            if (opened) TerrainChanged();
            // A broken belt is gone with what it carried; the line's order is worked out again.
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                if (world.Belts[cell].FactionId == 0 || world.Belts[cell].Hp != 0) continue;
                world.Belts[cell] = default;
                world.BeltOrder = null;
            }
        }

        private bool BuildingVisibleTo(uint faction, BuildingState b)
        {
            foreach (int cell in Footprint(b))
                if (world.Factions[faction - 1].VisibleCells[cell]) return true;
            return false;
        }

        /// <summary>The point of the footprint square closest to <paramref name="from"/>, for range checks.</summary>
        private SimPoint NearestFootprintPoint(BuildingState b, SimPoint from)
        {
            int width = world.Config.Map.WidthCells, size = SizeOf(b.Kind);
            long cell = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw;
            long minX = b.OriginCell % width * cell, minZ = b.OriginCell / width * cell;
            long maxX = minX + size * cell, maxZ = minZ + size * cell;
            long x = from.X.Raw < minX ? minX : from.X.Raw > maxX ? maxX : from.X.Raw;
            long z = from.Z.Raw < minZ ? minZ : from.Z.Raw > maxZ ? maxZ : from.Z.Raw;
            return new SimPoint(Fix64.FromRaw(x), Fix64.FromRaw(z));
        }
    }
}
