using System;
using Rts.Contracts;

namespace Rts.Application
{
    /// <summary>
    /// A proposal discarded at the local command boundary while a human interpretation is open.
    /// It is diagnostic/UI state only and is deliberately not part of the replay input log.
    /// </summary>
    public sealed class InterpretationRejection
    {
        public ulong ReservationId { get; }
        public uint FactionId { get; }
        public ScopeKey ProposalScope { get; }
        public long Tick { get; }
        public string Reason { get; }

        internal InterpretationRejection(ulong reservationId, uint factionId, ScopeKey proposalScope, long tick, string reason)
        {
            ReservationId = reservationId;
            FactionId = factionId;
            ProposalScope = proposalScope;
            Tick = tick;
            Reason = reason ?? "人の解釈中";
        }
    }
}
