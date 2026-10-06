using System;
using System.Globalization;

namespace Rts.UnityHost
{
    /// <summary>Command-line settings for the product smoke runner. This type deliberately has no Unity dependency.</summary>
    public sealed class SmokeOptions
    {
        public const int DefaultTicks = 2400;
        public const string DefaultOutputPath = "rts-smoke.json";

        public string WestTactic { get; private set; }
        public string EastTactic { get; private set; }
        public int Ticks { get; private set; }
        public ulong? Seed { get; private set; }
        public string OutputPath { get; private set; }

        private SmokeOptions()
        {
            WestTactic = "none";
            EastTactic = "none";
            Ticks = DefaultTicks;
            OutputPath = DefaultOutputPath;
        }

        public static bool HasSwitch(string[] args)
        {
            if (args == null) return false;
            foreach (var arg in args)
                if (string.Equals(arg, "-rts-smoke", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Returns false when the smoke switch is absent; an error is returned for a malformed smoke command.</summary>
        public static bool TryParse(string[] args, out SmokeOptions options, out string error)
        {
            options = null;
            error = null;
            if (!HasSwitch(args)) return false;
            var parsed = new SmokeOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i] ?? "";
                if (!string.Equals(key, "-rts-smoke", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsValueSwitch(key)) continue;
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[++i]))
                        return Fail("引数 " + key + " の値がありません。", out options, out error);
                    string value = args[i];
                    if (string.Equals(key, "-rts-smoke-west", StringComparison.OrdinalIgnoreCase)) parsed.WestTactic = NormalizeTactic(value);
                    else if (string.Equals(key, "-rts-smoke-east", StringComparison.OrdinalIgnoreCase)) parsed.EastTactic = NormalizeTactic(value);
                    else if (string.Equals(key, "-rts-smoke-out", StringComparison.OrdinalIgnoreCase)) parsed.OutputPath = value;
                    else if (string.Equals(key, "-rts-smoke-ticks", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ticks) || ticks <= 0)
                            return Fail("-rts-smoke-ticks は1以上の整数で指定してください。", out options, out error);
                        parsed.Ticks = ticks;
                    }
                    else if (string.Equals(key, "-rts-smoke-seed", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed))
                            return Fail("-rts-smoke-seed は0以上の整数で指定してください。", out options, out error);
                        parsed.Seed = seed;
                    }
                }
            }
            options = parsed;
            return true;
        }

        public static bool IsNone(string tactic)
        {
            return string.IsNullOrWhiteSpace(tactic) || string.Equals(tactic, "none", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeTactic(string tactic)
        {
            return IsNone(tactic) ? "none" : tactic.Trim();
        }

        private static bool IsValueSwitch(string key)
        {
            return string.Equals(key, "-rts-smoke-west", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "-rts-smoke-east", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "-rts-smoke-ticks", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "-rts-smoke-seed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "-rts-smoke-out", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Fail(string message, out SmokeOptions options, out string error)
        {
            options = null;
            error = message;
            return false;
        }
    }

    public sealed class SmokeSideResult
    {
        public string RequestedTactic { get; set; }
        public string Tactic { get; set; }
        public string Language { get; set; }
        public bool Loaded { get; set; }
        public string LoadError { get; set; }
        public int Calls { get; set; }
        public int SentCommands { get; set; }
        public int DiscardedCommands { get; set; }
        public int Failures { get; set; }
        public string LastFailureReason { get; set; }
        /// <summary>The first failure, including one while starting (for example Pyodide not becoming ready).</summary>
        public string FirstFailureReason { get; set; }
        public string[] ConsoleLog { get; set; }
        public int ExecutedAiCommands { get; set; }
        public int AcceptedAiCommands { get; set; }
        public double CallP50Milliseconds { get; set; }
    }

    public sealed class SmokeResult
    {
        public const int SchemaVersion = 1;
        public int TicksRequested { get; set; }
        public long Tick { get; set; }
        public ulong Seed { get; set; }
        public uint WinnerFactionId { get; set; }
        public string Winner { get; set; }
        public double JsCallP50Milliseconds { get; set; }
        public double PythonCallP50Milliseconds { get; set; }
        public SmokeSideResult West { get; set; }
        public SmokeSideResult East { get; set; }
    }

    /// <summary>Small deterministic JSON writer used by Unity and tested by the Headless test project.</summary>
    public static class SmokeJson
    {
        public static string Write(SmokeResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var json = new System.Text.StringBuilder(2048);
            json.Append("{\"schemaVersion\":").Append(SmokeResult.SchemaVersion)
                .Append(",\"mode\":\"rts-smoke\"")
                .Append(",\"ticksRequested\":").Append(result.TicksRequested.ToString(CultureInfo.InvariantCulture))
                .Append(",\"tick\":").Append(result.Tick.ToString(CultureInfo.InvariantCulture))
                .Append(",\"seed\":").Append(result.Seed.ToString(CultureInfo.InvariantCulture))
                .Append(",\"winnerFactionId\":").Append(result.WinnerFactionId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"winner\":");
            AppendString(json, result.Winner ?? "undecided");
            json.Append(",\"jsCallP50Milliseconds\":").Append(result.JsCallP50Milliseconds.ToString("0.###", CultureInfo.InvariantCulture));
            json.Append(",\"pythonCallP50Milliseconds\":").Append(result.PythonCallP50Milliseconds.ToString("0.###", CultureInfo.InvariantCulture));
            json.Append(",\"west\":"); AppendSide(json, result.West);
            json.Append(",\"east\":"); AppendSide(json, result.East);
            return json.Append('}').ToString();
        }

        private static void AppendSide(System.Text.StringBuilder json, SmokeSideResult side)
        {
            side = side ?? new SmokeSideResult();
            json.Append("{\"requestedTactic\":"); AppendString(json, side.RequestedTactic ?? "none");
            json.Append(",\"tactic\":"); AppendString(json, side.Tactic ?? "none");
            json.Append(",\"language\":"); AppendString(json, side.Language ?? "");
            json.Append(",\"loaded\":").Append(side.Loaded ? "true" : "false");
            json.Append(",\"loadError\":"); AppendString(json, side.LoadError ?? "");
            json.Append(",\"calls\":").Append(side.Calls.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"sentCommands\":").Append(side.SentCommands.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"discardedCommands\":").Append(side.DiscardedCommands.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"failures\":").Append(side.Failures.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"lastFailureReason\":"); AppendString(json, side.LastFailureReason ?? "");
            json.Append(",\"firstFailureReason\":"); AppendString(json, side.FirstFailureReason ?? "");
            json.Append(",\"consoleLog\":[");
            var lines = side.ConsoleLog ?? Array.Empty<string>();
            for (int i = 0; i < lines.Length; i++) { if (i != 0) json.Append(','); AppendString(json, lines[i] ?? ""); }
            json.Append("]");
            json.Append(",\"acceptedAiCommands\":").Append(side.AcceptedAiCommands.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"executedAiCommands\":").Append(side.ExecutedAiCommands.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"callP50Milliseconds\":").Append(side.CallP50Milliseconds.ToString("0.###", CultureInfo.InvariantCulture));
            json.Append('}');
        }

        private static void AppendString(System.Text.StringBuilder json, string value)
        {
            json.Append('"');
            foreach (char c in value ?? "")
            {
                if (c == '\\') json.Append("\\\\");
                else if (c == '"') json.Append("\\\"");
                else if (c == '\n') json.Append("\\n");
                else if (c == '\r') json.Append("\\r");
                else if (c == '\t') json.Append("\\t");
                else if (c < 32) json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else json.Append(c);
            }
            json.Append('"');
        }
    }
}
