using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed class AugmentFactionDiagnostic
    {
        public uint FactionId;
        public long[] ChoiceOpportunities = Array.Empty<long>();
        public long Rerolls;
        public long AutomaticChoices;
        public long[] CandidateCounts = Array.Empty<long>();
        public long[] SelectedCounts = Array.Empty<long>();
        public long[] AutoScoreSums = Array.Empty<long>();
        public long[] AutoScoreSamples = Array.Empty<long>();
        public long UnyieldingSaves;
        public long ArtisanExtras;
        public long ShatteringBlowApplications;
        public long WarriorsBloodApplications;
        public long ShadowExtraClones;
        public long HunterBonusDamage;
        public long UltimateCooldownReductions;
        public long GuardianKills;
        public long GuardianHuntOrders;
    }

    public sealed class AugmentDiagnosticObservation
    {
        public long Tick;
        public AugmentFactionDiagnostic[] Factions = Array.Empty<AugmentFactionDiagnostic>();
        public long GuardianSpawns;
    }

    public sealed partial class Simulation
    {
        private const int AugmentChoiceCount = 3;
        private const long AugmentChoiceTicks = 1200;
        private const long AugmentAutoPickTicks = 300;
        private bool AugmentsOn => world.Config.Economy.Augments;
        private readonly long[,] augmentChoiceOpportunities = new long[2, 5]; // ages 1..4, guardian = 4
        private readonly long[] augmentRerolls = new long[2];
        private readonly long[] augmentAutomaticChoices = new long[2];
        private readonly long[,] augmentCandidateCounts = new long[2, 22];
        private readonly long[,] augmentSelectedCounts = new long[2, 22];
        private readonly long[,] augmentAutoScoreSums = new long[2, 22];
        private readonly long[,] augmentAutoScoreSamples = new long[2, 22];
        private readonly long[] augmentUnyieldingSaves = new long[2];
        private readonly long[] augmentArtisanExtras = new long[2];
        private readonly long[] augmentShatteringBlowApplications = new long[2];
        private readonly long[] augmentWarriorsBloodApplications = new long[2];
        private readonly long[] augmentShadowExtraClones = new long[2];
        private readonly long[] augmentHunterBonusDamage = new long[2];
        private readonly long[] augmentUltimateCooldownReductions = new long[2];
        private readonly long[] augmentGuardianKills = new long[2];
        private readonly long[] augmentGuardianHuntOrders = new long[2];
        private long augmentGuardianSpawns;

        private void EnsureAugmentSoldierArrays()
        {
            if (!AugmentsOn) return;
            int length = world.Soldiers.Length;
            for (int i = 0; i < world.Factions.Length; i++)
            {
                var state = world.Factions[i];
                if (state.AugmentUnyieldingUsed.Length < length) Array.Resize(ref state.AugmentUnyieldingUsed, length);
                if (state.AugmentUnyieldingUntil.Length < length) Array.Resize(ref state.AugmentUnyieldingUntil, length);
                if (state.AugmentArmorPenalty.Length < length) Array.Resize(ref state.AugmentArmorPenalty, length);
                if (state.AugmentArmorPenaltyUntil.Length < length) Array.Resize(ref state.AugmentArmorPenaltyUntil, length);
                if (state.AugmentWarriorsBloodUntil.Length < length) Array.Resize(ref state.AugmentWarriorsBloodUntil, length);
                world.Factions[i] = state;
            }
        }

        private static readonly AugmentKind[] AugmentPool =
        {
            AugmentKind.ForgedEdge, AugmentKind.ThickHide, AugmentKind.SwiftFeet,
            AugmentKind.RichFields, AugmentKind.WoodcutterInstinct, AugmentKind.ManaSpring,
            AugmentKind.EmberOfRage, AugmentKind.RapidTraining, AugmentKind.ShatteringBlow,
            AugmentKind.AnotherShadow, AugmentKind.WarriorsBlood, AugmentKind.DoubleRage,
            AugmentKind.WorkshopHeat, AugmentKind.ArtisansHand, AugmentKind.Ironwall,
            AugmentKind.Hunter, AugmentKind.UltimateChain,
            AugmentKind.Unyielding, AugmentKind.AgeOfAbundance, AugmentKind.WarGod
        };

        private void InitializeAugmentsRuntime()
        {
            if (!AugmentsOn) return;
            for (int i = 0; i < world.SoldierCount; i++)
                for (int j = 0; j < world.Factions[world.Soldiers[i].Initial.FactionId - 1].AugmentOwnedCount; j++)
                    ApplyAugmentToSoldier(ref world.Soldiers[i], world.Factions[world.Soldiers[i].Initial.FactionId - 1].OwnedAugments[j]);
        }

        private void ApplyAugmentToSoldier(ref SoldierState soldier, AugmentKind kind)
        {
            if (kind == AugmentKind.ForgedEdge) soldier.Parameters.Damage = Math.Max(1, checked((soldier.Parameters.Damage * 105 + 99) / 100));
            else if (kind == AugmentKind.ThickHide)
            {
                int hp = Math.Max(1, checked((soldier.Parameters.Hp * 106 + 99) / 100));
                soldier.Parameters.Hp = hp; soldier.Hp = Math.Min(hp, checked((soldier.Hp * 106 + 99) / 100)); soldier.Initial.Hp = hp;
            }
            else if (kind == AugmentKind.SwiftFeet)
            {
                soldier.Parameters.Speed = Fix64.FromRaw(checked(soldier.Parameters.Speed.Raw * 106 / 100));
                soldier.StepDistance = MovementMath.StepDistance(soldier.Parameters.Speed, world.Config.Economy);
            }
            else if (kind == AugmentKind.WarGod)
            {
                soldier.Parameters.Damage = Math.Max(1, checked((soldier.Parameters.Damage * 112 + 99) / 100));
                int hp = Math.Max(1, checked((soldier.Parameters.Hp * 112 + 99) / 100));
                soldier.Parameters.Hp = hp; soldier.Hp = Math.Min(hp, checked((soldier.Hp * 112 + 99) / 100)); soldier.Initial.Hp = hp;
            }
        }

        private void ApplyAugmentToFaction(uint faction, AugmentKind kind)
        {
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction) ApplyAugmentToSoldier(ref world.Soldiers[i], kind);
            if (kind == AugmentKind.Ironwall)
                for (int i = 0; i < world.BuildingCount; i++)
                    if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction
                        && (world.Buildings[i].Kind == BuildingKind.Wall || world.Buildings[i].Kind == BuildingKind.Tower))
                        world.Buildings[i].Hp = checked(world.Buildings[i].Hp * 125 / 100);
        }

        private int AugmentBuildingHp(uint faction, BuildingKind kind, int hp)
        {
            return HasAugment(faction, AugmentKind.Ironwall)
                && (kind == BuildingKind.Wall || kind == BuildingKind.Tower)
                ? checked(hp * 125 / 100) : hp;
        }

        private void ApplyAugmentToNewSoldier(int index)
        {
            if (!AugmentsOn || index < 0 || index >= world.Soldiers.Length) return;
            uint faction = world.Soldiers[index].Initial.FactionId;
            ref var state = ref world.Factions[faction - 1];
            for (int i = 0; i < state.AugmentOwnedCount; i++) ApplyAugmentToSoldier(ref world.Soldiers[index], state.OwnedAugments[i]);
        }

        private AugmentGrade AugmentGradeFor(int age, int eventSerial)
        {
            var random = new SplitMix64(world.Config.Seed ^ (ulong)(0xC10A0000 + age * 257 + eventSerial * 17));
            int roll = (int)random.NextUInt64(100);
            return roll < 50 ? AugmentGrade.Silver : (roll < 85 ? AugmentGrade.Gold : AugmentGrade.Rainbow);
        }

        private void BeginAugmentChoice(uint faction, AugmentGrade grade, bool guardian = false)
            => BeginAugmentChoiceInternal(faction, grade, guardian, true);

        private void BeginAugmentChoiceInternal(uint faction, AugmentGrade grade, bool guardian, bool countOpportunity)
        {
            if (!AugmentsOn || faction < 1 || faction > 2) return;
            ref var state = ref world.Factions[faction - 1];
            int factionIndex = checked((int)faction - 1);
            if (countOpportunity)
            {
                int opportunity = guardian ? 4 : world.Economies[factionIndex].Age - 1;
                if (opportunity >= 0 && opportunity < 5) augmentChoiceOpportunities[factionIndex, opportunity]++;
            }
            int serial = state.AugmentChoiceSerial++;
            var random = new SplitMix64(world.Config.Seed ^ (ulong)(faction * 0x9E3779B9U) ^ (ulong)(serial * 0x85EBCA6BU));
            for (int i = 0; i < state.AugmentChoices.Length; i++) state.AugmentChoices[i] = AugmentKind.None;
            int cursor = (int)random.NextUInt64((ulong)AugmentPool.Length);
            int placed = 0;
            // The first card is the civilisation-fit card when the grade has one. The remaining
            // cards are ordinary cards of the same grade. The scan order is seed-derived and
            // does not depend on a dictionary or a process-local random source.
            for (int pass = 0; pass < AugmentPool.Length * 2 && placed == 0; pass++)
            {
                AugmentKind candidate = AugmentPool[(cursor + pass) % AugmentPool.Length];
                if (AugmentGradeOf(candidate) != grade || OwnsAugment(state, candidate) || !AugmentSuitable(candidate, faction)) continue;
                state.AugmentChoices[placed++] = candidate;
            }
            for (int pass = 0; pass < AugmentPool.Length * 2 && placed < AugmentChoiceCount; pass++)
            {
                AugmentKind candidate = AugmentPool[(cursor + pass) % AugmentPool.Length];
                if (AugmentGradeOf(candidate) != grade || OwnsAugment(state, candidate)) continue;
                bool duplicate = false;
                for (int j = 0; j < placed; j++) if (state.AugmentChoices[j] == candidate) duplicate = true;
                if (duplicate) continue;
                state.AugmentChoices[placed++] = candidate;
            }
            state.AugmentGrade = grade;
            state.AugmentChoiceDeadlineTick = world.Tick + AugmentChoiceTicks;
            state.AugmentRerollsRemaining = 1;
            for (int i = 0; i < state.AugmentChoices.Length; i++)
            {
                int kind = (int)state.AugmentChoices[i];
                if (kind > 0 && kind < 22) augmentCandidateCounts[factionIndex, kind]++;
            }
        }

        private bool AugmentSuitable(AugmentKind candidate, uint faction)
        {
            if (candidate == AugmentKind.None || faction < 1 || faction > 2) return false;
            CivKind civ = world.Economies[faction - 1].Civ;
            if (civ == CivKind.Primitive) return candidate == AugmentKind.ForgedEdge || candidate == AugmentKind.ThickHide || candidate == AugmentKind.SwiftFeet;
            if (civ == CivKind.Agrarian) return candidate == AugmentKind.RichFields || candidate == AugmentKind.ManaSpring || candidate == AugmentKind.AgeOfAbundance;
            if (civ == CivKind.Metallurgy) return candidate == AugmentKind.ForgedEdge || candidate == AugmentKind.WorkshopHeat || candidate == AugmentKind.WarGod;
            if (civ == CivKind.Forestry) return candidate == AugmentKind.WoodcutterInstinct || candidate == AugmentKind.SwiftFeet || candidate == AugmentKind.Hunter;
            if (civ == CivKind.Masonry) return candidate == AugmentKind.Ironwall || candidate == AugmentKind.ThickHide || candidate == AugmentKind.ShatteringBlow;
            if (civ == CivKind.Caravan) return candidate == AugmentKind.RichFields || candidate == AugmentKind.DoubleRage || candidate == AugmentKind.ArtisansHand;
            if (civ == CivKind.Cavalry) return candidate == AugmentKind.SwiftFeet || candidate == AugmentKind.WarGod || candidate == AugmentKind.Hunter;
            if (civ == CivKind.Academy) return candidate == AugmentKind.ManaSpring || candidate == AugmentKind.UltimateChain || candidate == AugmentKind.AnotherShadow;
            if (civ == CivKind.Cult) return candidate == AugmentKind.EmberOfRage || candidate == AugmentKind.Unyielding || candidate == AugmentKind.WarriorsBlood;
            return candidate == AugmentKind.RapidTraining || candidate == AugmentKind.ArtisansHand || candidate == AugmentKind.WarGod;
        }

        private static AugmentGrade AugmentGradeOf(AugmentKind kind)
        {
            if (kind >= AugmentKind.ForgedEdge && kind <= AugmentKind.RapidTraining) return AugmentGrade.Silver;
            if (kind >= AugmentKind.ShatteringBlow && kind <= AugmentKind.Hunter) return AugmentGrade.Gold;
            if (kind >= AugmentKind.UltimateChain && kind <= AugmentKind.WarGod) return AugmentGrade.Rainbow;
            return AugmentGrade.None;
        }

        private static bool OwnsAugment(FactionState state, AugmentKind kind)
        {
            if (state.OwnedAugments == null) return false;
            for (int i = 0; i < state.AugmentOwnedCount; i++) if (state.OwnedAugments[i] == kind) return true;
            return false;
        }

        private void AugmentThink()
        {
            if (!AugmentsOn) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                ref var state = ref world.Factions[faction - 1];
                if (state.AugmentGrade == AugmentGrade.None || state.AugmentChoiceDeadlineTick <= 0) continue;
                // C10.2: with the automatic economy on, the AI picks after AugmentAutoPickTicks (a human on the same
                // side still has that long to pick a card); with it off, the 1200-tick timeout of C10.1 picks.
                long due = world.Economies[faction - 1].AutoOff ? state.AugmentChoiceDeadlineTick
                    : state.AugmentChoiceDeadlineTick - AugmentChoiceTicks + AugmentAutoPickTicks;
                if (world.Tick < due) continue;
                int best = ChooseAugmentIndex(faction);
                if (AugmentScore(faction, state.AugmentChoices[best]) == 0 && state.AugmentRerollsRemaining > 0)
                {
                    state.AugmentRerollsRemaining = 0;
                    augmentRerolls[faction - 1]++;
                    BeginAugmentChoiceInternal(faction, state.AugmentGrade, false, false);
                    state.AugmentRerollsRemaining = 0;
                    continue;
                }
                TakeAugment(faction, best, true);
            }
        }

        private int ChooseAugmentIndex(uint faction)
        {
            ref var state = ref world.Factions[faction - 1];
            int best = 0, bestScore = int.MinValue;
            for (int i = 0; i < state.AugmentChoices.Length; i++)
            {
                int score = AugmentScore(faction, state.AugmentChoices[i]);
                int kind = (int)state.AugmentChoices[i];
                if (kind > 0 && kind < 22)
                {
                    augmentAutoScoreSums[faction - 1, kind] = checked(augmentAutoScoreSums[faction - 1, kind] + score);
                    augmentAutoScoreSamples[faction - 1, kind]++;
                }
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        private int AugmentScore(uint faction, AugmentKind kind)
        {
            return kind == AugmentKind.None ? 0 : AugmentScoreForAuto(faction, kind);
        }

        private bool ApplyChooseAugment(EconomyCommand command)
        {
            if (!AugmentsOn || command.FactionId < 1 || command.FactionId > 2) return false;
            ref var state = ref world.Factions[command.FactionId - 1];
            if (world.Tick >= state.AugmentChoiceDeadlineTick || command.AugmentIndex < 0 || command.AugmentIndex >= AugmentChoiceCount) return false;
            if (state.AugmentChoices[command.AugmentIndex] == AugmentKind.None) return false;
            TakeAugment(command.FactionId, command.AugmentIndex, false);
            return true;
        }

        private bool ApplyRerollAugment(EconomyCommand command)
        {
            if (!AugmentsOn || command.FactionId < 1 || command.FactionId > 2) return false;
            ref var state = ref world.Factions[command.FactionId - 1];
            if (state.AugmentRerollsRemaining == 0 || world.Tick >= state.AugmentChoiceDeadlineTick) return false;
            state.AugmentRerollsRemaining--;
            augmentRerolls[command.FactionId - 1]++;
            AugmentKind old0 = state.AugmentChoices[0];
            AugmentKind old1 = state.AugmentChoices[1];
            AugmentKind old2 = state.AugmentChoices[2];
            int attempts = 0;
            do
            {
                BeginAugmentChoiceInternal(command.FactionId, state.AugmentGrade, false, false);
                attempts++;
            }
            while (attempts < AugmentPool.Length && old0 == state.AugmentChoices[0] && old1 == state.AugmentChoices[1] && old2 == state.AugmentChoices[2]);
            state.AugmentRerollsRemaining = 0;
            return true;
        }

        private void TakeAugment(uint faction, int index, bool automatic)
        {
            ref var state = ref world.Factions[faction - 1];
            AugmentKind kind = state.AugmentChoices[index];
            if (kind == AugmentKind.None || OwnsAugment(state, kind)) return;
            if (state.AugmentOwnedCount < state.OwnedAugments.Length) state.OwnedAugments[state.AugmentOwnedCount++] = kind;
            int factionIndex = checked((int)faction - 1), kindIndex = (int)kind;
            if (kindIndex > 0 && kindIndex < 22) augmentSelectedCounts[factionIndex, kindIndex]++;
            if (automatic) augmentAutomaticChoices[factionIndex]++;
            ApplyAugmentToFaction(faction, kind);
            for (int i = 0; i < state.AugmentChoices.Length; i++) state.AugmentChoices[i] = AugmentKind.None;
            state.AugmentGrade = AugmentGrade.None;
            state.AugmentChoiceDeadlineTick = 0;
            state.AugmentRerollsRemaining = 0;
        }

        private void AugmentAgeReached(uint faction)
        {
            if (!AugmentsOn || faction < 1 || faction > 2) return;
            int age = world.Economies[faction - 1].Age;
            if (age >= 1 && age <= 4) BeginAugmentChoice(faction, AugmentGradeFor(age, age));
        }

        private void AugmentGuardianReward(uint faction)
        {
            if (AugmentsOn) BeginAugmentChoice(faction, AugmentGrade.Gold, true);
        }

        private bool HasAugment(uint faction, AugmentKind kind)
        {
            return AugmentsOn && faction >= 1 && faction <= 2 && OwnsAugment(world.Factions[faction - 1], kind);
        }

        private IReadOnlyList<AugmentKind> AugmentChoicesFor(uint faction)
        {
            if (!AugmentsOn || faction < 1 || faction > 2) return Array.Empty<AugmentKind>();
            return world.Factions[faction - 1].AugmentChoices ?? Array.Empty<AugmentKind>();
        }

        private IReadOnlyList<AugmentKind> OwnedAugmentsFor(uint faction)
        {
            if (!AugmentsOn || faction < 1 || faction > 2) return Array.Empty<AugmentKind>();
            ref var state = ref world.Factions[faction - 1];
            var result = new AugmentKind[state.AugmentOwnedCount];
            for (int i = 0; i < result.Length; i++) result[i] = state.OwnedAugments[i];
            return result;
        }

        private AugmentGrade AugmentGradeForFrame(uint faction) => AugmentsOn ? world.Factions[faction - 1].AugmentGrade : AugmentGrade.None;
        private long AugmentTicksForFrame(uint faction)
        {
            if (!AugmentsOn) return 0;
            long deadline = world.Factions[faction - 1].AugmentChoiceDeadlineTick;
            return deadline > world.Tick ? deadline - world.Tick : 0;
        }
        private int AugmentRerollsForFrame(uint faction) => AugmentsOn ? world.Factions[faction - 1].AugmentRerollsRemaining : 0;

        private int AugmentSoldierCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction) count++;
            return count;
        }

        private int AugmentBuildingCount(uint faction, BuildingKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsActualBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == kind) count++;
            return count;
        }

        private int HunterNeutralDamage(uint faction, int damage)
        {
            if (!HasAugment(faction, AugmentKind.Hunter) || damage <= 0) return damage;
            int boosted = checked(damage * 130 / 100);
            augmentHunterBonusDamage[faction - 1] = checked(augmentHunterBonusDamage[faction - 1] + boosted - damage);
            return boosted;
        }

        private int AugmentResourceCount(ResourceKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
                if (world.Nodes[i].Definition.Kind == kind && world.Nodes[i].Remaining > 0) count++;
            return count;
        }

        private bool AugmentIsEconomyEffect(AugmentKind kind)
        {
            return kind == AugmentKind.RichFields || kind == AugmentKind.WoodcutterInstinct
                || kind == AugmentKind.WorkshopHeat || kind == AugmentKind.ArtisansHand
                || kind == AugmentKind.Ironwall || kind == AugmentKind.AgeOfAbundance;
        }

        private int AugmentScoreForAuto(uint faction, AugmentKind kind)
        {
            if (kind == AugmentKind.None) return 0;
            int soldiers = AugmentSoldierCount(faction);
            int score;
            switch (kind)
            {
                case AugmentKind.ForgedEdge:
                case AugmentKind.ThickHide:
                case AugmentKind.SwiftFeet:
                case AugmentKind.EmberOfRage:
                case AugmentKind.AnotherShadow:
                case AugmentKind.WarriorsBlood:
                case AugmentKind.DoubleRage:
                case AugmentKind.UltimateChain:
                case AugmentKind.Unyielding:
                case AugmentKind.WarGod:
                    score = soldiers / 2; break;
                case AugmentKind.RichFields: score = AugmentBuildingCount(faction, BuildingKind.Farm); break;
                case AugmentKind.WoodcutterInstinct: score = AugmentResourceCount(ResourceKind.Wood); break;
                case AugmentKind.ManaSpring:
                case AugmentKind.RapidTraining:
                case AugmentKind.ShatteringBlow:
                    score = soldiers; break;
                case AugmentKind.WorkshopHeat: score = 0;
                    for (int i = 0; i < world.BuildingCount; i++)
                        if (IsActualBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction) score++;
                    break;
                case AugmentKind.ArtisansHand: score = AugmentBuildingCount(faction, BuildingKind.Assembler); break;
                case AugmentKind.Ironwall: score = AugmentBuildingCount(faction, BuildingKind.Wall) + AugmentBuildingCount(faction, BuildingKind.Tower); break;
                case AugmentKind.Hunter: score = world.NeutralCamps == null ? 0 : world.NeutralCamps.Length; break;
                case AugmentKind.AgeOfAbundance: score = AugmentResourceCount(ResourceKind.Food) + AugmentResourceCount(ResourceKind.Wood)
                    + AugmentResourceCount(ResourceKind.Ore) + AugmentResourceCount(ResourceKind.Stone); break;
                default: score = 0; break;
            }
            if (AugmentIsEconomyEffect(kind) && world.Economies[faction - 1].Age <= 2) score = checked(score + 5);
            return score;
        }

        private int WarriorsBloodDamage(int attackerIndex, int damage)
        {
            if (!AugmentsOn || attackerIndex < 0 || attackerIndex >= world.Soldiers.Length) return damage;
            uint faction = world.Soldiers[attackerIndex].Initial.FactionId;
            if (!HasAugment(faction, AugmentKind.WarriorsBlood) || world.Factions[faction - 1].AugmentWarriorsBloodUntil[attackerIndex] <= world.Tick) return damage;
            return checked(damage * 115 / 100);
        }

        private void AugmentVillagerDeath(uint faction, SimPoint position)
        {
            if (!HasAugment(faction, AugmentKind.WarriorsBlood)) return;
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction
                    && InRange(world.Soldiers[i].Position, position, Fix64.FromInt(6)))
                {
                    world.Factions[faction - 1].AugmentWarriorsBloodUntil[i] = checked(world.Tick + 200);
                    augmentWarriorsBloodApplications[faction - 1]++;
                }
        }

        private void RecordArtisanProduction(ref BuildingState building, ref FactionEconomy economy)
        {
            if (!HasAugment(building.FactionId, AugmentKind.ArtisansHand) || building.Kind != BuildingKind.Assembler) return;
            ref var state = ref world.Factions[building.FactionId - 1];
            state.AugmentArtisanProductionCount = checked(state.AugmentArtisanProductionCount + 1);
            if ((state.AugmentArtisanProductionCount % 4) != 0) return;
            augmentArtisanExtras[building.FactionId - 1]++;
            switch (building.Recipe)
            {
                case RecipeKind.Frames: economy.Frames++; economy.FramesProduced++; break;
                case RecipeKind.ArmsKit: if (building.InputSecondaryKind == ResourceKind.Steel) { economy.FineArmsKit++; economy.FineArmsKitProduced++; } else { economy.ArmsKit++; economy.ArmsKitProduced++; } break;
                case RecipeKind.Scholarly: economy.Scholarly++; economy.ScholarlyProduced++; break;
                case RecipeKind.SiegeParts: economy.SiegeParts++; economy.SiegePartsProduced++; break;
                case RecipeKind.Mechanism: economy.Mechanism++; economy.MechanismProduced++; break;
                case RecipeKind.SiegeEngine: economy.SiegeEngine++; economy.SiegeEngineProduced++; break;
                case RecipeKind.EliteKit: economy.EliteKit++; economy.EliteKitProduced++; break;
                case RecipeKind.Lens: economy.Lens++; economy.LensProduced++; break;
                case RecipeKind.Precision: economy.Precision++; economy.PrecisionProduced++; break;
            }
        }

        public AugmentDiagnosticObservation CaptureAugmentDiagnostic()
        {
            var factions = new AugmentFactionDiagnostic[2];
            for (int f = 0; f < factions.Length; f++)
            {
                var opportunities = new long[5];
                for (int i = 0; i < opportunities.Length; i++) opportunities[i] = augmentChoiceOpportunities[f, i];
                var candidates = new long[22];
                var selected = new long[22];
                var scoreSums = new long[22];
                var scoreSamples = new long[22];
                for (int i = 0; i < candidates.Length; i++)
                {
                    candidates[i] = augmentCandidateCounts[f, i];
                    selected[i] = augmentSelectedCounts[f, i];
                    scoreSums[i] = augmentAutoScoreSums[f, i];
                    scoreSamples[i] = augmentAutoScoreSamples[f, i];
                }
                factions[f] = new AugmentFactionDiagnostic
                {
                    FactionId = (uint)f + 1,
                    ChoiceOpportunities = opportunities,
                    Rerolls = augmentRerolls[f],
                    AutomaticChoices = augmentAutomaticChoices[f],
                    CandidateCounts = candidates,
                    SelectedCounts = selected,
                    AutoScoreSums = scoreSums,
                    AutoScoreSamples = scoreSamples,
                    UnyieldingSaves = augmentUnyieldingSaves[f],
                    ArtisanExtras = augmentArtisanExtras[f],
                    ShatteringBlowApplications = augmentShatteringBlowApplications[f],
                    WarriorsBloodApplications = augmentWarriorsBloodApplications[f],
                    ShadowExtraClones = augmentShadowExtraClones[f],
                    HunterBonusDamage = augmentHunterBonusDamage[f],
                    UltimateCooldownReductions = augmentUltimateCooldownReductions[f],
                    GuardianKills = augmentGuardianKills[f],
                    GuardianHuntOrders = augmentGuardianHuntOrders[f]
                };
            }
            return new AugmentDiagnosticObservation
            {
                Tick = world.Tick,
                Factions = factions,
                GuardianSpawns = augmentGuardianSpawns
            };
        }
    }
}
