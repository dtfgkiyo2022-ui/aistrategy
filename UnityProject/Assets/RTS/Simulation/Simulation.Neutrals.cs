using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        internal const byte TargetNeutral = 7;
        private long[] neutralDamage;
        private long[] neutralKills = Array.Empty<long>();
        private long[] neutralRewards = Array.Empty<long>();
        private long[] neutralBlessed = Array.Empty<long>();
        private long[] neutralHuntOrders = Array.Empty<long>();
        private long[,] neutralHuntFailures = new long[2, 8];
        private long[] greatLordKills = new long[2];
        private long[] siegeBuildingDamage = new long[2];

        private bool NeutralsOn => world.Config.Economy.Neutrals;
        private bool GreatOn => NeutralsOn && world.Config.Economy.GreatNeutrals;

        private void ApplyAttackNeutralCommand(EconomyCommand command)
        {
            if (command.FactionId < 1 || command.FactionId > 2 || FindNeutralCamp(command.TargetId) < 0) return;
            if (command.SoldierIds != null && command.SoldierIds.Count > 0)
            {
                foreach (uint id in command.SoldierIds)
                    if (id > 0 && id <= world.SoldierCount && world.Soldiers[id - 1].Alive && world.Soldiers[id - 1].Initial.FactionId == command.FactionId)
                    { world.NeutralOrders[id - 1] = command.TargetId; world.NeutralOrderCamps[id - 1] = command.TargetId; }
                return;
            }
            if (command.ProducerId == 0 || command.ProducerId > world.Armies.Length) return;
            var army = world.Armies[command.ProducerId - 1];
            if (army.Definition.FactionId != command.FactionId) return;
            foreach (uint id in army.SoldierIds)
                if (world.Soldiers[id - 1].Alive) { world.NeutralOrders[id - 1] = command.TargetId; world.NeutralOrderCamps[id - 1] = command.TargetId; }
        }

        private void InitializeNeutralsRuntime()
        {
            if (!NeutralsOn) return;
            neutralDamage = new long[world.Neutrals.Length];
            neutralKills = new long[checked(world.Factions.Length * 9)];
            neutralRewards = new long[checked(world.Factions.Length * 9)];
            neutralBlessed = new long[world.Factions.Length];
            neutralHuntOrders = new long[world.Factions.Length];
            neutralHuntFailures = new long[world.Factions.Length, 8];
            greatLordKills = new long[world.Factions.Length];
            siegeBuildingDamage = new long[world.Factions.Length];
        }

        private void NeutralThink()
        {
            if (!NeutralsOn) return;
            ExpireNeutralEffects();
            ExpireNeutralShellVision();
            CancelInvalidNeutralHunts();
            if (world.Tick % world.Config.NeutralRules.HuntIntervalTicks != 0) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                foreach (uint armyId in world.Factions[faction - 1].ArmyIds)
                {
                    ref var army = ref world.Armies[armyId - 1];
                    uint activeCamp = ActiveHuntCamp(army);
                    if (activeCamp != 0) continue;
                    NeutralHuntFailureReason failure;
                    uint campId = ChooseHuntCamp(faction, army, out failure);
                    if (campId == 0) continue;
                    foreach (uint soldierId in army.SoldierIds)
                    {
                        int index = checked((int)soldierId) - 1;
                        if (!world.Soldiers[index].Alive || world.NeutralOrderCamps[index] != 0) continue;
                        world.NeutralOrderCamps[index] = 0x80000000U | campId;
                        world.NeutralOrders[index] = campId;
                    }
                    neutralHuntOrders[faction - 1]++;
                    int selectedCampIndex = FindNeutralCamp(campId);
                    if (selectedCampIndex >= 0 && world.NeutralCamps[selectedCampIndex].Kind == NeutralKind.AugmentGuardian)
                        augmentGuardianHuntOrders[faction - 1]++;
                }
            }
        }

        private uint ChooseHuntCamp(uint faction, ArmyState army, out NeutralHuntFailureReason failure)
        {
            failure = NeutralHuntFailureReason.Role;
            if (HuntRoleBlocked(faction, army)) { neutralHuntFailures[faction - 1, (int)NeutralHuntFailureReason.Role - 1]++; return 0; }
            if (EnemyVisibleNearArmy(faction, army)) { neutralHuntFailures[faction - 1, (int)NeutralHuntFailureReason.EnemyNearby - 1]++; return 0; }
            if (!TryArmyCentre(army, out SimPoint centre)) { neutralHuntFailures[faction - 1, (int)NeutralHuntFailureReason.Role - 1]++; return 0; }
            uint selected = 0; int selectedPriority = int.MaxValue; Fix64 selectedDistance = Fix64.FromInt(int.MaxValue);
            bool far = false, weak = false, enemySide = false, age = false, centralBusy = false, centralEnemyNearby = false;
            for (int i = 0; i < world.NeutralCamps.Length; i++)
            {
                var camp = world.NeutralCamps[i];
                int hp = CampAliveHp(camp);
                if (hp <= 0 || CampReservedByOtherArmy(faction, camp.Id, army.Definition.Id)) continue;
                bool central = IsCentralNeutralKind(camp.Kind);
                if (!central && !InRange(centre, camp.Position, Fix64.FromInt(60))) { far = true; continue; }
                if (central && EnemyVisibleNear(faction, camp.Position, 25)) { centralEnemyNearby = true; continue; }
                if (!central && !OwnSideCampIndex(faction, i)) { enemySide = true; continue; }
                if (central && CentralHuntBusy(faction, army.Definition.Id)) { centralBusy = true; continue; }
                if (!NeutralAgeAvailable(faction, camp.Kind)) { age = true; continue; }
                int strength = central ? 1500 : world.Config.NeutralRules.HuntStrengthPermille;
                if (army.SoldierIds.Length == 0 || ArmyHp(army) * 1000L < (long)hp * strength) { weak = true; continue; }
                Fix64 distance = NeutralDistanceScore(centre, camp.Position);
                int priority = HuntPriority(camp.Kind);
                if (priority < selectedPriority || priority == selectedPriority && distance.Raw < selectedDistance.Raw
                    || priority == selectedPriority && distance.Raw == selectedDistance.Raw && camp.Id < selected)
                { selected = camp.Id; selectedPriority = priority; selectedDistance = distance; }
            }
            if (selected != 0) return selected;
            if (far) neutralHuntFailures[faction - 1, (int)NeutralHuntFailureReason.Far - 1]++;
            if (weak) neutralHuntFailures[faction - 1, (int)NeutralHuntFailureReason.Weak - 1]++;
            if (enemySide) neutralHuntFailures[faction - 1, (int)NeutralHuntFailureReason.EnemySide - 1]++;
            if (age) neutralHuntFailures[faction - 1, (int)NeutralHuntFailureReason.Age - 1]++;
            if (centralBusy) neutralHuntFailures[faction - 1, (int)NeutralHuntFailureReason.CentralBusy - 1]++;
            if (centralEnemyNearby) neutralHuntFailures[faction - 1, (int)NeutralHuntFailureReason.CentralEnemyNearby - 1]++;
            if (far) failure = NeutralHuntFailureReason.Far;
            else if (weak) failure = NeutralHuntFailureReason.Weak;
            else if (enemySide) failure = NeutralHuntFailureReason.EnemySide;
            else if (age) failure = NeutralHuntFailureReason.Age;
            else if (centralBusy) failure = NeutralHuntFailureReason.CentralBusy;
            else if (centralEnemyNearby) failure = NeutralHuntFailureReason.CentralEnemyNearby;
            else return 0;
            return selected;
        }

        private static int HuntPriority(NeutralKind kind)
        {
            if (kind == NeutralKind.GreatLord) return -2;
            if (kind == NeutralKind.AugmentGuardian) return -1;
            if (kind == NeutralKind.Dragon) return 0;
            if (kind == NeutralKind.SiegeBeast) return 5;
            if (kind == NeutralKind.FlameHornBeast || kind == NeutralKind.AzureSerpent) return 1;
            if (kind == NeutralKind.ShellBeast) return 2;
            if (kind == NeutralKind.RockBeast) return 3;
            return 4;
        }

        private static bool IsCentralNeutralKind(NeutralKind kind)
            => kind == NeutralKind.AugmentGuardian || kind == NeutralKind.Dragon || kind == NeutralKind.GreatLord;

        private bool CentralHuntBusy(uint faction, uint armyId)
        {
            foreach (uint id in world.Factions[faction - 1].ArmyIds)
                if (id != armyId && ActiveHuntCamp(world.Armies[id - 1]) != 0)
                {
                    int camp = FindNeutralCamp(ActiveHuntCamp(world.Armies[id - 1]));
                    if (camp >= 0 && IsCentralNeutralKind(world.NeutralCamps[camp].Kind)) return true;
                }
            return false;
        }

        private bool OwnSideCampIndex(uint faction, int campIndex)
        {
            return faction >= 1 && faction <= 2 && campIndex >= 0 && campIndex < world.NeutralCamps.Length
                && world.NeutralCampOwnSide[(faction - 1) * world.NeutralCamps.Length + campIndex];
        }

        private bool OwnSideCamp(uint faction, SimPoint camp)
        {
            int campIndex = FindNeutralCampAt(camp);
            return OwnSideCampIndex(faction, campIndex);
        }

        private int FindNeutralCampAt(SimPoint position)
        {
            for (int i = 0; i < world.NeutralCamps.Length; i++)
                if (SamePoint(world.NeutralCamps[i].Position, position)) return i;
            return -1;
        }

        private void CancelInvalidNeutralHunts()
        {
            for (uint faction = 1; faction <= 2; faction++)
                foreach (uint armyId in world.Factions[faction - 1].ArmyIds)
                {
                    ref var army = ref world.Armies[armyId - 1];
                    uint campId = ActiveHuntCamp(army);
                    if (campId == 0) continue;
                    int campIndex = FindNeutralCamp(campId);
                    bool invalid = HuntRoleBlocked(faction, army) || EnemyVisibleNearArmy(faction, army)
                        || campIndex < 0 || CampAliveHp(world.NeutralCamps[campIndex]) <= 0
                        || ArmyHp(army) * 1000L < (long)CampAliveHp(world.NeutralCamps[campIndex]) * world.Config.NeutralRules.HuntStrengthPermille;
                    if (invalid) ClearNeutralOrders(army);
                }
        }

        private uint ActiveHuntCamp(ArmyState army)
        {
            foreach (uint id in army.SoldierIds)
            {
                if (id == 0 || id > world.SoldierCount) continue;
                uint marker = world.NeutralOrderCamps[id - 1];
                if (marker >= 0x80000000U) return marker & 0x7fffffffU;
            }
            return 0;
        }

        private void ClearNeutralOrders(ArmyState army)
        {
            foreach (uint id in army.SoldierIds)
                if (id > 0 && id <= world.SoldierCount && world.NeutralOrderCamps[id - 1] >= 0x80000000U)
                    ClearNeutralOrder(checked((int)id - 1));
        }

        private void ClearNeutralOrder(int soldierIndex)
        {
            world.NeutralOrders[soldierIndex] = 0;
            world.NeutralOrderCamps[soldierIndex] = 0;
            ref var soldier = ref world.Soldiers[soldierIndex];
            if (soldier.TargetKind == TargetNeutral)
            {
                soldier.TargetKind = 0;
                soldier.TargetId = 0;
                soldier.MoveGoal = soldier.Position;
            }
        }

        private bool EnemyVisibleNear(uint faction, uint campId, int radius)
        {
            int campIndex = FindNeutralCamp(campId);
            return campIndex >= 0 && EnemyVisibleNear(faction, world.NeutralCamps[campIndex].Position, radius);
        }

        private int ArmyHp(ArmyState army)
        {
            int hp = 0;
            foreach (uint id in army.SoldierIds) if (world.Soldiers[id - 1].Alive) hp = checked(hp + Math.Max(0, world.Soldiers[id - 1].Hp));
            return hp;
        }

        private int CampAliveHp(NeutralCampState camp)
        {
            int hp = 0;
            foreach (uint id in camp.NeutralIds) if (world.Neutrals[id - 1].Alive) hp = checked(hp + world.Neutrals[id - 1].Hp);
            return hp;
        }

        private bool EnemyVisibleNear(uint faction, SimPoint point, int radius)
        {
            var observation = frames[faction - 1]?.Observation;
            if (observation == null) return false;
            foreach (var enemy in observation.VisibleEnemies)
                if (InRange(enemy.Position, point, Fix64.FromInt(radius))) return true;
            return false;
        }

        private void AssignNeutralIntents()
        {
            if (!NeutralsOn) return;
            for (int i = 0; i < world.SoldierTraversal.Length; i++)
            {
                int index = world.SoldierTraversal[i];
                ref var soldier = ref world.Soldiers[index];
                if (!soldier.Alive || world.NeutralOrders[index] == 0) continue;
                if (soldier.TargetKind == 1 || soldier.TargetKind == 2 || soldier.TargetKind == TargetVillager
                    || soldier.TargetKind == TargetBuilding || soldier.TargetKind == TargetBelt || soldier.TargetKind == TargetWard) continue;
                uint campId = world.NeutralOrders[index];
                int campIndex = FindNeutralCamp(campId);
                if (campIndex < 0) { world.NeutralOrders[index] = 0; world.NeutralOrderCamps[index] = 0; continue; }
                uint aliveNeutral = FirstAliveNeutral(world.NeutralCamps[campIndex]);
                // 11-18 testplay: a cleared camp ends the order. A human order has no hunt marker, so nothing else
                // cleared it and the soldier stayed pinned to the empty camp (it could not move after killing the wolf).
                if (aliveNeutral == 0) { world.NeutralOrders[index] = 0; world.NeutralOrderCamps[index] = 0; continue; }
                soldier.TargetKind = TargetNeutral;
                soldier.TargetId = aliveNeutral;
                soldier.MoveGoal = world.Neutrals[aliveNeutral - 1].Position;
            }
        }

        private uint FirstAliveNeutral(NeutralCampState camp)
        {
            foreach (uint id in camp.NeutralIds) if (world.Neutrals[id - 1].Alive) return id;
            return 0;
        }

        private int FindNeutralCamp(uint campId)
        {
            for (int i = 0; i < world.NeutralCamps.Length; i++) if (world.NeutralCamps[i].Id == campId) return i;
            return -1;
        }

        private bool CampReservedByOtherArmy(uint faction, uint campId, uint armyId)
        {
            for (int i = 0; i < world.NeutralOrderCamps.Length; i++)
            {
                uint marker = world.NeutralOrderCamps[i];
                if (marker < 0x80000000U || (marker & 0x7fffffffU) != campId) continue;
                var soldier = world.Soldiers[i];
                if (soldier.Alive && soldier.Initial.FactionId == faction && soldier.Initial.ArmyId != armyId) return true;
            }
            return false;
        }

        private bool HuntRoleBlocked(uint faction, ArmyState army)
        {
            if (army.Decision.Returning || army.Decision.Assignment == AssignmentKind.CoreDefense) return true;
            bool hasLive = false, allScoutOrVillager = true;
            foreach (uint id in army.SoldierIds)
            {
                if (id == 0 || id > world.SoldierCount || !world.Soldiers[id - 1].Alive) continue;
                hasLive = true;
                UnitKind kind = world.Soldiers[id - 1].Initial.Kind;
                if (kind != UnitKind.Scout && kind != UnitKind.Villager) allScoutOrVillager = false;
            }
            if (!hasLive || allScoutOrVillager || army.Policy == PolicyKind.Scout || army.Policy == PolicyKind.Retreat) return true;
            if (IsEnemyGoal(faction, army.Decision.Goal) || IsEnemyGoal(faction, army.Goal)) return true;
            if (army.Policy == PolicyKind.Focus && army.Definition.Id <= buildingAttackTargets.Length)
            {
                uint buildingId = buildingAttackTargets[army.Definition.Id - 1];
                if (buildingId != 0 && buildingId <= world.BuildingCount && world.Buildings[buildingId - 1].FactionId != faction) return true;
            }
            return false;
        }

        private bool IsEnemyGoal(uint faction, PolicyGoal goal)
        {
            if (goal.Kind == GoalKind.Core) return goal.Id > 0 && goal.Id <= world.Cores.Length
                && world.Cores[goal.Id - 1].Definition.FactionId != faction;
            if (goal.Kind == GoalKind.Outpost) return goal.Id > 0 && goal.Id <= world.Outposts.Length
                && world.Outposts[goal.Id - 1].OwnerFactionId != 0 && world.Outposts[goal.Id - 1].OwnerFactionId != faction;
            return false;
        }

        private bool TryArmyCentre(ArmyState army, out SimPoint centre)
        {
            long sumX = 0, sumZ = 0; int count = 0;
            foreach (uint id in army.SoldierIds)
            {
                if (id == 0 || id > world.SoldierCount || !world.Soldiers[id - 1].Alive) continue;
                sumX = checked(sumX + world.Soldiers[id - 1].Position.X.Raw);
                sumZ = checked(sumZ + world.Soldiers[id - 1].Position.Z.Raw);
                count++;
            }
            if (count == 0) { centre = default; return false; }
            centre = new SimPoint(Fix64.FromRaw(sumX / count), Fix64.FromRaw(sumZ / count));
            return true;
        }

        private bool EnemyVisibleNearArmy(uint faction, ArmyState army)
        {
            return TryArmyCentre(army, out SimPoint centre) && EnemyVisibleNear(faction, centre, 30);
        }

        private bool NeutralAgeAvailable(uint faction, NeutralKind kind)
        {
            if (!AgesOn) return true;
            if (kind == NeutralKind.WolfPack || kind == NeutralKind.RockBeast) return true;
            if (kind == NeutralKind.Dragon || kind == NeutralKind.SiegeBeast) return world.Economies[faction - 1].Age >= 1;
            if (kind == NeutralKind.GreatLord) return world.Economies[faction - 1].Age >= 2
                || world.Tick >= 9000 && world.Economies[0].Age < 2 && world.Economies[1].Age < 2;
            return kind == NeutralKind.AugmentGuardian ? world.Economies[faction - 1].Age >= 2 : world.Economies[faction - 1].Age >= 1;
        }

        private void NeutralSpawnStep()
        {
            if (!NeutralsOn) return;
            for (int i = 0; i < world.NeutralCamps.Length; i++)
            {
                ref var camp = ref world.NeutralCamps[i];
                if (world.Tick < camp.NextSpawnTick || CampAliveHp(camp) > 0) continue;
                for (int j = 0; j < camp.NeutralIds.Length; j++)
                {
                    ref var neutral = ref world.Neutrals[camp.NeutralIds[j] - 1];
                    neutral.Alive = true; neutral.Hp = neutral.MaxHp; neutral.Position = neutral.Home;
                    neutral.LastHitFaction = 0; neutral.LastHitSoldierId = 0; neutral.PreviousHitFaction = 0; neutral.PreviousHitSoldierId = 0;
                    neutral.LastCombatTick = -1; neutral.PreviousHitTick = -1; neutral.Returning = false;
                    neutral.BurnUntil = 0; neutral.SlowUntil = 0;
                    neutral.OwnerFactionId = 0; neutral.LifeUntilTick = 0; neutral.SiegeTargetBuildingId = 0;
                    neutral.NextSecondarySkillTick = neutral.Kind == NeutralKind.GreatLord ? checked(world.Tick + 240) : long.MaxValue;
                    if (neutral.Kind == NeutralKind.Dragon)
                    {
                        camp.DragonSpawnCount = checked(camp.DragonSpawnCount + 1);
                        neutral.DragonElement = NeutralCatalog.DragonElementFor(world.Config.Seed, camp.DragonSpawnCount);
                    }
                    neutral.NextSkillTick = neutral.Kind == NeutralKind.ShellBeast ? long.MaxValue : checked(world.Tick + (neutral.Kind == NeutralKind.GreatLord ? 300 : NeutralCatalog.SkillInterval(neutral.Kind)));
                }
                camp.HasSpawned = true;
                if (camp.Kind == NeutralKind.AugmentGuardian) augmentGuardianSpawns++;
            }
        }

        private void NeutralMoveStep()
        {
            if (!NeutralsOn) return;
            for (int i = 0; i < world.Neutrals.Length; i++)
            {
                ref var neutral = ref world.Neutrals[i];
                if (!neutral.Alive) continue;
                if (neutral.OwnerFactionId != 0 && neutral.Kind == NeutralKind.SiegeBeast)
                {
                    if (world.Tick >= neutral.LifeUntilTick)
                    {
                        neutral.Alive = false; neutral.OwnerFactionId = 0; neutral.SiegeTargetBuildingId = 0;
                        int expiredCamp = FindNeutralCamp(neutral.CampId);
                        if (expiredCamp >= 0) world.NeutralCamps[expiredCamp].NextSpawnTick = checked(world.Tick + NeutralCatalog.RespawnDelay(world.Config.NeutralRules, neutral.Kind));
                        continue;
                    }
                    if (neutral.SiegeTargetBuildingId == 0 || !SiegeTargetValid(neutral)) neutral.SiegeTargetBuildingId = SelectSiegeTarget(neutral.OwnerFactionId, neutral.Position);
                    if (neutral.SiegeTargetBuildingId != 0)
                    {
                        var building = world.Buildings[neutral.SiegeTargetBuildingId - 1];
                        SimPoint siegeGoal = NearestFootprintPoint(building, neutral.Position);
                        if (!SamePoint(neutral.Position, siegeGoal)) neutral.Position = FixMath.MoveTowards(neutral.Position, siegeGoal, Fix64.FromRatio(3, 2));
                    }
                    continue;
                }
                bool targetAlive = NeutralTargetAlive(neutral);
                if (!targetAlive || world.Tick - neutral.LastCombatTick > world.Config.NeutralRules.LostEnemyTicks
                    || !InRange(neutral.Home, targetAlive ? world.Soldiers[neutral.LastHitSoldierId - 1].Position : neutral.Position,
                        Fix64.FromInt(world.Config.NeutralRules.LeashMeters)))
                {
                    if (!SelectRecentNeutralAttacker(ref neutral))
                    { neutral.Returning = true; neutral.LastHitSoldierId = 0; neutral.LastHitFaction = 0; }
                    else targetAlive = true;
                }
                SimPoint goal = neutral.Returning ? neutral.Home : world.Soldiers[neutral.LastHitSoldierId - 1].Position;
                Fix64 neutralStep = Fix64.FromRatio(3, 2);
                if (neutral.SlowUntil > world.Tick) neutralStep = Fix64.FromRaw(neutralStep.Raw * Math.Max(0, 1000 - neutral.SlowPermille) / 1000);
                if (!SamePoint(neutral.Position, goal)) neutral.Position = FixMath.MoveTowards(neutral.Position, goal, neutralStep);
                if (neutral.Returning && InRange(neutral.Position, neutral.Home, Fix64.FromInt(1))) neutral.LastCombatTick = -1;
                if (neutral.LastCombatTick < 0 || world.Tick - neutral.LastCombatTick >= world.Config.NeutralRules.HealDelayTicks)
                    neutral.Hp = Math.Min(neutral.MaxHp, checked(neutral.Hp + Math.Max(1, neutral.MaxHp * world.Config.NeutralRules.HealPermillePerSecond / 1000 / 20)));
            }
        }

        private bool NeutralTargetAlive(NeutralState neutral)
            => neutral.LastHitSoldierId > 0 && neutral.LastHitSoldierId <= world.SoldierCount
            && world.Soldiers[neutral.LastHitSoldierId - 1].Alive;

        private bool SiegeTargetValid(NeutralState neutral)
        {
            if (neutral.SiegeTargetBuildingId == 0 || neutral.SiegeTargetBuildingId > world.BuildingCount) return false;
            var b = world.Buildings[neutral.SiegeTargetBuildingId - 1];
            return IsActualBuilding(b) && b.FactionId != neutral.OwnerFactionId && SiegeBuildingKnown(neutral.OwnerFactionId, b);
        }

        private bool SiegeBuildingKnown(uint faction, BuildingState building)
        {
            if (faction < 1 || faction > world.Factions.Length || !IsActualBuilding(building) || building.FactionId == faction) return false;
            if (BuildingVisibleTo(faction, building)) return true;
            var memory = world.Factions[faction - 1].BuildingMemory;
            if (memory == null) return false;
            for (int i = 0; i < memory.Length; i++)
                if (memory[i].Seen && memory[i].BuildingId == building.Id) return true;
            return false;
        }

        private uint SelectSiegeTarget(uint ownerFaction, SimPoint from)
        {
            uint best = 0; int bestClass = int.MaxValue; Fix64 bestDistance = Fix64.FromInt(int.MaxValue);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId == ownerFaction || !SiegeBuildingKnown(ownerFaction, b)) continue;
                int priority = b.Kind == BuildingKind.Tower || b.Kind == BuildingKind.Wall ? 0 : 1;
                Fix64 distance = NeutralDistanceScore(from, BuildingCenter(b));
                if (priority < bestClass || priority == bestClass && (distance.Raw < bestDistance.Raw || distance.Raw == bestDistance.Raw && b.Id < best))
                { best = b.Id; bestClass = priority; bestDistance = distance; }
            }
            return best;
        }

        private bool SelectRecentNeutralAttacker(ref NeutralState neutral)
        {
            uint bestId = 0, bestFaction = 0; long bestTick = world.Tick - world.Config.NeutralRules.LostEnemyTicks;
            if (neutral.LastHitSoldierId > 0 && neutral.LastHitFaction >= 1 && neutral.LastHitFaction <= 2
                && NeutralAttackerEligible(neutral.LastHitFaction, neutral.LastHitSoldierId, neutral.Home)
                && neutral.LastHitTick >= bestTick)
            { bestId = neutral.LastHitSoldierId; bestFaction = neutral.LastHitFaction; bestTick = neutral.LastHitTick; }
            if (neutral.PreviousHitSoldierId > 0 && neutral.PreviousHitFaction >= 1 && neutral.PreviousHitFaction <= 2
                && NeutralAttackerEligible(neutral.PreviousHitFaction, neutral.PreviousHitSoldierId, neutral.Home)
                && neutral.PreviousHitTick > bestTick)
            { bestId = neutral.PreviousHitSoldierId; bestFaction = neutral.PreviousHitFaction; bestTick = neutral.PreviousHitTick; }
            if (bestId == 0) return false;
            neutral.LastHitSoldierId = bestId; neutral.LastHitFaction = bestFaction; neutral.LastHitTick = bestTick; neutral.Returning = false;
            return true;
        }

        private bool NeutralAttackerEligible(uint faction, uint soldierId, SimPoint home)
        {
            if (soldierId > world.SoldierCount) return false;
            var soldier = world.Soldiers[soldierId - 1];
            return soldier.Alive && soldier.Initial.FactionId == faction && InRange(home, soldier.Position, Fix64.FromInt(world.Config.NeutralRules.LeashMeters));
        }

        private void RecordNeutralHit(ref NeutralState neutral, uint faction, uint soldierId)
        {
            if (neutral.LastHitSoldierId != soldierId || neutral.LastHitFaction != faction)
            { neutral.PreviousHitSoldierId = neutral.LastHitSoldierId; neutral.PreviousHitFaction = neutral.LastHitFaction; neutral.PreviousHitTick = neutral.LastHitTick; }
            neutral.LastHitFaction = faction; neutral.LastHitSoldierId = soldierId; neutral.LastHitTick = world.Tick; neutral.LastCombatTick = world.Tick; neutral.Returning = false;
        }

        private void AttackNeutralFromSoldier(int index)
        {
            ref var soldier = ref world.Soldiers[index];
            if (soldier.TargetId == 0 || soldier.TargetId > world.Neutrals.Length) return;
            ref var neutral = ref world.Neutrals[soldier.TargetId - 1];
            if (!neutral.Alive || !InRange(soldier.Position, neutral.Position, soldier.Parameters.Range)) return;
            if (neutral.OwnerFactionId != 0 && neutral.OwnerFactionId != soldier.Initial.FactionId)
            {
                int enemyDamage = CombatStatsOn
                    ? Rts.Decision.CombatMath.DamageAfterDefense(soldier.Parameters.Damage, neutral.Armor,
                        soldier.Parameters.PhysicalPenetrationPermille, soldier.Parameters.PhysicalFlatPenetration)
                    : soldier.Parameters.Damage;
                neutralDamage[neutral.Id - 1] = checked(neutralDamage[neutral.Id - 1] + Math.Max(1, enemyDamage));
                soldier.NextAttackTick = checked(world.Tick + soldier.Parameters.AttackIntervalTicks);
                soldier.IsAttacking = true;
                return;
            }
            int damage = CombatStatsOn
                ? Rts.Decision.CombatMath.DamageAfterDefense(soldier.Parameters.Damage, neutral.Armor, soldier.Parameters.PhysicalPenetrationPermille, soldier.Parameters.PhysicalFlatPenetration)
                : soldier.Parameters.Damage;
            damage = HunterNeutralDamage(soldier.Initial.FactionId, damage);
            neutralDamage[neutral.Id - 1] = checked(neutralDamage[neutral.Id - 1] + Math.Max(1, damage));
            RecordNeutralHit(ref neutral, soldier.Initial.FactionId, soldier.Initial.Id);
            ApplyFlameBlessingToNeutral(ref neutral, soldier.Initial.FactionId, soldier.Initial.Id);
            soldier.NextAttackTick = checked(world.Tick + soldier.Parameters.AttackIntervalTicks);
            soldier.IsAttacking = true;
        }

        private bool ApplySkillEffectToNeutral(int caster, SkillDefinition skill, SkillEffect effect)
        {
            if (!NeutralsOn || effect == null || effect.Kind != SkillEffectKind.Damage) return false;
            var soldier = world.Soldiers[caster];
            if (soldier.TargetKind != TargetNeutral || soldier.TargetId == 0 || soldier.TargetId > world.Neutrals.Length)
                return false;
            int neutralIndex = checked((int)soldier.TargetId - 1);
            ref var neutral = ref world.Neutrals[neutralIndex];
            if (!neutral.Alive || !InRange(soldier.Position, neutral.Position, skill.Range)) return false;
            int raw = checked(effect.Base + neutral.MaxHp * effect.TargetMaxHpPermille / 1000
                + (neutral.MaxHp - neutral.Hp) * effect.TargetMissingHpPermille / 1000
                + Math.Max(0, soldier.Parameters.Damage) * effect.AdPermille / 1000
                + Math.Max(0, soldier.Parameters.AbilityPower) * effect.ApPermille / 1000);
            raw = checked(raw * world.Config.Economy.SkillDamagePermille / 1000);
            int defense = effect.DamageType == SkillDamageType.Magic ? neutral.MagicResist : neutral.Armor;
            int damage = effect.DamageType == SkillDamageType.True ? Rts.Decision.CombatMath.TrueDamage(raw)
                : Rts.Decision.CombatMath.DamageAfterDefense(raw, Math.Max(0, defense), 0, 0);
            if (effect.DamageType == SkillDamageType.Physical) damage = Math.Max(1, damage);
            damage = HunterNeutralDamage(soldier.Initial.FactionId, damage);
            neutralDamage[neutralIndex] = checked(neutralDamage[neutralIndex] + Math.Max(0, damage));
            RecordNeutralHit(ref neutral, soldier.Initial.FactionId, soldier.Initial.Id);
            ApplyFlameBlessingToNeutral(ref neutral, soldier.Initial.FactionId, soldier.Initial.Id);
            return true;
        }

        private void NeutralAttackStep()
        {
            if (!NeutralsOn) return;
            NeutralEffectStep();
            for (int i = 0; i < world.Neutrals.Length; i++)
            {
                ref var neutral = ref world.Neutrals[i];
                if (!neutral.Alive) continue;
                if (neutral.OwnerFactionId != 0 && neutral.Kind == NeutralKind.SiegeBeast)
                {
                    if (neutral.SiegeTargetBuildingId != 0 && SiegeTargetValid(neutral)
                        && InRange(neutral.Position, NearestFootprintPoint(world.Buildings[neutral.SiegeTargetBuildingId - 1], neutral.Position), Fix64.FromInt(2)))
                    {
                        if (world.Tick >= neutral.NextAttackTick)
                        {
                            buildingDamage[neutral.SiegeTargetBuildingId - 1] = checked(buildingDamage[neutral.SiegeTargetBuildingId - 1] + 120);
                            siegeBuildingDamage[neutral.OwnerFactionId - 1] = checked(siegeBuildingDamage[neutral.OwnerFactionId - 1] + 120);
                            neutral.NextAttackTick = checked(world.Tick + 40);
                        }
                    }
                    continue;
                }
                NeutralAbilityStep(ref neutral);
                if (neutral.AttackDamage <= 0 || neutral.LastHitSoldierId == 0 || world.Tick < neutral.NextAttackTick) continue;
                int target = checked((int)neutral.LastHitSoldierId) - 1;
                if (target < 0 || target >= world.Soldiers.Length || !world.Soldiers[target].Alive || !InRange(neutral.Position, world.Soldiers[target].Position, neutral.Range)) continue;
                int damage = CombatStatsOn ? Rts.Decision.CombatMath.DamageAfterDefense(neutral.AttackDamage, world.Soldiers[target].Parameters.Armor, 0, 0) : neutral.AttackDamage;
                soldierDamage[target] = checked(soldierDamage[target] + Math.Max(1, damage));
                world.Soldiers[target].LastCombatTick = world.Tick;
                if (world.Soldiers[target].TargetKind != 1)
                { world.Soldiers[target].TargetKind = TargetNeutral; world.Soldiers[target].TargetId = neutral.Id; world.Soldiers[target].MoveGoal = neutral.Position; }
                neutral.NextAttackTick = checked(world.Tick + neutral.AttackIntervalTicks);
            }
            for (int i = 0; i < world.Neutrals.Length; i++)
            {
                ref var neutral = ref world.Neutrals[i];
                if (!neutral.Alive) continue;
                neutral.Hp = RemainingHp(neutral.Hp, neutralDamage[i]);
                if (neutral.Hp > 0) continue;
                neutral.Alive = false;
                int campIndex = FindNeutralCamp(neutral.CampId);
                if (campIndex >= 0 && CampAliveHp(world.NeutralCamps[campIndex]) == 0) ResolveNeutralCampReward(campIndex, neutral.LastHitFaction, neutral.Position);
            }
        }

        private void NeutralEffectStep()
        {
            for (int i = 0; i < world.Soldiers.Length; i++)
            {
                ref var soldier = ref world.Soldiers[i];
                if (!soldier.Alive || soldier.NeutralBurnDamage <= 0 || world.Tick >= soldier.NeutralBurnUntil || world.Tick < soldier.NeutralBurnNextTick) continue;
                int damage = CombatStatsOn ? Rts.Decision.CombatMath.DamageAfterDefense(soldier.NeutralBurnDamage, soldier.Parameters.MagicResist, 0, 0) : soldier.NeutralBurnDamage;
                soldierDamage[i] = checked(soldierDamage[i] + Math.Max(1, damage));
                soldier.NeutralBurnNextTick = checked(world.Tick + 20);
                soldier.LastCombatTick = world.Tick;
            }
            for (int i = 0; i < world.Neutrals.Length; i++)
            {
                ref var neutral = ref world.Neutrals[i];
                if (!neutral.Alive || neutral.BurnDamage <= 0 || world.Tick >= neutral.BurnUntil || world.Tick < neutral.BurnNextTick) continue;
                int damage = CombatStatsOn ? Rts.Decision.CombatMath.DamageAfterDefense(neutral.BurnDamage, neutral.MagicResist, 0, 0) : neutral.BurnDamage;
                neutralDamage[i] = checked(neutralDamage[i] + Math.Max(1, damage));
                neutral.BurnNextTick = checked(world.Tick + 20);
            }
        }

        private void NeutralAbilityStep(ref NeutralState neutral)
        {
            if (neutral.Kind == NeutralKind.GreatLord && NeutralTargetAlive(neutral))
            {
                if (world.Tick >= neutral.NextSkillTick)
                {
                    for (int i = 0; i < world.Soldiers.Length; i++)
                        if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == neutral.LastHitFaction
                            && InRange(neutral.Position, world.Soldiers[i].Position, Fix64.FromInt(5)))
                        { ApplyNeutralDamageToSoldier(i, 80, SkillDamageType.Physical); world.Soldiers[i].NeutralStunUntil = checked(world.Tick + 30); }
                    neutral.NextSkillTick = checked(world.Tick + 300);
                }
                if (world.Tick >= neutral.NextSecondarySkillTick)
                {
                    int skillTarget = checked((int)neutral.LastHitSoldierId) - 1;
                    if (skillTarget >= 0 && skillTarget < world.Soldiers.Length && world.Soldiers[skillTarget].Alive
                        && InRange(neutral.Position, world.Soldiers[skillTarget].Position, Fix64.FromInt(6)))
                    { ApplyNeutralDamageToSoldier(skillTarget, 70, SkillDamageType.Magic); world.Soldiers[skillTarget].NeutralSlowUntil = checked(world.Tick + 60); world.Soldiers[skillTarget].NeutralSlowPermille = 300; }
                    neutral.NextSecondarySkillTick = checked(world.Tick + 240);
                }
                return;
            }
            if (neutral.Kind == NeutralKind.Dragon && NeutralTargetAlive(neutral) && world.Tick >= neutral.NextSkillTick)
            {
                if (neutral.DragonElement == DragonElement.Water)
                {
                    int skillTarget = checked((int)neutral.LastHitSoldierId) - 1;
                    if (skillTarget >= 0 && skillTarget < world.Soldiers.Length && world.Soldiers[skillTarget].Alive
                        && InRange(neutral.Position, world.Soldiers[skillTarget].Position, Fix64.FromInt(10)))
                    { ApplyNeutralDamageToSoldier(skillTarget, 80, SkillDamageType.Magic); world.Soldiers[skillTarget].NeutralSlowUntil = checked(world.Tick + 60); world.Soldiers[skillTarget].NeutralSlowPermille = 300; }
                }
                else
                {
                    int radius = neutral.DragonElement == DragonElement.Fire ? 5 : 4;
                    int damage = neutral.DragonElement == DragonElement.Earth ? 50 : 40;
                    for (int i = 0; i < world.Soldiers.Length; i++)
                        if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == neutral.LastHitFaction && InRange(neutral.Position, world.Soldiers[i].Position, Fix64.FromInt(radius)))
                        { ApplyNeutralDamageToSoldier(i, damage, neutral.DragonElement == DragonElement.Fire ? SkillDamageType.Magic : SkillDamageType.Physical); if (neutral.DragonElement == DragonElement.Fire) ApplyBurnToSoldier(ref world.Soldiers[i], 4, 60); else if (neutral.DragonElement == DragonElement.Earth) world.Soldiers[i].NeutralStunUntil = checked(world.Tick + 20); }
                }
                neutral.NextSkillTick = checked(world.Tick + 240);
                return;
            }
            if (neutral.Kind == NeutralKind.SiegeBeast && NeutralTargetAlive(neutral) && world.Tick >= neutral.NextSkillTick)
            {
                int skillTarget = checked((int)neutral.LastHitSoldierId) - 1;
                if (skillTarget >= 0 && skillTarget < world.Soldiers.Length && world.Soldiers[skillTarget].Alive
                    && InRange(neutral.Position, world.Soldiers[skillTarget].Position, Fix64.FromInt(3)))
                    ApplyNeutralDamageToSoldier(skillTarget, 40, SkillDamageType.Physical);
                neutral.NextSkillTick = checked(world.Tick + 200);
                return;
            }
            if (neutral.Kind == NeutralKind.AugmentGuardian && neutral.NextSkillTick != int.MaxValue && world.Tick >= neutral.NextSkillTick
                && NeutralTargetAlive(neutral))
            {
                uint owner = neutral.LastHitFaction;
                for (int i = 0; i < world.Soldiers.Length; i++)
                    if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == owner
                        && InRange(neutral.Position, world.Soldiers[i].Position, Fix64.FromInt(4)))
                        ApplyNeutralDamageToSoldier(i, 60, SkillDamageType.Physical);
                neutral.NextSkillTick = checked(world.Tick + NeutralCatalog.SkillInterval(neutral.Kind));
                return;
            }
            if (neutral.Kind == NeutralKind.WolfPack || neutral.NextSkillTick == int.MaxValue || world.Tick < neutral.NextSkillTick
                || !NeutralTargetAlive(neutral)) return;
            int target = checked((int)neutral.LastHitSoldierId) - 1;
            if (!InRange(neutral.Position, world.Soldiers[target].Position, Fix64.FromInt(neutral.Kind == NeutralKind.FlameHornBeast ? 4 : neutral.Kind == NeutralKind.AzureSerpent ? 8 : 3))) return;
            if (neutral.Kind == NeutralKind.RockBeast)
            {
                ApplyNeutralDamageToSoldier(target, 30, SkillDamageType.Physical);
                world.Soldiers[target].NeutralSlowUntil = checked(world.Tick + 40); world.Soldiers[target].NeutralSlowPermille = 300;
            }
            else if (neutral.Kind == NeutralKind.FlameHornBeast)
            {
                ApplyNeutralDamageToSoldier(target, 50, SkillDamageType.Magic);
                ApplyBurnToSoldier(ref world.Soldiers[target], 4, 60);
            }
            else
                ApplyNeutralDamageToSoldier(target, 60, SkillDamageType.Magic);
            neutral.NextSkillTick = checked(world.Tick + NeutralCatalog.SkillInterval(neutral.Kind));
        }

        private void ApplyNeutralDamageToSoldier(int target, int raw, SkillDamageType type)
        {
            int defense = type == SkillDamageType.Magic
                ? GreatLordDefense(world.Soldiers[target].Initial.FactionId, world.Soldiers[target].Parameters.MagicResist)
                : GreatLordDefense(world.Soldiers[target].Initial.FactionId, world.Soldiers[target].Parameters.Armor);
            int damage = CombatStatsOn ? Rts.Decision.CombatMath.DamageAfterDefense(raw, defense, 0, 0) : raw;
            soldierDamage[target] = checked(soldierDamage[target] + Math.Max(1, damage));
            world.Soldiers[target].LastCombatTick = world.Tick;
        }

        private void ApplyFlameBlessingToNeutral(ref NeutralState neutral, uint faction, uint soldierId)
        {
            int index = checked((int)soldierId) - 1;
            if (index < 0 || index >= world.NeutralSoldierBlessings.Length || world.NeutralSoldierBlessingUntil[index] <= world.Tick
                || (world.NeutralSoldierBlessings[index] & NeutralBlessing.Flame) == 0) return;
            neutral.BurnDamage = 4; neutral.BurnUntil = Math.Max(neutral.BurnUntil, checked(world.Tick + 60));
            neutral.BurnNextTick = Math.Max(neutral.BurnNextTick, checked(world.Tick + 20));
            neutral.SlowUntil = Math.Max(neutral.SlowUntil, checked(world.Tick + 20)); neutral.SlowPermille = 200;
        }

        private void ApplyFlameBlessingToSoldier(int attackerIndex, int targetIndex)
        {
            if (attackerIndex < 0 || targetIndex < 0 || attackerIndex >= world.Soldiers.Length || targetIndex >= world.Soldiers.Length) return;
            if (world.NeutralSoldierBlessingUntil[attackerIndex] <= world.Tick
                || (world.NeutralSoldierBlessings[attackerIndex] & NeutralBlessing.Flame) == 0) return;
            ApplyBurnToSoldier(ref world.Soldiers[targetIndex], 4, 60);
            world.Soldiers[targetIndex].NeutralSlowUntil = Math.Max(world.Soldiers[targetIndex].NeutralSlowUntil, checked(world.Tick + 20));
            world.Soldiers[targetIndex].NeutralSlowPermille = 200;
        }

        private bool NeutralAzureSkillSpeed(int index)
            => NeutralsOn && index >= 0 && index < world.NeutralSoldierBlessings.Length
            && world.NeutralSoldierBlessingUntil[index] > world.Tick
            && (world.NeutralSoldierBlessings[index] & NeutralBlessing.Azure) != 0;

        private void ApplyBurnToSoldier(ref SoldierState target, int damage, int duration)
        {
            target.NeutralBurnDamage = damage;
            target.NeutralBurnUntil = Math.Max(target.NeutralBurnUntil, checked(world.Tick + duration));
            target.NeutralBurnNextTick = Math.Max(target.NeutralBurnNextTick, checked(world.Tick + 20));
        }

        private void ExpireNeutralEffects()
        {
            for (int i = 0; i < world.Soldiers.Length; i++)
            {
                if (!world.Soldiers[i].Alive || world.NeutralSoldierBlessingUntil[i] <= world.Tick)
                { world.NeutralSoldierBlessings[i] = NeutralBlessing.None; world.NeutralSoldierBlessingUntil[i] = 0; }
                if (world.Soldiers[i].NeutralBurnUntil <= world.Tick) world.Soldiers[i].NeutralBurnDamage = 0;
                if (world.Soldiers[i].NeutralSlowUntil <= world.Tick) world.Soldiers[i].NeutralSlowPermille = 0;
            }
            for (int i = 0; i < world.NeutralFactionBlessings.Length; i++)
                if (world.NeutralFactionBlessingUntil[i] <= world.Tick)
                { world.NeutralFactionBlessings[i] = NeutralBlessing.None; world.NeutralFactionBlessingUntil[i] = 0; }
        }

        private void ResolveNeutralCampReward(int campIndex, uint faction, SimPoint deathPosition)
        {
            if (faction < 1 || faction > 2) return;
            ref var camp = ref world.NeutralCamps[campIndex];
            int slot = checked((int)faction - 1) * 9 + (int)camp.Kind - 1;   // 9 kinds since 8b (was 6: a west dragon was counted as an east wolf)
            neutralKills[slot]++;
            int reward = 0;
            if (EconomyOn)
            {
                if (camp.Kind == NeutralKind.WolfPack) { AddStock(faction, ResourceKind.Food, 120); AddStock(faction, ResourceKind.Wood, 60); reward = 180; }
                else if (camp.Kind == NeutralKind.RockBeast) { AddStock(faction, ResourceKind.Stone, 150); AddStock(faction, ResourceKind.Gold, 80); reward = 230; }
            }
            neutralRewards[slot] += reward;
            NeutralBlessing blessing = camp.Kind == NeutralKind.FlameHornBeast ? NeutralBlessing.Flame
                : camp.Kind == NeutralKind.AzureSerpent ? NeutralBlessing.Azure : NeutralBlessing.None;
            if (blessing != NeutralBlessing.None)
            {
                int blessingTicks = HasAugment(faction, AugmentKind.Hunter) ? 3600 : 2400;
                world.NeutralFactionBlessings[faction - 1] |= blessing; world.NeutralFactionBlessingUntil[faction - 1] = checked(world.Tick + blessingTicks);
                for (int i = 0; i < world.Soldiers.Length; i++)
                    if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction && InRange(world.Soldiers[i].Position, deathPosition, Fix64.FromInt(15)))
                    { world.NeutralSoldierBlessings[i] |= blessing; world.NeutralSoldierBlessingUntil[i] = checked(world.Tick + blessingTicks); neutralBlessed[faction - 1]++; }
            }
            if (camp.Kind == NeutralKind.ShellBeast)
            {
                int visionIndex = checked((int)(faction - 1) * world.NeutralCamps.Length + campIndex);
                world.NeutralShellVisionPositions[visionIndex] = deathPosition;
                world.NeutralShellVisionUntil[visionIndex] = checked(world.Tick + 1800);
                ChangeVisibilityForMode(checked((int)faction - 1), deathPosition, Fix64.FromInt(20).Raw, 1);
                for (int i = 0; i < world.Soldiers.Length; i++)
                    if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction && InRange(world.Soldiers[i].Position, deathPosition, Fix64.FromInt(15)))
                    { world.NeutralSoldierBlessings[i] |= NeutralBlessing.Speed; world.NeutralSoldierBlessingUntil[i] = checked(world.Tick + 600); neutralBlessed[faction - 1]++; }
            }
            if (camp.Kind == NeutralKind.AugmentGuardian)
            {
                augmentGuardianKills[faction - 1]++;
                AugmentGuardianReward(faction);
            }
            if (camp.Kind == NeutralKind.Dragon) AwardDragonMark(faction, world.Neutrals[camp.NeutralIds[0] - 1].DragonElement);
            if (camp.Kind == NeutralKind.GreatLord) AwardGreatLordBlessing(faction);
            uint defeatedOwner = camp.NeutralIds.Length == 0 ? 0 : world.Neutrals[camp.NeutralIds[0] - 1].OwnerFactionId;
            if (camp.Kind == NeutralKind.SiegeBeast && defeatedOwner == 0 && faction >= 1 && faction <= 2)
            {
                ref var beast = ref world.Neutrals[camp.NeutralIds[0] - 1];
                beast.Alive = true; beast.OwnerFactionId = faction; beast.Hp = 1500; beast.MaxHp = 1500;
                beast.AttackDamage = 0; beast.Position = deathPosition; beast.Home = deathPosition; beast.LifeUntilTick = checked(world.Tick + 2400);
                beast.SiegeTargetBuildingId = 0; beast.NextAttackTick = checked(world.Tick + 40); beast.LastHitFaction = 0; beast.LastHitSoldierId = 0;
                camp.NextSpawnTick = long.MaxValue;
                return;
            }
            if (camp.Kind == NeutralKind.SiegeBeast && camp.NeutralIds.Length != 0)
            {
                ref var defeated = ref world.Neutrals[camp.NeutralIds[0] - 1];
                defeated.OwnerFactionId = 0; defeated.SiegeTargetBuildingId = 0; defeated.LifeUntilTick = 0;
            }
            camp.NextSpawnTick = checked(world.Tick + NeutralCatalog.RespawnDelay(world.Config.NeutralRules, camp.Kind));
        }

        private void AwardDragonMark(uint faction, DragonElement element)
        {
            if (!GreatOn || element < DragonElement.Fire || element > DragonElement.Wind) return;
            ref var state = ref world.Factions[faction - 1];
            int index = (int)element;
            state.DragonMarks[index] = checked(state.DragonMarks[index] + 1);
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction) ApplyDragonElement(ref world.Soldiers[i], element);
            if (!state.DragonSoul && state.DragonMarks[1] + state.DragonMarks[2] + state.DragonMarks[3] + state.DragonMarks[4] >= 4)
            {
                state.DragonSoul = true;
                for (int i = 0; i < world.SoldierCount; i++)
                    if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction) ApplyDragonSoul(ref world.Soldiers[i]);
            }
        }

        private void AwardGreatLordBlessing(uint faction)
        {
            greatLordKills[faction - 1]++;
            world.Factions[faction - 1].GreatLordBlessingUntil = checked(world.Tick + 3600);
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction)
                { world.NeutralSoldierBlessings[i] |= NeutralBlessing.GreatLord; world.NeutralSoldierBlessingUntil[i] = world.Factions[faction - 1].GreatLordBlessingUntil; }
        }

        private void ApplyDragonElement(ref SoldierState soldier, DragonElement element)
        {
            if (element == DragonElement.Fire) soldier.Parameters.Damage = checked(soldier.Parameters.Damage * 104 / 100);
            else if (element == DragonElement.Earth)
            {
                int oldMax = soldier.Parameters.Hp; soldier.Parameters.Hp = checked(oldMax * 105 / 100);
                soldier.Hp = checked(soldier.Hp * soldier.Parameters.Hp / Math.Max(1, oldMax));
            }
            else if (element == DragonElement.Wind) soldier.StepDistance = Fix64.FromRaw(soldier.StepDistance.Raw * 104 / 100);
        }

        private void ApplyDragonSoul(ref SoldierState soldier)
        {
            soldier.Parameters.Damage = checked(soldier.Parameters.Damage * 108 / 100);
            int oldMax = soldier.Parameters.Hp; soldier.Parameters.Hp = checked(oldMax * 108 / 100);
            soldier.Hp = checked(soldier.Hp * soldier.Parameters.Hp / Math.Max(1, oldMax));
        }

        private void ApplyGreatBonusesToNewSoldier(int index)
        {
            if (!GreatOn || index < 0 || index >= world.SoldierCount) return;
            uint faction = world.Soldiers[index].Initial.FactionId;
            if (faction < 1 || faction > 2) return;
            ref var state = ref world.Factions[faction - 1];
            for (int element = 1; element <= 4; element++)
                for (int count = 0; count < state.DragonMarks[element]; count++) ApplyDragonElement(ref world.Soldiers[index], (DragonElement)element);
            if (state.DragonSoul) ApplyDragonSoul(ref world.Soldiers[index]);
        }

        private void ExpireNeutralShellVision()
        {
            for (int faction = 0; faction < 2; faction++)
                for (int campIndex = 0; campIndex < world.NeutralCamps.Length; campIndex++)
                {
                    int index = checked(faction * world.NeutralCamps.Length + campIndex);
                    if (world.NeutralShellVisionUntil[index] == 0 || world.NeutralShellVisionUntil[index] > world.Tick) continue;
                    ChangeVisibilityForMode(faction, world.NeutralShellVisionPositions[index], Fix64.FromInt(20).Raw, -1);
                    world.NeutralShellVisionUntil[index] = 0;
                }
        }

        private bool NeutralVisionReveals(uint faction, SimPoint point)
        {
            if (faction < 1 || faction > 2) return false;
            int offset = checked((int)(faction - 1) * world.NeutralCamps.Length);
            for (int i = 0; i < world.NeutralCamps.Length; i++)
                if (world.NeutralShellVisionUntil[offset + i] > world.Tick
                    && InRange(point, world.NeutralShellVisionPositions[offset + i], Fix64.FromInt(20))) return true;
            return false;
        }

        private Fix64 NeutralDistanceScore(SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            return Fix64.FromRaw(checked(dx * dx / 65536L + dz * dz / 65536L));
        }

        private List<VisibleNeutral> VisibleNeutralsFor(uint faction)
        {
            var result = new List<VisibleNeutral>();
            for (int i = 0; i < world.Neutrals.Length; i++)
            {
                var n = world.Neutrals[i];
                if (!n.Alive || !IsVisibleTo(faction, n.Position)) continue;
                result.Add(new VisibleNeutral(n.Id, n.CampId, n.Kind, n.Position, n.Hp, n.MaxHp, true, n.DragonElement, n.OwnerFactionId));
            }
            result.Sort((a, b) => a.Id.CompareTo(b.Id));
            return result;
        }

        private List<NeutralCampView> NeutralCampsFor(uint faction)
        {
            var result = new List<NeutralCampView>(world.NeutralCamps.Length);
            int offset = checked((int)(faction - 1) * world.NeutralCamps.Length);
            foreach (var camp in world.NeutralCamps)
            {
                var memory = world.NeutralCampMemories[offset + result.Count];
                result.Add(new NeutralCampView(camp.Id, camp.Kind, camp.Position, memory.LastKnownHp, memory.LastKnownMaxHp, memory.LastKnownAlive, memory.LastSeenTick));
            }
            return result;
        }

        private void UpdateNeutralObservationMemory()
        {
            if (!NeutralsOn || world.NeutralCamps.Length == 0) return;
            for (uint faction = 1; faction <= 2; faction++)
                for (int i = 0; i < world.NeutralCamps.Length; i++)
                {
                    var camp = world.NeutralCamps[i];
                    bool campVisible = IsVisibleTo(faction, camp.Position);
                    bool creatureVisible = false;
                    for (int j = 0; j < camp.NeutralIds.Length; j++)
                    {
                        var neutral = world.Neutrals[camp.NeutralIds[j] - 1];
                        if (neutral.Alive && IsVisibleTo(faction, neutral.Position)) { creatureVisible = true; break; }
                    }
                    if (!campVisible && !creatureVisible) continue;
                    int hp = CampAliveHp(camp), maxHp = 0;
                    foreach (uint id in camp.NeutralIds) maxHp = checked(maxHp + world.Neutrals[id - 1].MaxHp);
                    world.NeutralCampMemories[checked((int)((faction - 1) * (uint)world.NeutralCamps.Length) + i)] = new NeutralCampMemoryState
                    { LastKnownHp = hp, LastKnownMaxHp = maxHp, LastKnownAlive = hp > 0, LastSeenTick = world.Tick };
                }
        }

        public long NeutralKillsFor(uint faction, NeutralKind kind) => !NeutralsOn ? 0 : neutralKills[(faction - 1) * 9 + (int)kind - 1];
        public long NeutralRewardsFor(uint faction, NeutralKind kind) => !NeutralsOn ? 0 : neutralRewards[(faction - 1) * 9 + (int)kind - 1];
        public long NeutralBlessedFor(uint faction) => !NeutralsOn ? 0 : neutralBlessed[faction - 1];
        internal long NeutralHuntOrdersFor(uint faction) => !NeutralsOn ? 0 : neutralHuntOrders[faction - 1];
        public long NeutralHuntFailuresFor(uint faction, NeutralHuntFailureReason reason)
            => !NeutralsOn || faction < 1 || faction > 2 || reason < NeutralHuntFailureReason.Role || reason > NeutralHuntFailureReason.CentralEnemyNearby
                ? 0 : neutralHuntFailures[faction - 1, (int)reason - 1];

        public long SiegeBuildingDamageFor(uint faction) => !GreatOn || faction < 1 || faction > 2 ? 0 : siegeBuildingDamage[faction - 1];
        public long GreatLordKillsFor(uint faction) => !GreatOn || faction < 1 || faction > 2 ? 0 : greatLordKills[faction - 1];
        public int DragonMarkFor(uint faction, DragonElement element)
            => !GreatOn || faction < 1 || faction > 2 || element < DragonElement.Fire || element > DragonElement.Wind ? 0 : world.Factions[faction - 1].DragonMarks[(int)element];
        public bool DragonSoulFor(uint faction) => GreatOn && faction >= 1 && faction <= 2 && world.Factions[faction - 1].DragonSoul;
        public long GreatLordBlessingTicksFor(uint faction) => !GreatOn || faction < 1 || faction > 2 ? 0 : Math.Max(0, world.Factions[faction - 1].GreatLordBlessingUntil - world.Tick);
    }
}
