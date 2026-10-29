using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Rts.Tactics;
using Rts.TacticsJs;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Headless.Cli;

internal static class TacticMatchCommand
{
    private sealed class ScheduledSignal
    {
        internal long Tick;
        internal string Name;
        internal SimPoint? Point;
    }
    private sealed class FrameSource : IFrameSource
    {
        private readonly Battle simulation;
        internal FrameSource(Battle simulation) { this.simulation = simulation; }
        public FactionFrame Latest(uint factionId) => simulation.Capture(factionId);
    }

    internal static int Run(Dictionary<string, string> options)
    {
        bool hasScenario = options.TryGetValue("--scenario", out var scenarioPath);
        bool hasSeed = options.TryGetValue("--map-seed", out var seedText);
        if (hasScenario == hasSeed) throw new InvalidDataException("--scenario または --map-seed の一方を指定してください。");
        string mapSize = options.GetValueOrDefault("--map-size") ?? "normal";
        if (mapSize != "normal" && mapSize != "large") throw new InvalidDataException("--map-sizeはnormalまたはlargeです。");
        if (mapSize != "normal" && (!hasSeed || !options.ContainsKey("--terrain")))
            throw new InvalidDataException("--map-size largeは--map-seedと--terrainと一緒に指定してください。");
        bool largeMap = mapSize == "large";
        var scenario = hasScenario
            ? JsonInput.Scenario(scenarioPath)
            : largeMap
                ? LiveGameRules.Create(ulong.Parse(seedText, CultureInfo.InvariantCulture), true, true, false, false)
                : options.ContainsKey("--terrain")
                // The played game's rules (LiveGameRules, all civilisations), so a tactic practises the match it will play.
                ? LiveGameRules.Create(ulong.Parse(seedText, CultureInfo.InvariantCulture), mapSize == "large", true, false, false)
                : MapGenerator.Generate(ulong.Parse(seedText, CultureInfo.InvariantCulture), true);
        if (options.ContainsKey("--early-start-off")) LiveGameRules.SetEarlyStart(scenario, false);
        if (options.ContainsKey("--house-flow-off")) scenario.Economy.HouseFlow = false;
        if (options.ContainsKey("--fast-movement-off")) scenario.Economy.FastMovement = false;
        int commonScale = ParseScale(options, "--scale", 1);
        int villagerScale = ParseScale(options, "--villager-scale", commonScale);
        int armyScale = ParseScale(options, "--army-scale", commonScale);
        if (options.ContainsKey("--scale") && (options.ContainsKey("--villager-scale") || options.ContainsKey("--army-scale")))
            throw new InvalidDataException("--scale と --villager-scale/--army-scale は同時に指定できません。");
        if (villagerScale != 1 || armyScale != 1)
            ScenarioScale.MultiplyForMeasurement(scenario, villagerScale, armyScale);
        if (options.ContainsKey("--craft-timing-off")) scenario.Economy.CraftTiming = false;
        if (options.ContainsKey("--craft-balance-off")) scenario.Economy.CraftBalance = false;
        if (options.ContainsKey("--resource-fog-off")) scenario.Economy.ResourceFog = false;
        if (options.ContainsKey("--explore-scouting-off")) scenario.Economy.ExploreScouting = false;
        if (options.ContainsKey("--army-recovery-off")) scenario.Economy.AiArmyRecovery = false;
        if (options.ContainsKey("--craft-tree1-off")) scenario.Economy.CraftTree1 = false;
        if (options.ContainsKey("--primitive-craft-off")) scenario.Economy.PrimitiveCraft = false;
        if (options.ContainsKey("--ai-factory-off")) scenario.Economy.AiFactory = false;
        if (options.ContainsKey("--craft-tree2-off")) scenario.Economy.CraftTree2 = false;
        if (options.ContainsKey("--craft-tree3-off")) scenario.Economy.CraftTree3 = false;
        if (options.ContainsKey("--craft-tree4-off")) scenario.Economy.CraftTree4 = false;
        if (options.ContainsKey("--craft-required-off")) scenario.Economy.CraftRequired = false;
        if (options.ContainsKey("--arms-scale-off")) scenario.Economy.ArmsScale = false;
        if (options.ContainsKey("--craft-depth-off")) scenario.Economy.CraftDepth = false;
        if (options.ContainsKey("--craft-flow-off")) scenario.Economy.CraftFlow = false;
        if (options.ContainsKey("--age-clock-ux-off")) scenario.Economy.AgeClockUx = false;
        if (options.ContainsKey("--age-four-off"))
        {
            scenario.Economy.AgeFour = false;
            scenario.Economy.AgeFourAi = false;
            scenario.Economy.AgeStrengths = false;
            scenario.Economy.AgeClockTicks1 = 4800;
            scenario.Economy.AgeClockTicks2 = 10800;
            scenario.Economy.AgeClockTicks3 = 15600;
            scenario.Economy.SiegeAge2Permille = 600;
            scenario.Economy.LatePushAfterTicks = 12000;
            scenario.Economy.LatePushAdvantageAfterTicks = 8000;
        }
        if (options.ContainsKey("--age-four-ai-off")) scenario.Economy.AgeFourAi = false;
        if (options.ContainsKey("--age-four-ai-industry")) scenario.Economy.AgeFourAiIndustry = true;
        if (options.TryGetValue("--siege-age2-permille", out var siegeAge2Text))
            scenario.Economy.SiegeAge2Permille = ParsePermille("--siege-age2-permille", siegeAge2Text);
        if (options.TryGetValue("--siege-age3-permille", out var siegeAge3Text))
            scenario.Economy.SiegeAge3Permille = ParsePermille("--siege-age3-permille", siegeAge3Text);
        if (options.ContainsKey("--ages")) scenario.Economy.Ages = true;
        // LiveGameRules keeps both features on by default. These explicit measurement switches are command-line-only.
        if (options.ContainsKey("--drop-site-limit-off")) scenario.Economy.DropSiteLimit = false;
        if (options.ContainsKey("--ai-belts-off")) scenario.Economy.AiBelts = false;
        if (options.ContainsKey("--open-farms-off")) scenario.Economy.OpenFarms = false;
        if (options.ContainsKey("--free-towns-off")) scenario.Economy.FreeTowns = false;
        if (options.ContainsKey("--belt-dropoff-off")) { scenario.Economy.BeltDropOff = false; scenario.Economy.BeltDropOffAi = false; }
        if (options.ContainsKey("--belt-dropoff-ai-off")) scenario.Economy.BeltDropOffAi = false;
        if (options.ContainsKey("--belt-stack-off")) scenario.Economy.BeltStack = false;
        if (options.ContainsKey("--belt-craft-off"))
        {
            scenario.Economy.BeltCraft = false;
            scenario.Economy.AiBeltParts = false;
        }
         if (options.ContainsKey("--ai-belt-parts-off")) scenario.Economy.AiBeltParts = false;
        if (options.TryGetValue("--free-towns-per-age", out var freeTownsPerAgeText))
        {
            int value = int.Parse(freeTownsPerAgeText, CultureInfo.InvariantCulture);
            if (value < 1 || value > 16) throw new InvalidDataException("--free-towns-per-age must be between 1 and 16.");
            scenario.Economy.FreeTownsPerAge = value;
        }
        long ticks = long.Parse(Required(options, "--ticks"), CultureInfo.InvariantCulture);
        // Measurement runs may intentionally extend the historical 36,000-tick verification ceiling;
        // this command does not record a replay, so raising the input guard does not change simulation state.
        scenario.VerificationTickLimit = Math.Max(scenario.VerificationTickLimit, ticks);
        if (ticks < 0 || ticks > scenario.VerificationTickLimit) throw new InvalidDataException("ticksがシナリオの範囲外です。");
        string westName = options.GetValueOrDefault("--west-tactic") ?? "auto";
        string eastName = options.GetValueOrDefault("--east-tactic") ?? "auto";
        ValidateTactic(westName); ValidateTactic(eastName);

        // Inclusive milliseconds per simulation phase, for --timing-out.
        var phaseMs = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var phaseStart = new Dictionary<string, long>(StringComparer.Ordinal);
        Action<string, bool> measure = !options.ContainsKey("--timing-out") ? null : (name, start) =>
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            if (start) { phaseStart[name] = now; phaseStart["alloc:" + name] = bytes; return; }
            if (!phaseStart.TryGetValue(name, out var began)) return;
            phaseMs["allocKB:" + name] = (phaseMs.TryGetValue("allocKB:" + name, out var allocated) ? allocated : 0) + (bytes - phaseStart["alloc:" + name]) / 1024.0;
            phaseMs[name] = (phaseMs.TryGetValue(name, out var sum) ? sum : 0) + (now - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        };
        // The comparison route is measurement-only and is selected by the harness, never by a game scenario or CLI flag.
        bool legacySpatialCombat = string.Equals(Environment.GetEnvironmentVariable("RTS_PERF5_LEGACY"), "1", StringComparison.Ordinal);
        bool legacyVisibility = string.Equals(Environment.GetEnvironmentVariable("RTS_PERF7_LEGACY"), "1", StringComparison.Ordinal);
        var simulation = new Battle(scenario, measure, legacySpatialCombat, legacyVisibility);
        var gateway = new CommandGateway(simulation);
        var source = new FrameSource(simulation);
        string runtimes = options.GetValueOrDefault("--runtimes") ?? PyodideTacticRuntime.FindDefaultRuntimes();
        ReserveCivilisation(options.GetValueOrDefault("--west-civ"), 1, gateway);
        ReserveCivilisation(options.GetValueOrDefault("--east-civ"), 2, gateway);
        if (options.ContainsKey("--west-auto-off")) gateway.SubmitEconomy(EconomyCommand.Auto(1, 1001, false));
        if (options.ContainsKey("--east-auto-off")) gateway.SubmitEconomy(EconomyCommand.Auto(2, 1002, false));
        using var west = CreateHost(westName, 1, source, gateway, runtimes, options.GetValueOrDefault("--west-param"));
        using var east = CreateHost(eastName, 2, source, gateway, runtimes, options.GetValueOrDefault("--east-param"));
        west?.Start(SetupJson(scenario, 1));
        east?.Start(SetupJson(scenario, 2));
        var westAuto = CreateAuto(westName, 1, gateway); var eastAuto = CreateAuto(eastName, 2, gateway);
        westAuto?.Initialize(); eastAuto?.Initialize();
        var lines = new List<string>();
        var westSignals = ParseSignals(options.GetValueOrDefault("--west-signal"));
        var westHuman = LoadHumanOrders(options.GetValueOrDefault("--west-human"), 1, ticks);
        var eastHuman = LoadHumanOrders(options.GetValueOrDefault("--east-human"), 2, ticks);
        var loggedDiscarded = new HashSet<ulong>();
        StreamWriter researchTrace = null;
        Dictionary<uint, Dictionary<uint, TechKind>> priorResearch = null;
        var priorBooks = new Dictionary<uint, int>();
        var researchStarts = new Dictionary<uint, int>();
        var researchCompletions = new Dictionary<uint, int>();
        var firstBooksThresholdTick = new Dictionary<uint, long>();
        var priorResearchFailureTicks = new Dictionary<uint, long>();
        if (options.TryGetValue("--research-trace-out", out var tracePath))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(tracePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            researchTrace = new StreamWriter(tracePath, false, new UTF8Encoding(false));
            priorResearch = new Dictionary<uint, Dictionary<uint, TechKind>>();
            researchTrace.WriteLine(JsonSerializer.Serialize(new
            {
                @event = "header",
                factions = new[] { 1, 2 },
                booksPerResearch = simulation.Capture(1).Economy?.BooksPerResearch ?? 0
            }));
        }
        StreamWriter craftTrace = null;
        if (options.TryGetValue("--craft-trace-out", out var craftTracePath))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(craftTracePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            craftTrace = new StreamWriter(craftTracePath, false, new UTF8Encoding(false));
            craftTrace.WriteLine("{\"event\":\"header\",\"intervalTicks\":1000}");
        }
        StreamWriter economyTrace = null;
        if (options.TryGetValue("--economy-trace-out", out var economyTracePath))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(economyTracePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            economyTrace = new StreamWriter(economyTracePath, false, new UTF8Encoding(false));
            economyTrace.WriteLine(JsonSerializer.Serialize(new
            {
                @event = "header", seed = scenario.Seed, largeEconomy = scenario.Economy.LargeEconomy,
                aiExpand = scenario.Economy.AiExpand, aiFactory = scenario.Economy.AiFactory,
                intervalTicks = 1000, resourceOrder = new[] { "food", "wood", "stone", "ore", "gold", "metal", "gears" },
                useOrder = LargeEconomyUseKind.Names
            }));
        }
        MatchPackWriter pack = null;
        if (options.TryGetValue("--pack-out", out var packPath))
        {
            pack = new MatchPackWriter(packPath, scenario, westName, eastName);
            pack.RecordInitial(simulation);
        }
        StreamWriter resourceFogTrace = null;
        if (options.TryGetValue("--resource-fog-trace-out", out var resourceFogTracePath))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(resourceFogTracePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            resourceFogTrace = new StreamWriter(resourceFogTracePath, false, new UTF8Encoding(false));
        }
        StreamWriter exploreTrace = null;
        int[] firstEnemyCoreSeen = { -1, -1 };
        if (options.TryGetValue("--explore-trace-out", out var exploreTracePath))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(exploreTracePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            exploreTrace = new StreamWriter(exploreTracePath, false, new UTF8Encoding(false));
        }
        // --timing-out writes one row per completed simulation tick so the caller can calculate nearest-rank
        // statistics without changing the deterministic simulation. Tactic execution is outside stepMs.
        StreamWriter timingWriter = null;
        if (options.TryGetValue("--timing-out", out var timingPath))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(timingPath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            timingWriter = new StreamWriter(timingPath, false, new UTF8Encoding(false));
            timingWriter.WriteLine("tick,stepMs,allocBytes,gc0,gc1,gc2,westVillagers,westSoldiers,eastVillagers,eastSoldiers,westBarracks,eastBarracks,westTowns,eastTowns,westHouses,eastHouses,westResourceRemaining,eastResourceRemaining,totalUnits,westFood,westWood,westStone,westMetal,eastFood,eastWood,eastStone,eastMetal,westIdle,eastIdle,westWorking,eastWorking,movedSoldiers,visibilityChangedSoldiers,visibilityProcessedCells,bridgeSearchCalls,bridgeCandidateStartCellsInspected,bridgeCandidatesEvaluated,bridgeRouteEvaluations,bridgeDistanceMapRequests,bridgeDistanceMapsBuilt,bridgeDistanceMapCacheHits,bridgeDistanceMapCells,bridgeLegacyDistanceMapCells,bridgeBasePassableBuilds,bridgeBasePassableCellsScanned,bridgeSourceCellsConsidered,bridgeDestinationCellsConsidered,placementSiteCandidatesInspected,placementSiteClearChecks,placementConnectivityChecks,placementConnectivityCacheHits,placementConnectivityVisitedCells,phases,westLatePushAdvantageTick,eastLatePushAdvantageTick," + LargeEconomyHeader());
        }
        StreamWriter routeTrace = null;
        if (options.TryGetValue("--route-trace-out", out var routeTracePath))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(routeTracePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            routeTrace = new StreamWriter(routeTracePath, false, new UTF8Encoding(false));
            routeTrace.WriteLine("tick,faction,lineKind,calls,searchCells,searchMs");
            simulation.RouteDiagnosticClock = System.Diagnostics.Stopwatch.GetTimestamp;
        }
        StreamWriter ageTrace = null;
        var priorAges = new Dictionary<uint, int>();
        var ageTicks = new Dictionary<uint, Dictionary<int, long>>();
        var latePushAdvantageTicks = new Dictionary<uint, long> { [1] = -1, [2] = -1 };
        int[] peakSoldiers = { 0, 0 };
        priorAges[1] = simulation.Capture(1).Economy?.Age ?? 0;
        priorAges[2] = simulation.Capture(2).Economy?.Age ?? 0;
        if (options.TryGetValue("--age-trace-out", out var ageTracePath))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(ageTracePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            ageTrace = new StreamWriter(ageTracePath, false, new UTF8Encoding(false));
            ageTrace.WriteLine("tick,faction,age");
        }
        LargeEconomyObservation largeObservation = null;
        long[] craftTraceArmsZeroAge2Ticks = { 0, 0 };
        for (long i = 0; i < ticks && !simulation.Capture(1).Result.HasEnded; i++)
        {
            long tick = simulation.Capture(1).Tick;
            QueueHumanOrders(westHuman, tick, 1, simulation, gateway, lines);
            QueueHumanOrders(eastHuman, tick, 2, simulation, gateway, lines);
            foreach (var signal in westSignals.Where(x => x.Tick == tick))
            {
                if (west == null) throw new InvalidDataException("--west-signalを送れません（west tacticがautoです）: " + signal.Name);
                if (!west.SendSignal(signal.Name, signal.Point, out var reason))
                    throw new InvalidDataException("--west-signalを送れません: " + (reason ?? signal.Name));
            }
            var westResult = west?.Tick();
            var eastResult = east?.Tick();
            WriteHostLog(lines, westResult, 1, westName);
            WriteHostLog(lines, eastResult, 2, eastName);
            pack?.RecordTactic(westResult, 1, westName);
            pack?.RecordTactic(eastResult, 2, eastName);
            if (timingWriter != null) phaseMs.Clear();
            long stepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            long allocatedBefore = timingWriter == null ? 0 : GC.GetAllocatedBytesForCurrentThread();
            int gc0Before = timingWriter == null ? 0 : GC.CollectionCount(0);
            int gc1Before = timingWriter == null ? 0 : GC.CollectionCount(1);
            int gc2Before = timingWriter == null ? 0 : GC.CollectionCount(2);
            gateway.Step();
            for (uint faction = 1; faction <= 2; faction++)
            {
                int soldiers = simulation.Capture(faction).Observation.OwnArmies.Sum(a => a.AliveCount);
                if (soldiers > peakSoldiers[faction - 1]) peakSoldiers[faction - 1] = soldiers;
                if (craftTrace != null)
                {
                    var economy = simulation.Capture(faction).Economy;
                    if (economy != null && economy.Age >= 2 && economy.ArmsKit + economy.FineArmsKit == 0)
                        craftTraceArmsZeroAge2Ticks[faction - 1]++;
                }
            }
            for (uint faction = 1; faction <= 2; faction++)
            {
                int age = simulation.Capture(faction).Economy?.Age ?? 0;
                if (age != priorAges[faction])
                {
                    if (ageTrace != null)
                        ageTrace.WriteLine(simulation.Capture(faction).Tick.ToString(CultureInfo.InvariantCulture) + "," + faction.ToString(CultureInfo.InvariantCulture) + "," + age.ToString(CultureInfo.InvariantCulture));
                    if (!ageTicks.TryGetValue(faction, out var factionAgeTicks))
                        ageTicks[faction] = factionAgeTicks = new Dictionary<int, long>();
                    factionAgeTicks[age] = simulation.Capture(faction).Tick;
                    priorAges[faction] = age;
                }
            }
            for (uint faction = 1; faction <= 2; faction++)
                if (latePushAdvantageTicks[faction] < 0
                    && Battle.LatePushAdvantageCondition(simulation.Capture(faction).Observation, scenario.Economy))
                    latePushAdvantageTicks[faction] = simulation.Capture(faction).Tick;
            largeObservation = (timingWriter != null || economyTrace != null || routeTrace != null || craftTrace != null)
                && (ShouldCaptureLargeObservation(simulation.Capture(1).Tick, ticks)
                    || simulation.Capture(1).Result.HasEnded || simulation.Capture(1).Tick == ticks)
                ? simulation.CaptureLargeEconomyObservation() : null;
            if (exploreTrace != null)
            {
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var frame = simulation.Capture(faction);
                    uint enemyCore = faction == 1 ? 2U : 1U;
                    var core = frame.Objectives.FirstOrDefault(o => o.Kind == GoalKind.Core && o.Id == enemyCore);
                    if (firstEnemyCoreSeen[faction - 1] < 0 && core.LastSeenTick > 0)
                        firstEnemyCoreSeen[faction - 1] = checked((int)core.LastSeenTick);
                }
                long traceTick = simulation.Capture(1).Tick;
                if (traceTick % 1000 == 0)
                    WriteExploreTrace(exploreTrace, scenario, simulation.Capture(1), simulation.Capture(2), firstEnemyCoreSeen);
            }
            if (resourceFogTrace != null)
            {
                var frame = simulation.Capture(1);
                if (frame.Tick == 3000 || frame.Tick == 6000 || frame.Tick == 12000)
                    WriteResourceFogTrace(resourceFogTrace, scenario, frame, simulation.Capture(2));
            }
            if (researchTrace != null)
            {
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var economy = simulation.Capture(faction).Economy;
                    if (economy == null) continue;
                    if (economy.AgeFourAiResearchFailureTick > (priorResearchFailureTicks.TryGetValue(faction, out var failureTick) ? failureTick : 0))
                    {
                        researchTrace.WriteLine(JsonSerializer.Serialize(new
                        {
                            @event = "research-failure", faction, tick = economy.AgeFourAiResearchFailureTick,
                            tech = economy.AgeFourAiResearchFailureTech.ToString(),
                            reason = ResearchFailureReasonName(economy.AgeFourAiResearchFailureReason),
                            food = economy.Food, wood = economy.Wood, metal = economy.Metal,
                            ore = economy.Ore, gems = economy.Gems
                        }));
                        priorResearchFailureTicks[faction] = economy.AgeFourAiResearchFailureTick;
                    }
                    if (!priorResearch.TryGetValue(faction, out var factionPriorResearch))
                    {
                        factionPriorResearch = new Dictionary<uint, TechKind>();
                        priorResearch[faction] = factionPriorResearch;
                    }
                    int booksBefore = priorBooks.TryGetValue(faction, out var savedBooks) ? savedBooks : 0;
                    if (!firstBooksThresholdTick.ContainsKey(faction) && booksBefore < economy.BooksPerResearch && economy.Books >= economy.BooksPerResearch)
                    {
                        firstBooksThresholdTick[faction] = simulation.Capture(faction).Tick;
                        researchTrace.WriteLine(JsonSerializer.Serialize(new { @event = "books-threshold", faction, tick = simulation.Capture(faction).Tick, books = economy.Books }));
                    }
                    var current = new Dictionary<uint, TechKind>();
                    foreach (var building in economy.Buildings.Where(b => b.FactionId == faction))
                    {
                        if (building.Researching != 0)
                        {
                            current[building.Id] = building.Researching;
                            if (!factionPriorResearch.TryGetValue(building.Id, out var prior) || prior == 0)
                            {
                                int starts = researchStarts.TryGetValue(faction, out var oldStarts) ? oldStarts + 1 : 1;
                                researchStarts[faction] = starts;
                                researchTrace.WriteLine(JsonSerializer.Serialize(new
                                {
                                    @event = "research-start", faction, tick = simulation.Capture(faction).Tick,
                                    building = building.Kind.ToString(), tech = building.Researching.ToString(), books = economy.Books, starts
                                }));
                            }
                        }
                        else if (factionPriorResearch.TryGetValue(building.Id, out var priorCompleted) && priorCompleted != 0)
                        {
                            int completions = researchCompletions.TryGetValue(faction, out var oldCompletions) ? oldCompletions + 1 : 1;
                            researchCompletions[faction] = completions;
                            researchTrace.WriteLine(JsonSerializer.Serialize(new
                            {
                                @event = "research-complete", faction, tick = simulation.Capture(faction).Tick,
                                building = building.Kind.ToString(), tech = priorCompleted.ToString(), completions
                            }));
                        }
                    }
                    priorResearch[faction] = current;
                    priorBooks[faction] = economy.Books;
                }
            }
            if (craftTrace != null && simulation.Capture(1).Tick % 1000 == 0)
            {
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var economy = simulation.Capture(faction).Economy;
                    if (economy == null) continue;
                    int Metalworks = economy.Buildings.Count(b => b.FactionId == faction && b.Kind == BuildingKind.Metalworks);
                    int RopeHuts = economy.Buildings.Count(b => b.FactionId == faction && b.Kind == BuildingKind.RopeHut);
                    int Smelters = economy.Buildings.Count(b => b.FactionId == faction && b.Kind == BuildingKind.Smelter);
                    int researchCount = CountBits(economy.Techs);
                    var assemblers = economy.Buildings.Where(b => b.FactionId == faction && b.Kind == BuildingKind.Assembler)
                        .OrderBy(b => b.Id)
                        .Select(b => new
                        {
                            id = b.Id, recipe = b.Recipe.ToString(), timer = b.TrainRemaining,
                            input = b.Input, inputSecondary = b.InputSecondary, inputTertiary = b.InputTertiary
                        }).ToArray();
                    int planksInAssemblers = assemblers.Where(b => b.recipe == RecipeKind.Frames.ToString()
                            || b.recipe == RecipeKind.ArmsKit.ToString()
                            || b.recipe == RecipeKind.Scholarly.ToString())
                        .Sum(b => b.input);
                    int foodInAssemblers = assemblers.Where(b => b.recipe == RecipeKind.Scholarly.ToString())
                        .Sum(b => b.inputTertiary);
                    craftTrace.WriteLine(JsonSerializer.Serialize(new
                    {
                        @event = "sample", tick = simulation.Capture(faction).Tick, faction,
                        age = economy.Age, food = economy.Food, wood = economy.Wood, planks = economy.Planks,
                        planksProduced = largeObservation.Factions[faction - 1].PlanksProduced,
                        planksUsedWeapons = largeObservation.Factions[faction - 1].PlanksUsedWeapons,
                        planksUsedConstruction = largeObservation.Factions[faction - 1].PlanksUsedConstruction,
                        planksUsedScholarly = largeObservation.Factions[faction - 1].PlanksUsedScholarly,
                        planksUsedBridges = largeObservation.Factions[faction - 1].PlanksUsedBridges,
                        planksUsedOther = largeObservation.Factions[faction - 1].PlanksUsedOther,
                        sawmillsCompleted = largeObservation.Factions[faction - 1].SawmillsCompleted,
                        sawmillsActive = largeObservation.Factions[faction - 1].SawmillsActive,
                        sawmillPlanks = largeObservation.Factions[faction - 1].SawmillPlanks,
                        haulers = largeObservation.Factions[faction - 1].Haulers,
                        armsStock = largeObservation.Factions[faction - 1].ArmsStock,
                        armsProduced = largeObservation.Factions[faction - 1].ArmsProduced,
                        armsUsedBySoldiers = largeObservation.Factions[faction - 1].ArmsUsedBySoldiers,
                        fittings = economy.Fittings, fittingsProduced = largeObservation.Factions[faction - 1].FittingsProduced,
                        fittingsConsumed = largeObservation.Factions[faction - 1].FittingsConsumed,
                        rope = economy.Rope, ropeProduced = largeObservation.Factions[faction - 1].RopeProduced,
                        ropeConsumed = largeObservation.Factions[faction - 1].RopeConsumed,
                        armsWithFittings = largeObservation.Factions[faction - 1].ArmsWithFittings,
                        armsWithoutFittings = largeObservation.Factions[faction - 1].ArmsWithoutFittings,
                        fittingsUse = largeObservation.Factions[faction - 1].FittingsUse,
                        ropeUse = largeObservation.Factions[faction - 1].RopeUse,
                        metalworks = Metalworks, ropeHuts = RopeHuts, smelters = Smelters, researchCount,
                        armsZeroAge2Ticks = craftTraceArmsZeroAge2Ticks[faction - 1],
                        planksInAssemblers, foodInAssemblers, assemblers
                    }));
                }
                craftTrace.Flush();
            }
            if (timingWriter != null)
            {
                var westFrame = simulation.Capture(1);
                var eastFrame = simulation.Capture(2);
                int westVillagers = CountOwnVillagers(westFrame.Economy);
                int eastVillagers = CountOwnVillagers(eastFrame.Economy);
                int westSoldiers = westFrame.Observation.OwnArmies.Sum(a => a.AliveCount);
                int eastSoldiers = eastFrame.Observation.OwnArmies.Sum(a => a.AliveCount);
                int westBarracks = westFrame.Economy?.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Barracks) ?? 0;
                int eastBarracks = eastFrame.Economy?.Buildings.Count(b => b.FactionId == 2 && b.Kind == BuildingKind.Barracks) ?? 0;
                int westTowns = westFrame.Economy?.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Town) ?? 0;
                int eastTowns = eastFrame.Economy?.Buildings.Count(b => b.FactionId == 2 && b.Kind == BuildingKind.Town) ?? 0;
                int westHouses = westFrame.Economy?.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.House) ?? 0;
                int eastHouses = eastFrame.Economy?.Buildings.Count(b => b.FactionId == 2 && b.Kind == BuildingKind.House) ?? 0;
                int westResourceRemaining = westFrame.Economy?.Resources.Sum(resource => resource.Remaining) ?? 0;
                int eastResourceRemaining = eastFrame.Economy?.Resources.Sum(resource => resource.Remaining) ?? 0;
                int westFood = westFrame.Economy?.Food ?? 0;
                int westWood = westFrame.Economy?.Wood ?? 0;
                int westStone = westFrame.Economy?.Stone ?? 0;
                int westMetal = westFrame.Economy?.Metal ?? 0;
                int eastFood = eastFrame.Economy?.Food ?? 0;
                int eastWood = eastFrame.Economy?.Wood ?? 0;
                int eastStone = eastFrame.Economy?.Stone ?? 0;
                int eastMetal = eastFrame.Economy?.Metal ?? 0;
                int westIdle = CountActivity(westFrame.Economy, VillagerActivity.Idle);
                int eastIdle = CountActivity(eastFrame.Economy, VillagerActivity.Idle);
                int westWorking = CountWorking(westFrame.Economy);
                int eastWorking = CountWorking(eastFrame.Economy);
                var phases = phaseMs.Where(p => !p.Key.StartsWith("alloc:", StringComparison.Ordinal)
                    && (Environment.GetEnvironmentVariable("RTS_ALLOC_PHASES") == "1" || !p.Key.StartsWith("allocKB:", StringComparison.Ordinal)));
                string phaseText = string.Join(";", phases.Select(p => p.Key + "=" + p.Value.ToString("0.000", CultureInfo.InvariantCulture)));
                timingWriter.WriteLine(string.Join(",", simulation.Capture(1).Tick.ToString(CultureInfo.InvariantCulture),
                    ((System.Diagnostics.Stopwatch.GetTimestamp() - stepStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("0.000", CultureInfo.InvariantCulture),
                    (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore).ToString(CultureInfo.InvariantCulture),
                    (GC.CollectionCount(0) - gc0Before).ToString(CultureInfo.InvariantCulture),
                    (GC.CollectionCount(1) - gc1Before).ToString(CultureInfo.InvariantCulture),
                    (GC.CollectionCount(2) - gc2Before).ToString(CultureInfo.InvariantCulture),
                    westVillagers.ToString(CultureInfo.InvariantCulture), westSoldiers.ToString(CultureInfo.InvariantCulture),
                    eastVillagers.ToString(CultureInfo.InvariantCulture), eastSoldiers.ToString(CultureInfo.InvariantCulture),
                    westBarracks.ToString(CultureInfo.InvariantCulture), eastBarracks.ToString(CultureInfo.InvariantCulture),
                    westTowns.ToString(CultureInfo.InvariantCulture), eastTowns.ToString(CultureInfo.InvariantCulture),
                    westHouses.ToString(CultureInfo.InvariantCulture), eastHouses.ToString(CultureInfo.InvariantCulture),
                    westResourceRemaining.ToString(CultureInfo.InvariantCulture), eastResourceRemaining.ToString(CultureInfo.InvariantCulture),
                    (westVillagers + westSoldiers + eastVillagers + eastSoldiers).ToString(CultureInfo.InvariantCulture),
                    westFood.ToString(CultureInfo.InvariantCulture), westWood.ToString(CultureInfo.InvariantCulture),
                    westStone.ToString(CultureInfo.InvariantCulture), westMetal.ToString(CultureInfo.InvariantCulture),
                    eastFood.ToString(CultureInfo.InvariantCulture), eastWood.ToString(CultureInfo.InvariantCulture),
                    eastStone.ToString(CultureInfo.InvariantCulture), eastMetal.ToString(CultureInfo.InvariantCulture),
                    westIdle.ToString(CultureInfo.InvariantCulture), eastIdle.ToString(CultureInfo.InvariantCulture),
                     westWorking.ToString(CultureInfo.InvariantCulture), eastWorking.ToString(CultureInfo.InvariantCulture),
                     simulation.MovedSoldiers.ToString(CultureInfo.InvariantCulture),
                     simulation.VisibilityChangedSoldiers.ToString(CultureInfo.InvariantCulture),
                     simulation.VisibilityProcessedCells.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeSearchCalls.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeCandidateStartCellsInspected.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeCandidatesEvaluated.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeRouteEvaluations.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeDistanceMapRequests.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeDistanceMapsBuilt.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeDistanceMapCacheHits.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeDistanceMapCells.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeLegacyDistanceMapCells.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeBasePassableBuilds.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeBasePassableCellsScanned.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeSourceCellsConsidered.ToString(CultureInfo.InvariantCulture),
                     simulation.BridgeDestinationCellsConsidered.ToString(CultureInfo.InvariantCulture),
                     simulation.PlacementSiteCandidatesInspected.ToString(CultureInfo.InvariantCulture),
                     simulation.PlacementSiteClearChecks.ToString(CultureInfo.InvariantCulture),
                     simulation.PlacementConnectivityChecks.ToString(CultureInfo.InvariantCulture),
                     simulation.PlacementConnectivityCacheHits.ToString(CultureInfo.InvariantCulture),
                     simulation.PlacementConnectivityVisitedCells.ToString(CultureInfo.InvariantCulture),
                     phaseText,
                     latePushAdvantageTicks[1].ToString(CultureInfo.InvariantCulture), latePushAdvantageTicks[2].ToString(CultureInfo.InvariantCulture),
                     LargeEconomyCsv(largeObservation)));
                if (simulation.Capture(1).Tick % 100 == 0) timingWriter.Flush();
            }
            if (routeTrace != null && simulation.Capture(1).Tick % 1000 == 0)
            {
                WriteRouteTrace(routeTrace, simulation);
                routeTrace.Flush();
            }
            if (economyTrace != null && (largeObservation != null || simulation.Capture(1).Result.HasEnded || simulation.Capture(1).Tick == ticks))
            {
                var observation = largeObservation ?? simulation.CaptureLargeEconomyObservation();
                WriteEconomyTrace(economyTrace, scenario, simulation, observation);
            }
            RecordHumanReplayDiscards(simulation, lines, loggedDiscarded);
            if (westAuto != null && !simulation.Capture(1).Result.HasEnded) westAuto.Step(simulation.Capture(1));
            if (eastAuto != null && !simulation.Capture(2).Result.HasEnded) eastAuto.Step(simulation.Capture(2));
            pack?.RecordAfterStep(simulation);
        }
        timingWriter?.Dispose();
        routeTrace?.Dispose();
        ageTrace?.Dispose();
        craftTrace?.Dispose();
        economyTrace?.Dispose();
        west?.Dispose();
        east?.Dispose();
        pack?.Complete(simulation, gateway.Inputs, ticks);
        resourceFogTrace?.WriteLine(JsonSerializer.Serialize(new
        {
            @event = "summary",
            tick = simulation.Capture(1).Tick,
            ended = simulation.Capture(1).Result.HasEnded,
            winner = simulation.Capture(1).Result.WinnerFactionId,
            factions = new[] { simulation.Capture(1).Economy?.Civ.ToString(), simulation.Capture(2).Economy?.Civ.ToString() }
        }));
        resourceFogTrace?.Dispose();
        if (exploreTrace != null)
        {
            exploreTrace.WriteLine(JsonSerializer.Serialize(new
            {
                @event = "summary",
                tick = simulation.Capture(1).Tick,
                ended = simulation.Capture(1).Result.HasEnded,
                winner = simulation.Capture(1).Result.WinnerFactionId,
                firstEnemyCoreSeen
            }));
            exploreTrace.Dispose();
        }
        if (researchTrace != null)
        {
            researchTrace.WriteLine(JsonSerializer.Serialize(new
            {
                @event = "summary",
                factions = new[] { 1, 2 }.Select(faction => new
                {
                    faction,
                    thresholdTick = firstBooksThresholdTick.TryGetValue((uint)faction, out var threshold) ? threshold : -1,
                    researchStarts = researchStarts.TryGetValue((uint)faction, out var starts) ? starts : 0,
                    researchCompletions = researchCompletions.TryGetValue((uint)faction, out var completions) ? completions : 0,
                    finalBooks = simulation.Capture((uint)faction).Economy?.Books ?? 0
                }).ToArray()
            }));
        }
        researchTrace?.Dispose();
        if (options.TryGetValue("--log-out", out var logPath)) File.WriteAllLines(logPath, lines, new UTF8Encoding(false));
        if (options.TryGetValue("--out", out var outputPath))
        {
            using var output = File.Create(outputPath);
            ReplayRunner.Record(output, scenario, gateway.Inputs, ticks, new BuildIdentity(), null, "none", "none");
        }
        var result = simulation.Capture(1).Result;
        Console.WriteLine("tactic-match: ticks=" + simulation.Capture(1).Tick.ToString(CultureInfo.InvariantCulture) + " ended=" + result.HasEnded + " winner=" + result.WinnerFactionId.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("late-push-advantage: faction1=" + latePushAdvantageTicks[1].ToString(CultureInfo.InvariantCulture)
            + " faction2=" + latePushAdvantageTicks[2].ToString(CultureInfo.InvariantCulture));
        for (uint faction = 1; faction <= 2; faction++)
        {
            if (!ageTicks.TryGetValue(faction, out var factionAgeTicks)) factionAgeTicks = new Dictionary<int, long>();
            Console.WriteLine("ages: faction=" + faction.ToString(CultureInfo.InvariantCulture)
                + " age1=" + (factionAgeTicks.TryGetValue(1, out var age1) ? age1 : -1).ToString(CultureInfo.InvariantCulture)
                + " age2=" + (factionAgeTicks.TryGetValue(2, out var age2) ? age2 : -1).ToString(CultureInfo.InvariantCulture)
                + " age3=" + (factionAgeTicks.TryGetValue(3, out var age3) ? age3 : -1).ToString(CultureInfo.InvariantCulture)
                + " age4=" + (factionAgeTicks.TryGetValue(4, out var age4) ? age4 : -1).ToString(CultureInfo.InvariantCulture));
        }
        if (scenario.Economy.LargeEconomy)
        {
            var outcomeObservation = simulation.CaptureLargeEconomyObservation();
            Console.WriteLine("outcome: reason=" + (result.IsFault ? "fault" : result.IsAgeVictory ? "age-victory" : result.HasEnded ? "core-destroyed" : "undecided")
                + " tick=" + outcomeObservation.Tick.ToString(CultureInfo.InvariantCulture)
                + " core1Hp=" + outcomeObservation.Factions[0].CoreHp.ToString(CultureInfo.InvariantCulture)
                + " core1Attackers=" + outcomeObservation.Factions[0].EnemySoldiersAtCore.ToString(CultureInfo.InvariantCulture)
                + " core2Hp=" + outcomeObservation.Factions[1].CoreHp.ToString(CultureInfo.InvariantCulture)
                + " core2Attackers=" + outcomeObservation.Factions[1].EnemySoldiersAtCore.ToString(CultureInfo.InvariantCulture));
        }
        for (uint faction = 1; faction <= 2; faction++)
        {
            var economy = simulation.Capture(faction).Economy;
            if (economy == null) continue;
            int dropSites = economy.Buildings.Count(b => b.FactionId == faction && b.Kind == BuildingKind.DropSite);
            int resources = economy.Resources.Sum(r => r.Remaining);
            Console.WriteLine("faction=" + faction.ToString(CultureInfo.InvariantCulture)
                + " dropSites=" + dropSites.ToString(CultureInfo.InvariantCulture)
                + " food=" + economy.Food.ToString(CultureInfo.InvariantCulture)
                + " wood=" + economy.Wood.ToString(CultureInfo.InvariantCulture)
                + " villagers=" + CountOwnVillagers(economy).ToString(CultureInfo.InvariantCulture)
                + " beltCells=" + economy.Belts.Count.ToString(CultureInfo.InvariantCulture)
                + " resourceRemaining=" + resources.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("craft4: faction=" + faction.ToString(CultureInfo.InvariantCulture)
                + " assemblers=" + economy.Buildings.Count(b => b.FactionId == faction && b.Kind == BuildingKind.Assembler).ToString(CultureInfo.InvariantCulture)
                + " siegeWorkshops=" + economy.Buildings.Count(b => b.FactionId == faction && b.Kind == BuildingKind.SiegeWorkshop).ToString(CultureInfo.InvariantCulture)
                + " siegeWorkshopComplete=" + economy.Buildings.Count(b => b.FactionId == faction && b.Kind == BuildingKind.SiegeWorkshop && b.Complete).ToString(CultureInfo.InvariantCulture)
                + " siegeWorkshopQueued=" + economy.Buildings.Where(b => b.FactionId == faction && b.Kind == BuildingKind.SiegeWorkshop).Sum(b => b.Queued).ToString(CultureInfo.InvariantCulture)
                + " siegeParts=" + economy.SiegeParts.ToString(CultureInfo.InvariantCulture)
                + " mechanism=" + economy.Mechanism.ToString(CultureInfo.InvariantCulture)
                + " siegeEngine=" + economy.SiegeEngine.ToString(CultureInfo.InvariantCulture)
                + " siegeEngineProduced=" + economy.SiegeEngineProduced.ToString(CultureInfo.InvariantCulture)
                + " siegeEngineConsumed=" + economy.SiegeEngineConsumed.ToString(CultureInfo.InvariantCulture)
                + " eliteKit=" + economy.EliteKit.ToString(CultureInfo.InvariantCulture)
                + " eliteKitProduced=" + economy.EliteKitProduced.ToString(CultureInfo.InvariantCulture)
                + " eliteKitConsumed=" + economy.EliteKitConsumed.ToString(CultureInfo.InvariantCulture)
                + " cannonProduced=" + economy.CannonProduced.ToString(CultureInfo.InvariantCulture)
                + " peakSoldiers=" + peakSoldiers[faction - 1].ToString(CultureInfo.InvariantCulture));
        }
        return result.IsFault ? 4 : 0;
    }

    private static int ParseScale(Dictionary<string, string> options, string key, int fallback)
    {
        if (!options.TryGetValue(key, out var text)) return fallback;
        int value = int.Parse(text, CultureInfo.InvariantCulture);
        if (value < 1 || value > 64) throw new InvalidDataException(key + " は1〜64の整数で指定してください。");
        return value;
    }

    private static int ParsePermille(string key, string text)
    {
        int value = int.Parse(text, CultureInfo.InvariantCulture);
        if (value < 1 || value > 10000) throw new InvalidDataException(key + "は1〜10000の整数で指定してください。");
        return value;
    }

    private static int CountOwnVillagers(EconomyView economy)
    {
        if (economy == null) return 0;
        int count = 0;
        for (int i = 0; i < economy.Villagers.Count; i++)
            if (economy.Villagers[i].IsOwn) count++;
        return count;
    }

    private static int CountActivity(EconomyView economy, VillagerActivity activity)
        => economy?.Villagers.Count(value => value.IsOwn && value.Activity == activity) ?? 0;

    private static int CountWorking(EconomyView economy)
        => economy?.Villagers.Count(value => value.IsOwn && value.Activity != VillagerActivity.Idle) ?? 0;

    private static string LargeEconomyHeader()
    {
        var cells = new List<string>();
        for (int faction = 1; faction <= 2; faction++)
        {
            string prefix = faction == 1 ? "westLarge" : "eastLarge";
            cells.AddRange(new[] { prefix + "Population", prefix + "PopulationLimit", prefix + "PlannedTowns", prefix + "TownsBuilding", prefix + "CompletedTowns",
                prefix + "TownVillagersQueued", prefix + "TownVillagersProduced", prefix + "TownResourceIncome",
                prefix + "TownConnectedLines", prefix + "CoreNearBuildings40m", prefix + "CoreFarBuildings40m",
                prefix + "Houses", prefix + "Barracks", prefix + "DropSites", prefix + "Farms", prefix + "ConnectedFarms", prefix + "BeltCells", prefix + "BeltComponents", prefix + "BeltDelivered", prefix + "BeltDropped",
                prefix + "BeltDroppedFood", prefix + "BeltDroppedWood", prefix + "BeltDroppedStone", prefix + "BeltDroppedOre", prefix + "BeltDroppedGold", prefix + "BeltDroppedMetal", prefix + "BeltWaits", prefix + "BeltDropOffAiSentClusters", prefix + "BeltDropOffAiLines",
                prefix + "LumberCamps", prefix + "OtherBuildings",
                prefix + "MinimumDropSiteDistanceMeters", prefix + "CoreHp", prefix + "EnemySoldiersAtCore", prefix + "PlannedHouses", prefix + "HouseLimitStopTicks",
                prefix + "BarracksResourceWaiting", prefix + "BarracksFoodWaiting", prefix + "BarracksWoodWaiting", prefix + "BarracksMetalWaiting",
                prefix + "BarracksPopulationWaiting", prefix + "BarracksTraining",
                prefix + "AiFactoryConnectedLines", prefix + "AiFactoryPrimitiveConnectedLines", prefix + "AiFactoryAge1ConnectedLines",
                prefix + "AiFactoryAge2MaterialConnectedLines", prefix + "AiFactoryAge2MetalConnectedLines", prefix + "AiFactoryAge3PartsConnectedLines",
                prefix + "AiFactoryBarracksResourceWaiting", prefix + "PlanksProduced", prefix + "PlanksConsumed",
                prefix + "CutStoneProduced", prefix + "CutStoneConsumed", prefix + "StoneToolsProduced", prefix + "StoneToolsConsumed",
                prefix + "JerkyProduced", prefix + "JerkyConsumed", prefix + "FramesProduced", prefix + "FramesConsumed",
                prefix + "ArmsKitProduced", prefix + "ArmsKitConsumed", prefix + "ArmsScaleAssemblersAdded", prefix + "ArmsScaleSawmillsAdded", prefix + "SawmillsCompleted",
                prefix + "PlanksUsedWeapons", prefix + "PlanksUsedConstruction", prefix + "PlanksUsedScholarly", prefix + "PlanksUsedBridges", prefix + "PlanksUsedOther", prefix + "TrainingArmsUnavailable", prefix + "SiegePartsProduced", prefix + "SiegePartsConsumed",
                prefix + "MetalProduced", prefix + "MetalConsumed", prefix + "FittingsProduced", prefix + "FittingsConsumed",
                prefix + "RopeProduced", prefix + "RopeConsumed", prefix + "GearsProduced", prefix + "GearsConsumed", prefix + "GearShortageFailedPlacements", prefix + "AiFactorySkippedUnknownResource",
                 prefix + "AiFactorySkippedNoSite", prefix + "AiFactorySkippedNoRoute", prefix + "AiExpandTownSkippedUnknownResource",
                  prefix + "AiExpandTownSkippedNoSite", prefix + "AiExpandTownSkippedWood", prefix + "AiExpandTownSkippedStone",
                  prefix + "ScholarlyProduced", prefix + "ScholarlyConsumed", prefix + "ScholarlyStock", prefix + "ResearchScholarlyBlocked",
                  prefix + "Builders", prefix + "TownBuilders", prefix + "FactoryBuilders", prefix + "OtherBuilders",
                 prefix + "MovingVillagers", prefix + "Haulers", prefix + "IdleWorkers" });
            foreach (string resource in new[] { "Food", "Wood", "Stone", "Ore", "Gold", "Metal" })
                cells.Add(prefix + resource + "Workers");
            foreach (string resource in new[] { "Food", "Wood", "Stone", "Ore", "Gold", "Metal" })
                cells.Add(prefix + resource + "RoundTripTicks");
            foreach (string resource in new[] { "Food", "Wood", "Stone", "Ore", "Gold", "Metal" })
                cells.Add(prefix + resource + "Income");
        }
        return string.Join(",", cells);
    }

    private static bool ShouldCaptureLargeObservation(long tick, long requestedTicks)
        => tick % 1000 == 0 || tick == 7200 || tick == 10800 || tick == 14400 || tick == 18000
            || tick == requestedTicks;

    private static string LargeEconomyCsv(LargeEconomyObservation observation)
    {
        var cells = new List<string>();
        for (int faction = 0; faction < 2; faction++)
        {
            if (observation == null)
            {
                for (int i = 0; i < 117; i++) cells.Add("");
                continue;
            }
            var value = observation.Factions[faction];
            cells.AddRange(new[] { value.Population, value.PopulationLimit, value.PlannedTowns, value.TownsBuilding, value.CompletedTowns, value.TownVillagersQueued,
                value.TownVillagersProduced, value.TownResourceIncome }.Select(FormatInvariant));
            cells.Add(string.Join("|", value.TownConnectedLines.Select(FormatInvariant)));
            cells.AddRange(new[] { value.CoreNearBuildings, value.CoreFarBuildings, value.Houses, value.Barracks, value.DropSites,
                value.Farms, value.ConnectedFarms, value.BeltCells, value.BeltComponents, value.BeltDelivered, value.BeltDropped,
                value.BeltDroppedByResource[0], value.BeltDroppedByResource[1], value.BeltDroppedByResource[2], value.BeltDroppedByResource[3], value.BeltDroppedByResource[4], value.BeltDroppedByResource[5], value.BeltWaits,
                value.BeltDropOffAiSentClusters, value.BeltDropOffAiLines, value.LumberCamps, value.OtherBuildings,
                value.MinimumDropSiteDistanceMeters, value.CoreHp, value.EnemySoldiersAtCore, value.PlannedHouses, checked((int)value.HouseLimitStopTicks),
                value.BarracksResourceWaiting, value.BarracksFoodWaiting, value.BarracksWoodWaiting, value.BarracksMetalWaiting,
                value.BarracksPopulationWaiting, value.BarracksTraining }.Select(FormatInvariant));
            cells.AddRange(new[] { value.AiFactoryConnectedLines, value.AiFactoryPrimitiveConnectedLines, value.AiFactoryAge1ConnectedLines,
                value.AiFactoryAge2MaterialConnectedLines, value.AiFactoryAge2MetalConnectedLines, value.AiFactoryAge3PartsConnectedLines,
                value.AiFactoryBarracksResourceWaiting, value.PlanksProduced, value.PlanksConsumed, value.CutStoneProduced, value.CutStoneConsumed,
                value.StoneToolsProduced, value.StoneToolsConsumed, value.JerkyProduced, value.JerkyConsumed, value.FramesProduced, value.FramesConsumed,
                value.ArmsKitProduced, value.ArmsKitConsumed, value.ArmsScaleAssemblersAdded, value.ArmsScaleSawmillsAdded,
                value.SawmillsCompleted, value.PlanksUsedWeapons, value.PlanksUsedConstruction, value.PlanksUsedScholarly,
                value.PlanksUsedBridges, value.PlanksUsedOther, value.TrainingArmsUnavailable,
                value.SiegePartsProduced, value.SiegePartsConsumed, value.MetalProduced, value.MetalConsumed,
                value.FittingsProduced, value.FittingsConsumed, value.RopeProduced, value.RopeConsumed,
                value.GearsProduced, value.GearsConsumed, value.GearShortageFailedPlacements,
                value.AiFactorySkippedUnknownResource, value.AiFactorySkippedNoSite, value.AiFactorySkippedNoRoute,
                value.AiExpandTownSkippedUnknownResource, value.AiExpandTownSkippedNoSite, value.AiExpandTownSkippedWood,
                 value.AiExpandTownSkippedStone, value.ScholarlyProduced, value.ScholarlyConsumed, value.ScholarlyStock,
                 value.ResearchScholarlyBlocked }.Select(FormatInvariant));
            cells.AddRange(new[] { value.Builders, value.TownBuilders, value.FactoryBuilders, value.OtherBuilders,
                value.MovingVillagers, value.Haulers, value.IdleWorkers }.Select(FormatInvariant));
            cells.AddRange(value.Workers.Select(FormatInvariant));
            cells.AddRange(value.WorkerRoundTripTicks.Select(FormatInvariant));
            cells.AddRange(value.Income.Select(FormatInvariant));
        }
        return string.Join(",", cells);
    }

    private static string FormatInvariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int CountBits(ulong value)
    {
        int count = 0;
        while (value != 0) { value &= value - 1; count++; }
        return count;
    }

    private static void WriteRouteTrace(StreamWriter writer, Battle simulation)
    {
        for (uint faction = 1; faction <= 2; faction++)
            for (int rawKind = 0; rawKind <= 20; rawKind++)
            {
                var kind = (ProcessingLineKind)rawKind;
                long calls = simulation.RouteDiagnosticCalls(faction, kind);
                long cells = simulation.RouteDiagnosticSearchCells(faction, kind);
                if (calls == 0 && cells == 0) continue;
                string name = rawKind == 0 ? "Unknown" : kind.ToString();
                double milliseconds = simulation.RouteDiagnosticElapsedTicks(faction, kind) * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency;
                writer.WriteLine(string.Join(",", simulation.Capture(1).Tick.ToString(CultureInfo.InvariantCulture),
                    faction.ToString(CultureInfo.InvariantCulture), name,
                    calls.ToString(CultureInfo.InvariantCulture), cells.ToString(CultureInfo.InvariantCulture),
                    milliseconds.ToString("0.000", CultureInfo.InvariantCulture)));
            }
    }

    private static void WriteEconomyTrace(StreamWriter writer, ScenarioDefinition scenario, Battle simulation, LargeEconomyObservation observation)
    {
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            @event = "checkpoint", tick = observation.Tick, ended = simulation.Capture(1).Result.HasEnded,
            winner = simulation.Capture(1).Result.WinnerFactionId,
            factions = observation.Factions.Select(f => new
            {
                faction = f.FactionId, villagers = f.Villagers, soldiers = f.Soldiers,
                workers = f.Workers, toResource = f.ToResourceWorkers, gathering = f.GatheringWorkers,
                toDropOff = f.ToDropOffWorkers, builders = f.Builders, haulers = f.Haulers, idle = f.IdleWorkers,
                roundTripTicks = f.WorkerRoundTripTicks, roundTripMeters = f.WorkerRoundTripMeters,
                villagerIncome = f.Income, beltIncome = f.BeltIncome,
                food = simulation.Capture(f.FactionId).Economy?.Food ?? 0,
                wood = simulation.Capture(f.FactionId).Economy?.Wood ?? 0,
                stone = simulation.Capture(f.FactionId).Economy?.Stone ?? 0,
                ore = simulation.Capture(f.FactionId).Economy?.Ore ?? 0,
                gold = simulation.Capture(f.FactionId).Economy?.Gold ?? 0,
                beltCells = f.BeltCells, beltComponents = f.BeltComponents, lumberCamps = f.LumberCamps, dropSites = f.DropSites,
                woodMachines = f.WoodMachines, stoneMachines = f.StoneMachines,
                woodMachinesActive = f.WoodMachinesActive, stoneMachinesActive = f.StoneMachinesActive,
                woodMachineOutputFull = f.WoodMachineOutputFull, stoneMachineOutputFull = f.StoneMachineOutputFull,
                towns = f.CompletedTowns, townsBuilding = f.TownsBuilding, factoryLines = f.AiFactoryConnectedLines,
                 aiExpandSkippedWood = f.AiExpandTownSkippedWood, aiExpandSkippedStone = f.AiExpandTownSkippedStone,
                scholarlyProduced = f.ScholarlyProduced, scholarlyConsumed = f.ScholarlyConsumed,
                scholarlyStock = f.ScholarlyStock, researchScholarlyBlocked = f.ResearchScholarlyBlocked,
                gears = simulation.Capture(f.FactionId).Economy?.Gears ?? 0,
                gearsProduced = f.GearsProduced, gearsConsumed = f.GearsConsumed,
                gearShortageFailedPlacements = f.GearShortageFailedPlacements,
                uses = Enumerable.Range(0, LargeEconomyUseKind.Count).Select(kind => new
                {
                    kind = LargeEconomyUseKind.Names[kind], wood = f.Uses[kind, 0], stone = f.Uses[kind, 1]
                }).ToArray()
            }).ToArray()
        }));
        writer.Flush();
    }

    private static void WriteResourceFogTrace(StreamWriter writer, ScenarioDefinition scenario, FactionFrame west, FactionFrame east)
    {
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            tick = west.Tick,
            allResourcePoints = scenario.ResourceNodes.Length,
            factions = new[] { west, east }.Select(frame => new
            {
                factionId = frame.FactionId,
                food = frame.Economy?.Food ?? 0,
                wood = frame.Economy?.Wood ?? 0,
                villagers = frame.Economy?.Villagers?.Count ?? 0,
                civ = frame.Economy?.Civ.ToString(),
                knownResourcePoints = scenario.Economy.ResourceFog
                    ? scenario.ResourceNodes.Count(node => frame.Fog.ExploredCells[CellOf(scenario, node.Position)])
                    : scenario.ResourceNodes.Length,
                shownResourcePoints = frame.Economy?.Resources?.Count ?? 0
            })
        }));
        writer.Flush();
    }

    private static void WriteExploreTrace(StreamWriter writer, ScenarioDefinition scenario,
        FactionFrame west, FactionFrame east, int[] firstEnemyCoreSeen)
    {
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            @event = "checkpoint",
            tick = west.Tick,
            exploreScouting = scenario.Economy.ExploreScouting,
            factions = new[] { west, east }.Select((frame, index) => new
            {
                factionId = frame.FactionId,
                exploredPermille = frame.Fog.ExploredCells.Count(value => value) * 1000 / Math.Max(1, frame.Fog.ExploredCells.Count),
                scoutAlive = frame.Observation.OwnArmies.Where(army => army.Kind == UnitKind.Scout).Sum(army => army.AliveCount),
                patrollingScouts = frame.Observation.OwnArmies.Where(army => army.Kind == UnitKind.Scout
                    && army.PatrolObjective.Kind == GoalKind.Point).Sum(army => army.AliveCount),
                firstEnemyCoreSeenTick = firstEnemyCoreSeen[index],
                knownResourcePoints = scenario.ResourceNodes.Count(node => frame.Fog.ExploredCells[CellOf(scenario, node.Position)]),
                knownResourceClusters = KnownResourceClusters(scenario, frame)
            })
        }));
        writer.Flush();
    }

    private static int KnownResourceClusters(ScenarioDefinition scenario, FactionFrame frame)
    {
        int radius = Math.Max(1, scenario.Economy.LargeResourceClusterRadiusMeters);
        long radiusRaw = Fix64.FromInt(radius).Raw;
        long radiusSquared = radiusRaw * radiusRaw;
        var known = new bool[scenario.ResourceNodes.Length];
        for (int i = 0; i < known.Length; i++)
            known[i] = frame.Fog.ExploredCells[CellOf(scenario, scenario.ResourceNodes[i].Position)];
        int clusters = 0;
        for (int start = 0; start < known.Length; start++)
        {
            if (!known[start]) continue;
            clusters++;
            var queue = new Queue<int>();
            known[start] = false;
            queue.Enqueue(start);
            while (queue.Count != 0)
            {
                var node = scenario.ResourceNodes[queue.Dequeue()];
                for (int i = 0; i < known.Length; i++)
                {
                    if (!known[i] || scenario.ResourceNodes[i].Kind != node.Kind) continue;
                    long dx = scenario.ResourceNodes[i].Position.X.Raw - node.Position.X.Raw;
                    long dz = scenario.ResourceNodes[i].Position.Z.Raw - node.Position.Z.Raw;
                    if (dx * dx + dz * dz <= radiusSquared)
                    {
                        known[i] = false;
                        queue.Enqueue(i);
                    }
                }
            }
        }
        return clusters;
    }

    private static int CellOf(ScenarioDefinition scenario, SimPoint point)
        => (int)(point.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
            + (int)(point.X.Raw / 65536 / scenario.Map.CellSizeMeters);

    private static void ReserveCivilisation(string civText, uint faction, CommandGateway gateway)
    {
        if (string.IsNullOrWhiteSpace(civText)) return;
        if (!Enum.TryParse(civText, true, out CivKind civ) || civ == CivKind.Primitive || !Enum.IsDefined(civ))
            throw new InvalidDataException("--west-civ/--east-civ は有効な文明名を指定してください: " + civText);
        gateway.SubmitEconomy(EconomyCommand.Advance(faction, faction, civ));
    }

    private static TacticHost CreateHost(string name, uint faction, IFrameSource source, CommandGateway gateway, string runtimes, string paramText)
    {
        if (name == "auto") return null;
        ITacticRuntime runtime;
        if (name == "idle") runtime = new IdleTactic();
        else if (name == "rush") runtime = new RushTactic();
        else
        {
            var loaded = TacticFolder.Load(name, runtimes);
            if (!loaded.IsSuccess) throw new InvalidDataException("戦術フォルダを読み込めません: " + loaded.Error);
            runtime = loaded.Runtime;
        }
        var host = new TacticHost(faction, source, gateway, gateway, runtime,
            versions: scope => gateway.FactionVersions(faction).Versions(scope));
        foreach (var assignment in (paramText ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = assignment.IndexOf('=');
            if (equals <= 0) throw new InvalidDataException("パラメータはname=valueで指定してください: " + assignment);
            string namePart = assignment.Substring(0, equals);
            string valuePart = assignment.Substring(equals + 1);
            if (!host.SetParam(namePart, ParseParamValue(valuePart)))
                throw new InvalidDataException("戦術パラメータが不正です: " + assignment);
        }
        return host;
    }

    private static object ParseParamValue(string text)
    {
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        return text;
    }

    private static string SetupJson(ScenarioDefinition scenario, uint faction)
    {
        return "{\"matchSeed\":" + scenario.Seed.ToString(CultureInfo.InvariantCulture) + ",\"factionId\":" + faction.ToString(CultureInfo.InvariantCulture) + "}";
    }

    // "auto" deliberately adds no logged input: it is the simulation's existing hands-off AI.
    private static PresetController CreateAuto(string name, uint faction, CommandGateway gateway) => null;

    private static List<HumanOrderRecord> LoadHumanOrders(string path, uint faction, long ticks)
    {
        if (string.IsNullOrWhiteSpace(path)) return new List<HumanOrderRecord>();
        var records = HumanOrderJson.Read(path);
        foreach (var record in records)
        {
            if (record.Tick < 0) throw new InvalidDataException("human-orders の tick が負です。");
            record.ToIntent(faction);
        }
        return records.Where(x => x.Tick < ticks).OrderBy(x => x.Tick).ThenBy(x => x.Sequence).ToList();
    }

    private static void QueueHumanOrders(IReadOnlyList<HumanOrderRecord> records, long tick, uint faction,
        Battle simulation, CommandGateway gateway, List<string> lines)
    {
        var due = records.Where(x => x.Tick == tick).ToList();
        if (due.Count == 0) return;
        int missing = 0, submitted = 0;
        foreach (var group in due.GroupBy(x => x.Sequence == 0 ? 1UL : x.Sequence).OrderBy(x => x.Key))
        {
            var valid = new List<UserPolicyIntent>();
            foreach (var record in group)
            {
                if (!TargetExists(simulation.Capture(faction), record.Target)) { missing++; continue; }
                valid.Add(record.ToIntent(faction));
            }
            if (valid.Count != 0)
            {
                gateway.SubmitBatch(valid);
                submitted += valid.Count;
            }
        }
        lines.Add(JsonSerializer.Serialize(new
        {
            tick,
            faction,
            humanReplay = new { submitted, targetMissing = missing }
        }));
    }

    private static bool TargetExists(FactionFrame frame, HumanScope target)
    {
        if (target == null || !Enum.TryParse(target.Kind, true, out ScopeKind kind)) return false;
        if (kind == ScopeKind.All) return target.Id == 0;
        if (kind == ScopeKind.Army) return frame.Observation.OwnArmies.Any(x => x.Id == target.Id);
        if (kind == ScopeKind.Outpost) return frame.Objectives.Any(x => x.Kind == GoalKind.Outpost && x.Id == target.Id && x.IsOwnerKnown && x.OwnerFactionId == frame.FactionId);
        if (kind == ScopeKind.Region) return frame.Regions.Any(x => x.Id == target.Id);
        return false;
    }

    private static void RecordHumanReplayDiscards(Battle simulation, List<string> lines, HashSet<ulong> logged)
    {
        for (uint faction = 1; faction <= 2; faction++)
        {
            var discarded = simulation.Capture(faction).Commands
                .Where(x => x.Source == CommandSource.Human && (x.Status == CommandStatus.Impossible || x.Status == CommandStatus.Expired || x.Status == CommandStatus.Cancelled))
                .Where(x => logged.Add(x.CommandId)).ToArray();
            foreach (var command in discarded)
                lines.Add(JsonSerializer.Serialize(new
                {
                    tick = simulation.Capture(faction).Tick,
                    faction,
                    humanReplay = new { discarded = 1, commandId = command.CommandId, reason = command.Reason.ToString(), status = command.Status.ToString() }
                }));
        }
    }

    private static void WriteHostLog(List<string> lines, TacticHostTickResult result, uint faction, string name)
    {
        if (result == null || !result.Called) return;
        var record = new
        {
            tick = result.Tick,
            faction,
            tactic = name,
            sentPolicies = result.SentPolicies,
            sentEconomy = result.SentEconomy,
            console = result.ConsoleLines,
            commands = result.CommandJson,
            rejected = result.Commands.Rejected.Select(x => new { index = x.Index, type = x.Type, reason = x.Reason }).ToArray(),
            failure = result.Failure == null ? null : new { reason = result.Failure.Reason, consecutive = result.Failure.Consecutive },
            paramChanges = result.ParamChanges.Select(x => new { tick = x.Tick, name = x.Name, from = x.From, to = x.To }).ToArray(),
            signals = result.Signals.Select(x => new { tick = x.Tick, name = x.Name, point = x.Point.HasValue ? new { x = (decimal)x.Point.Value.X.Raw / 65536m, z = (decimal)x.Point.Value.Z.Raw / 65536m } : null }).ToArray(),
            disabled = result.Disabled
        };
        lines.Add(JsonSerializer.Serialize(record));
    }

    private static void ValidateTactic(string name)
    {
        if (name == "idle" || name == "rush" || name == "auto") return;
        if (!Directory.Exists(name)) throw new InvalidDataException("戦術はidle、rush、auto、または存在するフォルダパスです。");
    }
    private static string ResearchFailureReasonName(byte reason)
        => reason == 1 ? "prerequisite" : reason == 2 ? "building" : reason == 3 ? "researching" : reason == 4 ? "resource" : "unknown";

    private static IReadOnlyList<ScheduledSignal> ParseSignals(string text)
    {
        var result = new List<ScheduledSignal>();
        foreach (var raw in (text ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0 || colon == raw.Length - 1) throw new InvalidDataException("--west-signalはtick:nameまたはtick:name@x,zです: " + raw);
            if (!long.TryParse(raw.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tick) || tick < 0)
                throw new InvalidDataException("--west-signalのtickが不正です: " + raw);
            string value = raw.Substring(colon + 1);
            int at = value.IndexOf('@');
            string name = at < 0 ? value : value.Substring(0, at);
            if (string.IsNullOrEmpty(name)) throw new InvalidDataException("--west-signalのnameが空です: " + raw);
            SimPoint? point = null;
            if (at >= 0)
            {
                string[] coordinates = value.Substring(at + 1).Split(',');
                if (coordinates.Length != 2 || !decimal.TryParse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    || !decimal.TryParse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    throw new InvalidDataException("--west-signalの地点はx,zです: " + raw);
                point = new SimPoint(Fix(x), Fix(z));
            }
            result.Add(new ScheduledSignal { Tick = tick, Name = name, Point = point });
        }
        return result;
    }
    private static Fix64 Fix(decimal value)
    {
        decimal raw = decimal.Round(value * 65536m, 0, MidpointRounding.ToEven);
        return Fix64.FromRaw(checked((long)raw));
    }
    private static string Required(Dictionary<string, string> options, string key) => options.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Missing " + key);
}
