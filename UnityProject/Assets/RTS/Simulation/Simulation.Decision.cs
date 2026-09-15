using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private long lastAllocationTick;
        private readonly int[] reserveShortfall = new int[2];
        private AttackMemory[][] attackMemory;
        private ContactApproachMemory[][] approachMemory;
        // One state per faction: unlike the old per-army target timer this describes a shared attack.
        private readonly FactionOffenseMemory[] offenseMemory = { new FactionOffenseMemory(), new FactionOffenseMemory() };

        private PolicyView ArmyPolicy(uint id)
        {
            var a = world.Armies[id - 1];
            var c = commandStates.FirstOrDefault(v => v.Order.CommandId == a.CommandId);
            return c == null ? default : new PolicyView(c.Order.CommandId, c.Order.Source, a.Policy, a.Goal, c.Order.AllowedLoss, c.Order.ReservePermille);
        }
        private bool HomeArrived(ArmyState a)
        {
            var live = a.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
            var home = world.Cores[world.Factions[a.Definition.FactionId - 1].CoreId - 1].Definition.Position;
            return live.Length > 0 && live.All(id => InRange(world.Soldiers[id - 1].Position, home, Fix64.FromInt(4)));
        }
        private void HoldArmy(uint id)
        {
            ref var a = ref world.Armies[id - 1];
            a.Decision.HoldUntilTick = checked(world.Tick + 60);
            a.Decision.Assignment = AssignmentKind.Reserve;
            a.Decision.Goal = new PolicyGoal(GoalKind.Core, world.Factions[a.Definition.FactionId - 1].CoreId, default);
            a.AutoStartIds = a.SoldierIds.Where(s => world.Soldiers[s - 1].Alive).ToArray();
        }
        private void DecideArmies()
        {
            if (attackMemory == null) attackMemory = new[] { new AttackMemory[world.Outposts.Length], new AttackMemory[world.Outposts.Length] };
            if (approachMemory == null) approachMemory = new[] { Array.Empty<ContactApproachMemory>(), Array.Empty<ContactApproachMemory>() };
            for (uint f = 1; f <= 2; f++)
            {
                var observation = frames[f - 1].Observation;
                // A* is invoked only for the finite visible-infantry × outpost set on this tick.
                var approachRoutes = new List<ContactApproachRoute>();
                foreach (var post in observation.Objectives.Where(o => o.Kind == GoalKind.Outpost).OrderBy(o => o.Id))
                foreach (var contact in observation.Contacts.Where(c => c.IsCurrentlyVisible && observation.VisibleEnemies.Any(e => e.ContactId == c.ContactId && e.Kind == (byte)UnitKind.Infantry)).OrderBy(c => c.ContactId))
                {
                    var path = world.Map.FindPath(world.Map.Cell(contact.LastPosition), post.Position);
                    int distance = path.Length == 0 || !InRange(world.Map.Center(path[path.Length - 1]), post.Position, Fix64.FromInt(4)) ? int.MaxValue : path.Length - 1;
                    approachRoutes.Add(new ContactApproachRoute(contact.ContactId, post.Id, distance));
                }
                approachMemory[f - 1] = PolicyDecision.UpdateApproaches(observation, approachMemory[f - 1], approachRoutes);
                foreach (var post in observation.Objectives.Where(o => o.Kind == GoalKind.Outpost).OrderBy(o => o.Id))
                    attackMemory[f - 1][post.Id - 1] = PolicyDecision.ObserveAttack(observation, post, attackMemory[f - 1][post.Id - 1]);
                foreach (var view in observation.OwnArmies.OrderBy(a => a.Id))
                {
                    ref var a = ref world.Armies[view.Id - 1];
                    if (a.AutoStartIds == null) a.AutoStartIds = a.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
                    int deaths = a.AutoStartIds.Count(id => !world.Soldiers[id - 1].Alive);
                    bool wasReturning = a.Decision.Returning;
                    a.Decision = PolicyDecision.AssessRetreat(observation, view, ArmyPolicy(view.Id), a.Decision, world.Tick,
                        a.AutoStartIds.Length > 0 && 1000L * deaths >= 300L * a.AutoStartIds.Length, HomeArrived(a));
                    if (wasReturning && !a.Decision.Returning) a.AutoStartIds = a.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
                    foreach (var c in commandStates.Where(c => c.Status == CommandStatus.Executing && c.Order.Kind == PolicyKind.Scout))
                        foreach (var execution in c.Armies.Where(e => e.ArmyId == view.Id && e.Active && !e.Finished))
                            if (a.SoldierIds.Any(id => world.Soldiers[id - 1].Alive &&
                                PolicyDecision.ScoutSeesEnemy(observation, world.Soldiers[id - 1].Position, world.Soldiers[id - 1].Parameters.Vision))) execution.Returning = true;
                }
                if (world.Tick % 20 != 0) continue;
                var inputs = new List<ArmyDecisionInput>();
                foreach (var view in observation.OwnArmies.OrderBy(a => a.Id))
                {
                    var a = world.Armies[view.Id - 1];
                    uint first = a.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).DefaultIfEmpty(0U).First();
                    var start = first == 0 ? world.Map.Cell(view.Position) : world.Map.Cell(world.Soldiers[first - 1].Position);
                    var routes = new List<ObjectiveRoute>();
                    foreach (var o in observation.Objectives.OrderBy(o => o.Kind).ThenBy(o => o.Id))
                    {
                        var path = world.Map.FindPath(start, o.Position);
                        int distance = path.Length == 0 || !InRange(world.Map.Center(path[path.Length - 1]), o.Position, Fix64.FromInt(4)) ? int.MaxValue : path.Length - 1;
                        routes.Add(new ObjectiveRoute(new PolicyGoal(o.Kind, o.Id, default), distance, path.Select(world.Map.Center).ToArray()));
                    }
                    inputs.Add(new ArmyDecisionInput(view, ArmyPolicy(view.Id), a.Definition.Role == "reserve", a.Decision, routes));
                }
                var policies = commandStates.Where(c => c.Status == CommandStatus.Executing && c.Order.Target.FactionId == f).OrderBy(c => c.Order.Source).ThenByDescending(c => c.LogIndex).ToArray();
                ushort reserve = policies.Where(c => c.Order.Kind == PolicyKind.MaintainReserve).Select(c => c.Order.ReservePermille).DefaultIfEmpty((ushort)100).First();
                var abandoned = policies.Where(c => c.Order.Kind == PolicyKind.AllowAbandon).Select(c => c.Order.Target.Id).OrderBy(id => id).ToArray();
                var allocations = PolicyDecision.Allocate(observation, world.Tick, inputs, reserve, abandoned, attackMemory[f - 1], policies.Select(c => c.Order).ToArray(), out reserveShortfall[f - 1], approachMemory[f - 1]);
                CoordinateOffense(f, observation, inputs, allocations);
                if (reserveShortfall[f - 1] > 0)
                    commandEvents.Add(new GameEvent(world.Tick, (uint)commandEvents.Count, EventKind.AiReport,
                        (byte)(1 << ((int)f - 1)), f, 0, default, reserveShortfall[f - 1], ReasonCode.ReserveShortfall));
                for (int i = 0; i < inputs.Count; i++)
                {
                    ref var a = ref world.Armies[inputs[i].Army.Id - 1];
                    var old = a.Decision; a.Decision = allocations[i];
                    if (old.Goal.Kind != a.Decision.Goal.Kind || old.Goal.Id != a.Decision.Goal.Id || old.Assignment != a.Decision.Assignment)
                    {
                        a.HasPathGoal = false;
                        Advance(new ScopeKey(f, ScopeKind.Army, a.Definition.Id), 0);
                    }
                }
            }
            if (world.Tick % 20 == 0) lastAllocationTick = world.Tick;
        }

        // Navigation remains Simulation-owned; all choices below use only the faction frame passed in.
        // A Point goal is the rally point while gathering, then the shared objective when advancing.
        private void CoordinateOffense(uint faction, FactionObservation observation, List<ArmyDecisionInput> inputs, ArmyDecisionMemory[] allocations)
        {
            var state = offenseMemory[faction - 1];
            var eligible = Enumerable.Range(0, inputs.Count).Where(i => inputs[i].Army.Kind == UnitKind.Infantry &&
                inputs[i].Policy.Kind == 0 && !allocations[i].Returning && allocations[i].Assignment == AssignmentKind.Advance).ToArray();
            if (eligible.Length == 0) { state.Phase = OffensivePhase.Idle; state.PlannedArmyIds = Array.Empty<uint>(); state.JoiningArmyIds = Array.Empty<uint>(); state.AdvancingArmyIds = Array.Empty<uint>(); return; }
            bool stillEnemy = state.Goal.Kind == GoalKind.Core || observation.Objectives.Any(o => o.Kind == GoalKind.Outpost && o.Id == state.Goal.Id && (!o.IsOwnerKnown || o.OwnerFactionId != faction));
            if (state.Phase == OffensivePhase.Idle || !stillEnemy || (state.Phase == OffensivePhase.WaitingToAdvance && world.Tick - state.GatheredTick >= 600))
            {
                if (state.Goal.Kind != GoalKind.None && state.Phase == OffensivePhase.WaitingToAdvance)
                    state.SuppressedGoals = state.SuppressedGoals.Concat(new[] { new SuppressedGoalMemory { Goal = state.Goal, UntilTick = checked(world.Tick + 600) } }).ToArray();
                var goals = observation.Objectives.Where(o => o.Kind == GoalKind.Outpost && (!o.IsOwnerKnown || o.OwnerFactionId != faction))
                    .Select(o => new PolicyGoal(o.Kind, o.Id, default)).ToList();
                if (observation.Objectives.Any(o => o.Kind == GoalKind.Outpost && o.IsOwnerKnown && o.OwnerFactionId == faction)) goals.Add(PolicyDecision.Core(observation, false));
                var choice = goals.Where(g => !state.SuppressedGoals.Any(s => s.Goal.Kind == g.Kind && s.Goal.Id == g.Id && world.Tick < s.UntilTick))
                    .Select(g => new { Goal = g, Routes = eligible.Select(i => inputs[i].Routes.FirstOrDefault(r => r.Goal.Kind == g.Kind && r.Goal.Id == g.Id)).ToArray() })
                    .Where(x => x.Routes.All(r => r.Goal.Kind == x.Goal.Kind && r.Goal.Id == x.Goal.Id && r.Distance != int.MaxValue))
                    .OrderBy(x => PolicyDecision.Estimate(observation, PolicyDecision.Position(observation, x.Goal)) * 1000 / Math.Max(1, eligible.Sum(i => inputs[i].Army.AliveCount)))
                    .ThenBy(x => x.Routes.Max(r => r.Distance)).ThenBy(x => x.Goal.Kind == GoalKind.Outpost ? 0 : 1).ThenBy(x => x.Goal.Id).FirstOrDefault();
                if (choice == null) { state.Phase = OffensivePhase.Idle; return; }
                var own = PolicyDecision.Position(observation, PolicyDecision.Core(observation, true));
                var path = world.Map.FindPath(world.Map.Cell(own), PolicyDecision.Position(observation, choice.Goal));
                if (path.Length == 0) { state.Phase = OffensivePhase.Idle; return; }
                int cursor = path.Length - 1, travelled = 0;
                while (cursor > 0 && travelled < 40) { travelled++; cursor--; }
                // Visible enemies make the rally point retreat towards home, never towards hidden state.
                while (cursor > 0 && observation.VisibleEnemies.Any(e => PolicyDecision.Within(e.Position, world.Map.Center(path[cursor]), 24))) cursor--;
                state.Id = checked(state.Id + 1); state.Goal = choice.Goal; state.RallyPoint = world.Map.Center(path[cursor]); state.Phase = OffensivePhase.Gathering;
                state.StartedTick = world.Tick; state.MaintainedSinceTick = world.Tick; state.GatheredTick = 0;
                state.PlannedArmyIds = eligible.Select(i => inputs[i].Army.Id).ToArray(); state.JoiningArmyIds = Array.Empty<uint>(); state.AdvancingArmyIds = Array.Empty<uint>();
                state.MoveDeadlineTick = checked(world.Tick + choice.Routes.Max(r => r.Distance) * 20 + 200);
            }
            var planned = state.PlannedArmyIds.Where(id => inputs.Any(i => i.Army.Id == id)).ToArray();
            if (state.Phase == OffensivePhase.Gathering && world.Tick > state.MoveDeadlineTick)
                planned = planned.Where(id => ArmyAtRally(id, state.RallyPoint, 12)).ToArray();
            state.PlannedArmyIds = planned;
            bool gathered = planned.Length != 0 && planned.All(id => ArmyAtRally(id, state.RallyPoint, 12));
            if (state.Phase == OffensivePhase.Gathering && gathered) { state.Phase = OffensivePhase.WaitingToAdvance; state.GatheredTick = world.Tick; }
            int near = inputs.Where(i => state.PlannedArmyIds.Contains(i.Army.Id)).Sum(i => ArmyNearRally(i.Army.Id, state.RallyPoint, 24));
            if (state.Phase == OffensivePhase.WaitingToAdvance && near * 2 >= PolicyDecision.Estimate(observation, state.RallyPoint))
            { state.Phase = OffensivePhase.Advancing; state.AdvancingArmyIds = state.PlannedArmyIds.Where(id => ArmyNearRally(id, state.RallyPoint, 24) > 0).ToArray(); }
            foreach (int i in eligible)
            {
                bool advance = state.Phase == OffensivePhase.Advancing && state.AdvancingArmyIds.Contains(inputs[i].Army.Id);
                allocations[i].Goal = advance ? state.Goal : new PolicyGoal(GoalKind.Point, 0, state.RallyPoint);
            }
        }
        private bool ArmyAtRally(uint armyId, SimPoint rally, int radius)
        {
            var live = world.Armies[armyId - 1].SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
            return live.Length != 0 && live.Count(id => PolicyDecision.Within(world.Soldiers[id - 1].Position, rally, radius)) * 2 > live.Length;
        }
        private int ArmyNearRally(uint armyId, SimPoint rally, int radius)
            => world.Armies[armyId - 1].SoldierIds.Count(id => world.Soldiers[id - 1].Alive && PolicyDecision.Within(world.Soldiers[id - 1].Position, rally, radius));
        private void WriteDecision(StateWriter w)
        {
            w.Value("Ai.LastAllocationTick", lastAllocationTick);
            for (int f = 0; f < 2; f++)
            {
                string n = "Ai.Factions[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "ReserveShortfall", reserveShortfall[f]);
                for (int i = 0; i < world.Outposts.Length; i++)
                {
                    var m = attackMemory == null ? default : attackMemory[f][i];
                    string p = n + "Attacks[" + (i + 1).ToString(CultureInfo.InvariantCulture) + "].";
                    w.Value(p + "OutpostId", m.OutpostId); w.Value(p + "LastAttackTick", m.LastAttackTick);
                    w.Value(p + "LastSeenTick", m.LastSeenTick); w.Value(p + "AlertUntilTick", m.AlertUntilTick);
                    w.Value(p + "Visible", m.Visible); w.Value(p + "HasAttack", m.HasAttack);
                }
                var approaches = approachMemory == null ? Array.Empty<ContactApproachMemory>() : approachMemory[f];
                for (int i = 0; i < approaches.Length; i++)
                {
                    var m = approaches[i]; string p = n + "Approaches[" + i.ToString(CultureInfo.InvariantCulture) + "].";
                    w.Value(p + "ContactId", m.ContactId); w.Value(p + "ObjectiveId", m.ObjectiveId);
                    w.Point(p + "PreviousPosition", m.PreviousPosition); w.Value(p + "PreviousDistance", m.PreviousDistance);
                    w.Value(p + "LastSeenTick", m.LastSeenTick); w.Value(p + "ThreatUntilTick", m.ThreatUntilTick); w.Value(p + "HasPrevious", m.HasPrevious);
                }
                var offense = offenseMemory[f]; string q = n + "Offense.";
                w.Value(q + "Id", offense.Id); w.Goal(q + "Goal", offense.Goal); w.Value(q + "Phase", (byte)offense.Phase);
                w.Point(q + "RallyPoint", offense.RallyPoint); w.Value(q + "StartedTick", offense.StartedTick);
                w.Value(q + "MoveDeadlineTick", offense.MoveDeadlineTick); w.Value(q + "GatheredTick", offense.GatheredTick); w.Value(q + "MaintainedSinceTick", offense.MaintainedSinceTick);
                w.Ids(q + "Planned", offense.PlannedArmyIds.ToArray()); w.Ids(q + "Joining", offense.JoiningArmyIds.ToArray());
                w.Ids(q + "Advancing", offense.AdvancingArmyIds.ToArray()); w.Ids(q + "CommittedReserve", offense.CommittedReserveArmyIds.ToArray());
                for (int j = 0; j < offense.SuppressedGoals.Count; j++) { w.Goal(q + "Suppressed[" + j.ToString(CultureInfo.InvariantCulture) + "].Goal", offense.SuppressedGoals[j].Goal); w.Value(q + "Suppressed[" + j.ToString(CultureInfo.InvariantCulture) + "].Until", offense.SuppressedGoals[j].UntilTick); }
            }
            foreach (var a in world.Armies)
            {
                string n = "Ai.Armies[" + a.Definition.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Assignment", (byte)a.Decision.Assignment); w.Goal(n + "Goal", a.Decision.Goal);
                w.Value(n + "Returning", a.Decision.Returning); w.Value(n + "InferiorSince", a.Decision.InferiorSince);
                w.Value(n + "HoldUntilTick", a.Decision.HoldUntilTick); w.Ids(n + "StartIds", a.AutoStartIds ?? Array.Empty<uint>());
                w.Value(n + "OffensiveSince", a.Decision.OffensiveSince); w.Value(n + "SuppressUntilTick", a.Decision.SuppressUntilTick); w.Goal(n + "SuppressedGoal", a.Decision.SuppressedGoal);
            }
            for (int i = 0; i < world.SoldierCount; i++)
            {
                var s = world.Soldiers[i];
                string n = "Ai.Soldiers[" + s.Initial.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Pursuit.Active", s.Pursuit.Active); w.Value(n + "Pursuit.Returning", s.Pursuit.Returning);
                w.Point(n + "Pursuit.Start", s.Pursuit.Start); w.Point(n + "Pursuit.Mission", s.Pursuit.Mission);
                w.Value(n + "Pursuit.StartTick", s.Pursuit.StartTick);
            }
        }
    }
}
