using System;
using System.Collections.Generic;

namespace Rts.Presentation
{
    /// <summary>Selection and read-only diagnostics for one side's optional tactic.</summary>
    public interface ITacticControl
    {
        string[] Choices { get; }
        string Current { get; set; }
        bool Active { get; }
        string Name { get; }
        long LastTick { get; }
        int SentCommands { get; }
        int RejectedCommands { get; }
        int FailureCount { get; }
        string LastFailureReason { get; }
        bool Disabled { get; }
        IReadOnlyList<string> ConsoleLines { get; }
        IReadOnlyList<TacticParamView> Parameters { get; }
        IReadOnlyDictionary<string, object> ParamValues { get; }
        bool SetParam(string name, object value);
    }

    /// <summary>
    /// One tactic knob as the panel draws it. Presentation may reference Contracts only, so UnityHost copies the
    /// tactic's own definition (Rts.Tactics.TacticParamDefinition) into this.
    /// </summary>
    public sealed class TacticParamView
    {
        public TacticParamView(string name, string label, string type, object defaultValue,
            decimal? min, decimal? max, decimal? step, IReadOnlyList<string> choices)
        {
            Name = name; Label = label; Type = type; DefaultValue = defaultValue;
            Min = min; Max = max; Step = step; Choices = choices ?? Array.Empty<string>();
        }

        public string Name { get; }
        public string Label { get; }
        public string Type { get; }
        public object DefaultValue { get; }
        public decimal? Min { get; }
        public decimal? Max { get; }
        public decimal? Step { get; }
        public IReadOnlyList<string> Choices { get; }
    }
}
