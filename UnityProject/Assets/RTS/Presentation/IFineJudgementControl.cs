namespace Rts.Presentation
{
    /// <summary>
    /// The match-setup boundary for the optional fine judgement provider. Presentation only changes the selected mode;
    /// UnityHost owns the provider, the key check and the restart request.
    /// </summary>
    public interface IFineJudgementControl
    {
        /// <summary>Stable ids in display order: off, jev and local-llm.</summary>
        string[] Options { get; }

        /// <summary>The selected option. Changing it applies from the next match.</summary>
        string Mode { get; set; }

        /// <summary>False disables the Jev button and leaves its key notice visible.</summary>
        bool JevKeyAvailable { get; }

        /// <summary>One line shown under the choices.</summary>
        string Description { get; }
    }
}
