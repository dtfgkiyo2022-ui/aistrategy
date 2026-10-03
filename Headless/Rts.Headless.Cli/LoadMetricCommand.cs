using System.Globalization;
using System.Text;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Headless.Cli;

internal sealed class LoadMetricDelta
{
    internal int VillagerIdle;
    internal int NodeExhausted;
    internal int BuildingDone;
    internal int TrainingDone;
    internal int ResearchDone;
    internal int PopulationCap;
    internal int LineCut;
    internal int MaterialWait;
    internal int NewContact;
    internal int ArmyHpHalf;
    internal int OutpostOwnerChanged;
    internal int CoreAttacked;
    internal int AgeReady;

    internal int TotalEvents => VillagerIdle + NodeExhausted + BuildingDone + TrainingDone + ResearchDone
        + PopulationCap + LineCut + MaterialWait + NewContact + ArmyHpHalf + OutpostOwnerChanged + CoreAttacked + AgeReady;
}

/// <summary>
/// Display-only, one-frame-difference event detector for the load metric. It never calls back into the simulation.
/// </summary>
internal sealed class LoadMetricDetector
{
    internal const int DefaultIdleTicks = 20;
    internal const int CoreAttackEndTicks = 200;

    private readonly FactionFrame[] previous = new FactionFrame[2];
    private readonly Dictionary<uint, int>[] maximumHp = { new(), new() };
    private readonly Dictionary<uint, bool>[] materialWaiting = { new(), new() };
    private readonly Dictionary<uint, int>[] idleStreak = { new(), new() };
    private readonly bool[] coreAttackActive = new bool[2];
    private readonly int[] coreAttackQuietTicks = new int[2];
    private readonly int idleTicks;

    internal LoadMetricDetector(int idleTicks = DefaultIdleTicks)
    {
        if (idleTicks <= 0) throw new ArgumentOutOfRangeException(nameof(idleTicks));
        this.idleTicks = idleTicks;
    }

    internal LoadMetricDelta Observe(FactionFrame frame)
    {
        if (frame == null || frame.FactionId < 1 || frame.FactionId > 2) throw new ArgumentException("Invalid faction frame.");
        int index = checked((int)frame.FactionId - 1);
        var old = previous[index];
        if (old == null)
        {
            RememberHp(index, frame);
            RememberMaterialWait(index, frame);
            RememberIdle(index, frame);
            previous[index] = frame;
            return new LoadMetricDelta();
        }

        var result = Difference(old, frame, maximumHp[index], materialWaiting[index]);
        result.CoreAttacked = ObserveCoreAttack(index, old, frame);
        result.VillagerIdle = ObserveIdle(index, frame);
        RememberHp(index, frame);
        RememberMaterialWait(index, frame);
        previous[index] = frame;
        return result;
    }

    internal static LoadMetricDelta Difference(FactionFrame old, FactionFrame current)
    {
        if (old == null || current == null) throw new ArgumentNullException(old == null ? nameof(old) : nameof(current));
        if (old.FactionId != current.FactionId) throw new ArgumentException("Frames belong to different factions.");
        return Difference(old, current, new Dictionary<uint, int>(), new Dictionary<uint, bool>());
    }

    private static LoadMetricDelta Difference(FactionFrame old, FactionFrame current, Dictionary<uint, int> maxHp,
        Dictionary<uint, bool> previousMaterialWait)
    {
        var result = new LoadMetricDelta();
        if (old.Economy != null && current.Economy != null)
        {
            result.NodeExhausted = RemovedCount(old.Economy.Resources.Select(v => v.Id), current.Economy.Resources.Select(v => v.Id));
            result.BuildingDone = BuildingCompletions(old.Economy, current.Economy, current.FactionId);
            result.TrainingDone = TrainingCompletions(old.Economy, current.Economy, current.FactionId);
            result.ResearchDone = ResearchCompletions(old.Economy, current.Economy, current.FactionId);
            result.PopulationCap = old.Economy.Population < old.Economy.PopulationCap
                && current.Economy.Population >= current.Economy.PopulationCap ? 1 : 0;
            result.LineCut = DifferenceCount(old.Economy.Lines.Where(v => v.FactionId == current.FactionId).Select(v => v.Id),
                current.Economy.Lines.Where(v => v.FactionId == current.FactionId).Select(v => v.Id));
            result.MaterialWait = MaterialWaitTransitions(old.Economy, current.Economy, current.FactionId, previousMaterialWait);
            result.AgeReady = WasAgeReady(old.Economy, current.Economy) ? 1 : 0;
        }

        result.NewContact = DifferenceCount(old.Observation?.Contacts.Select(v => v.ContactId) ?? Array.Empty<uint>(),
            current.Observation?.Contacts.Select(v => v.ContactId) ?? Array.Empty<uint>());
        result.ArmyHpHalf = ArmyHpHalfCrossings(old, current, maxHp);
        result.OutpostOwnerChanged = OwnerChanges(old, current, GoalKind.Outpost);
        result.CoreAttacked = CoreHpDrop(old, current) ? 1 : 0;
        return result;
    }

    private void RememberIdle(int index, FactionFrame frame)
    {
        idleStreak[index].Clear();
        if (frame.Economy == null) return;
        foreach (var villager in frame.Economy.Villagers.Where(v => v.IsOwn))
            if (villager.Activity == VillagerActivity.Idle)
                idleStreak[index][villager.Id] = 1;
    }

    private int ObserveIdle(int index, FactionFrame frame)
    {
        if (frame.Economy == null)
        {
            idleStreak[index].Clear();
            return 0;
        }

        var currentIds = new HashSet<uint>();
        int result = 0;
        foreach (var villager in frame.Economy.Villagers.Where(v => v.IsOwn))
        {
            currentIds.Add(villager.Id);
            if (villager.Activity != VillagerActivity.Idle)
            {
                idleStreak[index].Remove(villager.Id);
                continue;
            }

            int streak = idleStreak[index].GetValueOrDefault(villager.Id) + 1;
            idleStreak[index][villager.Id] = streak;
            if (streak == idleTicks) result++;
        }

        foreach (uint id in idleStreak[index].Keys.Where(id => !currentIds.Contains(id)).ToArray())
            idleStreak[index].Remove(id);
        return result;
    }

    private static int BuildingCompletions(EconomyView old, EconomyView current, uint faction)
    {
        var before = old.Buildings.Where(v => v.FactionId == faction).ToDictionary(v => v.Id);
        return current.Buildings.Where(v => v.FactionId == faction)
            .Count(v => before.TryGetValue(v.Id, out var prior) && !prior.Complete && v.Complete);
    }

    private static int TrainingCompletions(EconomyView old, EconomyView current, uint faction)
    {
        int completed = Positive(old.VillagerQueued - current.VillagerQueued);
        var before = old.Buildings.Where(v => v.FactionId == faction).ToDictionary(v => v.Id);
        foreach (var building in current.Buildings.Where(v => v.FactionId == faction))
            if (before.TryGetValue(building.Id, out var prior)) completed += Positive(prior.Queued - building.Queued);
        return completed;
    }

    private static int ResearchCompletions(EconomyView old, EconomyView current, uint faction)
    {
        int completed = BitCount(current.Techs & ~old.Techs);
        var before = old.Buildings.Where(v => v.FactionId == faction).ToDictionary(v => v.Id);
        foreach (var building in current.Buildings.Where(v => v.FactionId == faction))
            if (before.TryGetValue(building.Id, out var prior) && prior.Researching != 0 && building.Researching == 0)
                completed++;
        return completed;
    }

    private static int MaterialWaitTransitions(EconomyView old, EconomyView current, uint faction,
        Dictionary<uint, bool> previousMaterialWait)
    {
        var before = old.Buildings.Where(v => v.FactionId == faction).ToDictionary(v => v.Id);
        int result = 0;
        foreach (var building in current.Buildings.Where(v => v.FactionId == faction))
        {
            bool now = IsMaterialWaiting(building);
            bool was = before.TryGetValue(building.Id, out var prior) ? IsMaterialWaiting(prior) : previousMaterialWait.GetValueOrDefault(building.Id);
            if (!was && now) result++;
        }
        return result;
    }

    // EconomyView does not expose the internal processing timer or required recipe amounts. A completed processing
    // building with an empty input slot and no output is therefore the conservative display-only proxy we can derive.
    private static bool IsMaterialWaiting(BuildingView building)
    {
        if (!building.Complete || building.Output != 0) return false;
        return building.Kind == BuildingKind.Smelter || building.Kind == BuildingKind.CharcoalKiln
            ? building.Input == 0
            : building.Kind == BuildingKind.Steelworks
                ? building.Input == 0 || building.InputSecondary == 0
                : building.Kind == BuildingKind.Fletcher && (building.Input == 0 || building.InputSecondary == 0);
    }

    private static bool WasAgeReady(EconomyView old, EconomyView current)
    {
        return !AgeReady(old) && AgeReady(current);
    }

    private static bool AgeReady(EconomyView economy)
    {
        if (!economy.Ages || economy.AdvancingTo != CivKind.Primitive || economy.AdvanceRemaining != 0) return false;
        int food = economy.Age == 0 ? economy.AdvanceFoodCost : economy.Age == 1 ? economy.Age2FoodCost : economy.Age3FoodCost;
        int wood = economy.Age == 0 ? economy.AdvanceWoodCost : economy.Age == 1 ? economy.Age2WoodCost : economy.Age3WoodCost;
        if (food <= 0 && wood <= 0 && economy.NextAgeGoldCost <= 0) return false;
        if (economy.Food < food || economy.Wood < wood || economy.Gold < economy.NextAgeGoldCost) return false;
        if (economy.VillagerTrainRemaining != 0) return false;
        return economy.Buildings.All(v => v.FactionId != 0 && (v.TrainRemaining == 0 && v.ResearchRemaining == 0));
    }

    private static int ArmyHpHalfCrossings(FactionFrame old, FactionFrame current, Dictionary<uint, int> maximum)
    {
        var before = old.Units.Where(v => v.IsOwn && v.HasHp).ToDictionary(v => v.Id);
        int result = 0;
        foreach (var unit in current.Units.Where(v => v.IsOwn && v.HasHp))
        {
            int max = maximum.TryGetValue(unit.Id, out var known) ? Math.Max(known, unit.Hp) : unit.Hp;
            maximum[unit.Id] = max;
            if (before.TryGetValue(unit.Id, out var prior) && prior.Hp * 2 > max && unit.Hp * 2 <= max) result++;
        }
        return result;
    }

    private static int OwnerChanges(FactionFrame old, FactionFrame current, GoalKind kind)
    {
        var before = old.Objectives.Where(v => v.Kind == kind && v.IsOwnerKnown).ToDictionary(v => v.Id);
        return current.Objectives.Count(v => v.Kind == kind && v.IsOwnerKnown && before.TryGetValue(v.Id, out var prior)
            && prior.OwnerFactionId != v.OwnerFactionId);
    }

    private static bool CoreHpDrop(FactionFrame old, FactionFrame current)
    {
        var before = old.Objectives.Where(v => v.Kind == GoalKind.Core && v.Id == current.FactionId && v.IsHpKnown).ToDictionary(v => v.Id);
        return current.Objectives.Any(v => v.Kind == GoalKind.Core && v.Id == current.FactionId && v.IsHpKnown
            && before.TryGetValue(v.Id, out var prior) && v.Hp < prior.Hp);
    }

    private int ObserveCoreAttack(int index, FactionFrame old, FactionFrame current)
    {
        bool dropped = CoreHpDrop(old, current);
        if (dropped)
        {
            bool starts = !coreAttackActive[index] || coreAttackQuietTicks[index] >= CoreAttackEndTicks;
            coreAttackActive[index] = true;
            coreAttackQuietTicks[index] = 0;
            return starts ? 1 : 0;
        }
        if (coreAttackActive[index])
        {
            long elapsed = Math.Max(1, current.Tick - old.Tick);
            coreAttackQuietTicks[index] = (int)Math.Min(CoreAttackEndTicks, coreAttackQuietTicks[index] + elapsed);
        }
        return 0;
    }

    private void RememberHp(int index, FactionFrame frame)
    {
        foreach (var unit in frame.Units.Where(v => v.IsOwn && v.HasHp))
            maximumHp[index][unit.Id] = Math.Max(maximumHp[index].GetValueOrDefault(unit.Id), unit.Hp);
    }

    private void RememberMaterialWait(int index, FactionFrame frame)
    {
        if (frame.Economy == null) return;
        foreach (var building in frame.Economy.Buildings.Where(v => v.FactionId == frame.FactionId))
            materialWaiting[index][building.Id] = IsMaterialWaiting(building);
    }

    private static int DifferenceCount(IEnumerable<uint> old, IEnumerable<uint> current)
    {
        var before = new HashSet<uint>(old);
        return current.Count(id => before.Add(id));
    }

    private static int RemovedCount(IEnumerable<uint> old, IEnumerable<uint> current)
    {
        var remaining = new HashSet<uint>(current);
        return old.Count(id => !remaining.Contains(id));
    }

    private static int Positive(int value) => Math.Max(0, value);

    private static int BitCount(ulong value)
    {
        int count = 0;
        while (value != 0) { value &= value - 1; count++; }
        return count;
    }
}

internal sealed class LoadMetricRow
{
    internal ulong Seed;
    internal uint Faction;
    internal int Minute;
    internal LoadMetricDelta Events = new();
    internal int Buildings;
    internal int BeltCells;
    internal int Armies;
    internal int Fronts;
    internal int Soldiers;
    internal int Villagers;

    internal string Csv()
    {
        var e = Events;
        return string.Join(",", Seed.ToString(CultureInfo.InvariantCulture), Faction.ToString(CultureInfo.InvariantCulture),
            Minute.ToString(CultureInfo.InvariantCulture), e.VillagerIdle.ToString(CultureInfo.InvariantCulture),
            e.NodeExhausted.ToString(CultureInfo.InvariantCulture), e.BuildingDone.ToString(CultureInfo.InvariantCulture),
            e.TrainingDone.ToString(CultureInfo.InvariantCulture), e.ResearchDone.ToString(CultureInfo.InvariantCulture),
            e.PopulationCap.ToString(CultureInfo.InvariantCulture), e.LineCut.ToString(CultureInfo.InvariantCulture),
            e.MaterialWait.ToString(CultureInfo.InvariantCulture), e.NewContact.ToString(CultureInfo.InvariantCulture),
            e.ArmyHpHalf.ToString(CultureInfo.InvariantCulture), e.OutpostOwnerChanged.ToString(CultureInfo.InvariantCulture),
            e.CoreAttacked.ToString(CultureInfo.InvariantCulture), e.AgeReady.ToString(CultureInfo.InvariantCulture),
            Buildings.ToString(CultureInfo.InvariantCulture), BeltCells.ToString(CultureInfo.InvariantCulture),
            Armies.ToString(CultureInfo.InvariantCulture), Fronts.ToString(CultureInfo.InvariantCulture),
            Soldiers.ToString(CultureInfo.InvariantCulture), Villagers.ToString(CultureInfo.InvariantCulture),
            e.TotalEvents.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class LoadMetricMatchResult
{
    internal ulong Seed;
    internal long Tick;
    internal bool HasEnded;
    internal uint WinnerFactionId;
    internal bool IsDraw;
    internal bool IsUndecided;
}

internal static class LoadMetricCommand
{
    private const int TicksPerMinute = 1200;
    private const string Header = "seed,faction,minute,villager_idle,node_exhausted,building_done,training_done,research_done,population_cap,line_cut,material_wait,new_contact,army_hp_half,outpost_owner_changed,core_attacked,age_ready,buildings,belt_cells,armies,fronts,soldiers,villagers,total_events";

    internal static int Run(Dictionary<string, string> options)
    {
        string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Missing " + key);
        var seeds = ParseSeeds(Required("--map-seed"));
        long ticks = long.Parse(options.GetValueOrDefault("--ticks") ?? "24000", CultureInfo.InvariantCulture);
        if (ticks <= 0 || ticks > 10000000) throw new InvalidDataException("--ticks must be positive and at most 10000000.");
        string west = options.GetValueOrDefault("--west-preset") ?? "maintain";
        string east = options.GetValueOrDefault("--east-preset") ?? "maintain";
        int idleTicks = int.Parse(options.GetValueOrDefault("--idle-ticks") ?? LoadMetricDetector.DefaultIdleTicks.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (idleTicks <= 0) throw new InvalidDataException("--idle-ticks must be positive.");
        string output = Path.GetFullPath(Required("--out"));
        string directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var rows = new List<LoadMetricRow>();
        var matches = new List<LoadMetricMatchResult>();
        foreach (ulong seed in seeds)
        {
            var scenario = CreateScenario(seed, options.ContainsKey("--all-civs"), options.ContainsKey("--large"), options.ContainsKey("--army-growth"));
            scenario.Economy.CoreDefence = options.ContainsKey("--core-defence");
            if (ticks > scenario.VerificationTickLimit) throw new InvalidDataException("--ticks is outside the scenario limit.");
            rows.AddRange(RunScenario(scenario, ticks, west, east, idleTicks: idleTicks,
                completed: simulation =>
                {
                    var result = simulation.Capture(1).Result;
                    matches.Add(new LoadMetricMatchResult { Seed = seed, Tick = simulation.Capture(1).Tick,
                        HasEnded = result.HasEnded, WinnerFactionId = result.WinnerFactionId,
                        IsDraw = result.IsDraw, IsUndecided = result.IsUndecided });
                }));
        }
        using (var writer = new StreamWriter(output, false, new UTF8Encoding(false)))
        {
            writer.WriteLine(Header);
            foreach (var row in rows) writer.WriteLine(row.Csv());
        }
        PrintSummary(rows, seeds.Count);
        PrintMatchResults(matches);
        return 0;
    }

    internal static ScenarioDefinition CreateScenario(ulong seed, bool allCivilisations, bool large = false, bool armyGrowth = false)
    {
        var scenario = large ? MapGenerator.GenerateLarge(seed, gold: allCivilisations) : MapGenerator.GenerateTerrain(seed, gold: allCivilisations);
        scenario.Economy.ArmyGrowth = armyGrowth;
        if (!allCivilisations) return scenario;
        // Keep this list identical to UnityHost/LiveMatchHost.allCivilisations.
        scenario.Economy.Forestry = true;
        scenario.Economy.Masonry = true;
        scenario.Economy.Caravan = true;
        scenario.Economy.Cavalry = true;
        scenario.Economy.Bridge = true;
        scenario.Economy.Academy = true;
        scenario.Economy.Cult = true;
        scenario.Economy.MonksEnabled = true;
        scenario.Economy.Mountain = true;
        scenario.Economy.FishingCiv = true;
        scenario.Economy.FishingEnabled = true;
        scenario.Economy.Tollgate = true;
        scenario.Economy.Metropolis = true;
        scenario.Economy.Sanctuary = true;
        return scenario;
    }

    internal static List<LoadMetricRow> RunScenario(ScenarioDefinition scenario, long ticks, string westPreset, string eastPreset,
        Action<long, Battle> afterStep = null, int idleTicks = LoadMetricDetector.DefaultIdleTicks,
        Action<Battle> completed = null)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        var simulation = new Battle(scenario);
        var gateway = new CommandGateway(simulation);
        var west = PolicyPresets.CreateController(westPreset ?? "maintain", 1, gateway);
        var east = PolicyPresets.CreateController(eastPreset ?? "maintain", 2, gateway);
        west.Initialize();
        east.Initialize();
        var detector = new LoadMetricDetector(idleTicks);
        detector.Observe(simulation.Capture(1));
        detector.Observe(simulation.Capture(2));
        var rows = new List<LoadMetricRow>();
        var minuteEvents = new[] { new LoadMetricDelta(), new LoadMetricDelta() };

        for (long i = 0; i < ticks && !simulation.Capture(1).Result.HasEnded; i++)
        {
            gateway.Step();
            var westFrame = simulation.Capture(1);
            if (westFrame.Result.HasEnded) break;
            var eastFrame = simulation.Capture(2);
            var westDelta = detector.Observe(westFrame);
            var eastDelta = detector.Observe(eastFrame);
            Accumulate(westDelta, minuteEvents[0]);
            Accumulate(eastDelta, minuteEvents[1]);
            west.Step(westFrame);
            east.Step(eastFrame);
            afterStep?.Invoke(westFrame.Tick, simulation);

            if (westFrame.Tick % TicksPerMinute == 0)
            {
                rows.Add(Row(scenario.Seed, westFrame, minuteEvents[0], (int)(westFrame.Tick / TicksPerMinute)));
                rows.Add(Row(scenario.Seed, eastFrame, minuteEvents[1], (int)(westFrame.Tick / TicksPerMinute)));
                minuteEvents[0] = new LoadMetricDelta();
                minuteEvents[1] = new LoadMetricDelta();
            }
        }
        completed?.Invoke(simulation);
        return rows;
    }

    private static void Accumulate(LoadMetricDelta source, LoadMetricDelta target)
    {
        target.VillagerIdle += source.VillagerIdle; target.NodeExhausted += source.NodeExhausted;
        target.BuildingDone += source.BuildingDone; target.TrainingDone += source.TrainingDone; target.ResearchDone += source.ResearchDone;
        target.PopulationCap += source.PopulationCap; target.LineCut += source.LineCut; target.MaterialWait += source.MaterialWait;
        target.NewContact += source.NewContact; target.ArmyHpHalf += source.ArmyHpHalf; target.OutpostOwnerChanged += source.OutpostOwnerChanged;
        target.CoreAttacked += source.CoreAttacked; target.AgeReady += source.AgeReady;
    }

    private static LoadMetricRow Row(ulong seed, FactionFrame frame, LoadMetricDelta events, int minute)
    {
        var economy = frame.Economy;
        var ownBuildings = economy?.Buildings.Where(v => v.FactionId == frame.FactionId).ToArray() ?? Array.Empty<BuildingView>();
        var ownVillagers = economy?.Villagers.Count(v => v.IsOwn) ?? 0;
        int fronts = frame.Objectives.Count(v => v.OwnerFactionId != frame.FactionId && v.IsOwnerKnown
            && (v.Kind == GoalKind.Core ? v.IsHpKnown : v.CaptureDurationTicks > 0));
        return new LoadMetricRow { Seed = seed, Faction = frame.FactionId, Minute = minute, Events = events,
            Buildings = ownBuildings.Length, BeltCells = economy?.Belts.Count(v => v.FactionId == frame.FactionId) ?? 0,
            Armies = frame.Observation?.OwnArmies.Count(v => v.AliveCount > 0) ?? 0,
            Fronts = fronts, Soldiers = frame.Units.Count(v => v.IsOwn && v.Kind != UnitKind.Villager), Villagers = ownVillagers };
    }

    private static List<ulong> ParseSeeds(string text)
    {
        var result = new List<ulong>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length == 1) result.Add(ulong.Parse(range[0], CultureInfo.InvariantCulture));
            else if (range.Length == 2)
            {
                ulong start = ulong.Parse(range[0], CultureInfo.InvariantCulture), end = ulong.Parse(range[1], CultureInfo.InvariantCulture);
                if (end < start || end - start > 10000) throw new InvalidDataException("Invalid --map-seed range.");
                for (ulong value = start; value <= end; value++) result.Add(value);
            }
            else throw new InvalidDataException("--map-seed must be a comma list or a-b range.");
        }
        if (result.Count == 0 || result.Contains(0)) throw new InvalidDataException("--map-seed must contain positive seeds.");
        if (result.Distinct().Count() != result.Count) throw new InvalidDataException("Duplicate map seed.");
        return result;
    }

    private static void PrintSummary(IReadOnlyList<LoadMetricRow> rows, int seedCount)
    {
        Console.WriteLine("summary: minute,avg_total_events,min_total_events,max_total_events");
        foreach (var group in rows.GroupBy(v => v.Minute).OrderBy(v => v.Key))
        {
            var totals = group.GroupBy(v => v.Seed).Select(v => v.Sum(row => row.Events.TotalEvents)).ToArray();
            Console.WriteLine(string.Join(",", group.Key.ToString(CultureInfo.InvariantCulture),
                totals.Average().ToString("0.00", CultureInfo.InvariantCulture), totals.Min().ToString(CultureInfo.InvariantCulture),
                totals.Max().ToString(CultureInfo.InvariantCulture)));
        }
        Console.WriteLine("summary_seeds=" + seedCount.ToString(CultureInfo.InvariantCulture));
    }

    private static void PrintMatchResults(IReadOnlyList<LoadMetricMatchResult> matches)
    {
        Console.WriteLine("matches: seed,tick,ended,winner,is_draw,is_undecided");
        foreach (var match in matches.OrderBy(v => v.Seed))
            Console.WriteLine(string.Join(",", match.Seed.ToString(CultureInfo.InvariantCulture),
                match.Tick.ToString(CultureInfo.InvariantCulture), match.HasEnded ? "1" : "0",
                match.WinnerFactionId.ToString(CultureInfo.InvariantCulture), match.IsDraw ? "1" : "0",
                match.IsUndecided ? "1" : "0"));
    }
}
