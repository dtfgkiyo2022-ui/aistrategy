using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Tactics
{
    /// <summary>One human-to-tactic signal declared in tactic.json.</summary>
    public sealed class TacticSignalDefinition
    {
        public const int MaxCount = 8;

        public TacticSignalDefinition(string name, string label, bool needsPoint)
        {
            if (string.IsNullOrEmpty(name) || !name.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9'))
                throw new FormatException("signalsのnameは英数字でなければなりません: " + name);
            if (string.IsNullOrWhiteSpace(label)) throw new FormatException("signalsのlabelは必須です: " + name);
            Name = name;
            Label = label;
            NeedsPoint = needsPoint;
        }

        public string Name { get; }
        public string Label { get; }
        public bool NeedsPoint { get; }

        public static IReadOnlyList<TacticSignalDefinition> Validate(IEnumerable<TacticSignalDefinition> definitions)
        {
            var result = (definitions ?? Array.Empty<TacticSignalDefinition>()).Where(x => x != null).ToArray();
            if (result.Length > MaxCount) throw new FormatException("signalsは" + MaxCount + "個以内でなければなりません。");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in result)
                if (!names.Add(definition.Name)) throw new FormatException("signalsのnameが重複しています: " + definition.Name);
            return result;
        }
    }

    /// <summary>A signal accepted from a human or the staff officer and delivered once to the tactic.</summary>
    public sealed class TacticSignal
    {
        public long Tick { get; internal set; }
        public string Name { get; internal set; }
        public SimPoint? Point { get; internal set; }
    }

    public interface ITacticSignalRuntime
    {
        IReadOnlyList<TacticSignalDefinition> Signals { get; }
    }
}
