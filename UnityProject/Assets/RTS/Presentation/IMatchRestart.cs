namespace Rts.Presentation
{
    /// <summary>
    /// What the result overlay may ask of the host once a match is over: play again with the same settings.
    /// The overlay knows nothing about how a match is built.
    /// </summary>
    public interface IMatchRestart
    {
        /// <summary>Starts a fresh match from tick 0 with the settings of the one just played (delay, outside AI).</summary>
        void RestartMatch();
    }
}
