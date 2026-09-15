using System;
using System.Collections.Generic;
using Rts.Contracts;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    // Existing movement/combat tests use immediate templates; exercise the same Reserve/Resolve boundary.
    internal static class CommandTestInput
    {
        internal static void Step(Battle sim, long tick, IReadOnlyList<ScheduledInput> source)
        {
            if (source == null || source.Count == 0 || sim.Capture(1).Result.HasEnded) { sim.Step(tick, source); return; }
            var inputs = new List<ScheduledInput>();
            var revisions = new Dictionary<ScopeKey, ulong>();
            ulong index = checked((ulong)tick * 100);
            foreach (var input in source)
            foreach (var o in input.Orders)
            {
                ulong revision = revisions.TryGetValue(o.Target, out var v) ? v : sim.Revision(o.Target);
                revisions[o.Target] = revision + 1;
                ulong id = ++index;
                PolicyOrder Order(ulong rev) => new PolicyOrder(id, 0, CommandSource.Human, o.Target, o.Kind, o.Goal,
                    o.Priority, o.AllowedLoss, o.End, o.ReservePermille, rev, Array.Empty<PolicyVersion>(), tick - 1, o.Expiration);
                inputs.Add(new ScheduledInput(index, InputKind.Reserve, tick - 1, tick, id, id, new[] { Order(revision) }));
                inputs.Add(new ScheduledInput(++index, InputKind.Resolve, tick - 1, tick, id, id, new[] { Order(revision + 1) }));
            }
            sim.Step(tick, inputs);
        }
    }
}
