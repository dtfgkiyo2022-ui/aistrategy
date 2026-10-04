using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>固定語彙を返す参謀のJSON契約。LLMにはこのSchemaをそのまま渡せる。</summary>
    public static class AiCommandSchema
    {
        // Written for the strict modes of both Claude (strict tools) and OpenAI (strict json_schema): every property is
        // required and an optional one allows null, and there are no minimum/maximum (Claude rejects them on integers).
        // Ranges are checked by the interpreter instead (count 1-100, permille 0-1000, sequence >= 1).
        public const string Json = @"{
  ""type"": ""object"", ""additionalProperties"": false,
  ""required"": [""commands"", ""say"", ""reason"", ""unknown""],
  ""properties"": {
    ""commands"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"", ""additionalProperties"": false,
      ""required"": [""type"", ""kind"", ""scope"", ""goal"", ""region"", ""building"", ""location"", ""producer"", ""unit"", ""civ"", ""policy"", ""control"", ""sequence"", ""count"", ""reservePermille"", ""allowedLossPermille""],
      ""properties"": {
        ""type"": { ""type"": ""string"", ""enum"": [""policy"", ""economy""] },
        ""kind"": { ""type"": ""string"", ""enum"": [""Focus"", ""Defend"", ""AllowAbandon"", ""Retreat"", ""MaintainReserve"", ""Scout"", ""ReturnToAuto"", ""SetRegionControl"", ""SetEconomyPolicy"", ""AdvanceAge"", ""PlaceBuilding"", ""Train"", ""CancelTrain""] },
        ""scope"": { ""type"": [""string"", ""null""] }, ""goal"": { ""type"": [""string"", ""null""] },
        ""region"": { ""type"": [""string"", ""null""] }, ""building"": { ""type"": [""string"", ""null""] },
        ""location"": { ""type"": [""string"", ""null""] }, ""producer"": { ""type"": [""string"", ""null""] },
        ""unit"": { ""type"": [""string"", ""null""] }, ""civ"": { ""type"": [""string"", ""null""] },
        ""policy"": { ""type"": [""string"", ""null""] }, ""control"": { ""anyOf"": [ { ""type"": ""string"", ""enum"": [""Human"", ""Ai""] }, { ""type"": ""null"" } ] },
        ""sequence"": { ""type"": [""integer"", ""null""] }, ""count"": { ""type"": [""integer"", ""null""] },
        ""reservePermille"": { ""type"": [""integer"", ""null""] }, ""allowedLossPermille"": { ""type"": [""integer"", ""null""] }
      }
    } },
    ""say"": { ""type"": ""string"" }, ""reason"": { ""type"": [""string"", ""null""] },
    ""unknown"": { ""type"": ""boolean"" }
  }
}";

        public const string StableInstructions =
            "commandsは順番に実行する命令。対象と目標は戦況の名前表の文字列だけを使う。" +
            "不明・曖昧・未対応ならunknown=true、commands=[]、reasonを短く返す。";
    }

    public sealed class AiCommandInterpretationResult
    {
        public IReadOnlyList<UserPolicyIntent> Policies { get; internal set; } = Array.Empty<UserPolicyIntent>();
        public IReadOnlyList<EconomyCommand> EconomyCommands { get; internal set; } = Array.Empty<EconomyCommand>();
        public IReadOnlyList<AiRejectedCommand> Rejected { get; internal set; } = Array.Empty<AiRejectedCommand>();
        public string Say { get; internal set; } = "";
        public string Reason { get; internal set; } = "";
        public bool Unknown { get; internal set; }

        public string Report
        {
            get
            {
                var b = new StringBuilder();
                if (!string.IsNullOrEmpty(Say)) b.Append(Say);
                if (!string.IsNullOrEmpty(Reason)) { if (b.Length != 0) b.Append(" "); b.Append(Reason); }
                foreach (var rejected in Rejected)
                {
                    if (b.Length != 0) b.Append(" ");
                    b.Append("命令を破棄: ").Append(rejected.Reason);
                }
                return b.ToString();
            }
        }
    }

    public sealed class AiRejectedCommand
    {
        public int Index { get; internal set; }
        public string Reason { get; internal set; }
        public string Kind { get; internal set; }
    }

    public sealed class AiNameTableEntry
    {
        public string Name { get; internal set; }
        public ScopeKey Scope { get; internal set; }
        public bool HasScope { get; internal set; }
        public PolicyGoal Goal { get; internal set; }
        public bool HasGoal { get; internal set; }
        public uint Id { get; internal set; }
        public bool IsOwn { get; internal set; }
        public SimPoint Point { get; internal set; }
    }

    /// <summary>参謀が見てよい情報だけから作った短い戦況と、その中の名前→ID表。</summary>
    public sealed class AiSituationSummary
    {
        private readonly Dictionary<string, AiNameTableEntry> names = new Dictionary<string, AiNameTableEntry>(StringComparer.Ordinal);
        public uint FactionId { get; private set; }
        public long Tick { get; private set; }
        public string Text { get; private set; }
        public IReadOnlyList<AiNameTableEntry> NameTable { get; private set; }

        public bool TryGet(string name, out AiNameTableEntry entry) => names.TryGetValue(name ?? "", out entry);

        public string Prompt(string instruction)
        {
            var names = new StringBuilder();
            foreach (var entry in NameTable ?? Array.Empty<AiNameTableEntry>())
            {
                if (names.Length != 0) names.Append('、');
                names.Append(entry.Name);
            }
            return AiCommandSchema.StableInstructions + "\n名前表（この文字列だけを使う）:\n" + names +
                "\n戦況:\n" + Text + "\n指示:\n" + (instruction ?? "");
        }

        /// <summary>Host adapters may add a visible alias without changing its contract identity.</summary>
        public void AddAlias(string alias, string existingName)
        {
            if (string.IsNullOrEmpty(alias) || !TryGet(existingName, out var existing) || names.ContainsKey(alias)) return;
            var entry = new AiNameTableEntry { Name = alias, Scope = existing.Scope, HasScope = existing.HasScope,
                Goal = existing.Goal, HasGoal = existing.HasGoal, Id = existing.Id, IsOwn = existing.IsOwn, Point = existing.Point };
            names.Add(alias, entry);
            var list = new List<AiNameTableEntry>(NameTable ?? Array.Empty<AiNameTableEntry>()); list.Add(entry); NameTable = list.AsReadOnly();
        }

        public void AddGoalAlias(string alias, PolicyGoal goal, bool own, SimPoint point)
        {
            if (string.IsNullOrEmpty(alias) || names.ContainsKey(alias)) return;
            var entry = new AiNameTableEntry { Name = alias, Goal = goal, HasGoal = true, IsOwn = own, Id = goal.Id, Point = point };
            names.Add(alias, entry);
            var list = new List<AiNameTableEntry>(NameTable ?? Array.Empty<AiNameTableEntry>()); list.Add(entry); NameTable = list.AsReadOnly();
        }

        public static AiSituationSummary From(FactionFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var summary = new AiSituationSummary { FactionId = frame.FactionId, Tick = frame.Tick };
            var list = new List<AiNameTableEntry>();
            AddScope(summary, list, "全部隊", new ScopeKey(frame.FactionId, ScopeKind.All, 0), true);

            var armies = (frame.Observation?.OwnArmies ?? Array.Empty<OwnArmyView>()).OrderBy(a => a.Id).ToArray();
            for (int i = 0; i < armies.Length; i++)
                AddScope(summary, list, "第" + (i + 1).ToString(CultureInfo.InvariantCulture) + "軍",
                    new ScopeKey(frame.FactionId, ScopeKind.Army, armies[i].Id), true);

            var objectives = (frame.Observation?.Objectives ?? Array.Empty<KnownObjective>()).OrderBy(o => o.Kind).ThenBy(o => o.Id).ToArray();
            int outpostNumber = 1;
            foreach (var objective in objectives)
            {
                if (objective.Kind == GoalKind.Outpost)
                {
                    string name = OutpostName(objective, objectives, outpostNumber++);
                    bool own = objective.IsOwnerKnown && objective.OwnerFactionId == frame.FactionId;
                    var goal = new PolicyGoal(GoalKind.Outpost, objective.Id, objective.Position);
                    AddScope(summary, list, name, new ScopeKey(frame.FactionId, ScopeKind.Outpost, objective.Id), own);
                    AddGoal(summary, list, name, goal, own, objective.Position, objective.OwnerFactionId == frame.FactionId);
                }
                else if (objective.Kind == GoalKind.Core && objective.IsOwnerKnown)
                {
                    string name = objective.OwnerFactionId == frame.FactionId ? "自軍コア" : "敵コア";
                    var goal = new PolicyGoal(GoalKind.Core, objective.Id, objective.Position);
                    AddGoal(summary, list, name, goal, objective.OwnerFactionId == frame.FactionId, objective.Position, objective.OwnerFactionId == frame.FactionId);
                }
                else if (objective.Kind == GoalKind.Point)
                {
                    AddGoal(summary, list, "地点" + objective.Id.ToString(CultureInfo.InvariantCulture),
                        new PolicyGoal(GoalKind.Point, objective.Id, objective.Position), true, objective.Position, true);
                }
            }

            foreach (var region in (frame.Regions ?? Array.Empty<RegionView>()).OrderBy(r => r.Id))
                AddScope(summary, list, "区域" + region.Id.ToString(CultureInfo.InvariantCulture),
                    new ScopeKey(frame.FactionId, ScopeKind.Region, region.Id), true);

            if (frame.Economy != null)
            {
                int buildingNumber = 1;
                foreach (var building in frame.Economy.Buildings.Where(b => b.FactionId == frame.FactionId).OrderBy(b => b.Id))
                {
                    string name = BuildingName(building.Kind) + buildingNumber++.ToString(CultureInfo.InvariantCulture);
                    AddProducer(summary, list, name, building.Id, building.Center);
                }
            }
            summary.NameTable = list.AsReadOnly();
            summary.Text = BuildText(frame, armies, objectives);
            return summary;
        }

        private static string OutpostName(KnownObjective objective, IReadOnlyList<KnownObjective> all, int ordinal)
        {
            if (all.Count(o => o.Kind == GoalKind.Outpost) == 1) return "北の拠点";
            int north = all.Count(o => o.Kind == GoalKind.Outpost && o.Position.Z.Raw > objective.Position.Z.Raw);
            if (north == 0 && all.Count(o => o.Kind == GoalKind.Outpost) >= 2) return "北の拠点";
            int south = all.Count(o => o.Kind == GoalKind.Outpost && o.Position.Z.Raw < objective.Position.Z.Raw);
            if (south == 0 && all.Count(o => o.Kind == GoalKind.Outpost) >= 2) return "南の拠点";
            return "拠点" + ordinal.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildingName(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Barracks: return "兵舎";
                case BuildingKind.House: return "家";
                case BuildingKind.Farm: return "農場";
                case BuildingKind.Mine: return "鉱山";
                case BuildingKind.Smelter: return "溶鉱炉";
                case BuildingKind.Blacksmith: return "鍛冶場";
                default: return kind.ToString();
            }
        }

        private static void AddScope(AiSituationSummary s, List<AiNameTableEntry> list, string name, ScopeKey scope, bool own)
        {
            if (!s.names.ContainsKey(name))
            {
                var entry = new AiNameTableEntry { Name = name, Scope = scope, HasScope = true, Id = scope.Id, IsOwn = own };
                s.names.Add(name, entry); list.Add(entry);
            }
        }
        private static void AddGoal(AiSituationSummary s, List<AiNameTableEntry> list, string name, PolicyGoal goal, bool own, SimPoint point, bool addAlias)
        {
            var entry = new AiNameTableEntry { Name = name, Goal = goal, HasGoal = true, Id = goal.Id, IsOwn = own, Point = point };
            if (s.names.TryGetValue(name, out var old)) { old.Goal = goal; old.HasGoal = true; old.IsOwn = old.IsOwn || own; }
            else { s.names.Add(name, entry); list.Add(entry); }
        }
        private static void AddProducer(AiSituationSummary s, List<AiNameTableEntry> list, string name, uint id, SimPoint point)
        {
            var entry = new AiNameTableEntry { Name = name, Id = id, IsOwn = true, Point = point };
            if (!s.names.ContainsKey(name)) { s.names.Add(name, entry); list.Add(entry); }
        }
        private static string BuildText(FactionFrame frame, IReadOnlyList<OwnArmyView> armies, IReadOnlyList<KnownObjective> objectives)
        {
            var b = new StringBuilder();
            b.Append("tick ").Append(frame.Tick.ToString(CultureInfo.InvariantCulture)).Append("。自軍の軍団 ").Append(armies.Count).Append(" 個。\n");
            foreach (var army in armies)
                b.Append("自軍軍団#").Append(army.Id).Append(" 種別=").Append(army.Kind).Append(" 生存=").Append(army.AliveCount).Append("。\n");
            int visible = frame.Observation?.VisibleEnemies?.Count ?? 0;
            int contacts = frame.Observation?.Contacts?.Count ?? 0;
            b.Append("現在見えている敵=").Append(visible).Append("、既知の接触=").Append(contacts).Append("（未観測の敵は含めない）。\n");
            foreach (var objective in objectives)
            {
                b.Append("既知地点=").Append(objective.Kind).Append('#').Append(objective.Id);
                if (objective.IsOwnerKnown) b.Append(" 所有=").Append(objective.OwnerFactionId == frame.FactionId ? "自軍" : "敵/他");
                else b.Append(" 所有者不明");
                b.Append("。\n");
            }
            if (frame.Economy != null)
                b.Append("自軍内政: 食料=").Append(frame.Economy.Food).Append(" 木材=").Append(frame.Economy.Wood)
                    .Append(" 人口=").Append(frame.Economy.Population).Append('/').Append(frame.Economy.PopulationCap)
                    .Append(" 自動内政=").Append(frame.Economy.AutoEconomy ? "オン" : "オフ").Append("。\n");
            string text = b.ToString();
            return text.Length <= 8000 ? text : text.Substring(0, 7997) + "...";
        }
    }

    public interface IAiPlacementFinder
    {
        bool TryFindCell(FactionFrame frame, string locationName, BuildingKind building, out int cell, out string reason);
    }

    /// <summary>地図のセル規則をApplicationに漏らさない差し替え口。お任せ配置はこれを通る。</summary>
    public sealed class DelegateAiPlacementFinder : IAiPlacementFinder
    {
        private readonly Func<FactionFrame, string, BuildingKind, Tuple<bool, int, string>> finder;
        public DelegateAiPlacementFinder(Func<FactionFrame, string, BuildingKind, Tuple<bool, int, string>> finder)
        { this.finder = finder ?? throw new ArgumentNullException(nameof(finder)); }
        public bool TryFindCell(FactionFrame frame, string locationName, BuildingKind building, out int cell, out string reason)
        {
            var result = finder(frame, locationName, building) ?? Tuple.Create(false, 0, "置き場所を決められない");
            cell = result.Item2; reason = result.Item3; return result.Item1;
        }
    }

    public sealed class AiInterpretationContext
    {
        public FactionFrame Frame { get; set; }
        public AiSituationSummary Summary { get; set; }
        public bool HasFixedTarget { get; set; }
        public ScopeKey FixedTarget { get; set; }
        public long StartedTick { get; set; }
        public int MaxObservationAgeTicks { get; set; } = 240;
        public long DeadlineTick { get; set; }
        public IAiPlacementFinder PlacementFinder { get; set; }
    }

    /// <summary>LLMの返答を読み、検証に通ったものだけ既存Contractsへ変換する。</summary>
    public static class AiResponseInterpreter
    {
        public static AiCommandInterpretationResult Interpret(string json, AiInterpretationContext context)
        {
            if (context == null || context.Frame == null || context.Summary == null) throw new ArgumentNullException(nameof(context));
            try
            {
                var root = AiJson.AsObject(AiJson.Parse(json));
                var result = new AiCommandInterpretationResult { Say = AiJson.String(root, "say") ?? "", Reason = AiJson.String(root, "reason") ?? "" };
                result.Unknown = AiJson.Bool(root, "unknown");
                if (result.Unknown) return result;
                var policies = new List<UserPolicyIntent>();
                var economy = new List<EconomyCommand>();
                var rejected = new List<AiRejectedCommand>();
                var commands = AiJson.Array(root, "commands");
                if (commands == null) throw new FormatException("commands is required");
                ulong nextSequence = 1;
                for (int i = 0; i < commands.Count; i++)
                {
                    try { ConvertCommand(AiJson.AsObject(commands[i]), i, context, ref nextSequence, policies, economy); }
                    catch (AiCommandException e) { rejected.Add(new AiRejectedCommand { Index = i, Kind = e.Kind, Reason = e.Message }); }
                }
                result.Policies = policies.AsReadOnly(); result.EconomyCommands = economy.AsReadOnly(); result.Rejected = rejected.AsReadOnly();
                return result;
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is OverflowException)
            {
                return new AiCommandInterpretationResult { Reason = "読めない答え: " + e.Message };
            }
        }

        private static void ConvertCommand(Dictionary<string, object> command, int index, AiInterpretationContext c, ref ulong next, List<UserPolicyIntent> policies, List<EconomyCommand> economy)
        {
            string type = AiJson.String(command, "type"); string kindText = AiJson.String(command, "kind");
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(kindText)) throw new AiCommandException("形が違う", kindText);
            if (type == "policy") ConvertPolicy(command, kindText, c, ref next, policies);
            else if (type == "economy") ConvertEconomy(command, kindText, c, ref next, economy);
            else throw new AiCommandException("対応していない命令の種類", kindText);
        }

        private static void ConvertPolicy(Dictionary<string, object> command, string kindText, AiInterpretationContext c, ref ulong next, List<UserPolicyIntent> output)
        {
            if (!Enum.TryParse(kindText, false, out PolicyKind kind)) throw new AiCommandException("対応していない方針", kindText);
            ScopeKey scope;
            string scopeName = AiJson.String(command, "scope");
            if (string.IsNullOrEmpty(scopeName))
            {
                if (!c.HasFixedTarget) throw new AiCommandException("対象が不明", kindText);
                scope = c.FixedTarget;
            }
            else if (!c.Summary.TryGet(scopeName, out var scopeEntry) || !scopeEntry.HasScope || !scopeEntry.IsOwn)
                throw new AiCommandException("対象が名前表にないか、自分の物でない", kindText);
            else scope = scopeEntry.Scope;
            if (scope.FactionId != c.Frame.FactionId) throw new AiCommandException("自分の物でない対象", kindText);
            if (!IsScopeAllowed(kind, scope.Kind)) throw new AiCommandException("この範囲では出せない方針", kindText);
            PolicyGoal goal = default(PolicyGoal);
            string goalName = AiJson.String(command, "goal");
            bool needsGoal = kind == PolicyKind.Focus || kind == PolicyKind.Defend || kind == PolicyKind.Scout;
            if (!string.IsNullOrEmpty(goalName))
            {
                if (!c.Summary.TryGet(goalName, out var goalEntry) || !goalEntry.HasGoal)
                    throw new AiCommandException("目標が名前表にない", kindText);
                if ((kind == PolicyKind.Defend || kind == PolicyKind.Retreat) && !goalEntry.IsOwn)
                    throw new AiCommandException("自分の物でない目標", kindText);
                goal = goalEntry.Goal;
            }
            else if (needsGoal) throw new AiCommandException("目標が不明", kindText);
            var expiration = new Expiration(c.DeadlineTick == 0 ? c.StartedTick + c.MaxObservationAgeTicks : c.DeadlineTick,
                c.MaxObservationAgeTicks, ExpireFlags.ObservationTooOld);
            ushort reserve = AiJson.UInt16(command, "reservePermille", 0);
            ushort allowedLoss = AiJson.UInt16(command, "allowedLossPermille", 1000);
            if (reserve > 1000 || allowedLoss > 1000) throw new AiCommandException("割合が不正", kindText);
            output.Add(new UserPolicyIntent(next++, scope, kind, goal, 100, new LossBudget(allowedLoss),
                new EndCondition(EndKind.UntilReplaced, 0), reserve, expiration));
        }

        private static bool IsScopeAllowed(PolicyKind kind, ScopeKind scope)
        {
            if (kind == PolicyKind.Focus) return scope == ScopeKind.All || scope == ScopeKind.Army || scope == ScopeKind.Region;
            if (kind == PolicyKind.MaintainReserve) return scope != ScopeKind.Outpost;
            if (kind == PolicyKind.AllowAbandon) return scope == ScopeKind.Outpost || scope == ScopeKind.Region;
            return scope == ScopeKind.All || scope == ScopeKind.Army || scope == ScopeKind.Outpost || scope == ScopeKind.Region;
        }

        private static void ConvertEconomy(Dictionary<string, object> command, string kindText, AiInterpretationContext c, ref ulong next, List<EconomyCommand> output)
        {
            if (c.Frame.Economy == null) throw new AiCommandException("内政が対応していない", kindText);
            if (!Enum.TryParse(kindText, false, out EconomyCommandKind kind)) throw new AiCommandException("対応していない内政命令", kindText);
            ulong seq = AiJson.UInt64(command, "sequence", next++);
            string targetName = AiJson.String(command, "region");
            if (kind == EconomyCommandKind.SetRegionControl)
            {
                if (!c.Summary.TryGet(targetName, out var region) || !region.HasScope || region.Scope.Kind != ScopeKind.Region)
                    throw new AiCommandException("区域が名前表にない", kindText);
                string control = AiJson.String(command, "control") ?? "Human";
                if (!Enum.TryParse(control, false, out RegionControl regionControl)) throw new AiCommandException("区域担当が不明", kindText);
                output.Add(EconomyCommand.SetRegionControl(c.Frame.FactionId, seq, region.Scope.Id, regionControl)); return;
            }
            if (kind == EconomyCommandKind.SetAutoEconomy)
            {
                output.Add(EconomyCommand.Auto(c.Frame.FactionId, seq, AiJson.Bool(command, "enabled"))); return;
            }
            if (kind == EconomyCommandKind.ReturnEconomyToAuto)
            {
                output.Add(EconomyCommand.ReturnToAuto(c.Frame.FactionId, seq)); return;
            }
            if (c.Frame.Economy.AutoEconomy && kind != EconomyCommandKind.SetEconomyPolicy)
                throw new AiCommandException("内政の手動操作がオフ", kindText);
            switch (kind)
            {
                case EconomyCommandKind.SetEconomyPolicy:
                    if (!string.IsNullOrEmpty(targetName))
                    {
                        if (!c.Summary.TryGet(targetName, out var regionPolicy) || !regionPolicy.HasScope || regionPolicy.Scope.Kind != ScopeKind.Region)
                            throw new AiCommandException("区域が名前表にない", kindText);
                        output.Add(EconomyCommand.SetRegionPolicy(c.Frame.FactionId, seq, regionPolicy.Scope.Id, ParseEconomyPolicy(command)));
                    }
                    else output.Add(EconomyCommand.SetPolicy(c.Frame.FactionId, seq, ParseEconomyPolicy(command)));
                    return;
                case EconomyCommandKind.AdvanceAge:
                    if (!c.Frame.Economy.Ages) throw new AiCommandException("今のルールでは時代を進められない", kindText);
                    output.Add(EconomyCommand.Advance(c.Frame.FactionId, seq, ParseEnum<CivKind>(command, "civ"))); return;
                case EconomyCommandKind.PlaceBuilding:
                    var building = ParseEnum<BuildingKind>(command, "building");
                    int cell;
                    string location = AiJson.String(command, "location");
                    string namedPlacementReason = null;
                    if (string.IsNullOrEmpty(location) || location == "お任せ")
                    {
                        string placementReason = null;
                        if (c.PlacementFinder == null || !c.PlacementFinder.TryFindCell(c.Frame, location ?? "お任せ", building, out cell, out placementReason))
                            throw new AiCommandException(placementReason ?? "置き場所を決められない", kindText);
                    }
                    else if (c.Summary.TryGet(location, out var locationEntry) && c.PlacementFinder != null &&
                        c.PlacementFinder.TryFindCell(c.Frame, location, building, out cell, out namedPlacementReason))
                    {
                    }
                    else if (!int.TryParse(location, NumberStyles.Integer, CultureInfo.InvariantCulture, out cell))
                        throw new AiCommandException(namedPlacementReason ?? "置き場所が名前表にない", kindText);
                    int placeCount = Count(command);
                    for (int i = 0; i < placeCount; i++) output.Add(EconomyCommand.Place(c.Frame.FactionId, seq++, building, cell));
                    return;
                case EconomyCommandKind.Train:
                    int trainCount = Count(command);
                    for (int i = 0; i < trainCount; i++) output.Add(EconomyCommand.Train(c.Frame.FactionId, seq++, ProducerId(command, c), ParseEnum<UnitKind>(command, "unit")));
                    return;
                case EconomyCommandKind.CancelTrain:
                    output.Add(EconomyCommand.CancelTrain(c.Frame.FactionId, seq, ProducerId(command, c))); return;
                default: throw new AiCommandException("G-1で対応していない内政命令", kindText);
            }
        }

        private static uint ProducerId(Dictionary<string, object> command, AiInterpretationContext c)
        {
            string producer = AiJson.String(command, "producer");
            if (string.IsNullOrEmpty(producer) || producer == "コア") return 0;
            if (!c.Summary.TryGet(producer, out var entry) || entry.Id == 0) throw new AiCommandException("訓練元が名前表にない", "Train");
            return entry.Id;
        }
        private static int Count(Dictionary<string, object> command)
        {
            double value = AiJson.Number(command, "count", 1);
            if (value < 1 || value > 100 || value != Math.Truncate(value)) throw new AiCommandException("個数が不正", "count");
            return (int)value;
        }
        private static T ParseEnum<T>(Dictionary<string, object> obj, string key) where T : struct
        {
            string value = AiJson.String(obj, key);
            if (!Enum.TryParse(value, false, out T result)) throw new AiCommandException(key + "が不明", key);
            return result;
        }
        private static EconomyPolicy ParseEconomyPolicy(Dictionary<string, object> obj)
        {
            string value = AiJson.String(obj, "policy");
            if (value == "Economy") return EconomyPolicy.Growth;
            if (!Enum.TryParse(value, false, out EconomyPolicy result)) throw new AiCommandException("policyが不明", "SetEconomyPolicy");
            return result;
        }
        private sealed class AiCommandException : Exception
        { internal string Kind; internal AiCommandException(string message, string kind) : base(message) { Kind = kind; } }
    }

    public sealed class InterpreterRequest
    {
        public ulong RequestId { get; set; }
        public uint FactionId { get; set; }
        public string Instruction { get; set; }
        public ScopeKey FixedTarget { get; set; }
        public bool HasFixedTarget { get; set; }
        public AiSituationSummary Summary { get; set; }
        public string Model { get; set; }
        public long StartedTick { get; set; }
        public long DeadlineTick { get; set; }
    }

    public sealed class InterpreterReply
    {
        public ulong RequestId { get; internal set; }
        public long ReturnedTick { get; internal set; }
        public string Json { get; internal set; }
        public AiTokenUsage Usage { get; internal set; }
        public string Model { get; internal set; }
        public string FailureReason { get; internal set; }
    }

    public interface ICommandInterpreter
    {
        void Request(InterpreterRequest request);
        IReadOnlyList<InterpreterReply> Poll(long tick);
    }

    /// <summary>決まった答えをtick遅延で返す偽物の参謀。外部通信・時計・乱数を使わない。</summary>
    public sealed class FakeCommandInterpreter : ICommandInterpreter
    {
        private sealed class Pending { internal long Ready; internal InterpreterReply Reply; }
        private readonly int delayTicks;
        private readonly Func<InterpreterRequest, string> answer;
        private readonly List<Pending> pending = new List<Pending>();
        public FakeCommandInterpreter(int delayTicks, Func<InterpreterRequest, string> answer)
        { if (delayTicks < 0) throw new ArgumentOutOfRangeException(nameof(delayTicks)); this.delayTicks = delayTicks; this.answer = answer ?? throw new ArgumentNullException(nameof(answer)); }
        public void Request(InterpreterRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string json = answer(request);
            if (json == null) throw new InvalidOperationException("偽物の参謀がnullを返した");
            int inputTokens = AiCostCalculator.EstimateTokens(request.Summary == null ? request.Instruction : request.Summary.Prompt(request.Instruction));
            int outputTokens = AiCostCalculator.EstimateTokens(json);
            pending.Add(new Pending { Ready = checked(request.StartedTick + delayTicks), Reply = new InterpreterReply { RequestId = request.RequestId, Json = json, Model = request.Model ?? "gpt-6-luna", Usage = new AiTokenUsage(inputTokens, outputTokens) } });
        }
        public IReadOnlyList<InterpreterReply> Poll(long tick)
        {
            var ready = pending.Where(p => p.Ready <= tick).OrderBy(p => p.Ready).ThenBy(p => p.Reply.RequestId).Select(p => { p.Reply.ReturnedTick = tick; return p.Reply; }).ToArray();
            pending.RemoveAll(p => p.Ready <= tick); return ready;
        }
    }

    public sealed class InterpretedReply
    {
        public ulong RequestId { get; internal set; }
        public bool Late { get; internal set; }
        public AiCommandInterpretationResult Result { get; internal set; }
        public AiTokenUsage Usage { get; internal set; }
        public decimal CostYen { get; internal set; }
    }

    /// <summary>解釈中の固定対象と戦況を保持し、遅い返答を締め切りで捨てる。</summary>
    public sealed class CommandInterpreterCoordinator
    {
        private sealed class Open { internal AiInterpretationContext Context; internal string Model; }
        private readonly ICommandInterpreter interpreter;
        private readonly Dictionary<ulong, Open> open = new Dictionary<ulong, Open>();
        private ulong nextId = 1;
        public CommandInterpreterCoordinator(ICommandInterpreter interpreter) { this.interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter)); }
        public ulong Request(string instruction, FactionFrame frame, ScopeKey? fixedTarget, string model, long startedTick, int deadlineTicks, IAiPlacementFinder placementFinder = null)
        {
            var summary = AiSituationSummary.From(frame); ulong id = nextId++;
            var selected = AiModelCatalog.Get(model ?? "gpt-6-luna");
            var context = new AiInterpretationContext { Frame = frame, Summary = summary, StartedTick = startedTick, DeadlineTick = checked(startedTick + deadlineTicks), MaxObservationAgeTicks = deadlineTicks, PlacementFinder = placementFinder };
            if (fixedTarget.HasValue) { context.HasFixedTarget = true; context.FixedTarget = fixedTarget.Value; }
            string modelName = selected.Model;
            open.Add(id, new Open { Context = context, Model = modelName });
            interpreter.Request(new InterpreterRequest { RequestId = id, FactionId = frame.FactionId, Instruction = instruction ?? "", FixedTarget = fixedTarget.GetValueOrDefault(), HasFixedTarget = fixedTarget.HasValue, Summary = summary, Model = modelName, StartedTick = startedTick, DeadlineTick = context.DeadlineTick });
            return id;
        }
        private static AiCommandInterpretationResult LimitToModel(string model, AiCommandInterpretationResult result) => AiModelLimits.Apply(model, result);

        public IReadOnlyList<InterpretedReply> Poll(long tick)
        {
            var result = new List<InterpretedReply>();
            foreach (var reply in interpreter.Poll(tick))
            {
                if (!open.TryGetValue(reply.RequestId, out var item)) continue;
                open.Remove(reply.RequestId);
                bool late = reply.ReturnedTick > item.Context.DeadlineTick;
                var usage = reply.Usage;
                AiCommandInterpretationResult parsed;
                if (late) parsed = new AiCommandInterpretationResult { Reason = "締め切りを過ぎた答え" };
                else if (!string.IsNullOrEmpty(reply.FailureReason)) parsed = new AiCommandInterpretationResult { Unknown = true, Reason = reply.FailureReason };
                else parsed = LimitToModel(item.Model, AiResponseInterpreter.Interpret(reply.Json, item.Context));
                result.Add(new InterpretedReply { RequestId = reply.RequestId, Late = late, Usage = usage, CostYen = AiCostCalculator.Calculate(item.Model, usage), Result = parsed });
            }
            return result;
        }
    }

    internal static class AiModelLimits
    {
        /// <summary>
        /// A model that only takes fixed short orders (Jev) may return at most one command. The check is on the answer, not
        /// on the characters of the instruction, so an ordinary short order is never refused for its wording.
        /// </summary>
        internal static AiCommandInterpretationResult Apply(string model, AiCommandInterpretationResult result)
        {
            if (AiModelCatalog.Get(model).SupportsComplexInstructions || result.Unknown) return result;
            if (result.Policies.Count + result.EconomyCommands.Count <= 1) return result;
            return new AiCommandInterpretationResult { Unknown = true, Reason = "このモデルでは直せません。別のAIを選んでください" };
        }
    }

    public sealed class AiModelPrice
    {
        public string Model { get; set; } public decimal InputUsdPerMillion { get; set; } public decimal OutputUsdPerMillion { get; set; }
        public int DeadlineTicks { get; set; } public bool SupportsComplexInstructions { get; set; }
    }
    public static class AiModelCatalog
    {
        private static readonly Dictionary<string, AiModelPrice> fallbackPrices = new Dictionary<string, AiModelPrice>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-haiku-4-5"] = P("claude-haiku-4-5", 1, 5, 240, true), ["claude-sonnet-5-5"] = P("claude-sonnet-5-5", 2, 10, 240, true), ["claude-opus-5-5"] = P("claude-opus-5-5", 4, 20, 1200, true), ["claude-fable-5-1"] = P("claude-fable-5-1", 10, 50, 1200, true),
            ["gpt-6-luna"] = P("gpt-6-luna", .10m, .50m, 240, true), ["gpt-6.1-sol"] = P("gpt-6.1-sol", 2, 10, 240, true), ["gpt-6-astra"] = P("gpt-6-astra", 10, 50, 1200, true),
            ["jev"] = P("jev", .042m, 0, 240, false), ["local-llm"] = P("local-llm", 0, 0, 240, true)
        };
        private static decimal usdToYen;
        private static readonly Dictionary<string, AiModelPrice> prices = Load(out usdToYen);
        public static decimal UsdToYen => usdToYen;
        private static AiModelPrice P(string model, decimal input, decimal output, int deadline, bool complex) => new AiModelPrice { Model = model, InputUsdPerMillion = input, OutputUsdPerMillion = output, DeadlineTicks = deadline, SupportsComplexInstructions = complex };
        public static AiModelPrice Get(string model)
        {
            if (prices.TryGetValue(model ?? "", out var p)) return p;
            throw new ArgumentException("未知のモデルです。選択肢にあるモデルを指定してください: " + (model ?? "(null)"), nameof(model));
        }
        public static IReadOnlyList<AiModelPrice> All => prices.Values.OrderBy(p => p.Model, StringComparer.Ordinal).ToArray();
        public static IReadOnlyList<AiModelPrice> Available(Func<string, bool> configured = null)
            => All.Where(p => configured == null || configured(p.Model)).ToArray();

        private static Dictionary<string, AiModelPrice> Load(out decimal yen)
        {
            var result = new Dictionary<string, AiModelPrice>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in fallbackPrices) result[pair.Key] = pair.Value;
            yen = 150m;
            string path = Environment.GetEnvironmentVariable("AI_MODEL_PRICES_PATH");
            if (string.IsNullOrEmpty(path))
            {
                var dir = new DirectoryInfo(Environment.CurrentDirectory);
                for (int i = 0; i < 8 && dir != null && string.IsNullOrEmpty(path); i++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "Settings", "ai-model-prices.json");
                    if (File.Exists(candidate)) path = candidate;
                }
            }
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;
                var root = MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return result;
                if (root.TryGetValue("usdToYen", out var yenValue) && yenValue is double y && y > 0) yen = (decimal)y;
                if (!(root.TryGetValue("models", out var list) && list is List<object> models)) return result;
                foreach (var value in models)
                {
                    var o = value as Dictionary<string, object>; string name = Text(o, "model");
                    if (o == null || string.IsNullOrEmpty(name)) continue;
                    decimal input = Decimal(o, "inputUsdPerMillion", 0), output = Decimal(o, "outputUsdPerMillion", 0);
                    int deadline = (int)Decimal(o, "deadlineTicks", 240);
                    bool complex = !name.Equals("jev", StringComparison.OrdinalIgnoreCase);
                    result[name] = P(name, input, output, deadline, complex);
                }
            }
            catch (Exception) { yen = 150m; return result; }
            return result;
        }
        private static string Text(Dictionary<string, object> o, string key) => o != null && o.TryGetValue(key, out var v) ? v as string : null;
        private static decimal Decimal(Dictionary<string, object> o, string key, decimal fallback)
            => o != null && o.TryGetValue(key, out var v) && v is double d ? (decimal)d : fallback;
    }
    public readonly struct AiTokenUsage
    {
        public int InputTokens { get; } public int OutputTokens { get; } public int CacheReadInputTokens { get; } public int CacheCreationInputTokens { get; }
        public AiTokenUsage(int input, int output) : this(input, output, 0, 0) { }
        public AiTokenUsage(int input, int output, int cacheRead, int cacheCreation)
        { InputTokens = Math.Max(0, input); OutputTokens = Math.Max(0, output); CacheReadInputTokens = Math.Max(0, cacheRead); CacheCreationInputTokens = Math.Max(0, cacheCreation); }
    }
    public static class AiCostCalculator
    {
        public static decimal Calculate(string model, int inputTokens, int outputTokens)
            => Calculate(model, new AiTokenUsage(inputTokens, outputTokens));
        public static decimal Calculate(string model, AiTokenUsage usage)
        { var p = AiModelCatalog.Get(model); return (usage.InputTokens * p.InputUsdPerMillion + usage.OutputTokens * p.OutputUsdPerMillion + usage.CacheReadInputTokens * p.InputUsdPerMillion * .1m + usage.CacheCreationInputTokens * p.InputUsdPerMillion * 1.25m) * AiModelCatalog.UsdToYen / 1000000m; }
        public static int EstimateTokens(string text) => Math.Max(1, (text ?? "").Length / 4);
        public static decimal EstimateYen(string model, string prompt, int expectedOutputTokens = 200) => Calculate(model, EstimateTokens(prompt), expectedOutputTokens);
    }
    public sealed class AiBudgetMeter
    {
        public decimal BudgetYen { get; } public decimal SpentYen { get; private set; } public decimal RemainingYen => BudgetYen - SpentYen;
        public AiBudgetMeter(decimal budgetYen) { if (budgetYen < 0) throw new ArgumentOutOfRangeException(nameof(budgetYen)); BudgetYen = budgetYen; }
        public void Add(decimal yen) { if (yen < 0) throw new ArgumentOutOfRangeException(nameof(yen)); SpentYen += yen; }
    }

    internal static class AiJson
    {
        internal static object Parse(string text) { if (text == null) throw new FormatException("null"); return new Parser(text).Read(); }
        internal static Dictionary<string, object> AsObject(object value) => value as Dictionary<string, object> ?? throw new FormatException("object required");
        internal static List<object> Array(Dictionary<string, object> obj, string key) => obj.TryGetValue(key, out var v) ? v as List<object> ?? throw new FormatException(key + " must be array") : null;
        internal static string String(Dictionary<string, object> obj, string key) => obj.TryGetValue(key, out var v) ? v as string : null;
        internal static bool Bool(Dictionary<string, object> obj, string key) => obj.TryGetValue(key, out var v) && v is bool b && b;
        internal static double Number(Dictionary<string, object> obj, string key, double fallback)
        {
            if (!obj.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is long l) return l;
            if (v is double d) return d;
            throw new FormatException(key + " must be number");
        }
        internal static ushort UInt16(Dictionary<string, object> obj, string key, ushort fallback)
        {
            double value = Number(obj, key, fallback);
            if (value < 0 || value > ushort.MaxValue || value != Math.Truncate(value)) throw new FormatException(key + " must be integer");
            return (ushort)value;
        }
        internal static ulong UInt64(Dictionary<string, object> obj, string key, ulong fallback) { if (!obj.TryGetValue(key, out var v) || v == null) return fallback; if (v is long l && l >= 0) return checked((ulong)l); throw new FormatException(key + " must be integer"); }
        private sealed class Parser
        {
            private readonly string s; private int p; internal Parser(string text) { s = text; }
            internal object Read() { Skip(); var v = Value(); Skip(); if (p != s.Length) throw new FormatException("trailing data"); return v; }
            private object Value() { Skip(); if (p >= s.Length) throw new FormatException("unexpected end"); switch (s[p]) { case '{': return Object(); case '[': return List(); case '"': return Quoted(); case 't': Word("true"); return true; case 'f': Word("false"); return false; case 'n': Word("null"); return null; default: return Number(); } }
            private Dictionary<string, object> Object() { p++; var o = new Dictionary<string, object>(StringComparer.Ordinal); Skip(); if (Take('}')) return o; while (true) { Skip(); if (p >= s.Length || s[p] != '"') throw new FormatException("object key"); string k = Quoted(); Skip(); Need(':'); object v = Value(); if (!o.TryAdd(k, v)) throw new FormatException("duplicate key"); Skip(); if (Take('}')) return o; Need(','); } }
            private List<object> List() { p++; var a = new List<object>(); Skip(); if (Take(']')) return a; while (true) { a.Add(Value()); Skip(); if (Take(']')) return a; Need(','); } }
            private string Quoted() { Need('"'); var b = new StringBuilder(); while (p < s.Length) { char c = s[p++]; if (c == '"') return b.ToString(); if (c == '\\') { if (p >= s.Length) throw new FormatException("escape"); c = s[p++]; if (c == '"' || c == '\\' || c == '/') b.Append(c); else if (c == 'n') b.Append('\n'); else if (c == 'r') b.Append('\r'); else if (c == 't') b.Append('\t'); else if (c == 'u') { if (p + 4 > s.Length) throw new FormatException("escape"); int code = int.Parse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture); b.Append((char)code); p += 4; } else throw new FormatException("escape"); } else b.Append(c); } throw new FormatException("string"); }
            private long Number() { int start = p; if (p < s.Length && s[p] == '-') p++; while (p < s.Length && char.IsDigit(s[p])) p++; if (start == p) throw new FormatException("value"); return long.Parse(s.Substring(start, p - start), CultureInfo.InvariantCulture); }
            private void Word(string word) { if (p + word.Length > s.Length || !s.Substring(p, word.Length).Equals(word, StringComparison.Ordinal)) throw new FormatException("literal"); p += word.Length; }
            private void Skip() { while (p < s.Length && char.IsWhiteSpace(s[p])) p++; } private bool Take(char c) { if (p < s.Length && s[p] == c) { p++; return true; } return false; } private void Need(char c) { if (!Take(c)) throw new FormatException("expected " + c); }
        }
    }
}
