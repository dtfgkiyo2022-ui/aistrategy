using System.Globalization;
using System.Text.Json;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;

namespace Rts.Headless.Cli;

internal static class AnalyzeCommand
{
    internal static int Run(Dictionary<string, string> options, BuildIdentity build)
    {
        string input = options.GetValueOrDefault("--in"), scenario = options.GetValueOrDefault("--scenario");
        if ((input == null) == (scenario == null)) throw new InvalidDataException("Use exactly one of --in or --scenario.");
        if (input != null && options.Keys.Any(k => k is "--ticks" or "--west-preset" or "--east-preset"))
            throw new InvalidDataException("Scenario options cannot be used with --in.");
        string output = options.GetValueOrDefault("--out");
        if (output != null && new[] { input, scenario }.Where(p => p != null).Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Input and output must differ.");
        using Stream stream = input != null ? File.OpenRead(input) : new MemoryStream();
        if (scenario != null)
        {
            if (!options.TryGetValue("--ticks", out var limit)) throw new InvalidDataException("Missing --ticks.");
            long ticks = long.Parse(limit, CultureInfo.InvariantCulture);
            var definition = JsonInput.Scenario(scenario);
            string west = options.GetValueOrDefault("--west-preset") ?? "none", east = options.GetValueOrDefault("--east-preset") ?? "none";
            var inputs = PolicyPresets.RecordedInputs(definition, west, east, ticks);
            ReplayRunner.Record(stream, definition, inputs, ticks, build, null, west, east);
            stream.Position = 0;
        }
        var report = Analyze(stream, build, options.ContainsKey("--allow-build-mismatch"));
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (output == null) Console.WriteLine(json); else File.WriteAllText(output, json);
        return report.IsFault ? 4 : report.FirstMismatchTick.HasValue ? 2 : 0;
    }

    internal static IndicatorReport Analyze(Stream input, BuildIdentity build, bool allow = false, Action<DiagnosticState, byte[], byte[]> capture = null)
    {
        var counter = new IndicatorCounter();
        var outcome = ReplayRunner.Replay(input, build, (state, hash, events) =>
        {
            counter.Observe(state.Tick, DiagnosticComparison.Fields(state).ToDictionary(p => p.Key, p => p.Value));
            capture?.Invoke(state, hash, events);
        }, allow);
        counter.Report.FirstMismatchTick = outcome.FirstMismatchTick;
        counter.Report.IsFault = outcome.IsFault;
        return counter.Report;
    }
}

internal sealed class IndicatorReport
{
    public string TickConvention { get; } = "Transitions compare S(t-1) to S(t); durations count S1..SLast (S0 excluded). Factions: 1=west, 2=east. Retries count a new offense for any previously released goal.";
    public long LastTick { get; set; } = -1;
    public long? FirstCoreHitTick { get; set; }
    public long? EndTick { get; set; }
    public string Result { get; set; } = "Undecided";
    public long? FirstMismatchTick { get; set; }
    public bool IsFault { get; set; }
    public Dictionary<uint, FactionIndicators> Factions { get; } = new();
    public Dictionary<uint, ArmyIndicators> Armies { get; } = new();
    public Dictionary<uint, OutpostIndicators> Outposts { get; } = new();
    public BattleSnapshot FirstCoreHit { get; set; }
    public BattleSnapshot Final { get; set; }
}
internal sealed class FactionIndicators
{
    public int GatheringFailures { get; set; }
    public int Retries { get; set; }
    public long DefenseTicks { get; set; }
    public long ReserveShortfallTicks { get; set; }
    public Dictionary<string, long> PhaseTicks { get; } = new();
    public List<AdvanceEvent> Advances { get; } = new();
    public int AdvancedTotal => Advances.Sum(a => a.Alive);
    public double AdvancedAverage => Advances.Count == 0 ? 0 : (double)AdvancedTotal / Advances.Count;
    public List<OffenseEvent> Offenses { get; } = new();
    public int BornAfterFirstHit { get; set; }
    public int DiedAfterFirstHit { get; set; }
    public int CoreDefenseEntriesAfterFirstHit { get; set; }
}
internal sealed record AdvanceEvent(long Tick, ulong Id, string Goal, int Alive, uint[] Armies);
internal sealed record OffenseEvent(long Tick, string Event, ulong Id, string Goal, string Phase, long Deadline, long GatheredTick, int ReturningArmies, int InferiorArmies, int SuppressedGoals);
internal sealed class ArmyIndicators
{
    public uint Faction { get; set; }
    public long Guard { get; set; }
    public long Reserve { get; set; }
    public long CoreDefense { get; set; }
    public long Total => Guard + Reserve + CoreDefense;
}
internal sealed class OutpostIndicators
{
    public Dictionary<uint, long> OwnershipTicks { get; } = new() { [0] = 0, [1] = 0, [2] = 0 };
    public List<OwnerEvent> Changes { get; } = new();
}
internal sealed record OwnerEvent(long Tick, uint Owner);
internal sealed record BattleSnapshot(long Tick, Dictionary<uint, int> Alive, Dictionary<uint, int> CoreHp,
    Dictionary<uint, uint> Owners, Dictionary<uint, int> CoreDefenseAlive, Dictionary<uint, int> ArmyAlive, Dictionary<uint, string> Assignments);

// Owns only CLI aggregates and previous diagnostic values, never a Simulation reference.
internal sealed class IndicatorCounter
{
    public IndicatorReport Report { get; } = new();
    private Dictionary<string, string> previous;
    private readonly Dictionary<uint, HashSet<string>> released = new();
    private readonly Dictionary<uint, int> initialHp = new();
    private static long N(Dictionary<string, string> d, string key) => long.Parse(d[key], CultureInfo.InvariantCulture);
    private static uint[] Ids(Dictionary<string, string> d, string prefix) =>
        Enumerable.Range(0, checked((int)N(d, prefix + ".Count"))).Select(i => checked((uint)N(d, prefix + "[" + i + "]"))).ToArray();
    private static uint[] Entities(Dictionary<string, string> d, string prefix) =>
        d.Keys.Where(k => k.StartsWith(prefix + "[", StringComparison.Ordinal) && k.EndsWith("].Id", StringComparison.Ordinal))
            .Select(k => checked((uint)N(d, k))).OrderBy(x => x).ToArray();
    private static string Goal(Dictionary<string, string> d, string p) => ((GoalKind)N(d, p + "Goal.Kind")) + ":" + N(d, p + "Goal.Id");
    private static OffensivePhase Phase(Dictionary<string, string> d, string p) => (OffensivePhase)N(d, p + "Phase");
    private static bool Active(OffensivePhase p) => p != OffensivePhase.Idle;
    private static bool Gathering(OffensivePhase p) => p is OffensivePhase.Gathering or OffensivePhase.WaitingToAdvance;

    internal void Observe(long tick, Dictionary<string, string> d)
    {
        if (tick != Report.LastTick + 1) throw new InvalidDataException("Indicators require contiguous ticks beginning at S0.");
        var factions = Entities(d, "Factions");
        var soldiers = Entities(d, "Soldiers");
        var cores = Entities(d, "Cores");
        var outposts = Entities(d, "Outposts");
        var alive = factions.ToDictionary(f => f, f => (int)N(d, $"Factions[{f}].AliveCount"));
        var hp = cores.ToDictionary(c => c, c => (int)N(d, $"Cores[{c}].Hp"));
        var owners = outposts.ToDictionary(o => o, o => (uint)N(d, $"Outposts[{o}].OwnerFactionId"));
        var armyAlive = Entities(d, "Armies").ToDictionary(a => a, a => soldiers.Count(s => N(d, $"Soldiers[{s}].ArmyId") == a && N(d, $"Soldiers[{s}].Alive") != 0));
        var assignments = armyAlive.Keys.ToDictionary(a => a, a => ((AssignmentKind)N(d, $"Ai.Armies[{a}].Assignment")).ToString());
        // Core coordinates are immutable and not in diagnostics; count actual CoreDefense assignees instead of inferring positions.
        var defenseAlive = factions.ToDictionary(f => f, f => Ids(d, $"Factions[{f}].ArmyIds").Where(a => assignments[a] == "CoreDefense").Sum(a => armyAlive[a]));
        var snapshot = new BattleSnapshot(tick, alive, hp, owners, defenseAlive, armyAlive, assignments);
        if (tick == 0) foreach (var c in cores) initialHp[c] = hp[c];
        if (Report.FirstCoreHitTick == null && cores.Any(c => hp[c] < initialHp[c]))
        { Report.FirstCoreHitTick = tick; Report.FirstCoreHit = snapshot; }
        Report.Final = snapshot;
        if (N(d, "Result.IsFault") != 0) Report.Result = "Fault";
        else if (N(d, "Result.IsDraw") != 0) Report.Result = "Draw";
        else if (N(d, "Result.HasEnded") != 0)
            Report.Result = hp.Any(p => p.Value <= 0) ? "CoreDestroyed:" + string.Join(",", hp.Where(p => p.Value <= 0).Select(p => p.Key)) : "Undecided";
        if (N(d, "Result.HasEnded") != 0) Report.EndTick ??= tick;

        foreach (uint f in factions)
        {
            if (!Report.Factions.TryGetValue(f, out var metrics))
            { metrics = new FactionIndicators(); Report.Factions.Add(f, metrics); released.Add(f, new HashSet<string>()); }
            string p = $"Ai.Factions[{f}].Offense.";
            var phase = Phase(d, p); string goal = Goal(d, p); ulong id = ulong.Parse(d[p + "Id"], CultureInfo.InvariantCulture);
            var armies = Ids(d, $"Factions[{f}].ArmyIds");
            if (tick > 0)
            {
                metrics.PhaseTicks[phase.ToString()] = metrics.PhaseTicks.GetValueOrDefault(phase.ToString()) + 1;
                if (N(d, $"Ai.Factions[{f}].ReserveShortfall") > 0) metrics.ReserveShortfallTicks++;
            }
            bool newOffense = Active(phase) && (previous == null || !Active(Phase(previous, p)) || previous[p + "Id"] != d[p + "Id"]);
            if (previous != null && Active(Phase(previous, p)) && (!Active(phase) || previous[p + "Id"] != d[p + "Id"]))
            {
                if (Gathering(Phase(previous, p))) metrics.GatheringFailures++;
                released[f].Add(Goal(previous, p));
                metrics.Offenses.Add(Event("Released", previous));
            }
            if (newOffense)
            {
                if (released[f].Contains(goal)) metrics.Retries++;
                metrics.Offenses.Add(Event("Started", d));
            }
            if (phase == OffensivePhase.Advancing && previous != null && (Gathering(Phase(previous, p)) || newOffense))
            {
                var advancing = Ids(d, p + "Advancing");
                metrics.Advances.Add(new AdvanceEvent(tick, id, goal, advancing.Sum(a => armyAlive[a]), advancing));
            }
            foreach (uint a in armies)
            {
                if (!Report.Armies.TryGetValue(a, out var am)) { am = new ArmyIndicators { Faction = f }; Report.Armies.Add(a, am); }
                if (tick == 0) continue;
                var assignment = (AssignmentKind)N(d, $"Ai.Armies[{a}].Assignment");
                if (assignment == AssignmentKind.Guard) am.Guard++;
                if (assignment == AssignmentKind.Reserve) am.Reserve++;
                if (assignment == AssignmentKind.CoreDefense) am.CoreDefense++;
                if (assignment is AssignmentKind.Guard or AssignmentKind.Reserve or AssignmentKind.CoreDefense) metrics.DefenseTicks++;
                if (Report.FirstCoreHitTick < tick && assignment == AssignmentKind.CoreDefense && N(previous, $"Ai.Armies[{a}].Assignment") != (long)AssignmentKind.CoreDefense)
                    metrics.CoreDefenseEntriesAfterFirstHit++;
            }
            if (previous != null && Report.FirstCoreHitTick < tick)
                foreach (uint s in soldiers.Where(s => N(d, $"Soldiers[{s}].FactionId") == f))
                {
                    string key = $"Soldiers[{s}].Alive";
                    if (!previous.ContainsKey(key)) metrics.BornAfterFirstHit++;
                    else if (N(previous, key) != 0 && N(d, key) == 0) metrics.DiedAfterFirstHit++;
                }
            OffenseEvent Event(string kind, Dictionary<string, string> state) => new(tick, kind,
                ulong.Parse(state[p + "Id"], CultureInfo.InvariantCulture), Goal(state, p), Phase(state, p).ToString(),
                N(state, p + "MoveDeadlineTick"), N(state, p + "GatheredTick"),
                armies.Count(a => N(d, $"Ai.Armies[{a}].Returning") != 0),
                armies.Count(a => N(d, $"Ai.Armies[{a}].InferiorTicks") >= 40), (int)N(d, p + "SuppressedCount"));
        }
        foreach (uint o in outposts)
        {
            if (!Report.Outposts.TryGetValue(o, out var m)) { m = new OutpostIndicators(); Report.Outposts.Add(o, m); }
            if (tick > 0) m.OwnershipTicks[owners[o]]++;
            if (previous == null || N(previous, $"Outposts[{o}].OwnerFactionId") != owners[o]) m.Changes.Add(new OwnerEvent(tick, owners[o]));
        }
        previous = d;
        Report.LastTick = tick;
    }
}
