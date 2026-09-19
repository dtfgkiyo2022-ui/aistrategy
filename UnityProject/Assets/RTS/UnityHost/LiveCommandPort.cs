using System;
using System.Collections.Generic;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.UnityHost
{
    /// <summary>
    /// Command boundary between the UI and the real CommandGateway. With delay 0 the order is a direct
    /// standard command; with 0/60/200/400 it goes through the interpretation stub (chapter 11).
    /// </summary>
    public sealed class LiveCommandPort : ICommandPort, ICommandDelayControl
    {
        private readonly CommandGateway gateway;
        private readonly int providerDelayTicks;
        private UserPolicyIntent? interpreting;
        private int requestedDelayTicks;

        public LiveCommandPort(CommandGateway gateway, int providerDelayTicks)
        {
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.providerDelayTicks = providerDelayTicks;
            requestedDelayTicks = providerDelayTicks;
        }

        /// <summary>A provider's delay is fixed when the match starts, so a change asks the host for a restart.</summary>
        public int DelayTicks
        {
            get { return requestedDelayTicks; }
            set { requestedDelayTicks = value; }
        }

        public bool RestartRequested { get { return requestedDelayTicks != providerDelayTicks; } }

        public ulong Submit(UserPolicyIntent intent)
        {
            if (providerDelayTicks == 0) return gateway.Submit(intent);
            // DelayedPolicyProvider calls the factory inside SubmitInterpreted, on this thread, before returning.
            interpreting = intent;
            try { return gateway.SubmitInterpreted(intent); }
            finally { interpreting = null; }
        }

        public void Cancel(ulong requestId) { gateway.Cancel(requestId); }

        public ulong Propose(uint faction, ulong sequence, IReadOnlyList<PolicyOrder> orders, long applyTick)
        {
            return gateway.Propose(faction, sequence, orders, applyTick);
        }

        /// <summary>Interpretation stub: returns the operator's own standard command unchanged, after the delay.</summary>
        public IReadOnlyList<PolicyOrder> Interpret(PolicyRequest request)
        {
            if (interpreting == null) throw new InvalidOperationException("No standard command is being interpreted.");
            var intent = interpreting.Value;
            if (!intent.Target.Equals(request.Scope)) throw new InvalidOperationException("Interpretation target mismatch.");
            return new[]
            {
                new PolicyOrder(0, 0, CommandSource.Human, intent.Target, intent.Kind, intent.Goal, intent.Priority,
                    intent.AllowedLoss, intent.End, intent.ReservePermille, 0, Array.Empty<PolicyVersion>(),
                    request.StartedTick, intent.Expiration)
            };
        }
    }
}
