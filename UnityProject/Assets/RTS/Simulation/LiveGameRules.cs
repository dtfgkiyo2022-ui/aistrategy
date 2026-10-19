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
            e.CraftTree2 = allCivilisations && e.CraftTree1;
            e.CraftTree3 = allCivilisations && e.CraftTree2;
            e.BeltComponents = true;
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
            scenario.Rules.FactionCap = e.LargeFactionCap;
            // LargeEconomy keeps the existing resource and construction fields as the authoritative costs, but makes
            // the large-map queues affordable enough to sustain the requested 300 villagers and 1000 soldiers.
            e.BarracksWoodCost = 100;
            e.VillagerFoodCost = 10;
            e.InfantryFoodCost = 5;
            e.InfantryWoodCost = 1;
            e.TownWoodCost = 80;
            e.TownStoneCost = 50;
            e.TownWork = 400;
            e.HouseWoodCost = 20;
            e.HouseWork = 60;
        }
    }
}
