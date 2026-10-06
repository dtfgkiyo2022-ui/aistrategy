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
        var scenario = hasScenario ? JsonInput.Scenario(scenarioPath) : MapGenerator.Generate(ulong.Parse(seedText, CultureInfo.InvariantCulture), true);
        long ticks = long.Parse(Required(options, "--ticks"), CultureInfo.InvariantCulture);
        if (ticks < 0 || ticks > scenario.VerificationTickLimit) throw new InvalidDataException("ticksがシナリオの範囲外です。");
        string westName = options.GetValueOrDefault("--west-tactic") ?? "auto";
        string eastName = options.GetValueOrDefault("--east-tactic") ?? "auto";
        ValidateTactic(westName); ValidateTactic(eastName);

        var simulation = new Battle(scenario);
        var gateway = new CommandGateway(simulation);
        var source = new FrameSource(simulation);
        string runtimes = options.GetValueOrDefault("--runtimes") ?? PyodideTacticRuntime.FindDefaultRuntimes();
        using var west = CreateHost(westName, 1, source, gateway, runtimes);
        using var east = CreateHost(eastName, 2, source, gateway, runtimes);
        west?.Start(SetupJson(scenario, 1));
        east?.Start(SetupJson(scenario, 2));
        var westAuto = CreateAuto(westName, 1, gateway); var eastAuto = CreateAuto(eastName, 2, gateway);
        westAuto?.Initialize(); eastAuto?.Initialize();
        var lines = new List<string>();
        MatchPackWriter pack = null;
        if (options.TryGetValue("--pack-out", out var packPath))
        {
            pack = new MatchPackWriter(packPath, scenario, westName, eastName);
            pack.RecordInitial(simulation);
        }
        for (long i = 0; i < ticks && !simulation.Capture(1).Result.HasEnded; i++)
        {
            var westResult = west?.Tick();
            var eastResult = east?.Tick();
            WriteHostLog(lines, westResult, 1, westName);
            WriteHostLog(lines, eastResult, 2, eastName);
            pack?.RecordTactic(westResult, 1, westName);
            pack?.RecordTactic(eastResult, 2, eastName);
            gateway.Step();
            if (westAuto != null && !simulation.Capture(1).Result.HasEnded) westAuto.Step(simulation.Capture(1));
            if (eastAuto != null && !simulation.Capture(2).Result.HasEnded) eastAuto.Step(simulation.Capture(2));
            pack?.RecordAfterStep(simulation);
        }
        west?.Dispose();
        east?.Dispose();
        pack?.Complete(simulation, gateway.Inputs, ticks);
        if (options.TryGetValue("--log-out", out var logPath)) File.WriteAllLines(logPath, lines, new UTF8Encoding(false));
        if (options.TryGetValue("--out", out var outputPath))
        {
            using var output = File.Create(outputPath);
            ReplayRunner.Record(output, scenario, gateway.Inputs, ticks, new BuildIdentity(), null, "none", "none");
        }
        var result = simulation.Capture(1).Result;
        Console.WriteLine("tactic-match: ticks=" + simulation.Capture(1).Tick.ToString(CultureInfo.InvariantCulture) + " ended=" + result.HasEnded + " winner=" + result.WinnerFactionId.ToString(CultureInfo.InvariantCulture));
        return result.IsFault ? 4 : 0;
    }

    private static TacticHost CreateHost(string name, uint faction, IFrameSource source, CommandGateway gateway, string runtimes)
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
        return new TacticHost(faction, source, gateway, gateway, runtime);
    }

    private static string SetupJson(ScenarioDefinition scenario, uint faction)
    {
        return "{\"matchSeed\":" + scenario.Seed.ToString(CultureInfo.InvariantCulture) + ",\"factionId\":" + faction.ToString(CultureInfo.InvariantCulture) + "}";
    }

    // "auto" deliberately adds no logged input: it is the simulation's existing hands-off AI.
    private static PresetController CreateAuto(string name, uint faction, CommandGateway gateway) => null;

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
            disabled = result.Disabled
        };
        lines.Add(JsonSerializer.Serialize(record));
    }

    private static void ValidateTactic(string name)
    {
        if (name == "idle" || name == "rush" || name == "auto") return;
        if (!Directory.Exists(name)) throw new InvalidDataException("戦術はidle、rush、auto、または存在するフォルダパスです。");
    }
    private static string Required(Dictionary<string, string> options, string key) => options.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Missing " + key);
}
