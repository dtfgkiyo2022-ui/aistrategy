using System;
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
    }

    public sealed class TacticFolderLoadResult
    {
        public bool IsSuccess => Runtime != null;
        public JsTacticRuntime Runtime { get; internal set; }
        public TacticMetadata Metadata { get; internal set; }
        public string Error { get; internal set; }
        public string Reason => Error;
    }

    public static class TacticFolder
    {
        public const int ApiVersion = 1;

        public static TacticFolderLoadResult Load(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return Reject("戦術フォルダのパスが空です。");
                if (!Directory.Exists(path)) return Reject("戦術フォルダがありません: " + path);
                string metadataPath = Path.Combine(path, "tactic.json");
                if (!File.Exists(metadataPath)) return Reject("tactic.jsonがありません。");
                var metadata = ReadMetadata(File.ReadAllText(metadataPath, new UTF8Encoding(false, true)));
                if (metadata.ApiVersion != ApiVersion) return Reject("apiVersion=" + metadata.ApiVersion + " は未対応です（対応は1）。");
                if (!string.Equals(metadata.Language, "js", StringComparison.OrdinalIgnoreCase)) return Reject("languageはjsでなければなりません。");
                if (!string.Equals(metadata.Entry, "main.js", StringComparison.Ordinal)) return Reject("entryはmain.jsでなければなりません。");
                string entryPath = Path.Combine(path, metadata.Entry);
                if (!File.Exists(entryPath)) return Reject("入口ファイルがありません: " + metadata.Entry);
                var info = new FileInfo(entryPath);
                if (info.Length > JsTacticRuntime.SourceLimitBytes) return Reject("入口ファイルが1MiBを超えています。");
                string source = File.ReadAllText(entryPath, new UTF8Encoding(false, true));
                var runtime = new JsTacticRuntime(source, metadata.Name);
                return new TacticFolderLoadResult { Runtime = runtime, Metadata = metadata };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is DecoderFallbackException || e is FormatException || e is ArgumentException || e is OverflowException)
            {
                return Reject("読み込みに失敗しました: " + e.Message);
            }
        }

        public static bool TryLoad(string path, out JsTacticRuntime runtime, out string reason)
        {
            var result = Load(path); runtime = result.Runtime; reason = result.Error; return result.IsSuccess;
        }

        private static TacticFolderLoadResult Reject(string reason) => new TacticFolderLoadResult { Error = reason };

        private static TacticMetadata ReadMetadata(string json)
        {
            var root = TacticJsonForRuntime.Parse(json) as System.Collections.Generic.Dictionary<string, object>;
            if (root == null) throw new FormatException("tactic.jsonはオブジェクトでなければなりません。");
            return new TacticMetadata
            {
                Name = RequiredString(root, "name"),
                Author = RequiredString(root, "author"),
                Version = RequiredString(root, "version"),
                Language = RequiredString(root, "language"),
                Entry = RequiredString(root, "entry"),
                ApiVersion = checked((int)RequiredInteger(root, "apiVersion")),
                Description = RequiredString(root, "description")
            };
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
