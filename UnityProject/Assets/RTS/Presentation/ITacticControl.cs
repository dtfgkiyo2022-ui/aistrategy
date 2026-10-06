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
    }
}
