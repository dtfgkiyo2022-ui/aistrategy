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
                var kit = KitFor(i);
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
            for (int i = 0; i < kits.Length; i++) if (kits[i].UnitKind == kind && kits[i].FactionId == 0) return kits[i];
            return null;
        }

        private UnitSkillKit KitFor(int soldierIndex)
        {
            var kind = SkillUnitKind(soldierIndex);
            uint faction = world.Soldiers[soldierIndex].Initial.FactionId;
            var kits = world.Config.UnitSkillKits;
            for (int i = 0; i < kits.Length; i++) if (kits[i].UnitKind == kind && kits[i].FactionId == faction) return kits[i];
            return KitFor(kind);
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

        private int SkillIdFor(int soldierIndex, UnitSkillKit kit, int slot)
        {
            int id = kit.SkillIds[slot];
            if (slot != 3) return id;
            uint faction = world.Soldiers[soldierIndex].Initial.FactionId;
            CivKind civ = world.Economies[faction - 1].Civ;
            UnitKind kind = SkillUnitKind(soldierIndex);
            switch (civ)
            {
                case CivKind.Metallurgy: return kind == UnitKind.IronGuard ? 44 : id;
                case CivKind.Academy: return kind == UnitKind.Magus ? 45 : id;
                case CivKind.Cavalry: return kind == UnitKind.HorseArcher ? 46 : id;
                case CivKind.Cult: return kind == UnitKind.Zealot ? 47 : id;
                case CivKind.Forestry: return kind == UnitKind.ShadowHunter ? 48 : id;
                case CivKind.Masonry: return kind == UnitKind.StoneWarden ? 49 : id;
                default: return id;
            }
        }

        /// <summary>Internal skill entry point. A failed attempt leaves all state unchanged.</summary>
        public SkillCastFailure TryBeginCast(int soldierIndex, int slot, SkillAim aim)
        {
            if (!SkillsOn || soldierIndex < 0 || soldierIndex >= world.Soldiers.Length) return SkillCastFailure.InvalidSoldier;
            ref var soldier = ref world.Soldiers[soldierIndex];
            if (!soldier.Alive) return SkillCastFailure.InvalidSoldier;
            var kit = KitFor(soldierIndex);
            if (kit == null || slot < 0 || slot >= 4 || kit.SkillIds == null || slot >= kit.SkillIds.Length || kit.SkillIds[slot] == 0)
                return SkillCastFailure.NoSkill;
            var skill = SkillFor(SkillIdFor(soldierIndex, kit, slot));
            if (skill == null) return SkillCastFailure.NoSkill;
            if (TrySwapWithClone(soldierIndex, skill)) return SkillCastFailure.None;
            if (world.SkillCastId[soldierIndex] != 0) return SkillCastFailure.Busy;
            if (world.SkillCooldowns[soldierIndex * 8 + slot] > world.Tick) return SkillCastFailure.Cooldown;
            if (SkillCannotAct(soldierIndex)) return SkillCastFailure.CrowdControlled;
            if (!SkillAimValid(soldierIndex, skill, aim)) return SkillCastFailure.InvalidTarget;
            if (!MoveLandingValid(soldierIndex, skill, aim)) return SkillCastFailure.InvalidTarget;
            int factionIndex = checked((int)soldier.Initial.FactionId) - 1;
            if (SkillNeedsProjectile(skill, factionIndex, soldierIndex)) return SkillCastFailure.PoolFull;
            if (SkillNeedsCloneOrSummon(skill, factionIndex)) return SkillCastFailure.PoolFull;
            int resource = ReadSkillResource(soldierIndex, kit);
            if (skill.Resource == SkillResource.Health)
            {
                if (resource - skill.Cost < 1) return SkillCastFailure.Resource;
            }
            else if (skill.Cost > resource) return SkillCastFailure.Resource;
            if (skill.Resource != SkillResource.None) WriteSkillResource(soldierIndex, kit, resource - skill.Cost);
            int cooldownTicks = skill.CooldownTicks;
            if (slot == 3 && HasAugment(soldier.Initial.FactionId, AugmentKind.UltimateChain))
            {
                cooldownTicks = Math.Max(1, checked((cooldownTicks * 65 + 99) / 100));
                augmentUltimateCooldownReductions[soldier.Initial.FactionId - 1]++;
            }
            if (NeutralAzureSkillSpeed(soldierIndex)) cooldownTicks = Math.Max(1, checked((cooldownTicks * 5 + 5) / 6));
            cooldownTicks = GreatSkillCooldown(soldier.Initial.FactionId, cooldownTicks);
            world.SkillCooldowns[soldierIndex * 8 + slot] = checked(world.Tick + cooldownTicks);
            world.SkillCastId[soldierIndex] = skill.Id; world.SkillCastSlot[soldierIndex] = slot;
            world.SkillCastTarget[soldierIndex] = aim.TargetSoldierIndex;
            world.SkillCastPoint[soldierIndex] = aim.Point; world.SkillCastDirection[soldierIndex] = aim.Direction;
            world.SkillActivationCounter = checked(world.SkillActivationCounter + 1);
            int castTicks = skill.CastTicks;
            if (NeutralAzureSkillSpeed(soldierIndex)) castTicks = Math.Max(1, checked((castTicks * 5 + 5) / 6));
            world.SkillCastTick[soldierIndex] = checked(world.Tick + castTicks);
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
                if (HasAugment(world.Soldiers[index].Initial.FactionId, AugmentKind.ManaSpring)) gained = checked(gained * 120 / 100);
                gained = checked(gained * DragonResourcePermille(world.Soldiers[index].Initial.FactionId) / 1000);
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
            // Movement has already happened this tick. Rebuild the existing deterministic spatial index once so
            // combat4 projectiles and clone-origin effects query current cells, not a stale pre-movement snapshot.
            RebuildSpatialCombatIndex();
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
                    var activationAim = new SkillAim(world.SkillCastTarget[i], world.SkillCastPoint[i], world.SkillCastDirection[i]);
                    if (activationAim.TargetSoldierIndex >= 0 && activationAim.TargetSoldierIndex < world.Soldiers.Length)
                        activationAim.Point = world.Soldiers[activationAim.TargetSoldierIndex].Position;
                    ApplySkill(i, skill, activationAim);
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
            ProcessCombat4();
        }

        private void ApplySkill(int caster, SkillDefinition skill, SkillAim aim)
        {
            if (skill == null) return;
            RecordSkillCast(caster, skill.Id);
            world.SkillActivationCounter = checked(world.SkillActivationCounter + 1);
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
            if (skill.MirrorFromClones)
            {
                int cloneSide = checked((int)world.Soldiers[caster].Initial.FactionId) - 1;
                for (int i = 0; i < world.SkillClones[cloneSide].Length; i++)
                {
                    var clone = world.SkillClones[cloneSide][i];
                    if (clone.Alive && clone.OwnerIndex == caster)
                        ApplyEffects(caster, skill, aim, clone.Position, clone.FactionId);
                }
            }
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
                if (effect.Kind == SkillEffectKind.Projectile) { SpawnProjectiles(caster, skill, effect, e, aim, center, faction); continue; }
                if (effect.Kind == SkillEffectKind.Move) { BeginMoveEffect(caster, skill, effect, aim); continue; }
                if (effect.Kind == SkillEffectKind.Clone) { SpawnClones(caster, effect, center); continue; }
                if (effect.Kind == SkillEffectKind.Summon) { SpawnSummons(caster, skill, effect, center); continue; }
                if (ApplySkillEffectToNeutral(caster, skill, effect)) { hitCount++; continue; }
                int count = CollectSkillTargets(caster, skill, aim, center, faction, EffectAffects(skill, effect));
                for (int j = 0; j < count; j++)
                {
                    int target = skillCandidates[j];
                    if (!SkillConditionMet(caster, target, effect)) continue;
                    bool seen = false;
                    for (int h = 0; h < hitCount; h++) if (skillHitTargets[h] == target) { seen = true; break; }
                    if (!seen) skillHitTargets[hitCount++] = target;
                }
                if (effect.Kind == SkillEffectKind.Heal || effect.Kind == SkillEffectKind.Shield || effect.Kind == SkillEffectKind.DamageReduction || effect.Kind == SkillEffectKind.Invulnerable || effect.Kind == SkillEffectKind.Buff || effect.Kind == SkillEffectKind.Debuff || effect.Kind == SkillEffectKind.Camouflage || effect.Kind == SkillEffectKind.CcImmunity || effect.Kind == SkillEffectKind.Cc || effect.Kind == SkillEffectKind.Damage || effect.Kind == SkillEffectKind.Mark)
                    for (int j = 0; j < count; j++) if (SkillConditionMet(caster, skillCandidates[j], effect)) ApplyEffectTo(caster, skill, effect, skillCandidates[j]);
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
                ApplySkillDamageTo(caster, skill, effect, target, 1000);
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
            else if (effect.Kind == SkillEffectKind.Mark) ApplyMark(caster, skill, effect, target);
        }

        private void ApplySkillDamageTo(int caster, SkillDefinition skill, SkillEffect effect, int target, int scalePermille)
        {
            if (IsSkillInvulnerable(target)) return;
            ref var t = ref world.Soldiers[target];
            int raw = checked(effect.Base + t.Parameters.Hp * effect.TargetMaxHpPermille / 1000 + (t.Parameters.Hp - t.Hp) * effect.TargetMissingHpPermille / 1000
                + Math.Max(0, world.Soldiers[caster].Parameters.Damage + SkillBuffAmountFor(caster, SkillStat.AttackDamage)) * effect.AdPermille / 1000
                + Math.Max(0, world.Soldiers[caster].Parameters.AbilityPower + SkillBuffAmountFor(caster, SkillStat.AbilityPower)) * effect.ApPermille / 1000);
            int armor = Math.Max(0, t.Parameters.Armor - AugmentArmorPenalty(target) + SkillBuffAmountFor(target, SkillStat.Armor));
            int magicResist = Math.Max(0, t.Parameters.MagicResist - AugmentArmorPenalty(target) + SkillBuffAmountFor(target, SkillStat.MagicResist));
            raw = checked(raw * world.Config.Economy.SkillDamagePermille / 1000);
            raw = checked(raw * Math.Max(0, scalePermille) / 1000);
            int damage = effect.DamageType == SkillDamageType.True ? CombatMath.TrueDamage(raw)
                : CombatMath.DamageAfterDefense(raw, effect.DamageType == SkillDamageType.Magic ? magicResist : armor,
                    effect.DamageType == SkillDamageType.Magic ? world.Soldiers[caster].Parameters.MagicPenetrationPermille : world.Soldiers[caster].Parameters.PhysicalPenetrationPermille,
                    effect.DamageType == SkillDamageType.Magic ? world.Soldiers[caster].Parameters.MagicFlatPenetration : world.Soldiers[caster].Parameters.PhysicalFlatPenetration);
            if (effect.DamageType == SkillDamageType.Physical) damage = Math.Max(1, damage - world.SkillPassivePhysicalReduction[target]);
            soldierDamage[target] = checked(soldierDamage[target] + damage); skillDamage[target] = checked(skillDamage[target] + damage);
            RecordSkillDamage(world.Soldiers[caster].Initial.FactionId, skill.Id, damage);
            AddRage(caster, true); AddRage(target, false);
            AddDamageToMarks(target, damage);
        }

        private bool SkillConditionMet(int caster, int target, SkillEffect effect)
        {
            switch (effect.Condition)
            {
                case SkillConditionKind.TargetCc:
                    for (int cc = 0; cc < SkillCcCount; cc++) if (world.SkillCcUntil[target * SkillCcCount + cc] > world.Tick) return true;
                    return false;
                case SkillConditionKind.TargetHpBelowPermille:
                    return world.Soldiers[target].Hp * 1000L <= (long)world.Soldiers[target].Parameters.Hp * effect.ConditionValue;
                case SkillConditionKind.NearCasterClone:
                    int cloneSide = checked((int)world.Soldiers[caster].Initial.FactionId) - 1;
                    for (int i = 0; i < world.SkillClones[cloneSide].Length; i++)
                    { var clone = world.SkillClones[cloneSide][i]; if (clone.Alive && clone.OwnerIndex == caster && InRange(clone.Position, world.Soldiers[target].Position, Fix64.FromInt(Math.Max(1, effect.ConditionValue)))) return true; }
                    return false;
                case SkillConditionKind.TargetMarkStacks:
                    int markId = effect.ConditionMarkId > 0 ? effect.ConditionMarkId : effect.ConditionValue;
                    int requiredStacks = effect.ConditionMarkStacks > 0 ? effect.ConditionMarkStacks : 1;
                    return MarkStackCount(target, markId) >= requiredStacks;
                default: return true;
            }
        }

        private int MarkStackCount(int target, int markId)
        {
            if (markId <= 0) return 0;
            int start = target * 4, total = 0;
            for (int i = 0; i < 4; i++) if (world.SkillMarkIds[start + i] == markId && world.SkillMarkUntil[start + i] > world.Tick) total += world.SkillMarkStacks[start + i];
            return total;
        }

        private void ApplyMark(int caster, SkillDefinition skill, SkillEffect effect, int target)
        {
            if (effect.MarkId <= 0) return;
            int start = target * 4, slot = -1;
            for (int i = 0; i < 4; i++) if (world.SkillMarkIds[start + i] == effect.MarkId && world.SkillMarkUntil[start + i] > world.Tick) { slot = i; break; }
            if (slot < 0)
                for (int i = 0; i < 4; i++) if (world.SkillMarkUntil[start + i] <= world.Tick || world.SkillMarkIds[start + i] == 0) { slot = i; break; }
            if (slot < 0) slot = 0; // deterministic fixed four-slot replacement
            int index = start + slot;
            if (world.SkillMarkIds[index] != effect.MarkId) { world.SkillMarkIds[index] = effect.MarkId; world.SkillMarkStacks[index] = 0; world.SkillMarkDamage[index] = 0; }
            world.SkillMarkStacks[index] = Math.Min(Math.Max(1, effect.MarkMaxStacks), world.SkillMarkStacks[index] + 1);
            world.SkillMarkUntil[index] = checked((int)world.Tick + Math.Max(1, effect.MarkDurationTicks));
            world.SkillMarkCaster[index] = caster; world.SkillMarkSkill[index] = skill.Id;
            AddSkillMarkedTarget(target);
            if (effect.MarkTargetEffects != null)
                for (int i = 0; i < effect.MarkTargetEffects.Length; i++)
                    if (effect.MarkTargetEffects[i] != null) ApplyEffectTo(caster, skill, effect.MarkTargetEffects[i], target);
            if (world.SkillMarkStacks[index] >= Math.Max(1, effect.MarkMaxStacks))
            {
                if (effect.MarkBurstEffects != null) for (int i = 0; i < effect.MarkBurstEffects.Length; i++)
                    if (effect.MarkBurstEffects[i] != null) ApplyEffectTo(caster, skill, effect.MarkBurstEffects[i], target);
                if (effect.MarkBurstDamagePermille > 0 && world.SkillMarkDamage[index] > 0)
                {
                    var delayed = new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.Physical, Base = checked(world.SkillMarkDamage[index] * effect.MarkBurstDamagePermille / 1000) };
                    ApplyEffectTo(caster, skill, delayed, target);
                }
                world.SkillMarkIds[index] = 0; world.SkillMarkStacks[index] = 0; world.SkillMarkDamage[index] = 0; world.SkillMarkUntil[index] = 0;
            }
        }

        private void AddDamageToMarks(int target, int damage)
        {
            if (damage <= 0) return;
            int start = target * 4;
            for (int i = 0; i < 4; i++) if (world.SkillMarkIds[start + i] != 0 && world.SkillMarkUntil[start + i] > world.Tick)
                world.SkillMarkDamage[start + i] = checked(world.SkillMarkDamage[start + i] + damage);
        }

        private bool FindFreeProjectile(int faction, out int slot)
        { for (slot = 0; slot < world.SkillProjectiles[faction].Length; slot++) if (!world.SkillProjectiles[faction][slot].Alive) return true; slot = -1; return false; }
        private bool FindFreeClone(int faction, out int slot)
        { for (slot = 0; slot < world.SkillClones[faction].Length; slot++) if (!world.SkillClones[faction][slot].Alive) return true; slot = -1; return false; }
        private bool FindFreeSummon(int faction, out int slot)
        { for (slot = 0; slot < world.SkillSummons[faction].Length; slot++) if (!world.SkillSummons[faction][slot].Alive) return true; slot = -1; return false; }

        private bool SkillNeedsProjectile(SkillDefinition skill, int faction, int caster)
        {
            if (skill.Effects == null) return false;
            for (int i = 0; i < skill.Effects.Length; i++) if (skill.Effects[i] != null && skill.Effects[i].Kind == SkillEffectKind.Projectile)
            {
                int count = Math.Max(1, skill.Effects[i].ProjectileFanCount);
                if (skill.MirrorFromClones) for (int c = 0; c < world.SkillClones[faction].Length; c++) if (world.SkillClones[faction][c].Alive && world.SkillClones[faction][c].OwnerIndex == caster) count += Math.Max(1, skill.Effects[i].ProjectileFanCount);
                int free = 0; for (int p = 0; p < world.SkillProjectiles[faction].Length; p++) if (!world.SkillProjectiles[faction][p].Alive) free++;
                if (free < count) return true;
            }
            return false;
        }

        private bool SkillNeedsCloneOrSummon(SkillDefinition skill, int faction)
        {
            if (skill.Effects == null) return false;
            for (int i = 0; i < skill.Effects.Length; i++)
            {
                var effect = skill.Effects[i]; if (effect == null) continue;
                if (effect.Kind == SkillEffectKind.Clone) { int free = 0; for (int c = 0; c < world.SkillClones[faction].Length; c++) if (!world.SkillClones[faction][c].Alive) free++; if (free < Math.Max(1, effect.CloneCount)) return true; }
                if (effect.Kind == SkillEffectKind.Summon) { int free = 0; for (int c = 0; c < world.SkillSummons[faction].Length; c++) if (!world.SkillSummons[faction][c].Alive) free++; if (free == 0) return true; }
            }
            return false;
        }

        private void SpawnProjectiles(int caster, SkillDefinition skill, SkillEffect effect, int effectIndex, SkillAim aim, SimPoint center, uint faction)
        {
            int side = checked((int)faction) - 1, count = Math.Max(1, effect.ProjectileFanCount);
            SimPoint direction = aim.Direction;
            if (direction.X.Raw == 0 && direction.Z.Raw == 0 && aim.TargetSoldierIndex >= 0 && aim.TargetSoldierIndex < world.Soldiers.Length)
                direction = SkillDirection(center, world.Soldiers[aim.TargetSoldierIndex].Position);
            if (direction.X.Raw == 0 && direction.Z.Raw == 0) direction = new SimPoint(Fix64.FromInt(1), Fix64.FromInt(0));
            for (int n = 0; n < count; n++)
            {
                if (!FindFreeProjectile(side, out int slot)) return;
                var d = FanDirection(direction, n, count, effect.ProjectileFanAngleDegrees);
                world.SkillProjectiles[side][slot] = new SkillProjectileState { Alive = true, FactionId = faction, CasterIndex = caster, SkillId = skill.Id,
                    EffectIndex = effectIndex, PierceCount = effect.ProjectilePierceCount, BounceCount = effect.ProjectileBounceCount, BounceDistance = effect.ProjectileBounceDistance,
                    Position = center, Direction = d, ReturnOrigin = center, Speed = effect.ProjectileSpeed.Raw == 0 ? Fix64.FromInt(1) : effect.ProjectileSpeed,
                    Remaining = Fix64.FromInt(Math.Max(1, effect.ProjectileLength)), IgnoresTerrain = effect.IgnoresTerrain, ActivationId = world.SkillActivationCounter };
            }
        }

        private static SimPoint FanDirection(SimPoint direction, int index, int count, int angleDegrees)
        {
            var d = NormalizeDirection(direction); if (count <= 1 || angleDegrees == 0) return d;
            long px = -d.Z.Raw, pz = d.X.Raw;
            long offset = (long)(index * 2 - (count - 1)) * Math.Max(1, angleDegrees) * 9 / Math.Max(1, (count - 1) * 10);
            return NormalizeDirection(new SimPoint(Fix64.FromRaw(checked(d.X.Raw + px * offset / 1000)), Fix64.FromRaw(checked(d.Z.Raw + pz * offset / 1000))));
        }

        private static SimPoint NormalizeDirection(SimPoint direction)
        {
            long x = direction.X.Raw, z = direction.Z.Raw; if (x == 0 && z == 0) return new SimPoint(Fix64.FromInt(1), Fix64.FromInt(0));
            BigInteger q = new BigInteger(x) * x + new BigInteger(z) * z; BigInteger length = FixMath.IntegerSqrt(q); if (length <= 0) length = 1;
            long one = Fix64.FromInt(1).Raw;
            return new SimPoint(Fix64.FromRaw((long)(new BigInteger(x) * one / length)), Fix64.FromRaw((long)(new BigInteger(z) * one / length)));
        }

        private void SpawnClones(int caster, SkillEffect effect, SimPoint center)
        {
            int side = checked((int)world.Soldiers[caster].Initial.FactionId) - 1, count = Math.Max(1, effect.CloneCount);
            bool extraClone = HasAugment(world.Soldiers[caster].Initial.FactionId, AugmentKind.AnotherShadow);
            if (extraClone) count = checked(count + 1);
            for (int n = 0; n < count; n++)
            {
                if (!FindFreeClone(side, out int slot)) return;
                world.SkillClones[side][slot] = new SkillCloneState { Alive = true, FactionId = world.Soldiers[caster].Initial.FactionId, OwnerIndex = caster,
                    Position = center, EndTick = checked(world.Tick + Math.Max(1, effect.CloneDurationTicks)) };
                if (extraClone && n == count - 1) augmentShadowExtraClones[world.Soldiers[caster].Initial.FactionId - 1]++;
            }
        }

        private void SpawnSummons(int caster, SkillDefinition skill, SkillEffect effect, SimPoint center)
        {
            int side = checked((int)world.Soldiers[caster].Initial.FactionId) - 1;
            if (!FindFreeSummon(side, out int slot)) return;
            world.SkillSummons[side][slot] = new SkillSummonState { Alive = true, FactionId = world.Soldiers[caster].Initial.FactionId, OwnerIndex = caster,
                SkillId = skill.Id, Kind = effect.SummonUnitKind, Hp = Math.Max(1, effect.SummonHp), Damage = Math.Max(0, effect.SummonDamage),
                AttackIntervalTicks = Math.Max(1, effect.SummonAttackIntervalTicks), EndTick = checked(world.Tick + Math.Max(1, effect.SummonDurationTicks)),
                NextAttackTick = world.Tick, Position = center };
        }

        private bool TrySwapWithClone(int caster, SkillDefinition skill)
        {
            if (skill.Effects == null) return false;
            for (int e = 0; e < skill.Effects.Length; e++) if (skill.Effects[e] != null && skill.Effects[e].Kind == SkillEffectKind.Clone)
            {
                int side = checked((int)world.Soldiers[caster].Initial.FactionId) - 1;
                for (int i = 0; i < world.SkillClones[side].Length; i++)
                {
                    ref var clone = ref world.SkillClones[side][i];
                    if (!clone.Alive || clone.OwnerIndex != caster) continue;
                    var original = world.Soldiers[caster].Position; world.Soldiers[caster].Position = clone.Position; clone.Position = original; return true;
                }
            }
            return false;
        }

        private bool MoveLandingValid(int caster, SkillDefinition skill, SkillAim aim)
        {
            if (skill.Effects == null) return true;
            for (int e = 0; e < skill.Effects.Length; e++) if (skill.Effects[e] != null && skill.Effects[e].Kind == SkillEffectKind.Move && skill.Effects[e].MoveKind == SkillMoveKind.Leap)
            {
                SimPoint target = aim.Point; if (aim.TargetSoldierIndex >= 0 && aim.TargetSoldierIndex < world.Soldiers.Length) target = world.Soldiers[aim.TargetSoldierIndex].Position;
                return world.Map.IsPassableFor(world.Map.Cell(target), world.Soldiers[caster].Initial.FactionId);
            }
            return true;
        }

        private void BeginMoveEffect(int caster, SkillDefinition skill, SkillEffect effect, SkillAim aim)
        {
            ref var soldier = ref world.Soldiers[caster];
            SimPoint direction = aim.Direction;
            if (aim.TargetSoldierIndex >= 0 && aim.TargetSoldierIndex < world.Soldiers.Length) direction = SkillDirection(soldier.Position, world.Soldiers[aim.TargetSoldierIndex].Position);
            if (effect.MoveKind == SkillMoveKind.Backstep) direction = new SimPoint(Fix64.FromRaw(-direction.X.Raw), Fix64.FromRaw(-direction.Z.Raw));
            if (direction.X.Raw == 0 && direction.Z.Raw == 0) direction = new SimPoint(Fix64.FromInt(1), Fix64.FromInt(0));
            direction = NormalizeDirection(direction);
            Fix64 distance = effect.MoveDistance.Raw == 0 ? Fix64.FromInt(Math.Max(1, effect.ProjectileLength)) : effect.MoveDistance;
            if (effect.MoveKind == SkillMoveKind.Teleport || effect.MoveKind == SkillMoveKind.Leap)
            {
                SimPoint target = aim.Point;
                if (aim.TargetSoldierIndex >= 0 && aim.TargetSoldierIndex < world.Soldiers.Length) target = world.Soldiers[aim.TargetSoldierIndex].Position;
                soldier.Position = effect.MoveKind == SkillMoveKind.Leap ? target : world.Map.ClipMove(soldier.Position, target, soldier.Initial.FactionId);
                return;
            }
            world.SkillMoveDirection[caster] = direction; world.SkillMoveRemaining[caster] = distance;
            world.SkillMoveSpeed[caster] = effect.MoveSpeed.Raw == 0 ? soldier.StepDistance : effect.MoveSpeed;
            world.SkillMoveSkill[caster] = skill.Id;
            AddSkillMovingSoldier(caster);
        }

        private void ProcessSkillMoves()
        {
            for (int at = 0; at < world.SkillMovingCount; )
            {
                int index = world.SkillMovingSoldiers[at];
                if (index < 0 || index >= world.Soldiers.Length || !world.Soldiers[index].Alive || world.SkillMoveRemaining[index].Raw <= 0) { RemoveSkillMovingSoldier(index); continue; }
                if (SkillStopsMovement(index)) { at++; continue; }
                Fix64 step = world.SkillMoveSpeed[index]; if (step.Raw <= 0) step = world.Soldiers[index].StepDistance;
                if (step.Raw > world.SkillMoveRemaining[index].Raw) step = world.SkillMoveRemaining[index];
                var from = world.Soldiers[index].Position;
                var d = world.SkillMoveDirection[index];
                var requested = new SimPoint(Fix64.FromRaw(checked(from.X.Raw + d.X.Raw * step.Raw / Fix64.FromInt(1).Raw)), Fix64.FromRaw(checked(from.Z.Raw + d.Z.Raw * step.Raw / Fix64.FromInt(1).Raw)));
                var moved = world.Map.ClipMove(from, requested, world.Soldiers[index].Initial.FactionId);
                world.Soldiers[index].Position = moved;
                long dx = moved.X.Raw - from.X.Raw, dz = moved.Z.Raw - from.Z.Raw;
                BigInteger length = FixMath.IntegerSqrt(new BigInteger(dx) * dx + new BigInteger(dz) * dz);
                world.SkillMoveRemaining[index] = Fix64.FromRaw(Math.Max(0, world.SkillMoveRemaining[index].Raw - (long)length));
                if (moved.X.Raw == from.X.Raw && moved.Z.Raw == from.Z.Raw || length == 0) world.SkillMoveRemaining[index] = default(Fix64);
                if (world.SkillMoveRemaining[index].Raw <= 0) { RemoveSkillMovingSoldier(index); continue; }
                HitMovePath(index, from, moved);
                at++;
            }
        }

        private void HitMovePath(int caster, SimPoint from, SimPoint to)
        {
            int skillId = world.SkillMoveSkill[caster]; var skill = SkillFor(skillId); if (skill == null || skill.Effects == null) return;
            int enemySide = world.Soldiers[caster].Initial.FactionId == 1 ? 1 : 0;
            Fix64 query = Fix64.FromInt(2); int count = QuerySpatial(spatialSoldierHeads[enemySide], spatialSoldierNext, to, query, 0, skillQueryCandidates);
            SortSkillCandidatesById(count);
            for (int i = 0; i < count; i++)
            {
                int target = skillQueryCandidates[i]; if (!world.Soldiers[target].Alive || !InRange(to, world.Soldiers[target].Position, query)) continue;
                // A dash advances over several ticks. Hit a target only on the step it first comes within reach, so
                // one dash does not strike the same soldier every tick (11-16 review).
                if (InRange(from, world.Soldiers[target].Position, query)) continue;
                RecordSkillHits(world.Soldiers[caster].Initial.FactionId, skillId, 1);
                for (int e = 0; e < skill.Effects.Length; e++) if (skill.Effects[e] != null && skill.Effects[e].Kind == SkillEffectKind.Move)
                {
                    var effect = skill.Effects[e]; if (effect.Base != 0 || effect.AdPermille != 0 || effect.ApPermille != 0) ApplySkillDamageTo(caster, skill, effect, target, 1000);
                    if (effect.DurationTicks > 0 || effect.CcKind == SkillCcKind.Pull || effect.CcKind == SkillCcKind.Push) ApplyCc(caster, skill, target, effect);
                    if (effect.MoveStopsOnFirstHit) world.SkillMoveRemaining[caster] = default(Fix64);
                }
                if (world.SkillMoveRemaining[caster].Raw <= 0) break;
            }
        }

        private void ProcessSkillProjectiles()
        {
            for (int side = 0; side < 2; side++) for (int p = 0; p < world.SkillProjectiles[side].Length; p++)
            {
                ref var projectile = ref world.SkillProjectiles[side][p]; if (!projectile.Alive) continue;
                var skill = SkillFor(projectile.SkillId); if (skill == null || projectile.CasterIndex < 0 || projectile.CasterIndex >= world.Soldiers.Length) { projectile.Alive = false; continue; }
                var effect = projectile.EffectIndex >= 0 && projectile.EffectIndex < skill.Effects.Length ? skill.Effects[projectile.EffectIndex] : null;
                if (effect == null) { projectile.Alive = false; continue; }
                Fix64 step = projectile.Speed; if (step.Raw > projectile.Remaining.Raw) step = projectile.Remaining;
                var from = projectile.Position;
                var next = new SimPoint(Fix64.FromRaw(checked(from.X.Raw + projectile.Direction.X.Raw * step.Raw / Fix64.FromInt(1).Raw)), Fix64.FromRaw(checked(from.Z.Raw + projectile.Direction.Z.Raw * step.Raw / Fix64.FromInt(1).Raw)));
                if (!projectile.IgnoresTerrain)
                {
                    var clipped = world.Map.ClipMove(from, next, projectile.FactionId);
                    if (clipped.X.Raw != next.X.Raw || clipped.Z.Raw != next.Z.Raw) { projectile.Alive = false; continue; }
                }
                projectile.Position = next; projectile.Remaining = Fix64.FromRaw(Math.Max(0, projectile.Remaining.Raw - step.Raw));
                HitProjectileTargets(ref projectile, p, effect, skill, side);
                if (!projectile.Alive) continue;
                if (projectile.Remaining.Raw <= 0)
                {
                    if (effect.ProjectileReturns && !projectile.Returning && projectile.CasterIndex < world.Soldiers.Length && world.Soldiers[projectile.CasterIndex].Alive)
                    {
                        projectile.Returning = true; projectile.Direction = NormalizeDirection(SkillDirection(projectile.Position, world.Soldiers[projectile.CasterIndex].Position));
                        projectile.Remaining = Fix64.FromInt(Math.Max(1, effect.ProjectileLength)); projectile.HitCount = 0;
                    }
                    else projectile.Alive = false;
                }
                if (projectile.Returning && projectile.CasterIndex < world.Soldiers.Length && InRange(projectile.Position, world.Soldiers[projectile.CasterIndex].Position, projectile.Speed)) projectile.Alive = false;
            }
        }

        private void HitProjectileTargets(ref SkillProjectileState projectile, int projectileSlot, SkillEffect effect, SkillDefinition skill, int side)
        {
            int enemySide = side == 0 ? 1 : 0; Fix64 width = Fix64.FromInt(Math.Max(1, effect.ProjectileWidth));
            int count = QuerySpatial(spatialSoldierHeads[enemySide], spatialSoldierNext, projectile.Position, width, 0, skillQueryCandidates); SortSkillCandidatesById(count);
            for (int i = 0; i < count; i++)
            {
                int target = skillQueryCandidates[i]; if (!world.Soldiers[target].Alive || !InRange(projectile.Position, world.Soldiers[target].Position, width) || ProjectileHasHit(side, projectileSlot, target)) continue;
                int scale = ActivationHitBefore(side, projectile.ActivationId, target) ? 500 : 1000;
                AddProjectileHit(side, projectileSlot, target);
                ApplySkillDamageTo(projectile.CasterIndex, skill, effect, target, scale);
                if (effect.DurationTicks > 0 && effect.CcKind != SkillCcKind.Pull && effect.CcKind != SkillCcKind.Push) ApplyCc(projectile.CasterIndex, skill, target, effect);
                RecordSkillHits(projectile.FactionId, skill.Id, 1); projectile.HitCount++;
                if (projectile.BounceCount > 0)
                {
                    projectile.BounceCount--; projectile.Direction = NormalizeDirection(SkillDirection(projectile.Position, world.Soldiers[target].Position));
                    projectile.Remaining = Fix64.FromInt(Math.Max(1, projectile.BounceDistance));
                }
                if (projectile.PierceCount == 0 || projectile.HitCount >= projectile.PierceCount) { if (effect.ProjectileReturns && !projectile.Returning) { projectile.Returning = true; projectile.HitCount = 0; projectile.Direction = NormalizeDirection(SkillDirection(projectile.Position, world.Soldiers[projectile.CasterIndex].Position)); projectile.Remaining = Fix64.FromInt(Math.Max(1, effect.ProjectileLength)); } else projectile.Alive = false; break; }
            }
        }

        private void SortSkillCandidatesById(int count)
        {
            for (int i = 1; i < count; i++)
            {
                int value = skillQueryCandidates[i], j = i;
                while (j > 0 && world.Soldiers[skillQueryCandidates[j - 1]].Initial.Id > world.Soldiers[value].Initial.Id) { skillQueryCandidates[j] = skillQueryCandidates[j - 1]; j--; }
                skillQueryCandidates[j] = value;
            }
        }

        private int ProjectileHitBase(int side, int slot) => checked((side * 1024 + slot) * 16);
        private bool ProjectileHasHit(int side, int slot, int target)
        { int start = ProjectileHitBase(side, slot); for (int i = 0; i < 16; i++) if (world.SkillProjectileHitTargets[start + i] == target + 1) return true; return false; }
        private void AddProjectileHit(int side, int slot, int target)
        { int start = ProjectileHitBase(side, slot), at = 0; while (at < 16 && world.SkillProjectileHitTargets[start + at] != 0) at++; if (at < 16) world.SkillProjectileHitTargets[start + at] = target + 1; }
        private bool ActivationHitBefore(int side, long activation, int target)
        {
            for (int p = 0; p < world.SkillProjectiles[side].Length; p++)
            { var projectile = world.SkillProjectiles[side][p]; if (projectile.Alive && projectile.ActivationId == activation && ProjectileHasHit(side, p, target)) return true; }
            return false;
        }

        private void ProcessSkillSummons()
        {
            for (int side = 0; side < 2; side++) for (int i = 0; i < world.SkillSummons[side].Length; i++)
            {
                ref var summon = ref world.SkillSummons[side][i]; if (!summon.Alive) continue;
                if (summon.EndTick <= world.Tick || summon.Hp <= 0) { summon.Alive = false; continue; }
                if (summon.NextAttackTick > world.Tick) continue;
                int enemySide = side == 0 ? 1 : 0; int count = QuerySpatial(spatialSoldierHeads[enemySide], spatialSoldierNext, summon.Position, Fix64.FromInt(6), 0, skillQueryCandidates); SortSkillCandidatesById(count);
                int target = -1; BigInteger best = default(BigInteger); uint bestId = uint.MaxValue;
                for (int n = 0; n < count; n++) { int candidate = skillQueryCandidates[n]; if (!world.Soldiers[candidate].Alive) continue; var d = SkillDistanceSquared(summon.Position, world.Soldiers[candidate].Position); if (target < 0 || d < best || d == best && world.Soldiers[candidate].Initial.Id < bestId) { target = candidate; best = d; bestId = world.Soldiers[candidate].Initial.Id; } }
                if (target < 0) continue;
                ref var t = ref world.Soldiers[target]; int damage = CombatMath.DamageAfterDefense(summon.Damage, Math.Max(0, t.Parameters.Armor), 0, 0);
                soldierDamage[target] = checked(soldierDamage[target] + damage); skillDamage[target] = checked(skillDamage[target] + damage);
                RecordSkillDamage(summon.FactionId, summon.SkillId, damage); AddRage(summon.OwnerIndex, true); AddRage(target, false);
                summon.NextAttackTick = checked(world.Tick + summon.AttackIntervalTicks);
            }
        }

        private void ProcessSkillMarks()
        {
            for (int at = 0; at < world.SkillMarkedTargetCount; )
            {
                int target = world.SkillMarkedTargets[at];
                int start = target * 4;
                for (int slot = 0; slot < 4; slot++)
                {
                    int index = start + slot; if (world.SkillMarkIds[index] == 0 || world.SkillMarkUntil[index] > world.Tick) continue;
                    int caster = world.SkillMarkCaster[index], skillId = world.SkillMarkSkill[index]; var skill = SkillFor(skillId);
                    if (skill != null && caster >= 0 && caster < world.Soldiers.Length && world.Soldiers[caster].Alive && world.SkillMarkDamage[index] > 0)
                    {
                        int permille = 0;
                        if (skill.Effects != null) for (int e = 0; e < skill.Effects.Length; e++) if (skill.Effects[e] != null && skill.Effects[e].Kind == SkillEffectKind.Mark) permille = skill.Effects[e].MarkBurstDamagePermille;
                        if (permille > 0) { var delayed = new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.Physical, Base = checked(world.SkillMarkDamage[index] * permille / 1000) }; ApplyEffectTo(caster, skill, delayed, target); }
                    }
                    world.SkillMarkIds[index] = 0; world.SkillMarkStacks[index] = 0; world.SkillMarkUntil[index] = 0; world.SkillMarkDamage[index] = 0; world.SkillMarkCaster[index] = 0; world.SkillMarkSkill[index] = 0;
                }
                bool active = false;
                for (int slot = 0; slot < 4; slot++) if (world.SkillMarkIds[start + slot] != 0 && world.SkillMarkUntil[start + slot] > world.Tick) { active = true; break; }
                if (active) at++; else RemoveSkillMarkedTargetAt(at);
            }
        }

        private void AddSkillMarkedTarget(int target)
        {
            for (int i = 0; i < world.SkillMarkedTargetCount; i++) if (world.SkillMarkedTargets[i] == target) return;
            int at = world.SkillMarkedTargetCount;
            while (at > 0 && world.SkillMarkedTargets[at - 1] > target) { world.SkillMarkedTargets[at] = world.SkillMarkedTargets[at - 1]; at--; }
            world.SkillMarkedTargets[at] = target; world.SkillMarkedTargetCount++;
        }

        private void RemoveSkillMarkedTargetAt(int at)
        {
            for (int i = at + 1; i < world.SkillMarkedTargetCount; i++) world.SkillMarkedTargets[i - 1] = world.SkillMarkedTargets[i];
            world.SkillMarkedTargetCount--;
        }

        private void ProcessCombat4()
        {
            ProcessSkillMoves(); ProcessSkillProjectiles(); ProcessSkillSummons(); ProcessSkillMarks();
            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < world.SkillClones[side].Length; i++) if (world.SkillClones[side][i].Alive && world.SkillClones[side][i].EndTick <= world.Tick) world.SkillClones[side][i].Alive = false;
                for (int i = 0; i < world.SkillSummons[side].Length; i++) if (world.SkillSummons[side][i].Alive && world.SkillSummons[side][i].EndTick <= world.Tick) world.SkillSummons[side][i].Alive = false;
            }
        }

        private void AddSkillMovingSoldier(int index)
        {
            for (int i = 0; i < world.SkillMovingCount; i++) if (world.SkillMovingSoldiers[i] == index) return;
            int at = world.SkillMovingCount; while (at > 0 && world.SkillMovingSoldiers[at - 1] > index) { world.SkillMovingSoldiers[at] = world.SkillMovingSoldiers[at - 1]; at--; }
            world.SkillMovingSoldiers[at] = index; world.SkillMovingCount++;
        }

        private void RemoveSkillMovingSoldier(int index)
        {
            for (int at = 0; at < world.SkillMovingCount; at++) if (world.SkillMovingSoldiers[at] == index)
            { for (int i = at + 1; i < world.SkillMovingCount; i++) world.SkillMovingSoldiers[i - 1] = world.SkillMovingSoldiers[i]; world.SkillMovingCount--; world.SkillMoveSkill[index] = 0; return; }
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
            int bonus = SkillBuffAmountFor(index, SkillStat.AttackSpeed) + world.SkillPassiveAttackSpeed[index];
            if (bonus <= 0) return world.Soldiers[index].Parameters.AttackIntervalTicks;
            long value = ((long)world.Soldiers[index].Parameters.AttackIntervalTicks * 1000 + 1000 + bonus - 1) / (1000 + bonus);
            return Math.Max(5, checked((int)value));
        }

        private void ApplyCc(int caster, SkillDefinition skill, int target, SkillEffect effect)
        {
            int kind = (int)effect.CcKind; if (kind < 0 || kind >= SkillCcCount) return;
            int idx = target * SkillCcCount + kind; if (world.SkillCcImmuneUntil[idx] > world.Tick) return;
            if (HasAugment(world.Soldiers[caster].Initial.FactionId, AugmentKind.ShatteringBlow))
            {
                ref var targetFaction = ref world.Factions[world.Soldiers[target].Initial.FactionId - 1];
                targetFaction.AugmentArmorPenalty[target] = 15;
                targetFaction.AugmentArmorPenaltyUntil[target] = checked(world.Tick + 120);
                augmentShatteringBlowApplications[world.Soldiers[caster].Initial.FactionId - 1]++;
            }
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
            var kit = KitFor(index); if (kit == null || kit.Resource != SkillResource.Rage) return;
            ReadSkillResource(index, kit);
            int amount = dealt ? world.Config.Economy.SkillRageOnHitDealt : world.Config.Economy.SkillRageOnHitTaken;
            if (HasAugment(world.Soldiers[index].Initial.FactionId, AugmentKind.EmberOfRage)) amount = checked(amount * 130 / 100);
            if (HasAugment(world.Soldiers[index].Initial.FactionId, AugmentKind.DoubleRage)) amount = checked(amount * 200 / 100);
            amount = checked(amount * DragonResourcePermille(world.Soldiers[index].Initial.FactionId) / 1000);
            world.SkillRageAmount[index] = Math.Min(kit.ResourceMax, world.SkillRageAmount[index] + amount); world.SkillLastCombatTick[index] = (int)world.Tick; world.SkillRageStampTick[index] = (int)world.Tick;
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

        internal int SkillResourceForTest(int index) => SkillsOn ? ReadSkillResource(index, KitFor(index)) : 0;
        internal int SkillRageForTest(int index) => SkillsOn ? ReadSkillResource(index, KitFor(index)) : 0;

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
                ReadSkillResource(index, KitFor(index));
                TryAutoCast(index);
            }
        }

        private void ComputePassive(int index)
        {
            var kit = KitFor(index);
            world.SkillPassiveArmor[index] = 0; world.SkillPassivePhysicalReduction[index] = 0; world.SkillPassiveMovePermille[index] = 0; world.SkillPassiveHealPermille[index] = 0; world.SkillPassiveAttackSpeed[index] = 0;
            world.SkillMomentumPermille[index] = (int)Math.Min(500, Math.Max(0, world.SkillWalkDistanceRaw[index] / Fix64.FromInt(1).Raw * 25));
            if (kit == null) return;
            if (kit.PassiveKind == PassiveKind.HardSkin)
                world.SkillPassivePhysicalReduction[index] = 5;
            else if (kit.PassiveKind == PassiveKind.Formation)
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
            else if (kit.PassiveKind == PassiveKind.BloodOath)
            {
                int missing = Math.Max(0, world.Soldiers[index].Parameters.Hp - world.Soldiers[index].Hp);
                int lostTwoPercent = (int)((long)missing * 1000 / Math.Max(1, world.Soldiers[index].Parameters.Hp) / 20);
                world.SkillPassiveAttackSpeed[index] = Math.Min(400, lostTwoPercent * 10);
            }
        }

        private void TryAutoCast(int index)
        {
            var kit = KitFor(index); if (kit == null) return;
            int armyIndex = (int)world.Soldiers[index].Initial.ArmyId - 1;
            for (int slot = 3; slot >= 0; slot--)
            {
                if (kit.SkillIds == null || slot >= kit.SkillIds.Length || kit.SkillIds[slot] == 0) continue;
                var skill = SkillFor(SkillIdFor(index, kit, slot));
                // Resource-less skills are useful for explicit/manual casts and test fixtures, but have no
                // automatic readiness signal. Live kits all use rage, mana, or energy for auto-casting.
                if (skill == null || skill.Resource == SkillResource.None || world.SkillCooldowns[index * 8 + slot] > world.Tick) continue;
                if (slot == 3)
                {
                    if (armyIndex < 0 || armyIndex >= world.SkillArmySignalUntil.Length) continue;
                    int ultimateMinimumTargets = skill.Id >= 44 && skill.Id <= 49 ? 1 : 5;
                    if (world.SkillArmySignalUntil[armyIndex] <= world.Tick && !SkillShapeHasCount(index, skill, ultimateMinimumTargets)) continue;
                    if (world.SkillArmySignalUntil[armyIndex] <= world.Tick) world.SkillArmySignalUntil[armyIndex] = world.Tick + SkillThinkInterval * 2;
                    if (!SkillShapeHasCount(index, skill, 1)) continue;
                }
                else if (IsEnemyAreaSkill(skill) && !SkillShapeHasCount(index, skill, 3)) continue;
                if (!AutoSelfCondition(index, skill)) continue;
                if (!TryChooseAutoAim(index, skill, out SkillAim aim)) continue;
                if (TryBeginCast(index, slot, aim) == SkillCastFailure.None) return;
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
            if (skill.Id == 21) return VisibleEnemyCount(index, 8) >= 1;
            if (skill.Id == 34) return world.Soldiers[index].Hp * 5 <= world.Soldiers[index].Parameters.Hp * 3
                && VisibleEnemyCount(index, 4) >= 1;
            if (skill.Id == 41 || skill.Id == 38) return VisibleEnemyCount(index, 8) >= 1;
            if (skill.Id == 43) return world.Soldiers[index].Hp * 4 <= world.Soldiers[index].Parameters.Hp
                && VisibleEnemyCount(index, 5) >= 2;
            if (skill.Resource == SkillResource.Health && world.Soldiers[index].Hp - skill.Cost < world.Soldiers[index].Parameters.Hp * 3 / 10)
                return false;
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
            if (skill.Id == 21 || skill.Id == 34)
            {
                Fix64 range = Fix64.FromInt(skill.Id == 21 ? 8 : 4);
                int enemyFaction = s.Initial.FactionId == 1 ? 1 : 0;
                int nearestCount = QuerySpatial(spatialSoldierHeads[enemyFaction], spatialSoldierNext, s.Position, range, 0, skillQueryCandidates);
                int nearest = -1;
                for (int n = 0; n < nearestCount; n++)
                {
                    int candidate = skillQueryCandidates[n]; var target = world.Soldiers[candidate];
                    if (!target.Alive || !IsVisibleTo(s.Initial.FactionId, target.Position)) continue;
                    if (nearest < 0 || SkillDistanceSquared(target.Position, s.Position) < SkillDistanceSquared(world.Soldiers[nearest].Position, s.Position)
                        || SkillDistanceSquared(target.Position, s.Position) == SkillDistanceSquared(world.Soldiers[nearest].Position, s.Position) && target.Initial.Id < world.Soldiers[nearest].Initial.Id) nearest = candidate;
                }
                if (nearest < 0) return false;
                aim = SkillAim.Along(skill.Id == 34 ? SkillDirection(world.Soldiers[nearest].Position, s.Position) : SkillDirection(s.Position, world.Soldiers[nearest].Position));
                return true;
            }
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
                if (allies && skill.Id == 42 && t.Hp * 2 > t.Parameters.Hp) continue;
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
        private void RecordSkillCast(int caster, int skillId)
        {
            uint faction = world.Soldiers[caster].Initial.FactionId;
            int i = SkillStatIndex(faction, skillId); world.SkillStatCastCount[i]++;
            ref var economy = ref world.Economies[faction - 1];
            switch (skillId)
            {
                case 44: economy.UniqueIronGuardEnhancedCasts++; break;
                case 45: economy.UniqueMagusEnhancedCasts++; break;
                case 46: economy.UniqueHorseArcherEnhancedCasts++; break;
                case 47: economy.UniqueZealotEnhancedCasts++; break;
                case 48: economy.UniqueShadowHunterEnhancedCasts++; break;
                case 49: economy.UniqueStoneWardenEnhancedCasts++; break;
            }
        }
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
