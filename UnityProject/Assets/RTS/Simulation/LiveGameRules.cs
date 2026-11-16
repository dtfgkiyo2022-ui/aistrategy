using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// The rules of the played game on the economy maps (LiveMatchHost). Everything that has been implemented is on, so
    /// a test play can try it (owner, 10-09). Kept on the simulation side so Headless can run exactly the played match.
    /// </summary>
    public static class LiveGameRules
    {
        /// <summary>The terrain or large map of the played game, with its rules.</summary>
        public static ScenarioDefinition Create(ulong mapSeed, bool largeMap, bool allCivilisations, bool monks, bool ageVictory, bool glassworksOff = false, bool modulesOff = false, bool lensAssemblerReserveOff = false, bool precisionAssemblerReserveOff = false, bool precisionGearReserveOff = false, bool precisionMetalReserveOff = false, bool outpostSiegeOff = false, bool randomCoresOff = false, bool wardsOff = false, bool combatStatsOff = false, bool skillsOff = false)
        {
            var scenario = largeMap ? MapGenerator.GenerateLarge(mapSeed, gold: allCivilisations, processingChain: true,
                largeEconomy: true, glassworks: !glassworksOff, randomCores: !randomCoresOff)
                : MapGenerator.GenerateTerrain(mapSeed, gold: allCivilisations, processingChain: true);
            Apply(scenario, allCivilisations, monks, ageVictory);
            // 67: the played large map breaks every tie the same way for both start positions (west won 64%).
            scenario.Economy.SideFair = largeMap;
            if (glassworksOff) scenario.Economy.Glassworks = false;
            if (modulesOff) scenario.Economy.Modules = false;
            if (lensAssemblerReserveOff) scenario.Economy.LensAssemblerReserve = false;
            if (precisionAssemblerReserveOff) scenario.Economy.PrecisionAssemblerReserve = false;
            if (precisionGearReserveOff) scenario.Economy.PrecisionGearReserve = false;
            if (precisionMetalReserveOff) scenario.Economy.PrecisionMetalReserve = false;
            if (outpostSiegeOff) scenario.Economy.OutpostSiege = false;
            scenario.Economy.CombatStats = largeMap && !combatStatsOff;
            scenario.Economy.Skills = largeMap && !skillsOff;
            if (scenario.Economy.Skills)
                ConfigureSkills(scenario);
            else
            {
                // Skill data is part of extension 119. Keep small maps and an explicit off profile empty so
                // the disabled profile cannot acquire skill state merely by constructing the live rules.
                scenario.Skills = Array.Empty<SkillDefinition>();
                scenario.UnitSkillKits = Array.Empty<UnitSkillKit>();
            }
            if (wardsOff) scenario.Economy.Wards = false;
            return scenario;
        }

        public static void Apply(ScenarioDefinition scenario, bool allCivilisations, bool monks, bool ageVictory)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            var e = scenario.Economy;
            e.AiLevelWest = EconomyRules.AiLevelMax;
            e.AiLevelEast = EconomyRules.AiLevelMax;
            e.MonksEnabled = monks;
            e.AgeVictoryEnabled = ageVictory;
            // 32.28 processing chain, 32.30 belt components and the line guard (#362/#363), S-5 regions (RequestLine and
            // the region controls name them) and S-3 outpost towns. The large map had none of the belt rules.
            e.ProcessingChain = true;
            e.ResourceFog = true;
            // 71: building memory is a LargeEconomy default and remains opt-in for smaller profiles.
            e.BuildingMemory = e.LargeEconomy;
            // 73: wards are part of the played large-map profile; authored/small scenarios remain unchanged.
            e.Wards = e.LargeEconomy;
            e.ExploreScouting = allCivilisations;
            e.DropSiteLimit = true;
            e.AiBelts = true;
            e.StorageFeedsProcessing = allCivilisations;
            e.Rations = allCivilisations;
            e.CommonDrill = allCivilisations;
            e.Inserters = allCivilisations;
            e.Bricks = allCivilisations;
            e.Tack = allCivilisations;
            e.Planks = allCivilisations;
            e.Books = allCivilisations;
            e.Preserves = allCivilisations;
            e.Jewels = allCivilisations;
            e.TradeGoods = allCivilisations;
            e.Incense = allCivilisations;
            e.Charms = allCivilisations;
            e.RepairKits = allCivilisations;
            e.Tools = allCivilisations;
            e.GoodsMarket = allCivilisations;
            e.CraftTiming = allCivilisations;
            e.CraftBalance = allCivilisations;
            e.CraftTree1 = allCivilisations;
            e.PrimitiveCraft = allCivilisations;
            // 37.8 was designed and measured for the 300-villager large economy. On the small (provisional) map the
            // templates take the little wood there is, and the sides field neither their second unit nor primitive goods.
            e.AiFactory = allCivilisations && e.PrimitiveCraft && e.LargeEconomy;
            e.AiExpand = e.AiFactory;
            e.StockAwareGather = e.AiExpand;
            e.GatherSpread = e.StockAwareGather;
            e.GatherNodeWorkerCap = 0;
            e.GatherNodeRemainingPerWorker = 50;
            e.GatherScoutWindowTicks = 600;
            e.GatherScoutTriggerFailures = 20;
            e.GatherScoutMaxTicks = 2400;
            e.GatherSwitchPerInterval = 40;
            e.FreeTowns = e.AiExpand;
            // fastmove1: only the large played map opts into the movement step multiplier.
            e.FastMovement = e.LargeEconomy;
            e.ArmyFlow = e.LargeEconomy;
            e.ArmyFinalApproach = e.LargeEconomy;
            e.HumanOrderFlow = e.LargeEconomy;
            e.CraftTree2 = allCivilisations && e.CraftTree1;
            e.CraftTree3 = allCivilisations && e.CraftTree2;
            e.CraftTree4 = allCivilisations && e.CraftTree3 && e.AgeFour;
            e.CraftRequired = allCivilisations && e.LargeEconomy && e.CraftTree2;
            e.CraftDepth = allCivilisations && e.LargeEconomy && e.CraftRequired;
            e.ArmsScale = allCivilisations && e.LargeEconomy && e.CraftRequired;
            e.ArmsFlow = e.ArmsScale && e.CraftDepth;
            // craftflow1: observation and presentation only, enabled on the large live economy profile.
            e.CraftFlow = e.LargeEconomy;
            e.BeltComponents = true;
            // 38.7 is measured on the large economy like 37.8. On the small (provisional) map the primitive farms take
            // the little wood there is and the hut makes no goods by 5000 ticks.
            e.OpenFarms = allCivilisations && e.LargeEconomy;
            // 38.12 is intentionally large-map-only; small maps and explicit old scenarios retain the historical hand haul.
            e.BeltDropOff = e.LargeEconomy;
            // 38.13 is only the automatic side of BeltDropOff, and is intentionally large-map-only as well.
            e.BeltDropOffAi = e.LargeEconomy && e.BeltDropOff;
            e.BeltStack = e.LargeEconomy;
            e.BeltCraft = e.BeltStack && e.CraftRequired;
            e.AiBeltParts = e.BeltCraft;
            e.AiInserterParts = e.AiBeltParts;
            e.AiInserterPartsMinStock = 50;
            e.AiBeltPartsGearsStockTarget = 8;
            e.AiMergeMaxCells = 400;
            e.AiUndergroundDetourPermille = 1500;
            e.AiCatapultMinRouteCells = 40;
            // 38.9 is a large-map-only opening rule. Apply it after map generation so direct map-generator callers keep
            // the historical three starting villagers, while the played large map gets the same deterministic placement
            // pattern with the configured early-start count.
            SetEarlyStart(scenario, e.LargeEconomy);
            if (e.LineRebuildDelayTicks == 0) e.LineRebuildDelayTicks = 200;
            if (e.RaidLinePriority == 0) e.RaidLinePriority = 1;
            e.Regions = true;
            e.DirectPathMovement = true;
            e.Towns = true;
            e.AdjacentBuildings = true;
            // 38.27: the livelihood flag is a large-map live rule. Its four production permissions are
            // deliberately widened here; the civilisation-specific effects remain in the simulation predicates.
            e.Livelihood = e.LargeEconomy;
            if (e.Livelihood)
            {
                e.Preserves = true;
                e.Tools = true;
                e.TradeGoods = true;
                e.Incense = true;
            }
            // 38.29: the common civilian-goods loop is a large-map all-civilisations rule.
            e.CivGoodsRequired = allCivilisations && e.LargeEconomy;
            if (e.CivGoodsRequired)
            {
                e.Bricks = true;
                e.Tack = true;
                e.RepairKits = true;
                e.CivGoodsMaxLines = 6;
            }
            // Fortify1 is deliberately a large-map live rule. Small maps and old authored scenarios keep the old bytes.
            e.Fortify = e.LargeEconomy;
            e.RepairUnderAttack = e.Fortify;
            // FarmHaulIdle is an opt-in rule for authored scenarios and is on by default in the played large match.
            e.FarmHaulIdle = e.LargeEconomy;
            // BuildingAttackOrder is a live-game presentation/simulation rule; authored and small scenarios remain off.
            e.BuildingAttackOrder = e.LargeEconomy;
            e.HumanAttackFix = e.LargeEconomy;
            e.AiAvoidEnemy = e.LargeEconomy;
            // raidvil1 is deliberately a large-map live rule. It is independent of the age rules in the simulation.
            e.RaidVillagersFirst = e.LargeEconomy;
            // byproducts1 is deliberately a large-map live rule. The explicit extension/off switch can disable it.
            e.Byproducts = e.LargeEconomy;
            e.Modules = e.LargeEconomy;
            e.LensAssemblerReserve = e.Modules;
            e.PrecisionAssemblerReserve = e.Modules;
            e.PrecisionGearReserve = e.Modules;
            e.PrecisionMetalReserve = e.Modules;
            e.ArmySplit = true;
            // OutpostSiege is a large-map live rule; authored scenarios and the old small map remain byte-for-byte old.
            e.OutpostSiege = e.LargeEconomy;
            if (!allCivilisations) return;
            e.Forestry = true;
            e.Masonry = true;
            e.Caravan = true;
            e.Cavalry = true;
            e.Bridge = true;
            e.Academy = true;
            e.Cult = true;
            e.MonksEnabled = true; // the cult trains monks, so the monk rules come with it
            e.Mountain = true;
            e.FishingCiv = true;
            e.FishingEnabled = true; // the fishing civilisation needs the river fish
            e.Tollgate = true;
            e.Metropolis = true;
            e.Sanctuary = true;
            e.CivVariety = true;
            e.CivVarietyCaps = true;
            // The first 250‰ window produced one 31.25% mountain result in seeds 1..40. The measured 200‰ window
            // keeps thirteen civilisations in play while keeping every selected count below 30%.
            e.CivVarietyWindowPermille = 200;
            e.EarlyArms = true;
            e.AgeClock = true;
            e.AgeClockUx = true;
            e.AgeFour = true;
            e.AgeFourAi = e.AgeFour;
            e.AgeStrengths = e.AgeFour;
            e.CraftTree4 = allCivilisations && e.CraftTree3 && e.AgeFour;
            e.AgeClockTicks1 = 6000; e.AgeClockTicks2 = 12000; e.AgeClockTicks3 = 18000; e.AgeClockTicks4 = 24000;
            if (e.LargeEconomy) { e.Age4FoodCost = 1080; e.Age4WoodCost = 720; }
            else { e.Age4FoodCost = 2400; e.Age4WoodCost = 1600; }
            // Both ordered large-map measurement candidates pass the 24,000..36,000 tick window;
            // keep the age-to-age increase by adopting the original (450, 600) pair.
            e.SiegeAge0Permille = 100; e.SiegeAge1Permille = 250; e.SiegeAge2Permille = 450; e.SiegeAge3Permille = 600; e.SiegeAge4Permille = 4000;
            e.LatePushAfterTicks = 24000; e.LatePushAdvantageAfterTicks = 24000;
            e.CoreDefence = true;
            e.ArmyGrowth = true;
            e.EconomyScale = true;
            e.AiArmyRecovery = true;
            e.LineBuildingCheck = true;
            e.LatePush = true;
            e.SiegeByAge = true;
            e.PushWaves = true;
            e.ManualControl = true;
            e.AutoReturnAfterHuman = true;
            e.BuildingReservations = true;
            e.NoNegativeStock = true;
            // 38.10 is deliberately enabled only by the large-map live profile.
            e.HouseFlow = e.LargeEconomy;
            if (e.LargeEconomy)
            {
                EnableLargeEconomy(scenario);
            }
            // The late push is now a real assault under SiegeByAge. Keep the core defence meaningful, but do not let
            // the old 16-cell/30-damage setting delete every attacker before the ordinary core attack envelope.
            e.CoreDefenceRange = 9;
            e.CoreDefenceDamage = 15;
        }

        private static void ConfigureSkills(ScenarioDefinition scenario)
        {
            // Live rage gain is doubled from the rule defaults (4/2). At 4 per hit an infantryman needed five
            // uninterrupted seconds for one 20-rage skill, and shield bash / step-in fired about once a match (11-16).
            scenario.Economy.SkillRageOnHitDealt = 8;
            scenario.Economy.SkillRageOnHitTaken = 4;
            scenario.Skills = new[]
            {
                Skill(1, "盾打ち", SkillResource.Rage, 20, 160, SkillTargeting.Unit, SkillShape.Single, 3, 0, 0, 0, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Damage, SkillDamageType.Physical, 15, 500), Effect(SkillEffectKind.Cc, SkillDamageType.Physical, 0, 0, SkillCcKind.Slow, 40, 300)),
                Skill(2, "踏み込み", SkillResource.Rage, 25, 240, SkillTargeting.Unit, SkillShape.Single, 6, 0, 0, 0, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Damage, SkillDamageType.Physical, 10, 300), Effect(SkillEffectKind.Cc, SkillDamageType.Physical, 0, 0, SkillCcKind.Stun, 15, 0)),
                Skill(3, "構え", SkillResource.Rage, 15, 300, SkillTargeting.Self, SkillShape.Single, 0, 0, 0, 0, SkillAffects.Self,
                    Effect(SkillEffectKind.DamageReduction, SkillDamageType.Physical, 0, 0, default(SkillCcKind), 40, 250), Effect(SkillEffectKind.Debuff, SkillDamageType.Physical, 0, 0, default(SkillCcKind), 40, 300, SkillStat.MoveSpeed)),
                Skill(4, "鬨の声", SkillResource.Rage, 0, 1200, SkillTargeting.Self, SkillShape.Circle, 10, 0, 0, 0, SkillAffects.AlliesAndSelf,
                    Effect(SkillEffectKind.Buff, SkillDamageType.Physical, 0, 0, default(SkillCcKind), 100, 200, SkillStat.AttackDamage), Effect(SkillEffectKind.Buff, SkillDamageType.Physical, 0, 0, default(SkillCcKind), 100, 250, SkillStat.AttackSpeed)),
                Skill(5, "連射", SkillResource.Mana, 40, 400, SkillTargeting.Self, SkillShape.Single, 0, 0, 0, 0, SkillAffects.Self,
                    Effect(SkillEffectKind.Buff, SkillDamageType.Physical, 0, 0, default(SkillCcKind), 60, 600, SkillStat.AttackSpeed)),
                Skill(6, "扇撃ち", SkillResource.Mana, 50, 240, SkillTargeting.Direction, SkillShape.Cone, 8, 40, 0, 0, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Damage, SkillDamageType.Physical, 12, 800)),
                Skill(7, "後ろ跳び", SkillResource.Mana, 30, 300, SkillTargeting.Self, SkillShape.Circle, 4, 0, 0, 0, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Cc, SkillDamageType.Physical, 0, 0, SkillCcKind.Slow, 40, 400)),
                Skill(8, "矢の雨", SkillResource.Mana, 100, 1200, SkillTargeting.Point, SkillShape.Circle, 4, 0, 0, 20, SkillAffects.Enemies, 40, 20,
                    Effect(SkillEffectKind.Damage, SkillDamageType.Physical, 20, 1000)),
                Skill(9, "突撃", SkillResource.Rage, 25, 240, SkillTargeting.Unit, SkillShape.Single, 10, 0, 0, 0, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Damage, SkillDamageType.Physical, 20, 600), Effect(SkillEffectKind.Cc, SkillDamageType.Physical, 0, 0, SkillCcKind.Push, 0, 4)),
                Skill(10, "蹴散らし", SkillResource.Rage, 20, 160, SkillTargeting.Self, SkillShape.Circle, 3, 0, 0, 0, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Damage, SkillDamageType.Physical, 15, 600)),
                Skill(11, "駆け抜け", SkillResource.Rage, 15, 400, SkillTargeting.Self, SkillShape.Single, 0, 0, 0, 0, SkillAffects.Self,
                    Effect(SkillEffectKind.Buff, SkillDamageType.Physical, 0, 0, default(SkillCcKind), 60, 400, SkillStat.MoveSpeed), Effect(SkillEffectKind.CcImmunity, SkillDamageType.Physical, 0, 0, SkillCcKind.Slow, 60, 1 << (int)SkillCcKind.Slow)),
                Skill(12, "大突撃", SkillResource.Rage, 0, 1200, SkillTargeting.Direction, SkillShape.Line, 0, 0, 12, 3, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Damage, SkillDamageType.Physical, 30, 1000), Effect(SkillEffectKind.Cc, SkillDamageType.Physical, 0, 0, SkillCcKind.KnockUp, 20, 0)),
                Skill(13, "煙玉", SkillResource.Energy, 40, 600, SkillTargeting.Self, SkillShape.Single, 0, 0, 0, 0, SkillAffects.Self,
                    Effect(SkillEffectKind.Camouflage, SkillDamageType.Physical, 0, 0, default(SkillCcKind), 60, 0)),
                Skill(14, "投げ網", SkillResource.Energy, 30, 300, SkillTargeting.Unit, SkillShape.Single, 8, 0, 0, 0, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Cc, SkillDamageType.Physical, 0, 0, SkillCcKind.Root, 40, 0)),
                Skill(15, "目印", SkillResource.Energy, 30, 400, SkillTargeting.Unit, SkillShape.Single, 12, 0, 0, 0, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Buff, SkillDamageType.Physical, 0, 0, default(SkillCcKind), 100, 100, SkillStat.DamageTaken)),
                Skill(16, "撹乱", SkillResource.Energy, 60, 1200, SkillTargeting.Self, SkillShape.Circle, 6, 0, 0, 0, SkillAffects.Enemies,
                    Effect(SkillEffectKind.Cc, SkillDamageType.Physical, 0, 0, SkillCcKind.Fear, 30, 0)),
                Skill(17, "癒し", SkillResource.Mana, 50, 200, SkillTargeting.Unit, SkillShape.Single, 8, 0, 0, 0, SkillAffects.Allies,
                    Effect(SkillEffectKind.Heal, SkillDamageType.Magic, 30, 0, ap: 500)),
                Skill(18, "加護", SkillResource.Mana, 60, 300, SkillTargeting.Unit, SkillShape.Single, 8, 0, 0, 0, SkillAffects.Allies,
                    Effect(SkillEffectKind.Shield, SkillDamageType.Magic, 30, 0, ap: 500, duration: 80)),
                Skill(19, "聖域", SkillResource.Mana, 150, 1500, SkillTargeting.Point, SkillShape.Circle, 6, 0, 0, 0, SkillAffects.AlliesAndSelf, 100, 20,
                    Effect(SkillEffectKind.Heal, SkillDamageType.Magic, 10, 0, applies: SkillEffectAppliesTo.Allies), Effect(SkillEffectKind.Cc, SkillDamageType.Magic, 0, 0, SkillCcKind.Slow, 20, 300, applies: SkillEffectAppliesTo.Enemies))
            };
            scenario.UnitSkillKits = new[]
            {
                // Rage ultimates (war cry, grand charge) cost no rage and wait only on their 60 s cooldown. Paying 50-60
                // rage starved them, and holding rage back for them starved Q/W/E (11-16).
                Kit(UnitKind.Infantry, SkillResource.Rage, 100, PassiveKind.Formation, 1, 2, 3, 4),
                Kit(UnitKind.Archer, SkillResource.Mana, 200, PassiveKind.Aim, 5, 6, 7, 8, 4),
                Kit(UnitKind.Cavalry, SkillResource.Rage, 100, PassiveKind.Momentum, 9, 10, 11, 12),
                // The large-map AI fields light cavalry, not the plain cavalry class. Until C5.3 gives it a kit of its
                // own it shares the cavalry kit, so the four cavalry skills are actually used (11-16).
                Kit(UnitKind.LightCavalry, SkillResource.Rage, 100, PassiveKind.Momentum, 9, 10, 11, 12),
                Kit(UnitKind.Scout, SkillResource.Energy, 100, PassiveKind.Fleet, 13, 14, 15, 16, 10),
                Kit(UnitKind.Monk, SkillResource.Mana, 300, PassiveKind.Prayer, 17, 18, 0, 19, 5)
            };
            // Combat4 replaces the three placeholder movement/weapon shapes with data-driven effects.
            scenario.Skills[1].Targeting = SkillTargeting.Direction; scenario.Skills[1].Shape = SkillShape.Line; scenario.Skills[1].Length = 6; scenario.Skills[1].Width = 2;
            scenario.Skills[1].Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Move, MoveKind = SkillMoveKind.Dash, MoveDistance = Fix64.FromInt(6), MoveSpeed = Fix64.FromInt(1), MoveStopsOnFirstHit = true, Base = 10, AdPermille = 300, DamageType = SkillDamageType.Physical, CcKind = SkillCcKind.Stun, DurationTicks = 15 } };
            scenario.Skills[5].Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Projectile, DamageType = SkillDamageType.Physical, Base = 12, AdPermille = 800, ProjectileSpeed = Fix64.FromInt(1), ProjectileLength = 8, ProjectileWidth = 1, ProjectileFanCount = 5, ProjectileFanAngleDegrees = 40 } };
            scenario.Skills[6].Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Move, MoveKind = SkillMoveKind.Backstep, MoveDistance = Fix64.FromInt(4), MoveSpeed = Fix64.FromInt(1) }, new SkillEffect { Kind = SkillEffectKind.Cc, DamageType = SkillDamageType.Physical, CcKind = SkillCcKind.Slow, DurationTicks = 40, Strength = 400 } };
            scenario.Skills[8].Targeting = SkillTargeting.Direction; scenario.Skills[8].Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Move, MoveKind = SkillMoveKind.Dash, MoveDistance = Fix64.FromInt(10), MoveSpeed = Fix64.FromInt(1), Base = 20, AdPermille = 600, DamageType = SkillDamageType.Physical, CcKind = SkillCcKind.Push, Strength = 4 } };
            scenario.Skills[11].Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Move, MoveKind = SkillMoveKind.Dash, MoveDistance = Fix64.FromInt(12), MoveSpeed = Fix64.FromInt(1), Base = 30, AdPermille = 1000, DamageType = SkillDamageType.Physical, CcKind = SkillCcKind.KnockUp, DurationTicks = 20 } };
            for (int i = 0; i < scenario.UnitParameters.Length; i++)
                if (scenario.UnitParameters[i].Kind == UnitKind.Monk) { var p = scenario.UnitParameters[i]; p.AbilityPower = 20; scenario.UnitParameters[i] = p; }
        }

        /// <summary>Combat4 measurement profile: faction 1 scouts use Shadow and faction 2 scouts use Earth.</summary>
        public static void EnableSkillsDemo(ScenarioDefinition scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            scenario.Economy.Skills = true;
            var oldSkills = scenario.Skills ?? Array.Empty<SkillDefinition>();
            if (oldSkills.Length == 0)
            {
                ConfigureSkills(scenario);
                oldSkills = scenario.Skills ?? Array.Empty<SkillDefinition>();
            }
            var demo = Combat4DemoSkills(); var all = new SkillDefinition[oldSkills.Length + demo.Length]; Array.Copy(oldSkills, all, oldSkills.Length); Array.Copy(demo, 0, all, oldSkills.Length, demo.Length); scenario.Skills = all;
            var oldKits = scenario.UnitSkillKits ?? Array.Empty<UnitSkillKit>(); var kits = new UnitSkillKit[oldKits.Length + 2]; Array.Copy(oldKits, kits, oldKits.Length);
            kits[oldKits.Length] = Combat4ShadowKit(); kits[oldKits.Length + 1] = Combat4EarthKit(); scenario.UnitSkillKits = kits;
        }

        public static UnitSkillKit Combat4ShadowKit() => new UnitSkillKit { UnitKind = UnitKind.Scout, FactionId = 1, Resource = SkillResource.Energy, ResourceMax = 200, RegenPerSecond = 10, SkillIds = new[] { 20, 21, 22, 23 } };
        public static UnitSkillKit Combat4EarthKit() => new UnitSkillKit { UnitKind = UnitKind.Scout, FactionId = 2, Resource = SkillResource.Mana, ResourceMax = 300, RegenPerSecond = 5, PassiveKind = PassiveKind.HardSkin, SkillIds = new[] { 24, 25, 26, 27 } };

        private static SkillDefinition[] Combat4DemoSkills()
        {
            return new[]
            {
                new SkillDefinition { Id = 20, Name = "影の投げ刃", Resource = SkillResource.Energy, Cost = 50, CooldownTicks = 120, Range = Fix64.FromInt(9), Targeting = SkillTargeting.Direction, Shape = SkillShape.Line, Length = 9, Width = 1, Affects = SkillAffects.Enemies, MirrorFromClones = true,
                    Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Projectile, DamageType = SkillDamageType.Physical, Base = 25, AdPermille = 1000, ProjectileSpeed = Fix64.FromRatio(3, 2), ProjectileLength = 9, ProjectileWidth = 1, ProjectilePierceCount = 3 } } },
                new SkillDefinition { Id = 21, Name = "影を置く", Resource = SkillResource.Energy, Cost = 40, CooldownTicks = 360, Range = Fix64.FromInt(12), Targeting = SkillTargeting.Direction, Shape = SkillShape.Single, Affects = SkillAffects.Self,
                    Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Clone, CloneCount = 1, CloneDurationTicks = 100 } } },
                new SkillDefinition { Id = 22, Name = "回し斬り", Resource = SkillResource.Energy, Cost = 40, CooldownTicks = 100, Range = Fix64.FromInt(4), Targeting = SkillTargeting.Self, Shape = SkillShape.Circle, Radius = 4, Affects = SkillAffects.Enemies, MirrorFromClones = true,
                    Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.Physical, Base = 20, AdPermille = 600 }, new SkillEffect { Kind = SkillEffectKind.Cc, CcKind = SkillCcKind.Slow, DurationTicks = 30, Strength = 300, Condition = SkillConditionKind.NearCasterClone, ConditionValue = 1 } } },
                new SkillDefinition { Id = 23, Name = "死の印", Resource = SkillResource.Energy, Cost = 0, CooldownTicks = 1600, Range = Fix64.FromInt(8), Targeting = SkillTargeting.Unit, Shape = SkillShape.Single, Affects = SkillAffects.Enemies,
                    Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Move, MoveKind = SkillMoveKind.Teleport, MoveDistance = Fix64.FromInt(8) }, new SkillEffect { Kind = SkillEffectKind.Clone, CloneCount = 1, CloneDurationTicks = 60 }, new SkillEffect { Kind = SkillEffectKind.Mark, MarkId = 1, MarkMaxStacks = 1, MarkDurationTicks = 60, MarkBurstDamagePermille = 500 } } },
                new SkillDefinition { Id = 24, Name = "鎖の手", Resource = SkillResource.Mana, Cost = 60, CooldownTicks = 300, Range = Fix64.FromInt(10), Targeting = SkillTargeting.Direction, Shape = SkillShape.Line, Length = 10, Width = 1, Affects = SkillAffects.Enemies,
                    Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Projectile, DamageType = SkillDamageType.Magic, Base = 30, ApPermille = 600, ProjectileSpeed = Fix64.FromInt(1), ProjectileLength = 10, ProjectileWidth = 1, ProjectilePierceCount = 1, MoveKind = SkillMoveKind.Dash, MoveDistance = Fix64.FromInt(1) } } },
                new SkillDefinition { Id = 25, Name = "絶望", Resource = SkillResource.Mana, Cost = 20, CooldownTicks = 60, Range = Fix64.FromInt(3), Targeting = SkillTargeting.Self, Shape = SkillShape.Circle, Radius = 3, Affects = SkillAffects.Enemies, PersistTicks = 200, PulseIntervalTicks = 20,
                    Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.Magic, Base = 8, TargetMaxHpPermille = 10 } } },
                new SkillDefinition { Id = 26, Name = "怒りの一撃", Resource = SkillResource.None, Cost = 0, CooldownTicks = 200, Range = Fix64.FromInt(4), Targeting = SkillTargeting.Self, Shape = SkillShape.Circle, Radius = 4, Affects = SkillAffects.Enemies,
                    Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.Magic, Base = 25 }, new SkillEffect { Kind = SkillEffectKind.DamageReduction, Strength = 100, DurationTicks = 300 } } },
                new SkillDefinition { Id = 27, Name = "大地の叫び", Resource = SkillResource.Mana, Cost = 100, CooldownTicks = 1800, CastTicks = 10, Range = Fix64.FromInt(7), Targeting = SkillTargeting.Self, Shape = SkillShape.Circle, Radius = 7, Affects = SkillAffects.Enemies,
                    Effects = new[] { new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.Magic, Base = 150, ApPermille = 1000 }, new SkillEffect { Kind = SkillEffectKind.Cc, CcKind = SkillCcKind.Stun, DurationTicks = 30 } } }
            };
        }

        private static UnitSkillKit Kit(UnitKind kind, SkillResource resource, int max, PassiveKind passive, int q, int w, int e, int r, int regen = 0)
            => new UnitSkillKit { UnitKind = kind, Resource = resource, ResourceMax = max, RegenPerSecond = regen, PassiveKind = passive, SkillIds = new[] { q, w, e, r } };

        private static SkillDefinition Skill(int id, string name, SkillResource resource, int cost, int cooldown, SkillTargeting targeting,
            SkillShape shape, int radius, int angle, int length, int width, SkillAffects affects, params SkillEffect[] effects)
            => Skill(id, name, resource, cost, cooldown, targeting, shape, radius, angle, length, width, affects, effects, 0, 0);

        private static SkillDefinition Skill(int id, string name, SkillResource resource, int cost, int cooldown, SkillTargeting targeting,
            SkillShape shape, int radius, int angle, int length, int width, SkillAffects affects, SkillEffect[] effects, int persist, int pulse)
            => new SkillDefinition { Id = id, Name = name, Resource = resource, Cost = cost, CooldownTicks = cooldown, Range = Fix64.FromInt(Math.Max(1, radius)),
                Targeting = targeting, Shape = shape, Radius = radius, AngleDegrees = angle == 0 ? 90 : angle, Length = length, Width = width,
                Affects = affects, Effects = effects, PersistTicks = persist, PulseIntervalTicks = pulse };

        private static SkillDefinition Skill(int id, string name, SkillResource resource, int cost, int cooldown, SkillTargeting targeting,
            SkillShape shape, int radius, int angle, int length, int width, SkillAffects affects, int persist, int pulse, params SkillEffect[] effects)
        {
            var skill = Skill(id, name, resource, cost, cooldown, targeting, shape, radius, angle, length, width, affects, effects, persist, pulse);
            skill.Range = id == 1 ? Fix64.FromRatio(5, 2) : id == 8 ? Fix64.FromInt(14) : id == 19 ? Fix64.FromInt(10) : skill.Range;
            return skill;
        }

        private static SkillEffect Effect(SkillEffectKind kind, SkillDamageType damage, int @base, int ad, SkillCcKind cc = default(SkillCcKind),
            int duration = 0, int strength = 0, SkillStat stat = default(SkillStat), SkillEffectAppliesTo applies = SkillEffectAppliesTo.Inherit, int ap = 0)
            => new SkillEffect { Kind = kind, DamageType = damage, Base = @base, AdPermille = ad, ApPermille = ap, CcKind = cc,
                DurationTicks = duration, Strength = strength, Stat = stat, AppliesTo = applies, AmountKind = SkillAmountKind.Permille };

        internal static void EnableLargeEconomy(ScenarioDefinition scenario)
        {
            var e = scenario.Economy;
            e.LargeEconomy = true;
            e.Glassworks = true;
            e.CarriedInputs = true;
        e.CarriedInputsOutletBuildingLimit = 3;
        e.CarriedHaulerCap = 80;
            e.AiReasons = e.LargeEconomy;
            e.VillagerTrainTicks = e.LargeVillagerTrainTicks;
            e.InfantryTrainTicks = e.LargeInfantryTrainTicks;
            e.AutoVillagerTarget = 300;
            e.AutoInfantryQueue = 32;
            e.QueueLimit = 64;
            e.PopulationCap = 1400;
            e.BasePopulation = 80;
            e.HousePopulation = 50;
            e.TownMaxBuildings = 8;
            e.TownCoreDistance = 160;
            e.TownResourceReach = 40;
            e.LargeFactionCap = 1400;
            e.LargeTownVillagersPerTown = 80;
            e.LargeBarracksVillagersPerBuilding = 20;
            e.LargeTownAutoWorkers = 4;
            e.LargeTargetRoundTripTicks = 300;
            e.LargeResourceClusterRadiusMeters = 24;
            e.LargeClusterDecisionIntervalTicks = 100;
            e.LargeResourceClusterMaxDropSites = 8;
            e.LargeDropSiteMax = 8;
            e.LargeDropSiteMinSpacingMeters = 24;
            e.LargeClusterSwitchPermille = 200;
            e.LargeVillagersPerFarm = 20;
            e.LargeFarmMinimum = 5;
            e.LargeFarmMaximum = 32;
            e.LargeInfantryResourceReserveUnits = 8;
            // DropSiteMax is the existing shared command/frame field. Keep it in sync so the large-map limit is
            // visible to human placement and older presentation paths as well as the LargeEconomy policy.
            e.DropSiteMax = e.LargeDropSiteMax;
            e.LargeClusterMinGatherers = 2;
            e.ExploreScouting = true;
            e.LargeExploreTargetPermille = 900;
            e.LargeExploreScoutCount = 4;
            e.LargeExploreChunkCells = 16;
            e.ExploreChunkCells = e.LargeExploreChunkCells;
            // Ordinary construction prices remain authoritative. More builders are the large-map scheduling choice,
            // not a price or work-value change, and let a remote town become useful before its resource cluster moves on.
            e.Builders = 8;
            scenario.Rules.FactionCap = e.LargeFactionCap;
            // LargeEconomy changes production scale and capacity only. Unit and building costs remain the same as the
            // ordinary map, so the large-map AI must improve its collection, hauling, and construction decisions.
        }

        /// <summary>Changes only the live opening roster; used by the measurement CLI's explicit off switch.</summary>
        public static void SetEarlyStart(ScenarioDefinition scenario, bool enabled)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            var e = scenario.Economy;
            e.EarlyStart = enabled && e.LargeEconomy;
            if (scenario.Villagers == null || scenario.Villagers.Length == 0) return;
            int desired = e.EarlyStart ? e.EarlyStartVillagers : 3;
            if (desired < 3 || desired == scenario.Villagers.Length) return;
            if (!e.EarlyStart && scenario.Villagers.Length != e.EarlyStartVillagers * 2) return;

            var villagers = new VillagerDefinition[desired * 2];
            for (uint faction = 1; faction <= 2; faction++)
            {
                var core = scenario.Cores[faction - 1].Position;
                var enemy = scenario.Cores[1 - (faction - 1)].Position;
                int towardX = Math.Sign(enemy.X.Raw - core.X.Raw), towardZ = Math.Sign(enemy.Z.Raw - core.Z.Raw);
                if (towardX == 0 && towardZ == 0) towardX = 1;
                for (int i = 0; i < desired; i++)
                {
                    uint id = (faction - 1) * (uint)desired + (uint)i + 1;
                    int slotX = 5 + i / 5 * 2, slotZ = -2 + i % 5 * 2;
                    if (!scenario.Economy.RandomCores)
                        slotX = faction == 1 ? -slotX : slotX;
                    else
                    {
                        int perpendicularX = -towardZ, perpendicularZ = towardX;
                        slotX = -towardX * slotX + perpendicularX * slotZ;
                        slotZ = -towardZ * (5 + i / 5 * 2) + perpendicularZ * (i % 5 * 2 - 2);
                    }
                    villagers[id - 1] = new VillagerDefinition { Id = id, FactionId = faction,
                        Position = new SimPoint(core.X + Fix64.FromInt(slotX), core.Z + Fix64.FromInt(slotZ)) };
                }
            }
            scenario.Villagers = villagers;
        }
    }
}
