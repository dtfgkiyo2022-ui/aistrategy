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
                var observation = ObserveForDecision(f);
                // A* is invoked only for the finite visible-infantry × outpost set on this tick.
                var approachRoutes = new List<ContactApproachRoute>();
                foreach (var post in observation.Objectives.Where(o => o.Kind == GoalKind.Outpost).OrderBy(o => o.Id))
                foreach (var contact in observation.Contacts.Where(c => !c.IsArmyContact && c.IsCurrentlyVisible && observation.VisibleEnemies.Any(e => e.ContactId == c.ContactId && e.Kind == (byte)UnitKind.Infantry)).OrderBy(c => c.ContactId))
                {
                    var path = world.Map.FindPath(world.Map.Cell(contact.LastPosition), post.Position);
                    int distance = path.Length == 0 || !InRange(world.Map.Center(path[path.Length - 1]), post.Position, Fix64.FromInt(4)) ? int.MaxValue : checked((int)(OffenseDecision.Length(path.Select(world.Map.Center).ToArray()) / Fix64.FromInt(1).Raw));
                    approachRoutes.Add(new ContactApproachRoute(contact.ContactId, post.Id, distance));
                }
                approachMemory[f - 1] = PolicyDecision.UpdateApproaches(observation, approachMemory[f - 1], approachRoutes);
                foreach (var post in observation.Objectives.Where(o => o.Kind == GoalKind.Outpost).OrderBy(o => o.Id))
                    attackMemory[f - 1][post.Id - 1] = PolicyDecision.ObserveAttack(observation, post, attackMemory[f - 1][post.Id - 1]);
                foreach (var view in observation.OwnArmies.OrderBy(a => a.Id))
                {
                    ref var a = ref world.Armies[view.Id - 1];
                    if (a.AutoStartIds == null) a.AutoStartIds = a.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();

                }

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
                        int distance = path.Length == 0 || !InRange(world.Map.Center(path[path.Length - 1]), o.Position, Fix64.FromInt(4)) ? int.MaxValue : checked((int)(OffenseDecision.Length(path.Select(world.Map.Center).ToArray()) / Fix64.FromInt(1).Raw));
                        routes.Add(new ObjectiveRoute(new PolicyGoal(o.Kind, o.Id, default), distance, path.Select(world.Map.Center).ToArray()));
                    }
                    inputs.Add(new ArmyDecisionInput(view, ArmyPolicy(view.Id), a.Definition.Role == "reserve", a.Decision, routes));
                }
                var policies = commandStates.Where(c => c.Status == CommandStatus.Executing && c.Order.Target.FactionId == f).OrderBy(c => c.Order.Source).ThenByDescending(c => c.LogIndex).ToArray();
                ushort reserve = policies.Where(c => c.Order.Kind == PolicyKind.MaintainReserve).Select(c => c.Order.ReservePermille).DefaultIfEmpty((ushort)100).First();
                var abandoned = policies.Where(c => c.Order.Kind == PolicyKind.AllowAbandon).Select(c => c.Order.Target.Id).OrderBy(id => id).ToArray();
                var own = inputs.Select(i => {
                    var a = world.Armies[i.Army.Id - 1];
                    var live = a.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).OrderBy(id => id).ToArray();
                    return new OffenseArmyInput(i.Army.Id, live.Select(id => world.Soldiers[id - 1].Position).ToArray(), live.Length == 0 ? 0 : world.Soldiers[live[0] - 1].StepDistance.Raw,
                        false, HomeArrived(a), a.AutoStartIds.Length, a.AutoStartIds.Count(id => !world.Soldiers[id - 1].Alive));
                }).ToArray();
                var offenseRoutes = OffenseRoutes(observation, inputs);
                var assessed = inputs.Select(i => i.Memory).ToArray();
                OffenseDecision.Retreat(observation, world.Tick, offenseMemory[f - 1], inputs, own, assessed, offenseRoutes.FirstOrDefault(r => r.Goal.Kind == offenseMemory[f - 1].Goal.Kind && r.Goal.Id == offenseMemory[f - 1].Goal.Id));
                for (int i = 0; i < inputs.Count; i++) {
                    if (inputs[i].Memory.Returning && !assessed[i].Returning) world.Armies[inputs[i].Army.Id - 1].AutoStartIds = world.Armies[inputs[i].Army.Id - 1].SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
                    inputs[i] = new ArmyDecisionInput(inputs[i].Army, inputs[i].Policy, inputs[i].IsReserveRole, assessed[i], inputs[i].Routes);
                }
                var allocations = world.Tick % 20 != 0 ? assessed : PolicyDecision.Allocate(observation, world.Tick, inputs, reserve, abandoned, attackMemory[f - 1], policies.Select(c => c.Order).ToArray(), out reserveShortfall[f - 1], approachMemory[f - 1], offenseMemory[f - 1].CommittedReserveArmyIds, true);
                var waitGoal = OffenseDecision.WaitGoal(observation, offenseRoutes);
                OffenseDecision.Update(observation, world.Tick, offenseMemory[f - 1], inputs, own, allocations, offenseRoutes, world.Tick % 20 == 0,
                    policies.Any(c => c.Order.Kind == PolicyKind.MaintainReserve && c.Order.Source == CommandSource.Human), waitGoal);
                if (world.Tick % 20 == 0 && reserveShortfall[f - 1] > 0)
                    commandEvents.Add(new GameEvent(world.Tick, (uint)commandEvents.Count, EventKind.AiReport,
                        (byte)(1 << ((int)f - 1)), f, 0, default, reserveShortfall[f - 1], ReasonCode.ReserveShortfall));
                for (int i = 0; i < inputs.Count; i++)
                {
                    ref var a = ref world.Armies[inputs[i].Army.Id - 1];
                    var old = a.Decision; a.Decision = allocations[i];
                    if (old.Goal.Kind != a.Decision.Goal.Kind || old.Goal.Id != a.Decision.Goal.Id || !SamePoint(old.Goal.Point, a.Decision.Goal.Point) || old.Returning != a.Decision.Returning || old.Assignment != a.Decision.Assignment)
                    {
                        a.HasPathGoal = false;
                    }
                }
            }
            if (world.Tick % 20 == 0) lastAllocationTick = world.Tick;
        }

        // Simulation measures navigation; Decision selects the rally and evaluates all choices.
        private OffenseRouteInput[] OffenseRoutes(FactionObservation observation, IReadOnlyList<ArmyDecisionInput> inputs)
        {
            var result = new List<OffenseRouteInput>();
            var home = PolicyDecision.Position(observation, PolicyDecision.Core(observation, true));
            foreach (var goal in observation.Objectives.OrderBy(o => o.Kind).ThenBy(o => o.Id))
            {
                var policyGoal = new PolicyGoal(goal.Kind, goal.Id, default);
                var corePath = world.Map.FindPath(world.Map.Cell(home), goal.Position);
                if (corePath.Length == 0 || !InRange(world.Map.Center(corePath[corePath.Length - 1]), goal.Position, Fix64.FromInt(4))) continue;
                var state = offenseMemory[observation.FactionId - 1];
                SimPoint rally;
                if (state.Phase != OffensivePhase.Idle && state.Goal.Kind == goal.Kind && state.Goal.Id == goal.Id) rally = state.RallyPoint;
                else if (!OffenseDecision.TryRally(observation, corePath.Select(world.Map.Center).ToArray(), out rally)) continue;
                var tail = world.Map.FindPath(world.Map.Cell(rally), goal.Position);
                var legs = inputs.Select(i => {
                    var path = world.Map.FindPath(world.Map.Cell(i.Army.Position), rally);
                    if (path.Length > 0 && !SamePoint(world.Map.Center(path[path.Length - 1]), rally)) path = Array.Empty<int>();
                    return new ObjectiveRoute(new PolicyGoal(GoalKind.Point, i.Army.Id, default), path.Length == 0 ? int.MaxValue : path.Length - 1, path.Select(world.Map.Center).ToArray());
                }).ToArray();
                result.Add(new OffenseRouteInput(policyGoal, rally, tail.Select(world.Map.Center).ToArray(), legs, OffenseDecision.Length(corePath.Select(world.Map.Center).ToArray())));
            }
            return result.ToArray();
        }
        private void WriteDecision(StateWriter w)
        {
            w.Value("Ai.LastAllocationTick", lastAllocationTick);
            for (int f = 0; f < 2; f++)
            {
                string n = "Ai.Factions[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "ReserveShortfall", reserveShortfall[f]);
                WriteDecisionObservation(w, f, n + "Observation.");
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
                w.Value(q + "SuppressedCount", offense.SuppressedGoals.Count);
                w.Value(q + "ReleasedTick", offense.ReleasedTick); w.Value(q + "Id", offense.Id); w.Goal(q + "Goal", offense.Goal); w.Value(q + "Phase", (byte)offense.Phase);
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
                w.Value(n + "InferiorTicks", a.Decision.InferiorTicks); w.Value(n + "Returning", a.Decision.Returning); w.Value(n + "InferiorSince", a.Decision.InferiorSince);
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
