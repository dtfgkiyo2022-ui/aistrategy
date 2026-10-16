using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Pure ordering for the one visible movement arrow allowed per army.</summary>
    public static class BattlefieldArrowRules
    {
        public static bool IsNewer(CommandView candidate, CommandView current)
        {
            if (candidate.AcceptedTick != current.AcceptedTick) return candidate.AcceptedTick > current.AcceptedTick;
            if (candidate.ApplyTick != current.ApplyTick) return candidate.ApplyTick > current.ApplyTick;
            return candidate.CommandId > current.CommandId;
        }
    }
}
