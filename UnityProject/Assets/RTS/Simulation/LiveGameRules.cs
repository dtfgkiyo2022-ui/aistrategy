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
            var scenario = largeMap ? MapGenerator.GenerateLarge(mapSeed, gold: allCivilisations, processingChain: true)
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
            e.BeltComponents = true;
            if (e.LineRebuildDelayTicks == 0) e.LineRebuildDelayTicks = 200;
            if (e.RaidLinePriority == 0) e.RaidLinePriority = 1;
            e.Regions = true;
            e.Towns = true;
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
            e.EarlyArms = true;
            e.CoreDefence = true;
            e.ArmyGrowth = true;
            e.EconomyScale = true;
            e.LatePush = true;
        }
    }
}
