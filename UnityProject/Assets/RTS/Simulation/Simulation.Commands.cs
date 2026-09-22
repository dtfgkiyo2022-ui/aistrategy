using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private sealed class RevisionChange
        {
            internal ScopeKey Scope;
            internal ulong Revision;
            internal int Field;
        }
        private readonly List<RevisionChange> revisionChanges = new List<RevisionChange>();
        private sealed class ArmyExecution
        {
            internal uint ArmyId;
            internal uint[] StartIds = Array.Empty<uint>();
            internal int N0, Deaths;
            internal bool Active = true, Returning, Finished, Failed;
        }
        private sealed class CommandState
        {
            internal PolicyOrder Order;
            internal ulong RequestId, LogIndex, ExecutionRevision;
            internal long AcceptedTick, ApplyTick, DeadlineTick;
            internal CommandStatus Status;
            internal ReasonCode Reason;
            internal bool Acquired;
            internal uint[] ReservedArmies = Array.Empty<uint>();
            internal PolicyVersion[] Dependencies = Array.Empty<PolicyVersion>();
            internal List<ArmyExecution> Armies = new List<ArmyExecution>();
        }
        private readonly List<CommandState> commandStates = new List<CommandState>();
        private readonly List<PolicyVersion> revisions = new List<PolicyVersion>();
        private readonly List<uint> lossReturns = new List<uint>();
        private readonly List<GameEvent> commandEvents = new List<GameEvent>();
        private ulong nextCommandId = 1, nextRequestId = 1, nextBatchId = 1;

        public ulong ExecutionRevision(ulong commandId) => commandStates.First(c => c.Order.CommandId == commandId).ExecutionRevision;

        public ulong Revision(ScopeKey scope)
        {
            foreach (var v in revisions) if (v.Scope.Equals(scope)) return v.Revision;
            return 0;
        }
        private ulong Advance(ScopeKey scope, int field = 1)
        {
            ulong value = checked(Revision(scope) + 1);
            int index = revisions.FindIndex(v => v.Scope.Equals(scope));
            if (index < 0) revisions.Add(new PolicyVersion(scope, value));
            else revisions[index] = new PolicyVersion(scope, value);
            revisionChanges.Add(new RevisionChange { Scope = scope, Revision = value, Field = field });
            return value;
        }
        private bool VersionMatches(CommandState c, PolicyVersion version) => Revision(version.Scope) == version.Revision ||
            c.Order.Source == CommandSource.Human && !revisionChanges.Any(change => change.Scope.Equals(version.Scope) &&
                change.Revision > version.Revision && change.Field == Field(c.Order.Kind));

        private static bool Terminal(CommandState c) => c.Status >= CommandStatus.Completed;
        private static int Field(PolicyKind kind) => kind == PolicyKind.MaintainReserve ? 2 : kind == PolicyKind.AllowAbandon ? 3 : 1;
        private bool ValidScope(ScopeKey s) => s.FactionId >= 1 && s.FactionId <= 2 &&
            (s.Kind == ScopeKind.All ? s.Id == 0 : s.Kind == ScopeKind.Army ? s.Id > 0 && s.Id <= world.Armies.Length && world.Armies[s.Id - 1].Definition.FactionId == s.FactionId
            : s.Kind == ScopeKind.Outpost && s.Id > 0 && s.Id <= world.Outposts.Length);
        private uint[] Affected(ScopeKey s)
        {
            if (!ValidScope(s)) return Array.Empty<uint>();
            return world.Armies.Where(a => a.Definition.FactionId == s.FactionId &&
                (s.Kind == ScopeKind.All || s.Kind == ScopeKind.Army && a.Definition.Id == s.Id ||
                 s.Kind == ScopeKind.Outpost && (a.Definition.HomeObjective.Kind == GoalKind.Outpost && a.Definition.HomeObjective.Id == s.Id || a.Goal.Kind == GoalKind.Outpost && a.Goal.Id == s.Id || a.Decision.Goal.Kind == GoalKind.Outpost && a.Decision.Goal.Id == s.Id)))
                .Select(a => a.Definition.Id).OrderBy(id => id).ToArray();
        }
        private bool Overlap(CommandState a, PolicyOrder b)
        {
            if (a.Order.Target.FactionId != b.Target.FactionId) return false;
            if (a.Status == CommandStatus.Executing)
                return a.Armies.Any(e => e.Active && Affected(b.Target).Contains(e.ArmyId)) ||
                    a.Armies.Count == 0 && (a.Order.Target.Equals(b.Target) || b.Target.Kind == ScopeKind.All);
            return a.Order.Target.Equals(b.Target) || a.Order.Target.Kind == ScopeKind.All || b.Target.Kind == ScopeKind.All ||
                Affected(a.Order.Target).Intersect(Affected(b.Target)).Any() || a.ReservedArmies.Intersect(Affected(b.Target)).Any();
        }
        /// <summary>Only dependencies actually used by this scope; unrelated outposts are excluded.</summary>
        public IReadOnlyList<PolicyVersion> Versions(ScopeKey scope)
        {
            var scopes = new List<ScopeKey> { scope };
            if (scope.Kind != ScopeKind.All) scopes.Add(new ScopeKey(scope.FactionId, ScopeKind.All, 0));
            foreach (uint id in Affected(scope))
            {
                var armyScope = new ScopeKey(scope.FactionId, ScopeKind.Army, id);
                if (!scopes.Contains(armyScope)) scopes.Add(armyScope);
                var mission = world.Armies[id - 1].Decision.Goal;
                if (mission.Kind == GoalKind.Outpost)
                {
                    var missionScope = new ScopeKey(scope.FactionId, ScopeKind.Outpost, mission.Id);
                    if (!scopes.Contains(missionScope)) scopes.Add(missionScope);
                }
                foreach (var c in commandStates)
                    if (!Terminal(c) && c.Status == CommandStatus.Executing && c.Order.Target.Kind == ScopeKind.Outpost &&
                        c.Armies.Any(a => a.ArmyId == id && a.Active) && !scopes.Contains(c.Order.Target)) scopes.Add(c.Order.Target);
            }
            return scopes.OrderBy(s => s.FactionId).ThenBy(s => s.Kind).ThenBy(s => s.Id)
                .Select(s => new PolicyVersion(s, Revision(s))).ToArray();
        }
        private void ValidateInputs(long tick, IReadOnlyList<ScheduledInput> inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            ulong last = world.InputCursor;
            foreach (var input in inputs)
            {
                if (input == null || input.AcceptedTick < 0 || input.AcceptedTick >= tick || input.ApplyTick < tick ||
                    !Enum.IsDefined(typeof(InputKind), input.Kind) || input.LogIndex == 0 || input.LogIndex <= last || input.Orders.Any(o => o == null)
                    || (input.Kind == InputKind.Economy) != (input.Economy != null) || input.Economy != null && (input.Economy.FactionId < 1 || input.Economy.FactionId > 2))
                    throw new ArgumentException("Inputs must be ordered, received before this tick and not past their apply tick.", nameof(inputs));
                last = input.LogIndex;
            }
        }
        private ReasonCode Payload(PolicyOrder o)
        {
            if (!ValidScope(o.Target) || o.CommandId == 0 || !Enum.IsDefined(typeof(CommandSource), o.Source) ||
                !Enum.IsDefined(typeof(PolicyKind), o.Kind) || !Enum.IsDefined(typeof(GoalKind), o.Goal.Kind) ||
                !Enum.IsDefined(typeof(EndKind), o.End.Kind) || o.Priority > 100 || o.AllowedLoss.Permille > 1000 || o.ReservePermille > 1000 ||
                o.ObservedTick < 0 || o.ObservedTick >= world.Tick || o.Expiration.ValidUntilTick < 0 || o.Expiration.MaxObservationAgeTicks < 0 ||
                (byte)o.Expiration.Flags > 7 || o.End.Tick < 0 || o.End.Kind != EndKind.AtTick && o.End.Tick != 0 ||
                o.Kind != PolicyKind.MaintainReserve && o.ReservePermille != 0) return ReasonCode.InvalidPayload;
            if (o.Goal.Kind == GoalKind.Point && (o.Goal.Id != 0 || world.Map.Cell(o.Goal.Point) < 0) ||
                o.Goal.Kind != GoalKind.Point && !SamePoint(o.Goal.Point, default) ||
                o.Goal.Kind == GoalKind.None && o.Goal.Id != 0 ||
                o.Goal.Kind == GoalKind.Outpost && (o.Goal.Id == 0 || o.Goal.Id > world.Outposts.Length) ||
                o.Goal.Kind == GoalKind.Core && (o.Goal.Id == 0 || o.Goal.Id > world.Cores.Length)) return ReasonCode.InvalidPayload;
            if ((o.Kind == PolicyKind.Focus || o.Kind == PolicyKind.Retreat) && o.Target.Kind == ScopeKind.Outpost ||
                o.Kind == PolicyKind.Focus && (o.Goal.Kind == GoalKind.None || o.Goal.Kind == GoalKind.Core && world.Cores[o.Goal.Id - 1].Definition.FactionId == o.Target.FactionId) ||
                o.Kind == PolicyKind.AllowAbandon && (o.Target.Kind != ScopeKind.Outpost || o.Goal.Kind != GoalKind.None) ||
                o.Kind == PolicyKind.MaintainReserve && (o.Target.Kind != ScopeKind.All || o.Goal.Kind != GoalKind.None) ||
                o.Kind == PolicyKind.ReturnToAuto && (o.Source != CommandSource.Human || o.Goal.Kind != GoalKind.None) ||
                o.Kind == PolicyKind.Defend && (o.Target.Kind == ScopeKind.All || o.Goal.Kind != GoalKind.Outpost && o.Goal.Kind != GoalKind.Core ||
                    o.Goal.Kind == GoalKind.Core && world.Cores[o.Goal.Id - 1].Definition.FactionId != o.Target.FactionId) ||
                o.Kind == PolicyKind.Scout && (o.Target.Kind != ScopeKind.Army || world.Armies[o.Target.Id - 1].Definition.Role != "scout" ||
                    o.Goal.Kind != GoalKind.Point && o.Goal.Kind != GoalKind.Outpost)) return ReasonCode.InvalidPayload;
            if (o.Source == CommandSource.Ai && ((o.Expiration.Flags & ExpireFlags.ObservationTooOld) == 0 || o.Expiration.MaxObservationAgeTicks < 0)) return ReasonCode.InvalidPayload;
            if (o.Parents.Any(p => !ValidScope(p.Scope) || p.Scope.FactionId != o.Target.FactionId) ||
                o.Parents.Select(p => p.Scope).Distinct().Count() != o.Parents.Count) return ReasonCode.InvalidPayload;
            return ReasonCode.None;
        }
        private ReasonCode Expired(PolicyOrder o, bool receiving)
        {
            if (world.Tick > o.Expiration.ValidUntilTick) return ReasonCode.Deadline;
            if ((o.Expiration.Flags & ExpireFlags.OwnershipChanged) != 0)
            {
                uint id = o.Target.Kind == ScopeKind.Outpost ? o.Target.Id : o.Goal.Kind == GoalKind.Outpost ? o.Goal.Id : 0;
                // Own asset loss is public to its owner. Other ownership is read only through observation.
                if (id != 0 && world.Outposts[id - 1].OwnerFactionId != o.Target.FactionId) return ReasonCode.OwnershipChanged;
            }
            if (receiving && (o.Expiration.Flags & ExpireFlags.ObservationTooOld) != 0 && world.Tick - o.ObservedTick > o.Expiration.MaxObservationAgeTicks)
                return ReasonCode.ObservationTooOld;
            return ReasonCode.None;
        }
        private ReasonCode Authority(PolicyOrder order, CommandState self)
        {
            foreach (var c in commandStates)
            {
                if (c == self || Terminal(c) || !Overlap(c, order)) continue;
                if (order.Source != CommandSource.Human && c.Order.Source == CommandSource.Human &&
                    (c.Status != CommandStatus.Executing || Field(c.Order.Kind) == Field(order.Kind))) return ReasonCode.Superseded;
                if (order.Source == CommandSource.Ai && c.Order.Source == CommandSource.Doctrine && Field(c.Order.Kind) == Field(order.Kind)) return ReasonCode.Superseded;
            }
            return ReasonCode.None;
        }
        private void Notice(CommandState c, ReasonCode reason)
        {
            commandEvents.Add(new GameEvent(world.Tick, (uint)commandEvents.Count, EventKind.CommandChanged,
                (byte)(1 << ((int)c.Order.Target.FactionId - 1)), c.Order.Target.Id, c.Order.CommandId, default, (int)c.Status, reason));
        }
        private void EndCommand(CommandState c, CommandStatus status, ReasonCode reason)
        {
            if (Terminal(c)) return;
            bool waiting = c.Status != CommandStatus.Executing;
            c.Status = status; c.Reason = reason;
            foreach (var a in c.Armies)
            {
                a.Active = false;
                if (!waiting && Combat(c.Order))
                    world.Armies[a.ArmyId - 1].AutoStartIds = world.Armies[a.ArmyId - 1].SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
            }
            if (c.Acquired && ValidScope(c.Order.Target))
            {
                // A superseded token still releases a revision, but must not revoke its human successor.
                int changedField = VersionMatches(c, new PolicyVersion(c.Order.Target, c.ExecutionRevision)) ? Field(c.Order.Kind) : 0;
                Advance(c.Order.Target, changedField);
            }
            Notice(c, reason);
            if (waiting && c.Order.BatchId != 0)
                foreach (var other in commandStates)
                    if (other != c && !Terminal(other) && other.Status != CommandStatus.Executing && other.Order.BatchId == c.Order.BatchId)
                        EndCommand(other, status, reason);
        }
        private CommandState Register(ScheduledInput input, PolicyOrder o)
        {
            var c = new CommandState { Order = o, RequestId = input.RequestId, LogIndex = input.LogIndex,
                AcceptedTick = input.AcceptedTick, ApplyTick = input.ApplyTick, DeadlineTick = input.DeadlineTick,
                Status = CommandStatus.Interpreting, ReservedArmies = Affected(o.Target) };
            commandStates.Add(c);
            return c;
        }
        private void ApplyInputs(IReadOnlyList<ScheduledInput> inputs)
        {
            foreach (var input in inputs)
            {
                if (input.LogIndex <= world.InputCursor) throw new ArgumentException("LogIndex must increase across ticks.");
                world.InputCursor = input.LogIndex;
                if (input.Kind == InputKind.Economy) { ApplyEconomyCommand(input.Economy); continue; }
                nextRequestId = Math.Max(nextRequestId, checked(input.RequestId + 1));
                foreach (var o in input.Orders) { nextCommandId = Math.Max(nextCommandId, checked(o.CommandId + 1)); nextBatchId = Math.Max(nextBatchId, checked(o.BatchId + 1)); }
                if (input.Kind == InputKind.Cancel)
                {
                    var requested = commandStates.Where(c => c.RequestId == input.RequestId).ToArray();
                    var batches = requested.Select(c => c.Order.BatchId).Where(id => id != 0).ToArray();
                    foreach (var c in commandStates.Where(c => c.RequestId == input.RequestId || batches.Contains(c.Order.BatchId)))
                        EndCommand(c, CommandStatus.Cancelled, ReasonCode.UserCancelled);
                    continue;
                }
                if (input.ResolutionReason != ReasonCode.None)
                {
                    foreach (var c in commandStates.Where(c => c.RequestId == input.RequestId))
                        if (Terminal(c)) Notice(c, input.ResolutionReason);
                        else EndCommand(c, input.ResolutionReason == ReasonCode.Deadline ? CommandStatus.Expired : CommandStatus.Impossible, input.ResolutionReason);
                    continue;
                }
                var group = new List<CommandState>();
                ReasonCode failure = ReasonCode.None;
                foreach (var o in input.Orders)
                {
                    var c = commandStates.Find(v => v.Order.CommandId == o.CommandId);
                    if (c != null && (Terminal(c) || input.Kind != InputKind.Resolve || c.Status != CommandStatus.Interpreting || c.RequestId != input.RequestId))
                    { Notice(c, ReasonCode.StaleVersion); failure = CombineFailure(failure, ReasonCode.StaleVersion); continue; }
                    if (c == null)
                    {
                        c = Register(input, o);
                        if (input.Kind == InputKind.Resolve) failure = CombineFailure(failure, ReasonCode.StaleVersion);
                    }
                    group.Add(c);
                    var invalid = Payload(o);
                    if (input.Orders.Count(v => v.CommandId == o.CommandId) != 1) invalid = ReasonCode.InvalidPayload;
                    if (invalid != ReasonCode.None) failure = CombineFailure(failure, invalid);
                    if (c.Acquired && world.Tick > c.Order.Expiration.ValidUntilTick) failure = CombineFailure(failure, ReasonCode.Deadline);
                    if (input.Kind == InputKind.Reserve)
                    {
                        if (o.Source != CommandSource.Human) failure = CombineFailure(failure, ReasonCode.InvalidPayload);
                    }
                    else if (input.Kind == InputKind.Resolve)
                    {
                        if (o.Source != CommandSource.Human || !c.Order.Target.Equals(o.Target) || c.Order.BatchId != o.BatchId || o.TargetRevision != c.ExecutionRevision || !VersionMatches(c, new PolicyVersion(o.Target, c.ExecutionRevision)))
                            failure = CombineFailure(failure, ReasonCode.StaleVersion);
                        if (c.Dependencies.Any(v => !VersionMatches(c, v)) || !c.ReservedArmies.SequenceEqual(Affected(o.Target))) failure = CombineFailure(failure, ReasonCode.StaleVersion);
                        if (world.Tick - 1 > c.DeadlineTick) failure = CombineFailure(failure, ReasonCode.Deadline);
                    }
                    else
                    {
                        if (o.Source == CommandSource.Human) failure = CombineFailure(failure, ReasonCode.InvalidPayload);
                        if (o.TargetRevision != Revision(o.Target)) failure = CombineFailure(failure, ReasonCode.StaleVersion);
                        foreach (var v in Versions(o.Target))
                            if (!v.Scope.Equals(o.Target) && !o.Parents.Any(p => p.Scope.Equals(v.Scope) && p.Revision == v.Revision)) failure = CombineFailure(failure, ReasonCode.StaleVersion);
                        foreach (var p in o.Parents) if (Revision(p.Scope) != p.Revision) failure = CombineFailure(failure, ReasonCode.StaleVersion);
                    }
                    if (invalid == ReasonCode.None)
                    {
                        var expired = Expired(o, true); if (expired != ReasonCode.None) failure = CombineFailure(failure, expired);
                        var authority = Authority(o, c); if (authority != ReasonCode.None) failure = CombineFailure(failure, authority);
                    }
                }
                if (failure != ReasonCode.None)
                {
                    foreach (var c in group) EndCommand(c, failure == ReasonCode.InvalidPayload ? CommandStatus.Impossible : CommandStatus.Expired, failure);
                    continue;
                }
                var acquiredScopes = new List<ScopeKey>();
                foreach (var c in group)
                {
                    var o = input.Orders.First(v => v.CommandId == c.Order.CommandId);
                    if (input.Kind != InputKind.Resolve)
                    {
                        if (!acquiredScopes.Contains(o.Target)) { Advance(o.Target, Field(o.Kind)); acquiredScopes.Add(o.Target); }
                        c.ExecutionRevision = Revision(o.Target); c.Acquired = true;
                    }
                    c.Order = o; c.ApplyTick = input.ApplyTick;
                    c.Status = input.Kind == InputKind.Reserve ? CommandStatus.Interpreting : CommandStatus.Pending;
                    Notice(c, ReasonCode.None);
                }
                if (input.Kind == InputKind.Reserve)
                {
                    foreach (var replacement in group)
                    foreach (var old in commandStates)
                        if (!group.Contains(old) && !Terminal(old) && old.Status != CommandStatus.Executing &&
                            old.Order.Source == CommandSource.Human && old.LogIndex < replacement.LogIndex &&
                            Field(old.Order.Kind) == Field(replacement.Order.Kind) && Overlap(old, replacement.Order))
                            EndCommand(old, CommandStatus.Cancelled, ReasonCode.Superseded);
                }
                if (input.Kind != InputKind.Resolve) foreach (var c in group) c.Dependencies = Versions(c.Order.Target).ToArray();
            }
        }
        private bool Combat(PolicyOrder o) => o.Kind == PolicyKind.Focus || o.Kind == PolicyKind.Retreat || o.Kind == PolicyKind.Defend || o.Kind == PolicyKind.Scout;
        private SimPoint Destination(PolicyOrder o) => o.Goal.Kind == GoalKind.None
            ? world.Cores[world.Factions[o.Target.FactionId - 1].CoreId - 1].Definition.Position : GoalPosition(o.Goal);
        private ReasonCode CanStart(CommandState c)
        {
            var o = c.Order;
            var expired = Expired(o, true); if (expired != ReasonCode.None) return expired;
            if (!VersionMatches(c, new PolicyVersion(o.Target, c.ExecutionRevision)) || c.Dependencies.Any(v => !VersionMatches(c, v))) return ReasonCode.StaleVersion;
            if (!c.ReservedArmies.SequenceEqual(Affected(o.Target))) return ReasonCode.StaleVersion;
            var authority = Authority(o, c); if (authority != ReasonCode.None) return authority;
            if (!Combat(o)) return ReasonCode.None;
            bool any = false;
            foreach (uint id in Affected(o.Target))
            {
                var soldiers = world.Armies[id - 1].SoldierIds.Where(s => world.Soldiers[s - 1].Alive).ToArray();
                if (soldiers.Length == 0) return ReasonCode.EmptyArmy;
                any = true;
                var goal = Destination(o);
                var radius = o.Goal.Kind == GoalKind.Outpost ? world.Config.Rules.CaptureRadius :
                    o.Goal.Kind == GoalKind.Core && o.Kind == PolicyKind.Focus ? world.Config.Rules.CoreRadius + world.Soldiers[soldiers[0] - 1].Parameters.Range : Fix64.FromInt(4);
                {
                    var path = world.Map.FindPath(world.Map.Cell(world.Soldiers[soldiers[0] - 1].Position), goal);
                    if (path.Length == 0 || !InRange(world.Map.Center(path[path.Length - 1]), goal, radius)) return ReasonCode.NoPath;
                }
            }
            return any ? ReasonCode.None : ReasonCode.EmptyArmy;
        }
        private void ApplyPendingCommands()
        {
            var due = commandStates.Where(c => c.Status == CommandStatus.Pending && c.ApplyTick <= world.Tick)
                .OrderBy(c => c.ApplyTick).ThenBy(c => c.LogIndex).ThenBy(c => c.Order.CommandId).ToArray();
            var done = new List<CommandState>();
            foreach (var first in due)
            {
                if (done.Contains(first) || Terminal(first)) continue;
                var batch = first.Order.BatchId == 0 ? new[] { first } : commandStates.Where(c => c.Order.BatchId == first.Order.BatchId).ToArray();
                done.AddRange(batch);
                ReasonCode failure = ReasonCode.None;
                foreach (var c in batch)
                {
                    var reason = c.Status != CommandStatus.Pending || c.ApplyTick != first.ApplyTick ? ReasonCode.StaleVersion : CanStart(c);
                    if (reason != ReasonCode.None && (failure == ReasonCode.None || FailureRank(reason) < FailureRank(failure))) failure = reason;
                }
                if (failure != ReasonCode.None)
                {
                    foreach (var c in batch) EndCommand(c, FailureRank(failure) == 0 ? CommandStatus.Expired : CommandStatus.Impossible, failure);
                    continue;
                }
                foreach (var c in batch) StartCommand(c);
                ComposePolicies();
            }
        }
        private static int FailureRank(ReasonCode reason) => reason == ReasonCode.NoPath || reason == ReasonCode.EmptyArmy || reason == ReasonCode.InvalidPayload ? 1 : 0;
        private static ReasonCode CombineFailure(ReasonCode current, ReasonCode next) => current == ReasonCode.None ||
            next != ReasonCode.None && FailureRank(next) < FailureRank(current) ? next : current;
        private void StartCommand(CommandState c)
        {
            var o = c.Order;
            var affected = Affected(o.Target);
            foreach (var old in commandStates.ToArray())
            {
                if (old == c || Terminal(old) || !Overlap(old, o)) continue;
                bool reset = o.Kind == PolicyKind.ReturnToAuto && old.Order.Source == CommandSource.Human;
                bool replace = Field(old.Order.Kind) == Field(o.Kind) && (o.Source < old.Order.Source || o.Source == old.Order.Source && c.LogIndex > old.LogIndex);
                if (!reset && !replace) continue;
                if (old.Status == CommandStatus.Executing && o.Target.Kind != ScopeKind.All && !old.Order.Target.Equals(o.Target))
                {
                    foreach (var a in old.Armies) if (affected.Contains(a.ArmyId)) a.Active = false;
                    if (old.Armies.Any(a => a.Active)) continue;
                }
                EndCommand(old, CommandStatus.Cancelled, ReasonCode.Superseded);
            }
            c.Status = CommandStatus.Executing;
            foreach (uint id in affected)
            {
                uint[] ids = world.Armies[id - 1].SoldierIds.Where(s => world.Soldiers[s - 1].Alive).OrderBy(s => s).ToArray();
                
                if (Combat(o))
                {
                    world.Armies[id - 1].Decision.Returning = false;
                    world.Armies[id - 1].Decision.InferiorSince = 0;
                }
                c.Armies.Add(new ArmyExecution { ArmyId = id, StartIds = ids, N0 = ids.Length });
            }
            Notice(c, ReasonCode.None);
            if (o.Kind == PolicyKind.ReturnToAuto) EndCommand(c, CommandStatus.Completed, ReasonCode.None);
        }
        private void ComposePolicies()
        {
            foreach (int index in world.ArmyTraversal)
            {
                uint armyId = world.Armies[index].Definition.Id;
                var selected = commandStates.Where(c => c.Status == CommandStatus.Executing &&
                    Combat(c.Order) && c.Armies.Any(e => e.ArmyId == armyId && e.Active && !e.Finished))
                    .OrderBy(c => c.Order.Source).ThenByDescending(c => c.LogIndex).FirstOrDefault();
                ref var a = ref world.Armies[index];
                var policy = selected == null ? (lossReturns.Contains(armyId) ? PolicyKind.Retreat : (PolicyKind)0) : selected.Order.Kind;
                var goal = selected == null ? default : selected.Order.Goal;
                if (selected != null && selected.Armies.Any(e => e.ArmyId == armyId && e.Returning)) { policy = PolicyKind.Retreat; goal = default; }
                if (a.Policy != policy || a.Goal.Kind != goal.Kind || a.Goal.Id != goal.Id || !SamePoint(a.Goal.Point, goal.Point))
                {
                    a.HasPathGoal = false;
                }
                if (a.CommandId != (selected?.Order.CommandId ?? 0) && (a.Policy == PolicyKind.Scout || policy == PolicyKind.Scout))
                    foreach (uint soldier in a.SoldierIds) world.Soldiers[soldier - 1].Pursuit = default;
                a.Policy = policy; a.Goal = goal;
                a.CommandId = selected?.Order.CommandId ?? 0; a.LogIndex = selected?.LogIndex ?? 0;
                a.AcceptedTick = selected?.AcceptedTick ?? 0; a.ApplyTick = selected?.ApplyTick ?? 0;
            }
        }
        private void FinishCommands()
        {
            lossReturns.RemoveAll(id =>
            {
                var army = world.Armies[id - 1];
                var goal = world.Cores[world.Factions[army.Definition.FactionId - 1].CoreId - 1].Definition.Position;
                bool arrived = army.SoldierIds.Where(s => world.Soldiers[s - 1].Alive).All(s => InRange(world.Soldiers[s - 1].Position, goal, Fix64.FromInt(4)));
                if (arrived) HoldArmy(id);
                return arrived;
            });
            foreach (var c in commandStates)
            {
                if (Terminal(c)) continue;
                if (c.Status == CommandStatus.Interpreting && world.Tick > c.DeadlineTick)
                { EndCommand(c, CommandStatus.Expired, ReasonCode.Deadline); continue; }
                var expired = Expired(c.Order, false);
                if (expired != ReasonCode.None) { EndCommand(c, CommandStatus.Expired, expired); continue; }
                if (c.Status != CommandStatus.Executing) continue;
                bool complete = true, loss = false;
                foreach (var a in c.Armies.Where(a => a.Active))
                {
                    if (a.Finished) continue;
                    a.Deaths = a.StartIds.Count(id => !world.Soldiers[id - 1].Alive);
                    var live = world.Armies[a.ArmyId - 1].SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
                    if (Combat(c.Order) && live.Length == 0)
                    {
                        a.Failed = true; a.Active = false;
                        if (c.Reason != ReasonCode.EmptyArmy) { c.Reason = ReasonCode.EmptyArmy; Notice(c, c.Reason); }
                        continue;
                    }
                    bool reached = a.N0 > 0 && 1000L * a.Deaths >= (long)c.Order.AllowedLoss.Permille * a.N0;
                    loss |= reached;
                    if (reached && Combat(c.Order) && c.Order.End.Kind != EndKind.LossReached) a.Returning = true;
                    var scoutReturning = c.Order.Kind == PolicyKind.Scout && live.Any(id => world.Soldiers[id - 1].Pursuit.Returning);
                    var goal = a.Returning || scoutReturning ? world.Cores[world.Factions[c.Order.Target.FactionId - 1].CoreId - 1].Definition.Position : Destination(c.Order);
                    bool arrived = live.Length > 0 && live.All(id => InRange(world.Soldiers[id - 1].Position, goal, Fix64.FromInt(4)));
                    a.Finished = a.Returning ? arrived : c.Order.End.Kind == EndKind.Arrived ? arrived :
                        c.Order.End.Kind == EndKind.AtTick ? world.Tick >= c.Order.End.Tick :
                        c.Order.End.Kind == EndKind.LossReached ? reached :
                        (c.Order.End.Kind == EndKind.ObjectiveOwned || c.Order.Kind == PolicyKind.Focus) && c.Order.Goal.Kind == GoalKind.Outpost && world.Outposts[c.Order.Goal.Id - 1].OwnerFactionId == c.Order.Target.FactionId;
                    if (a.Finished && reached && c.Order.End.Kind == EndKind.LossReached &&
                        Combat(c.Order) && !lossReturns.Contains(a.ArmyId)) lossReturns.Add(a.ArmyId);
                    if (a.Finished && (a.Returning || c.Order.Kind == PolicyKind.Retreat))
                        HoldArmy(a.ArmyId);
                    complete &= a.Finished;
                }
                if (c.Armies.Any(a => a.Failed) && c.Armies.All(a => a.Failed || !a.Active || a.Finished)) EndCommand(c, CommandStatus.Impossible, ReasonCode.EmptyArmy);
                else if ((c.Armies.Count != 0 && complete) || c.Order.End.Kind == EndKind.AtTick && world.Tick >= c.Order.End.Tick && !c.Armies.Any(a => a.Returning && !a.Finished))
                    EndCommand(c, CommandStatus.Completed, loss ? ReasonCode.LossLimit : ReasonCode.None);
            }
        }
    }
}
