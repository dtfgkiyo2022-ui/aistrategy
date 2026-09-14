using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>Week-one, single-threaded 20 Hz simulation. Step(1) transforms S0 into S1.</summary>
    public sealed partial class Simulation : ISimulation
    {
        private readonly WorldState world;
        private readonly SimPoint[] nextPositions;
        private readonly long[] soldierDamage, coreDamage;
        private readonly FactionFrame[] frames = new FactionFrame[2];
        private readonly FogView fog;

        public Simulation(ScenarioDefinition scenario)
        {
            world = new WorldState(scenario);
            nextPositions = new SimPoint[world.Soldiers.Length];
            soldierDamage = new long[world.Soldiers.Length];
            coreDamage = new long[world.Cores.Length];
            var cells = new bool[checked(world.Config.Map.WidthCells * world.Config.Map.HeightCells)];
            Array.Fill(cells, true);
            fog = new FogView(cells, cells);
            UpdateObservations();
            ResolveVictory();
            PublishFrames();
        }

        public void Step(long tick, IReadOnlyList<ScheduledInput> inputs)
        {
            if (world.Result.HasEnded) return;
            if (tick <= 0 || tick - 1 != world.Tick) throw new ArgumentOutOfRangeException(nameof(tick), "Expected the next consecutive tick.");
            ValidateInputs(tick, inputs); // A malformed call leaves the previous state untouched.
            try
            {
                world.Tick = tick;
                ApplyInputs(inputs);
                GenerateIntents();
                Move();
                Attack();
                ResolveDeaths();
                CaptureOutposts();
                Reinforce();
                UpdateObservations();
                ResolveVictory();
            }
            catch (ArithmeticException)
            {
                // A failed tick is diagnostic-only and terminal; it is never a victory or draw.
                world.Result = new MatchResult(true, 0, false, true, false);
            }
            PublishFrames();
        }

        private void ValidateInputs(long tick, IReadOnlyList<ScheduledInput> inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            foreach (var input in inputs)
            {
                if (input == null || input.ApplyTick != tick || input.AcceptedTick < 0 || input.AcceptedTick > tick
                    || (input.Kind != InputKind.Resolve && input.Kind != InputKind.Proposal))
                    throw new ArgumentException("Pass only resolved/proposed orders for this tick; scheduling is owned by the caller.", nameof(inputs));
                foreach (var order in input.Orders)
                {
                    if (order == null || order.Target.Kind != ScopeKind.Army || order.Target.Id == 0
                        || order.Target.Id > world.Armies.Length
                        || world.Armies[order.Target.Id - 1].Definition.FactionId != order.Target.FactionId)
                        throw new ArgumentException("Invalid army order.", nameof(inputs));
                    if (order.Kind == PolicyKind.Retreat) continue;
                    if (order.Kind != PolicyKind.Focus) throw new ArgumentException("Unsupported week-one policy.", nameof(inputs));
                    if (order.Goal.Kind == GoalKind.Point)
                        WorldState.ValidatePoint(order.Goal.Point, world.Config.Map);
                    else if (order.Goal.Kind != GoalKind.Core || order.Goal.Id == 0 || order.Goal.Id > world.Cores.Length
                        || world.Cores[order.Goal.Id - 1].Definition.FactionId == order.Target.FactionId)
                        throw new ArgumentException("Focus requires a point or enemy core.", nameof(inputs));
                }
            }
        }

        private void ApplyInputs(IReadOnlyList<ScheduledInput> inputs)
        {
            // Issue #16 owns reservations, revisions, batches and transitions. Preserve received order here.
            foreach (var input in inputs)
            {
                world.InputCursor = input.LogIndex;
                foreach (var order in input.Orders)
                {
                    ref var a = ref world.Armies[order.Target.Id - 1];
                    a.Policy = order.Kind;
                    a.Goal = order.Goal;
                    a.CommandId = order.CommandId;
                    a.AcceptedTick = input.AcceptedTick;
                    a.ApplyTick = input.ApplyTick;
                }
            }
        }

        private void GenerateIntents()
        {
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                s.IsMoving = s.IsAttacking = false;
                s.TargetKind = 0;
                s.TargetId = 0;
                if (!s.Alive) continue;
                var a = world.Armies[s.Initial.ArmyId - 1];
                s.IsRetreating = a.Policy == PolicyKind.Retreat;
                uint faction = s.Initial.FactionId;
                uint coreId = world.Factions[s.IsRetreating ? faction - 1 : 2 - faction].CoreId;
                s.MoveGoal = world.Cores[coreId - 1].Definition.Position;
                if (a.Policy == PolicyKind.Focus)
                    s.MoveGoal = a.Goal.Kind == GoalKind.Point ? a.Goal.Point : world.Cores[a.Goal.Id - 1].Definition.Position;
                if (s.IsRetreating) continue;

                // Candidates come from the previous tick's faction observation, never enemy HP/policies.
                var observation = frames[faction - 1].Observation;
                BigInteger best = 0;
                foreach (var enemy in observation.VisibleEnemies)
                {
                    uint id = InternalSoldierId(faction, enemy.ContactId);
                    Consider(ref s, enemy.Position, s.Parameters.Range, 1, id, ref best);
                }
                foreach (var objective in observation.Objectives)
                    if (objective.Kind == GoalKind.Core && objective.OwnerFactionId != faction && objective.Hp > 0)
                        Consider(ref s, objective.Position, s.Parameters.Range + world.Config.Rules.CoreRadius, 2, objective.Id, ref best);
            }
        }

        private static void Consider(ref SoldierState s, SimPoint position, Fix64 range, byte kind, uint id, ref BigInteger best)
        {
            BigInteger distance = DistanceSquared(s.Position, position);
            if (distance > new BigInteger(range.Raw) * range.Raw) return;
            if (s.TargetKind != 0 && (distance > best || (distance == best
                && (kind > s.TargetKind || (kind == s.TargetKind && id >= s.TargetId))))) return;
            best = distance;
            s.TargetKind = kind;
            s.TargetId = id;
        }

        private uint InternalSoldierId(uint faction, uint contactId)
        {
            foreach (int i in world.SoldierTraversal)
                if (world.Factions[faction - 1].ContactIds[i] == contactId) return (uint)i + 1;
            throw new InvalidOperationException("Observation contact has no internal mapping.");
        }

        private void Move()
        {
            foreach (int i in world.SoldierTraversal)
            {
                var s = world.Soldiers[i];
                nextPositions[i] = s.Alive ? FixMath.MoveTowards(s.Position, s.MoveGoal, s.StepDistance) : s.Position;
            }
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                s.IsMoving = s.Alive && !SamePoint(s.Position, nextPositions[i]);
                s.Position = nextPositions[i];
            }
        }

        private void Attack()
        {
            Array.Clear(soldierDamage, 0, soldierDamage.Length);
            Array.Clear(coreDamage, 0, coreDamage.Length);
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                if (!s.Alive || s.IsRetreating || s.TargetKind == 0 || world.Tick < s.NextAttackTick) continue;
                int target = checked((int)s.TargetId - 1);
                if (s.TargetKind == 1)
                {
                    var enemy = world.Soldiers[target];
                    if (!enemy.Alive || enemy.Initial.FactionId == s.Initial.FactionId || !InRange(s.Position, enemy.Position, s.Parameters.Range)) continue;
                    soldierDamage[target] = checked(soldierDamage[target] + s.Parameters.Damage);
                }
                else
                {
                    var core = world.Cores[target];
                    if (core.Hp <= 0 || core.Definition.FactionId == s.Initial.FactionId
                        || !InRange(s.Position, core.Definition.Position, s.Parameters.Range + world.Config.Rules.CoreRadius)) continue;
                    coreDamage[target] = checked(coreDamage[target] + s.Parameters.Damage);
                }
                s.NextAttackTick = checked(world.Tick + s.Parameters.AttackIntervalTicks);
                s.IsAttacking = true;
            }
            foreach (int i in world.SoldierTraversal)
                world.Soldiers[i].Hp = RemainingHp(world.Soldiers[i].Hp, soldierDamage[i]);
            foreach (var f in world.Factions)
                world.Cores[f.CoreId - 1].Hp = RemainingHp(world.Cores[f.CoreId - 1].Hp, coreDamage[f.CoreId - 1]);
        }

        // HP has a semantic floor of zero; accumulated damage uses checked long, never saturating arithmetic.
        private static int RemainingHp(int hp, long damage) => damage >= hp ? 0 : checked(hp - (int)damage);

        private void ResolveDeaths()
        {
            foreach (int i in world.SoldierTraversal)
                if (world.Soldiers[i].Hp == 0) world.Soldiers[i].Alive = false;
        }

        private void CaptureOutposts() { /* TODO: capture after deaths, in the later objective issue. */ }
        private void Reinforce() { /* TODO: new soldiers do not act in their birth tick. */ }

        private void UpdateObservations()
        {
            // No fog in week one. Allocate here (including S0), never from Capture.
            for (int f = 0; f < world.Factions.Length; f++)
            {
                ref var faction = ref world.Factions[f];
                faction.AliveCount = 0;
                foreach (int i in world.SoldierTraversal)
                {
                    var s = world.Soldiers[i];
                    if (!s.Alive) continue;
                    if (s.Initial.FactionId == faction.Id) faction.AliveCount++;
                    else if (faction.ContactIds[i] == 0)
                    {
                        faction.ContactIds[i] = faction.NextContactId;
                        faction.NextContactId = checked(faction.NextContactId + 1);
                    }
                }
            }
            // TODO: replace all-visible observation with the visibility/memory phase.
        }

        private void ResolveVictory()
        {
            bool first = world.Cores[world.Factions[0].CoreId - 1].Hp == 0;
            bool second = world.Cores[world.Factions[1].CoreId - 1].Hp == 0;
            if (first || second) world.Result = new MatchResult(true, first == second ? 0U : first ? 2U : 1U, first && second, false, false);
        }

        public FactionFrame Capture(uint factionId)
        {
            if (factionId < 1 || factionId > 2) throw new ArgumentOutOfRangeException(nameof(factionId));
            return frames[factionId - 1];
        }

        private void PublishFrames()
        {
            for (uint f = 1; f <= 2; f++)
            {
                var units = new List<RenderUnit>();
                var enemies = new List<VisibleEnemy>();
                var contacts = new List<EnemyContact>();
                var armies = new List<OwnArmyView>();
                var commands = new List<CommandView>();
                var objectives = new List<KnownObjective>();
                foreach (int i in world.SoldierTraversal)
                {
                    var s = world.Soldiers[i];
                    if (!s.Alive) continue;
                    bool own = s.Initial.FactionId == f;
                    uint id = own ? s.Initial.Id : world.Factions[f - 1].ContactIds[i];
                    units.Add(new RenderUnit(id, own, s.Initial.Kind, s.Position, s.IsMoving, s.IsAttacking,
                        own && s.IsRetreating, own, own ? s.Hp : 0));
                    if (!own)
                    {
                        enemies.Add(new VisibleEnemy(id, s.Position, (byte)s.Initial.Kind));
                        contacts.Add(new EnemyContact(id, s.Position, world.Tick, 1, 1, true));
                    }
                }
                foreach (uint id in world.Factions[f - 1].ArmyIds)
                {
                    var a = world.Armies[id - 1];
                    int count = 0;
                    SimPoint position = world.Cores[world.Factions[f - 1].CoreId - 1].Definition.Position;
                    UnitKind kind = a.Definition.Role == "scout" ? UnitKind.Scout : UnitKind.Infantry;
                    foreach (uint soldierId in a.SoldierIds)
                    {
                        var s = world.Soldiers[soldierId - 1];
                        if (!s.Alive) continue;
                        if (count++ == 0) { position = s.Position; kind = s.Initial.Kind; }
                    }
                    armies.Add(new OwnArmyView(id, f, kind, position, count, a.Definition.HomeObjective));
                    if (a.Policy != 0) commands.Add(new CommandView(a.CommandId, new ScopeKey(f, ScopeKind.Army, id),
                        a.Policy, CommandStatus.Executing, a.AcceptedTick, a.ApplyTick, ReasonCode.None));
                }
                foreach (var faction in world.Factions)
                {
                    var core = world.Cores[faction.CoreId - 1];
                    objectives.Add(new KnownObjective(GoalKind.Core, core.Definition.Id, core.Definition.Position,
                        true, core.Definition.FactionId, true, core.Hp, world.Tick));
                }
                foreach (var outpost in world.Config.Outposts)
                    objectives.Add(new KnownObjective(GoalKind.Outpost, outpost.Id, outpost.Position, true,
                        outpost.OwnerFactionId, false, 0, world.Tick));
                var observation = new FactionObservation(f, world.Tick, armies, enemies, contacts, objectives);
                // Combat event detail is deferred; terminal outcomes are already useful to the host.
                var events = world.Result.HasEnded ? new[] { new GameEvent(world.Tick, 0,
                    world.Result.IsFault ? EventKind.Fault : EventKind.MatchEnded, 3, 0, 0, default,
                    (int)world.Result.WinnerFactionId, ReasonCode.None) } : Array.Empty<GameEvent>();
                frames[f - 1] = new FactionFrame(world.Tick, f, units, observation, commands, events, fog, world.Result);
            }
        }

        private static bool SamePoint(SimPoint a, SimPoint b) => a.X == b.X && a.Z == b.Z;
        private static BigInteger DistanceSquared(SimPoint a, SimPoint b)
        {
            long dx = checked(a.X.Raw - b.X.Raw), dz = checked(a.Z.Raw - b.Z.Raw);
            return new BigInteger(dx) * dx + new BigInteger(dz) * dz;
        }
        private static bool InRange(SimPoint a, SimPoint b, Fix64 range) => DistanceSquared(a, b) <= new BigInteger(range.Raw) * range.Raw;
    }
}
