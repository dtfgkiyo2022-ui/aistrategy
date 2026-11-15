using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        internal const int SkillThinkInterval = 10;
        private int[] skillCandidates;
        private readonly int[] skillSpatialCandidates = Array.Empty<int>();
        private int[] skillQueryCandidates;
        private int[] skillHitTargets;
        private const int SkillCcCount = 9;
        private const int SkillBuffSlots = 6;
        private static readonly int[] ConeHalfCosPermille =
        {
            1000,1000,1000,999,999,999,998,998,997,996,995,995,994,993,991,990,989,988,986,985,
            983,982,980,978,976,974,972,970,968,966,964,961,959,956,954,951,948,946,943,940,
            937,934,930,927,924,921,917,914,910,906,903,899,895,891,887,883,879,875,870,866,
            862,857,853,848,843,839,834,829,824,819,814,809,804,799,793,788,783,777,772,766,
            760,755,749,743,737,731,725,719,713,707,701,695,688,682,676,669,663,656,649,643,
            636,629,623,616,609,602,595,588,581,574,566,559,552,545,537,530,522,515,508,500,
            492,485,477,469,462,454,446,438,431,423,415,407,399,391,383,375,367,358,350,342,
            334,326,317,309,301,292,284,276,267,259,250,242,233,225,216,208,199,191,182,174,
            165,156,148,139,131,122,113,105,96,87,78,70,61,52,44,35,26,17,9,0
        };

        private bool SkillsOn => world.Config.Economy.Skills;

        private void InitializeSkillsRuntime()
        {
            skillCandidates = new int[Math.Max(1, world.Soldiers.Length)];
            skillQueryCandidates = new int[Math.Max(1, world.Soldiers.Length)];
            skillHitTargets = new int[Math.Max(1, world.Soldiers.Length)];
            for (int i = 0; i < world.Soldiers.Length; i++)
            {
                var kit = KitFor(SkillUnitKind(i));
                world.SkillResourceAmount[i] = kit == null ? 0 : kit.ResourceMax;
                world.SkillResourceStampTick[i] = 0;
                world.SkillRageStampTick[i] = 0;
                world.SkillLastCombatTick[i] = -1;
                world.SkillCastTarget[i] = -1;
                for (int cc = 0; cc < SkillCcCount; cc++) world.SkillCcTarget[i * SkillCcCount + cc] = -1;
            }
        }

        private UnitSkillKit KitFor(UnitKind kind)
        {
            var kits = world.Config.UnitSkillKits;
            for (int i = 0; i < kits.Length; i++) if (kits[i].UnitKind == kind) return kits[i];
            return null;
        }

        private UnitKind SkillUnitKind(int index)
            => world.Soldiers[index].Class != 0 ? world.Soldiers[index].Class : world.Soldiers[index].Initial.Kind;

        internal void EnsureSkillsRuntimeCapacity(int capacity)
        {
            if (!SkillsOn) return;
            if (skillCandidates == null || skillCandidates.Length < capacity) skillCandidates = new int[capacity];
            if (skillQueryCandidates == null || skillQueryCandidates.Length < capacity) skillQueryCandidates = new int[capacity];
            if (skillHitTargets == null || skillHitTargets.Length < capacity) skillHitTargets = new int[capacity];
        }

        private SkillDefinition SkillFor(int id)
        {
            if (id <= 0 || id > world.Config.Skills.Length) return null;
            return world.Config.Skills[id - 1];
        }

        /// <summary>Internal skill entry point. A failed attempt leaves all state unchanged.</summary>
        public SkillCastFailure TryBeginCast(int soldierIndex, int slot, SkillAim aim)
        {
            if (!SkillsOn || soldierIndex < 0 || soldierIndex >= world.Soldiers.Length) return SkillCastFailure.InvalidSoldier;
            ref var soldier = ref world.Soldiers[soldierIndex];
            if (!soldier.Alive) return SkillCastFailure.InvalidSoldier;
            var kit = KitFor(SkillUnitKind(soldierIndex));
            if (kit == null || slot < 0 || slot >= 4 || kit.SkillIds == null || slot >= kit.SkillIds.Length || kit.SkillIds[slot] == 0)
                return SkillCastFailure.NoSkill;
            var skill = SkillFor(kit.SkillIds[slot]);
            if (skill == null) return SkillCastFailure.NoSkill;
            if (world.SkillCastId[soldierIndex] != 0) return SkillCastFailure.Busy;
            if (world.SkillCooldowns[soldierIndex * 8 + slot] > world.Tick) return SkillCastFailure.Cooldown;
            if (SkillCannotAct(soldierIndex)) return SkillCastFailure.CrowdControlled;
            if (!SkillAimValid(soldierIndex, skill, aim)) return SkillCastFailure.InvalidTarget;
            int resource = ReadSkillResource(soldierIndex, kit);
            if (skill.Resource == SkillResource.Health)
            {
                if (resource - skill.Cost < 1) return SkillCastFailure.Resource;
            }
            else if (skill.Cost > resource) return SkillCastFailure.Resource;
            if (skill.Resource != SkillResource.None) WriteSkillResource(soldierIndex, kit, resource - skill.Cost);
            world.SkillCooldowns[soldierIndex * 8 + slot] = checked(world.Tick + skill.CooldownTicks);
            world.SkillCastId[soldierIndex] = skill.Id; world.SkillCastSlot[soldierIndex] = slot;
            world.SkillCastTarget[soldierIndex] = aim.TargetSoldierIndex;
            world.SkillCastPoint[soldierIndex] = aim.Point; world.SkillCastDirection[soldierIndex] = aim.Direction;
            world.SkillCastTick[soldierIndex] = checked(world.Tick + skill.CastTicks);
            AddSkillCastingSoldier(soldierIndex);
            soldier.IsMoving = false;
            soldier.MoveGoal = soldier.Position;
            return SkillCastFailure.None;
        }

        private bool SkillAimValid(int caster, SkillDefinition skill, SkillAim aim)
        {
            var s = world.Soldiers[caster];
            if (skill.Targeting == SkillTargeting.Self) return true;
            // Directional skills consume the direction vector only. Their reference point is not a target and must
            // not be forced through the ordinary point-range check (Line uses its length; Cone uses its radius).
            if (skill.Targeting == SkillTargeting.Direction)
                return aim.Direction.X.Raw != 0 || aim.Direction.Z.Raw != 0;
            SimPoint target = aim.Point;
            if (skill.Targeting == SkillTargeting.Unit)
            {
                if (aim.TargetSoldierIndex < 0 || aim.TargetSoldierIndex >= world.Soldiers.Length) return false;
                var t = world.Soldiers[aim.TargetSoldierIndex]; if (!t.Alive) return false;
                target = t.Position;
                bool ally = t.Initial.FactionId == s.Initial.FactionId;
                bool acceptsAllies = skill.TargetAllies || skill.Affects == SkillAffects.Allies || skill.Affects == SkillAffects.AlliesAndSelf;
                if (acceptsAllies != ally) return false;
            }
            return InRange(s.Position, target, skill.Range);
        }

        private int ReadSkillResource(int index, UnitSkillKit kit)
        {
            if (kit == null || kit.Resource == SkillResource.None) return 0;
            if (kit.Resource == SkillResource.Mana || kit.Resource == SkillResource.Energy)
            {
                int elapsed = (int)Math.Max(0, world.Tick - world.SkillResourceStampTick[index]);
                int gained = (elapsed / 20) * kit.RegenPerSecond;
                if (gained > 0) { world.SkillResourceAmount[index] = Math.Min(kit.ResourceMax, world.SkillResourceAmount[index] + gained); world.SkillResourceStampTick[index] += (elapsed / 20) * 20; }
            }
            else if (kit.Resource == SkillResource.Rage)
            {
                if (world.SkillLastCombatTick[index] >= 0)
                {
                    int decayStart = checked(world.SkillLastCombatTick[index] + world.Config.Economy.SkillRageDecayDelayTicks);
                    int applied = Math.Max(world.SkillRageStampTick[index], decayStart);
                    int elapsed = Math.Max(0, checked((int)world.Tick - applied));
                    int seconds = elapsed / 20;
                    if (seconds > 0)
                    {
                        world.SkillRageAmount[index] = Math.Max(0, world.SkillRageAmount[index] - seconds * world.Config.Economy.SkillRageDecayPerSecond);
                        world.SkillRageStampTick[index] = checked(applied + seconds * 20);
                    }
                }
            }
            return kit.Resource == SkillResource.Health ? world.Soldiers[index].Hp : kit.Resource == SkillResource.Rage ? world.SkillRageAmount[index] : world.SkillResourceAmount[index];
        }

        private void WriteSkillResource(int index, UnitSkillKit kit, int value)
        {
            if (kit.Resource == SkillResource.Rage) world.SkillRageAmount[index] = Math.Max(0, value);
            else if (kit.Resource != SkillResource.Health && kit.Resource != SkillResource.None) { world.SkillResourceAmount[index] = Math.Max(0, value); world.SkillResourceStampTick[index] = (int)world.Tick; }
            else if (kit.Resource == SkillResource.Health) world.Soldiers[index].Hp = Math.Max(1, value);
        }

        private bool SkillCannotAct(int index)
            => SkillCcActive(index, SkillCcKind.Stun) || SkillCcActive(index, SkillCcKind.KnockUp) || SkillCcActive(index, SkillCcKind.Silence);

        private bool SkillCcActive(int index, SkillCcKind kind)
            => SkillsOn && world.SkillCcUntil[index * SkillCcCount + (int)kind] > world.Tick;

        private void ProcessSkills()
        {
            if (!SkillsOn) return;
            if (world.Config.Economy.SkillStress && world.Tick % 100 == 0)
                for (int i = 0; i < world.SoldierCount; i++)
                    if (world.Soldiers[i].Alive) TryBeginCast(i, 0, SkillAim.AtPoint(world.Soldiers[i].Position));
            ProcessSkillThinkers();
            for (int position = 0; position < world.SkillCastingCount; )
            {
                int i = world.SkillCastingSoldiers[position];
                if (world.SkillCastId[i] != 0 && (SkillCannotAct(i) || !world.Soldiers[i].Alive))
                {
                    int slot = world.SkillCastSlot[i];
                    long until = world.SkillCooldowns[i * 8 + slot];
                    world.SkillCooldowns[i * 8 + slot] = world.Tick + Math.Max(0, (until - world.Tick) / 2);
                    world.SkillCastId[i] = 0; world.SkillCastTarget[i] = -1;
                    RemoveSkillCastingSoldier(i);
                    continue;
                }
                if (world.Soldiers[i].Alive && world.SkillCastId[i] != 0 && world.SkillCastTick[i] <= world.Tick)
                {
                    var skill = SkillFor(world.SkillCastId[i]);
                    ApplySkill(i, skill, new SkillAim(world.SkillCastTarget[i], world.SkillCastPoint[i], world.SkillCastDirection[i]));
                    world.SkillCastId[i] = 0; world.SkillCastTarget[i] = -1;
                    RemoveSkillCastingSoldier(i);
                    continue;
                }
                position++;
            }
            for (int f = 0; f < world.SkillAreas.Length; f++)
                for (int i = 0; i < world.SkillAreas[f].Length; i++)
                {
                    ref var area = ref world.SkillAreas[f][i];
                    if (!area.Alive || area.NextPulseTick > world.Tick) continue;
                    var skill = SkillFor(area.SkillId); if (skill == null || area.EndTick < world.Tick) { area.Alive = false; continue; }
                    ApplyAreaSkill(area, skill);
                    if (skill.PersistTicks <= 0) area.Alive = false; else area.NextPulseTick = checked(world.Tick + Math.Max(1, skill.PulseIntervalTicks));
                }
        }

        private void ApplySkill(int caster, SkillDefinition skill, SkillAim aim)
        {
            if (skill == null) return;
            RecordSkillCast(caster, skill.Id);
            if (skill.PersistTicks > 0 || skill.Delay > 0)
            {
                int faction = checked((int)world.Soldiers[caster].Initial.FactionId) - 1;
                for (int i = 0; i < world.SkillAreas[faction].Length; i++)
                {
                    ref var area = ref world.SkillAreas[faction][i]; if (area.Alive) continue;
                    area = new SkillAreaState { Alive = true, SkillId = skill.Id, CasterIndex = caster, FactionId = world.Soldiers[caster].Initial.FactionId,
                        Center = skill.Targeting == SkillTargeting.Self ? world.Soldiers[caster].Position : aim.Point,
                        Direction = aim.Direction, StartTick = checked(world.Tick + skill.Delay), NextPulseTick = checked(world.Tick + skill.Delay), EndTick = checked(world.Tick + skill.Delay + Math.Max(0, skill.PersistTicks)) };
                    return;
                }
                return;
            }
            ApplyEffects(caster, skill, aim, world.Soldiers[caster].Position, world.Soldiers[caster].Initial.FactionId);
        }

        private void ApplyAreaSkill(SkillAreaState area, SkillDefinition skill)
        {
            if (area.CasterIndex < 0 || area.CasterIndex >= world.Soldiers.Length || !world.Soldiers[area.CasterIndex].Alive) return;
            ApplyEffects(area.CasterIndex, skill, new SkillAim(-1, area.Center, area.Direction), area.Center, area.FactionId);
        }

        private void ApplyEffects(int caster, SkillDefinition skill, SkillAim aim, SimPoint center, uint faction)
        {
            int hitCount = 0;
            for (int e = 0; e < skill.Effects.Length; e++)
            {
                var effect = skill.Effects[e];
                if (effect == null) continue;
                int count = CollectSkillTargets(caster, skill, aim, center, faction, EffectAffects(skill, effect));
                for (int j = 0; j < count; j++)
                {
                    int target = skillCandidates[j];
                    bool seen = false;
                    for (int h = 0; h < hitCount; h++) if (skillHitTargets[h] == target) { seen = true; break; }
                    if (!seen) skillHitTargets[hitCount++] = target;
                }
                if (effect.Kind == SkillEffectKind.Heal || effect.Kind == SkillEffectKind.Shield || effect.Kind == SkillEffectKind.DamageReduction || effect.Kind == SkillEffectKind.Invulnerable || effect.Kind == SkillEffectKind.Buff || effect.Kind == SkillEffectKind.Debuff || effect.Kind == SkillEffectKind.Camouflage || effect.Kind == SkillEffectKind.CcImmunity || effect.Kind == SkillEffectKind.Cc || effect.Kind == SkillEffectKind.Damage)
                    for (int j = 0; j < count; j++) ApplyEffectTo(caster, skill, effect, skillCandidates[j]);
            }
            if (hitCount > 0) RecordSkillHits(faction, skill.Id, hitCount);
        }

        private SkillAffects EffectAffects(SkillDefinition skill, SkillEffect effect)
            => effect.AppliesTo == SkillEffectAppliesTo.Inherit ? skill.Affects
                : effect.AppliesTo == SkillEffectAppliesTo.Allies ? SkillAffects.Allies : SkillAffects.Enemies;

        private int CollectSkillTargets(int caster, SkillDefinition skill, SkillAim aim, SimPoint center, uint faction, SkillAffects affects)
        {
            if (skill.Targeting == SkillTargeting.Unit && skill.Shape == SkillShape.Single)
            {
                int target = aim.TargetSoldierIndex;
                if (target < 0 || target >= world.Soldiers.Length || !world.Soldiers[target].Alive) return 0;
                var targetSoldier = world.Soldiers[target];
                bool same = targetSoldier.Initial.FactionId == faction;
                if (!AffectsTarget(affects, same, target == caster) || !InRange(center, targetSoldier.Position, skill.Range)) return 0;
                if (!same && world.SkillCamouflageUntil[target] > world.Tick && !InRange(center, targetSoldier.Position, Fix64.FromInt(3))) return 0;
                skillCandidates[0] = target;
                return 1;
            }
            Fix64 range = Fix64.FromInt(Math.Max(1, skill.Radius > 0 ? skill.Radius : skill.Length));
            int enemyFaction = faction == 1 ? 1 : 0;
            bool allies = affects == SkillAffects.Allies || affects == SkillAffects.AlliesAndSelf || skill.TargetAllies;
            int sourceFaction = allies ? checked((int)faction) - 1 : enemyFaction;
            int candidateCount = QuerySpatial(spatialSoldierHeads[sourceFaction], spatialSoldierNext, center, range, 0, skillQueryCandidates);
            int count = 0;
            for (int n = 0; n < candidateCount; n++)
            {
                int i = skillQueryCandidates[n]; var t = world.Soldiers[i]; if (!t.Alive) continue;
                bool same = t.Initial.FactionId == faction;
                if (!AffectsTarget(affects, same, i == caster) || !ShapeContains(skill, center, aim, t.Position)) continue;
                if (!same && world.SkillCamouflageUntil[i] > world.Tick && !InRange(center, t.Position, Fix64.FromInt(3))) continue;
                skillCandidates[count++] = i;
            }
            if ((affects == SkillAffects.Self || affects == SkillAffects.AlliesAndSelf) && count < skillCandidates.Length)
            {
                bool present = false; for (int i = 0; i < count; i++) if (skillCandidates[i] == caster) present = true;
                if (!present) skillCandidates[count++] = caster;
            }
            return count;
        }

        private static bool AffectsTarget(SkillAffects affects, bool same, bool self)
            => affects == SkillAffects.Self ? self : affects == SkillAffects.AlliesAndSelf ? same : affects == SkillAffects.Allies ? same : !same;

        private bool ShapeContains(SkillDefinition skill, SimPoint center, SkillAim aim, SimPoint point)
        {
            long dx = point.X.Raw - center.X.Raw, dz = point.Z.Raw - center.Z.Raw;
            BigInteger d2 = new BigInteger(dx) * dx + new BigInteger(dz) * dz;
            long radius = Fix64.FromInt(Math.Max(1, skill.Radius)).Raw;
            if (skill.Shape == SkillShape.Single) return d2 <= new BigInteger(radius) * radius;
            if (skill.Shape == SkillShape.Circle) return d2 <= new BigInteger(radius) * radius;
            long dirX = aim.Direction.X.Raw, dirZ = aim.Direction.Z.Raw;
            if (dirX == 0 && dirZ == 0) { dirX = point.X.Raw - center.X.Raw; dirZ = point.Z.Raw - center.Z.Raw; }
            BigInteger dot = new BigInteger(dirX) * dx + new BigInteger(dirZ) * dz;
            if (skill.Shape == SkillShape.Line)
            {
                BigInteger len2 = new BigInteger(dirX) * dirX + new BigInteger(dirZ) * dirZ;
                if (len2 == 0) return false;
                BigInteger lengthRaw = Fix64.FromInt(Math.Max(1, skill.Length)).Raw;
                long width = Fix64.FromInt(Math.Max(1, skill.Width)).Raw;
                BigInteger projectionLimit = len2 * lengthRaw * lengthRaw;
                BigInteger perpendicularNumerator = d2 * len2 - dot * dot;
                return dot >= 0 && dot * dot <= projectionLimit && perpendicularNumerator >= 0
                    && perpendicularNumerator * 4 <= new BigInteger(width) * width * len2;
            }
            if (skill.Shape == SkillShape.Cone)
            {
                if (dot <= 0) return false;
                BigInteger dir2 = new BigInteger(dirX) * dirX + new BigInteger(dirZ) * dirZ;
                int angle = Math.Max(1, Math.Min(180, skill.AngleDegrees));
                int cosinePermille = ConeHalfCosPermille[angle - 1];
                return d2 <= new BigInteger(radius) * radius && dot * dot * 1000000 >= d2 * dir2 * cosinePermille * cosinePermille;
            }
            return false;
        }

        private void ApplyEffectTo(int caster, SkillDefinition skill, SkillEffect effect, int target)
        {
            if (target < 0 || target >= world.Soldiers.Length || !world.Soldiers[target].Alive) return;
            ref var t = ref world.Soldiers[target];
            if (effect.Kind == SkillEffectKind.Damage)
            {
                if (IsSkillInvulnerable(target)) return;
                int raw = checked(effect.Base + t.Parameters.Hp * effect.TargetMaxHpPermille / 1000 + (t.Parameters.Hp - t.Hp) * effect.TargetMissingHpPermille / 1000
                    + Math.Max(0, world.Soldiers[caster].Parameters.Damage + SkillBuffAmountFor(caster, SkillStat.AttackDamage)) * effect.AdPermille / 1000
                    + Math.Max(0, world.Soldiers[caster].Parameters.AbilityPower + SkillBuffAmountFor(caster, SkillStat.AbilityPower)) * effect.ApPermille / 1000);
                int armor = Math.Max(0, t.Parameters.Armor + SkillBuffAmountFor(target, SkillStat.Armor));
                int magicResist = Math.Max(0, t.Parameters.MagicResist + SkillBuffAmountFor(target, SkillStat.MagicResist));
                raw = checked(raw * world.Config.Economy.SkillDamagePermille / 1000);
                int damage = effect.DamageType == SkillDamageType.True ? CombatMath.TrueDamage(raw)
                    : CombatMath.DamageAfterDefense(raw, effect.DamageType == SkillDamageType.Magic ? magicResist : armor,
                        effect.DamageType == SkillDamageType.Magic ? world.Soldiers[caster].Parameters.MagicPenetrationPermille : world.Soldiers[caster].Parameters.PhysicalPenetrationPermille,
                        effect.DamageType == SkillDamageType.Magic ? world.Soldiers[caster].Parameters.MagicFlatPenetration : world.Soldiers[caster].Parameters.PhysicalFlatPenetration);
                soldierDamage[target] = checked(soldierDamage[target] + damage); skillDamage[target] = checked(skillDamage[target] + damage);
                RecordSkillDamage(world.Soldiers[caster].Initial.FactionId, skill.Id, damage);
                AddRage(caster, true); AddRage(target, false);
            }
            else if (effect.Kind == SkillEffectKind.Cc) { if (!IsSkillInvulnerable(target)) ApplyCc(caster, skill, target, effect); }
            else if (effect.Kind == SkillEffectKind.Shield) { int value = checked(effect.Base + (world.Soldiers[caster].Parameters.AbilityPower + SkillBuffAmountFor(caster, SkillStat.AbilityPower)) * effect.ApPermille / 1000); if (value > 0) tIndexShield(target, value, checked((int)world.Tick + effect.DurationTicks), skill.Id, world.Soldiers[caster].Initial.FactionId); }
            else if (effect.Kind == SkillEffectKind.Heal)
            {
                int heal = checked(effect.Base + (world.Soldiers[caster].Parameters.AbilityPower + SkillBuffAmountFor(caster, SkillStat.AbilityPower)) * effect.ApPermille / 1000);
                heal = checked(heal * (1000 + world.SkillPassiveHealPermille[target]) / 1000);
                t.Hp = Math.Min(t.Parameters.Hp, checked(t.Hp + heal));
            }
            else if (effect.Kind == SkillEffectKind.DamageReduction) { world.SkillDamageReductionPermille[target] = Math.Max(world.SkillDamageReductionPermille[target], effect.Strength); world.SkillDamageReductionUntil[target] = checked((int)world.Tick + effect.DurationTicks); }
            else if (effect.Kind == SkillEffectKind.Invulnerable) world.SkillInvulnerableUntil[target] = Math.Max(world.SkillInvulnerableUntil[target], checked((int)world.Tick + effect.DurationTicks));
            else if (effect.Kind == SkillEffectKind.Buff || effect.Kind == SkillEffectKind.Debuff) AddSkillBuff(target, effect);
            else if (effect.Kind == SkillEffectKind.Camouflage) world.SkillCamouflageUntil[target] = Math.Max(world.SkillCamouflageUntil[target], checked((int)world.Tick + effect.DurationTicks));
            else if (effect.Kind == SkillEffectKind.CcImmunity)
            {
                int mask = effect.Strength;
                for (int cc = 0; cc < SkillCcCount; cc++)
                    if (mask == 0 || (mask & (1 << cc)) != 0)
                        world.SkillCcImmuneUntil[target * SkillCcCount + cc] = Math.Max(world.SkillCcImmuneUntil[target * SkillCcCount + cc], checked(world.Tick + effect.DurationTicks));
            }
        }

        private void tIndexShield(int target, int amount, int until, int skillId, uint faction)
        { if (world.SkillShieldUntil[target] < world.Tick) world.SkillShieldAmount[target] = 0; world.SkillShieldAmount[target] = checked(world.SkillShieldAmount[target] + amount); world.SkillShieldUntil[target] = Math.Max(world.SkillShieldUntil[target], until); world.SkillShieldSourceId[target] = skillId; world.SkillShieldSourceFaction[target] = faction; }
        private void AddSkillBuff(int target, SkillEffect effect)
        {
            int baseIndex = target * SkillBuffSlots, slot = -1, earliest = int.MaxValue;
            for (int i = 0; i < SkillBuffSlots; i++) { int idx = baseIndex + i; if (world.SkillBuffUntil[idx] <= world.Tick) { slot = i; break; } if (world.SkillBuffUntil[idx] < earliest) { earliest = world.SkillBuffUntil[idx]; slot = i; } }
            int index = baseIndex + slot; world.SkillBuffStat[index] = (byte)effect.Stat; world.SkillBuffAmount[index] = effect.Kind == SkillEffectKind.Debuff ? -effect.Strength : effect.Strength; world.SkillBuffUntil[index] = checked((int)world.Tick + effect.DurationTicks);
        }

        private int SkillBuffAmountFor(int index, SkillStat stat)
        {
            if (!SkillsOn) return 0;
            int sum = 0, start = index * SkillBuffSlots;
            for (int i = 0; i < SkillBuffSlots; i++) if (world.SkillBuffUntil[start + i] > world.Tick && world.SkillBuffStat[start + i] == (byte)stat) sum = checked(sum + world.SkillBuffAmount[start + i]);
            if (stat == SkillStat.AttackDamage) return checked(world.Soldiers[index].Parameters.Damage * sum / 1000);
            return sum;
        }

        private int SkillDamageTakenAmountFor(int index)
        {
            int amount = 0, start = index * SkillBuffSlots;
            for (int i = 0; i < SkillBuffSlots; i++)
                if (world.SkillBuffUntil[start + i] > world.Tick && world.SkillBuffStat[start + i] == (byte)SkillStat.DamageTaken)
                    amount = checked(amount + world.SkillBuffAmount[start + i]);
            return amount;
        }

        private int SkillAttackInterval(int index)
        {
            int bonus = SkillBuffAmountFor(index, SkillStat.AttackSpeed);
            if (bonus <= 0) return world.Soldiers[index].Parameters.AttackIntervalTicks;
            long value = ((long)world.Soldiers[index].Parameters.AttackIntervalTicks * 1000 + 1000 + bonus - 1) / (1000 + bonus);
            return Math.Max(5, checked((int)value));
        }

        private void ApplyCc(int caster, SkillDefinition skill, int target, SkillEffect effect)
        {
            int kind = (int)effect.CcKind; if (kind < 0 || kind >= SkillCcCount) return;
            int idx = target * SkillCcCount + kind; if (world.SkillCcImmuneUntil[idx] > world.Tick) return;
            int duration = effect.DurationTicks;
            if (effect.CcKind != SkillCcKind.KnockUp && effect.CcKind != SkillCcKind.Pull && effect.CcKind != SkillCcKind.Push)
                duration = Math.Max(1, (int)(((long)duration * (1000 - Math.Min(600, Math.Max(0, tTenacity(target))))) / 1000));
            long until = checked(world.Tick + duration);
            if (effect.CcKind == SkillCcKind.Slow) { if (world.SkillCcUntil[idx] <= world.Tick || world.SkillCcStrength[idx] < effect.Strength) world.SkillCcStrength[idx] = effect.Strength; }
            if (until > world.SkillCcUntil[idx]) world.SkillCcUntil[idx] = until;
            RecordSkillCc(world.Soldiers[caster].Initial.FactionId, skill.Id, duration);
            if (effect.CcKind == SkillCcKind.Stun || effect.CcKind == SkillCcKind.Root || effect.CcKind == SkillCcKind.KnockUp || effect.CcKind == SkillCcKind.Taunt || effect.CcKind == SkillCcKind.Fear)
                world.SkillCcImmuneUntil[idx] = until + world.Config.Economy.SkillCcImmuneTicks;
            if (effect.CcKind == SkillCcKind.Taunt || effect.CcKind == SkillCcKind.Fear) world.SkillCcTarget[idx] = caster;
            if (effect.CcKind == SkillCcKind.Pull || effect.CcKind == SkillCcKind.Push)
            {
                var from = world.Soldiers[caster].Position; var to = world.Soldiers[target].Position;
                SimPoint moved;
                if (effect.CcKind == SkillCcKind.Pull)
                {
                    SimPoint stop = FixMath.MoveTowards(from, to, Fix64.FromInt(1));
                    moved = FixMath.MoveTowards(to, stop, Fix64.FromInt(effect.Strength));
                }
                else
                    moved = FixMath.MoveTowards(to, SkillPointAwayFrom(to, from, Fix64.FromInt(effect.Strength)), Fix64.FromInt(effect.Strength));
                world.Soldiers[target].Position = world.Map.ClipMove(to, moved, world.Soldiers[target].Initial.FactionId);
            }
        }

        private int tTenacity(int target) => world.Soldiers[target].Parameters.TenacityPermille;
        private bool IsSkillInvulnerable(int target) => world.SkillInvulnerableUntil[target] > world.Tick;
        private void AddRage(int index, bool dealt)
        {
            MarkSkillCombat(index);
            var kit = KitFor(SkillUnitKind(index)); if (kit == null || kit.Resource != SkillResource.Rage) return;
            ReadSkillResource(index, kit); world.SkillRageAmount[index] = Math.Min(kit.ResourceMax, world.SkillRageAmount[index] + (dealt ? world.Config.Economy.SkillRageOnHitDealt : world.Config.Economy.SkillRageOnHitTaken)); world.SkillLastCombatTick[index] = (int)world.Tick; world.SkillRageStampTick[index] = (int)world.Tick;
        }

        private void MarkSkillCombat(int index)
        {
            if (SkillsOn && index >= 0 && index < world.SkillLastCombatTick.Length)
                world.SkillLastCombatTick[index] = (int)world.Tick;
        }

        private void ApplySkillDamageModifiers(int index)
        {
            if (!SkillsOn || soldierDamage[index] <= 0) return;
            if (IsSkillInvulnerable(index)) { soldierDamage[index] = 0; return; }
            if (world.SkillShieldUntil[index] > world.Tick && world.SkillShieldAmount[index] > 0)
            {
                long blocked = Math.Min(soldierDamage[index], world.SkillShieldAmount[index]); soldierDamage[index] -= blocked; world.SkillShieldAmount[index] -= (int)blocked;
                long skillBlocked = Math.Min(skillDamage[index], blocked); skillDamage[index] -= skillBlocked;
                if (skillBlocked > 0) RecordSkillShieldBlocked(world.SkillShieldSourceFaction[index], world.SkillShieldSourceId[index], skillBlocked);
            }
            if (world.SkillDamageReductionUntil[index] > world.Tick)
            {
                int factor = Math.Max(0, 1000 - world.SkillDamageReductionPermille[index]);
                soldierDamage[index] = soldierDamage[index] * factor / 1000;
                skillDamage[index] = skillDamage[index] * factor / 1000;
            }
            int taken = SkillDamageTakenAmountFor(index);
            if (taken != 0)
            {
                int factor = Math.Max(0, 1000 + taken);
                soldierDamage[index] = soldierDamage[index] * factor / 1000;
                skillDamage[index] = skillDamage[index] * factor / 1000;
            }
        }

        private void AddSkillRageFromNormalAttack(int attacker, int target, int damage)
        {
            if (!SkillsOn || damage <= 0) return; AddRage(attacker, true); AddRage(target, false);
        }

        private bool SkillStopsMovement(int index)
            => SkillCcActive(index, SkillCcKind.Stun) || SkillCcActive(index, SkillCcKind.Root) || SkillCcActive(index, SkillCcKind.KnockUp)
                || SkillCcActive(index, SkillCcKind.Taunt);
        private bool SkillStopsAttack(int index) => SkillCcActive(index, SkillCcKind.Stun) || SkillCcActive(index, SkillCcKind.KnockUp) || SkillCcActive(index, SkillCcKind.Fear);

        internal int SkillResourceForTest(int index) => SkillsOn ? ReadSkillResource(index, KitFor(SkillUnitKind(index))) : 0;
        internal int SkillRageForTest(int index) => SkillsOn ? ReadSkillResource(index, KitFor(SkillUnitKind(index))) : 0;

        private void ProcessSkillThinkers()
        {
            if (world.SkillThinkBuckets == null) return;
            int bucket = (int)(world.Tick % SkillThinkInterval);
            var thinkers = world.SkillThinkBuckets[bucket];
            for (int n = 0; n < thinkers.Length; n++)
            {
                int index = thinkers[n];
                if (index < 0 || index >= world.Soldiers.Length || !world.Soldiers[index].Alive) continue;
                ComputePassive(index);
                if (world.SkillManualUntil[index] > world.Tick || SkillCannotAct(index)) continue;
                ReadSkillResource(index, KitFor(SkillUnitKind(index)));
                TryAutoCast(index);
            }
        }

        private void ComputePassive(int index)
        {
            var kit = KitFor(SkillUnitKind(index));
            world.SkillPassiveArmor[index] = 0; world.SkillPassiveMovePermille[index] = 0; world.SkillPassiveHealPermille[index] = 0;
            world.SkillMomentumPermille[index] = (int)Math.Min(500, Math.Max(0, world.SkillWalkDistanceRaw[index] / Fix64.FromInt(1).Raw * 25));
            if (kit == null) return;
            if (kit.PassiveKind == PassiveKind.Formation)
            {
                int faction = checked((int)world.Soldiers[index].Initial.FactionId) - 1;
                int count = QuerySpatial(spatialSoldierHeads[faction], spatialSoldierNext, world.Soldiers[index].Position, Fix64.FromInt(4), 0, skillQueryCandidates);
                int infantry = 0;
                for (int n = 0; n < count; n++) if (skillQueryCandidates[n] != index && world.Soldiers[skillQueryCandidates[n]].Alive && SkillUnitKind(skillQueryCandidates[n]) == UnitKind.Infantry) infantry++;
                world.SkillPassiveArmor[index] = Math.Min(8, infantry);
            }
            else if (kit.PassiveKind == PassiveKind.Fleet)
                world.SkillPassiveMovePermille[index] = world.SkillLastCombatTick[index] < 0 || world.Tick - world.SkillLastCombatTick[index] > 60 ? 200 : 0;
            else if (kit.PassiveKind == PassiveKind.Prayer)
            {
                int faction = checked((int)world.Soldiers[index].Initial.FactionId) - 1;
                int count = QuerySpatial(spatialSoldierHeads[faction], spatialSoldierNext, world.Soldiers[index].Position, Fix64.FromInt(6), 0, skillQueryCandidates);
                for (int n = 0; n < count; n++) if (skillQueryCandidates[n] != index && world.Soldiers[skillQueryCandidates[n]].Alive && SkillUnitKind(skillQueryCandidates[n]) == UnitKind.Monk) { world.SkillPassiveHealPermille[index] = 100; break; }
            }
        }

        private void TryAutoCast(int index)
        {
            var kit = KitFor(SkillUnitKind(index)); if (kit == null) return;
            int armyIndex = (int)world.Soldiers[index].Initial.ArmyId - 1;
            bool savingForUltimate = false;
            for (int slot = 3; slot >= 0; slot--)
            {
                if (kit.SkillIds == null || slot >= kit.SkillIds.Length || kit.SkillIds[slot] == 0) continue;
                var skill = SkillFor(kit.SkillIds[slot]);
                // Resource-less skills are useful for explicit/manual casts and test fixtures, but have no
                // automatic readiness signal. Live kits all use rage, mana, or energy for auto-casting.
                if (skill == null || skill.Resource == SkillResource.None || world.SkillCooldowns[index * 8 + slot] > world.Tick) continue;
                if (slot == 3)
                {
                    if (armyIndex < 0 || armyIndex >= world.SkillArmySignalUntil.Length) continue;
                    if (world.SkillArmySignalUntil[armyIndex] <= world.Tick && !SkillShapeHasCount(index, skill, 5)) continue;
                    if (world.SkillArmySignalUntil[armyIndex] <= world.Tick) world.SkillArmySignalUntil[armyIndex] = world.Tick + SkillThinkInterval * 2;
                    if (!SkillShapeHasCount(index, skill, 1)) continue;
                }
                else if (IsEnemyAreaSkill(skill) && !SkillShapeHasCount(index, skill, 3)) continue;
                // An ultimate that is off cooldown keeps its price: a lesser skill that would leave less than the
                // ultimate's cost is not cast. Without it rage units spent every point on Q/W/E and never reached
                // the 50 rage of the war cry (0 casts in ten large matches, 11-16).
                // Only while the ultimate is wanted right now (its own conditions passed) and short of resource.
                if (slot < 3 && savingForUltimate && skill.Resource != SkillResource.Health
                    && ReadSkillResource(index, kit) - skill.Cost < UltimateReserve(index, kit)) continue;
                if (!AutoSelfCondition(index, skill)) continue;
                if (!TryChooseAutoAim(index, skill, out SkillAim aim)) continue;
                var failure = TryBeginCast(index, slot, aim);
                if (failure == SkillCastFailure.None) return;
                if (slot == 3 && failure == SkillCastFailure.Resource && UltimateReserve(index, kit) > 0) savingForUltimate = true;
            }
        }

        private bool AutoSelfCondition(int index, SkillDefinition skill)
        {
            // The infantry stance is deliberately conservative: it is for a visible group of three enemies, not
            // for every ordinary skirmish. Smoke is the scout's escape button and only fires at half HP or less.
            if (skill.Id == 3) return VisibleEnemyCount(index, 8) >= 3;
            if (skill.Id == 5) return VisibleEnemyCount(index, world.Soldiers[index].Parameters.Range) >= 1;
            if (skill.Id == 11) return VisibleEnemyCount(index, 8) >= 1;
            if (skill.Id == 13) return world.Soldiers[index].Hp * 2 <= world.Soldiers[index].Parameters.Hp
                && VisibleEnemyCount(index, 8) >= 1;
            return true;
        }

        private int VisibleEnemyCount(int index, int rangeMeters)
            => VisibleEnemyCount(index, Fix64.FromInt(rangeMeters));

        private int VisibleEnemyCount(int index, Fix64 range)
        {
            var s = world.Soldiers[index];
            int enemyFaction = s.Initial.FactionId == 1 ? 1 : 0;
            int count = QuerySpatial(spatialSoldierHeads[enemyFaction], spatialSoldierNext, s.Position,
                range, 0, skillQueryCandidates);
            int found = 0;
            for (int n = 0; n < count; n++)
            {
                int candidate = skillQueryCandidates[n];
                var target = world.Soldiers[candidate];
                bool camouflaged = world.SkillCamouflageUntil[candidate] > world.Tick
                    && !InRange(s.Position, target.Position, Fix64.FromInt(3));
                if (target.Alive && IsVisibleTo(s.Initial.FactionId, target.Position) && !camouflaged)
                    found++;
            }
            return found;
        }

        private bool SkillShapeHasCount(int index, SkillDefinition skill, int minimum)
        {
            var s = world.Soldiers[index];
            bool selfOnly = skill.Affects == SkillAffects.Self;
            bool allies = selfOnly || skill.Affects == SkillAffects.Allies || skill.Affects == SkillAffects.AlliesAndSelf;
            int faction = allies ? checked((int)s.Initial.FactionId) - 1 : s.Initial.FactionId == 1 ? 1 : 0;
            Fix64 range = SkillAutoRange(skill);
            SimPoint center = s.Position;
            SkillAim shapeAim = default(SkillAim);
            if (skill.Targeting == SkillTargeting.Point || skill.Targeting == SkillTargeting.Direction)
            {
                int anchorCount = QuerySpatial(spatialSoldierHeads[faction], spatialSoldierNext, s.Position, range, 0, skillQueryCandidates);
                int anchor = -1;
                for (int n = 0; n < anchorCount; n++)
                {
                    int candidate = skillQueryCandidates[n]; var target = world.Soldiers[candidate];
                    bool same = target.Initial.FactionId == s.Initial.FactionId;
                    if (!target.Alive || selfOnly && candidate != index || !selfOnly && allies != same) continue;
                    if (!allies && !IsVisibleTo(s.Initial.FactionId, target.Position)) continue;
                    if (!allies && world.SkillCamouflageUntil[candidate] > world.Tick && !InRange(s.Position, target.Position, Fix64.FromInt(3))) continue;
                    if (anchor < 0 || SkillDistanceSquared(target.Position, s.Position) < SkillDistanceSquared(world.Soldiers[anchor].Position, s.Position)
                        || SkillDistanceSquared(target.Position, s.Position) == SkillDistanceSquared(world.Soldiers[anchor].Position, s.Position) && target.Initial.Id < world.Soldiers[anchor].Initial.Id)
                        anchor = candidate;
                }
                if (anchor < 0) return false;
                if (skill.Targeting == SkillTargeting.Point) center = world.Soldiers[anchor].Position;
                else shapeAim = SkillAim.Along(SkillDirection(s.Position, world.Soldiers[anchor].Position));
            }
            Fix64 queryRange = skill.Targeting == SkillTargeting.Point ? Fix64.FromInt(Math.Max(1, skill.Radius)) : range;
            int count = QuerySpatial(spatialSoldierHeads[faction], spatialSoldierNext, center, queryRange, 0, skillQueryCandidates);
            int found = 0;
            for (int n = 0; n < count; n++)
            {
                var target = world.Soldiers[skillQueryCandidates[n]];
                bool same = target.Initial.FactionId == s.Initial.FactionId;
                if (!target.Alive || selfOnly && skillQueryCandidates[n] != index || !selfOnly && allies != same) continue;
                if (!same && !IsVisibleTo(s.Initial.FactionId, target.Position)) continue;
                if (!same && world.SkillCamouflageUntil[skillQueryCandidates[n]] > world.Tick && !InRange(s.Position, target.Position, Fix64.FromInt(3))) continue;
                if (skill.Targeting == SkillTargeting.Self && skill.Shape == SkillShape.Single
                    ? InRange(s.Position, target.Position, range)
                    : ShapeContains(skill, center, shapeAim, target.Position)) found++;
            }
            return found >= minimum;
        }

        /// <summary>The cost of the kit's ultimate while it is off cooldown and paid from the same pool; otherwise 0.</summary>
        private int UltimateReserve(int index, UnitSkillKit kit)
        {
            if (kit.SkillIds == null || kit.SkillIds.Length < 4 || kit.SkillIds[3] == 0) return 0;
            var ultimate = SkillFor(kit.SkillIds[3]);
            if (ultimate == null || ultimate.Resource != kit.Resource || ultimate.Resource == SkillResource.None
                || ultimate.Resource == SkillResource.Health || world.SkillCooldowns[index * 8 + 3] > world.Tick) return 0;
            return ultimate.Cost;
        }

        private static bool IsEnemyAreaSkill(SkillDefinition skill)
        {
            if (skill == null || skill.Shape == SkillShape.Single || skill.Affects != SkillAffects.Enemies || skill.Effects == null) return false;
            for (int i = 0; i < skill.Effects.Length; i++)
                if (skill.Effects[i] != null && (skill.Effects[i].Kind == SkillEffectKind.Damage || skill.Effects[i].Kind == SkillEffectKind.Cc)) return true;
            return false;
        }

        private static Fix64 SkillAutoRange(SkillDefinition skill)
        {
            if (skill.Targeting == SkillTargeting.Unit || skill.Targeting == SkillTargeting.Point)
                return skill.Range;
            return Fix64.FromInt(Math.Max(1, skill.Shape == SkillShape.Line ? skill.Length : skill.Radius > 0 ? skill.Radius : 8));
        }

        private bool TryChooseAutoAim(int index, SkillDefinition skill, out SkillAim aim)
        {
            var s = world.Soldiers[index]; aim = SkillAim.AtPoint(s.Position);
            if (skill.Targeting == SkillTargeting.Self)
            {
                if (skill.Affects == SkillAffects.Self)
                {
                    return SkillShapeHasCount(index, skill, skill.Effects != null && skill.Effects.Length > 0 && skill.Effects[0].Kind == SkillEffectKind.Camouflage ? 1 : 1);
                }
                return SkillShapeHasCount(index, skill, 1);
            }
            bool allies = skill.TargetAllies || skill.Affects == SkillAffects.Allies || skill.Affects == SkillAffects.AlliesAndSelf;
            int faction = allies ? checked((int)s.Initial.FactionId) - 1 : s.Initial.FactionId == 1 ? 1 : 0;
            int count = QuerySpatial(spatialSoldierHeads[faction], spatialSoldierNext, s.Position, SkillAutoRange(skill), 0, skillQueryCandidates);
            int best = -1;
            for (int n = 0; n < count; n++)
            {
                int candidate = skillQueryCandidates[n]; var t = world.Soldiers[candidate];
                if (!t.Alive || !InRange(s.Position, t.Position, SkillAutoRange(skill)) || !allies && !IsVisibleTo(s.Initial.FactionId, t.Position)) continue;
                if (!AffectsTarget(skill.Affects, t.Initial.FactionId == s.Initial.FactionId, candidate == index)) continue;
                if (!allies && world.SkillCamouflageUntil[candidate] > world.Tick && !InRange(s.Position, t.Position, Fix64.FromInt(3))) continue;
                if (allies && (skill.Id == 17 || skill.Id == 18)
                    && t.Hp * 10 > t.Parameters.Hp * 7) continue;
                if (skill.Targeting == SkillTargeting.Unit && skill.Effects != null)
                {
                    SkillCcKind ccKind = default(SkillCcKind); bool ccSkill = false;
                    for (int e = 0; e < skill.Effects.Length; e++) if (skill.Effects[e].Kind == SkillEffectKind.Cc) { ccSkill = true; ccKind = skill.Effects[e].CcKind; break; }
                    if (ccSkill && (SkillCcActive(candidate, ccKind) || world.SkillCcImmuneUntil[candidate * SkillCcCount + (int)ccKind] > world.Tick)) continue;
                }
                if (best < 0 || SkillDistanceSquared(t.Position, s.Position) < SkillDistanceSquared(world.Soldiers[best].Position, s.Position) || SkillDistanceSquared(t.Position, s.Position) == SkillDistanceSquared(world.Soldiers[best].Position, s.Position) && t.Initial.Id < world.Soldiers[best].Initial.Id) best = candidate;
            }
            if (best < 0) return false;
            if (skill.Targeting == SkillTargeting.Unit) aim = SkillAim.AtSoldier(best);
            else if (skill.Targeting == SkillTargeting.Direction) aim = SkillAim.Along(SkillDirection(s.Position, world.Soldiers[best].Position));
            else aim = SkillAim.AtPoint(world.Soldiers[best].Position);
            return true;
        }

        private static SimPoint SkillDirection(SimPoint from, SimPoint to)
            => new SimPoint(Fix64.FromRaw(to.X.Raw - from.X.Raw), Fix64.FromRaw(to.Z.Raw - from.Z.Raw));

        private static BigInteger SkillDistanceSquared(SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            return new BigInteger(dx) * dx + new BigInteger(dz) * dz;
        }

        private void AddSkillCastingSoldier(int index)
        {
            for (int i = 0; i < world.SkillCastingCount; i++) if (world.SkillCastingSoldiers[i] == index) return;
            int at = world.SkillCastingCount;
            while (at > 0 && world.SkillCastingSoldiers[at - 1] > index) { world.SkillCastingSoldiers[at] = world.SkillCastingSoldiers[at - 1]; at--; }
            world.SkillCastingSoldiers[at] = index; world.SkillCastingCount++;
        }

        private void RemoveSkillCastingSoldier(int index)
        {
            for (int at = 0; at < world.SkillCastingCount; at++) if (world.SkillCastingSoldiers[at] == index)
            { for (int i = at + 1; i < world.SkillCastingCount; i++) world.SkillCastingSoldiers[i - 1] = world.SkillCastingSoldiers[i]; world.SkillCastingCount--; return; }
        }

        private int SkillStatIndex(uint faction, int skillId)
        { int width = Math.Max(1, world.Config.Skills.Length + 1); return checked(((int)faction - 1) * width + skillId); }
        private void RecordSkillCast(int caster, int skillId) { int i = SkillStatIndex(world.Soldiers[caster].Initial.FactionId, skillId); world.SkillStatCastCount[i]++; }
        private void RecordSkillHits(uint faction, int skillId, int count) { world.SkillStatHitCount[SkillStatIndex(faction, skillId)] += count; }
        private void RecordSkillDamage(uint faction, int skillId, int damage) { world.SkillStatDamage[SkillStatIndex(faction, skillId)] += damage; }
        private void RecordSkillCc(uint faction, int skillId, int ticks) { world.SkillStatCcTicks[SkillStatIndex(faction, skillId)] += ticks; }
        private void RecordSkillShieldBlocked(uint faction, int skillId, long amount) { if (faction != 0 && skillId > 0) world.SkillStatShieldBlocked[SkillStatIndex(faction, skillId)] += amount; }

        internal SkillStatsSnapshot[] CaptureSkillStats()
        {
            if (!SkillsOn) return Array.Empty<SkillStatsSnapshot>();
            var result = new SkillStatsSnapshot[checked(2 * world.Config.Skills.Length)]; int at = 0;
            for (uint faction = 1; faction <= 2; faction++) for (int skill = 1; skill <= world.Config.Skills.Length; skill++)
            { int i = SkillStatIndex(faction, skill); result[at++] = new SkillStatsSnapshot(faction, skill, world.SkillStatCastCount[i], world.SkillStatHitCount[i], world.SkillStatDamage[i], world.SkillStatCcTicks[i], world.SkillStatShieldBlocked[i]); }
            return result;
        }

        private static SimPoint SkillPointAwayFrom(SimPoint point, SimPoint source, Fix64 distance)
        {
            long dx = point.X.Raw - source.X.Raw, dz = point.Z.Raw - source.Z.Raw;
            if (dx == 0 && dz == 0) dx = Fix64.FromInt(1).Raw;
            BigInteger squared = new BigInteger(dx) * dx + new BigInteger(dz) * dz;
            BigInteger length = FixMath.IntegerSqrt(squared); if (length * length < squared) length++;
            long x = (long)(new BigInteger(dx) * distance.Raw / length), z = (long)(new BigInteger(dz) * distance.Raw / length);
            return new SimPoint(Fix64.FromRaw(checked(point.X.Raw + x)), Fix64.FromRaw(checked(point.Z.Raw + z)));
        }
    }
}
