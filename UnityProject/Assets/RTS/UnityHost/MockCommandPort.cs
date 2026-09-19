using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.UnityHost
{
    /// <summary>Accepts commands without a simulation behind it, so the input UI can be checked on its own.</summary>
    public sealed class MockCommandPort : ICommandPort
    {
        private ulong nextId = 1;
        private long tick;

        private sealed class Entry
        {
            public ulong Id;
            public UserPolicyIntent Intent;
            public long AcceptedTick;
            public bool Cancelled;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public List<UserPolicyIntent> Submitted { get; } = new List<UserPolicyIntent>();

        public void SetTick(long currentTick) { tick = currentTick; }

        public ulong Submit(UserPolicyIntent intent)
        {
            Submitted.Add(intent);
            ulong id = nextId++;
            entries.Add(new Entry { Id = id, Intent = intent, AcceptedTick = tick });
            return id;
        }

        // Fake status timeline so the display can be checked: interpret 20 ticks, wait until 60, run until 500.
        // A Focus toward the far east (x > 200 m) is shown as impossible (no path).
        public IReadOnlyList<CommandView> Views()
        {
            var views = new List<CommandView>();
            foreach (var e in entries)
            {
                long age = tick - e.AcceptedTick;
                var status = e.Cancelled ? CommandStatus.Cancelled
                    : age < 20 ? CommandStatus.Interpreting
                    : age < 60 ? CommandStatus.Pending
                    : age < 500 ? CommandStatus.Executing : CommandStatus.Completed;
                var reason = e.Cancelled ? ReasonCode.UserCancelled : ReasonCode.None;
                bool unreachable = e.Intent.Kind == PolicyKind.Focus && e.Intent.Goal.Kind == GoalKind.Point && e.Intent.Goal.Point.X.Raw > 200L * 65536L;
                if (unreachable && age >= 60) { status = CommandStatus.Impossible; reason = ReasonCode.NoPath; }
                views.Add(new CommandView(e.Id, e.Intent.Target, e.Intent.Kind, e.Intent.Goal, status,
                    e.AcceptedTick, e.AcceptedTick + 60, reason, CommandSource.Human));
            }
            return views;
        }

        public void Cancel(ulong requestId)
        {
            foreach (var e in entries) if (e.Id == requestId) e.Cancelled = true;
        }

        public ulong Propose(uint faction, ulong sequence, IReadOnlyList<PolicyOrder> orders, long applyTick)
        {
            return nextId++;
        }
    }
}
