using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Rts.Tactics
{
    /// <summary>Prepares a validated tactic folder for a Workshop upload without touching Steam or Unity.</summary>
    public static class WorkshopTacticPreparer
    {
        public const long DefaultMaxTotalBytes = 50L * 1024L * 1024L;
        public const string PublicationMapFileName = "workshop-tactics.json";

        private static readonly HashSet<string> MemoryDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "memory", "match-memory", "match_memory", "saved-memory", "persistent-memory"
        };

        public static WorkshopPreparationResult Prepare(string sourceFolder, string temporaryRoot, string storageRoot,
            Func<string, string> validator = null, long maxTotalBytes = DefaultMaxTotalBytes)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceFolder)) return Reject("戦術フォルダのパスが空です。");
                if (!Directory.Exists(sourceFolder)) return Reject("戦術フォルダがありません。");
                if (maxTotalBytes <= 0) return Reject("公開用の容量上限が不正です。");

                string source = Path.GetFullPath(sourceFolder);
                string error = validator == null ? ValidateBasicFolder(source) : validator(source);
                if (!string.IsNullOrEmpty(error)) return Reject(error);
                WorkshopTacticInfo info = ReadInfo(source);

                string root = Path.GetFullPath(temporaryRoot ?? "");
                string storage = Path.GetFullPath(storageRoot ?? "");
                if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(storage)) return Reject("公開用の保存先が空です。");
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(storage);
                string leaf = SanitizeLeaf(Path.GetFileName(source));
                string destination = Path.Combine(root, leaf);
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
                Directory.CreateDirectory(destination);

                long total = 0;
                CopyTree(source, destination, ref total, maxTotalBytes);
                return new WorkshopPreparationResult
                {
                    IsSuccess = true,
                    StagedFolder = destination,
                    Info = info,
                    TotalBytes = total,
                    PublicationMapPath = Path.Combine(storage, PublicationMapFileName)
                };
            }
            catch (WorkshopPreparationException e) { return Reject(e.Message); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is PathTooLongException)
            { return Reject("公開用フォルダの準備に失敗しました: " + e.Message); }
        }

        public static WorkshopPublicationMap LoadPublicationMap(string storageRoot)
        {
            string path = Path.Combine(Path.GetFullPath(storageRoot ?? ""), PublicationMapFileName);
            if (!File.Exists(path)) return new WorkshopPublicationMap(path);
            try
            {
                var root = TacticJson.Object(TacticJson.Parse(File.ReadAllText(path, new UTF8Encoding(false, true))), "Workshop対応表");
                var map = new WorkshopPublicationMap(path);
                var values = TacticJson.Array(root.TryGetValue("items", out var raw) ? raw : new List<object>(), "items");
                foreach (var item in values)
                {
                    var obj = TacticJson.Object(item, "itemsの要素");
                    string folder = TacticJson.String(obj, "folder", true);
                    string idText = TacticJson.String(obj, "publishedFileId", true);
                    if (ulong.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0)
                        map.Set(folder, id);
                }
                return map;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is DecoderFallbackException || e is FormatException)
            { throw new InvalidDataException("Workshop対応表を読めません: " + e.Message, e); }
        }

        private static string ValidateBasicFolder(string source)
        {
            string metadata = Path.Combine(source, "tactic.json");
            if (!File.Exists(metadata)) return "tactic.jsonがありません。";
            try
            {
                var info = ReadInfo(source);
                string entry = Path.Combine(source, info.Language.Equals("python", StringComparison.OrdinalIgnoreCase) ? "main.py" : "main.js");
                if (!File.Exists(entry)) return "入口ファイルがありません。";
                return null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is DecoderFallbackException || e is FormatException)
            { return "tactic.jsonを読めません: " + e.Message; }
        }

        private static WorkshopTacticInfo ReadInfo(string source)
        {
            string json = File.ReadAllText(Path.Combine(source, "tactic.json"), new UTF8Encoding(false, true));
            var root = TacticJson.Object(TacticJson.Parse(json), "tactic.json");
            string name = TacticJson.String(root, "name", true);
            string language = TacticJson.String(root, "language", true);
            if (!language.Equals("js", StringComparison.OrdinalIgnoreCase) && !language.Equals("python", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("languageはjsまたはpythonでなければなりません。");
            string style = TacticJson.String(root, "style") ?? "";
            string description = TacticJson.String(root, "description") ?? "";
            string readme = Path.Combine(source, "README.md");
            if (File.Exists(readme))
            {
                string first = File.ReadLines(readme, new UTF8Encoding(false, true)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                if (!string.IsNullOrWhiteSpace(first)) description = first.Trim().TrimStart('#').Trim();
            }
            if (description.Length > 4000) description = description.Substring(0, 4000);
            var tags = new List<string> { language.ToLowerInvariant() };
            if (!string.IsNullOrWhiteSpace(style)) tags.Add(style);
            return new WorkshopTacticInfo(name, description, tags, language);
        }

        private static void CopyTree(string source, string destination, ref long total, long limit)
        {
            foreach (string path in Directory.GetFiles(source))
            {
                var file = new FileInfo(path);
                if (IsHidden(file) || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                total = checked(total + file.Length);
                if (total > limit) throw new WorkshopPreparationException("公開用フォルダが容量上限（" + limit.ToString("N0", CultureInfo.InvariantCulture) + " bytes）を超えています。");
                File.Copy(path, Path.Combine(destination, file.Name), true);
            }
            foreach (string path in Directory.GetDirectories(source))
            {
                var directory = new DirectoryInfo(path);
                if (IsHidden(directory) || (directory.Attributes & FileAttributes.ReparsePoint) != 0 || MemoryDirectories.Contains(directory.Name)) continue;
                string child = Path.Combine(destination, directory.Name);
                Directory.CreateDirectory(child);
                CopyTree(path, child, ref total, limit);
            }
        }

        private static bool IsHidden(FileSystemInfo item)
        { return item.Name.StartsWith(".", StringComparison.Ordinal) || (item.Attributes & FileAttributes.Hidden) != 0; }

        private static string SanitizeLeaf(string name)
        { return string.IsNullOrWhiteSpace(name) ? "tactic" : name.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_'); }

        private static WorkshopPreparationResult Reject(string reason) { return new WorkshopPreparationResult { Error = reason }; }

        private sealed class WorkshopPreparationException : Exception
        { public WorkshopPreparationException(string message) : base(message) { } }
    }

    public sealed class WorkshopTacticInfo
    {
        public WorkshopTacticInfo(string title, string description, IReadOnlyList<string> tags, string language)
        { Title = title; Description = description ?? ""; Tags = tags ?? Array.Empty<string>(); Language = language ?? ""; }
        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<string> Tags { get; }
        public string Language { get; }
    }

    public sealed class WorkshopPreparationResult
    {
        public bool IsSuccess { get; internal set; }
        public string Error { get; internal set; }
        public string StagedFolder { get; internal set; }
        public string PublicationMapPath { get; internal set; }
        public long TotalBytes { get; internal set; }
        public WorkshopTacticInfo Info { get; internal set; }
    }

    public sealed class WorkshopPublicationMap
    {
        private readonly string path;
        private readonly Dictionary<string, ulong> items = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        internal WorkshopPublicationMap(string path) { this.path = path; }
        public bool TryGet(string folder, out ulong id) { return items.TryGetValue(Path.GetFullPath(folder), out id); }
        public void Set(string folder, ulong id) { items[Path.GetFullPath(folder)] = id; }
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var b = new StringBuilder("{\n  \"version\": 1,\n  \"items\": [\n");
            bool first = true;
            foreach (var item in items.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!first) b.Append(",\n");
                first = false;
                b.Append("    {\"folder\":").Append(TacticJson.Quote(item.Key)).Append(",\"publishedFileId\":").Append(TacticJson.Quote(item.Value.ToString(CultureInfo.InvariantCulture))).Append('}');
            }
            b.Append("\n  ]\n}\n");
            File.WriteAllText(path, b.ToString(), new UTF8Encoding(false));
        }
    }
}
