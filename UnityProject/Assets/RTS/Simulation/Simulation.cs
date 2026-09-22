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

        private readonly Action<string, bool> measure;

        /// <summary>
        /// Debug re-run only (chapter 13.3): receives the phase ordinal, its name and the SHA-256 of the canonical
        /// state at the end of each phase. Never read back into simulation decisions or canonical state.
        /// </summary>
        public Action<int, string, byte[]> PhaseHashObserver { get; set; }

        private int phaseOrdinal;

        // Diagnostic observer only; never read back into simulation decisions or canonical state.
        public Simulation(ScenarioDefinition scenario, Action<string, bool> measure = null)
        {
            this.measure = measure;
            world = new WorldState(scenario);
            world.Map.Measure = measure;
            nextPositions = new SimPoint[world.Soldiers.Length];
            soldierDamage = new long[world.Soldiers.Length];
            coreDamage = new long[world.Cores.Length];
            UpdateVisibility();
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
                phaseOrdinal = 0;
                commandEvents.Clear();
                Phase("Commands", () => { ApplyInputs(inputs); ApplyPendingCommands(); ComposePolicies(); });
                Phase("AI", () => { DecideArmies(); DecideEconomy(); });
                Phase("Commands", ComposePolicies);
                Phase("EnemySearchCombat", GenerateIntents);
                Phase("Movement", () => { Move(); MoveVillagers(); });
                Phase("Visibility", UpdateVisibility); // Post-movement combat visibility.
                Phase("EnemySearchCombat", () => { Attack(); ResolveDeaths(); });
                // With an economy, soldiers come only from production (technical-design-v3 4), so the free reinforcements stop.
                Phase("ObjectivesReinforcements", () => { EconomyStep(); CaptureOutposts(); if (!EconomyOn) Reinforce(); });
                Phase("Visibility", () => { UpdateVisibility(); UpdateObservations(); });
                Phase("Commands", () => { FinishCommands(); ComposePolicies(); });
                Phase("ObjectivesReinforcements", ResolveVictory);
            }
            catch (ArithmeticException)
            {
                // A failed tick is diagnostic-only and terminal; it is never a victory or draw.
                world.Result = new MatchResult(true, 0, false, true, false);
            }
            Phase("Frames", PublishFrames);
        }

        private void Phase(string name, Action action)
        {
            measure?.Invoke(name, true);
            try { action(); }
            finally { measure?.Invoke(name, false); }
            if (PhaseHashObserver != null)
            {
                phaseOrdinal++;
                var state = CaptureDiagnostic().CanonicalState;
                var bytes = new byte[state.Count];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = state[i];
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    PhaseHashObserver(phaseOrdinal, name, sha.ComputeHash(bytes));
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
                uint faction = s.Initial.FactionId;
                var observation = decisionObservations[faction - 1] ?? frames[faction - 1].Observation;
                var home = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
                bool returning = a.Policy == PolicyKind.Retreat || a.Decision.Returning || a.Policy == 0 && world.Tick < a.Decision.HoldUntilTick;
                var mission = ArmyGoal(a);
                var input = new TacticalInput(a.Definition.Id, world.Tick, s.Position, mission, home,
                    s.Parameters.Range, world.Config.Rules.CoreRadius, a.Policy, a.Decision.Assignment, returning);
                ArmyIntent intent;
                if (s.Initial.Kind == UnitKind.Scout && a.Policy == PolicyKind.Scout && !returning)
                {
                    bool seesEnemy = PolicyDecision.ScoutSeesEnemy(observation, s.Position, s.Parameters.Vision);
                    if (seesEnemy) s.Pursuit.Returning = true;

                    if (s.Pursuit.Returning)
                    {
                        var away = PolicyDecision.ScoutReturn(observation, s.Position, home, s.StepDistance);
                        intent = new ArmyIntent(a.Definition.Id, away, 0, default, true);
                    }
                    else intent = new ArmyIntent(a.Definition.Id, mission, 0, default, false);
                }
                else intent = PolicyDecision.Tactics(observation, input, ref s.Pursuit);
                s.MoveGoal = intent.MoveGoal; s.IsRetreating = intent.IsRetreating;
                if (s.IsRetreating && s.Initial.Kind == UnitKind.Scout && a.Policy != PolicyKind.Scout)
                    s.MoveGoal = PolicyDecision.ScoutReturn(observation, s.Position, s.MoveGoal, s.StepDistance);
                if (intent.TargetContactId != 0) { s.TargetKind = 1; s.TargetId = InternalSoldierId(faction, intent.TargetContactId); }
                else if (intent.TargetObjective.Kind == GoalKind.Core) { s.TargetKind = 2; s.TargetId = intent.TargetObjective.Id; }
                PickRaidTarget(ref s);

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
            if (world.Config.Map.BlockedCellIds.Length != 0 || !world.Config.Map.DefaultPassable || world.BuildingCount > 0) PrepareArmyPaths();
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
            if (EconomyOn) ClearRaidDamage();
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                if (!s.Alive || s.IsRetreating || s.TargetKind == 0 || world.Tick < s.NextAttackTick) continue;
                int target = checked((int)s.TargetId - 1);
                if (s.TargetKind == 1)
                {
                    var enemy = world.Soldiers[target];
                    if (!enemy.Alive || enemy.Initial.FactionId == s.Initial.FactionId || !IsVisibleTo(s.Initial.FactionId, enemy.Position) || !InRange(s.Position, enemy.Position, s.Parameters.Range)) continue;
                    soldierDamage[target] = checked(soldierDamage[target] + s.Parameters.Damage);
                }
                else if (s.TargetKind == TargetVillager || s.TargetKind == TargetBuilding) { AddRaidDamage(ref s); continue; }
                else
                {
                    var core = world.Cores[target];
                    if (core.Hp <= 0 || core.Definition.FactionId == s.Initial.FactionId
                        || !IsVisibleTo(s.Initial.FactionId, core.Definition.Position) || !InRange(s.Position, core.Definition.Position, s.Parameters.Range + world.Config.Rules.CoreRadius)) continue;
                    coreDamage[target] = checked(coreDamage[target] + s.Parameters.Damage);
                }
                s.NextAttackTick = checked(world.Tick + s.Parameters.AttackIntervalTicks);
                s.IsAttacking = true;
            }
            foreach (int i in world.SoldierTraversal)
                world.Soldiers[i].Hp = RemainingHp(world.Soldiers[i].Hp, soldierDamage[i]);
            foreach (var f in world.Factions)
                world.Cores[f.CoreId - 1].Hp = RemainingHp(world.Cores[f.CoreId - 1].Hp, coreDamage[f.CoreId - 1]);
            if (EconomyOn) ApplyRaidDamage();
        }

        // HP has a semantic floor of zero; accumulated damage uses checked long, never saturating arithmetic.
        private static int RemainingHp(int hp, long damage) => damage >= hp ? 0 : checked(hp - (int)damage);

        private void ResolveDeaths()
        {
            foreach (int i in world.SoldierTraversal)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Hp == 0)
                {
                    for (uint f = 1; f <= 2; f++)
                        if (world.Soldiers[i].Initial.FactionId != f && IsVisibleTo(f, world.Soldiers[i].Position))
                            world.Factions[f - 1].ContactIds[i] = 0;
                    world.Soldiers[i].Alive = false;
                }
            if (EconomyOn) ResolveRaidDeaths();
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
            for (int f = 0; f < world.Factions.Length; f++)
            {
                ref var faction = ref world.Factions[f];
                faction.AliveCount = 0;
                foreach (int i in world.SoldierTraversal)
                {
                    var s = world.Soldiers[i];
                    if (s.Alive && s.Initial.FactionId == faction.Id) faction.AliveCount++;
                    if (s.Initial.FactionId == faction.Id) continue;
                    bool visible = IsVisibleTo(faction.Id, s.Position);
                    if (s.Alive && visible)
                    {
                        if (faction.ContactIds[i] == 0) { faction.ContactIds[i] = faction.NextContactId; faction.NextContactId = checked(faction.NextContactId + 1); }
                        faction.ContactPositions[i] = s.Position;
                        faction.ContactLastSeenTicks[i] = world.Tick;
                        faction.ContactAbsent[i] = false;
                    }
                    else if (faction.ContactIds[i] != 0 && IsVisibleTo(faction.Id, faction.ContactPositions[i]))
                        faction.ContactAbsent[i] = true;
                }
                UpdateArmyContacts(ref faction);
            }
        }

        private void UpdateVisibility()
        {
            for (int f = 0; f < world.Factions.Length; f++)
            {
                ref var faction = ref world.Factions[f];
                Array.Clear(faction.VisibleCells, 0, faction.VisibleCells.Length);
                foreach (int i in world.SoldierTraversal)
                {
                    var s = world.Soldiers[i];
                    if (s.Alive && s.Initial.FactionId == faction.Id) Reveal(faction.VisibleCells, s.Position, s.Parameters.Vision);
                }
                var core = world.Cores[faction.CoreId - 1];
                if (core.Hp > 0) Reveal(faction.VisibleCells, core.Definition.Position, world.Config.Rules.OwnedObjectiveVision);
                for (int i = 0; i < world.Outposts.Length; i++)
                    if (world.Outposts[i].OwnerFactionId == faction.Id) Reveal(faction.VisibleCells, world.Outposts[i].Definition.Position, world.Config.Rules.OwnedObjectiveVision);
                for (int i = 0; i < faction.VisibleCells.Length; i++) if (faction.VisibleCells[i]) faction.ExploredCells[i] = true;
            }
        }

        private void Reveal(bool[] visible, SimPoint source, Fix64 radius)
        {
            long squared = checked(radius.Raw * radius.Raw);
            long cellRaw = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw;
            int minX = Math.Max(0, checked((int)((source.X.Raw - radius.Raw) / cellRaw) - 2));
            int maxX = Math.Min(world.Config.Map.WidthCells - 1, checked((int)((source.X.Raw + radius.Raw) / cellRaw) + 2));
            int minZ = Math.Max(0, checked((int)((source.Z.Raw - radius.Raw) / cellRaw) - 2));
            int maxZ = Math.Min(world.Config.Map.HeightCells - 1, checked((int)((source.Z.Raw + radius.Raw) / cellRaw) + 2));
            for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            {
                int cell = z * world.Config.Map.WidthCells + x;
                var center = world.Map.Center(cell);
                long dx = checked(source.X.Raw - center.X.Raw), dz = checked(source.Z.Raw - center.Z.Raw);
                if (checked(dx * dx + dz * dz) <= squared) visible[cell] = true;
            }
        }
        private bool IsVisibleTo(uint faction, SimPoint point)
        {
            int cell = world.Map.Cell(point);
            return cell >= 0 && cell < world.Factions[faction - 1].VisibleCells.Length && world.Factions[faction - 1].VisibleCells[cell];
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
                    if (!own && !IsVisibleTo(f, s.Position)) continue;
                    uint id = own ? s.Initial.Id : world.Factions[f - 1].ContactIds[i];
                    units.Add(new RenderUnit(id, own, s.Initial.Kind, s.Position, s.IsMoving, s.IsAttacking,
                        own && s.IsRetreating, own, own ? s.Hp : 0));
                    if (!own)
                    {
                        enemies.Add(new VisibleEnemy(id, s.Position, (byte)s.Initial.Kind));
                        contacts.Add(Contact(f, i, true));
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
                    objectives.Add(Objective(f, GoalKind.Core, core.Definition.Id, core.Definition.Position, core.Definition.FactionId, core.Hp, 0, 0, 0));
                }
                for (int oi = 0; oi < world.Outposts.Length; oi++) { var outpost = world.Outposts[oi];
                    objectives.Add(Objective(f, GoalKind.Outpost, outpost.Definition.Id, outpost.Definition.Position, outpost.OwnerFactionId, 0, outpost.CapturingFaction, outpost.CaptureTicks, world.Config.Rules.CaptureDurationTicks)); }
                for (int i = 0; i < world.SoldierCount; i++)
                    if (world.Soldiers[i].Initial.FactionId != f && world.Factions[f - 1].ContactIds[i] != 0 && !(world.Soldiers[i].Alive && IsVisibleTo(f, world.Soldiers[i].Position))) contacts.Add(Contact(f, i, false));
                AddArmyContacts(f, contacts);
                foreach (var command in commandStates)
                    if (command.Order.Target.FactionId == f)
                        commands.Add(new CommandView(command.Order.CommandId, command.Order.Target, command.Order.Kind, command.Order.Goal,
                            command.Status, command.AcceptedTick, command.ApplyTick, command.Reason, command.Order.Source));
                var observation = new FactionObservation(f, world.Tick, armies, enemies, contacts, objectives, world.Config.Rules.FactionCap);
                // Combat event detail is deferred; terminal outcomes are already useful to the host.
                var events = world.Result.HasEnded ? new[] { new GameEvent(world.Tick, 0,
                    world.Result.IsFault ? EventKind.Fault : EventKind.MatchEnded, 3, 0, 0, default,
                    (int)world.Result.WinnerFactionId, ReasonCode.None) } : Array.Empty<GameEvent>();
                var visibleEvents = new List<GameEvent>();
                foreach (var e in commandEvents)
                    if ((e.AudienceMask & (1 << ((int)f - 1))) != 0)
                        // The recipient must not learn whether the other faction also saw this event.
                        visibleEvents.Add(new GameEvent(e.Tick, (uint)visibleEvents.Count, e.Kind, (byte)(1 << ((int)f - 1)),
                            e.Kind == EventKind.Reinforcement && world.Soldiers[e.SubjectId - 1].Initial.FactionId != f
                                ? world.Factions[f - 1].ContactIds[e.SubjectId - 1] : e.SubjectId,
                            e.CommandId, e.Position, e.Value, e.Reason));
                foreach (var e in events) visibleEvents.Add(new GameEvent(e.Tick, (uint)visibleEvents.Count, e.Kind,
                    e.AudienceMask, e.SubjectId, e.CommandId, e.Position, e.Value, e.Reason));
                frames[f - 1] = new FactionFrame(world.Tick, f, units, observation, commands, visibleEvents,
                    new FogView(world.Factions[f - 1].VisibleCells, world.Factions[f - 1].ExploredCells), world.Result,
                    world.Factions[f - 1].AliveCount, world.Config.Rules.FactionCap, ReinforcementViews(f));
            }
        }

        private void UpdateArmyContacts(ref FactionState faction)
        {
            foreach (int armyIndex in world.ArmyTraversal)
            {
                var army = world.Armies[armyIndex];
                if (army.Definition.FactionId == faction.Id) continue;
                ref var memory = ref faction.ArmyContacts[armyIndex];
                var covered = new List<uint>();
                int visible = 0;
                SimPoint position = default;
                foreach (uint id in army.SoldierIds)
                {
                    int i = checked((int)id - 1);
                    uint contact = faction.ContactIds[i];
                    if (contact == 0) continue;
                    covered.Add(contact); // Only previously observed individuals, including frozen contacts.
                    if (!world.Soldiers[i].Alive || !IsVisibleTo(faction.Id, world.Soldiers[i].Position)) continue;
                    if (visible++ == 0) position = faction.ContactPositions[i];
                }
                memory.Visible = visible > 0;
                if (visible > 0)
                {
                    if (memory.Id == 0) { memory.Id = faction.NextArmyContactId; faction.NextArmyContactId = checked(faction.NextArmyContactId + 1); }
                    memory.Position = position;
                    memory.LastSeenTick = world.Tick;
                    memory.Max = checked(((visible + 4) / 5) * 5);
                    memory.Min = memory.Max - 4;
                    memory.Absent = false;
                    memory.Covered = covered.ToArray();
                }
                else if (memory.Id != 0)
                {
                    // Removal is justified only when every remembered individual was visibly killed.
                    if (covered.Count == 0) { memory = default; continue; }
                    if (IsVisibleTo(faction.Id, memory.Position)) memory.Absent = true;
                }
            }
        }

        private void AddArmyContacts(uint faction, List<EnemyContact> contacts)
        {
            foreach (var memory in world.Factions[faction - 1].ArmyContacts)
            {
                if (memory.Id == 0) continue;
                long age = world.Tick - memory.LastSeenTick;
                bool unknown = !memory.Visible && age >= 600;
                contacts.Add(new EnemyContact(memory.Id, memory.Position, memory.LastSeenTick,
                    unknown ? -1 : memory.Min, unknown ? -1 : memory.Max, memory.Visible,
                    memory.Covered, !memory.Visible && age >= 200, unknown, 10, memory.Absent, true));
            }
            contacts.Sort((a, b) => { int c = a.IsArmyContact.CompareTo(b.IsArmyContact); return c != 0 ? c : a.ContactId.CompareTo(b.ContactId); });
        }

        private EnemyContact Contact(uint factionId, int soldierIndex, bool visible)
        {
            ref var f = ref world.Factions[factionId - 1];
            long age = world.Tick - f.ContactLastSeenTicks[soldierIndex];
            bool unknown = !visible && age >= 600;
            return new EnemyContact(f.ContactIds[soldierIndex], f.ContactPositions[soldierIndex], f.ContactLastSeenTicks[soldierIndex],
                unknown ? -1 : 1, unknown ? -1 : 1, visible, null, !visible && age >= 200, unknown, 10, f.ContactAbsent[soldierIndex]);
        }
        private KnownObjective Objective(uint factionId, GoalKind kind, uint id, SimPoint position, uint owner, int hp, uint capturing, int captureTicks, int duration)
        {
            ref var f = ref world.Factions[factionId - 1]; int index = kind == GoalKind.Core ? checked((int)id - 1) : world.Cores.Length + checked((int)id - 1);
            bool own = owner == factionId; bool visible = own || IsVisibleTo(factionId, position);
            ref var memory = ref f.Objectives[index];
            if (kind == GoalKind.Core) { memory.OwnerKnown = true; memory.OwnerFactionId = owner; }
            if (visible) { memory.OwnerKnown = true; memory.OwnerFactionId = owner; memory.HpKnown = kind == GoalKind.Core; memory.Hp = hp; memory.LastSeenTick = world.Tick; }
            return new KnownObjective(kind, id, position, memory.OwnerKnown, memory.OwnerFactionId, memory.HpKnown, memory.Hp, memory.LastSeenTick,
                visible ? capturing : 0, visible ? captureTicks : 0, visible ? duration : 0);
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
