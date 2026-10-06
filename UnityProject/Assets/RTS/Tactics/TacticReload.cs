using System;
using System.Collections.Generic;
using System.IO;
using Rts.Contracts;

namespace Rts.Tactics
{
    /// <summary>Last-write stamps for the files that make up a tactic folder.</summary>
    public sealed class TacticFileStamp : IEquatable<TacticFileStamp>
    {
        private readonly FileStamp tacticJson;
        private readonly FileStamp mainJs;
        private readonly FileStamp mainPy;

        private TacticFileStamp(FileStamp tacticJson, FileStamp mainJs, FileStamp mainPy)
        {
            this.tacticJson = tacticJson;
            this.mainJs = mainJs;
            this.mainPy = mainPy;
        }

        public static TacticFileStamp Capture(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return new TacticFileStamp(default(FileStamp), default(FileStamp), default(FileStamp));
            return new TacticFileStamp(
                FileStamp.Capture(Path.Combine(folder, "tactic.json")),
                FileStamp.Capture(Path.Combine(folder, "main.js")),
                FileStamp.Capture(Path.Combine(folder, "main.py")));
        }

        public bool Equals(TacticFileStamp other)
        {
            return other != null && tacticJson.Equals(other.tacticJson) && mainJs.Equals(other.mainJs) && mainPy.Equals(other.mainPy);
        }

        public override bool Equals(object obj) => Equals(obj as TacticFileStamp);
        public override int GetHashCode() => tacticJson.GetHashCode() ^ (mainJs.GetHashCode() * 397) ^ (mainPy.GetHashCode() * 7919);
        public static bool operator ==(TacticFileStamp left, TacticFileStamp right) => Equals(left, right);
        public static bool operator !=(TacticFileStamp left, TacticFileStamp right) => !Equals(left, right);

        private struct FileStamp : IEquatable<FileStamp>
        {
            private readonly bool exists;
            private readonly long lastWriteUtcTicks;

            private FileStamp(bool exists, long lastWriteUtcTicks)
            {
                this.exists = exists;
                this.lastWriteUtcTicks = lastWriteUtcTicks;
            }

            public static FileStamp Capture(string path)
            {
                try
                {
                    if (!File.Exists(path)) return default(FileStamp);
                    return new FileStamp(true, File.GetLastWriteTimeUtc(path).Ticks);
                }
                catch (IOException) { return default(FileStamp); }
                catch (UnauthorizedAccessException) { return default(FileStamp); }
            }

            public bool Equals(FileStamp other) => exists == other.exists && lastWriteUtcTicks == other.lastWriteUtcTicks;
            public override bool Equals(object obj) => obj is FileStamp && Equals((FileStamp)obj);
            public override int GetHashCode() => exists ? lastWriteUtcTicks.GetHashCode() : 0;
        }
    }

    /// <summary>Wall-clock gate used by the Unity Update loop; it has no simulation-time meaning.</summary>
    public sealed class TacticReloadPoller
    {
        private readonly TimeSpan interval;
        private DateTime nextCheckUtc;

        public TacticReloadPoller(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            this.interval = interval;
            nextCheckUtc = DateTime.MinValue;
        }

        public bool ShouldCheck(DateTime utcNow)
        {
            if (utcNow < nextCheckUtc) return false;
            nextCheckUtc = utcNow + interval;
            return true;
        }
    }

    /// <summary>Finds values that can safely survive a tactic definition reload.</summary>
    public static class TacticParameterCarryover
    {
        public static IReadOnlyDictionary<string, object> CompatibleValues(
            IReadOnlyList<TacticParamDefinition> oldDefinitions,
            IReadOnlyDictionary<string, object> oldValues,
            IReadOnlyList<TacticParamDefinition> newDefinitions)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (oldDefinitions == null || oldValues == null || newDefinitions == null) return result;
            var oldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in oldDefinitions)
                if (definition != null) oldNames.Add(definition.Name);

            foreach (var definition in newDefinitions)
            {
                if (definition == null || !oldNames.Contains(definition.Name)) continue;
                if (!oldValues.TryGetValue(definition.Name, out var value)) continue;
                if (definition.TryNormalize(value, out var normalized, out _)) result[definition.Name] = normalized;
            }
            return result;
        }
    }

    /// <summary>Unity-independent assembly of a replacement host used by live and headless callers.</summary>
    public static class TacticReloadBuilder
    {
        public static TacticMatchSide CreateReplacement(
            uint factionId,
            string selection,
            Func<string, ITacticRuntime> loadRuntime,
            IFrameSource frames,
            ICommandPort commandPort,
            IEconomyPort economyPort,
            TacticHost previous,
            string setupJson = "{}",
            ITacticGlobalPolicyPort globalPolicyPort = null,
            Func<ScopeKey, IReadOnlyList<PolicyVersion>> versions = null)
        {
            var replacement = TacticMatchSetup.Create(factionId, selection, loadRuntime, frames, commandPort, economyPort,
                globalPolicyPort, versions);
            try
            {
                var values = previous == null ? new Dictionary<string, object>(StringComparer.Ordinal)
                    : TacticParameterCarryover.CompatibleValues(previous.Parameters, previous.ParamValues, replacement.Host.Parameters);
                replacement.Host.ApplyInitialParameterValues(values);
                replacement.Host.Start(setupJson ?? "{}");
                if (replacement.Host.Failures.Count != 0) throw new InvalidOperationException(replacement.Host.LastFailure.Reason);
                return replacement;
            }
            catch
            {
                replacement.Host?.Dispose();
                throw;
            }
        }
    }
}
