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
        private readonly List<Request> requests = new List<Request>();
        private readonly List<Arrival> arrivals = new List<Arrival>();
        private readonly List<ScheduledInput> log = new List<ScheduledInput>();
        private ulong nextRequest = 1, nextCommand = 1, nextBatch = 1, nextLog = 1;
        private long tick;
        public CommandGateway(Battle simulation)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
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
        public ulong SubmitInterpreted(UserPolicyIntent intent, int deadlineTicks = 240) => Enqueue(new[] { intent }, false, deadlineTicks);
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
            return r.Id;
        }
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
                Orders = reply.Orders.ToArray(), Reason = tick > r.Deadline ? ReasonCode.Deadline : reply.Reason });
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
                    if (!r.Reserved || a.Orders.Length != r.Orders.Length) reason = ReasonCode.InvalidPayload;
                    var orders = reason != ReasonCode.None ? Array.Empty<PolicyOrder>() : a.Orders.Select((o, i) =>
                        Copy(o, r.Orders[i].CommandId, r.Batch, r.Orders[i].TargetRevision, r.Orders[i].Parents)).ToArray();
                    Add(inputs, InputKind.Resolve, a.Tick, Math.Max(checked(r.ReceivedTick + 40), next), r, orders, reason);
                }
            }
            arrivals.Clear();
            simulation.Step(next, inputs); tick = next; log.AddRange(inputs);
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
