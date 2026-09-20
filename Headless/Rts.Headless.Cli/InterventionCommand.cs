using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Application;
using Rts.Replay;

namespace Rts.Headless.Cli;

/// <summary>
/// Issue 4-5 comparison: records one match for a (player style, east preset, reply delay) combination and writes a
/// small summary. The replay can then be fed to `analyze --in` for the balance indicators.
/// </summary>
internal static class InterventionCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class Summary
    {
        public BuildIdentity Build = new();
        public string Scenario = "";
        public string Style = "";
        public string EastPreset = "";
        public int DelayTicks;
        public string AiProfile = "";
        public long LastTick;
        public long FirstContactTick;
        public List<InterventionCommandOutcome> Commands = new();
    }

    internal static int Run(Dictionary<string, string> options, BuildIdentity build)
    {
        string scenarioPath = options.TryGetValue("--scenario", out var path) ? path : throw new InvalidDataException("Missing --scenario.");
        string output = options.TryGetValue("--out", out var o) ? o : throw new InvalidDataException("Missing --out.");
        if (string.Equals(Path.GetFullPath(scenarioPath), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Input and output must differ.");
        var scenario = JsonInput.Scenario(scenarioPath);
        string style = options.GetValueOrDefault("--style") ?? "auto";
        var parsed = style switch
        {
            "auto" => InterventionStyle.Auto,
            "start" => InterventionStyle.StartOnly,
            "change" => InterventionStyle.Change,
            "push" => InterventionStyle.Push,
            "secure" => InterventionStyle.Secure,
            _ => throw new InvalidDataException("--style must be auto, start, change, push or secure.")
        };
        string east = options.GetValueOrDefault("--east-preset") ?? "none";
        int delay = options.TryGetValue("--delay", out var d) ? int.Parse(d, CultureInfo.InvariantCulture) : 0;
        var profile = AiTimingProfile.Parse(options.GetValueOrDefault("--ai-profile") ?? "default");
        long trigger = options.TryGetValue("--trigger-tick", out var tt) ? long.Parse(tt, CultureInfo.InvariantCulture) : -1;
        long ticks = long.Parse(options.TryGetValue("--ticks", out var t) ? t : throw new InvalidDataException("Missing --ticks."), CultureInfo.InvariantCulture);

        var result = InterventionRunner.Run(scenario, ticks, east, parsed, delay, profile, trigger);
        using (var file = File.Create(output))
        {
            var outcome = ReplayRunner.Record(file, scenario, result.Inputs, ticks, build, null, "none", east, delay, profile.Name);
            if (outcome.IsFault) { Console.Error.WriteLine("Fault at tick " + outcome.LastTick); return 4; }
        }

        var summary = new Summary
        {
            Build = build,
            Scenario = scenario.ScenarioId,
            Style = style,
            EastPreset = east,
            DelayTicks = delay,
            AiProfile = profile.Name,
            LastTick = result.LastTick,
            FirstContactTick = result.FirstContactTick,
            Commands = result.Commands
        };
        string summaryPath = options.GetValueOrDefault("--summary-out") ?? output + ".summary.json";
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, Json));
        Console.WriteLine("Recorded S0..S" + result.LastTick + " style=" + style + " east=" + east + " delay=" + delay);
        return 0;
    }
}
