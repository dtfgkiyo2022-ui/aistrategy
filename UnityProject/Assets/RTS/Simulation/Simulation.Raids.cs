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

        private const int RaidVillagerRetargetIntervalTicks = 10, RaidVillagerChaseExtraMeters = 4;
        private bool RaidVillagersFirstOn => world.Config.Economy.Enabled && world.Config.Economy.RaidVillagersFirst;

        private long[] villagerDamage = Array.Empty<long>();
        private long[] buildingDamage = Array.Empty<long>();
        private long[] beltDamage = Array.Empty<long>();

        private bool BuildingAttackOrderOn => world.Config.Economy.Enabled && world.Config.Economy.BuildingAttackOrder;

        private uint FindEnemyBuildingAtPoint(SimPoint point, uint faction)
        {
            int cell = world.Map.Cell(point);
            if (cell < 0) return 0;
            uint best = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || building.FactionId == faction || Array.IndexOf(Footprint(building), cell) < 0) continue;
                if (best == 0 || building.Id < best) best = building.Id;
            }
            return best;
        }

        private bool BuildingTargetInRange(uint buildingId, SimPoint from, Fix64 range)
        {
            if (buildingId == 0 || buildingId > world.BuildingCount) return false;
            var building = world.Buildings[buildingId - 1];
            return IsActualBuilding(building) && InRange(from, NearestFootprintPoint(building, from), range);
        }

        private bool BuildingTargetGone(uint armyId, uint faction)
        {
            if (!BuildingAttackOrderOn || armyId == 0 || armyId > buildingAttackTargets.Length) return false;
            uint buildingId = buildingAttackTargets[armyId - 1];
            if (buildingId == 0 || buildingId > world.BuildingCount) return false;
            var building = world.Buildings[buildingId - 1];
            return !IsActualBuilding(building) || building.FactionId == faction;
        }

        /// <summary>Intents phase: fills only an empty target, and never for a soldier that is retreating.</summary>
        private void PickRaidTarget(ref SoldierState s, bool preferVillagers, bool suppressVillagers)
        {
            if (!EconomyOn || s.TargetKind != 0 || s.IsRetreating) return;
            uint faction = s.Initial.FactionId;
            int best = -1; BigInteger bestDistance = 0;
            if (preferVillagers && !suppressVillagers && TryPickRaidVillager(faction, s.Position, s.Parameters.Range, out uint villagerId))
            {
                s.TargetKind = TargetVillager; s.TargetId = villagerId; return;
            }
            if (!preferVillagers && !suppressVillagers && world.Config.Economy.RaidLinePriority > 0 && TryPickRaidLineTarget(faction, s.Position, s.Parameters.Range,
                out byte lineTargetKind, out uint lineTargetId))
            {
                s.TargetKind = lineTargetKind; s.TargetId = lineTargetId; return;
            }
            if (preferVillagers && !suppressVillagers && world.Config.Economy.RaidLinePriority > 0 && TryPickRaidLineTarget(faction, s.Position, s.Parameters.Range,
                out lineTargetKind, out lineTargetId))
            {
                s.TargetKind = lineTargetKind; s.TargetId = lineTargetId; return;
            }
            if (!suppressVillagers && !preferVillagers && TryPickRaidVillager(faction, s.Position, s.Parameters.Range, out villagerId))
            {
                s.TargetKind = TargetVillager; s.TargetId = villagerId; return;
            }
            int buildingCandidates = QueryRaidBuildings(s.Position, s.Parameters.Range);
            for (int n = 0; n < buildingCandidates; n++)
            {
                int i = spatialBuildingCandidates[n];
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId == faction || !BuildingVisibleTo(faction, b) || !InRange(s.Position, NearestFootprintPoint(b, s.Position), s.Parameters.Range)) continue;
                BigInteger d = DistanceSquared(s.Position, NearestFootprintPoint(b, s.Position));
                if (best < 0 || d < bestDistance || d == bestDistance && i < best) { best = i; bestDistance = d; }
            }
            if (best >= 0) { s.TargetKind = TargetBuilding; s.TargetId = world.Buildings[best].Id; return; }
            // V3-5 (32 #4): last, an enemy belt cell in range - cutting the enemy's lines. Maps with ages only.
            if (!AgesOn) return;
            int cellBest = NearestEnemyBelt(faction, s.Position, s.Parameters.Range);
            if (cellBest >= 0) { s.TargetKind = TargetBelt; s.TargetId = (uint)cellBest + 1; }
        }

        private bool TryPickRaidVillager(uint faction, SimPoint from, Fix64 range, out uint villagerId)
        {
            int best = -1; BigInteger bestDistance = 0; uint bestId = uint.MaxValue;
            int candidates = QueryRaidVillagers(from, range);
            for (int n = 0; n < candidates; n++)
            {
                int index = spatialVillagerCandidates[n];
                var villager = world.Villagers[index];
                if (!villager.Alive || villager.FactionId == faction || !IsVisibleTo(faction, villager.Position)
                    || !InRange(from, villager.Position, range)) continue;
                BigInteger distance = DistanceSquared(from, villager.Position);
                if (best < 0 || distance < bestDistance || distance == bestDistance && villager.Id < bestId)
                {
                    best = index; bestDistance = distance; bestId = villager.Id;
                }
            }
            villagerId = best < 0 ? 0 : world.Villagers[best].Id;
            return best >= 0;
        }

        private void RetargetRepairingVillager(ref SoldierState soldier)
        {
            if (soldier.TargetKind != TargetBuilding || soldier.TargetId == 0 || soldier.TargetId > world.BuildingCount) return;
            var building = world.Buildings[soldier.TargetId - 1];
            if (!IsActualBuilding(building) || building.FactionId == soldier.Initial.FactionId || building.Hp >= MaxHpOf(building)) return;
            int best = -1; BigInteger bestDistance = 0; uint bestId = uint.MaxValue;
            Fix64 searchRange = soldier.Parameters.Range + Fix64.FromInt(4);
            int candidates = QueryRaidVillagers(soldier.Position, searchRange);
            for (int n = 0; n < candidates; n++)
            {
                int index = spatialVillagerCandidates[n];
                var villager = world.Villagers[index];
                if (!villager.Alive || villager.FactionId == soldier.Initial.FactionId || villager.BuildingId != building.Id
                    || villager.Task != VillagerTask.Building || !IsVisibleTo(soldier.Initial.FactionId, villager.Position)
                    || !InRange(soldier.Position, villager.Position, searchRange)) continue;
                var distance = DistanceSquared(soldier.Position, villager.Position);
                if (best < 0 || distance < bestDistance || distance == bestDistance && villager.Id < bestId)
                {
                    best = index; bestDistance = distance; bestId = villager.Id;
                }
            }
            if (best >= 0)
            {
                var villager = world.Villagers[best];
                if (InRange(soldier.Position, villager.Position, soldier.Parameters.Range))
                {
                    soldier.TargetKind = TargetVillager;
                    soldier.TargetId = villager.Id;
                    world.Economies[soldier.Initial.FactionId - 1].FortifyRetargets = checked(world.Economies[soldier.Initial.FactionId - 1].FortifyRetargets + 1);
                }
                else if (!soldier.IsRetreating)
                {
                    soldier.MoveGoal = villager.Position;
                    world.Economies[soldier.Initial.FactionId - 1].FortifyRetargets = checked(world.Economies[soldier.Initial.FactionId - 1].FortifyRetargets + 1);
                }
            }
        }

        /// <summary>The target is cleared every tick, so a villager already in range wins in PickRaidTarget. This
        /// also walks a soldier hitting a building towards a villager just out of reach (range + 4 m, as for repairers).</summary>
        private void RetargetRaidVillagerFirst(ref SoldierState soldier, in ArmyState army, bool canChase)
        {
            if (!RaidVillagersFirstOn || soldier.TargetKind != TargetBuilding && soldier.TargetKind != TargetBelt
                || soldier.IsRetreating || army.BreakingPath
                || soldier.Initial.Kind == UnitKind.Ram || soldier.Initial.Kind == UnitKind.Cannon
                || IsNamedFocusTarget(army)) return;
            bool countTick = (world.Tick + soldier.Initial.Id) % RaidVillagerRetargetIntervalTicks == 0;
            ref var economy = ref world.Economies[soldier.Initial.FactionId - 1];
            if (countTick && TryPickRaidVillager(soldier.Initial.FactionId, soldier.Position, soldier.Parameters.Range, out uint villagerId))
            {
                soldier.TargetKind = TargetVillager;
                soldier.TargetId = villagerId;
                economy.RaidVillagerRetargets = checked(economy.RaidVillagerRetargets + 1);
                return;
            }
            if (!canChase || !TryPickRaidVillager(soldier.Initial.FactionId, soldier.Position,
                soldier.Parameters.Range + Fix64.FromInt(RaidVillagerChaseExtraMeters), out uint nearId)) return;
            soldier.MoveGoal = world.Villagers[nearId - 1].Position;
            if (countTick) economy.RaidVillagerChases = checked(economy.RaidVillagerChases + 1);
        }

        private bool IsNamedFocusTarget(in ArmyState army)
        {
            if (army.Policy != PolicyKind.Focus || army.CommandId == 0) return false;
            var command = CommandById(army.CommandId);
            return command != null && command.Order.Kind == PolicyKind.Focus
                && (command.Order.Goal.Kind == GoalKind.Core || IsLatePushCoreFocus(command.Order));
        }

        private bool TryPickRaidLineTarget(uint faction, SimPoint from, Fix64 range, out byte targetKind, out uint targetId)
        {
            targetKind = 0; targetId = 0;
            int bestType = int.MaxValue; uint bestId = 0; BigInteger bestDistance = 0;
            int buildingCandidates = QueryRaidBuildings(from, range);
            for (int n = 0; n < buildingCandidates; n++)
            {
                int i = spatialBuildingCandidates[n];
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId == faction || LineForBuilding(b.FactionId, b.Id) < 0
                    || !BuildingVisibleTo(faction, b)) continue;
                var point = NearestFootprintPoint(b, from);
                if (!InRange(from, point, range)) continue;
                BigInteger distance = DistanceSquared(from, point);
                if (bestType > TargetBuilding || bestType == TargetBuilding && (distance < bestDistance
                    || distance == bestDistance && b.Id < bestId))
                {
                    bestType = TargetBuilding; bestId = b.Id; bestDistance = distance;
                }
            }
            int beltCandidates = QueryRaidBelts(from, range);
            for (int n = 0; n < beltCandidates; n++)
            {
                int cell = spatialBeltCandidates[n];
                var belt = world.Belts[cell];
                if (belt.FactionId == 0 || belt.FactionId == faction || BeltUnderFortification(cell)
                    || LineForBelt(belt.FactionId, cell) < 0
                    || !world.Factions[faction - 1].VisibleCells[cell]) continue;
                var point = world.Map.Center(cell);
                if (!InRange(from, point, range)) continue;
                BigInteger distance = DistanceSquared(from, point);
                uint id = checked((uint)cell + 1);
                if (bestType > TargetBelt || bestType == TargetBelt && (distance < bestDistance
                    || distance == bestDistance && id < bestId))
                {
                    bestType = TargetBelt; bestId = id; bestDistance = distance;
                }
            }
            if (bestType == int.MaxValue) return false;
            targetKind = (byte)bestType; targetId = bestId; return true;
        }

        /// <summary>The visible enemy belt cell nearest <paramref name="from"/> within <paramref name="range"/> (then the lower cell), or -1.</summary>
        private int NearestEnemyBelt(uint faction, SimPoint from, Fix64 range)
        {
            int beltCandidates = QueryRaidBelts(from, range);
            int best = -1; BigInteger bestDistance = 0;
            for (int n = 0; n < beltCandidates; n++)
            {
                int cell = spatialBeltCandidates[n];
                var belt = world.Belts[cell];
                if (belt.FactionId == 0 || belt.FactionId == faction || BeltUnderFortification(cell)
                    || !world.Factions[faction - 1].VisibleCells[cell]) continue;
                var centre = world.Map.Center(cell);
                if (!InRange(from, centre, range)) continue;
                BigInteger d = DistanceSquared(from, centre);
                if (best < 0 || d < bestDistance || d == bestDistance && cell < best) { best = cell; bestDistance = d; }
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
                villagerDamage[i] = checked(villagerDamage[i] + SanctuaryDamage(faction, s.Parameters.Damage));
                MetropolisVillagerCounterattack(v.FactionId, i, (int)(s.Initial.Id - 1));
            }
            else if (s.TargetKind == TargetBelt)
            {
                int cell = checked((int)s.TargetId - 1);
                var belt = world.Belts[cell];
                if (belt.FactionId == 0 || belt.FactionId == faction || BeltUnderFortification(cell)
                    || !world.Factions[faction - 1].VisibleCells[cell]
                    || !InRange(s.Position, world.Map.Center(cell), s.Parameters.Range)) return;
                beltDamage[cell] = checked(beltDamage[cell] + SanctuaryDamage(faction, s.Parameters.Damage));
            }
            else
            {
                int i = checked((int)s.TargetId - 1);
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId == faction || !BuildingVisibleTo(faction, b) || !InRange(s.Position, NearestFootprintPoint(b, s.Position), s.Parameters.Range)) return;
                buildingDamage[i] = checked(buildingDamage[i] + SanctuaryDamage(faction, SiegeDamage(s)));
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
                if (world.Villagers[i].Alive && world.Villagers[i].Hp == 0)
                {
                    world.Villagers[i].Alive = false;
                    // Ordinary Carry follows the historical V3 rule, but a caravan load is an unpaid route
                    // balance and is lost with the trader.
                    world.Villagers[i].CaravanWood = 0;
                    world.Villagers[i].CaravanGems = 0;
                }
            bool opened = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || b.Reserved || b.Hp != 0) continue;
                b.Alive = false;
                RemoveRegionCenter(b);
                if (b.Kind == BuildingKind.Bridge)
                {
                    b.DestroyedTick = world.Tick;
                    KillBridgeOccupants(b);
                    foreach (int cell in Footprint(b)) world.Map.SetPassable(cell, false);
                }
                else
                {
                    RemoveFortifyWallOverBelt(b);
                    foreach (int cell in Footprint(b)) world.Map.SetPassable(cell, true);
                    if (FortifyOn && (b.Kind == BuildingKind.Wall || b.Kind == BuildingKind.Gate))
                    {
                        world.Economies[b.FactionId - 1].FortifyDestroyed = checked(world.Economies[b.FactionId - 1].FortifyDestroyed + 1);
                        CollapseAdjacentFortifications(b);
                    }
                }
                DelayLineAfterRaid(b.FactionId, b.Id, FootprintCenter(b.OriginCell, SizeOf(b.Kind)));
                opened = true;
            }
            if (opened) TerrainChanged();
            // A broken belt is gone with what it carried; the line's order is worked out again.
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                if (world.Belts[cell].FactionId == 0 || world.Belts[cell].Hp != 0) continue;
                uint faction = world.Belts[cell].FactionId;
                int fortification = FortifyOn ? FortificationAtCell(cell) : -1;
                if (fortification >= 0) RemoveFortifyWallOverBelt(world.Buildings[fortification]);
                DelayLineAfterRaid(faction, cell);
                if (world.Belts[cell].Item == ResourceKind.Glass)
                    brokenBeltGlass[faction - 1] = checked(brokenBeltGlass[faction - 1] + BeltItemCount(world.Belts[cell]));
                world.Belts[cell] = default;
                InvalidateBeltCaches();
            }
        }

        private void CollapseAdjacentFortifications(BuildingState destroyed)
        {
            int width = world.Config.Map.WidthCells;
            foreach (int cell in Footprint(destroyed))
            {
                int[] adjacent = { cell + width, cell + 1, cell - width, cell - 1 };
                for (int n = 0; n < adjacent.Length; n++)
                {
                    int candidate = adjacent[n];
                    if (candidate < 0 || candidate >= world.Belts.Length || n == 1 && candidate / width != cell / width
                        || n == 3 && candidate / width != cell / width) continue;
                    int index = FortificationAtCell(candidate);
                    if (index < 0) continue;
                    ref var collapsed = ref world.Buildings[index];
                    if (!collapsed.Alive || collapsed.FactionId != destroyed.FactionId) continue;
                    RemoveFortifyWallOverBelt(collapsed);
                    collapsed.Alive = false;
                    collapsed.Hp = 0;
                    foreach (int openCell in Footprint(collapsed)) world.Map.SetPassable(openCell, true);
                    world.Economies[destroyed.FactionId - 1].FortifyCollapsed = checked(world.Economies[destroyed.FactionId - 1].FortifyCollapsed + 1);
                    DelayLineAfterRaid(collapsed.FactionId, collapsed.Id, FootprintCenter(collapsed.OriginCell, SizeOf(collapsed.Kind)));
                }
            }
        }

        /// <summary>
        /// 40.5: a soldier of an army that is breaking in strikes the breach cell's wall once it is in range. A fight with
        /// an enemy soldier or the core comes first. Review (11-04): the soldier's own army is passed in; looking it up by
        /// scanning every army's soldier list cost soldiers x armies x size each tick.
        /// </summary>
        private void PickBreachTarget(ref SoldierState soldier, in ArmyState army)
        {
            if (soldier.IsRetreating || soldier.TargetKind == 1 || soldier.TargetKind == 2) return;
            if (!army.BreakingPath || army.BreachCell < 0) return;
            int index = FortificationAtCell(army.BreachCell);
            if (index < 0) return;
            var building = world.Buildings[index];
            if (building.FactionId == soldier.Initial.FactionId || !BuildingVisibleTo(soldier.Initial.FactionId, building)
                || !InRange(soldier.Position, NearestFootprintPoint(building, soldier.Position), soldier.Parameters.Range)) return;
            soldier.TargetKind = TargetBuilding;
            soldier.TargetId = building.Id;
        }

        private int FortificationAtCell(int cell)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.Complete && (b.Kind == BuildingKind.Wall || b.Kind == BuildingKind.Gate)
                    && b.OriginCell == cell) return i;
            }
            return -1;
        }

        private bool BuildingVisibleTo(uint faction, BuildingState b)
        {
            if (b.Reserved) return false;
            foreach (int cell in Footprint(b))
                if (world.Factions[faction - 1].VisibleCells[cell]) return true;
            return false;
        }

        /// <summary>The point of the footprint square closest to <paramref name="from"/>, for range checks.</summary>
        private SimPoint NearestFootprintPoint(BuildingState b, SimPoint from)
        {
            if (b.Kind == BuildingKind.Bridge || b.Kind == BuildingKind.Tollgate)
            {
                var cells = Footprint(b);
                int best = cells.Length == 0 ? -1 : cells[0];
                for (int i = 1; i < cells.Length; i++)
                    if (DistanceSquared(from, world.Map.Center(cells[i])) < DistanceSquared(from, world.Map.Center(best))) best = cells[i];
                return best < 0 ? from : world.Map.Center(best);
            }
            int width = world.Config.Map.WidthCells, size = SizeOf(b.Kind);
            long cell = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw;
            long minX = b.OriginCell % width * cell, minZ = b.OriginCell / width * cell;
            long maxX = minX + size * cell, maxZ = minZ + size * cell;
            long x = from.X.Raw < minX ? minX : from.X.Raw > maxX ? maxX : from.X.Raw;
            long z = from.Z.Raw < minZ ? minZ : from.Z.Raw > maxZ ? maxZ : from.Z.Raw;
            return new SimPoint(Fix64.FromRaw(x), Fix64.FromRaw(z));
        }

        private void KillBridgeOccupants(BuildingState bridge)
        {
            var cells = Footprint(bridge);
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Alive && Array.IndexOf(cells, world.Map.Cell(world.Soldiers[i].Position)) >= 0)
                {
                    world.Soldiers[i].Hp = 0;
                    world.Soldiers[i].Alive = false;
                    for (uint faction = 1; faction <= 2; faction++)
                        if (world.Soldiers[i].Initial.FactionId != faction && IsVisibleTo(faction, world.Soldiers[i].Position))
                            world.Factions[faction - 1].ContactIds[i] = 0;
                }
            for (int i = 0; i < world.VillagerCount; i++)
                if (world.Villagers[i].Alive && Array.IndexOf(cells, world.Map.Cell(world.Villagers[i].Position)) >= 0)
                {
                    world.Villagers[i].Hp = 0;
                    world.Villagers[i].Alive = false;
                }
        }
    }
}
