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
            for (uint f = 1; f <= 2; f++)
            {
                var observation = frames[f - 1].Observation;
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
                    var routes = new List<ObjectiveRoute>();
                    foreach (var o in observation.Objectives.OrderBy(o => o.Kind).ThenBy(o => o.Id))
                    {
                        var path = world.Map.FindPath(world.Map.Cell(view.Position), o.Position);
                        int distance = path.Length == 0 || !InRange(world.Map.Center(path[path.Length - 1]), o.Position, Fix64.FromInt(4)) ? int.MaxValue : path.Length - 1;
                        routes.Add(new ObjectiveRoute(new PolicyGoal(o.Kind, o.Id, default), distance));
                    }
                    inputs.Add(new ArmyDecisionInput(view, ArmyPolicy(view.Id), a.Definition.Role == "reserve", a.Decision, routes));
                }
                var policies = commandStates.Where(c => c.Status == CommandStatus.Executing && c.Order.Target.FactionId == f).OrderBy(c => c.Order.Source).ThenByDescending(c => c.LogIndex).ToArray();
                ushort reserve = policies.Where(c => c.Order.Kind == PolicyKind.MaintainReserve).Select(c => c.Order.ReservePermille).DefaultIfEmpty((ushort)200).First();
                var abandoned = policies.Where(c => c.Order.Kind == PolicyKind.AllowAbandon).Select(c => c.Order.Target.Id).OrderBy(id => id).ToArray();
                var allocations = PolicyDecision.Allocate(observation, world.Tick, inputs, reserve, abandoned, attackMemory[f - 1], policies.Select(c => c.Order).ToArray(), out reserveShortfall[f - 1]);
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
            }
            foreach (var a in world.Armies)
            {
                string n = "Ai.Armies[" + a.Definition.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Assignment", (byte)a.Decision.Assignment); w.Goal(n + "Goal", a.Decision.Goal);
                w.Value(n + "Returning", a.Decision.Returning); w.Value(n + "InferiorSince", a.Decision.InferiorSince);
                w.Value(n + "HoldUntilTick", a.Decision.HoldUntilTick); w.Ids(n + "StartIds", a.AutoStartIds ?? Array.Empty<uint>());
            }
            foreach (var s in world.Soldiers)
            {
                string n = "Ai.Soldiers[" + s.Initial.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Pursuit.Active", s.Pursuit.Active); w.Value(n + "Pursuit.Returning", s.Pursuit.Returning);
                w.Point(n + "Pursuit.Start", s.Pursuit.Start); w.Point(n + "Pursuit.Mission", s.Pursuit.Mission);
                w.Value(n + "Pursuit.StartTick", s.Pursuit.StartTick);
            }
        }
    }
}
