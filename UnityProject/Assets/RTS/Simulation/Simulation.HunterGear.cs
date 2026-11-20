using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private bool HunterGearOn => world.Config.Economy.HunterGear && NeutralsOn;

        private bool HunterMarksSharedOn => HunterGearOn && world.Config.Economy.HunterMarksShared;

        // Chapter 85.6: with shared marks the tier is the faction's, so every hunter of a faction has the same tier.
        private int HunterMarksOf(in SoldierState soldier)
            => HunterMarksSharedOn ? world.Economies[soldier.Initial.FactionId - 1].HunterMarks : soldier.HunterMarks;

        private static bool HunterGearEligible(UnitKind kind)
            => kind != UnitKind.Villager && kind != UnitKind.Scout && kind != UnitKind.Monk
                && kind != UnitKind.Ram && kind != UnitKind.Cannon;

        private byte HunterTierFor(int marks)
        {
            if (!HunterGearOn) return 0;
            return marks >= world.Config.Economy.HunterGearTier3Marks ? (byte)3
                : marks >= world.Config.Economy.HunterGearTier2Marks ? (byte)2 : (byte)1;
        }

        private int HunterTarget(uint faction)
            => world.Factions[faction - 1].EnemyCoreSeenTick == 0
                ? world.Config.Economy.HunterGearTargetBeforeCore
                : world.Config.Economy.HunterGearTargetAfterCore;

        private int HunterEquippedCount(uint faction)
        {
            int count = 0;
            foreach (int i in world.SoldierTraversal)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction && world.Soldiers[i].HunterEquipped) count++;
            return count;
        }

        private int HuntArmyEquippedCount(ArmyState army)
        {
            int count = 0;
            foreach (uint id in army.SoldierIds)
                if (id > 0 && id <= world.SoldierCount && world.Soldiers[id - 1].Alive && world.Soldiers[id - 1].HunterEquipped) count++;
            return count;
        }

        private int HuntArmyEquippedHp(ArmyState army)
        {
            int hp = 0;
            foreach (uint id in army.SoldierIds)
                if (id > 0 && id <= world.SoldierCount && world.Soldiers[id - 1].Alive && world.Soldiers[id - 1].HunterEquipped)
                    hp = checked(hp + Math.Max(0, world.Soldiers[id - 1].Hp));
            return hp;
        }

        private System.Collections.Generic.IEnumerable<uint> HuntArmyIds(uint faction)
        {
            if (!HunterGearOn) return world.Factions[faction - 1].ArmyIds;
            var ids = new System.Collections.Generic.List<uint>(world.Factions[faction - 1].ArmyIds);
            ids.Sort((left, right) =>
            {
                int c = HuntArmyEquippedCount(world.Armies[right - 1]).CompareTo(HuntArmyEquippedCount(world.Armies[left - 1]));
                if (c != 0) return c;
                c = HuntArmyEquippedHp(world.Armies[right - 1]).CompareTo(HuntArmyEquippedHp(world.Armies[left - 1]));
                return c != 0 ? c : left.CompareTo(right);
            });
            return ids;
        }

        private int HuntArmyHp(ArmyState army)
        {
            int hp = 0;
            foreach (uint id in army.SoldierIds)
            {
                if (!world.Soldiers[id - 1].Alive) continue;
                var soldier = world.Soldiers[id - 1];
                int value = Math.Max(0, soldier.Hp);
                if (HunterGearOn && soldier.HunterEquipped)
                    value = checked(value * (HunterTierFor(HunterMarksOf(soldier)) == 3 ? world.Config.Economy.HunterGearTier3NeutralDamagePermille
                        : HunterTierFor(HunterMarksOf(soldier)) == 2 ? world.Config.Economy.HunterGearTier2NeutralDamagePermille
                        : world.Config.Economy.HunterGearTier1NeutralDamagePermille) / 1000);
                hp = checked(hp + value);
            }
            return hp;
        }

        private int HunterQueuedCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.FactionId != faction || b.QueueHunterGear == null) continue;
                for (int q = 0; q < b.QueueHunterGear.Length; q++) if (b.QueueHunterGear[q]) count++;
            }
            return count;
        }

        private bool ShouldQueueHunterGear(uint faction, UnitKind kind)
            => HunterGearOn && HunterGearEligible(kind)
                && world.Economies[faction - 1].Hides >= world.Config.Economy.HunterGearHideCost
                && HunterEquippedCount(faction) + HunterQueuedCount(faction) < HunterTarget(faction);

        private bool QueueHunterGearAt(BuildingState b, int index)
            => b.QueueHunterGear != null && index >= 0 && index < b.QueueHunterGear.Length && b.QueueHunterGear[index];

        private void AddHunterGearToSoldier(int index)
        {
            if (!HunterGearOn || index < 0 || index >= world.SoldierCount) return;
            ref var soldier = ref world.Soldiers[index];
            if (!HunterGearEligible(soldier.Initial.Kind) && !HunterGearEligible(soldier.Class)) return;
            soldier.HunterEquipped = true;
            soldier.HunterMarks = 0;
        }

        // A soldier keeps only the camp it last hit and when: the beasts of one camp fight together, so a hit on any of
        // them counts toward a kill in that camp (a per-soldier-per-beast table was large and was written on every dump).
        private void RecordHunterNeutralAttack(int soldierIndex, int neutralIndex)
        {
            if (!HunterGearOn || soldierIndex < 0 || soldierIndex >= world.SoldierCount
                || neutralIndex < 0 || neutralIndex >= world.Neutrals.Length) return;
            ref var soldier = ref world.Soldiers[soldierIndex];
            if (!soldier.HunterEquipped) return;
            soldier.HunterLastCampId = world.Neutrals[neutralIndex].CampId;
            soldier.HunterLastCampTick = world.Tick;
        }

        private int HunterHideReward(NeutralKind kind)
        {
            var e = world.Config.Economy;
            switch (kind)
            {
                case NeutralKind.WolfPack: return e.HunterGearWolfHides;
                case NeutralKind.RockBeast: return e.HunterGearRockHides;
                case NeutralKind.FlameHornBeast: return e.HunterGearFlameHides;
                case NeutralKind.AzureSerpent: return e.HunterGearAzureHides;
                case NeutralKind.ShellBeast: return e.HunterGearShellHides;
                case NeutralKind.SiegeBeast: return e.HunterGearSiegeHides;
                case NeutralKind.Dragon: return e.HunterGearDragonHides;
                case NeutralKind.AugmentGuardian: return e.HunterGearGuardianHides;
                case NeutralKind.GreatLord: return e.HunterGearGreatLordHides;
                default: return 0;
            }
        }

        private void ResolveHunterNeutralDeath(int neutralIndex)
        {
            if (!HunterGearOn || neutralIndex < 0 || neutralIndex >= world.Neutrals.Length) return;
            var neutral = world.Neutrals[neutralIndex];
            uint faction = neutral.LastHitFaction;
            if (faction < 1 || faction > 2) return;
            int reward = HunterHideReward(neutral.Kind);
            world.Economies[faction - 1].Hides = checked(world.Economies[faction - 1].Hides + reward);
            world.Economies[faction - 1].HunterHidesObtained = checked(world.Economies[faction - 1].HunterHidesObtained + reward);
            int radius = world.Config.Economy.HunterGearRangeMeters;
            long lookback = world.Config.Economy.HunterGearAttackLookbackTicks;
            bool shared = HunterMarksSharedOn;
            bool anyHunter = false;
            for (int i = 0; i < world.SoldierCount; i++)
            {
                ref var soldier = ref world.Soldiers[i];
                if (!soldier.Alive || soldier.Initial.FactionId != faction || !soldier.HunterEquipped
                    || !InRange(soldier.Position, neutral.Position, Fix64.FromInt(radius))) continue;
                if (soldier.HunterLastCampId == 0 || soldier.HunterLastCampId != neutral.CampId
                    || world.Tick - soldier.HunterLastCampTick > lookback) continue;
                byte tier = HunterTierFor(HunterMarksOf(soldier));
                anyHunter = true;
                if (!shared) soldier.HunterMarks = checked(soldier.HunterMarks + 1);
                if (tier == 1)
                {
                    int heal = checked((soldier.Parameters.Hp * world.Config.Economy.HunterGearTier1KillHealPermille + 999) / 1000);
                    soldier.Hp = Math.Min(soldier.Parameters.Hp, checked(soldier.Hp + heal));
                }
            }
            // Shared marks: one mark per beast, given when at least one of the faction's hunters took part.
            if (shared && anyHunter) world.Economies[faction - 1].HunterMarks = checked(world.Economies[faction - 1].HunterMarks + 1);
        }

        private int HunterGearNeutralDamage(int soldierIndex, int damage)
        {
            if (!HunterGearOn || damage <= 0 || soldierIndex < 0 || soldierIndex >= world.SoldierCount) return damage;
            var soldier = world.Soldiers[soldierIndex];
            if (!soldier.HunterEquipped) return damage;
            var e = world.Config.Economy;
            int factor = HunterTierFor(HunterMarksOf(soldier)) == 3 ? e.HunterGearTier3NeutralDamagePermille
                : HunterTierFor(HunterMarksOf(soldier)) == 2 ? e.HunterGearTier2NeutralDamagePermille : e.HunterGearTier1NeutralDamagePermille;
            return checked(damage * factor / 1000);
        }

        private int HunterGearEnemyDamage(int soldierIndex, int damage)
        {
            if (!HunterGearOn || damage <= 0 || soldierIndex < 0 || soldierIndex >= world.SoldierCount) return damage;
            var soldier = world.Soldiers[soldierIndex];
            return soldier.HunterEquipped && HunterTierFor(HunterMarksOf(soldier)) >= 3
                ? checked(damage * world.Config.Economy.HunterGearEnemyDamagePermille / 1000) : damage;
        }

        private void ApplyHunterCombatHealing()
        {
            if (!HunterGearOn) return;
            int interval = Math.Max(1, world.Config.Economy.HunterGearCombatHealIntervalTicks);
            if (world.Tick % interval != 0) return;
            for (int i = 0; i < world.SoldierCount; i++)
            {
                ref var soldier = ref world.Soldiers[i];
                if (!soldier.Alive || !soldier.HunterEquipped || HunterTierFor(HunterMarksOf(soldier)) < 2 || soldier.TargetKind != TargetNeutral) continue;
                int target = checked((int)soldier.TargetId - 1);
                if (target < 0 || target >= world.Neutrals.Length || !world.Neutrals[target].Alive) continue;
                int heal = Math.Max(1, checked((soldier.Parameters.Hp * world.Config.Economy.HunterGearTier2CombatHealPermille + 999) / 1000));
                soldier.Hp = Math.Min(soldier.Parameters.Hp, checked(soldier.Hp + heal));
            }
        }

        public int HunterHidesObtainedFor(uint faction)
            => HunterGearOn && faction >= 1 && faction <= world.Economies.Length ? world.Economies[faction - 1].HunterHidesObtained : 0;

        public int HunterHidesSpentFor(uint faction)
            => HunterGearOn && faction >= 1 && faction <= world.Economies.Length ? world.Economies[faction - 1].HunterHidesSpent : 0;

        public int HunterFactionMarksFor(uint faction)
            => HunterMarksSharedOn && faction >= 1 && faction <= world.Economies.Length ? world.Economies[faction - 1].HunterMarks : 0;

        public int HunterEquippedCountFor(uint faction)
            => HunterGearOn && faction >= 1 && faction <= world.Economies.Length ? HunterEquippedCount(faction) : 0;

        public int HunterTierCountFor(uint faction, byte tier)
        {
            if (!HunterGearOn || faction < 1 || faction > world.Economies.Length) return 0;
            int count = 0;
            foreach (int i in world.SoldierTraversal)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction && world.Soldiers[i].HunterEquipped
                    && HunterTierFor(HunterMarksOf(world.Soldiers[i])) == tier) count++;
            return count;
        }
    }
}
