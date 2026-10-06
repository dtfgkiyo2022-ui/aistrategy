using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Rts.Tactics;
using Rts.TacticsJs;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Headless.Cli;

/// <summary>One headless Gymnasium-like match. The player's commands do not pass through TacticHost.</summary>
public sealed class TacticGymSession
{
    public const double CoreDamageRewardCoefficient = 0.001;
    private readonly uint faction;
    private readonly string opponent;
    private readonly long maxTicks;
    private readonly Battle simulation;
    private readonly CommandGateway gateway;
    private readonly TacticHost opponentHost;
    private long previousEnemyCoreHp = -1;
    private long previousOwnCoreHp;
    private ulong sequence = 1;

    public TacticGymSession(ScenarioDefinition scenario, uint faction, string opponent, long maxTicks)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        if (faction < 1 || faction > 2) throw new ArgumentOutOfRangeException(nameof(faction));
        if (maxTicks < 1 || maxTicks > scenario.VerificationTickLimit) throw new ArgumentOutOfRangeException(nameof(maxTicks));
        if (opponent == null) throw new ArgumentNullException(nameof(opponent));
        this.faction = faction;
        this.opponent = opponent;
        this.maxTicks = maxTicks;
        simulation = new Battle(scenario);
        gateway = new CommandGateway(simulation);
        opponentHost = CreateOpponent(opponent, faction == 1 ? 2U : 1U, gateway, simulation);
        opponentHost?.Start(SetupJson(scenario, faction == 1 ? 2U : 1U));
        var frame = simulation.Capture(faction);
        previousOwnCoreHp = CoreHp(frame, faction, out _);
    }

    public string ViewJson => TacticViewWriter.Write(simulation.Capture(faction));
    public long Tick => simulation.Capture(faction).Tick;

    public TacticGymStepResult Step(string commandJson, int ticks)
    {
        if (ticks < 1 || ticks > 600) throw new ArgumentOutOfRangeException(nameof(ticks), "ticksは1から600です。");
        var frame = simulation.Capture(faction);
        var commands = TacticCommandReader.Read(commandJson, frame);
        if (commands.IsMalformed) throw new FormatException(commands.Error ?? "命令JSONを読めません。");
        if (commands.GlobalPolicy != null)
        {
            // The current tactic-match path also validates this shortcut but has no global-policy port.
            // Keep the same behavior here; ordinary policy and economy commands are still submitted below.
        }
        if (commands.Policies.Count != 0)
            gateway.Propose(faction, sequence++, commands.Policies, checked(frame.Tick + 1));
        foreach (var economy in commands.EconomyCommands) gateway.SubmitEconomy(economy);

        for (int i = 0; i < ticks && Tick < maxTicks && !simulation.Capture(faction).Result.HasEnded; i++)
        {
            opponentHost?.Tick();
            gateway.Step();
        }

        var after = simulation.Capture(faction);
        bool ended = after.Result.HasEnded;
        bool timedOut = !ended && after.Tick >= maxTicks;
        bool done = ended || timedOut;
        uint winner = ended ? after.Result.WinnerFactionId : 0;
        long ownCoreHp = CoreHp(after, faction, out _);
        long enemyCoreHp = CoreHp(after, faction == 1 ? 2U : 1U, out bool enemyKnown);
        double damageReward = 0;
        if (!done)
        {
            long ownDamage = Math.Max(0, previousOwnCoreHp - ownCoreHp);
            long enemyDamage = enemyKnown && previousEnemyCoreHp >= 0 ? Math.Max(0, previousEnemyCoreHp - enemyCoreHp) : 0;
            damageReward = (enemyDamage - ownDamage) * CoreDamageRewardCoefficient;
        }
        double outcomeReward = ended ? (winner == faction ? 1 : winner == 0 ? 0 : -1) : 0;
        double reward = done ? (ended ? outcomeReward : 0) : damageReward;
        previousOwnCoreHp = ownCoreHp;
        if (enemyKnown) previousEnemyCoreHp = enemyCoreHp;

        var info = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["rejected"] = commands.Rejected.Select(x => new TacticGymRejected(x.Index, x.Type, x.Reason)).ToArray(),
            ["ownSoldiers"] = after.AliveCount,
            ["enemySoldiersKnown"] = after.Observation?.VisibleEnemies?.Count ?? 0,
            ["ownCoreHp"] = ownCoreHp,
            ["enemyCoreHpKnown"] = enemyKnown ? enemyCoreHp : -1,
            ["outcomeReward"] = outcomeReward,
            ["damageReward"] = damageReward,
            ["timeLimit"] = timedOut
        };
        return new TacticGymStepResult(TacticViewWriter.Write(after), after.Tick, done, winner, reward, info);
    }

    private static long CoreHp(FactionFrame frame, uint owner, out bool known)
    {
        var objective = frame.Objectives.FirstOrDefault(x => x.Kind == GoalKind.Core && x.Id == owner);
        known = objective.Id == owner && objective.IsHpKnown;
        return known ? objective.Hp : 0;
    }

    private static TacticHost CreateOpponent(string name, uint faction, CommandGateway gateway, Battle simulation)
    {
        if (string.Equals(name, "auto", StringComparison.OrdinalIgnoreCase)) return null;
        ITacticRuntime runtime;
        if (string.Equals(name, "idle", StringComparison.OrdinalIgnoreCase)) runtime = new IdleTactic();
        else if (string.Equals(name, "rush", StringComparison.OrdinalIgnoreCase)) runtime = new RushTactic();
        else
        {
            var loaded = TacticFolder.Load(name);
            if (!loaded.IsSuccess) throw new InvalidDataException("戦術フォルダを読み込めません: " + loaded.Error);
            runtime = loaded.Runtime;
        }
        return new TacticHost(faction, new SessionFrameSource(simulation), gateway, gateway, runtime);
    }

    private static string SetupJson(ScenarioDefinition scenario, uint faction)
        => "{\"matchSeed\":" + scenario.Seed.ToString(CultureInfo.InvariantCulture) + ",\"factionId\":" + faction.ToString(CultureInfo.InvariantCulture) + "}";

    private sealed class SessionFrameSource : IFrameSource
    {
        private readonly Battle simulation;
        internal SessionFrameSource(Battle simulation) { this.simulation = simulation; }
        public FactionFrame Latest(uint factionId) => simulation.Capture(factionId);
    }
}

public sealed class TacticGymRejected
{
    public int Index { get; }
    public string Type { get; }
    public string Reason { get; }
    public TacticGymRejected(int index, string type, string reason) { Index = index; Type = type; Reason = reason; }
}

public sealed class TacticGymStepResult
{
    public string ViewJson { get; }
    public long Tick { get; }
    public bool Done { get; }
    public uint Winner { get; }
    public double Reward { get; }
    public IReadOnlyDictionary<string, object> Info { get; }
    public TacticGymStepResult(string viewJson, long tick, bool done, uint winner, double reward, IReadOnlyDictionary<string, object> info)
    { ViewJson = viewJson; Tick = tick; Done = done; Winner = winner; Reward = reward; Info = info; }
}

internal static class TacticGymCommand
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false };

    internal static int Run(Dictionary<string, string> options)
    {
        bool profile = options.ContainsKey("--profile");
        TacticGymSession session = null;
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string response;
            bool close;
            try { response = Handle(line, ref session, profile, out close); }
            catch (Exception e) when (e is JsonException || e is InvalidDataException || e is FormatException || e is ArgumentException || e is OverflowException)
            { response = Error(e.Message); close = false; }
            catch (Exception e)
            { response = Error(e.Message); close = false; }
            Console.WriteLine(response);
            Console.Out.Flush();
            if (close) break;
        }
        return 0;
    }

    private static string Handle(string line, ref TacticGymSession session, bool profile, out bool close)
    {
        close = false;
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("要求はJSONオブジェクトです。");
        string op = RequiredString(root, "op");
        if (op == "close") { session = null; close = true; return JsonSerializer.Serialize(new { ok = true }, Json); }
        if (op == "reset")
        {
            ulong seed = RequiredUInt64(root, "mapSeed");
            uint faction = checked((uint)RequiredLong(root, "faction"));
            string opponent = RequiredString(root, "opponent");
            long maxTicks = RequiredLong(root, "maxTicks");
            var flags = ReadFlags(root);
            var scenario = TacticGymScenarios.Create(seed, flags);
            ValidateOpponent(opponent);
            session = new TacticGymSession(scenario, faction, opponent, maxTicks);
            var view = JsonDocument.Parse(session.ViewJson).RootElement.Clone();
            return JsonSerializer.Serialize(new { ok = true, view, tick = 0L }, Json);
        }
        if (op == "step")
        {
            if (session == null) throw new InvalidDataException("resetが必要です。");
            if (!root.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("commandsは配列です。");
            int ticks = checked((int)RequiredLong(root, "ticks"));
            string commandJson = "{\"version\":1,\"commands\":" + commands.GetRawText() + "}";
            var stopwatch = Stopwatch.StartNew();
            var result = session.Step(commandJson, ticks);
            stopwatch.Stop();
            if (profile) Console.Error.WriteLine("tactic-gym step ticks=" + ticks.ToString(CultureInfo.InvariantCulture) + " elapsedMs=" + (stopwatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)));
            var view = JsonDocument.Parse(result.ViewJson).RootElement.Clone();
            return JsonSerializer.Serialize(new { ok = true, view, tick = result.Tick, done = result.Done, winner = result.Winner, reward = result.Reward, info = result.Info }, Json);
        }
        throw new InvalidDataException("opはreset、step、closeのいずれかです。");
    }

    private static Dictionary<string, bool> ReadFlags(JsonElement root)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("ruleFlags", out var flags)) return result;
        if (flags.ValueKind != JsonValueKind.Object) throw new InvalidDataException("ruleFlagsはオブジェクトです。");
        foreach (var property in flags.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.True && property.Value.ValueKind != JsonValueKind.False)
                throw new InvalidDataException("ruleFlagsの値はbooleanです: " + property.Name);
            result[property.Name] = property.Value.GetBoolean();
        }
        return result;
    }

    private static void ValidateOpponent(string opponent)
    {
        if (opponent == "auto" || opponent == "idle" || opponent == "rush") return;
        if (!Directory.Exists(opponent)) throw new InvalidDataException("相手の戦術フォルダがありません: " + opponent);
    }

    private static string RequiredString(JsonElement root, string name)
    { if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new InvalidDataException(name + "は必須の文字列です。"); return value.GetString(); }
    private static long RequiredLong(JsonElement root, string name)
    { if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result)) throw new InvalidDataException(name + "は整数です。"); return result; }
    private static ulong RequiredUInt64(JsonElement root, string name)
    { long value = RequiredLong(root, name); if (value < 0) throw new InvalidDataException(name + "は0以上です。"); return (ulong)value; }
    private static string Error(string message) => JsonSerializer.Serialize(new { ok = false, error = message ?? "不正な要求です。" }, Json);
}

internal static class TacticGymScenarios
{
    internal static ScenarioDefinition Create(ulong seed, IReadOnlyDictionary<string, bool> flags)
    {
        bool terrain = Get(flags, "terrain");
        bool large = Get(flags, "large");
        bool economy = !flags.ContainsKey("economy") || Get(flags, "economy");
        bool industry = Get(flags, "industry");
        bool processing = Get(flags, "processingChain");
        bool gold = Get(flags, "gold");
        bool masonry = Get(flags, "masonry");
        bool bridge = Get(flags, "bridge");
        ScenarioDefinition scenario;
        if (large) scenario = MapGenerator.GenerateLarge(seed, gold, processing, masonry, bridge);
        else if (terrain) scenario = MapGenerator.GenerateTerrain(seed, gold, processing, masonry, bridge);
        else scenario = MapGenerator.Generate(seed, economy, industry);
        if (Get(flags, "ages")) scenario.Economy.Ages = true;
        return scenario;
    }
    private static bool Get(IReadOnlyDictionary<string, bool> flags, string key) => flags.TryGetValue(key, out var value) && value;
}
