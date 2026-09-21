namespace Rts.Presentation
{
    /// <summary>
    /// What the command panel may do with the optional outside AI. The panel only reads a state and flips a switch; it
    /// knows nothing about the service behind it.
    /// </summary>
    public interface IExternalAiControl
    {
        /// <summary>False when no key is configured on this machine, so the switch would have nothing to use.</summary>
        bool KeyAvailable { get; }

        /// <summary>
        /// Off by default. Turning it on sends what this faction can see - unit positions and counts, outposts and cores -
        /// to an outside service, and restarts the match so that both sides start from the same tick.
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>One line for the display: whether it is asking, paused after failures, or answering with nothing.</summary>
        string Status { get; }
    }
}
