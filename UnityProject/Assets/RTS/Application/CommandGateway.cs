using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rts.Contracts;
using Rts.Replay;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Application
{
    /// <summary>Faction-local policy versions for doctrine generators; no world or enemy state is exposed.</summary>
    public interface IFactionPolicyVersions
    {
        IReadOnlyList<PolicyVersion> Versions(ScopeKey scope);
    }
    /// <summary>Tick driven command boundary. Call Submit/Cancel between Step calls on the host thread.</summary>
    public sealed class CommandGateway : ICommandPort
    {
        private sealed class Request
        {
            internal ulong Id, Batch, Sequence;
            internal long ReceivedTick, Deadline;
            internal PolicyOrder[] Orders;
            internal bool Direct, Reserved;
        }
        private sealed class AutonomousRequest
        {
            internal PolicyRequest Snapshot;
            internal bool Completed;
            internal ReasonCode Invalidated;
        }
        private readonly List<AutonomousRequest> autonomous = new List<AutonomousRequest>();
        private readonly List<UserPolicyIntent> autonomousTargets = new List<UserPolicyIntent>();
        public AiTimingProfile AiProfile { get; }
        private sealed class Arrival
        {
            internal Request Request;
            internal InputKind Kind;
            internal long Tick;
            internal ulong Sequence;
            internal PolicyOrder[] Orders;
            internal ReasonCode Reason;
        }
        private readonly Battle simulation;
        private readonly IPolicyProvider provider;
        private readonly List<Request> requests = new List<Request>();
        private readonly List<Arrival> arrivals = new List<Arrival>();
        private readonly List<ScheduledInput> log = new List<ScheduledInput>();
        private ulong nextRequest = 1, nextCommand = 1, nextBatch = 1, nextLog = 1;
        private long tick;
        public CommandGateway(Battle simulation, IPolicyProvider provider = null, AiTimingProfile profile = null)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            this.provider = provider;
            AiProfile = profile ?? (provider as DelayedPolicyProvider)?.Profile ?? AiTimingProfile.Default;
            if (provider is DelayedPolicyProvider delayed && delayed.Profile != AiProfile)
                throw new ArgumentException("Gateway and provider must use the same AI profile.", nameof(profile));
            tick = simulation.Capture(1).Tick;
            if (tick != 0) throw new ArgumentException("Attach the gateway at S0.", nameof(simulation));
        }
        public IReadOnlyList<ScheduledInput> Inputs => Array.AsReadOnly(log.ToArray());
        internal IFactionPolicyVersions FactionVersions(uint faction)
        {
            if (faction < 1 || faction > 2) throw new ArgumentOutOfRangeException(nameof(faction));
            return new FactionVersionReader(simulation, faction);
        }
        private sealed class FactionVersionReader : IFactionPolicyVersions
        {
            private readonly Battle simulation; private readonly uint faction;
            internal FactionVersionReader(Battle simulation, uint faction) { this.simulation = simulation; this.faction = faction; }
            public IReadOnlyList<PolicyVersion> Versions(ScopeKey scope)
            {
                if (scope.FactionId != faction) throw new ArgumentException("Doctrine may only read its own faction versions.", nameof(scope));
                return simulation.Versions(scope);
            }
        }
        public ulong Submit(UserPolicyIntent intent) => SubmitBatch(new[] { intent });
        public ulong SubmitBatch(IReadOnlyList<UserPolicyIntent> intents) => Enqueue(intents, true, 240);
        public ulong SubmitInterpreted(UserPolicyIntent intent, int? deadlineTicks = null) => Enqueue(new[] { intent }, false, deadlineTicks ?? AiProfile.DeadlineTicks);
        private ulong Enqueue(IReadOnlyList<UserPolicyIntent> intents, bool direct, int deadlineTicks)
        {
            if (intents == null || intents.Count == 0 || deadlineTicks < 0) throw new ArgumentException("Empty request or negative deadline.");
            uint faction = intents[0].Target.FactionId;
            if (faction < 1 || faction > 2 || intents.Any(i => i.Target.FactionId != faction)) throw new ArgumentException("A request belongs to one faction.");
            var r = new Request { Id = nextRequest, Batch = nextBatch, ReceivedTick = tick, Deadline = checked(tick + deadlineTicks),
                Sequence = intents[0].IssuerSequence, Direct = direct };
            nextRequest = checked(nextRequest + 1); nextBatch = checked(nextBatch + 1);
            r.Orders = intents.Select(i =>
            {
                ulong id = nextCommand; nextCommand = checked(nextCommand + 1);
                return new PolicyOrder(id, r.Batch, CommandSource.Human, i.Target, i.Kind, i.Goal, i.Priority, i.AllowedLoss,
                    i.End, i.ReservePermille, 0, Array.Empty<PolicyVersion>(), tick, i.Expiration);
            }).ToArray();
            requests.Add(r);
            arrivals.Add(new Arrival { Request = r, Kind = InputKind.Reserve, Tick = tick, Sequence = r.Sequence });
            if (!direct && provider != null)
            {
                var frame = simulation.Capture(faction);
                var observation = frame.Observation;
                // PolicyRequest owns immutable DTO lists, and DelayedPolicyProvider takes a second copy.
                provider.Request(new PolicyRequest(r.Id, faction, intents[0].Target, tick,
                    new FactionObservation(observation.FactionId, observation.Tick, observation.OwnArmies.ToArray(),
                        observation.VisibleEnemies.ToArray(), observation.Contacts.ToArray(), observation.Objectives.ToArray()),
                    simulation.Versions(intents[0].Target).ToArray(), r.Deadline, intents[0].Kind, intents[0].Goal));
            }
            return r.Id;
        }
        /// <summary>Register a recurring upper-policy decision, evaluated at R=0,20,40,... .</summary>
        public void EnableAutonomous(UserPolicyIntent intent)
        {
            if (provider == null) throw new InvalidOperationException("Autonomous requests require a provider.");
            if (intent.Target.FactionId < 1 || intent.Target.FactionId > 2) throw new ArgumentException("Invalid faction.");
            if (autonomousTargets.Any(i => i.Target.Equals(intent.Target))) throw new ArgumentException("Target already registered.");
            autonomousTargets.Add(intent);
        }
        private void InvalidateAutonomous()
        {
            foreach (var r in autonomous.Where(r => !r.Completed && r.Invalidated == ReasonCode.None))
                if (r.Snapshot.Versions.Any(v => simulation.Revision(v.Scope) != v.Revision)) r.Invalidated = ReasonCode.StaleVersion;
                else if (tick > r.Snapshot.DeadlineTick) r.Invalidated = ReasonCode.Deadline;
        }
        private void PollAutonomous()
        {
            InvalidateAutonomous();
            if (tick % 20 != 0) return;
            foreach (var intent in autonomousTargets)
            {
                if (autonomous.Any(r => !r.Completed && r.Invalidated == ReasonCode.None && r.Snapshot.Scope.Equals(intent.Target))) continue;
                var snapshot = new PolicyRequest(nextRequest, intent.Target.FactionId, intent.Target, tick,
                    simulation.Capture(intent.Target.FactionId).Observation, simulation.Versions(intent.Target),
                    checked(tick + AiProfile.DeadlineTicks), intent.Kind, intent.Goal);
                nextRequest = checked(nextRequest + 1);
                autonomous.Add(new AutonomousRequest { Snapshot = snapshot });
                provider.Request(snapshot);
            }
        }
        private void Receive(PolicyReply reply)
        {
            var pending = autonomous.Find(r => r.Snapshot.RequestId == reply.RequestId);
            if (pending == null) { Resolve(reply); return; }
            if (reply.ReturnedTick != tick) throw new ArgumentException("Reply must be received at the current tick.");
            if (pending.Completed) return;
            pending.Completed = true;
            var snapshot = pending.Snapshot;
            var reason = pending.Invalidated != ReasonCode.None ? pending.Invalidated : reply.Reason;
            if (reason != ReasonCode.None || reply.Orders.Count == 0 || reply.Orders.Any(o => !o.Target.Equals(snapshot.Scope)))
            {
                // Rejections are external control inputs too, even though AI requests have no reservation.
                logRejections.Add(new ScheduledInput(0, InputKind.Proposal, tick, tick + 1, snapshot.RequestId, 0,
                    Array.Empty<PolicyOrder>(), snapshot.DeadlineTick, reason == ReasonCode.None ? ReasonCode.InvalidPayload : reason));
                return;
            }
            var orders = reply.Orders.Select(o => new PolicyOrder(0, 0, CommandSource.Ai, snapshot.Scope,
                o.Kind, o.Goal, o.Priority, o.AllowedLoss, o.End, o.ReservePermille,
                snapshot.Versions.First(v => v.Scope.Equals(snapshot.Scope)).Revision,
                snapshot.Versions.Where(v => !v.Scope.Equals(snapshot.Scope)).ToArray(), snapshot.StartedTick,
                new Expiration(o.Expiration.ValidUntilTick, AiProfile.MaxObservationAgeTicks,
                    o.Expiration.Flags | ExpireFlags.ObservationTooOld))).ToArray();
            Propose(snapshot.FactionId, snapshot.RequestId, orders,
                Math.Max(checked(snapshot.StartedTick + DelayedPolicyProvider.TransmissionTicks), tick + 1));
        }
        private readonly List<ScheduledInput> logRejections = new List<ScheduledInput>();
        public void Cancel(ulong requestId)
        {
            var r = requests.Find(v => v.Id == requestId);
            if (r == null) throw new ArgumentException("Unknown request.", nameof(requestId));
            arrivals.Add(new Arrival { Request = r, Kind = InputKind.Cancel, Tick = tick, Sequence = r.Sequence });
        }
        /// <summary>Provider IDs are ignored. Only this request's allocated commands and reservation tokens are used.</summary>
        public void Resolve(PolicyReply reply)
        {
            if (reply == null || reply.ReturnedTick != tick) throw new ArgumentException("Reply must be received at the current tick.");
            var r = requests.Find(v => v.Id == reply.RequestId);
            if (r == null) throw new ArgumentException("Unknown request.");
            arrivals.Add(new Arrival { Request = r, Kind = InputKind.Resolve, Tick = tick, Sequence = r.Sequence,
                Orders = reply.Orders.Select(o => new PolicyOrder(o.CommandId, o.BatchId, CommandSource.Human,
                    o.Target, o.Kind, o.Goal, o.Priority, o.AllowedLoss, o.End, o.ReservePermille, o.TargetRevision,
                    o.Parents, r.ReceivedTick, new Expiration(o.Expiration.ValidUntilTick, AiProfile.MaxObservationAgeTicks,
                        o.Expiration.Flags | ExpireFlags.ObservationTooOld))).ToArray(),
                Reason = tick > r.Deadline ? ReasonCode.Deadline : reply.Reason });
        }
        public ulong Propose(uint faction, ulong sequence, IReadOnlyList<PolicyOrder> orders, long applyTick)
        {
            if (orders == null || orders.Count == 0 || orders.Any(o => o == null || o.Target.FactionId != faction || o.Source == CommandSource.Human) || applyTick <= tick)
                throw new ArgumentException("Invalid proposal.");
            var r = new Request { Id = nextRequest, Batch = nextBatch, Sequence = sequence, ReceivedTick = tick, Deadline = applyTick };
            nextRequest = checked(nextRequest + 1); nextBatch = checked(nextBatch + 1);
            r.Orders = orders.Select(o => { ulong id = nextCommand; nextCommand = checked(nextCommand + 1); return Copy(o, id, r.Batch, o.TargetRevision, o.Parents); }).ToArray();
            requests.Add(r); arrivals.Add(new Arrival { Request = r, Kind = InputKind.Proposal, Tick = tick, Sequence = sequence });
            return r.Id;
        }
        public IReadOnlyList<ScheduledInput> Step()
        {
            PollAutonomous();
            if (provider != null)
                foreach (var reply in provider.Poll(tick)) Receive(reply);
            long next = checked(tick + 1);
            var inputs = new List<ScheduledInput>();
            var reserved = new List<Request>();
            foreach (var a in arrivals.OrderBy(a => a.Tick).ThenBy(a => a.Request.Orders[0].Target.FactionId).ThenBy(a => a.Sequence)
                .ThenBy(a => a.Request.Id).ThenBy(a => a.Kind))
            {
                var r = a.Request;
                if (a.Kind == InputKind.Reserve)
                {
                    var orders = r.Orders.Select(o => Copy(o, o.CommandId, r.Batch, simulation.Revision(o.Target),
                        simulation.Versions(o.Target).Where(v => !v.Scope.Equals(o.Target)).ToArray())).ToArray();
                    Add(inputs, InputKind.Reserve, a.Tick, next, r, orders, ReasonCode.None);
                    reserved.Add(r);
                }
                else if (a.Kind == InputKind.Cancel) Add(inputs, InputKind.Cancel, a.Tick, next, r, Array.Empty<PolicyOrder>(), ReasonCode.None);
                else if (a.Kind == InputKind.Proposal) Add(inputs, InputKind.Proposal, a.Tick, r.Deadline, r, r.Orders, ReasonCode.None);
                else
                {
                    ReasonCode reason = a.Reason;
                    // A zero-delay reply can arrive in the same host tick as Reserve.  The input
                    // ordering below puts Reserve first, so it receives the same confirmed token.
                    bool reservesNow = arrivals.Any(v => v.Request == r && v.Kind == InputKind.Reserve);
                    if (reason == ReasonCode.None && ((!r.Reserved && !reservesNow) || a.Orders.Length != r.Orders.Length)) reason = ReasonCode.InvalidPayload;
                    var orders = reason != ReasonCode.None ? Array.Empty<PolicyOrder>() : a.Orders.Select((o, i) =>
                        Copy(o, r.Orders[i].CommandId, r.Batch, r.Orders[i].TargetRevision, r.Orders[i].Parents)).ToArray();
                    Add(inputs, InputKind.Resolve, a.Tick, Math.Max(checked(r.ReceivedTick + 40), next), r, orders, reason);
                }
            }
            foreach (var rejection in logRejections)
            {
                inputs.Add(new ScheduledInput(nextLog, rejection.Kind, rejection.AcceptedTick, rejection.ApplyTick,
                    rejection.RequestId, rejection.IssuerSequence, rejection.Orders, rejection.DeadlineTick, rejection.ResolutionReason));
                nextLog = checked(nextLog + 1);
            }
            logRejections.Clear();
            arrivals.Clear();
            simulation.Step(next, inputs); tick = next; log.AddRange(inputs);
            InvalidateAutonomous();
            foreach (var r in reserved)
            {
                r.Orders = r.Orders.Select(o => Copy(o, o.CommandId, r.Batch, simulation.ExecutionRevision(o.CommandId), o.Parents)).ToArray();
                r.Reserved = true;
                if (r.Direct) arrivals.Add(new Arrival { Request = r, Kind = InputKind.Resolve, Tick = tick,
                    Sequence = r.Sequence, Orders = r.Orders, Reason = ReasonCode.None });
            }
            return inputs.ToArray();
        }
        private void Add(List<ScheduledInput> inputs, InputKind kind, long received, long apply, Request r, PolicyOrder[] orders, ReasonCode reason)
        {
            inputs.Add(new ScheduledInput(nextLog, kind, received, apply, r.Id, r.Sequence, orders, r.Deadline, reason));
            nextLog = checked(nextLog + 1);
        }
        private static PolicyOrder Copy(PolicyOrder o, ulong command, ulong batch, ulong revision, IReadOnlyList<PolicyVersion> parents) =>
            new PolicyOrder(command, batch, o.Source, o.Target, o.Kind, o.Goal, o.Priority, o.AllowedLoss, o.End,
                o.ReservePermille, revision, parents, o.ObservedTick, o.Expiration);

        /// <summary>Host diagnostic includes allocator cursors and received-but-not-yet-delivered control inputs.</summary>
        public byte[] CaptureDiagnostic() => ReplayBinary.Pack(w =>
        {
            ReplayBinary.Text(w, AiProfile.Name);
            var providerBytes = (provider as DelayedPolicyProvider)?.CaptureDiagnostic() ?? Array.Empty<byte>();
            w.Write(providerBytes.Length); w.Write(providerBytes);
            w.Write(autonomousTargets.Count);
            foreach (var intent in autonomousTargets)
            {
                w.Write(intent.Target.FactionId); w.Write((byte)intent.Target.Kind); w.Write(intent.Target.Id);
                w.Write((byte)intent.Kind); w.Write((byte)intent.Goal.Kind); w.Write(intent.Goal.Id);
                w.Write(intent.Goal.Point.X.Raw); w.Write(intent.Goal.Point.Z.Raw);
            }
            w.Write(autonomous.Count);
            foreach (var r in autonomous)
            {
                w.Write(r.Snapshot.RequestId); w.Write(r.Snapshot.StartedTick); w.Write(r.Snapshot.DeadlineTick);
                w.Write(r.Snapshot.Scope.FactionId); w.Write((byte)r.Snapshot.Scope.Kind); w.Write(r.Snapshot.Scope.Id);
                w.Write((byte)r.Snapshot.Kind); w.Write((byte)r.Snapshot.Goal.Kind); w.Write(r.Snapshot.Goal.Id);
                w.Write(r.Snapshot.Goal.Point.X.Raw); w.Write(r.Snapshot.Goal.Point.Z.Raw);
                w.Write(r.Completed); w.Write((byte)r.Invalidated);
                w.Write(r.Snapshot.Versions.Count);
                foreach (var v in r.Snapshot.Versions)
                { w.Write(v.Scope.FactionId); w.Write((byte)v.Scope.Kind); w.Write(v.Scope.Id); w.Write(v.Revision); }
            }
            w.Write(tick); w.Write(nextRequest); w.Write(nextCommand); w.Write(nextBatch); w.Write(nextLog);
            w.Write(requests.Count);
            foreach (var r in requests)
            {
                w.Write(r.Id); w.Write(r.Batch); w.Write(r.Sequence); w.Write(r.ReceivedTick); w.Write(r.Deadline); w.Write(r.Direct); w.Write(r.Reserved);
                var b = InputBinary.Encode(new ScheduledInput(0, InputKind.Reserve, r.ReceivedTick, Math.Max(1, r.ReceivedTick + 1), r.Id, r.Sequence, r.Orders, r.Deadline, ReasonCode.None));
                w.Write(b.Length); w.Write(b);
            }
            w.Write(arrivals.Count);
            foreach (var a in arrivals)
            {
                w.Write(a.Request.Id); w.Write((byte)a.Kind); w.Write(a.Tick); w.Write(a.Sequence); w.Write((byte)a.Reason);
                var b = InputBinary.Encode(new ScheduledInput(0, a.Kind, a.Tick, a.Tick + 1, a.Request.Id, a.Sequence, a.Orders ?? Array.Empty<PolicyOrder>()));
                w.Write(b.Length); w.Write(b);
            }
        });
    }
}
