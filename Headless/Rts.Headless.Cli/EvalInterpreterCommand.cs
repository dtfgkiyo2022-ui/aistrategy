using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using Rts.Application;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Headless.Cli;

/// <summary>Runs the fixed Japanese interpreter set. The fake mode is deterministic and is used by headless CI; real models use the same coordinator and scorer.</summary>
internal static class EvalInterpreterCommand
{
    private sealed class EvalCase
    {
        internal int Id;
        internal string Text = "";
        internal string Selected;
        internal JsonElement Expect;
        internal string Reason = "";
        internal string[] Tags = Array.Empty<string>();
    }
    private sealed class ScoreRow
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public string Tags { get; set; } = "";
        public string AnswerJson { get; set; } = "";
        public bool ExpectedRefusal { get; set; }
        public bool Correct { get; set; }
        public bool FalseRefusal { get; set; }
        public long ElapsedMs { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CacheReadInputTokens { get; set; }
        public decimal CostYen { get; set; }
        public string Score { get; set; } = "";
        public string ResultReason { get; set; } = "";
        public int IssuedCount { get; set; }
        public string RejectedReasons { get; set; } = "";
    }

    internal static int Run(Dictionary<string, string> options)
    {
        string setPath = Required(options, "--eval-set");
        string model = options.TryGetValue("--model", out var selectedModel) ? selectedModel : "fake";
        string csvPath = options.TryGetValue("--csv", out var csv) ? csv : (options.TryGetValue("--out", out var output) ? output + ".csv" : "eval-interpreter.csv");
        var cases = Read(setPath);
        var rows = new List<ScoreRow>();
        foreach (var item in cases)
        {
            try { rows.Add(RunOne(item, model)); }
            catch (Exception e) { throw new InvalidDataException("評価問題 " + item.Id + " で失敗しました: " + e.Message, e); }
        }
        var summary = Summarize(model, rows);
        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        if (options.TryGetValue("--out", out var outPath)) File.WriteAllText(outPath, json, new UTF8Encoding(false)); else Console.WriteLine(json);
        WriteCsv(csvPath, rows);
        return rows.All(r => r.Correct) ? 0 : 2;
    }

    private static ScoreRow RunOne(EvalCase item, string model)
    {
        var frame = FixedFrame();
        var summary = AiSituationSummary.From(frame);
        AddEvaluationAliases(summary);
        var selected = ParseSelected(item.Selected);
        string providerModel = model.Equals("fake", StringComparison.OrdinalIgnoreCase) ? "local-llm" : model;
        AiModelCatalog.Get(providerModel);
        var context = new AiInterpretationContext { Frame = frame, Summary = summary, HasFixedTarget = selected.HasValue, FixedTarget = selected.GetValueOrDefault(),
            StartedTick = frame.Tick, DeadlineTick = frame.Tick + AiModelCatalog.Get(providerModel).DeadlineTicks, MaxObservationAgeTicks = AiModelCatalog.Get(providerModel).DeadlineTicks,
            PlacementFinder = new DelegateAiPlacementFinder((_, __, ___) => Tuple.Create(true, 1, "")) };
        long start = Stopwatch.GetTimestamp();
        InterpretedReply reply;
        string answerForCsv;
        if (model.Equals("fake", StringComparison.OrdinalIgnoreCase))
        {
            string answer = FakeAnswer(item);
            answerForCsv = answer;
            var interpreted = AiResponseInterpreter.Interpret(answer, context);
            var usage = new AiTokenUsage(AiCostCalculator.EstimateTokens(summary.Prompt(item.Text)), AiCostCalculator.EstimateTokens(answer));
            reply = new InterpretedReply { Result = interpreted, Usage = usage, CostYen = AiCostCalculator.Calculate(providerModel, usage) };
        }
        else
        {
            using (var interpreter = CreateInterpreter(model))
            {
                var request = new InterpreterRequest { RequestId = 1, FactionId = frame.FactionId, Instruction = item.Text, FixedTarget = selected.GetValueOrDefault(), HasFixedTarget = selected.HasValue,
                    Summary = summary, Model = providerModel, StartedTick = frame.Tick, DeadlineTick = context.DeadlineTick };
                interpreter.Request(request);
                InterpreterReply transportReply = null;
                for (int i = 0; i < 120000 && transportReply == null; i++)
                {
                    var ready = interpreter.Poll(frame.Tick + i); if (ready.Count != 0) transportReply = ready[0]; else Thread.Sleep(1);
                }
                if (transportReply == null) throw new TimeoutException("評価 API が応答しませんでした。");
                answerForCsv = transportReply.Json ?? "";
                var result = string.IsNullOrEmpty(transportReply.FailureReason) ? AiResponseInterpreter.Interpret(transportReply.Json, context) : new AiCommandInterpretationResult { Unknown = true, Reason = transportReply.FailureReason };
                reply = new InterpretedReply { Result = result, Usage = transportReply.Usage, CostYen = AiCostCalculator.Calculate(providerModel, transportReply.Usage) };
            }
        }
        long elapsed = (long)(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        bool expectedRefusal = IsUnknown(item.Expect);
        bool conditional = item.Tags.Any(t => t.Equals("G-4", StringComparison.OrdinalIgnoreCase));
        bool correct = Score(item, reply.Result, summary, conditional);
        bool falseRefusal = !expectedRefusal && !conditional && !item.Tags.Contains("no-llm", StringComparer.Ordinal) && (reply.Result.Unknown || reply.Result.Policies.Count + reply.Result.EconomyCommands.Count == 0);
        string score = correct ? "correct" : expectedRefusal ? "unexpected-command" : falseRefusal ? "false-refusal" : "wrong-command";
        return new ScoreRow { Id = item.Id, Text = item.Text, Tags = string.Join("|", item.Tags), AnswerJson = answerForCsv,
            ExpectedRefusal = expectedRefusal, Correct = correct, FalseRefusal = falseRefusal, ElapsedMs = elapsed,
            InputTokens = reply.Usage.InputTokens, OutputTokens = reply.Usage.OutputTokens, CacheReadInputTokens = reply.Usage.CacheReadInputTokens,
            CostYen = reply.CostYen, Score = score, ResultReason = reply.Result.Reason, IssuedCount = reply.Result.Policies.Count + reply.Result.EconomyCommands.Count, RejectedReasons = string.Join("|", reply.Result.Rejected.Select(x => x.Reason)) };
    }

    private static HttpCommandInterpreter CreateInterpreter(string model)
    {
        if (model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) return new ClaudeCommandInterpreter(() => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"), timeout: TimeSpan.FromSeconds(60));
        if (model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)) return new OpenAiCommandInterpreter(() => Environment.GetEnvironmentVariable("OPENAI_API_KEY"), timeout: TimeSpan.FromSeconds(60));
        if (model.Equals("local-llm", StringComparison.OrdinalIgnoreCase)) return new LocalLlmCommandInterpreter(Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL") ?? "local-model", url: Environment.GetEnvironmentVariable("LOCAL_LLM_URL") ?? LocalLlmCommandInterpreter.DefaultUrl, timeout: TimeSpan.FromSeconds(60));
        throw new InvalidDataException("評価対象のモデル名が不明です: " + model);
    }

    private static object Summarize(string model, IReadOnlyList<ScoreRow> rows)
    {
        var byTag = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (string tag in rows.SelectMany(r => r.Tags.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            var group = rows.Where(r => r.Tags.Split('|').Contains(tag, StringComparer.Ordinal)).ToArray();
            byTag[tag] = new { count = group.Length, correct = group.Count(r => r.Correct), accuracy = Rate(group.Count(r => r.Correct), group.Length) };
        }
        var refused = rows.Where(r => r.ExpectedRefusal).ToArray();
        var nonRefused = rows.Where(r => !r.ExpectedRefusal).ToArray();
        return new
        {
            model,
            count = rows.Count,
            correct = rows.Count(r => r.Correct),
            accuracy = Rate(rows.Count(r => r.Correct), rows.Count),
            byTag,
            shouldRefuse = new { count = refused.Length, correct = refused.Count(r => r.Correct), rate = Rate(refused.Count(r => r.Correct), refused.Length) },
            shouldNotRefuse = new { count = nonRefused.Length, falseRefusal = nonRefused.Count(r => r.FalseRefusal), rate = Rate(nonRefused.Count(r => r.FalseRefusal), nonRefused.Length) },
            responseTimeMs = new { p50 = Percentile(rows.Select(r => r.ElapsedMs)), p95 = Percentile(rows.Select(r => r.ElapsedMs), .95) },
            costYen = new { perRequest = rows.Count == 0 ? 0m : rows.Sum(r => r.CostYen) / rows.Count, total = rows.Sum(r => r.CostYen) },
            tokens = new { input = rows.Sum(r => r.InputTokens), output = rows.Sum(r => r.OutputTokens), cacheRead = rows.Sum(r => r.CacheReadInputTokens) },
            csv = "問題ごとの AnswerJson / Score / トークン数を --csv の CSV に保存"
        };
    }

    private static decimal Rate(int numerator, int denominator) => denominator == 0 ? 0m : (decimal)numerator / denominator;
    private static long Percentile(IEnumerable<long> values, double percentile = .5)
    {
        var sorted = values.OrderBy(x => x).ToArray(); if (sorted.Length == 0) return 0;
        int index = Math.Max(0, (int)Math.Ceiling(sorted.Length * percentile) - 1); return sorted[index];
    }

    private static bool Score(EvalCase item, AiCommandInterpretationResult result, AiSituationSummary summary, bool conditionalMayRefuse)
    {
        if (item.Tags.Contains("no-llm", StringComparer.Ordinal)) return true;
        if (IsUnknown(item.Expect)) return result.Unknown || (result.Policies.Count == 0 && result.EconomyCommands.Count == 0);
        if (conditionalMayRefuse) return result.Unknown || ScoreConditional(item.Id, result);
        if (result.Unknown) return false;
        var expected = item.Expect.EnumerateArray().SelectMany(e =>
            e.TryGetProperty("count", out var count) && (e.GetProperty("kind").GetString() == "Train" || e.GetProperty("kind").GetString() == "PlaceBuilding")
                ? Enumerable.Repeat(e, count.GetInt32()) : new[] { e }).ToArray();
        if (result.Policies.Count + result.EconomyCommands.Count != expected.Length) return false;
        var used = new bool[expected.Length];
        foreach (var policy in result.Policies)
        {
            int match = FindExpected(expected, used, e => IsPolicy(e, policy, summary));
            if (match < 0) return false; used[match] = true;
        }
        foreach (var economy in result.EconomyCommands)
        {
            int match = FindExpected(expected, used, e => IsEconomy(e, economy, summary));
            if (match < 0) return false; used[match] = true;
        }
        return true;
    }

    private static bool ScoreConditional(int id, AiCommandInterpretationResult result)
    {
        if (result.Operations.Count != 1 || result.Operations[0].Then.Count != 1) return false;
        var operation = result.Operations[0]; var action = operation.Then[0];
        if (id == 45)
            return operation.When.Kind == OperationConditionKind.EnemyNearObjective && operation.When.ObjectiveId == 1 && action.IsPolicy && action.Policy.Kind == PolicyKind.Defend && action.Policy.Target.Kind == ScopeKind.Army && action.Policy.Target.Id == 3 && action.Policy.Goal.Kind == GoalKind.Outpost && action.Policy.Goal.Id == 1;
        if (id == 46)
            return operation.When.Kind == OperationConditionKind.OwnArmyBelowPercent && operation.When.OwnArmyPermille == 500 && action.IsPolicy && action.Policy.Kind == PolicyKind.Retreat && action.Policy.Target.Kind == ScopeKind.All;
        if (id == 47)
            return operation.When.Kind == OperationConditionKind.MatchTimeAfter && operation.When.TimeTick == 12000 && action.IsPolicy && action.Policy.Kind == PolicyKind.Focus && action.Policy.Target.Kind == ScopeKind.All && action.Policy.Goal.Kind == GoalKind.Core && action.Policy.Goal.Id == 2;
        if (id == 48)
            return operation.When.Kind == OperationConditionKind.OutpostOwnerChangedToEnemy && operation.When.ObjectiveId == 2 && !action.IsPolicy && action.Economy.Kind == EconomyCommandKind.SetRegionControl && action.Economy.RegionId == 4 && action.Economy.Enabled;
        return false;
    }

    private static int FindExpected(JsonElement[] expected, bool[] used, Func<JsonElement, bool> predicate)
    { for (int i = 0; i < expected.Length; i++) if (!used[i] && predicate(expected[i])) return i; return -1; }
    private static bool IsPolicy(JsonElement e, UserPolicyIntent p, AiSituationSummary summary)
    {
        if (!e.TryGetProperty("kind", out var kind) || !Enum.TryParse(kind.GetString(), false, out PolicyKind expectedKind) || expectedKind != p.Kind) return false;
        if (!e.TryGetProperty("scope", out var scope) || !ScopeMatches(scope.GetString(), p.Target)) return false;
        if (e.TryGetProperty("goal", out var goal) && summary.TryGet(goal.GetString(), out var targetGoal) && targetGoal.HasGoal)
            return p.Goal.Kind == targetGoal.Goal.Kind && p.Goal.Id == targetGoal.Goal.Id;
        return !e.TryGetProperty("goal", out _);
    }
    private static bool IsEconomy(JsonElement e, EconomyCommand c, AiSituationSummary summary)
    {
        string kind = e.GetProperty("kind").GetString();
        if (kind == "CancelTraining") kind = "CancelTrain";
        if (!string.Equals(kind, c.Kind.ToString(), StringComparison.Ordinal)) return false;
        if (e.TryGetProperty("building", out var building) && (!Enum.TryParse(building.GetString(), false, out BuildingKind b) || c.Building != b)) return false;
        if (e.TryGetProperty("unit", out var unit) && (!Enum.TryParse(unit.GetString(), false, out UnitKind u) || c.Unit != u)) return false;
        if (e.TryGetProperty("civ", out var civ) && !civ.GetString().StartsWith("(", StringComparison.Ordinal) && (!Enum.TryParse(civ.GetString(), false, out CivKind civKind) || c.Civ != civKind)) return false;
        if (e.TryGetProperty("policy", out var policy) && !(policy.GetString() == "Economy" ? c.Policy == EconomyPolicy.Growth : Enum.TryParse(policy.GetString(), false, out EconomyPolicy p) && c.Policy == p)) return false;
        string regionName = e.TryGetProperty("region", out var region) ? region.GetString() : e.TryGetProperty("scope", out var scope) && scope.GetString().StartsWith("Region:", StringComparison.Ordinal) ? scope.GetString().Substring("Region:".Length) : null;
        if (!string.IsNullOrEmpty(regionName) && (!summary.TryGet(regionName, out var r) || c.RegionId != r.Scope.Id)) return false;
        if (e.TryGetProperty("control", out var control) && c.Enabled != (control.GetString() == "Human")) return false;
        if (e.TryGetProperty("producer", out var producer) && summary.TryGet(producer.GetString(), out var producerEntry) && c.ProducerId != producerEntry.Id) return false;
        return true;
    }
    private static bool ScopeMatches(string text, ScopeKey actual)
    {
        if (text == "All") return actual.Kind == ScopeKind.All;
        int colon = text.IndexOf(':'); if (colon < 0) return false;
        ScopeKind kind = text.StartsWith("Army:", StringComparison.Ordinal) ? ScopeKind.Army : text.StartsWith("Outpost:", StringComparison.Ordinal) ? ScopeKind.Outpost : ScopeKind.Region;
        return actual.Kind == kind && actual.Id != 0;
    }

    private static string FakeAnswer(EvalCase item)
    {
        if (item.Tags.Contains("no-llm", StringComparer.Ordinal))
            return "{\"commands\":[],\"say\":\"ローカル処理\"}";
        if (IsUnknown(item.Expect))
            return JsonSerializer.Serialize(new Dictionary<string, object> { ["commands"] = Array.Empty<object>(), ["operations"] = Array.Empty<object>(), ["unknown"] = true, ["reason"] = item.Reason, ["say"] = "" });
        if (item.Tags.Any(t => t.Equals("G-4", StringComparison.OrdinalIgnoreCase)))
        {
            var operation = new Dictionary<string, object>();
            if (item.Id == 45) operation = Conditional("EnemyNear", "北の拠点", 1, null, null, null, new[] { Command("policy", "Defend", "南の予備", "北の拠点") }, true);
            else if (item.Id == 46) operation = Conditional("OwnArmyBelowPercent", null, null, 500, null, null, new[] { Command("policy", "Retreat", "全部隊", null) }, true);
            else if (item.Id == 47) operation = Conditional("TimeAfter", null, null, null, 10, null, new[] { Command("policy", "Focus", "全部隊", "敵のコア") }, true);
            else operation = Conditional("OwnerChangedToEnemy", "南の拠点", null, null, null, null, new[] { Command("economy", "SetRegionControl", null, null, "区域4", "Human") }, true);
            return JsonSerializer.Serialize(new Dictionary<string, object> { ["commands"] = Array.Empty<object>(), ["operations"] = new[] { operation }, ["unknown"] = false, ["reason"] = null, ["say"] = "" });
        }
        var commands = new List<Dictionary<string, object>>();
        foreach (var e in item.Expect.EnumerateArray())
        {
            string kind = e.GetProperty("kind").GetString(); var command = new Dictionary<string, object>();
            if (kind == "SetRegionControl" || kind == "SetEconomyPolicy" || kind == "AdvanceAge" || kind == "PlaceBuilding" || kind == "Train" || kind == "CancelTraining")
            {
                command["type"] = "economy"; command["kind"] = kind == "CancelTraining" ? "CancelTrain" : kind;
                Copy(command, e, "region");
                if (kind == "SetEconomyPolicy" && !command.ContainsKey("region") && e.TryGetProperty("scope", out var regionScope) && regionScope.GetString().StartsWith("Region:", StringComparison.Ordinal)) command["region"] = regionScope.GetString().Substring("Region:".Length);
                Copy(command, e, "control"); Copy(command, e, "policy");
                if (kind == "SetEconomyPolicy" && e.TryGetProperty("policy", out var policyValue) && policyValue.GetString() == "Economy") command["policy"] = "Economy";
                Copy(command, e, "civ");
                if (kind == "AdvanceAge" && (!e.TryGetProperty("civ", out var civValue) || civValue.GetString().StartsWith("(", StringComparison.Ordinal))) command["civ"] = "Primitive";
                Copy(command, e, "building"); Copy(command, e, "unit"); Copy(command, e, "count"); Copy(command, e, "producer");
                if (e.TryGetProperty("place", out var place)) command["location"] = place.GetString() == "auto" ? "お任せ" : place.GetString();
            }
            else
            {
                command["type"] = "policy"; command["kind"] = kind; command["scope"] = ScopeName(e.GetProperty("scope").GetString());
                Copy(command, e, "goal"); Copy(command, e, "reservePermille"); Copy(command, e, "allowedLossPermille");
                if (e.TryGetProperty("allowedLoss", out var loss) && loss.GetString() == "half") command["allowedLossPermille"] = 500;
            }
            commands.Add(command);
        }
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["commands"] = commands, ["operations"] = Array.Empty<object>(), ["say"] = "", ["reason"] = null, ["unknown"] = false });
    }
    private static Dictionary<string, object> Command(string type, string kind, string scope, string goal, string region = null, string control = null)
        => new Dictionary<string, object> { ["type"] = type, ["kind"] = kind, ["scope"] = scope, ["goal"] = goal, ["region"] = region, ["building"] = null, ["location"] = null, ["producer"] = null, ["unit"] = null, ["civ"] = null, ["policy"] = null, ["control"] = control, ["sequence"] = null, ["count"] = null, ["reservePermille"] = null, ["allowedLossPermille"] = null, ["enabled"] = null };
    private static Dictionary<string, object> Conditional(string kind, string objective, int? count, int? permille, int? minutes, string statement, IEnumerable<Dictionary<string, object>> then, bool once)
        => new Dictionary<string, object> { ["when"] = new Dictionary<string, object> { ["kind"] = kind, ["objective"] = objective, ["count"] = count, ["permille"] = permille, ["minutes"] = minutes, ["statement"] = statement, ["all"] = null }, ["then"] = then.ToArray(), ["once"] = once };
    private static void Copy(Dictionary<string, object> target, JsonElement source, string name) { if (source.TryGetProperty(name, out var value)) target[name] = value.ValueKind == JsonValueKind.Number ? value.GetInt32() : value.GetString(); }
    private static string ScopeName(string value)
    { if (value == "All") return "全部隊"; int colon = value.IndexOf(':'); return colon >= 0 ? value.Substring(colon + 1) : value; }

    private static FactionFrame FixedFrame()
    {
        var north = new SimPoint(Fix64.FromInt(0), Fix64.FromInt(100)); var south = new SimPoint(Fix64.FromInt(0), Fix64.FromInt(-100));
        var observation = new FactionObservation(1, 100, new[]
        {
            new OwnArmyView(1, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(0), Fix64.FromInt(60)), 20, default),
            new OwnArmyView(2, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(0), Fix64.FromInt(20)), 20, default),
            new OwnArmyView(3, 1, UnitKind.Scout, new SimPoint(Fix64.FromInt(0), Fix64.FromInt(-20)), 5, default)
        }, Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), new[]
        {
            new KnownObjective(GoalKind.Outpost, 1, north, true, 1, false, 0, 100), new KnownObjective(GoalKind.Outpost, 2, south, true, 1, false, 0, 100),
            new KnownObjective(GoalKind.Core, 1, new SimPoint(Fix64.FromInt(-100), Fix64.FromInt(0)), true, 1, true, 1000, 100),
            new KnownObjective(GoalKind.Core, 2, new SimPoint(Fix64.FromInt(100), Fix64.FromInt(0)), true, 2, true, 1000, 100)
        });
        var economy = new EconomyView(100, 100, 10, 30, 0, 0, false, 2, 50, 10, 10, 10, Array.Empty<VillagerView>(), Array.Empty<BuildingView>(), Array.Empty<ResourceView>(),
            industry: false, ore: 0, metal: 0, beltWoodCost: 0, beltTicksPerCell: 0, belts: null, infantryMetalCost: 0, mineWoodCost: 0, smelterWoodCost: 0, mineSizeCells: 0, smelterSizeCells: 0, corePlayerHeld: false, policy: EconomyPolicy.Balanced,
            ages: true, civ: CivKind.Primitive, advancingTo: CivKind.Primitive, advanceRemaining: 0, advanceFoodCost: 0, advanceWoodCost: 0, farmWoodCost: 0, farmSizeCells: 0, scoutFoodCost: 0, houseWoodCost: 50, dropSiteWoodCost: 0, stone: 0, wallStoneCost: 0, towerWoodCost: 50, towerStoneCost: 0, blacksmithWoodCost: 0, techs: 0, techFoodCosts: null, techWoodCosts: null, techMetalCosts: null, age: 0, age2FoodCost: 0, age2WoodCost: 0, archerFoodCost: 0, archerWoodCost: 0, cavalryFoodCost: 0, cavalryWoodCost: 0, cavalryMetalCost: 0, marketWoodCost: 0, workshopWoodCost: 0, tradeLot: 0, tradeReturn: 0, ramFoodCost: 0, ramWoodCost: 0, age3FoodCost: 0, age3WoodCost: 0, rangeWoodCost: 0, stableWoodCost: 0, castleWoodCost: 0, castleStoneCost: 0);
        return new FactionFrame(100, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(), Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()), new MatchResult(false, 0, false, false, true), economy: economy,
            regions: new[] { new RegionView(1, RegionCenterKind.None, 0, default, RegionControl.Ai, PolicyKind.ReturnToAuto, EconomyPolicy.Balanced), new RegionView(2, RegionCenterKind.None, 0, default, RegionControl.Ai, PolicyKind.ReturnToAuto, EconomyPolicy.Balanced), new RegionView(3, RegionCenterKind.None, 0, default, RegionControl.Ai, PolicyKind.ReturnToAuto, EconomyPolicy.Balanced), new RegionView(4, RegionCenterKind.None, 0, default, RegionControl.Ai, PolicyKind.ReturnToAuto, EconomyPolicy.Balanced) });
    }
    private static void AddEvaluationAliases(AiSituationSummary s)
    {
        s.AddAlias("北軍", "第1軍"); s.AddAlias("南軍", "第2軍"); s.AddAlias("南にいる部隊", "第2軍"); s.AddAlias("斥候", "第3軍"); s.AddAlias("南の予備", "第3軍"); s.AddAlias("自分のコア", "自軍コア"); s.AddAlias("敵のコア", "敵コア"); s.AddAlias("支城の区域", "区域2"); s.AddAlias("兵舎1", "第1軍"); s.AddAlias("兵舎", "兵舎1"); s.AddAlias("北の拠点の近く", "北の拠点");
        s.AddGoalAlias("区域1の中心", new PolicyGoal(GoalKind.Point, 101, default), true, default);
        s.AddGoalAlias("東の資源の固まりの近く", new PolicyGoal(GoalKind.Point, 102, default), true, default);
    }
    private static ScopeKey? ParseSelected(string selected)
    { if (string.IsNullOrEmpty(selected)) return null; int c = selected.IndexOf(':'); if (c < 0) return null; ScopeKind kind = selected.StartsWith("Army:", StringComparison.Ordinal) ? ScopeKind.Army : selected.StartsWith("Outpost:", StringComparison.Ordinal) ? ScopeKind.Outpost : ScopeKind.Region; uint id = uint.TryParse(selected.Substring(c + 1).Replace("第", "").Replace("軍", ""), out var n) ? n : 1; return new ScopeKey(1, kind, id); }
    private static bool IsUnknown(JsonElement e) => e.ValueKind == JsonValueKind.String && e.GetString() == "unknown";
    private static List<EvalCase> Read(string path)
    { var result = new List<EvalCase>(); foreach (string line in File.ReadLines(path, Encoding.UTF8)) { if (string.IsNullOrWhiteSpace(line)) continue; using var doc = JsonDocument.Parse(line); var root = doc.RootElement; result.Add(new EvalCase { Id = root.GetProperty("id").GetInt32(), Text = root.GetProperty("text").GetString() ?? "", Selected = root.GetProperty("selected").ValueKind == JsonValueKind.Null ? null : root.GetProperty("selected").GetString(), Expect = root.GetProperty("expect").Clone(), Reason = root.TryGetProperty("reason", out var reason) ? reason.GetString() : "", Tags = root.GetProperty("tags").EnumerateArray().Select(x => x.GetString()).ToArray() }); } return result; }
    private static void WriteCsv(string path, IEnumerable<ScoreRow> rows)
    { using var w = new StreamWriter(path, false, new UTF8Encoding(false)); w.WriteLine("id,text,tags,answer_json,expected_refusal,correct,false_refusal,elapsed_ms,input_tokens,output_tokens,cache_read_input_tokens,cost_yen,score,result_reason,issued_count,rejected_reasons"); foreach (var r in rows) w.WriteLine(string.Join(",", new[] { r.Id.ToString(CultureInfo.InvariantCulture), Csv(r.Text), Csv(r.Tags), Csv(r.AnswerJson), r.ExpectedRefusal.ToString(), r.Correct.ToString(), r.FalseRefusal.ToString(), r.ElapsedMs.ToString(CultureInfo.InvariantCulture), r.InputTokens.ToString(CultureInfo.InvariantCulture), r.OutputTokens.ToString(CultureInfo.InvariantCulture), r.CacheReadInputTokens.ToString(CultureInfo.InvariantCulture), r.CostYen.ToString(CultureInfo.InvariantCulture), Csv(r.Score), Csv(r.ResultReason), r.IssuedCount.ToString(CultureInfo.InvariantCulture), Csv(r.RejectedReasons) })); }
    private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    private static string Required(Dictionary<string, string> options, string key) => options.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Missing " + key);
}
