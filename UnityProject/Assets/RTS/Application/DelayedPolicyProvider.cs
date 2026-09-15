using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;
using Rts.Replay;

namespace Rts.Application
{
    public sealed class AiTimingProfile
    {
        public static AiTimingProfile Default { get; } = new AiTimingProfile("default", DelayedPolicyProvider.DefaultDeadlineTicks);
        public static AiTimingProfile Long { get; } = new AiTimingProfile("long", DelayedPolicyProvider.LongDeadlineTicks);
        public string Name { get; }
        public int DeadlineTicks { get; }
        public int MaxObservationAgeTicks => DeadlineTicks;
        private AiTimingProfile(string name, int ticks) { Name = name; DeadlineTicks = ticks; }
        public static AiTimingProfile Parse(string name) => name == "default" ? Default : name == "long" ? Long :
            throw new ArgumentException("AI profile must be default or long.", nameof(name));
    }
    /// <summary>
    /// Deterministic, tick-driven policy provider used by the host and tests.  It deliberately
    /// has no wall-clock, task, random, or simulation reference: a reply is made at Request time
    /// from the request snapshot and is merely held until its ready tick.
    /// </summary>
    public sealed class DelayedPolicyProvider : IPolicyProvider
    {
        public const int DefaultDeadlineTicks = 240;
        public const int DefaultMaxObservationAgeTicks = 240;
        public const int TransmissionTicks = 40;
        public const int LongDeadlineTicks = 500;
        public const int LongMaxObservationAgeTicks = 500;
        private sealed class Pending
        {
            internal long ReadyTick;
            internal PolicyReply Reply;
        }
        private readonly int delayTicks;
        private readonly Func<PolicyRequest, IReadOnlyList<PolicyOrder>> decide;
        private readonly List<Pending> pending = new List<Pending>();

        public DelayedPolicyProvider(int delayTicks, Func<PolicyRequest, IReadOnlyList<PolicyOrder>> decide, AiTimingProfile profile = null)
        {
            if (delayTicks != 0 && delayTicks != 60 && delayTicks != 200 && delayTicks != 400)
                throw new ArgumentOutOfRangeException(nameof(delayTicks), "Delay must be 0, 60, 200, or 400 ticks.");
            this.delayTicks = delayTicks;
            Profile = profile ?? AiTimingProfile.Default;
            this.decide = decide ?? throw new ArgumentNullException(nameof(decide));
        }
        public AiTimingProfile Profile { get; }
        public int DelayTicks => delayTicks;
        public void Request(PolicyRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            // Reconstruct the request so the callback cannot retain caller-owned list wrappers.
            var snapshot = new PolicyRequest(request.RequestId, request.FactionId, request.Scope, request.StartedTick,
                Copy(request.Observation), request.Versions.ToArray(), request.DeadlineTick, request.Kind, request.Goal);
            var orders = decide(snapshot);
            if (orders == null) throw new InvalidOperationException("Policy factory returned null.");
            pending.Add(new Pending { ReadyTick = checked(request.StartedTick + delayTicks),
                Reply = new PolicyReply(request.RequestId, checked(request.StartedTick + delayTicks), orders.Select(o => new PolicyOrder(o.CommandId, o.BatchId, o.Source, o.Target, o.Kind, o.Goal,
                    o.Priority, o.AllowedLoss, o.End, o.ReservePermille, o.TargetRevision, o.Parents, request.StartedTick,
                    new Expiration(o.Expiration.ValidUntilTick, Profile.MaxObservationAgeTicks,
                        o.Expiration.Flags | ExpireFlags.ObservationTooOld))).ToArray(), ReasonCode.None) });
        }
        public IReadOnlyList<PolicyReply> Poll(long tick)
        {
            var ready = pending.Where(p => p.ReadyTick <= tick).OrderBy(p => p.ReadyTick).ThenBy(p => p.Reply.RequestId)
                .Select(p => new PolicyReply(p.Reply.RequestId, tick, p.Reply.Orders, p.Reply.Reason)).ToArray();
            pending.RemoveAll(p => p.ReadyTick <= tick);
            return ready;
        }
        public byte[] CaptureDiagnostic() => ReplayBinary.Pack(w =>
        {
            w.Write(delayTicks); ReplayBinary.Text(w, Profile.Name); w.Write(pending.Count);
            foreach (var p in pending.OrderBy(p => p.ReadyTick).ThenBy(p => p.Reply.RequestId))
            {
                w.Write(p.Reply.RequestId); w.Write(p.ReadyTick);
                var bytes = InputBinary.Encode(new ScheduledInput(0, InputKind.Proposal, 0,
                    Math.Max(1, p.ReadyTick), p.Reply.RequestId, 0, p.Reply.Orders, long.MaxValue, p.Reply.Reason));
                w.Write(bytes.Length); w.Write(bytes);
            }
        });
        private static FactionObservation Copy(FactionObservation o)
        {
            if (o == null) throw new ArgumentException("Request observation is required.", nameof(o));
            return new FactionObservation(o.FactionId, o.Tick, o.OwnArmies.ToArray(), o.VisibleEnemies.ToArray(), o.Contacts.ToArray(), o.Objectives.ToArray());
        }
    }
}
