using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Decision
{
    /// <summary>Pure observation-based rules. No world, internal enemy IDs, or mutable shared state.</summary>
    public static class PolicyDecision
    {
        public static BigInteger Distance(SimPoint a, SimPoint b)
        { var x = new BigInteger(a.X.Raw) - b.X.Raw; var z = new BigInteger(a.Z.Raw) - b.Z.Raw; return x * x + z * z; }
        public static bool Within(SimPoint a, SimPoint b, int meters) => Within(a, b, Fix64.FromInt(meters));
        private static bool Within(SimPoint a, SimPoint b, Fix64 radius) => Distance(a, b) <= new BigInteger(radius.Raw) * radius.Raw;
        public static SimPoint Position(FactionObservation o, PolicyGoal goal)
        {
            if (goal.Kind == GoalKind.Point) return goal.Point;
            var found = o.Objectives.FirstOrDefault(v => v.Kind == goal.Kind && v.Id == goal.Id);
            return found.Kind == goal.Kind && found.Id == goal.Id ? found.Position : default;
        }
        public static PolicyGoal Core(FactionObservation o, bool own)
        { var c = o.Objectives.First(v => v.Kind == GoalKind.Core && (v.OwnerFactionId == o.FactionId) == own); return new PolicyGoal(c.Kind, c.Id, default); }
        public static IEnumerable<EnemyContact> CountableContacts(FactionObservation o)
        {
            var covered = new HashSet<uint>(o.Contacts.SelectMany(c => c.CoveredContactIds ?? Array.Empty<uint>()));
            return o.Contacts.Where(c => !covered.Contains(c.ContactId)).GroupBy(c => c.ContactId).Select(g => g.First());
        }
        public static int Estimate(FactionObservation o, SimPoint point)
        {
            int count = 0;
            foreach (var c in CountableContacts(o).OrderBy(c => c.ContactId))
                if (Within(c.LastPosition, point, 24)) count = checked(count + (c.EstimateMax < 0 || o.Tick - c.LastSeenTick >= 600 ? 10 : c.EstimateMax));
            return count;
        }
        public static AttackMemory ObserveAttack(FactionObservation o, KnownObjective objective, AttackMemory memory)
        {
            bool visible = o.VisibleEnemies.Any(e => e.Kind == (byte)UnitKind.Infantry && Within(e.Position, objective.Position, 24));
            memory.OutpostId = objective.Id;
            if (visible)
            {
                if (!memory.Visible && (!memory.HasAttack || o.Tick - memory.LastSeenTick > 60))
                {
                    if (memory.HasAttack && o.Tick - memory.LastAttackTick <= 600) memory.AlertUntilTick = checked(o.Tick + 600);
                    memory.LastAttackTick = o.Tick; memory.HasAttack = true;
                }
                memory.LastSeenTick = o.Tick;
            }
            memory.Visible = visible;
            return memory;
        }
        private static bool Locked(ArmyDecisionInput a) => a.Policy.Kind == PolicyKind.Retreat || a.Policy.Kind == PolicyKind.Defend || a.Policy.Kind == PolicyKind.Scout;
        private static int Route(ArmyDecisionInput a, PolicyGoal goal) => a.Routes.Where(r => r.Goal.Kind == goal.Kind && r.Goal.Id == goal.Id).Select(r => r.Distance).DefaultIfEmpty(int.MaxValue).First();
        private static bool Same(PolicyGoal a, PolicyGoal b) => a.Kind == b.Kind && a.Id == b.Id;
        private static int ContactEstimate(FactionObservation o, EnemyContact c) => c.EstimateMax < 0 || o.Tick - c.LastSeenTick >= 600 ? 10 : c.EstimateMax;

        // Squared distance to a segment, kept entirely in fixed-point integer arithmetic.
        public static bool NearRoute(SimPoint point, IReadOnlyList<SimPoint> cells, int meters)
        {
            if (cells == null || cells.Count == 0) return false;
            BigInteger radius = new BigInteger(Fix64.FromInt(meters).Raw); radius *= radius;
            for (int i = 0; i < cells.Count; i++)
            {
                SimPoint a = cells[i], b = cells[Math.Min(i + 1, cells.Count - 1)];
                BigInteger ax = a.X.Raw, az = a.Z.Raw, bx = b.X.Raw, bz = b.Z.Raw, px = point.X.Raw, pz = point.Z.Raw;
                BigInteger dx = bx - ax, dz = bz - az, ux = px - ax, uz = pz - az, length = dx * dx + dz * dz;
                BigInteger cross;
                if (length == 0) { cross = ux * ux + uz * uz; if (cross <= radius) return true; }
                else
                {
                    BigInteger dot = ux * dx + uz * dz;
                    if (dot <= 0) { if (ux * ux + uz * uz <= radius) return true; continue; }
                    else if (dot >= length) { BigInteger vx = px - bx, vz = pz - bz; if (vx * vx + vz * vz <= radius) return true; continue; }
                    else cross = (ux * ux + uz * uz) * length - dot * dot;
                    if (cross <= radius * length) return true;
                }
            }
            return false;
        }
        private static int RouteEstimate(FactionObservation o, ObjectiveRoute route, KnownObjective goal)
        {
            int count = 0;
            foreach (var c in CountableContacts(o).OrderBy(c => c.ContactId))
                if (NearRoute(c.LastPosition, route.Cells, 24)) count = checked(count + ContactEstimate(o, c));
            // No current/valid contact around an unobserved objective is a warning assumption, not an observation.
            bool unknown = goal.Kind == GoalKind.Outpost ? !goal.IsOwnerKnown : goal.Kind == GoalKind.Core && !goal.IsHpKnown;
            return unknown && count == 0 ? checked(count + 10) : count;
        }

        public static ContactApproachMemory[] UpdateApproaches(FactionObservation o, IReadOnlyList<ContactApproachMemory> old)
            => UpdateApproaches(o, old, Array.Empty<ContactApproachRoute>());
        public static ContactApproachMemory[] UpdateApproaches(FactionObservation o, IReadOnlyList<ContactApproachMemory> old,
            IReadOnlyList<ContactApproachRoute> routes)
        {
            // A contact can be a tombstone for a while, but approach memory must not be one.
            var contacts = new HashSet<uint>(o.Contacts.Select(c => c.ContactId));
            var result = old.Where(m => contacts.Contains(m.ContactId) && o.Tick - m.LastSeenTick <= 600)
                .OrderBy(m => m.ContactId).ThenBy(m => m.ObjectiveId).ToList();
            foreach (var post in o.Objectives.Where(x => x.Kind == GoalKind.Outpost).OrderBy(x => x.Id))
            foreach (var c in o.Contacts.Where(c => c.IsCurrentlyVisible && o.VisibleEnemies.Any(e => e.ContactId == c.ContactId && e.Kind == (byte)UnitKind.Infantry)).OrderBy(c => c.ContactId))
            {
                int ix = result.FindIndex(m => m.ContactId == c.ContactId && m.ObjectiveId == post.Id);
                var m = ix < 0 ? new ContactApproachMemory { ContactId = c.ContactId, ObjectiveId = post.Id } : result[ix];
                int distance = routes.Where(r => r.ContactId == c.ContactId && r.ObjectiveId == post.Id).Select(r => r.Distance).DefaultIfEmpty(int.MaxValue).First();
                if (distance != int.MaxValue)
                {
                    // The first observation establishes a baseline.  A later increase explicitly
                    // cancels the approach; only a decrease while inside 60 m creates a threat.
                    if (m.HasPrevious && distance > m.PreviousDistance) m.ThreatUntilTick = 0;
                    else if (m.HasPrevious && distance < m.PreviousDistance && distance <= 60) m.ThreatUntilTick = checked(o.Tick + 200);
                    m.PreviousPosition = c.LastPosition; m.PreviousDistance = distance; m.LastSeenTick = o.Tick; m.HasPrevious = true;
                }
                if (ix < 0) result.Add(m); else result[ix] = m;
            }
            return result.OrderBy(m => m.ObjectiveId).ThenBy(m => m.ContactId).ToArray();
        }
        public static ArmyDecisionMemory[] Allocate(FactionObservation observation, long tick,
            IReadOnlyList<ArmyDecisionInput> inputs, ushort reservePermille, IReadOnlyList<uint> abandoned,
            IReadOnlyList<AttackMemory> attacks, IReadOnlyList<PolicyOrder> guardPriorities, out int reserveShortfall,
            IReadOnlyList<ContactApproachMemory> approaches = null, IReadOnlyList<uint> committedReserves = null, bool coordinated = false)
        {
            committedReserves = committedReserves ?? Array.Empty<uint>();
            var armies = inputs.OrderBy(a => a.Army.Id).ToArray();
            var result = armies.Select(a => a.Memory).ToArray();
            var used = new bool[armies.Length];
            var core = Core(observation, true); var home = Position(observation, core);
            int total = armies.Sum(a => a.Army.AliveCount), held = 0;
            bool humanReserve = guardPriorities.Any(p => p.Kind == PolicyKind.MaintainReserve && p.Source == CommandSource.Human);
            int target = checked((int)((total * (long)reservePermille + 999) / 1000));
            var candidates = Enumerable.Range(0, armies.Length).Where(i => armies[i].Army.AliveCount > 0 && !Locked(armies[i]) &&
                !result[i].Returning && tick >= result[i].HoldUntilTick).ToArray();
            for (int i = 0; i < armies.Length; i++)
            {
                used[i] = !candidates.Contains(i);
                if (!used[i]) { result[i].Assignment = AssignmentKind.Advance; result[i].Goal = default; }
            }
            // Core emergencies precede reserve and outpost allocations. Focus permits this reassignment.
            int danger = observation.VisibleEnemies.Any(e => Within(e.Position, home, 24)) ? Math.Max(1, Estimate(observation, home)) : 0;
            if (danger > 0)
            {
                int defenders = armies.Where(Locked).Where(a => a.Policy.Kind == PolicyKind.Defend && a.Policy.Goal.Kind == core.Kind && a.Policy.Goal.Id == core.Id).Sum(a => a.Army.AliveCount);
                foreach (int i in candidates.Where(i => armies[i].Army.Kind != UnitKind.Scout).OrderBy(i => committedReserves.Contains(armies[i].Army.Id) ? 0 : armies[i].IsReserveRole ? 1 : 2).ThenBy(i => Route(armies[i], core)).ThenBy(i => armies[i].Army.Id))
                {
                    if (defenders >= danger && held >= target && !committedReserves.Contains(armies[i].Army.Id)) break;
                    used[i] = true; result[i].Assignment = AssignmentKind.CoreDefense; result[i].Goal = core;
                    defenders += armies[i].Army.AliveCount; held += armies[i].Army.AliveCount;
                }
            }
            foreach (int i in candidates.Where(i => !used[i] && armies[i].Army.Kind != UnitKind.Scout)
                .OrderBy(i => armies[i].IsReserveRole ? 0 : 1).ThenBy(i => Route(armies[i], core)).ThenBy(i => armies[i].Army.Id))
            {
                if (held >= target) break;
                if (!humanReserve && committedReserves.Contains(armies[i].Army.Id)) continue;
                int need = target - held;
                // The default auto reserve may leave a large army free for offense.  Explicit human
                // MaintainReserve remains an unconditional lower-bound guarantee.
                if (!humanReserve && reservePermille != 0 && !armies[i].IsReserveRole && 2L * need < armies[i].Army.AliveCount) continue;
                used[i] = true; result[i].Assignment = AssignmentKind.Reserve; result[i].Goal = core; held += armies[i].Army.AliveCount;
            }
            reserveShortfall = Math.Max(0, target - held);
            approaches = approaches ?? Array.Empty<ContactApproachMemory>();
            var posts = observation.Objectives.Where(o => o.Kind == GoalKind.Outpost && o.IsOwnerKnown && o.OwnerFactionId == observation.FactionId && !abandoned.Contains(o.Id)
                && (observation.VisibleEnemies.Any(e => e.Kind == (byte)UnitKind.Infantry && Within(e.Position, o.Position, 24))
                    || approaches.Any(m => m.ObjectiveId == o.Id && tick <= m.ThreatUntilTick)))
                .OrderByDescending(o => guardPriorities.Where(p => p.Target.Kind == ScopeKind.Outpost && p.Target.Id == o.Id && p.Source == CommandSource.Human).Select(p => (int)p.Priority).DefaultIfEmpty(0).Max())
                .ThenByDescending(o => attacks.Any(m => m.OutpostId == o.Id && tick <= m.AlertUntilTick && m.AlertUntilTick > 0)).ThenBy(o => o.Id);
            foreach (var post in posts)
            {
                if (armies.Any(a => a.Army.AliveCount > 0 && a.Policy.Kind == PolicyKind.Defend && a.Policy.Goal.Kind == post.Kind && a.Policy.Goal.Id == post.Id)) continue;
                int i = candidates.Where(j => !used[j] && armies[j].Army.Kind != UnitKind.Scout && armies[j].Army.HomeObjective.Kind == post.Kind && armies[j].Army.HomeObjective.Id == post.Id).DefaultIfEmpty(-1).First();
                if (i < 0) i = candidates.Where(j => !used[j] && armies[j].Army.Kind != UnitKind.Scout)
                    .OrderBy(j => Route(armies[j], new PolicyGoal(post.Kind, post.Id, default)))
                    .ThenBy(j => armies[j].Army.Id).DefaultIfEmpty(-1).First();
                if (i < 0) continue;
                used[i] = true; result[i].Assignment = AssignmentKind.Guard; result[i].Goal = new PolicyGoal(post.Kind, post.Id, default);
            }
            foreach (int i in candidates.Where(i => !used[i]))
            {
                var a = armies[i];
                if (a.Policy.Kind == PolicyKind.Focus) result[i].Goal = a.Policy.Goal;
                else if (coordinated) { result[i].Assignment = AssignmentKind.Advance; }
                else
                {
                    // Retain a still-valid auto target for 600 ticks.  It is intentionally not renewed.
                    var current = result[i].Goal;
                    bool owned = observation.Objectives.Any(o => o.Kind == GoalKind.Outpost && o.IsOwnerKnown && o.OwnerFactionId == observation.FactionId);
                    bool currentOwned = current.Kind == GoalKind.Outpost && observation.Objectives.Any(o => o.Kind == GoalKind.Outpost && o.Id == current.Id && o.IsOwnerKnown && o.OwnerFactionId == observation.FactionId);
                    if (current.Kind != GoalKind.None && result[i].OffensiveSince != 0 && tick - result[i].OffensiveSince < 600 && !currentOwned && Route(a, current) != int.MaxValue)
                    { result[i].Assignment = AssignmentKind.Advance; continue; }
                    var goals = observation.Objectives.Where(o => o.Kind == GoalKind.Outpost && (!o.IsOwnerKnown || o.OwnerFactionId != observation.FactionId))
                        .Select(o => new PolicyGoal(o.Kind, o.Id, default)).ToList();
                    if (owned) goals.Add(Core(observation, false));
                    var choice = goals.Where(g => !(Same(g, result[i].SuppressedGoal) && tick < result[i].SuppressUntilTick))
                        .Select(g => new { Goal = g, RouteInfo = a.Routes.FirstOrDefault(r => r.Goal.Kind == g.Kind && r.Goal.Id == g.Id),
                            Objective = observation.Objectives.FirstOrDefault(o => o.Kind == g.Kind && o.Id == g.Id) })
                        // Missing route metadata is unreachable.  Do not turn a partial observation
                        // into InvalidOperationException through First().
                        .Where(x => x.RouteInfo.Distance != int.MaxValue && x.Objective.Kind == x.Goal.Kind && x.Objective.Id == x.Goal.Id)
                        .Select(x => new { x.Goal, Route = x.RouteInfo.Distance, Threat = RouteEstimate(observation, x.RouteInfo, x.Objective) })
                        .OrderBy(x => x.Threat * 1000 / Math.Max(1, a.Army.AliveCount)).ThenBy(x => x.Route)
                        .ThenBy(x => x.Goal.Kind == GoalKind.Outpost ? 0 : 1).ThenBy(x => x.Goal.Id).FirstOrDefault();
                    // No route is a deliberate wait at home, not an Advance with a None goal.
                    // The latter falls through to the enemy core in navigation and can leave a
                    // path-impossible army motionless forever.
                    result[i].Assignment = choice == null ? AssignmentKind.Reserve : AssignmentKind.Advance;
                    result[i].Goal = choice == null ? core : choice.Goal;
                    result[i].OffensiveSince = choice != null && !Same(current, choice.Goal) ? tick : result[i].OffensiveSince;
                }
            }
            return result;
        }
        public static ArmyDecisionMemory AssessRetreat(FactionObservation o, OwnArmyView army, PolicyView policy,
            ArmyDecisionMemory memory, long tick, bool lossReached, bool homeArrived)
        {
            if (memory.Returning)
            {
                if (homeArrived)
                {
                    memory.Returning = false; memory.HoldUntilTick = checked(tick + 60); memory.InferiorSince = 0;
                    memory.Assignment = AssignmentKind.Reserve; memory.Goal = Core(o, true);
                }
                return memory;
            }
            if (policy.CommandId != 0 || tick < memory.HoldUntilTick) { memory.InferiorSince = 0; return memory; }
            bool inferior = army.AliveCount * 2L < Estimate(o, army.Position);
            if (!inferior) memory.InferiorSince = 0;
            else if (memory.InferiorSince == 0) memory.InferiorSince = tick;
            if (lossReached || inferior && tick - memory.InferiorSince >= 39)
            {
                memory.Returning = true;
                if (memory.Goal.Kind != GoalKind.None)
                { memory.SuppressedGoal = memory.Goal; memory.SuppressUntilTick = checked(tick + 600); memory.OffensiveSince = 0; }
            }
            return memory;
        }
        public static ArmyIntent Tactics(FactionObservation o, TacticalInput input, ref PursuitMemory pursuit)
        {
            var position = input.Position; var mission = input.Mission;
            if (pursuit.Active || pursuit.Returning) pursuit.Mission = mission;
            if (input.Returning || input.Policy == PolicyKind.Retreat)
            { pursuit = default; return new ArmyIntent(input.ArmyId, mission, 0, default, true); }
            if (pursuit.Returning)
            {
                if (!Within(position, pursuit.Mission, 1)) return new ArmyIntent(input.ArmyId, pursuit.Mission, 0, default, false);
                pursuit = default;
            }
            if (pursuit.Active && (input.Tick - pursuit.StartTick >= 60 || !Within(position, pursuit.Start, Fix64.FromRaw(Fix64.FromInt(12).Raw - 1))))
            {
                pursuit.Active = false; pursuit.Returning = true;
                return new ArmyIntent(input.ArmyId, pursuit.Mission, 0, default, false);
            }
            bool allocated = input.Policy == 0 || input.Policy == PolicyKind.Focus;
            bool reserve = allocated && (input.Assignment == AssignmentKind.Reserve || input.Assignment == AssignmentKind.CoreDefense);
            bool defend = input.Policy == PolicyKind.Defend || allocated && input.Assignment == AssignmentKind.Guard;
            SimPoint anchor = reserve ? input.Home : mission;
            int leash = reserve ? 24 : defend ? 12 : 24;
            // A defender outside its hold area first returns; no opportunistic attack extends its leash.
            if ((defend || reserve) && !Within(position, anchor, leash))
                return new ArmyIntent(input.ArmyId, anchor, 0, default, false);
            var visible = o.VisibleEnemies.Where(e => (defend || reserve) ? Within(e.Position, anchor, leash) : Within(e.Position, position, 24))
                .OrderBy(e => Distance(position, e.Position)).ThenBy(e => e.ContactId).ToArray();
            foreach (var enemy in visible)
                if (Within(position, enemy.Position, input.Range))
                    return new ArmyIntent(input.ArmyId, defend && Within(position, anchor, 8) || reserve && Within(position, anchor, 8) ? position : mission, enemy.ContactId, default, false);
            var core = o.Objectives.Where(c => c.Kind == GoalKind.Core && c.OwnerFactionId != o.FactionId && c.IsHpKnown && c.Hp > 0 &&
                Within(position, c.Position, input.Range + input.CoreRadius) && (!defend && !reserve || Within(c.Position, anchor, leash)))
                .OrderBy(c => Distance(position, c.Position)).ThenBy(c => c.Id).ToArray();
            if (core.Length > 0) return new ArmyIntent(input.ArmyId, mission, 0, new PolicyGoal(GoalKind.Core, core[0].Id, default), false);
            if (visible.Length > 0)
            {
                if (!pursuit.Active) { pursuit.Active = true; pursuit.Start = position; pursuit.StartTick = input.Tick; pursuit.Mission = mission; }
                return new ArmyIntent(input.ArmyId, FixMath.MoveTowards(pursuit.Start, visible[0].Position, Fix64.FromInt(12)), 0, default, false);
            }
            if (pursuit.Active) { pursuit.Active = false; pursuit.Returning = true; return new ArmyIntent(input.ArmyId, pursuit.Mission, 0, default, false); }
            return new ArmyIntent(input.ArmyId, (defend || reserve) && Within(position, anchor, 8) ? position : mission, 0, default, false);
        }
        public static SimPoint ScoutReturn(FactionObservation o, SimPoint position, SimPoint home)
        {
            var enemy = o.VisibleEnemies.Where(e => Within(e.Position, position, 12)).OrderBy(e => Distance(e.Position, position)).ThenBy(e => e.ContactId).ToArray();
            if (enemy.Length == 0) return home;
            var e = enemy[0].Position;
            long dx = checked(position.X.Raw - e.X.Raw), dz = checked(position.Z.Raw - e.Z.Raw);
            if (dx == 0 && dz == 0) dx = home.X.Raw >= position.X.Raw ? 1 : -1;
            // A destination on the visible-enemy-to-scout ray; normal movement speed still applies.
            return new SimPoint(Fix64.FromRaw(checked(position.X.Raw + dx)), Fix64.FromRaw(checked(position.Z.Raw + dz)));
        }
        public static bool ScoutSeesEnemy(FactionObservation o, SimPoint position, Fix64 vision)
            => o.VisibleEnemies.Any(e => Within(e.Position, position, vision));
    }
}
