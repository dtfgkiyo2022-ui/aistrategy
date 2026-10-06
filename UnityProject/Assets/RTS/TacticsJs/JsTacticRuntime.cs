using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Jint;
using Rts.Contracts;
using Rts.Tactics;

namespace Rts.TacticsJs
{
    /// <summary>
    /// Runs one JavaScript tactic in one Jint engine for the lifetime of a match.
    /// The engine is deliberately configured without CLR access, modules, or host I/O.
    /// </summary>
    public sealed class JsTacticRuntime : ITacticRuntime, ITacticLogSource, ITacticParameterRuntime
    {
        public const int SourceLimitBytes = 1024 * 1024;
        public const int ConsoleLineLimit = 20;
        public const int ConsoleCharacterLimit = 200;
        public const long MemoryLimitBytes = 64L * 1024L * 1024L;
        public const int StatementLimit = 1_000_000;
        public const int RecursionLimit = 256;
        public static readonly TimeSpan CallTimeout = TimeSpan.FromMilliseconds(50);

        private readonly string source;
        private readonly List<string> consoleLines = new List<string>();
        private readonly string runtimeName;
        private readonly IReadOnlyList<TacticParamDefinition> parameters;
        private readonly Dictionary<string, object> parameterValues = new Dictionary<string, object>(StringComparer.Ordinal);
        private Engine engine;
        private SplitMix64 random;
        private bool started;

        public JsTacticRuntime(string source, string name = "js", IReadOnlyList<TacticParamDefinition> parameters = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (Encoding.UTF8.GetByteCount(source) > SourceLimitBytes) throw new ArgumentException("JavaScriptの入口ファイルが1MiBを超えています。", nameof(source));
            this.source = source;
            runtimeName = string.IsNullOrEmpty(name) ? "js" : name;
            this.parameters = parameters ?? Array.Empty<TacticParamDefinition>();
            foreach (var parameter in this.parameters) parameterValues[parameter.Name] = parameter.DefaultValue;
            random = new SplitMix64(0);
            engine = CreateEngine();
        }

        public string Name => runtimeName;
        public IReadOnlyList<TacticParamDefinition> Parameters => parameters;

        public void SetParameters(IReadOnlyDictionary<string, object> values)
        {
            parameterValues.Clear();
            foreach (var parameter in parameters)
                parameterValues[parameter.Name] = values != null && values.TryGetValue(parameter.Name, out var value) ? value : parameter.DefaultValue;
        }

        public void Start(string setupJson)
        {
            if (started) throw new InvalidOperationException("JsTacticRuntimeは既に開始されています。");
            started = true;
            string inputSetup = setupJson ?? "{}";
            var setup = inputSetup.IndexOf("\"params\"", StringComparison.Ordinal) >= 0 ? inputSetup : TacticParameterJson.AddParams(inputSetup, parameterValues);
            ulong seed = ReadSeed(setup);
            random = new SplitMix64(seed);
            engine.SetValue("__tacticSetupJson", setup);
            engine.Execute(source);
            engine.Execute("if (typeof onStart === 'function') onStart(JSON.parse(__tacticSetupJson));");
        }

        public string Tick(string viewJson)
        {
            if (!started) Start("{}");
            if (viewJson == null) throw new ArgumentNullException(nameof(viewJson));
            engine.SetValue("__tacticViewJson", WithParams(viewJson));
            var result = engine.Evaluate("(function() { var output = onTick(JSON.parse(__tacticViewJson)); if (output && output.version === undefined) output.version = 1; return JSON.stringify(output); })()");
            string text = result.ToString();
            if (string.IsNullOrEmpty(text) || text == "undefined") throw new InvalidOperationException("onTickはJSON化できる値を返してください。");
            return text;
        }

        private string WithParams(string json)
        {
            return json != null && json.IndexOf("\"params\"", StringComparison.Ordinal) >= 0
                ? json : TacticParameterJson.AddParams(json ?? "{}", parameterValues);
        }

        public IReadOnlyList<string> TakeConsoleLines()
        {
            if (consoleLines.Count == 0) return Array.Empty<string>();
            var result = consoleLines.ToArray();
            consoleLines.Clear();
            return result;
        }

        private Engine CreateEngine()
        {
            var created = new Engine(options => options
                .LimitMemory(MemoryLimitBytes)
                .MaxStatements(StatementLimit)
                .LimitRecursion(RecursionLimit)
                .TimeoutInterval(CallTimeout));
            created.SetValue("__tacticConsoleLog", new Action<string>(RecordConsole));
            created.SetValue("__tacticRandom", new Func<double>(NextRandom));
            created.Execute(@"
                var console = { log: function() {
                    var values = [];
                    for (var i = 0; i < arguments.length; i++) values.push(String(arguments[i]));
                    __tacticConsoleLog(values.join(' '));
                }};
                Math.random = function() { return __tacticRandom(); };
            ");
            return created;
        }

        private void RecordConsole(string value)
        {
            if (consoleLines.Count >= ConsoleLineLimit) return;
            string text = value ?? "null";
            if (text.Length > ConsoleCharacterLimit) text = text.Substring(0, ConsoleCharacterLimit);
            consoleLines.Add(text);
        }

        private double NextRandom()
        {
            ulong value = random.NextUInt64();
            return (value >> 11) * (1.0 / 9007199254740992.0);
        }

        private static ulong ReadSeed(string json)
        {
            try
            {
                var root = TacticJsonForRuntime.Parse(json);
                if (root is Dictionary<string, object> obj && obj.TryGetValue("matchSeed", out var value))
                {
                    if (value is long signed && signed >= 0) return (ulong)signed;
                    if (value is decimal decimalValue && decimalValue >= 0 && decimal.Truncate(decimalValue) == decimalValue) return checked((ulong)decimalValue);
                }
            }
            catch (Exception) { }
            return 0;
        }
    }

    /// <summary>Small metadata parser kept separate so the JS runtime never needs file access.</summary>
    internal static class TacticJsonForRuntime
    {
        internal static object Parse(string json)
        {
            if (json == null) return null;
            var parser = new Parser(json);
            var value = parser.Value();
            parser.White();
            if (!parser.End) throw new FormatException("JSONの末尾にデータがあります。");
            return value;
        }

        internal static Dictionary<string, object> Object(object value, string name)
        {
            if (!(value is Dictionary<string, object> result)) throw new FormatException(name + "はオブジェクトです。");
            return result;
        }

        internal static List<object> Array(object value, string name)
        {
            if (!(value is List<object> result)) throw new FormatException(name + "は配列です。");
            return result;
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;
            internal Parser(string text) { this.text = text; }
            internal bool End => index >= text.Length;
            internal void White() { while (!End && char.IsWhiteSpace(text[index])) index++; }
            internal object Value()
            {
                White();
                if (End) throw new FormatException("JSONが空です。");
                switch (text[index])
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return String();
                    case 't': Literal("true"); return true;
                    case 'f': Literal("false"); return false;
                    case 'n': Literal("null"); return null;
                    default: return Number();
                }
            }
            private Dictionary<string, object> Object()
            {
                index++; var result = new Dictionary<string, object>(StringComparer.Ordinal); White();
                if (Take('}')) return result;
                while (true)
                {
                    White(); string key = String(); White(); Expect(':'); result.Add(key, Value()); White();
                    if (Take('}')) return result; Expect(',');
                }
            }
            private List<object> Array()
            {
                index++; var result = new List<object>(); White();
                if (Take(']')) return result;
                while (true) { result.Add(Value()); White(); if (Take(']')) return result; Expect(','); }
            }
            private string String()
            {
                Expect('"'); var result = new StringBuilder();
                while (!End)
                {
                    char c = text[index++];
                    if (c == '"') return result.ToString();
                    if (c < 0x20) throw new FormatException("JSON文字列に制御文字があります。");
                    if (c != '\\') { result.Append(c); continue; }
                    if (End) throw new FormatException("JSONエスケープが未完了です。");
                    c = text[index++];
                    switch (c)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (index + 4 > text.Length) throw new FormatException("JSON Unicodeエスケープが未完了です。");
                            result.Append((char)ushort.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            index += 4;
                            break;
                        default: throw new FormatException("未知のJSONエスケープです。");
                    }
                }
                throw new FormatException("JSON文字列が未完了です。");
            }
            private object Number()
            {
                int start = index; if (text[index] == '-') index++;
                Digits(); if (!End && text[index] == '.') { index++; Digits(); }
                if (!End && (text[index] == 'e' || text[index] == 'E')) { index++; if (!End && (text[index] == '+' || text[index] == '-')) index++; Digits(); }
                string token = text.Substring(start, index - start);
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
                if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue)) return decimalValue;
                throw new FormatException("JSON数値が不正です。");
            }
            private void Digits() { int start = index; while (!End && text[index] >= '0' && text[index] <= '9') index++; if (start == index) throw new FormatException("JSON数値の桁がありません。"); }
            private void Literal(string value) { if (index + value.Length > text.Length || text.Substring(index, value.Length) != value) throw new FormatException("JSONリテラルが不正です。"); index += value.Length; }
            private bool Take(char value) { if (!End && text[index] == value) { index++; return true; } return false; }
            private void Expect(char value) { White(); if (End || text[index] != value) throw new FormatException("JSONの記号が不正です。"); index++; }
        }
    }
}
