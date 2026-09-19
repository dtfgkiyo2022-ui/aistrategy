namespace Rts.Presentation
{
    /// <summary>Verification-UI hook for the host's tick clock and the faction whose frame is shown.</summary>
    public interface IMatchClock
    {
        bool Paused { get; set; }
        /// <summary>1, 2 or 4.</summary>
        int SpeedMultiplier { get; set; }
        uint ViewFactionId { get; set; }
        long Tick { get; }
        void StepOneTick();
    }
}
