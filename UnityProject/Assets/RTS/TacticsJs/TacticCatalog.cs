using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rts.TacticsJs
{
    /// <summary>A folder found by the tactic catalog. Invalid entries stay visible with a reason.</summary>
    public sealed class TacticCatalogEntry
    {
        internal TacticCatalogEntry(string folderName, string path, TacticMetadata metadata, string error)
        {
            FolderName = folderName;
            Path = path;
            Metadata = metadata;
            Error = error;
        }

        public string FolderName { get; }
        public string Path { get; }
        public TacticMetadata Metadata { get; }
        public string Error { get; }
        public bool IsSelectable => Metadata != null && string.IsNullOrEmpty(Error);
        public string DisplayName => Metadata == null || string.IsNullOrWhiteSpace(Metadata.Name) ? FolderName : Metadata.Name;
        public string Reason => Error;
    }

    /// <summary>Lists tactic folders directly below a set of parent folders without Unity dependencies.</summary>
    public static class TacticCatalog
    {
        public static IReadOnlyList<TacticCatalogEntry> Scan(IEnumerable<string> parentFolders)
        {
            var entries = new List<TacticCatalogEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (parentFolders == null) return entries.AsReadOnly();

            foreach (string parent in parentFolders)
            {
                if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) continue;
                string[] directories;
                try { directories = Directory.GetDirectories(parent); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    entries.Add(new TacticCatalogEntry(parent, parent, null, "親フォルダを読めません: " + e.Message));
                    continue;
                }

                foreach (string path in directories)
                {
                    string fullPath;
                    try { fullPath = System.IO.Path.GetFullPath(path); }
                    catch (Exception e) when (e is ArgumentException || e is IOException)
                    {
                        entries.Add(new TacticCatalogEntry(System.IO.Path.GetFileName(path), path, null, "パスを読めません: " + e.Message));
                        continue;
                    }
                    if (!seen.Add(fullPath)) continue;
                    string folderName = System.IO.Path.GetFileName(fullPath);
                    string metadataPath = System.IO.Path.Combine(fullPath, "tactic.json");
                    if (!File.Exists(metadataPath))
                    {
                        entries.Add(new TacticCatalogEntry(folderName, fullPath, null, "tactic.jsonがありません。"));
                        continue;
                    }

                    try
                    {
                        var loaded = TacticFolder.Load(fullPath);
                        entries.Add(loaded.IsSuccess
                            ? new TacticCatalogEntry(folderName, fullPath, loaded.Metadata, null)
                            : new TacticCatalogEntry(folderName, fullPath, loaded.Metadata, loaded.Error));
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException
                        || e is ArgumentException || e is OverflowException || e is InvalidOperationException)
                    {
                        entries.Add(new TacticCatalogEntry(folderName, fullPath, null, "読み込みに失敗しました: " + e.Message));
                    }
                }
            }

            return entries
                .OrderBy(x => x.FolderName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FolderName, StringComparer.Ordinal)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<TacticCatalogEntry> Scan(params string[] parentFolders)
        {
            return Scan((IEnumerable<string>)parentFolders);
        }
    }
}
