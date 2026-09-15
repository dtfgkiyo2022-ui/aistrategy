using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    /// <summary>Week-one, single-threaded 20 Hz simulation. Step(1) transforms S0 into S1.</summary>
    public sealed partial class Simulation : ISimulation
    {
        private readonly WorldState world;
        private SimPoint[] nextPositions;
        private long[] soldierDamage;
        private readonly long[] coreDamage;
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
                commandEvents.Clear();
                ApplyInputs(inputs);
                ApplyPendingCommands();
                ComposePolicies();
                DecideArmies();
                ComposePolicies();
                GenerateIntents();
                Move();
                Attack();
                ResolveDeaths();
                CaptureOutposts();
                Reinforce();
                UpdateObservations();
                FinishCommands();
                ComposePolicies();
                ResolveVictory();
            }
            catch (ArithmeticException)
            {
                // A failed tick is diagnostic-only and terminal; it is never a victory or draw.
                world.Result = new MatchResult(true, 0, false, true, false);
            }
            PublishFrames();
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
                uint faction = s.Initial.FactionId;
                var observation = decisionObservations[faction - 1] ?? frames[faction - 1].Observation;
                var home = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
                bool returning = a.Policy == PolicyKind.Retreat || a.Decision.Returning || a.Policy == 0 && world.Tick < a.Decision.HoldUntilTick;
                var mission = ArmyGoal(a);
                var input = new TacticalInput(a.Definition.Id, world.Tick, s.Position, mission, home,
                    s.Parameters.Range, world.Config.Rules.CoreRadius, a.Policy, a.Decision.Assignment, returning);
                var intent = PolicyDecision.Tactics(observation, input, ref s.Pursuit);
                s.MoveGoal = intent.MoveGoal; s.IsRetreating = intent.IsRetreating;
                if (s.IsRetreating && s.Initial.Kind == UnitKind.Scout)
                    s.MoveGoal = PolicyDecision.ScoutReturn(observation, s.Position, s.MoveGoal);
                if (intent.TargetContactId != 0) { s.TargetKind = 1; s.TargetId = InternalSoldierId(faction, intent.TargetContactId); }
                else if (intent.TargetObjective.Kind == GoalKind.Core) { s.TargetKind = 2; s.TargetId = intent.TargetObjective.Id; }

            }
        }

        private uint InternalSoldierId(uint faction, uint contactId)
        {
            foreach (int i in world.SoldierTraversal)
                if (world.Factions[faction - 1].ContactIds[i] == contactId) return (uint)i + 1;
            throw new InvalidOperationException("Observation contact has no internal mapping.");
        }

        private void Move()
        {
            if (world.Config.Map.BlockedCellIds.Length != 0 || !world.Config.Map.DefaultPassable) PrepareArmyPaths();
            foreach (int i in world.SoldierTraversal)
            {
                var s = world.Soldiers[i];
                nextPositions[i] = s.Alive ? world.Map.ClipMove(s.Position, FixMath.MoveTowards(s.Position, s.MoveGoal, s.StepDistance)) : s.Position;
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

        private void CaptureOutposts()
        {
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                ref var o = ref world.Outposts[i];
                int mask = 0;
                foreach (int soldier in world.SoldierTraversal)
                {
                    var s = world.Soldiers[soldier];
                    if (s.Alive && s.Initial.Kind == UnitKind.Infantry && InRange(s.Position, o.Definition.Position, world.Config.Rules.CaptureRadius))
                        mask |= 1 << ((int)s.Initial.FactionId - 1);
                }
                uint challenger = mask == 1 ? 1U : mask == 2 ? 2U : 0U;
                if (challenger == 0 || challenger == o.OwnerFactionId) { o.CapturingFaction = 0; o.CaptureTicks = 0; continue; }
                if (o.CapturingFaction != challenger) { o.CapturingFaction = challenger; o.CaptureTicks = 0; }
                o.CaptureTicks++;
                if (o.CaptureTicks >= world.Config.Rules.CaptureDurationTicks)
                { o.OwnerFactionId = challenger; o.CapturingFaction = 0; o.CaptureTicks = 0;
                    o.NextReinforcementTick = checked(world.Tick + world.Config.Rules.OutpostReinforcementIntervalTicks); }
            }
        }

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

                }
                foreach (var faction in world.Factions)
                {
                    var core = world.Cores[faction.CoreId - 1];
                    objectives.Add(new KnownObjective(GoalKind.Core, core.Definition.Id, core.Definition.Position,
                        true, core.Definition.FactionId, true, core.Hp, world.Tick));
                }
                foreach (var outpost in world.Outposts)
                    objectives.Add(new KnownObjective(GoalKind.Outpost, outpost.Definition.Id, outpost.Definition.Position, true,
                        outpost.OwnerFactionId, false, 0, world.Tick, outpost.CapturingFaction, outpost.CaptureTicks, world.Config.Rules.CaptureDurationTicks));
                foreach (var command in commandStates)
                    if (command.Order.Target.FactionId == f)
                        commands.Add(new CommandView(command.Order.CommandId, command.Order.Target, command.Order.Kind,
                            command.Status, command.AcceptedTick, command.ApplyTick, command.Reason));
                var observation = new FactionObservation(f, world.Tick, armies, enemies, contacts, objectives);
                // Combat event detail is deferred; terminal outcomes are already useful to the host.
                var events = world.Result.HasEnded ? new[] { new GameEvent(world.Tick, 0,
                    world.Result.IsFault ? EventKind.Fault : EventKind.MatchEnded, 3, 0, 0, default,
                    (int)world.Result.WinnerFactionId, ReasonCode.None) } : Array.Empty<GameEvent>();
                var visibleEvents = new List<GameEvent>();
                foreach (var e in commandEvents)
                    if ((e.AudienceMask & (1 << ((int)f - 1))) != 0)
                        visibleEvents.Add(new GameEvent(e.Tick, (uint)visibleEvents.Count, e.Kind, e.AudienceMask,
                            e.Kind == EventKind.Reinforcement && world.Soldiers[e.SubjectId - 1].Initial.FactionId != f
                                ? world.Factions[f - 1].ContactIds[e.SubjectId - 1] : e.SubjectId,
                            e.CommandId, e.Position, e.Value, e.Reason));
                foreach (var e in events) visibleEvents.Add(new GameEvent(e.Tick, (uint)visibleEvents.Count, e.Kind,
                    e.AudienceMask, e.SubjectId, e.CommandId, e.Position, e.Value, e.Reason));
                frames[f - 1] = new FactionFrame(world.Tick, f, units, observation, commands, visibleEvents, fog, world.Result,
                    world.Factions[f - 1].AliveCount, world.Config.Rules.FactionCap, ReinforcementViews(f));
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
