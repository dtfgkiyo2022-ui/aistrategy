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
            if (world.Tick % world.Config.NeutralRules.HuntIntervalTicks != 0) return;
            for (int i = 0; i < world.NeutralOrderCamps.Length; i++)
                if (world.NeutralOrderCamps[i] >= 0x80000000U) { world.NeutralOrders[i] = 0; world.NeutralOrderCamps[i] = 0; }
            for (uint faction = 1; faction <= 2; faction++)
            {
                foreach (uint armyId in world.Factions[faction - 1].ArmyIds)
                {
                    ref var army = ref world.Armies[armyId - 1];
                    if (army.Policy == PolicyKind.Focus || army.Policy == PolicyKind.Defend || army.Decision.Assignment == AssignmentKind.Guard
                        || army.Decision.Assignment == AssignmentKind.CoreDefense || army.Decision.Assignment == AssignmentKind.Reserve) continue;
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
                if (army.SoldierIds.Length == 0 || ArmyHp(army) * 1000L < (long)hp * world.Config.NeutralRules.HuntStrengthPermille) continue;
                var home = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
                Fix64 distance = NeutralDistanceScore(centre, camp.Position);
                int priority = HuntPriority(camp.Kind, camp.Position, home);
                if (priority < selectedPriority || priority == selectedPriority && distance.Raw < selectedDistance.Raw
                    || priority == selectedPriority && distance.Raw == selectedDistance.Raw && camp.Id < selected)
                { selected = camp.Id; selectedPriority = priority; selectedDistance = distance; }
            }
            return selected;
        }

        private static int HuntPriority(NeutralKind kind, SimPoint camp, SimPoint home)
        {
            bool nearHome = camp.X.Raw < home.X.Raw ? home.X.Raw - camp.X.Raw < Fix64.FromInt(96).Raw : camp.X.Raw - home.X.Raw < Fix64.FromInt(96).Raw;
            if (kind == NeutralKind.FlameHornBeast || kind == NeutralKind.AzureSerpent) return nearHome ? 0 : 4;
            if (kind == NeutralKind.ShellBeast) return 1;
            if (kind == NeutralKind.RockBeast) return 2;
            return 3;
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
                    neutral.LastHitFaction = 0; neutral.LastHitSoldierId = 0; neutral.LastCombatTick = -1; neutral.Returning = false;
                }
                camp.HasSpawned = true; camp.LastKnownAlive = true; camp.LastKnownHp = checked(camp.NeutralIds.Length * world.Neutrals[camp.NeutralIds[0] - 1].MaxHp);
            }
        }

        private void NeutralMoveStep()
        {
            if (!NeutralsOn) return;
            for (int i = 0; i < world.Neutrals.Length; i++)
            {
                ref var neutral = ref world.Neutrals[i];
                if (!neutral.Alive) continue;
                bool targetAlive = neutral.LastHitSoldierId > 0 && neutral.LastHitSoldierId <= world.SoldierCount
                    && world.Soldiers[neutral.LastHitSoldierId - 1].Alive;
                if (!targetAlive || world.Tick - neutral.LastCombatTick > world.Config.NeutralRules.LostEnemyTicks
                    || !InRange(neutral.Home, targetAlive ? world.Soldiers[neutral.LastHitSoldierId - 1].Position : neutral.Position,
                        Fix64.FromInt(world.Config.NeutralRules.LeashMeters)))
                { neutral.Returning = true; neutral.LastHitSoldierId = 0; neutral.LastHitFaction = 0; }
                SimPoint goal = neutral.Returning ? neutral.Home : world.Soldiers[neutral.LastHitSoldierId - 1].Position;
                if (!SamePoint(neutral.Position, goal)) neutral.Position = FixMath.MoveTowards(neutral.Position, goal, Fix64.FromRatio(3, 2));
                if (neutral.Returning && InRange(neutral.Position, neutral.Home, Fix64.FromInt(1))) { neutral.Returning = false; neutral.LastCombatTick = -1; }
                if (neutral.LastCombatTick < 0 || world.Tick - neutral.LastCombatTick >= world.Config.NeutralRules.HealDelayTicks)
                    neutral.Hp = Math.Min(neutral.MaxHp, checked(neutral.Hp + Math.Max(1, neutral.MaxHp * world.Config.NeutralRules.HealPermillePerSecond / 1000 / 20)));
            }
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
            neutral.LastHitFaction = soldier.Initial.FactionId; neutral.LastHitSoldierId = soldier.Initial.Id;
            neutral.LastCombatTick = world.Tick;
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
            neutral.LastHitFaction = soldier.Initial.FactionId;
            neutral.LastHitSoldierId = soldier.Initial.Id;
            neutral.LastHitTick = world.Tick;
            neutral.LastCombatTick = world.Tick;
            return true;
        }

        private void NeutralAttackStep()
        {
            if (!NeutralsOn) return;
            for (int i = 0; i < world.Neutrals.Length; i++)
            {
                ref var neutral = ref world.Neutrals[i];
                if (!neutral.Alive || neutral.AttackDamage <= 0 || neutral.LastHitSoldierId == 0 || world.Tick < neutral.NextAttackTick) continue;
                int target = checked((int)neutral.LastHitSoldierId) - 1;
                if (target < 0 || target >= world.Soldiers.Length || !world.Soldiers[target].Alive || !InRange(neutral.Position, world.Soldiers[target].Position, neutral.Range)) continue;
                int damage = CombatStatsOn ? Rts.Decision.CombatMath.DamageAfterDefense(neutral.AttackDamage, world.Soldiers[target].Parameters.Armor, 0, 0) : neutral.AttackDamage;
                soldierDamage[target] = checked(soldierDamage[target] + Math.Max(1, damage));
                world.Soldiers[target].LastCombatTick = world.Tick;
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
                for (int i = 0; i < world.Soldiers.Length; i++)
                    if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction && InRange(world.Soldiers[i].Position, deathPosition, Fix64.FromInt(15)))
                    { world.NeutralSoldierBlessings[i] |= NeutralBlessing.Speed; world.NeutralSoldierBlessingUntil[i] = checked(world.Tick + 600); neutralBlessed[faction - 1]++; }
            }
            camp.NextSpawnTick = checked(world.Tick + NeutralCatalog.RespawnDelay(world.Config.NeutralRules, camp.Kind));
            camp.LastKnownAlive = false; camp.LastKnownHp = 0; camp.LastSeenTick = world.Tick;
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
                int campIndex = FindNeutralCamp(n.CampId);
                if (campIndex >= 0) { world.NeutralCamps[campIndex].LastKnownHp = n.Hp; world.NeutralCamps[campIndex].LastKnownMaxHp = n.MaxHp; world.NeutralCamps[campIndex].LastKnownAlive = true; world.NeutralCamps[campIndex].LastSeenTick = world.Tick; }
            }
            result.Sort((a, b) => a.Id.CompareTo(b.Id));
            return result;
        }

        private List<NeutralCampView> NeutralCampsFor(uint faction)
        {
            var result = new List<NeutralCampView>(world.NeutralCamps.Length);
            foreach (var camp in world.NeutralCamps)
                result.Add(new NeutralCampView(camp.Id, camp.Kind, camp.Position, camp.LastKnownHp, camp.LastKnownMaxHp, camp.LastKnownAlive, camp.LastSeenTick));
            return result;
        }

        internal long NeutralKillsFor(uint faction, NeutralKind kind) => !NeutralsOn ? 0 : neutralKills[(faction - 1) * 5 + (int)kind - 1];
        internal long NeutralHuntOrdersFor(uint faction) => !NeutralsOn ? 0 : neutralHuntOrders[faction - 1];
    }
}
