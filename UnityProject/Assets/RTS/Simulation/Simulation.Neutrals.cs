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

        private bool NeutralsOn => world.Config.Economy.Neutrals;

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
            neutralKills = new long[checked(world.Factions.Length * 5)];
            neutralRewards = new long[checked(world.Factions.Length * 5)];
            neutralBlessed = new long[world.Factions.Length];
            neutralHuntOrders = new long[world.Factions.Length];
        }

        private void NeutralThink()
        {
            if (!NeutralsOn) return;
            ExpireNeutralEffects();
            ExpireNeutralShellVision();
            CancelNeutralHuntsNearEnemies();
            if (world.Tick % world.Config.NeutralRules.HuntIntervalTicks != 0) return;
            for (int i = 0; i < world.NeutralOrderCamps.Length; i++)
                if (world.NeutralOrderCamps[i] >= 0x80000000U) ClearNeutralOrder(i);
            for (uint faction = 1; faction <= 2; faction++)
            {
                foreach (uint armyId in world.Factions[faction - 1].ArmyIds)
                {
                    ref var army = ref world.Armies[armyId - 1];
                    if (army.Policy != 0 || army.Decision.Assignment != AssignmentKind.Advance
                        || army.Decision.Goal.Kind != GoalKind.None || army.Decision.Returning) continue;
                    uint campId = ChooseHuntCamp(faction, army);
                    if (campId == 0) continue;
                    foreach (uint soldierId in army.SoldierIds)
                    {
                        int index = checked((int)soldierId) - 1;
                        if (!world.Soldiers[index].Alive || world.NeutralOrderCamps[index] != 0) continue;
                        world.NeutralOrderCamps[index] = 0x80000000U | campId;
                        world.NeutralOrders[index] = campId;
                    }
                    neutralHuntOrders[faction - 1]++;
                }
            }
        }

        private uint ChooseHuntCamp(uint faction, ArmyState army)
        {
            SimPoint centre = army.SoldierIds.Length == 0 ? world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position : world.Soldiers[army.SoldierIds[0] - 1].Position;
            uint selected = 0; int selectedPriority = int.MaxValue; Fix64 selectedDistance = Fix64.FromInt(int.MaxValue);
            for (int i = 0; i < world.NeutralCamps.Length; i++)
            {
                var camp = world.NeutralCamps[i];
                int hp = CampAliveHp(camp);
                if (hp <= 0 || EnemyVisibleNear(faction, camp.Position, world.Config.NeutralRules.HuntRadiusMeters)) continue;
                if ((camp.Kind == NeutralKind.FlameHornBeast || camp.Kind == NeutralKind.AzureSerpent || camp.Kind == NeutralKind.ShellBeast)
                    && (!AgesOn || world.Economies[faction - 1].Age < 1)) continue;
                if (army.SoldierIds.Length == 0 || ArmyHp(army) * 1000L < (long)hp * world.Config.NeutralRules.HuntStrengthPermille) continue;
                var home = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
                Fix64 distance = NeutralDistanceScore(centre, camp.Position);
                if (!OwnSideCamp(faction, camp.Position)) continue;
                int priority = HuntPriority(camp.Kind);
                if (priority < selectedPriority || priority == selectedPriority && distance.Raw < selectedDistance.Raw
                    || priority == selectedPriority && distance.Raw == selectedDistance.Raw && camp.Id < selected)
                { selected = camp.Id; selectedPriority = priority; selectedDistance = distance; }
            }
            return selected;
        }

        private static int HuntPriority(NeutralKind kind)
        {
            if (kind == NeutralKind.FlameHornBeast || kind == NeutralKind.AzureSerpent) return 0;
            if (kind == NeutralKind.ShellBeast) return 1;
            if (kind == NeutralKind.RockBeast) return 2;
            return 3;
        }

        private bool OwnSideCamp(uint faction, SimPoint camp)
        {
            if (faction < 1 || faction > 2) return false;
            SimPoint own = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
            int enemyIndex = faction == 1 ? 1 : 0;
            SimPoint enemy = world.Cores[world.Factions[enemyIndex].CoreId - 1].Definition.Position;
            int ownDistance = NeutralPathDistance(camp, own, faction), enemyDistance = NeutralPathDistance(camp, enemy, faction);
            return ownDistance <= enemyDistance;
        }

        private int NeutralPathDistance(SimPoint camp, SimPoint core, uint faction)
        {
            int[] path = world.Map.FindPath(world.Map.Cell(camp), core, faction);
            return path.Length == 0 ? int.MaxValue : path.Length;
        }

        private void CancelNeutralHuntsNearEnemies()
        {
            for (int i = 0; i < world.NeutralOrderCamps.Length; i++)
            {
                uint marker = world.NeutralOrderCamps[i];
                if (marker < 0x80000000U) continue;
                uint faction = world.Soldiers[i].Initial.FactionId;
                if (EnemyVisibleNear(faction, marker & 0x7fffffffU, world.Config.NeutralRules.HuntRadiusMeters))
                    ClearNeutralOrder(i);
            }
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
                soldier.TargetKind = TargetNeutral;
                soldier.TargetId = FirstAliveNeutral(world.NeutralCamps[campIndex]);
                soldier.MoveGoal = world.NeutralCamps[campIndex].Position;
                if (soldier.TargetId != 0) soldier.MoveGoal = world.Neutrals[soldier.TargetId - 1].Position;
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
                    neutral.NextSkillTick = neutral.Kind == NeutralKind.ShellBeast ? long.MaxValue : checked(world.Tick + NeutralCatalog.SkillInterval(neutral.Kind));
                }
                camp.HasSpawned = true;
            }
        }

        private void NeutralMoveStep()
        {
            if (!NeutralsOn) return;
            for (int i = 0; i < world.Neutrals.Length; i++)
            {
                ref var neutral = ref world.Neutrals[i];
                if (!neutral.Alive) continue;
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
            int damage = CombatStatsOn
                ? Rts.Decision.CombatMath.DamageAfterDefense(soldier.Parameters.Damage, neutral.Armor, soldier.Parameters.PhysicalPenetrationPermille, soldier.Parameters.PhysicalFlatPenetration)
                : soldier.Parameters.Damage;
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
            int defense = type == SkillDamageType.Magic ? world.Soldiers[target].Parameters.MagicResist : world.Soldiers[target].Parameters.Armor;
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
            int slot = checked((int)faction - 1) * 5 + (int)camp.Kind - 1;
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
                world.NeutralFactionBlessings[faction - 1] |= blessing; world.NeutralFactionBlessingUntil[faction - 1] = checked(world.Tick + 2400);
                for (int i = 0; i < world.Soldiers.Length; i++)
                    if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction && InRange(world.Soldiers[i].Position, deathPosition, Fix64.FromInt(15)))
                    { world.NeutralSoldierBlessings[i] |= blessing; world.NeutralSoldierBlessingUntil[i] = checked(world.Tick + 2400); neutralBlessed[faction - 1]++; }
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
            camp.NextSpawnTick = checked(world.Tick + NeutralCatalog.RespawnDelay(world.Config.NeutralRules, camp.Kind));
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
                result.Add(new VisibleNeutral(n.Id, n.CampId, n.Kind, n.Position, n.Hp, n.MaxHp, true));
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

        public long NeutralKillsFor(uint faction, NeutralKind kind) => !NeutralsOn ? 0 : neutralKills[(faction - 1) * 5 + (int)kind - 1];
        public long NeutralRewardsFor(uint faction, NeutralKind kind) => !NeutralsOn ? 0 : neutralRewards[(faction - 1) * 5 + (int)kind - 1];
        public long NeutralBlessedFor(uint faction) => !NeutralsOn ? 0 : neutralBlessed[faction - 1];
        internal long NeutralHuntOrdersFor(uint faction) => !NeutralsOn ? 0 : neutralHuntOrders[faction - 1];
    }
}
