using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Rts.Tactics;

namespace Rts.TacticsJs
{
    public sealed class TacticMetadata
    {
        public string Name { get; internal set; }
        public string Author { get; internal set; }
        public string Version { get; internal set; }
        public string Language { get; internal set; }
        public string Entry { get; internal set; }
        public int ApiVersion { get; internal set; }
        public string Description { get; internal set; }
        public IReadOnlyList<TacticParamDefinition> Params { get; internal set; } = Array.Empty<TacticParamDefinition>();
    }

    public sealed class TacticFolderLoadResult
    {
        public bool IsSuccess => Runtime != null;
        public ITacticRuntime Runtime { get; internal set; }
        public TacticMetadata Metadata { get; internal set; }
        public string Error { get; internal set; }
        public string Reason => Error;
    }

    public static class TacticFolder
    {
        public const int ApiVersion = 1;

        public static TacticFolderLoadResult Load(string path, string runtimesPath = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return Reject("戦術フォルダのパスが空です。");
                if (!Directory.Exists(path)) return Reject("戦術フォルダがありません: " + path);
                string metadataPath = Path.Combine(path, "tactic.json");
                if (!File.Exists(metadataPath)) return Reject("tactic.jsonがありません。");
                var metadata = ReadMetadata(File.ReadAllText(metadataPath, new UTF8Encoding(false, true)));
                if (metadata.ApiVersion != ApiVersion) return Reject("apiVersion=" + metadata.ApiVersion + " は未対応です（対応は1）。");
                bool python = string.Equals(metadata.Language, "python", StringComparison.OrdinalIgnoreCase);
                if (!python && !string.Equals(metadata.Language, "js", StringComparison.OrdinalIgnoreCase)) return Reject("languageはjsまたはpythonでなければなりません。");
                string entry = python ? "main.py" : "main.js";
                if (!string.Equals(metadata.Entry, entry, StringComparison.Ordinal)) return Reject("entryは" + entry + "でなければなりません。");
                string entryPath = Path.Combine(path, metadata.Entry);
                if (!File.Exists(entryPath)) return Reject("入口ファイルがありません: " + metadata.Entry);
                var info = new FileInfo(entryPath);
                if (info.Length > JsTacticRuntime.SourceLimitBytes) return Reject("入口ファイルが1MiBを超えています。");
                string source = File.ReadAllText(entryPath, new UTF8Encoding(false, true));
                if (python)
                {
                    string error = PyodideTacticRuntime.AvailabilityError(runtimesPath);
                    if (error != null) return new TacticFolderLoadResult { Metadata = metadata, Error = error };
                }
                ITacticRuntime runtime = python ? (ITacticRuntime)new PyodideTacticRuntime(path, runtimesPath, metadata.Name, metadata.Params) : new JsTacticRuntime(source, metadata.Name, metadata.Params);
                return new TacticFolderLoadResult { Runtime = runtime, Metadata = metadata };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is DecoderFallbackException || e is FormatException || e is ArgumentException || e is OverflowException)
            {
                return Reject("読み込みに失敗しました: " + e.Message);
            }
        }

        public static bool TryLoad(string path, out ITacticRuntime runtime, out string reason)
        {
            var result = Load(path); runtime = result.Runtime; reason = result.Error; return result.IsSuccess;
        }

        private static TacticFolderLoadResult Reject(string reason) => new TacticFolderLoadResult { Error = reason };

        private static TacticMetadata ReadMetadata(string json)
        {
            var root = TacticJsonForRuntime.Parse(json) as System.Collections.Generic.Dictionary<string, object>;
            if (root == null) throw new FormatException("tactic.jsonはオブジェクトでなければなりません。");
            var metadata = new TacticMetadata
            {
                Name = RequiredString(root, "name"),
                Author = RequiredString(root, "author"),
                Version = RequiredString(root, "version"),
                Language = RequiredString(root, "language"),
                Entry = RequiredString(root, "entry"),
                ApiVersion = checked((int)RequiredInteger(root, "apiVersion")),
                Description = RequiredString(root, "description"),
                Params = ReadParams(root)
            };
            return metadata;
        }

        private static IReadOnlyList<TacticParamDefinition> ReadParams(Dictionary<string, object> root)
        {
            if (!root.TryGetValue("params", out var raw) || raw == null) return Array.Empty<TacticParamDefinition>();
            var list = TacticJsonForRuntime.Array(raw, "params");
            var result = new List<TacticParamDefinition>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                var obj = TacticJsonForRuntime.Object(item, "paramsの要素");
                string name = RequiredString(obj, "name");
                if (!names.Add(name)) throw new FormatException("paramsのnameが重複しています: " + name);
                string label = RequiredString(obj, "label");
                string type = RequiredString(obj, "type");
                if (!obj.TryGetValue("default", out var defaultValue)) throw new FormatException("paramsのdefaultは必須です: " + name);
                decimal? min = OptionalNumber(obj, "min", name);
                decimal? max = OptionalNumber(obj, "max", name);
                decimal? step = OptionalNumber(obj, "step", name);
                IReadOnlyList<string> choices = null;
                if (obj.TryGetValue("choices", out var choicesRaw))
                {
                    var choiceList = TacticJsonForRuntime.Array(choicesRaw, "paramsのchoices");
                    var strings = new List<string>();
                    foreach (var choice in choiceList)
                        if (!(choice is string text)) throw new FormatException("paramsのchoicesは文字列配列です: " + name);
                        else strings.Add(text);
                    choices = strings;
                }
                result.Add(new TacticParamDefinition(name, label, type, defaultValue, min, max, step, choices));
            }
            return result.ToArray();
        }

        private static decimal? OptionalNumber(Dictionary<string, object> obj, string key, string paramName)
        {
            if (!obj.TryGetValue(key, out var raw) || raw == null) return null;
            if (raw is long l) return l;
            if (raw is decimal m) return m;
            throw new FormatException("paramsの" + key + "は数値です: " + paramName);
        }

        private static string RequiredString(System.Collections.Generic.Dictionary<string, object> root, string key)
        {
            if (!root.TryGetValue(key, out var value) || !(value is string text) || string.IsNullOrWhiteSpace(text)) throw new FormatException(key + "は必須の文字列です。");
            return text;
        }

        private static long RequiredInteger(System.Collections.Generic.Dictionary<string, object> root, string key)
        {
            if (!root.TryGetValue(key, out var value) || !(value is long integer)) throw new FormatException(key + "は必須の整数です。");
            return integer;
        }
    }
}
