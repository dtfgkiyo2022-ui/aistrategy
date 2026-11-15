using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private bool WardsOn => EconomyOn && world.Config.Economy.Wards;
        private int wardPlacedCount;
        private readonly int[] wardDestroyedCount = new int[2];
        private readonly int[] wardDestroyedByEnemyCount = new int[2];

        internal int WardPlacedCount(uint faction) => faction >= 1 && faction <= 2 ? wardPlacedCount : 0;
        internal int WardDestroyedCount(uint faction) => faction >= 1 && faction <= 2 ? wardDestroyedCount[faction - 1] : 0;
        internal int WardDestroyedByEnemyCount(uint faction) => faction >= 1 && faction <= 2 ? wardDestroyedByEnemyCount[faction - 1] : 0;

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
                scout.WardPlaceKind = command.Ward; scout.WardPlaceCell = command.Cell;
                scout.WardPlaceRemaining = Math.Max(1, rules.WardPlacementTicks); scout.MoveGoal = scout.Position;
                return true;
            }
            int vindex = checked((int)command.ProducerId - 1);
            if (vindex < 0 || vindex >= world.Villagers.Length) return false;
            ref var villager = ref world.Villagers[vindex];
            if (!villager.Alive || villager.FactionId != command.FactionId || villager.WardPlaceRemaining > 0
                || !InRange(villager.Position, point, Fix64.FromInt(rules.WardPlacementRangeMeters))) return false;
            economy.Wood -= wood; economy.Gold -= gold;
            villager.WardPlaceKind = command.Ward; villager.WardPlaceCell = command.Cell;
            villager.WardPlaceRemaining = Math.Max(1, rules.WardPlacementTicks); villager.MoveGoal = villager.Position;
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
            villager.WardPlaceKind = 0; villager.WardPlaceCell = 0; villager.WardPlaceRemaining = 0;
        }

        private void CompleteWardPlacement(ref SoldierState scout)
        {
            PlaceWard(scout.Initial.FactionId, scout.WardPlaceKind, world.Map.Center(scout.WardPlaceCell));
            scout.WardPlaceKind = 0; scout.WardPlaceCell = 0; scout.WardPlaceRemaining = 0;
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
            wardPlacedCount++;
        }

        private void ExpireWards()
        {
            if (!WardsOn) return;
            for (int i = 0; i < world.WardCount; i++)
                if (world.Wards[i].Alive && world.Wards[i].ExpiryTick > 0 && world.Tick >= world.Wards[i].ExpiryTick) DestroyWard(i, 0);
        }

        private void DestroyWard(int index, uint byFaction)
        {
            if (index < 0 || index >= world.WardCount || !world.Wards[index].Alive) return;
            uint owner = world.Wards[index].FactionId;
            world.Wards[index].Alive = false;
            if (owner >= 1 && owner <= 2)
            {
                wardDestroyedCount[owner - 1]++;
                if (byFaction >= 1 && byFaction <= 2 && byFaction != owner) wardDestroyedByEnemyCount[owner - 1]++;
            }
        }

        private bool IsWardDetectedBy(uint observer, WardState ward)
        {
            if (ward.FactionId == observer) return true;
            Fix64 reveal = Fix64.FromInt(world.Config.Economy.WardRevealMeters);
            for (int i = 0; i < world.WardCount; i++)
            {
                var source = world.Wards[i];
                if (source.Alive && source.FactionId == observer && source.Kind == WardKind.Revealing
                    && InRange(source.Position, ward.Position, reveal)) return true;
            }
            Fix64 scoutReveal = Fix64.FromInt(world.Config.Economy.WardScoutRevealMeters);
            for (int i = 0; i < world.Soldiers.Length; i++)
            {
                var scout = world.Soldiers[i];
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
            if (!WardsOn) return;
            var core = OwnCore(faction).Definition.Position;
            if (CountActiveWards(faction, WardKind.Revealing) < world.Config.Economy.WardRevealingMax)
            {
                SimPoint target = core;
                for (int i = 0; i < world.Outposts.Length; i++)
                    if (world.Outposts[i].OwnerFactionId == faction) { target = world.Outposts[i].Definition.Position; break; }
                TryAutomaticWard(faction, WardKind.Revealing, target, true);
            }
            if (CountActiveWards(faction, WardKind.Hidden) < world.Config.Economy.WardHiddenMax)
            {
                SimPoint target = core;
                bool hasMemory = BuildingMemoryOn && world.Factions[faction - 1].BuildingMemory != null
                    && world.Factions[faction - 1].BuildingMemory.Length >= world.BuildingCount;
                var memories = hasMemory ? RememberedBuildingsFor(faction) : Array.Empty<RememberedBuilding>();
                if (memories.Count > 0) target = memories[0].Position;
                TryAutomaticWard(faction, WardKind.Hidden, target, memories.Count > 0);
            }
        }

        private bool TryAutomaticWard(uint faction, WardKind kind, SimPoint target, bool preferScout)
        {
            int cell = world.Map.Cell(target);
            if (!world.Map.IsPassableFor(cell, faction)) cell = world.Map.Cell(OwnCore(faction).Definition.Position);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < world.Soldiers.Length; i++)
                {
                    ref var scout = ref world.Soldiers[i];
                    if (!scout.Alive || scout.Initial.FactionId != faction || scout.Initial.Kind != UnitKind.Scout || scout.WardPlaceRemaining > 0
                        || !InRange(scout.Position, world.Map.Center(cell), Fix64.FromInt(world.Config.Economy.WardPlacementRangeMeters))) continue;
                    if (preferScout || pass == 1) return StartWardPlacement(EconomyCommand.PlaceWard(faction, 0, kind, scout.Initial.Id, cell, true));
                }
                for (int i = 0; i < world.Villagers.Length; i++)
                {
                    ref var villager = ref world.Villagers[i];
                    if (!villager.Alive || villager.FactionId != faction || villager.WardPlaceRemaining > 0
                        || !InRange(villager.Position, world.Map.Center(cell), Fix64.FromInt(world.Config.Economy.WardPlacementRangeMeters))) continue;
                    return StartWardPlacement(EconomyCommand.PlaceWard(faction, 0, kind, villager.Id, cell, false));
                }
            }
            return false;
        }
    }
}
