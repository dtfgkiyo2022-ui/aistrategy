using System;

namespace Rts.Simulation
{
    /// <summary>
    /// The rules of the played game on the economy maps (LiveMatchHost). Everything that has been implemented is on, so
    /// a test play can try it (owner, 10-09). Kept on the simulation side so Headless can run exactly the played match.
    /// </summary>
    public static class LiveGameRules
    {
        /// <summary>The terrain or large map of the played game, with its rules.</summary>
        public static ScenarioDefinition Create(ulong mapSeed, bool largeMap, bool allCivilisations, bool monks, bool ageVictory)
        {
            var scenario = largeMap ? MapGenerator.GenerateLarge(mapSeed, gold: allCivilisations, processingChain: true,
                largeEconomy: true)
                : MapGenerator.GenerateTerrain(mapSeed, gold: allCivilisations, processingChain: true);
            Apply(scenario, allCivilisations, monks, ageVictory);
            return scenario;
        }

        public static void Apply(ScenarioDefinition scenario, bool allCivilisations, bool monks, bool ageVictory)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            var e = scenario.Economy;
            e.MonksEnabled = monks;
            e.AgeVictoryEnabled = ageVictory;
            // 32.28 processing chain, 32.30 belt components and the line guard (#362/#363), S-5 regions (RequestLine and
            // the region controls name them) and S-3 outpost towns. The large map had none of the belt rules.
            e.ProcessingChain = true;
            e.ResourceFog = true;
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
            e.CraftTree2 = allCivilisations && e.CraftTree1;
            e.CraftTree3 = allCivilisations && e.CraftTree2;
            e.CraftTree4 = allCivilisations && e.CraftTree3 && e.AgeFour;
            e.BeltComponents = true;
            // 38.7 is measured on the large economy like 37.8. On the small (provisional) map the primitive farms take
            // the little wood there is and the hut makes no goods by 5000 ticks.
            e.OpenFarms = allCivilisations && e.LargeEconomy;
            if (e.LineRebuildDelayTicks == 0) e.LineRebuildDelayTicks = 200;
            if (e.RaidLinePriority == 0) e.RaidLinePriority = 1;
            e.Regions = true;
            e.DirectPathMovement = true;
            e.Towns = true;
            e.AdjacentBuildings = true;
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
            if (e.LargeEconomy)
            {
                EnableLargeEconomy(scenario);
            }
            // The late push is now a real assault under SiegeByAge. Keep the core defence meaningful, but do not let
            // the old 16-cell/30-damage setting delete every attacker before the ordinary core attack envelope.
            e.CoreDefenceRange = 9;
            e.CoreDefenceDamage = 15;
        }

        internal static void EnableLargeEconomy(ScenarioDefinition scenario)
        {
            var e = scenario.Economy;
            e.LargeEconomy = true;
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
    }
}
