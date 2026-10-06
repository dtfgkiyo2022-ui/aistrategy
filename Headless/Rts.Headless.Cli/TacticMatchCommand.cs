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
        var scenario = hasScenario
            ? JsonInput.Scenario(scenarioPath)
            : options.ContainsKey("--terrain")
                ? MapGenerator.GenerateTerrain(ulong.Parse(seedText, CultureInfo.InvariantCulture))
                : MapGenerator.Generate(ulong.Parse(seedText, CultureInfo.InvariantCulture), true);
        if (options.ContainsKey("--ages")) scenario.Economy.Ages = true;
        long ticks = long.Parse(Required(options, "--ticks"), CultureInfo.InvariantCulture);
        if (ticks < 0 || ticks > scenario.VerificationTickLimit) throw new InvalidDataException("ticksがシナリオの範囲外です。");
        string westName = options.GetValueOrDefault("--west-tactic") ?? "auto";
        string eastName = options.GetValueOrDefault("--east-tactic") ?? "auto";
        ValidateTactic(westName); ValidateTactic(eastName);

        var simulation = new Battle(scenario);
        var gateway = new CommandGateway(simulation);
        var source = new FrameSource(simulation);
        string runtimes = options.GetValueOrDefault("--runtimes") ?? PyodideTacticRuntime.FindDefaultRuntimes();
        using var west = CreateHost(westName, 1, source, gateway, runtimes, options.GetValueOrDefault("--west-param"));
        using var east = CreateHost(eastName, 2, source, gateway, runtimes, options.GetValueOrDefault("--east-param"));
        west?.Start(SetupJson(scenario, 1));
        east?.Start(SetupJson(scenario, 2));
        var westAuto = CreateAuto(westName, 1, gateway); var eastAuto = CreateAuto(eastName, 2, gateway);
        westAuto?.Initialize(); eastAuto?.Initialize();
        var lines = new List<string>();
        var westSignals = ParseSignals(options.GetValueOrDefault("--west-signal"));
        MatchPackWriter pack = null;
        if (options.TryGetValue("--pack-out", out var packPath))
        {
            pack = new MatchPackWriter(packPath, scenario, westName, eastName);
            pack.RecordInitial(simulation);
        }
        for (long i = 0; i < ticks && !simulation.Capture(1).Result.HasEnded; i++)
        {
            long tick = simulation.Capture(1).Tick;
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
