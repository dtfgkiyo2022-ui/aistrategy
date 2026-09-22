using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>
    /// Chapter 11 UI timeline: first contact, report shown, command accepted, interpreted, applied,
    /// departure, arrival, loss and capture change. Built from captured frames only; no wall clock.
    /// </summary>
    public sealed class MatchTimeline
    {
        public readonly struct Entry
        {
            public long Tick { get; }
            public string Text { get; }
            public Entry(long tick, string text) { Tick = tick; Text = text; }
        }

        private const int Capacity = 400;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<ulong, CommandStatus> commandStatus = new Dictionary<ulong, CommandStatus>();
        private readonly HashSet<uint> knownContacts = new HashSet<uint>();
        private readonly Dictionary<uint, uint> outpostOwners = new Dictionary<uint, uint>();
        private long lastTick = -1;
        private uint factionId;

        public IReadOnlyList<Entry> Entries { get { return entries; } }

        public void Clear()
        {
            entries.Clear();
            commandStatus.Clear();
            knownContacts.Clear();
            outpostOwners.Clear();
            lastTick = -1;
        }

        public void Ingest(FactionFrame frame)
        {
            if (frame == null) return;
            if (frame.FactionId != factionId) { factionId = frame.FactionId; Clear(); }
            // A tick that goes backwards means the match was restarted: forget the old match's entries.
            if (frame.Tick < lastTick) Clear();
            else if (frame.Tick == lastTick) return;
            lastTick = frame.Tick;

            foreach (var contact in frame.Observation.Contacts)
            {
                if (contact.IsArmyContact || !knownContacts.Add(contact.ContactId)) continue;
                Add(frame.Tick, "first contact C" + contact.ContactId + " (report shown t=" + frame.Tick + ")");
            }

            foreach (var objective in frame.Objectives)
            {
                if (objective.Kind != GoalKind.Outpost || !objective.IsOwnerKnown) continue;
                if (outpostOwners.TryGetValue(objective.Id, out var previous))
                {
                    if (previous == objective.OwnerFactionId) continue;
                    Add(frame.Tick, "capture changed: outpost " + objective.Id + " faction " + previous + " -> " + objective.OwnerFactionId);
                }
                outpostOwners[objective.Id] = objective.OwnerFactionId;
            }

            foreach (var command in frame.Commands)
            {
                bool known = commandStatus.TryGetValue(command.CommandId, out var previous);
                if (!known)
                {
                    Add(command.AcceptedTick, "command accepted #" + command.CommandId + " " + command.Kind
                        + " " + command.Target.Kind + command.Target.Id + " (" + command.Source + ")");
                }
                if (known && previous == command.Status) continue;
                commandStatus[command.CommandId] = command.Status;
                switch (command.Status)
                {
                    case CommandStatus.Pending:
                        Add(frame.Tick, "interpreted #" + command.CommandId + ", applies at t=" + command.ApplyTick);
                        break;
                    case CommandStatus.Executing:
                        Add(frame.Tick, "applied #" + command.CommandId);
                        break;
                    case CommandStatus.Completed:
                        Add(frame.Tick, "arrived/completed #" + command.CommandId);
                        break;
                    case CommandStatus.Cancelled:
                    case CommandStatus.Expired:
                    case CommandStatus.Impossible:
                        Add(frame.Tick, command.Status.ToString().ToLowerInvariant() + " #" + command.CommandId
                            + (command.Reason == ReasonCode.None ? "" : " (" + command.Reason + ")"));
                        break;
                }
            }

            foreach (var e in frame.Events)
            {
                switch (e.Kind)
                {
                    case EventKind.MoveStarted:
                        Add(e.Tick, "departed: unit " + e.SubjectId);
                        break;
                    case EventKind.Death:
                        Add(e.Tick, "loss: unit " + e.SubjectId);
                        break;
                    case EventKind.Capture:
                        Add(e.Tick, "capture event: outpost " + e.SubjectId + " -> faction " + e.Value);
                        break;
                    case EventKind.Reinforcement:
                        Add(e.Tick, "reinforcement: +" + e.Value + " at " + e.SubjectId);
                        break;
                    case EventKind.MatchEnded:
                        var outcome = MatchOutcome.Describe(frame.Result, frame.FactionId, e.Tick);
                        Add(e.Tick, outcome.HasValue ? "match ended: " + outcome.Value.Headline : "match ended");
                        break;
                }
            }
        }

        private void Add(long tick, string text)
        {
            entries.Add(new Entry(tick, text));
            if (entries.Count > Capacity) entries.RemoveRange(0, entries.Count - Capacity);
        }
    }
}
