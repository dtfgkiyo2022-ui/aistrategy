using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private bool WardsOn => EconomyOn && world.Config.Economy.Wards;
        private bool HuntWardFixOn => WardsOn && world.Config.Economy.HuntWardFix;
        // [faction - 1, kind - 1]. These are presentation/measurement counters only; ward lifetime is
        // still held by the canonical WardState array.
        private readonly int[,] wardPlacedCount = new int[2, 2];
        private readonly int[,] wardExpiredCount = new int[2, 2];
        private readonly int[,] wardDestroyedCount = new int[2, 2];
        private readonly int[,] wardDestroyedByEnemyCount = new int[2, 2];
        private int[] wardScoutIndices = Array.Empty<int>();
        private int wardScoutCount;
        private int wardScoutKnownSoldierCount;
        private uint[] automaticWardTargets = Array.Empty<uint>();
        private BigInteger[] automaticWardDistances = Array.Empty<BigInteger>();

        public int WardPlacedCount(uint faction) => WardPlacedCount(faction, WardKind.Hidden) + WardPlacedCount(faction, WardKind.Revealing);
        public int WardPlacedCount(uint faction, WardKind kind) => WardCounter(wardPlacedCount, faction, kind);
        public int WardExpiredCount(uint faction, WardKind kind) => WardCounter(wardExpiredCount, faction, kind);
        public int WardDestroyedCount(uint faction, WardKind kind) => WardCounter(wardDestroyedCount, faction, kind);
        public int WardDestroyedByEnemyCount(uint faction, WardKind kind) => WardCounter(wardDestroyedByEnemyCount, faction, kind);
        public int WardAbandonedCountFor(uint faction) => HuntWardFixOn && faction >= 1 && faction <= 2 ? world.WardAbandonedCount[faction - 1] : 0;
        public int WardPendingVillagerMaxFor(uint faction) => HuntWardFixOn && faction >= 1 && faction <= 2 ? world.WardPendingVillagerMax[faction - 1] : 0;

        private static int WardCounter(int[,] counters, uint faction, WardKind kind)
            => faction >= 1 && faction <= 2 && kind >= WardKind.Hidden && kind <= WardKind.Revealing
                ? counters[faction - 1, (int)kind - 1] : 0;

        private void InitializeWardScoutIndex()
        {
            wardScoutIndices = new int[world.Soldiers.Length];
            wardScoutCount = 0;
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Initial.Kind == UnitKind.Scout) wardScoutIndices[wardScoutCount++] = i;
            wardScoutKnownSoldierCount = world.SoldierCount;
            automaticWardTargets = new uint[Math.Max(1, world.Soldiers.Length)];
            automaticWardDistances = new BigInteger[automaticWardTargets.Length];
        }

        private void EnsureWardScratchCapacity()
        {
            if (automaticWardTargets.Length < world.Soldiers.Length)
            {
                Array.Resize(ref automaticWardTargets, world.Soldiers.Length);
                Array.Resize(ref automaticWardDistances, world.Soldiers.Length);
            }
            if (wardScoutKnownSoldierCount >= world.SoldierCount) return;
            if (wardScoutIndices.Length < world.Soldiers.Length) Array.Resize(ref wardScoutIndices, world.Soldiers.Length);
            for (int i = wardScoutKnownSoldierCount; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Initial.Kind == UnitKind.Scout) wardScoutIndices[wardScoutCount++] = i;
            wardScoutKnownSoldierCount = world.SoldierCount;
        }

        private bool StartWardPlacement(EconomyCommand command)
        {
            if (!WardsOn || (command.Ward != WardKind.Hidden && command.Ward != WardKind.Revealing)) return false;
            if (command.Cell < 0 || command.Cell >= world.Config.Map.WidthCells * world.Config.Map.HeightCells
                || !world.Map.IsPassableFor(command.Cell, command.FactionId)) return false;
            var point = world.Map.Center(command.Cell);
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[command.FactionId - 1];
            int wood = command.Ward == WardKind.Hidden ? rules.WardHiddenWoodCost : rules.WardRevealingWoodCost;
            int gold = command.Ward == WardKind.Revealing ? rules.WardRevealingGoldCost : 0;
            if (economy.Wood < wood || economy.Gold < gold || CountActiveWards(command.FactionId, command.Ward) >= WardCap(command.Ward)) return false;
            if (command.Unit == UnitKind.Scout)
            {
                int i = checked((int)command.ProducerId - 1);
                if (i < 0 || i >= world.Soldiers.Length) return false;
                ref var scout = ref world.Soldiers[i];
                if (!scout.Alive || scout.Initial.FactionId != command.FactionId || scout.Initial.Kind != UnitKind.Scout || scout.WardPlaceRemaining > 0
                    || !InRange(scout.Position, point, Fix64.FromInt(rules.WardPlacementRangeMeters))) return false;
                economy.Wood -= wood; economy.Gold -= gold;
                scout.WardPlacePending = false; scout.WardPlaceKind = command.Ward; scout.WardPlaceCell = command.Cell;
                scout.WardPlaceStartTick = 0;
                scout.WardPlaceRemaining = Math.Max(1, rules.WardPlacementTicks); scout.MoveGoal = scout.Position;
                return true;
            }
            int vindex = checked((int)command.ProducerId - 1);
            if (vindex < 0 || vindex >= world.Villagers.Length) return false;
            ref var villager = ref world.Villagers[vindex];
            if (!villager.Alive || villager.FactionId != command.FactionId || villager.WardPlaceRemaining > 0
                || !InRange(villager.Position, point, Fix64.FromInt(rules.WardPlacementRangeMeters))) return false;
            economy.Wood -= wood; economy.Gold -= gold;
            villager.WardPlacePending = false; villager.WardPlaceKind = command.Ward; villager.WardPlaceCell = command.Cell;
            villager.WardPlaceStartTick = 0;
            villager.WardPlaceRemaining = Math.Max(1, rules.WardPlacementTicks); villager.MoveGoal = villager.Position;
            return true;
        }

        private bool QueueAutomaticWardPlacement(uint faction, WardKind kind, uint workerId, int cell, bool scout)
        {
            if (!WardsOn || cell < 0 || cell >= world.Config.Map.WidthCells * world.Config.Map.HeightCells
                || !world.Map.IsPassableFor(cell, faction)) return false;
            SimPoint target = world.Map.Center(cell);
            if (scout)
            {
                int index = checked((int)workerId - 1);
                if (index < 0 || index >= world.Soldiers.Length) return false;
                ref var unit = ref world.Soldiers[index];
                if (!unit.Alive || unit.Initial.FactionId != faction || unit.Initial.Kind != UnitKind.Scout
                    || unit.WardPlacePending || unit.WardPlaceRemaining > 0) return false;
                if (InRange(unit.Position, target, Fix64.FromInt(world.Config.Economy.WardPlacementRangeMeters)))
                    return StartWardPlacement(EconomyCommand.PlaceWard(faction, 0, kind, workerId, cell, true));
                unit.WardPlacePending = true; unit.WardPlaceKind = kind; unit.WardPlaceCell = cell; unit.MoveGoal = target;
                if (HuntWardFixOn) unit.WardPlaceStartTick = world.Tick;
                return true;
            }
            int villagerIndex = checked((int)workerId - 1);
            if (villagerIndex < 0 || villagerIndex >= world.Villagers.Length) return false;
            ref var villager = ref world.Villagers[villagerIndex];
            if (!villager.Alive || villager.FactionId != faction || villager.WardPlacePending || villager.WardPlaceRemaining > 0)
                return false;
            if (InRange(villager.Position, target, Fix64.FromInt(world.Config.Economy.WardPlacementRangeMeters)))
                return StartWardPlacement(EconomyCommand.PlaceWard(faction, 0, kind, workerId, cell, false));
            villager.WardPlacePending = true; villager.WardPlaceKind = kind; villager.WardPlaceCell = cell; villager.MoveGoal = target;
            if (HuntWardFixOn) villager.WardPlaceStartTick = world.Tick;
            return true;
        }

        private void OrderWardAttack(EconomyCommand command)
        {
            if (!WardsOn || command.TargetId == 0 || command.TargetId > world.WardCount) return;
            ref var ward = ref world.Wards[command.TargetId - 1];
            if (!ward.Alive || ward.FactionId == command.FactionId || !WardVisibleTo(command.FactionId, ward)) return;
            if (command.Unit == UnitKind.Scout)
            {
                int i = checked((int)command.ProducerId - 1);
                if (i < 0 || i >= world.Soldiers.Length) return;
                ref var scout = ref world.Soldiers[i];
                if (!scout.Alive || scout.Initial.FactionId != command.FactionId || scout.Initial.Kind != UnitKind.Scout) return;
                scout.WardAttackTargetId = command.TargetId;
                scout.MoveGoal = ward.Position;
                return;
            }
            int vindex = checked((int)command.ProducerId - 1);
            if (vindex < 0 || vindex >= world.Villagers.Length) return;
            ref var villager = ref world.Villagers[vindex];
            if (!villager.Alive || villager.FactionId != command.FactionId) return;
            villager.WardAttackTargetId = command.TargetId;
            villager.WardNextAttackTick = world.Tick;
        }

        private int WardCap(WardKind kind) => kind == WardKind.Hidden ? world.Config.Economy.WardHiddenMax : world.Config.Economy.WardRevealingMax;

        private int CountActiveWards(uint faction, WardKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.WardCount; i++)
                if (world.Wards[i].Alive && world.Wards[i].FactionId == faction && world.Wards[i].Kind == kind) count++;
            return count;
        }

        private void CompleteWardPlacement(ref VillagerState villager)
        {
            PlaceWard(villager.FactionId, villager.WardPlaceKind, world.Map.Center(villager.WardPlaceCell));
            if (HuntWardFixOn) ReleaseVillagerRoute(ref villager);
            villager.WardPlacePending = false; villager.WardPlaceKind = 0; villager.WardPlaceCell = 0; villager.WardPlaceRemaining = 0; villager.WardPlaceStartTick = 0;
        }

        private void CompleteWardPlacement(ref SoldierState scout)
        {
            PlaceWard(scout.Initial.FactionId, scout.WardPlaceKind, world.Map.Center(scout.WardPlaceCell));
            if (HuntWardFixOn) ReleaseLocalPath(ref scout);
            scout.WardPlacePending = false; scout.WardPlaceKind = 0; scout.WardPlaceCell = 0; scout.WardPlaceRemaining = 0; scout.WardPlaceStartTick = 0;
        }

        private void PlaceWard(uint faction, WardKind kind, SimPoint position)
        {
            int cap = WardCap(kind);
            if (CountActiveWards(faction, kind) >= cap)
            {
                if (kind == WardKind.Revealing) return;
                int oldest = -1;
                for (int i = 0; i < world.WardCount; i++)
                {
                    var candidate = world.Wards[i];
                    if (!candidate.Alive || candidate.FactionId != faction || candidate.Kind != kind) continue;
                    if (oldest < 0 || candidate.PlacedTick < world.Wards[oldest].PlacedTick
                        || candidate.PlacedTick == world.Wards[oldest].PlacedTick && candidate.Id < world.Wards[oldest].Id) oldest = i;
                }
                if (oldest >= 0) DestroyWard(oldest, 0);
            }
            uint id = world.NextWardId++;
            int slot = checked((int)id - 1);
            if (slot >= world.Wards.Length) Array.Resize(ref world.Wards, Math.Max(slot + 1, world.Wards.Length * 2));
            world.Wards[slot] = new WardState { Id = id, FactionId = faction, Kind = kind, Position = position,
                Hp = world.Config.Economy.WardHp, PlacedTick = world.Tick,
                ExpiryTick = kind == WardKind.Hidden ? checked(world.Tick + world.Config.Economy.WardHiddenDurationTicks) : 0, Alive = true };
            wardPlacedCount[faction - 1, (int)kind - 1]++;
        }

        private void ExpireWards()
        {
            if (!WardsOn) return;
            for (int i = 0; i < world.WardCount; i++)
                if (world.Wards[i].Alive && world.Wards[i].ExpiryTick > 0 && world.Tick >= world.Wards[i].ExpiryTick) DestroyWard(i, 0, true);
        }

        private void DestroyWard(int index, uint byFaction, bool expired = false)
        {
            if (index < 0 || index >= world.WardCount || !world.Wards[index].Alive) return;
            uint owner = world.Wards[index].FactionId;
            world.Wards[index].Alive = false;
            if (owner >= 1 && owner <= 2)
            {
                int kind = (int)world.Wards[index].Kind - 1;
                if (expired) wardExpiredCount[owner - 1, kind]++;
                else wardDestroyedCount[owner - 1, kind]++;
                if (byFaction >= 1 && byFaction <= 2 && byFaction != owner) wardDestroyedByEnemyCount[owner - 1, kind]++;
            }
        }

        private bool IsWardDetectedBy(uint observer, WardState ward)
        {
            if (ward.FactionId == observer) return true;
            EnsureWardScratchCapacity();
            Fix64 reveal = Fix64.FromInt(world.Config.Economy.WardRevealMeters);
            for (int i = 0; i < world.WardCount; i++)
            {
                var source = world.Wards[i];
                if (source.Alive && source.FactionId == observer && source.Kind == WardKind.Revealing
                    && InRange(source.Position, ward.Position, reveal)) return true;
            }
            Fix64 scoutReveal = Fix64.FromInt(world.Config.Economy.WardScoutRevealMeters);
            for (int n = 0; n < wardScoutCount; n++)
            {
                var scout = world.Soldiers[wardScoutIndices[n]];
                if (scout.Alive && scout.Initial.FactionId == observer && scout.Initial.Kind == UnitKind.Scout
                    && InRange(scout.Position, ward.Position, scoutReveal)) return true;
            }
            return false;
        }

        private bool WardVisibleTo(uint observer, WardState ward)
            => ward.Alive && (ward.FactionId == observer || ward.Kind == WardKind.Revealing && IsVisibleTo(observer, ward.Position)
                || ward.Kind == WardKind.Hidden && IsWardDetectedBy(observer, ward));

        private List<WardView> OwnWardViewsFor(uint faction)
        {
            var result = new List<WardView>();
            for (int i = 0; i < world.WardCount; i++)
            {
                var ward = world.Wards[i];
                if (ward.Alive && ward.FactionId == faction)
                    result.Add(new WardView(ward.Id, ward.FactionId, ward.Kind, ward.Position, ward.Hp, ward.PlacedTick, ward.ExpiryTick, true));
            }
            return result;
        }

        private List<WardView> VisibleEnemyWardViewsFor(uint faction)
        {
            var result = new List<WardView>();
            for (int i = 0; i < world.WardCount; i++)
            {
                var ward = world.Wards[i];
                if (ward.Alive && ward.FactionId != faction && WardVisibleTo(faction, ward))
                    result.Add(new WardView(ward.Id, ward.FactionId, ward.Kind, ward.Position, ward.Hp, ward.PlacedTick, ward.ExpiryTick,
                        ward.Kind == WardKind.Revealing || IsWardDetectedBy(faction, ward)));
            }
            return result;
        }

        private List<WardView> WardViewsFor(uint faction)
        {
            var result = OwnWardViewsFor(faction);
            result.AddRange(VisibleEnemyWardViewsFor(faction));
            return result;
        }

        private void DecideWards(uint faction)
        {
            if (!WardsOn || world.Economies[faction - 1].Age == 0) return;
            var core = OwnCore(faction).Definition.Position;
            if (CountActiveWards(faction, WardKind.Revealing) < world.Config.Economy.WardRevealingMax
                && TryCentralTower(faction, out SimPoint tower))
                TryAutomaticWard(faction, WardKind.Revealing, tower);

            if (CountActiveWards(faction, WardKind.Hidden) >= world.Config.Economy.WardHiddenMax) return;
            RememberedBuilding bestMemory = default;
            bool hasMemory = false;
            if (BuildingMemoryOn && world.Factions[faction - 1].BuildingMemory != null)
            {
                var memories = RememberedBuildingsFor(faction);
                BigInteger bestDistance = default;
                for (int i = 0; i < memories.Count; i++)
                {
                    BigInteger distance = DistanceSquared(core, memories[i].Position);
                    if (!hasMemory || distance < bestDistance || distance == bestDistance && memories[i].Id < bestMemory.Id)
                    { bestMemory = memories[i]; bestDistance = distance; hasMemory = true; }
                }
            }
            SimPoint target = hasMemory ? FixMath.MoveTowards(bestMemory.Position, core, Fix64.FromInt(25)) : core;
            if (!hasMemory)
            {
                uint enemy = faction == 1 ? 2U : 1U;
                SimPoint enemyCore = OwnCore(enemy).Definition.Position;
                if (!IsVisibleTo(faction, enemyCore))
                {
                    if (TryRememberedDirection(faction, out SimPoint remembered)) enemyCore = remembered;
                    else enemyCore = world.Map.Center((world.Config.Map.HeightCells / 2) * world.Config.Map.WidthCells + world.Config.Map.WidthCells / 2);
                }
                target = FixMath.MoveTowards(core, enemyCore, Fix64.FromInt(60));
            }
            TryAutomaticWard(faction, WardKind.Hidden, target);
        }

        private bool TryCentralTower(uint faction, out SimPoint target)
        {
            target = default; bool found = false; BigInteger best = default; int bestIndex = -1;
            SimPoint mapCenter = world.Map.Center((world.Config.Map.HeightCells / 2) * world.Config.Map.WidthCells + world.Config.Map.WidthCells / 2);
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                var outpost = world.Outposts[i];
                if (outpost.OwnerFactionId != faction) continue;
                BigInteger distance = DistanceSquared(mapCenter, outpost.Definition.Position);
                if (!found || distance < best || distance == best && i < bestIndex)
                { target = outpost.Definition.Position; best = distance; bestIndex = i; found = true; }
            }
            return found;
        }

        private bool TryRememberedDirection(uint faction, out SimPoint target)
        {
            target = default;
            if (!BuildingMemoryOn || world.Factions[faction - 1].BuildingMemory == null) return false;
            var memories = RememberedBuildingsFor(faction);
            if (memories.Count == 0) return false;
            SimPoint core = OwnCore(faction).Definition.Position; BigInteger best = default; uint bestId = 0;
            for (int i = 0; i < memories.Count; i++)
            {
                BigInteger distance = DistanceSquared(core, memories[i].Position);
                if (bestId == 0 || distance < best || distance == best && memories[i].Id < bestId)
                { target = memories[i].Position; best = distance; bestId = memories[i].Id; }
            }
            return bestId != 0;
        }

        private bool TryAutomaticWard(uint faction, WardKind kind, SimPoint target)
        {
            if (HuntWardFixOn && HasPendingWardPlacement(faction, kind)) return false;
            if (!TryWardCell(faction, kind, target, out int cell)) return false;
            for (int n = 0; n < wardScoutCount; n++)
            {
                int i = wardScoutIndices[n]; var scout = world.Soldiers[i];
                if (HuntWardFixOn && AiHuntBeforeCore(faction)) continue;
                if (scout.Alive && scout.Initial.FactionId == faction && !scout.WardPlacePending && scout.WardPlaceRemaining == 0)
                    return QueueAutomaticWardPlacement(faction, kind, scout.Initial.Id, cell, true);
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var villager = world.Villagers[i];
                if (villager.Alive && villager.FactionId == faction && villager.Task == VillagerTask.Idle && !villager.Held
                    && !villager.WardPlacePending && villager.WardPlaceRemaining == 0)
                    return QueueAutomaticWardPlacement(faction, kind, villager.Id, cell, false);
            }
            return false;
        }

        private bool TryWardCell(uint faction, WardKind kind, SimPoint target, out int cell)
        {
            SimPoint core = OwnCore(faction).Definition.Position;
            if (InRange(target, core, Fix64.FromInt(8))) { cell = -1; return false; }
            cell = world.Map.Cell(target);
            if (cell >= 0 && world.Map.IsPassableFor(cell, faction) && !IsWardCellAbandoned(faction, kind, cell)
                && !HasWardAtTarget(faction, kind, world.Map.Center(cell))) return true;
            int origin = cell;
            if (origin < 0) origin = world.Map.Cell(core);
            int ox = origin % world.Config.Map.WidthCells, oz = origin / world.Config.Map.WidthCells;
            for (int radius = 1; radius <= 4; radius++)
                for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dz) != radius) continue;
                    int x = ox + dx, z = oz + dz;
                    if (x < 0 || x >= world.Config.Map.WidthCells || z < 0 || z >= world.Config.Map.HeightCells) continue;
                    int candidate = z * world.Config.Map.WidthCells + x; SimPoint point = world.Map.Center(candidate);
                    if (world.Map.IsPassableFor(candidate, faction) && !IsWardCellAbandoned(faction, kind, candidate)
                        && !InRange(point, core, Fix64.FromInt(8))
                        && !HasWardAtTarget(faction, kind, point)) { cell = candidate; return true; }
                }
            cell = -1; return false;
        }

        private bool HasPendingWardPlacement(uint faction, WardKind kind)
        {
            for (int i = 0; i < world.VillagerCount; i++)
                if (world.Villagers[i].Alive && world.Villagers[i].FactionId == faction && world.Villagers[i].WardPlacePending
                    && world.Villagers[i].WardPlaceKind == kind) return true;
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction && world.Soldiers[i].WardPlacePending
                    && world.Soldiers[i].WardPlaceKind == kind) return true;
            return false;
        }

        private int WardAbandonedCellIndex(uint faction, WardKind kind, int cell)
            => checked(((int)faction - 1) * 2 * world.Config.Map.WidthCells * world.Config.Map.HeightCells
                + ((int)kind - 1) * world.Config.Map.WidthCells * world.Config.Map.HeightCells + cell);

        private bool IsWardCellAbandoned(uint faction, WardKind kind, int cell)
            => HuntWardFixOn && cell >= 0 && cell < world.Config.Map.WidthCells * world.Config.Map.HeightCells
                && world.WardAbandonedCellsUntil[WardAbandonedCellIndex(faction, kind, cell)] > world.Tick;

        private void AbandonWardPlacement(ref VillagerState villager)
        {
            if (!HuntWardFixOn || !villager.WardPlacePending) return;
            int cell = villager.WardPlaceCell;
            world.WardAbandonedCellsUntil[WardAbandonedCellIndex(villager.FactionId, villager.WardPlaceKind, cell)] =
                checked(world.Tick + world.Config.Economy.HuntWardAbandonedCellCooldownTicks);
            world.WardAbandonedCount[villager.FactionId - 1]++;
            ReleaseVillagerRoute(ref villager);
            villager.WardPlacePending = false; villager.WardPlaceKind = 0; villager.WardPlaceCell = 0;
            villager.WardPlaceStartTick = 0; villager.MoveGoal = villager.Position; villager.Task = VillagerTask.Idle;
        }

        private void AbandonWardPlacement(ref SoldierState scout)
        {
            if (!HuntWardFixOn || !scout.WardPlacePending) return;
            int cell = scout.WardPlaceCell;
            world.WardAbandonedCellsUntil[WardAbandonedCellIndex(scout.Initial.FactionId, scout.WardPlaceKind, cell)] =
                checked(world.Tick + world.Config.Economy.HuntWardAbandonedCellCooldownTicks);
            world.WardAbandonedCount[scout.Initial.FactionId - 1]++;
            ReleaseLocalPath(ref scout); scout.TacticalRoute = false;
            scout.WardPlacePending = false; scout.WardPlaceKind = 0; scout.WardPlaceCell = 0;
            scout.WardPlaceStartTick = 0; scout.MoveGoal = scout.Position;
        }

        internal void UpdateHuntWardMetrics()
        {
            if (!HuntWardFixOn) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                int pending = 0;
                for (int i = 0; i < world.VillagerCount; i++)
                    if (world.Villagers[i].Alive && world.Villagers[i].FactionId == faction && world.Villagers[i].WardPlacePending) pending++;
                world.WardPendingVillagerMax[faction - 1] = Math.Max(world.WardPendingVillagerMax[faction - 1], pending);
            }
        }

        private bool HasWardAtTarget(uint faction, WardKind kind, SimPoint point)
        {
            for (int i = 0; i < world.WardCount; i++)
                if (world.Wards[i].Alive && world.Wards[i].FactionId == faction && world.Wards[i].Kind == kind
                    && InRange(world.Wards[i].Position, point, Fix64.FromInt(2))) return true;
            return false;
        }

        private void BuildAutomaticWardTargets()
        {
            if (!WardsOn || automaticWardTargets.Length == 0) return;
            EnsureWardScratchCapacity();
            Array.Clear(automaticWardTargets, 0, automaticWardTargets.Length);
            Fix64 pullRange = Fix64.FromInt(20);
            for (int wi = 0; wi < world.WardCount; wi++)
            {
                var ward = world.Wards[wi];
                if (!ward.Alive) continue;
                uint observer = ward.FactionId == 1 ? 2U : 1U;
                if (!WardVisibleTo(observer, ward)) continue;
                int candidates = QuerySpatial(spatialSoldierHeads[observer - 1], spatialSoldierNext, ward.Position, pullRange, 0, spatialEnemyIndices);
                for (int n = 0; n < candidates; n++)
                {
                    int soldierIndex = spatialEnemyIndices[n];
                    var soldier = world.Soldiers[soldierIndex];
                    if (!soldier.Alive || soldier.Initial.FactionId != observer || soldier.WardPlacePending
                        || soldier.WardPlaceRemaining > 0 || !InRange(soldier.Position, ward.Position, pullRange)) continue;
                    BigInteger distance = DistanceSquared(soldier.Position, ward.Position);
                    if (automaticWardTargets[soldierIndex] == 0 || distance < automaticWardDistances[soldierIndex]
                        || distance == automaticWardDistances[soldierIndex] && ward.Id < automaticWardTargets[soldierIndex])
                    { automaticWardTargets[soldierIndex] = ward.Id; automaticWardDistances[soldierIndex] = distance; }
                }
            }
        }
    }
}
