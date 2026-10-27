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
        public const int HomeArrivalRadius = 24;
        private long lastAllocationTick;
        private readonly int[] reserveShortfall = new int[2];
        private AttackMemory[][] attackMemory;
        private ContactApproachMemory[][] approachMemory;
        // One state per faction: unlike the old per-army target timer this describes a shared attack.
        private readonly FactionOffenseMemory[] offenseMemory = { new FactionOffenseMemory(), new FactionOffenseMemory() };
        private const int ExplorePathFailureLimit = 3;
        private readonly List<ContactApproachRoute> approachRoutesScratch = new List<ContactApproachRoute>();
        private readonly HashSet<uint> visibleInfantryContactsScratch = new HashSet<uint>();
        private readonly List<ArmyDecisionInput> armyInputsScratch = new List<ArmyDecisionInput>(16);
        private readonly List<ObjectiveRoute> objectiveRoutesScratch = new List<ObjectiveRoute>(16);
        private readonly List<OffenseArmyInput> ownArmyInputsScratch = new List<OffenseArmyInput>(16);
        private readonly List<OffenseRouteInput> offenseRoutesScratch = new List<OffenseRouteInput>(16);

        private bool ExploreScoutingOn => world.Config.Economy.ExploreScouting;
        private bool LargeExploreOn => ExploreScoutingOn && world.Config.Economy.LargeEconomy;

        private bool LargeExploreReached(uint faction)
        {
            int total = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            return (long)world.Factions[faction - 1].ExploredCellCount * 1000L
                >= (long)world.Config.Economy.LargeExploreTargetPermille * total;
        }

        private bool IsScoutArmy(ArmyState army)
        {
            bool any = false;
            foreach (uint id in army.SoldierIds)
            {
                var soldier = world.Soldiers[id - 1];
                if (soldier.Initial.Kind != UnitKind.Scout) return false;
                if (soldier.Alive) any = true;
            }
            return any;
        }

        private bool ExploreAnyReturning(ArmyState army)
        {
            foreach (uint id in army.SoldierIds)
                if (world.Soldiers[id - 1].Alive && world.Soldiers[id - 1].Pursuit.Returning) return true;
            return false;
        }

        private bool ExploreArrived(ArmyState army)
        {
            if (!army.ExploreHasGoal) return false;
            var live = army.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
            return live.Length > 0 && live.All(id => InRange(world.Soldiers[id - 1].Position, army.ExploreGoal, Fix64.FromInt(4)));
        }

        private void UpdateExplorePatrol(uint faction, ref ArmyState army)
        {
            if (!ExploreScoutingOn || !IsScoutArmy(army))
            {
                army.ExplorePatrolActive = false; army.ExplorePatrolAuto = false; army.ExploreHasGoal = false;
                return;
            }
            bool manualPatrol = army.Policy == PolicyKind.Scout && army.Goal.Kind == GoalKind.None;
            bool autoPatrol = army.Policy == 0 && ((LargeExploreOn && !LargeExploreReached(faction))
                || (!LargeExploreOn && world.Tick <= world.Config.Economy.ExploreAutoUntilTick)
                || army.ExplorePatrolActive && ExploreAnyReturning(army));
            if (!manualPatrol && !autoPatrol)
            {
                army.ExplorePatrolActive = false; army.ExplorePatrolAuto = false; army.ExploreHasGoal = false;
                army.HasPathGoal = false;
                return;
            }
            if (ExploreAnyReturning(army))
            {
                if (HomeArrived(army))
                {
                    foreach (uint id in army.SoldierIds) world.Soldiers[id - 1].Pursuit = default;
                    army.ExploreRetargetTick = world.Tick;
                    army.ExploreHasGoal = false;
                    army.HasPathGoal = false;
                }
                army.ExplorePatrolActive = true;
                army.ExplorePatrolAuto = autoPatrol && !manualPatrol;
                return;
            }
            if (army.ExploreHasGoal && army.PathImpossible)
            {
                if (army.ExploreFailedChunkX == army.ExploreGoalChunkX && army.ExploreFailedChunkZ == army.ExploreGoalChunkZ)
                    army.ExploreFailureCount++;
                else
                {
                    army.ExploreFailedChunkX = army.ExploreGoalChunkX;
                    army.ExploreFailedChunkZ = army.ExploreGoalChunkZ;
                    army.ExploreFailureCount = 1;
                }
                if (army.ExploreFailureCount >= ExplorePathFailureLimit)
                {
                    army.ExploreAvoidChunkX = army.ExploreGoalChunkX;
                    army.ExploreAvoidChunkZ = army.ExploreGoalChunkZ;
                    army.ExploreAvoidUntilTick = checked(world.Tick + world.Config.Economy.ExploreAvoidTicks);
                    army.ExploreFailureCount = 0;
                    army.ExploreHasGoal = false;
                    army.HasPathGoal = false;
                    army.PathImpossible = false;
                }
            }
            if (!army.ExplorePatrolActive || army.ExplorePatrolAuto != (autoPatrol && !manualPatrol))
            {
                army.ExplorePatrolActive = true;
                army.ExplorePatrolAuto = autoPatrol && !manualPatrol;
                army.ExploreRetargetTick = world.Tick;
                army.ExploreHasGoal = false;
                army.HasPathGoal = false;
                if (LargeExploreOn && army.ExplorePatrolAuto)
                    foreach (uint id in army.SoldierIds)
                    {
                        ref var scout = ref world.Soldiers[id - 1];
                        scout.ExploreHasGoal = false;
                        scout.ExploreRetargetTick = world.Tick;
                    }
            }
            if (world.Tick < army.ExploreRetargetTick && !ExploreArrived(army)) return;

            var passable = new bool[world.Config.Map.WidthCells * world.Config.Map.HeightCells];
            for (int cell = 0; cell < passable.Length; cell++) passable[cell] = world.Map.IsPassableFor(cell, faction);
            var avoid = army.ExploreAvoidUntilTick >= world.Tick
                ? new[] { new ExploreAvoidChunk(army.ExploreAvoidChunkX, army.ExploreAvoidChunkZ, army.ExploreAvoidUntilTick) }
                : Array.Empty<ExploreAvoidChunk>();
            var core = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
            if (LargeExploreOn && autoPatrol && !manualPatrol)
            {
                UpdateLargeScoutTargets(faction, ref army, core, passable, avoid);
                return;
            }
            var target = ExploreDecision.NextTarget(world.Factions[faction - 1].ExploredCells,
                world.Config.Map.WidthCells, world.Config.Map.HeightCells, world.Config.Map.CellSizeMeters,
                world.Config.Economy.ExploreChunkCells, world.Config.Economy.ExploreMinUnexploredPermille,
                world.Config.Economy.ExploreMinUnexploredPermilleStage2,
                world.Config.Economy.ExploreMinUnexploredPermilleStage3,
                world.Config.Economy.ExploreMinUnexploredPermilleStage4,
                core, passable, avoid, world.Tick, world.Config.Economy.ExploreAvoidChunks);
            bool changed = army.ExploreHasGoal != target.HasTarget || target.HasTarget && !SamePoint(army.ExploreGoal, target.Position);
            army.ExplorePatrolActive = true;
            army.ExploreHasGoal = target.HasTarget;
            army.ExploreGoal = target.Position;
            army.ExploreGoalChunkX = target.ChunkX; army.ExploreGoalChunkZ = target.ChunkZ;
            army.ExploreRetargetTick = checked(world.Tick + world.Config.Economy.ExploreRetargetTicks);
            if (changed)
            {
                army.HasPathGoal = false;
                army.PathImpossible = false;
                army.ExploreFailureCount = 0;
            }
        }

        private void UpdateLargeScoutTargets(uint faction, ref ArmyState army, SimPoint core,
            IReadOnlyList<bool> passable, IReadOnlyList<ExploreAvoidChunk> avoid)
        {
            var reserved = new List<ExploreReservedChunk>();
            bool oldHasGoal = army.ExploreHasGoal;
            SimPoint oldGoal = army.ExploreGoal;
            uint[] scouts = army.SoldierIds.Where(id => world.Soldiers[id - 1].Alive
                && world.Soldiers[id - 1].Initial.Kind == UnitKind.Scout).OrderBy(id => id).ToArray();
            for (int i = 0; i < scouts.Length; i++)
            {
                ref var scout = ref world.Soldiers[scouts[i] - 1];
                bool arrived = scout.ExploreHasGoal && InRange(scout.Position, scout.ExploreGoal, Fix64.FromInt(4));
                if (!scout.ExploreHasGoal || world.Tick >= scout.ExploreRetargetTick || arrived)
                {
                    var target = ExploreDecision.NextTarget(world.Factions[faction - 1].ExploredCells,
                        world.Factions[faction - 1].ExploredChunkCounts,
                        world.Config.Map.WidthCells, world.Config.Map.HeightCells, world.Config.Map.CellSizeMeters,
                        world.Config.Economy.ExploreChunkCells, world.Config.Economy.ExploreMinUnexploredPermille,
                        world.Config.Economy.ExploreMinUnexploredPermilleStage2,
                        world.Config.Economy.ExploreMinUnexploredPermilleStage3,
                        world.Config.Economy.ExploreMinUnexploredPermilleStage4,
                        core, passable, avoid, reserved, world.Tick, world.Config.Economy.ExploreAvoidChunks);
                    scout.ExploreHasGoal = target.HasTarget;
                    scout.ExploreGoal = target.Position;
                    scout.ExploreGoalChunkX = target.ChunkX;
                    scout.ExploreGoalChunkZ = target.ChunkZ;
                    scout.ExploreRetargetTick = checked(world.Tick + world.Config.Economy.ExploreRetargetTicks);
                    scout.TacticalRoute = false;
                    ReleaseLocalPath(ref scout);
                }
                if (scout.ExploreHasGoal)
                    reserved.Add(new ExploreReservedChunk(scout.ExploreGoalChunkX, scout.ExploreGoalChunkZ));
            }
            if (scouts.Length == 0)
            {
                army.ExploreHasGoal = false;
                army.ExploreGoal = default;
            }
            else
            {
                var lead = world.Soldiers[scouts[0] - 1];
                army.ExploreHasGoal = lead.ExploreHasGoal;
                army.ExploreGoal = lead.ExploreGoal;
                army.ExploreGoalChunkX = lead.ExploreGoalChunkX;
                army.ExploreGoalChunkZ = lead.ExploreGoalChunkZ;
                army.ExploreRetargetTick = lead.ExploreRetargetTick;
            }
            if (oldHasGoal != army.ExploreHasGoal || oldHasGoal && !SamePoint(oldGoal, army.ExploreGoal))
                army.HasPathGoal = false;
        }

        private PolicyView ArmyPolicy(uint id)
        {
            var a = world.Armies[id - 1];
            var c = CommandById(a.CommandId);
            return c == null ? default : new PolicyView(c.Order.CommandId, c.Order.Source, a.Policy, a.Goal, c.Order.AllowedLoss, c.Order.ReservePermille);
        }
        private bool HomeArrived(ArmyState a)
        {
            var live = (a.AutoStartIds ?? System.Array.Empty<uint>())
                .Where(id => world.Soldiers[id - 1].Alive).ToArray();
            var home = world.Cores[world.Factions[a.Definition.FactionId - 1].CoreId - 1].Definition.Position;
            return live.All(id => InRange(world.Soldiers[id - 1].Position, home, Fix64.FromInt(HomeArrivalRadius)));
        }
        private bool ReturnComplete(FactionObservation observation, ArmyState a)
        {
            if (HomeArrived(a)) return true;
            var live = a.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
            return live.Length > 0 && live.All(id => world.Soldiers[id - 1].Initial.Kind == UnitKind.Scout)
                && live.All(id => !PolicyDecision.ScoutSeesEnemy(observation, world.Soldiers[id - 1].Position, world.Soldiers[id - 1].Parameters.Vision));
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
                FactionObservation observation = null;
                measure?.Invoke("AI.Observation", true);
                try
                {
                    observation = ObserveForDecision(f);
                    // A* is invoked only for the finite visible-infantry × outpost set on this tick.
                    var approachRoutes = approachRoutesScratch;
                    var visibleInfantryContacts = visibleInfantryContactsScratch;
                    approachRoutes.Clear();
                    visibleInfantryContacts.Clear();
                    for (int i = 0; i < observation.VisibleEnemies.Count; i++)
                        if (observation.VisibleEnemies[i].Kind == (byte)UnitKind.Infantry)
                            visibleInfantryContacts.Add(observation.VisibleEnemies[i].ContactId);
                    foreach (var post in observation.Objectives.Where(o => o.Kind == GoalKind.Outpost).OrderBy(o => o.Id))
                    foreach (var contact in observation.Contacts.Where(c => !c.IsArmyContact && c.IsCurrentlyVisible && visibleInfantryContacts.Contains(c.ContactId)).OrderBy(c => c.ContactId))
                    {
                        var path = world.Map.FindPath(world.Map.Cell(contact.LastPosition), post.Position, f);
                        int distance = path.Length == 0 || !InRange(world.Map.Center(path[path.Length - 1]), post.Position, Fix64.FromInt(4)) ? int.MaxValue : checked((int)(OffenseDecision.Length(world.Map.FindPathCenterView(world.Map.Cell(contact.LastPosition), post.Position, f)) / Fix64.FromInt(1).Raw));
                        approachRoutes.Add(new ContactApproachRoute(contact.ContactId, post.Id, distance));
                    }
                    approachMemory[f - 1] = PolicyDecision.UpdateApproaches(observation, approachMemory[f - 1], approachRoutes, world.Config.Rules.OccupationThreatMemoryTicks);
                    foreach (var post in observation.Objectives.Where(o => o.Kind == GoalKind.Outpost).OrderBy(o => o.Id))
                        attackMemory[f - 1][post.Id - 1] = PolicyDecision.ObserveAttack(observation, post, attackMemory[f - 1][post.Id - 1]);
                }
                finally { measure?.Invoke("AI.Observation", false); }
                foreach (var view in observation.OwnArmies.OrderBy(a => a.Id))
                {
                    ref var a = ref world.Armies[view.Id - 1];
                    if (a.AutoStartIds == null) a.AutoStartIds = a.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();

                }

                measure?.Invoke("AI.Routes", true);
                try
                {
                // The sanctuary civilisation hands the common policies a candidate list of observed outposts only, and
                // reports the enemy core unreachable while such an outpost is not yet its own. For every other faction
                // this is the same observation instance and focus is false, so nothing below changes for them.
                var targets = SanctuaryTargetObservation(f, observation);
                bool sanctuaryFocus = SanctuaryOutpostFocus(f, targets);
                var inputs = armyInputsScratch;
                inputs.Clear();
                foreach (var view in observation.OwnArmies.OrderBy(a => a.Id))
                {
                    var a = world.Armies[view.Id - 1];
                    uint first = 0;
                    for (int si = 0; si < a.SoldierIds.Length; si++)
                        if (world.Soldiers[a.SoldierIds[si] - 1].Alive) { first = a.SoldierIds[si]; break; }
                    var start = first == 0 ? world.Map.Cell(view.Position) : world.Map.Cell(world.Soldiers[first - 1].Position);
                    var routes = objectiveRoutesScratch;
                    routes.Clear();
                    foreach (var o in targets.Objectives.OrderBy(o => o.Kind).ThenBy(o => o.Id))
                    {
                        var path = world.Map.FindPath(start, o.Position, f);
                        var centers = world.Map.FindPathCenterView(start, o.Position, f);
                        int distance = path.Length == 0 || !InRange(world.Map.Center(path[path.Length - 1]), o.Position, Fix64.FromInt(4)) ? int.MaxValue : checked((int)(OffenseDecision.Length(centers) / Fix64.FromInt(1).Raw));
                        if (SanctuarySkipsGoal(f, sanctuaryFocus, o.Kind, o.Id)) distance = int.MaxValue;
                        routes.Add(new ObjectiveRoute(new PolicyGoal(o.Kind, o.Id, default), distance, centers));
                    }
                    if (OffenseDecision.IsMobileArmyKind(view.Kind))
                        foreach (var target in observation.RaidTargets.OrderBy(t => t.Kind).ThenBy(t => t.Id))
                        {
                            var path = world.Map.FindPath(start, target.Position, f);
                            var centers = world.Map.FindPathCenterView(start, target.Position, f);
                            int distance = path.Length == 0 || !InRange(world.Map.Center(path[path.Length - 1]), target.Position, Fix64.FromInt(4))
                                ? int.MaxValue : checked((int)(OffenseDecision.Length(centers) / Fix64.FromInt(1).Raw));
                            routes.Add(new ObjectiveRoute(new PolicyGoal(GoalKind.Point, target.Id, target.Position), distance,
                                centers));
                        }
                    inputs.Add(new ArmyDecisionInput(view, ArmyPolicy(view.Id), a.Definition.Role == "reserve", a.Decision, routes));
                }
                var policies = liveCommands.Where(c => c.Status == CommandStatus.Executing && c.Order.Target.FactionId == f).OrderBy(c => SourcePriority(c.Order.Source)).ThenByDescending(c => c.LogIndex).ToArray();
                var decisionPolicies = PolicyOrdersForDecision(f, policies);
                ushort reserve = policies.Where(c => c.Order.Kind == PolicyKind.MaintainReserve).Select(c => c.Order.ReservePermille).DefaultIfEmpty(world.Config.Rules.DefaultReservePermille).First();
                var abandoned = decisionPolicies.Where(o => o.Kind == PolicyKind.AllowAbandon && o.Target.Kind == ScopeKind.Outpost).Select(o => o.Target.Id).OrderBy(id => id).ToArray();
                var own = ownArmyInputsScratch;
                own.Clear();
                for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    var i = inputs[inputIndex];
                    var a = world.Armies[i.Army.Id - 1];
                    var live = a.SoldierIds.Where(id => world.Soldiers[id - 1].Alive).OrderBy(id => id).ToArray();
                    var soldierPositions = new SimPoint[live.Length];
                    for (int si = 0; si < live.Length; si++) soldierPositions[si] = world.Soldiers[live[si] - 1].Position;
                    int lost = 0;
                    for (int si = 0; si < a.AutoStartIds.Length; si++) if (!world.Soldiers[a.AutoStartIds[si] - 1].Alive) lost++;
                    own.Add(new OffenseArmyInput(i.Army.Id, soldierPositions, live.Length == 0 ? 0 : world.Soldiers[live[0] - 1].StepDistance.Raw,
                        false, ReturnComplete(observation, a), a.AutoStartIds.Length, lost));
                }
                var offenseRoutes = OffenseRoutes(targets, inputs);
                ArmyDecisionMemory[] allocations;
                measure?.Invoke("AI.Policy", true);
                try
                {
                var assessed = inputs.Select(i => i.Memory).ToArray();
                OffenseDecision.Retreat(targets, world.Tick, offenseMemory[f - 1], inputs, own, assessed, offenseRoutes.FirstOrDefault(r => r.Goal.Kind == offenseMemory[f - 1].Goal.Kind && r.Goal.Id == offenseMemory[f - 1].Goal.Id));
                for (int i = 0; i < inputs.Count; i++) {
                    if (inputs[i].Memory.Returning && !assessed[i].Returning) world.Armies[inputs[i].Army.Id - 1].AutoStartIds = world.Armies[inputs[i].Army.Id - 1].SoldierIds.Where(id => world.Soldiers[id - 1].Alive).ToArray();
                    inputs[i] = new ArmyDecisionInput(inputs[i].Army, inputs[i].Policy, inputs[i].IsReserveRole, assessed[i], inputs[i].Routes);
                }
                // The common offense policy remains unchanged. An academy that has finished a military or siege
                // advantage asks that policy for an allocation on every tick, so its existing thresholds can fire a
                // little earlier without adding a civilisation flag branch inside OffenseDecision.
                bool academyOffenseTick = AcademyOffenseReady(f);
                allocations = world.Tick % 20 != 0 && !academyOffenseTick ? assessed : PolicyDecision.Allocate(targets, world.Tick, inputs, reserve, abandoned, attackMemory[f - 1], decisionPolicies, out reserveShortfall[f - 1], approachMemory[f - 1], offenseMemory[f - 1].CommittedReserveArmyIds, true);
                var waitGoal = OffenseDecision.WaitGoal(targets, offenseRoutes);
                OffenseDecision.Update(targets, world.Tick, offenseMemory[f - 1], inputs, own, allocations, offenseRoutes, world.Tick % 20 == 0 || academyOffenseTick,
                    policies.Any(c => c.Order.Kind == PolicyKind.MaintainReserve && c.Order.Source == CommandSource.Human), waitGoal);
                // SiegeByAge's late push is a committed core assault. The ordinary allocator may reserve or guard
                // every army when the home core is threatened, which makes a visible enemy core unreachable to Tactics
                // even though the authoritative policy says Focus/Core. Keep this exception inside the feature flag;
                // all other policies retain the normal reserve and defence allocation.
                if (world.Config.Economy.SiegeByAge)
                    for (int i = 0; i < inputs.Count; i++)
                        if (IsLatePushAttack(inputs[i].Army.Id) && !allocations[i].Returning)
                        {
                            allocations[i].Assignment = AssignmentKind.Advance;
                            allocations[i].Goal = inputs[i].Policy.Goal;
                        }
                }
                finally { measure?.Invoke("AI.Policy", false); }
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
                    foreach (var view in observation.OwnArmies.OrderBy(a => a.Id))
                    UpdateExplorePatrol(f, ref world.Armies[view.Id - 1]);
                }
                }
                finally { measure?.Invoke("AI.Routes", false); }
            }
            if (world.Tick % 20 == 0) lastAllocationTick = world.Tick;
        }

        private bool IsLatePushAttack(uint armyId) => IsPushWaveAssaultArmy(armyId) || liveCommands.Any(c => c.Status == CommandStatus.Executing
            && IsLatePushCoreFocus(c.Order)
            && c.Armies.Any(a => a.ArmyId == armyId && a.Active && !a.Failed && !a.Finished));

        // Simulation measures navigation; Decision selects the rally and evaluates all choices.
        private OffenseRouteInput[] OffenseRoutes(FactionObservation observation, IReadOnlyList<ArmyDecisionInput> inputs)
        {
            var result = offenseRoutesScratch;
            result.Clear();
            var home = PolicyDecision.Position(observation, PolicyDecision.Core(observation, true));
            bool sanctuaryFocus = SanctuaryOutpostFocus(observation.FactionId, observation);
            foreach (var goal in observation.Objectives.OrderBy(o => o.Kind).ThenBy(o => o.Id))
            {
                if (SanctuarySkipsGoal(observation.FactionId, sanctuaryFocus, goal.Kind, goal.Id)) continue;
                var policyGoal = new PolicyGoal(goal.Kind, goal.Id, default);
                var corePath = world.Map.FindPath(world.Map.Cell(home), goal.Position, observation.FactionId);
                if (corePath.Length == 0 || !InRange(world.Map.Center(corePath[corePath.Length - 1]), goal.Position, Fix64.FromInt(4))) continue;
                var state = offenseMemory[observation.FactionId - 1];
                SimPoint rally;
                var coreCenters = world.Map.FindPathCenterView(world.Map.Cell(home), goal.Position, observation.FactionId);
                if (state.Phase != OffensivePhase.Idle && state.Goal.Kind == goal.Kind && state.Goal.Id == goal.Id) rally = state.RallyPoint;
                else if (!OffenseDecision.TryRally(observation, coreCenters, out rally)) continue;
                var tail = world.Map.FindPath(world.Map.Cell(rally), goal.Position, observation.FactionId);
                var tailCenters = world.Map.FindPathCenterView(world.Map.Cell(rally), goal.Position, observation.FactionId);
                var legs = inputs.Select(i => {
                    var path = world.Map.FindPath(world.Map.Cell(i.Army.Position), rally, observation.FactionId);
                    if (path.Length > 0 && !SamePoint(world.Map.Center(path[path.Length - 1]), rally)) path = Array.Empty<int>();
                    var centers = world.Map.FindPathCenterView(world.Map.Cell(i.Army.Position), rally, observation.FactionId);
                    return new ObjectiveRoute(new PolicyGoal(GoalKind.Point, i.Army.Id, default), path.Length == 0 ? int.MaxValue : path.Length - 1, centers);
                }).ToArray();
                result.Add(new OffenseRouteInput(policyGoal, rally, tail.Length == 0 ? System.Array.Empty<SimPoint>() : tailCenters, legs, OffenseDecision.Length(coreCenters)));
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
                if (ExploreScoutingOn)
                {
                    w.Value(n + "ExplorePatrolActive", a.ExplorePatrolActive); w.Value(n + "ExplorePatrolAuto", a.ExplorePatrolAuto);
                    w.Value(n + "ExploreHasGoal", a.ExploreHasGoal); w.Point(n + "ExploreGoal", a.ExploreGoal);
                    w.Value(n + "ExploreGoalChunkX", a.ExploreGoalChunkX); w.Value(n + "ExploreGoalChunkZ", a.ExploreGoalChunkZ);
                    w.Value(n + "ExploreRetargetTick", a.ExploreRetargetTick);
                    w.Value(n + "ExploreAvoidChunkX", a.ExploreAvoidChunkX); w.Value(n + "ExploreAvoidChunkZ", a.ExploreAvoidChunkZ);
                    w.Value(n + "ExploreAvoidUntilTick", a.ExploreAvoidUntilTick);
                    w.Value(n + "ExploreFailedChunkX", a.ExploreFailedChunkX); w.Value(n + "ExploreFailedChunkZ", a.ExploreFailedChunkZ);
                    w.Value(n + "ExploreFailureCount", a.ExploreFailureCount);
                }
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
