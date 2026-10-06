using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tactics
{
    /// <summary>Writes the bounded, AI-readable files belonging to one match.</summary>
    public sealed partial class MatchPackWriter
    {
        public const int Version = 1;
        public const int SnapshotIntervalTicks = 600;
        private const int MaxTacticLogRecords = 20000;
        private readonly string directory;
        private readonly ScenarioDefinition scenario;
        private readonly string westTactic;
        private readonly string eastTactic;
        private readonly List<string> tacticLines = new List<string>();
        private readonly List<string> timelineLines = new List<string>();
        private readonly List<MatchPackSnapshot> snapshots = new List<MatchPackSnapshot>();
        private readonly HashSet<string> seenEvents = new HashSet<string>(StringComparer.Ordinal);
        private string rulebookMarkdown;

        public MatchPackWriter(string outputDirectory, ScenarioDefinition scenario, string westTactic, string eastTactic)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("記録パックの出力先が空です。", nameof(outputDirectory));
            this.directory = outputDirectory;
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            this.westTactic = string.IsNullOrEmpty(westTactic) ? "auto" : westTactic;
            this.eastTactic = string.IsNullOrEmpty(eastTactic) ? "auto" : eastTactic;
            rulebookMarkdown = TacticRulebook.Write(scenario).markdown;
        }

        public string DirectoryPath => directory;

        public void RecordInitial(Battle simulation)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            RecordEvents(simulation);
            RecordSnapshotIfDue(simulation);
            RecordCredit(simulation);
        }

        public void RecordTactic(TacticHostTickResult result, uint factionId, string selection)
        {
            if (result == null || (!result.Called && (result.ParamChanges == null || result.ParamChanges.Count == 0)) || tacticLines.Count >= MaxTacticLogRecords) return;
            var commands = result.Commands ?? new TacticCommandResult();
            var b = new StringBuilder(); b.Append('{');
            Field(b, "tick", result.Tick.ToString(CultureInfo.InvariantCulture));
            Field(b, "faction", factionId.ToString(CultureInfo.InvariantCulture));
            Field(b, "tactic", Quote(selection ?? ""));
            Field(b, "commandJson", Quote(result.CommandJson ?? ""));
            Field(b, "sentPolicies", result.SentPolicies.ToString(CultureInfo.InvariantCulture));
            Field(b, "sentEconomy", result.SentEconomy.ToString(CultureInfo.InvariantCulture));
            Field(b, "rejected", RejectedJson(commands.Rejected));
            Field(b, "failure", result.Failure == null ? "null" : FailureJson(result.Failure));
            Field(b, "consoleLog", StringArrayJson(result.ConsoleLines));
            Field(b, "paramChanges", ParamChangesJson(result.ParamChanges));
            Field(b, "disabled", result.Disabled ? "true" : "false");
            b.Append('}'); tacticLines.Add(b.ToString());
        }

        /// <summary>Records a manual or automatic tactic reload in the same JSONL stream as tactic calls.</summary>
        public void RecordTacticReload(DateTime utcTime, long tick, uint factionId, string selection, bool success, bool automatic, string reason)
        {
            if (tacticLines.Count >= MaxTacticLogRecords) return;
            var b = new StringBuilder(); b.Append('{');
            Field(b, "kind", Quote("reload"));
            Field(b, "timeUtc", Quote(utcTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            Field(b, "tick", tick.ToString(CultureInfo.InvariantCulture));
            Field(b, "faction", factionId.ToString(CultureInfo.InvariantCulture));
            Field(b, "tactic", Quote(selection ?? ""));
            Field(b, "automatic", automatic ? "true" : "false");
            Field(b, "success", success ? "true" : "false");
            Field(b, "reason", Quote(reason ?? ""));
            b.Append('}'); tacticLines.Add(b.ToString());
        }

        public void RecordAfterStep(Battle simulation)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            RecordEvents(simulation);
            RecordSnapshotIfDue(simulation);
            RecordCredit(simulation);
        }

        public void Complete(IEnumerable<ScheduledInput> inputs, long tickLimit)
        {
            Directory.CreateDirectory(directory);
            var replayPath = Path.Combine(directory, "replay.rpl");
            using (var stream = File.Create(replayPath))
                ReplayRunner.Record(stream, scenario, inputs ?? Array.Empty<ScheduledInput>(), tickLimit, new BuildIdentity(), null, westTactic, eastTactic);
            File.WriteAllText(Path.Combine(directory, "rulebook.md"), rulebookMarkdown, new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(directory, "tactic-log.jsonl"), tacticLines, new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(directory, "timeline.jsonl"), timelineLines, new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(directory, "snapshots.jsonl"), snapshots.Select(SnapshotJson), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "summary.json"), SummaryJson(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "README.md"), Readme(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "pack.json"), PackJson(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "credit.json"), CreditJson(), new UTF8Encoding(false));
        }

        public void Complete(Battle simulation, IEnumerable<ScheduledInput> inputs, long tickLimit)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            RecordCredit(simulation, true);
            Complete(inputs, tickLimit);
            // Summary is written from the final simulation state, so replace the placeholder written by the overload.
            File.WriteAllText(Path.Combine(directory, "summary.json"), SummaryJson(simulation), new UTF8Encoding(false));
        }

        private void RecordEvents(Battle simulation)
        {
            for (uint faction = 1; faction <= 2; faction++)
                foreach (var e in simulation.Capture(faction).Events)
                {
                    string key = faction + ":" + e.Tick + ":" + e.Kind + ":" + e.Ordinal + ":" + e.SubjectId + ":" + e.CommandId + ":" + e.Value + ":" + e.Reason;
                    if (!seenEvents.Add(key)) continue;
                    var b = new StringBuilder(); b.Append('{');
                    Field(b, "tick", e.Tick.ToString(CultureInfo.InvariantCulture)); Field(b, "faction", faction.ToString(CultureInfo.InvariantCulture));
                    Field(b, "ordinal", e.Ordinal.ToString(CultureInfo.InvariantCulture)); Field(b, "kind", Quote(e.Kind.ToString()));
                    Field(b, "subjectId", e.SubjectId.ToString(CultureInfo.InvariantCulture)); Field(b, "commandId", e.CommandId.ToString(CultureInfo.InvariantCulture));
                    Field(b, "position", PointJson(e.Position)); Field(b, "value", e.Value.ToString(CultureInfo.InvariantCulture)); Field(b, "reason", Quote(e.Reason.ToString()));
                    Field(b, "text", Quote(EventText(e))); b.Append('}'); timelineLines.Add(b.ToString());
                }
        }

        private void RecordSnapshotIfDue(Battle simulation)
        {
            var snapshot = simulation.CaptureMatchPackSnapshot();
            if (snapshot.Tick % SnapshotIntervalTicks == 0 && snapshots.All(x => x.Tick != snapshot.Tick)) snapshots.Add(snapshot);
        }

        private string SummaryJson() => SummaryJson(null);
        private string SummaryJson(Battle simulation)
        {
            long tick = simulation == null ? 0 : simulation.Capture(1).Tick;
            var result = simulation == null ? new MatchResult(false, 0, false, false, true) : simulation.Capture(1).Result;
            var b = new StringBuilder(); b.Append('{');
            Field(b, "winnerFactionId", result.WinnerFactionId.ToString(CultureInfo.InvariantCulture)); Field(b, "ended", result.HasEnded ? "true" : "false");
            Field(b, "endTick", tick.ToString(CultureInfo.InvariantCulture)); Field(b, "minutes", (tick / 20m / 60m).ToString("0.###", CultureInfo.InvariantCulture));
            Field(b, "westTactic", Quote(westTactic)); Field(b, "eastTactic", Quote(eastTactic)); Field(b, "mapSeed", scenario.Seed.ToString(CultureInfo.InvariantCulture));
            Field(b, "rulebookVersion", Version.ToString(CultureInfo.InvariantCulture)); b.Append('}'); return b.ToString();
        }

        private string PackJson() => "{\"packVersion\":1,\"files\":[\"summary.json\",\"replay.rpl\",\"tactic-log.jsonl\",\"timeline.jsonl\",\"snapshots.jsonl\",\"credit.json\",\"README.md\",\"rulebook.md\"]}";

        private string Readme()
        {
            return "# 試合の記録パック\n\n"
                + "このフォルダは AI や人が試合を振り返り、戦術を直すための資料です。`pack.json` の `packVersion` は1です。\n\n"
                + "- `summary.json`: 勝者、決着 tick・分、両陣営の戦術名、地図の種、ルールブックの版。\n"
                + "- `replay.rpl`: 命令を再生できるリプレイ。\n"
                + "- `tactic-log.jsonl`: 戦術を呼んだ各回の tick、命令 JSON、送った数、捨てた命令と理由、失敗、`console.log`、つまみの変更（`paramChanges`）。読み直しは `kind=reload` として時刻、戦術、成功・失敗、理由を記録します。\n"
                + "- `snapshots.jsonl`: 600tick（30秒）ごとの霧なし集計。兵種別の兵数、村人数、資源、建物数、拠点の持ち主、時代と文明。\n"
                + "- `timeline.jsonl`: 両陣営から見えた `GameEvent` の時系列。\n"
                + "- `credit.json`: 部隊が従っていた命令の出どころ別の時間、損害、見えていた敵の撃破、拠点の出来事、命令の結果。\n"
                + "- `rulebook.md`: この試合で使ったルールブック。\n\n"
                + CreditReadmeTable()
                + "\n戦術を直すときは、まず `summary.json` と `tactic-log.jsonl` の失敗・捨てた命令を確認します。次に `credit.json` で人・お任せ・戦術のどの部隊がその場面にいたかを探し、`snapshots.jsonl` と `timeline.jsonl` で前後を見ます。必要なら `replay.rpl` を再生して確かめます。\n";
        }

        private static string SnapshotJson(MatchPackSnapshot s)
        {
            var b = new StringBuilder(); b.Append('{'); Field(b, "tick", s.Tick.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"factions\":["); for (int i = 0; i < s.Factions.Length; i++) { if (i != 0) b.Append(','); var f = s.Factions[i]; b.Append('{');
                Field(b, "factionId", f.FactionId.ToString(CultureInfo.InvariantCulture)); Field(b, "soldiers", CountsJson(f.Soldiers)); Field(b, "villagers", f.Villagers.ToString(CultureInfo.InvariantCulture));
                Field(b, "resources", "{\"Food\":" + f.Food + ",\"Wood\":" + f.Wood + ",\"Ore\":" + f.Ore + ",\"Metal\":" + f.Metal + ",\"Stone\":" + f.Stone + ",\"Gold\":" + f.Gold + ",\"Gems\":" + f.Gems + "}");
                Field(b, "buildings", CountsJson(f.Buildings)); Field(b, "age", f.Age.ToString(CultureInfo.InvariantCulture)); Field(b, "civ", Quote(f.Civ)); b.Append('}'); } b.Append(']');
            Field(b, "cores", ObjectivesJson(s.Cores)); Field(b, "outposts", ObjectivesJson(s.Outposts)); b.Append('}'); return b.ToString();
        }

        private static string CountsJson(IEnumerable<MatchPackCount> counts) => "[" + string.Join(",", counts.Select(x => "{\"kind\":" + Quote(x.Kind) + ",\"count\":" + x.Count.ToString(CultureInfo.InvariantCulture) + "}")) + "]";
        private static string ObjectivesJson(IEnumerable<MatchPackObjectiveSnapshot> values) => "[" + string.Join(",", values.Select(x => "{\"id\":" + x.Id + ",\"ownerFactionId\":" + x.OwnerFactionId + "}")) + "]";
        private static string RejectedJson(IEnumerable<TacticRejectedCommand> values) => "[" + string.Join(",", (values ?? Array.Empty<TacticRejectedCommand>()).Select(x => "{\"index\":" + x.Index + ",\"type\":" + Quote(x.Type) + ",\"reason\":" + Quote(x.Reason) + "}")) + "]";
        private static string FailureJson(TacticFailure x) => "{\"reason\":" + Quote(x.Reason) + ",\"consecutive\":" + x.Consecutive + "}";
        private static string ParamChangesJson(IEnumerable<TacticParamChange> values)
            => "[" + string.Join(",", (values ?? Array.Empty<TacticParamChange>()).Select(x => "{\"tick\":" + x.Tick.ToString(CultureInfo.InvariantCulture) + ",\"name\":" + Quote(x.Name) + ",\"from\":" + TacticParameterJson.Value(x.From) + ",\"to\":" + TacticParameterJson.Value(x.To) + "}")) + "]";
        private static string StringArrayJson(IEnumerable<string> values) => "[" + string.Join(",", (values ?? Array.Empty<string>()).Select(Quote)) + "]";
        private static string PointJson(SimPoint p) => "{\"x\":" + ((decimal)p.X.Raw / 65536m).ToString("0.###", CultureInfo.InvariantCulture) + ",\"z\":" + ((decimal)p.Z.Raw / 65536m).ToString("0.###", CultureInfo.InvariantCulture) + "}";
        private static string EventText(GameEvent e) => e.Kind + (e.Reason == ReasonCode.None ? "" : " (" + e.Reason + ")") + " subject=" + e.SubjectId;
        private static string Quote(string value) => TacticJson.Quote(value ?? "");
        private static void Field(StringBuilder b, string name, string value)
        {
            if (b.Length > 1 && b[b.Length - 1] != '{' && b[b.Length - 1] != '[' && b[b.Length - 1] != ',') b.Append(',');
            b.Append(Quote(name)).Append(':').Append(value);
        }
    }
}
