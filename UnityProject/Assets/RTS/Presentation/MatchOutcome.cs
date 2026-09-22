using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>
    /// What to tell the player when a match is over: who won from their side, and how long it took. Pure text from the
    /// captured result and tick, so it can be tested without Unity and never reads anything the frame does not hold.
    /// </summary>
    public readonly struct MatchOutcome
    {
        public string Headline { get; }
        public string Detail { get; }

        public MatchOutcome(string headline, string detail)
        {
            Headline = headline;
            Detail = detail;
        }

        /// <summary>Ticks per second of the scenario clock, which the match tick count is measured in.</summary>
        public const int TicksPerSecond = 20;

        /// <summary>Null while the match is still running.</summary>
        public static MatchOutcome? Describe(MatchResult result, uint viewFactionId, long tick)
        {
            if (!result.HasEnded) return null;
            string time = "Time " + Clock(tick) + " (t=" + tick + ")";
            if (result.IsFault) return new MatchOutcome("Match aborted", "The simulation stopped on an internal error. " + time);
            if (result.IsDraw) return new MatchOutcome("Draw", "Both cores fell at the same time. " + time);
            if (result.IsUndecided || result.WinnerFactionId == 0) return new MatchOutcome("No winner", time);
            return result.WinnerFactionId == viewFactionId
                ? new MatchOutcome("Victory", "The enemy core was destroyed. " + time)
                : new MatchOutcome("Defeat", "Your core was destroyed. " + time);
        }

        /// <summary>Minutes and seconds, e.g. 5428 ticks at 20 Hz is 4:31.</summary>
        public static string Clock(long tick)
        {
            long seconds = tick < 0 ? 0 : tick / TicksPerSecond;
            return (seconds / 60) + ":" + (seconds % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
