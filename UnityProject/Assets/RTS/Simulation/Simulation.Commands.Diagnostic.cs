using System.Globalization;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private void WriteCommands(StateWriter w)
        {
            w.Ids("Policies.LossReturns", lossReturns.OrderBy(id => id).ToArray());
            w.Value("Gateway.NextCommandId", nextCommandId); w.Value("Gateway.NextRequestId", nextRequestId);
            w.Value("Gateway.NextBatchId", nextBatchId); w.Value("Gateway.NextLogIndex", checked(world.InputCursor + 1));
            w.Value("Revisions.Count", (uint)revisions.Count);
            int index = 0;
            foreach (var v in revisions.OrderBy(v => v.Scope.FactionId).ThenBy(v => v.Scope.Kind).ThenBy(v => v.Scope.Id))
            {
                string n = "Revisions[" + (index++).ToString(CultureInfo.InvariantCulture) + "].";
                WriteScope(w, n, v.Scope); w.Value(n + "Revision", v.Revision);
            }
            w.Value("RevisionChanges.Count", (uint)revisionChanges.Count);
            for (int i = 0; i < revisionChanges.Count; i++)
            {
                string p = "RevisionChanges[" + i.ToString(CultureInfo.InvariantCulture) + "].";
                WriteScope(w, p, revisionChanges[i].Scope); w.Value(p + "Revision", revisionChanges[i].Revision); w.Value(p + "Field", revisionChanges[i].Field);
            }
            w.Value("CommandStates.Count", (uint)commandStates.Count);
            foreach (var c in commandStates.OrderBy(c => c.ApplyTick).ThenBy(c => c.LogIndex).ThenBy(c => c.Order.CommandId))
            {
                string n = "CommandStates[" + c.Order.CommandId.ToString(CultureInfo.InvariantCulture) + "].";
                var o = c.Order;
                w.Value(n + "CommandId", o.CommandId); w.Value(n + "BatchId", o.BatchId); w.Value(n + "Source", (byte)o.Source);
                WriteScope(w, n + "Target.", o.Target); w.Value(n + "Kind", (byte)o.Kind); w.Goal(n + "Goal", o.Goal);
                w.Value(n + "Priority", o.Priority); w.Value(n + "AllowedLoss", (uint)o.AllowedLoss.Permille);
                w.Value(n + "End.Kind", (byte)o.End.Kind); w.Value(n + "End.Tick", o.End.Tick); w.Value(n + "ReservePermille", (uint)o.ReservePermille);
                w.Value(n + "TargetRevision", o.TargetRevision); w.Value(n + "ObservedTick", o.ObservedTick);
                w.Value(n + "Expiration.ValidUntilTick", o.Expiration.ValidUntilTick); w.Value(n + "Expiration.MaxObservationAgeTicks", o.Expiration.MaxObservationAgeTicks);
                w.Value(n + "Expiration.Flags", (byte)o.Expiration.Flags);
                WriteVersions(w, n + "Parents", o.Parents.OrderBy(v => v.Scope.FactionId).ThenBy(v => v.Scope.Kind).ThenBy(v => v.Scope.Id).ToArray());
                WriteVersions(w, n + "Dependencies", c.Dependencies);
                w.Value(n + "RequestId", c.RequestId); w.Value(n + "LogIndex", c.LogIndex); w.Value(n + "ExecutionRevision", c.ExecutionRevision);
                w.Value(n + "AcceptedTick", c.AcceptedTick); w.Value(n + "ApplyTick", c.ApplyTick); w.Value(n + "DeadlineTick", c.DeadlineTick);
                w.Value(n + "Acquired", c.Acquired);
                w.Value(n + "Status", (byte)c.Status); w.Value(n + "Reason", (byte)c.Reason); w.Ids(n + "ReservedArmies", c.ReservedArmies);
                w.Value(n + "Armies.Count", (uint)c.Armies.Count);
                foreach (var a in c.Armies)
                {
                    string p = n + "Armies[" + a.ArmyId.ToString(CultureInfo.InvariantCulture) + "].";
                    w.Value(p + "ArmyId", a.ArmyId); w.Ids(p + "StartIds", a.StartIds); w.Value(p + "N0", a.N0); w.Value(p + "Deaths", a.Deaths);
                    w.Value(p + "Active", a.Active); w.Value(p + "Returning", a.Returning); w.Value(p + "Finished", a.Finished); w.Value(p + "Failed", a.Failed);
                }
            }
        }
        private static void WriteScope(StateWriter w, string n, ScopeKey s)
        { w.Value(n + "FactionId", s.FactionId); w.Value(n + "Kind", (byte)s.Kind); w.Value(n + "Id", s.Id); }
        private static void WriteVersions(StateWriter w, string n, PolicyVersion[] versions)
        {
            w.Value(n + ".Count", (uint)versions.Length);
            for (int i = 0; i < versions.Length; i++)
            {
                string p = n + "[" + i.ToString(CultureInfo.InvariantCulture) + "].";
                WriteScope(w, p, versions[i].Scope); w.Value(p + "Revision", versions[i].Revision);
            }
        }
    }
}
