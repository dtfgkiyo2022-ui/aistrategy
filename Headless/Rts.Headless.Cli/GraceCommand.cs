using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;

namespace Rts.Headless.Cli;

/// <summary>
/// Chapter 11 judgement-grace sweep. One order is replayed from tick 0 once per candidate acceptance tick and
/// scored with one success predicate; both the immediate case and the +60 tick comprehension case are reported.
/// </summary>
internal static class GraceCommand
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
        string output = options.GetValueOrDefault("--out");
        if (output != null && string.Equals(Path.GetFullPath(scenarioPath), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Input and output must differ.");
        var scenario = JsonInput.Scenario(scenarioPath);

        var request = new GraceRequest
        {
            Scenario = scenario,
            Criterion = Criterion(options.GetValueOrDefault("--criterion") ?? "core-defense"),
            FactionId = (uint)Number(options, "--faction", 1),
            ArmyId = (uint)Number(options, "--army", 0),
            OutpostId = (uint)Number(options, "--outpost", 0),
            FirstObservedTick = Number(options, "--observed-tick", 0),
            TickLimit = Number(options, "--ticks", scenario.VerificationTickLimit),
            MinAcceptTick = Number(options, "--min-r", 1),
            MaxAcceptTick = Number(options, "--max-r", -1),
            AcceptStep = Number(options, "--r-step", 1),
            InputDelayTicks = Number(options, "--input-delay", 60)
        };
        var order = Order(options, request.FactionId);
        request.Orders = new[] { order };

        var report = GraceMeasurement.Measure(request);
        var document = new GraceDocument
        {
            Build = build,
            ScenarioId = scenario.ScenarioId,
            ScenarioPath = Path.GetFullPath(scenarioPath),
            OrderKind = order.Kind + " " + order.Target.Kind + ":" + order.Target.Id + " goal " + order.Goal.Kind + ":" + order.Goal.Id,
            Report = report
        };
        string json = JsonSerializer.Serialize(document, Json);
        if (output == null) Console.WriteLine(json); else File.WriteAllText(output, json);
        return 0;
    }

    private static long Number(Dictionary<string, string> options, string key, long fallback) =>
        options.TryGetValue(key, out var value) ? long.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static GraceCriterion Criterion(string name) => name switch
    {
        "core-defense" => GraceCriterion.CoreDefense,
        "retreat" => GraceCriterion.Retreat,
        "reinforcement" => GraceCriterion.Reinforcement,
        "diversion" => GraceCriterion.DiversionResponse,
        "outpost-held" => GraceCriterion.OutpostHeld,
        _ => throw new InvalidDataException("--criterion must be core-defense, retreat, reinforcement, diversion or outpost-held.")
    };

    private static PolicyOrder Order(Dictionary<string, string> options, uint faction)
    {
        var kind = Enum.Parse<PolicyKind>(options.GetValueOrDefault("--order-kind") ?? "Defend", true);
        var scopeKind = Enum.Parse<ScopeKind>(options.GetValueOrDefault("--order-scope") ?? "All", true);
        var goalKind = Enum.Parse<GoalKind>(options.GetValueOrDefault("--order-goal") ?? "None", true);
        var scope = new ScopeKey(faction, scopeKind, (uint)Number(options, "--order-scope-id", 0));
        var goal = new PolicyGoal(goalKind, (uint)Number(options, "--order-goal-id", 0), default);
        // The measurement rewrites CommandId, TargetRevision and ObservedTick per run; only the payload matters here.
        return new PolicyOrder(1, 0, CommandSource.Human, scope, kind, goal, 50, new LossBudget(300),
            new EndCondition(EndKind.UntilReplaced, 0), (ushort)Number(options, "--reserve-permille", 0), 0,
            Array.Empty<PolicyVersion>(), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None));
    }
}

internal sealed class GraceDocument
{
    public string TickConvention { get; } =
        "Candidate ticks are operator-side acceptance ticks R; the delayed case applies the order during Step(R + InputDelayTicks). Grace = LastSuccessTick - FirstObservedTick.";
    public BuildIdentity Build { get; set; } = new();
    public string ScenarioId { get; set; } = "";
    public string ScenarioPath { get; set; } = "";
    public string OrderKind { get; set; } = "";
    public GraceReport Report { get; set; } = new();
}
