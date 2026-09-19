namespace Rts.Presentation
{
    /// <summary>Verification-UI hook: lets the panel switch the simulated AI reply delay (0/60/200/400 ticks).</summary>
    public interface ICommandDelayControl
    {
        int DelayTicks { get; set; }
    }
}
