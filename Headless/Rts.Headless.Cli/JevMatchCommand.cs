using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Application;
using Rts.Contracts;
using Rts.Providers;
using Rts.Replay;

namespace Rts.Headless.Cli;

/// <summary>
/// Plays one match where a faction's autonomous upper policy comes from the external judgement model, and reports what
/// it cost and what it actually changed. This is the only command that touches the network: it needs a key and cannot
/// run in CI. The replay it writes is an ordinary one - it holds the resulting orders, never the model's answers - so
/// `replay` and `compare` verify it exactly as they verify any other match.
/// </summary>
internal static class JevMatchCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static int Run(Dictionary<string, string> options, BuildIdentity build)
    {
        string scenarioPath = options.TryGetValue("--scenario", out var path) ? path : throw new InvalidDataException("Missing --scenario.");
        var scenario = JsonInput.Scenario(scenarioPath);
        long ticks = Number(options, "--ticks", 6000);
        uint faction = (uint)Number(options, "--faction", 1);
        if (faction is not (1 or 2)) throw new InvalidDataException("--faction must be 1 or 2.");
        string keyVariable = options.GetValueOrDefault("--key-env") ?? "PROBE_KEY";
        var thresholds = new JevThresholds
        {
            MinFocusConfidence = Permille(options, "--min-confidence-permille", 700),
            RetreatProbability = Permille(options, "--retreat-permille", 700)
        };
        var schedule = options.GetValueOrDefault("--schedule") switch
        {
            null or "on-change" => AutonomousPollSchedule.OnChange(Number(options, "--heartbeat", 600)),
            "every-cycle" => AutonomousPollSchedule.EveryCycle,
            _ => throw new InvalidDataException("--schedule must be on-change or every-cycle.")
        };
        if (Environment.GetEnvironmentVariable(keyVariable) is not { Length: > 0 })
            throw new InvalidDataException("The environment variable " + keyVariable + " holds no key.");

        var transport = new HttpJevTransport(() => Environment.GetEnvironmentVariable(keyVariable),
            timeout: TimeSpan.FromSeconds(Number(options, "--timeout-seconds", 12)));
        using var provider = new JevPolicyProvider(transport, thresholds);
        var answers = new List<JevAnswerLine>();
        // The raw answers go into this report, which is a diagnostic file, never into the replay.
        provider.Observe = record => answers.Add(new JevAnswerLine { Tick = record.Tick, Answered = record.Answered,
            Focus = record.Focus, ConfidencePermille = (long)(record.FocusConfidence * 1000),
            RetreatPermille = record.RetreatProbability.HasValue ? (long)(record.RetreatProbability.Value * 1000) : null,
            OrderCount = record.OrderCount });
        var simulation = new Rts.Simulation.Simulation(scenario);
        var gateway = new CommandGateway(simulation, provider, null, schedule);
        gateway.EnableAutonomous(new UserPolicyIntent(0, new ScopeKey(faction, ScopeKind.All, 0), PolicyKind.Focus,
            default, 50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0,
            new Expiration(long.MaxValue, 0, ExpireFlags.None)));

        var report = new JevMatchReport { Build = build, ScenarioId = scenario.ScenarioId, FactionId = faction,
            Schedule = schedule.ToString(), MinConfidencePermille = Number(options, "--min-confidence-permille", 700),
            RetreatPermille = Number(options, "--retreat-permille", 700) };
        // One allocation cycle is 20 ticks, which is one second at the scenario's 20 Hz.
        long cycleSleepMs = Number(options, "--cycle-sleep-ms", 1000);
        report.CycleSleepMs = cycleSleepMs;
        long tick = 0;
        while (tick < ticks && !simulation.Capture(faction).Result.HasEnded)
        {
            gateway.Step();
            tick++;
            // The match must advance at the speed a real one does. The model answers in seconds of wall clock, and the
            // deadline is counted in ticks, so running the match faster than 20 Hz throws away answers that would have
            // arrived in time: at 40x, a 2 second answer lands 1600 ticks late against a 240 tick deadline.
            if (tick % 20 == 0) Thread.Sleep((int)cycleSleepMs);
            if (provider.Availability == JevAvailability.Paused && report.FirstPausedTick == null) report.FirstPausedTick = tick;
        }
        var result = simulation.Capture(faction).Result;
        report.Ticks = tick;
        report.HasEnded = result.HasEnded;
        report.WinnerFactionId = result.WinnerFactionId;
        report.FailedCalls = provider.FailureCount;
        report.DeclinedCalls = provider.DeclinedCount;
        report.Answers = answers;
        report.InputTokens = transport.InputTokens;
        // Prices are per million input tokens; kept as permille of a cent so no floating point enters the report.
        report.CostMicroDollars = report.InputTokens * 42 / 1000;
        foreach (var input in gateway.Inputs)
        {
            if (input.Kind == InputKind.Proposal && input.Orders.Count == 0) report.RejectedProposals++;
            else if (input.Kind == InputKind.Proposal)
            {
                report.AcceptedProposals++;
                foreach (var order in input.Orders)
                    report.Orders.Add(new JevOrderLine { Tick = input.AcceptedTick, Kind = order.Kind.ToString(),
                        Goal = order.Goal.Kind + ":" + order.Goal.Id });
            }
        }
        string output = options.GetValueOrDefault("--out");
        string json = JsonSerializer.Serialize(report, Json);
        if (output == null) Console.WriteLine(json); else File.WriteAllText(output, json);
        Console.Error.WriteLine("calls=" + (report.AcceptedProposals + report.RejectedProposals)
            + " orders=" + report.AcceptedProposals + " declined=" + report.DeclinedCalls + " failed=" + report.FailedCalls
            + " inputTokens=" + report.InputTokens + " cost=$" + (report.CostMicroDollars / 1000000m).ToString("0.000000", CultureInfo.InvariantCulture));
        return 0;
    }

    private static long Number(Dictionary<string, string> options, string key, long fallback) =>
        options.TryGetValue(key, out var value) ? long.Parse(value, CultureInfo.InvariantCulture) : fallback;

    // The thresholds are doubles because the model answers in probabilities; the command line stays in integers.
    private static double Permille(Dictionary<string, string> options, string key, long fallback) =>
        Number(options, key, fallback) / 1000.0;
}

internal sealed class JevAnswerLine
{
    public long Tick { get; set; }
    public bool Answered { get; set; }
    public string Focus { get; set; }
    public long ConfidencePermille { get; set; }
    public long? RetreatPermille { get; set; }
    public int OrderCount { get; set; }
}

internal sealed class JevOrderLine
{
    public long Tick { get; set; }
    public string Kind { get; set; } = "";
    public string Goal { get; set; } = "";
}

internal sealed class JevMatchReport
{
    public string Note { get; } = "Calls that produced no order are logged as rejected proposals; that is the model declining, not an error.";
    public BuildIdentity Build { get; set; } = new();
    public string ScenarioId { get; set; } = "";
    public uint FactionId { get; set; }
    public string Schedule { get; set; } = "";
    /// <summary>Wall-clock milliseconds per 20 ticks. 1000 is real time; less throws away answers on the deadline.</summary>
    public long CycleSleepMs { get; set; }
    public long MinConfidencePermille { get; set; }
    public long RetreatPermille { get; set; }
    public long Ticks { get; set; }
    public bool HasEnded { get; set; }
    public uint WinnerFactionId { get; set; }
    public int AcceptedProposals { get; set; }
    public int RejectedProposals { get; set; }
    public int FailedCalls { get; set; }
    /// <summary>Answered, but the answer cleared no threshold. Not an error.</summary>
    public int DeclinedCalls { get; set; }
    public long? FirstPausedTick { get; set; }
    public long InputTokens { get; set; }
    public long CostMicroDollars { get; set; }
    public List<JevOrderLine> Orders { get; set; } = new();
    public List<JevAnswerLine> Answers { get; set; } = new();
}
