using System.Runtime.CompilerServices;

// The rulebook and the match pack (Rts.TacticsTools) write JSON with the same quoting as the tactic view.
[assembly: InternalsVisibleTo("Rts.TacticsTools")]

namespace Rts.Tactics
{
    internal static class TacticsAssemblyMarker { }
}
