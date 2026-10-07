using System.Globalization;
using System.Text;
using System.Text.Json;
using Rts.Application;
using Rts.Contracts;
using Rts.Tactics;
using Rts.TacticsJs;
using Rts.Providers;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Headless.Cli;

internal static class SayCommand
{
    private sealed class SayScript { public List<SayEntry> Commands { get; set; } = new(); }
    private sealed class SayEntry
    {
        public long Tick { get; set; }
        public uint Faction { get; set; } = 1;
        public string Instruction { get; set; } = "";
        public string Response { get; set; } = "";
        public string Answer { get; set; } = "";
        public string Model { get; set; } = "gpt-6-luna";
        public ulong CancelOperation { get; set; }
        public Dictionary<string, double> Judgement { get; set; } = new();
    }
    private sealed class SayResult
    {
        public long Tick { get; set; } public uint Faction { get; set; } public string Say { get; set; } = "";
        public string Reason { get; set; } = ""; public bool Late { get; set; }
        public List<string> Issued { get; set; } = new(); public List<string> Rejected { get; set; } = new();
        public List<string> Operations { get; set; } = new();
    }

    internal static int Run(Dictionary<string, string> options)
    {
        bool hasScenario = options.TryGetValue("--scenario", out var scenarioPath);
        bool hasSeed = options.TryGetValue("--map-seed", out var seedText);
        if (hasScenario == hasSeed) throw new InvalidDataException("--scenario または --map-seed の一方を指定してください。");
        string scriptPath = Required(options, "--say");
        long ticks = long.Parse(Required(options, "--ticks"), CultureInfo.InvariantCulture);
        uint defaultFaction = options.TryGetValue("--faction", out var faction) ? uint.Parse(faction, CultureInfo.InvariantCulture) : 1;
        var scenario = hasScenario
            ? JsonInput.Scenario(scenarioPath)
            : options.ContainsKey("--terrain")
                ? MapGenerator.GenerateTerrain(ulong.Parse(seedText, CultureInfo.InvariantCulture))
                : MapGenerator.Generate(ulong.Parse(seedText, CultureInfo.InvariantCulture), true);
        if (options.ContainsKey("--ages")) scenario.Economy.Ages = true;
        var entries = Read(scriptPath);
        var byTick = entries.GroupBy(e => e.Tick).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Faction).ToList());
        var sim = new Battle(scenario);
        var provider = new DelayedPolicyProvider(0, request => new[] { new PolicyOrder(0, 0, CommandSource.Human, request.Scope,
            request.Kind, request.Goal, 100, new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0, 0,
            request.Versions, request.StartedTick, new Expiration(request.DeadlineTick, 240, ExpireFlags.ObservationTooOld)) });
        var gateway = new CommandGateway(sim, provider);
        var operationTables = new Dictionary<uint, OperationTable> { [1] = new OperationTable(1), [2] = new OperationTable(2) };
        var judgements = new Dictionary<uint, JevAnswers>();
        var reports = new List<SayResult>();
        using var westTactic = SayTacticController.Create(options.GetValueOrDefault("--west-tactic"), scenario, sim, gateway, options.GetValueOrDefault("--runtimes"));
        for (long tick = 0; tick < ticks && !sim.Capture(1).Result.HasEnded; tick++)
        {
            westTactic?.Tick();
            if (byTick.TryGetValue(tick, out var atTick))
                foreach (var entry in atTick)
                    InterpretAndSubmit(entry, defaultFaction, sim, gateway, reports, operationTables, judgements, westTactic);
            for (uint factionId = 1; factionId <= 2; factionId++)
            {
                var frame = sim.Capture(factionId);
                var evaluation = operationTables[factionId].Evaluate(frame, judgements.TryGetValue(factionId, out var answer) ? answer : null);
                DispatchOperations(factionId, frame, gateway, evaluation, reports);
            }
            gateway.Step();
        }
        string output = JsonSerializer.Serialize(new { LastTick = sim.Capture(1).Tick, Reports = reports, Inputs = gateway.Inputs }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true });
        if (options.TryGetValue("--out", out var outputPath)) File.WriteAllText(outputPath, output, new UTF8Encoding(false)); else Console.WriteLine(output);
        return sim.Capture(1).Result.IsFault ? 4 : 0;
    }

    private static void InterpretAndSubmit(SayEntry entry, uint defaultFaction, Battle sim, CommandGateway gateway, List<SayResult> reports,
        Dictionary<uint, OperationTable> operationTables, Dictionary<uint, JevAnswers> judgements, SayTacticController westTactic)
    {
        uint faction = entry.Faction == 0 ? defaultFaction : entry.Faction;
        var frame = sim.Capture(faction); var summary = westTactic != null && faction == 1 ? westTactic.Summary(frame) : AiSituationSummary.From(frame);
        var table = operationTables[faction];
        if (entry.CancelOperation != 0)
        {
            var cancelReport = new SayResult { Tick = frame.Tick, Faction = faction, Say = "", Reason = table.Cancel(entry.CancelOperation) ? "人が作戦を取り消した" : "作戦IDが見つからない" };
            reports.Add(cancelReport);
        }
        if (entry.Judgement != null && entry.Judgement.Count != 0)
        {
            var answer = new JevAnswers();
            foreach (var pair in entry.Judgement) answer.Noul[pair.Key] = pair.Value;
            judgements[faction] = answer;
        }
        string response = string.IsNullOrWhiteSpace(entry.Response) ? entry.Answer : entry.Response;
        if (westTactic != null && faction == 1 &&
            AiTacticSignalMatcher.TryMatch(entry.Instruction ?? "", summary, null, out var direct))
        {
            var directReport = new SayResult { Tick = frame.Tick, Faction = faction };
            var changed = westTactic.ApplyDirect(direct);
            if (changed.Success)
            {
                directReport.Say = "合図「" + direct.Label + "」を送りました（AI を使わずに判定）";
                directReport.Issued.Add(directReport.Say);
            }
            else directReport.Rejected.Add(changed.Reason);
            reports.Add(directReport);
            return;
        }
        var result = AiResponseInterpreter.Interpret(response, new AiInterpretationContext { Frame = frame, Summary = summary, StartedTick = frame.Tick, DeadlineTick = frame.Tick + AiModelCatalog.Get(entry.Model).DeadlineTicks, MaxObservationAgeTicks = AiModelCatalog.Get(entry.Model).DeadlineTicks });
        var report = new SayResult { Tick = frame.Tick, Faction = faction, Say = result.Say, Reason = result.Reason };
        foreach (var tactic in result.TacticCommands)
        {
            var changed = westTactic != null && faction == 1 ? westTactic.Apply(tactic) : TacticChange.Fail("自軍戦術のCLI制御がありません。");
            if (!changed.Success) report.Rejected.Add(changed.Reason);
            else { report.Issued.Add(changed.Report); report.Say = string.IsNullOrEmpty(report.Say) ? changed.Report : report.Say + " " + changed.Report; }
        }
        foreach (var policy in result.Policies) { gateway.SubmitInterpreted(policy, AiModelCatalog.Get(entry.Model).DeadlineTicks); report.Issued.Add(policy.Kind + ":" + policy.Target.Kind + ":" + policy.Goal.Kind); }
        foreach (var command in result.EconomyCommands) { gateway.SubmitEconomy(command); report.Issued.Add(command.Kind.ToString()); }
        foreach (var operation in result.Operations)
        {
            var added = table.Add(operation, frame);
            report.Operations.Add(added.Accepted ? "作戦" + added.Id + "を登録: " + operation.When.Describe() : "作戦を却下: " + added.Reason);
        }
        foreach (var rejected in result.Rejected) report.Rejected.Add(rejected.Reason);
        reports.Add(report);
    }

    private static void DispatchOperations(uint faction, FactionFrame frame, CommandGateway gateway,
        OperationEvaluation evaluation, List<SayResult> reports)
    {
        foreach (var fired in evaluation.Fired)
        {
            var report = new SayResult { Tick = frame.Tick, Faction = faction, Say = "作戦" + fired.Id + "が発火", Reason =
                fired.Source == OperationSource.Human ? "人の作戦なので Submit で出した" : "参謀の作戦なので AI の Proposal で出した" };
            foreach (var action in fired.Actions)
            {
                if (action.IsPolicy)
                {
                    if (fired.Source == OperationSource.Human)
                    {
                        gateway.Submit(action.Policy);
                    }
                    else
                    {
                        var policy = action.Policy;
                        var order = new PolicyOrder(0, 0, CommandSource.Ai, policy.Target, policy.Kind, policy.Goal, policy.Priority,
                            policy.AllowedLoss, policy.End, policy.ReservePermille, 0, Array.Empty<PolicyVersion>(), frame.Tick, policy.Expiration);
                        gateway.Propose(faction, fired.Id, TacticOrderVersions.Stamp(new[] { order }, scope => gateway.FactionVersions(faction).Versions(scope)), checked(frame.Tick + 1));
                    }
                    report.Issued.Add(action.Policy.Kind + ":" + action.Policy.Target.Kind + ":" + action.Policy.Goal.Kind);
                }
                else
                {
                    gateway.SubmitEconomy(action.Economy);
                    report.Issued.Add(action.Economy.Kind.ToString());
                }
            }
            reports.Add(report);
        }
    }

    private static List<SayEntry> Read(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)) return ReadCsv(text);
        var script = JsonSerializer.Deserialize<SayScript>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return script?.Commands ?? throw new InvalidDataException("say JSON must contain commands.");
    }
    private static List<SayEntry> ReadCsv(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries); if (lines.Length == 0) throw new InvalidDataException("empty say CSV");
        var header = SplitCsv(lines[0]).Select(x => x.Trim()).ToArray(); var result = new List<SayEntry>();
        for (int i = 1; i < lines.Length; i++) { var cells = SplitCsv(lines[i]); string Get(string name) { int n = Array.IndexOf(header, name); return n >= 0 && n < cells.Count ? cells[n] : ""; }
            result.Add(new SayEntry { Tick = long.Parse(Get("tick"), CultureInfo.InvariantCulture), Faction = uint.TryParse(Get("faction"), out var f) ? f : 1, Instruction = Get("instruction"), Response = Get("response"), Model = string.IsNullOrEmpty(Get("model")) ? "gpt-6-luna" : Get("model"), CancelOperation = ulong.TryParse(Get("cancelOperation"), out var operation) ? operation : 0 }); }
        return result;
    }
    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>(); var b = new StringBuilder(); bool quoted = false;
        foreach (char c in line) { if (c == '"') quoted = !quoted; else if (c == ',' && !quoted) { cells.Add(b.ToString()); b.Clear(); } else b.Append(c); }
        cells.Add(b.ToString()); return cells;
    }
    private static string Required(Dictionary<string, string> options, string key) => options.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Missing " + key);

    private sealed class TacticChange
    {
        internal bool Success; internal string Reason = ""; internal string Report = "";
        internal static TacticChange Ok(string report) => new TacticChange { Success = true, Report = report };
        internal static TacticChange Fail(string reason) => new TacticChange { Reason = reason };
    }

    private sealed class SayTacticController : IDisposable
    {
        private sealed class Frames : IFrameSource
        {
            private readonly Battle simulation; internal Frames(Battle simulation) { this.simulation = simulation; }
            public FactionFrame Latest(uint factionId) => simulation.Capture(factionId);
        }
        private readonly ScenarioDefinition scenario; private readonly Battle simulation; private readonly CommandGateway gateway; private readonly string runtimes;
        private readonly Frames frames; private TacticHost host; private string selection; private string displayName;
        private SayTacticController(string selection, ScenarioDefinition scenario, Battle simulation, CommandGateway gateway, string runtimes)
        { this.selection = selection; this.scenario = scenario; this.simulation = simulation; this.gateway = gateway; this.runtimes = runtimes; frames = new Frames(simulation); Load(selection); }

        internal static SayTacticController Create(string selection, ScenarioDefinition scenario, Battle simulation, CommandGateway gateway, string runtimes)
        {
            if (string.IsNullOrEmpty(selection) || selection == "auto") return null;
            return new SayTacticController(selection, scenario, simulation, gateway, runtimes ?? PyodideTacticRuntime.FindDefaultRuntimes());
        }

        private void Load(string name)
        {
            ITacticRuntime runtime;
            if (name == "idle") runtime = new IdleTactic();
            else if (name == "rush") runtime = new RushTactic();
            else
            {
                var loaded = TacticFolder.Load(name, runtimes);
                if (!loaded.IsSuccess) throw new InvalidDataException("戦術フォルダを読み込めません: " + loaded.Error);
                runtime = loaded.Runtime;
            }
            host = new TacticHost(1, frames, gateway, gateway, runtime, versions: scope => gateway.FactionVersions(1).Versions(scope));
            host.Start("{\"matchSeed\":" + scenario.Seed.ToString(CultureInfo.InvariantCulture) + ",\"factionId\":1}");
            selection = name; displayName = host.Name;
        }

        internal void Tick() { host?.Tick(); }
        internal AiSituationSummary Summary(FactionFrame frame)
        {
            var summary = AiSituationSummary.From(frame);
            var parameters = host.Parameters.Select(d => new AiTacticParameterInfo
            {
                Name = d.Name, Label = d.Label, Type = d.Type, Value = Value(host.ParamValues[d.Name]), Min = d.Min, Max = d.Max, Step = d.Step, Choices = d.Choices
            }).ToArray();
            var signals = host.Signals.Select(s => new AiTacticSignalInfo(s.Name, s.Label, s.NeedsPoint)).ToArray();
            summary.SetTacticInfo(displayName, AvailableNames(), parameters, signals);
            return summary;
        }

        internal TacticChange Apply(AiTacticCommand command)
        {
            if (command.Kind == "SetTacticParam")
            {
                var definition = host.Parameters.FirstOrDefault(x => x.Name == command.ParamName);
                if (definition == null) return TacticChange.Fail("つまみが見つかりません: " + command.ParamName);
                object parsed; string reason;
                if (!Parse(definition, command.ParamValue, out parsed, out reason)) return TacticChange.Fail(reason);
                object before = host.ParamValues[command.ParamName];
                if (!host.TrySetParam(command.ParamName, parsed, out reason)) return TacticChange.Fail(reason);
                return TacticChange.Ok(definition.Label + "を " + Value(before) + " → " + Value(host.ParamValues[command.ParamName]) + " にしました");
            }
            if (command.Kind == "SendTacticSignal")
            {
                string reason;
                if (!host.SendSignal(command.SignalName, command.Point, out reason)) return TacticChange.Fail(reason);
                return TacticChange.Ok("戦術の合図「" + command.SignalName + "」を送りました");
            }
            string name = string.IsNullOrEmpty(command.TacticName) ? "auto" : ResolveName(command.TacticName);
            if (name == null) return TacticChange.Fail("戦術名が見つかりません: " + command.TacticName);
            if (name != "idle" && name != "rush" && name != "auto" && !Directory.Exists(name)) return TacticChange.Fail("戦術名が見つかりません: " + name);
            host.Dispose();
            if (name == "auto") { host = null; displayName = ""; selection = "auto"; return TacticChange.Ok("戦術をやめ、お任せに戻しました"); }
            Load(name); return TacticChange.Ok("戦術を " + displayName + " に切り替えました");
        }

        internal TacticChange ApplyDirect(AiDirectTacticSignal signal)
        {
            string reason;
            if (!host.SendSignal(signal.Name, signal.Point, out reason)) return TacticChange.Fail(reason);
            return TacticChange.Ok("合図「" + signal.Label + "」を送りました（AI を使わずに判定）");
        }

        private string[] AvailableNames()
        {
            var names = new List<string> { "idle", "rush" };
            if (Directory.Exists(selection))
            {
                string parent = Directory.GetParent(Path.GetFullPath(selection))?.FullName;
                foreach (var entry in TacticCatalog.Scan(parent ?? "", runtimes).Where(x => x.IsSelectable)) names.Add(entry.DisplayName);
            }
            if (!string.IsNullOrEmpty(displayName)) names.Add(displayName);
            return names.Distinct(StringComparer.Ordinal).ToArray();
        }

        private string ResolveName(string requested)
        {
            if (requested == "idle" || requested == "rush" || requested == "auto") return requested;
            if (Directory.Exists(requested)) return requested;
            if (!Directory.Exists(selection)) return null;
            string parent = Directory.GetParent(Path.GetFullPath(selection))?.FullName;
            var entry = TacticCatalog.Scan(parent ?? "", runtimes).FirstOrDefault(x => x.IsSelectable && (x.DisplayName == requested || x.FolderName == requested));
            return entry?.Path;
        }

        private static bool Parse(TacticParamDefinition definition, string text, out object value, out string reason)
        {
            value = null; reason = null;
            if (definition.Type == "bool") { if (text == "true") { value = true; return true; } if (text == "false") { value = false; return true; } reason = "boolはtrueまたはfalseです。"; return false; }
            if (definition.Type == "choice") { value = text; return true; }
            if (definition.Type == "int" && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { value = i; return true; }
            if (definition.Type == "number" && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { value = d; return true; }
            reason = definition.Type + "は数値でなければなりません。"; return false;
        }
        private static string Value(object value) => value is bool b ? (b ? "true" : "false") : value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
        public void Dispose() { host?.Dispose(); }
    }
}
