using System;
using System.Collections.Generic;
using System.Linq;
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
        private SimPoint[] visibilitySoldierPositions;
        private long[] visibilitySoldierVisionRaw;
        private bool[] visibilitySoldierActive;
        private SimPoint[] visibilityBuildingPositions;
        private long[] visibilityBuildingVisionRaw;
        private int[] visibilityBuildingFactions;
        private bool[] visibilityBuildingActive;
        private uint[] visibilityOutpostOwners;
        private bool[] visibilityCoreActive;
        private long visibilityChangedSoldiers;
        private long visibilityProcessedCells;
        private int movedSoldiers;
        private readonly FactionFrame[] frames = new FactionFrame[2];
        private bool automaticEconomyAction;

        private readonly Action<string, bool> measure;
        // Test-only comparison path; the shipped simulation always uses the optimized index.
        private readonly bool useLegacySpatialCombatIndex;
        // Test-only comparison path; the shipped simulation always uses the optimized visibility ranges.
        private bool useLegacyVisibility;

        internal long VisibilityChangedSoldiers => visibilityChangedSoldiers;
        internal long VisibilityProcessedCells => visibilityProcessedCells;
        internal int MovedSoldiers => movedSoldiers;

        /// <summary>
        /// Debug re-run only (chapter 13.3): receives the phase ordinal, its name and the SHA-256 of the canonical
        /// state at the end of each phase. Never read back into simulation decisions or canonical state.
        /// </summary>
        public Action<int, string, byte[]> PhaseHashObserver { get; set; }

        private int phaseOrdinal;

        // Diagnostic observer only; never read back into simulation decisions or canonical state.
        public Simulation(ScenarioDefinition scenario, Action<string, bool> measure = null)
            : this(scenario, measure, false)
        {
        }

        internal Simulation(ScenarioDefinition scenario, Action<string, bool> measure, bool useLegacySpatialCombatIndex)
        {
            this.measure = measure;
            this.useLegacySpatialCombatIndex = useLegacySpatialCombatIndex;
            world = new WorldState(scenario);
            InitializeSpatialCombatIndex();
            InitializeRegions();
            world.Map.Measure = measure;
            // Only a map with the tollgate rules gets the faction overlay; every other map keeps the plain terrain path.
            if (TollgateOn) { world.Map.FactionPassability = IsPassableForFaction; RefreshTollgateOwners(); }
            nextPositions = new SimPoint[world.Soldiers.Length];
            soldierDamage = new long[world.Soldiers.Length];
            coreDamage = new long[world.Cores.Length];
            InitializeVisibilityCaches();
            UpdateVisibility();
            UpdateObservations();
            ResolveVictory();
            PublishFrames();
        }

        internal Simulation(ScenarioDefinition scenario, Action<string, bool> measure, bool useLegacySpatialCombatIndex,
            bool useLegacyVisibility)
            : this(scenario, measure, useLegacySpatialCombatIndex)
        {
            this.useLegacyVisibility = useLegacyVisibility;
        }

        public void Step(long tick, IReadOnlyList<ScheduledInput> inputs)
        {
            if (world.Result.HasEnded) return;
            if (tick <= 0 || tick - 1 != world.Tick) throw new ArgumentOutOfRangeException(nameof(tick), "Expected the next consecutive tick.");
            ValidateInputs(tick, inputs); // A malformed call leaves the previous state untouched.
            try
            {
                world.Tick = tick;
                visibilityChangedSoldiers = 0;
                visibilityProcessedCells = 0;
                movedSoldiers = 0;
                phaseOrdinal = 0;
                commandEvents.Clear();
                PruneLiveCommands();
                Phase("Commands", () => { ApplyInputs(inputs); GenerateLatePushCommands(); ApplyPendingCommands(); ComposePolicies(); });
                Phase("AI", () => { DecideArmies(); DecideEconomy(); });
                Phase("Commands", ComposePolicies);
                Phase("EnemySearchCombat", () => { RebuildSpatialCombatIndex(); GenerateIntents(); });
                Phase("Movement", () => { Move(); MoveVillagers(); });
                Phase("Visibility", UpdateVisibility); // Post-movement combat visibility.
                Phase("EnemySearchCombat", () => { Attack(); ResolveConversions(); ResolveDeaths(); });
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
            // A phase may finish, destroy or remove a tollgate; the faction overlay follows at once (tollgate maps only).
            if (TollgateOn) RefreshTollgateOwners();
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
            uint cachedArmyId = 0;
            bool cachedHasHumanCommand = false, cachedHumanFocusMovement = false, cachedRegionBlocksMovement = false;
            bool cachedReturning = false, cachedAllocated = false, cachedReserve = false, cachedDefend = false;
            bool cachedWaveStaging = false, cachedAuthoritativeAssault = false;
            Fix64 cachedSearchRange = default;
            SimPoint cachedHome = default, cachedArmyGoal = default, cachedStagingPoint = default;
            FactionObservation cachedObservation = null;
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                s.IsMoving = s.IsAttacking = false;
                s.TargetKind = 0;
                s.TargetId = 0;
                if (!s.Alive) continue;
                ref var a = ref world.Armies[s.Initial.ArmyId - 1];
                if (cachedArmyId != a.Definition.Id)
                {
                    cachedArmyId = a.Definition.Id;
                    cachedHasHumanCommand = world.Config.Economy.DirectPathMovement && a.CommandId != 0 && CommandById(a.CommandId)?.Order.Source == CommandSource.Human;
                    cachedHumanFocusMovement = world.Config.Economy.DirectPathMovement && cachedHasHumanCommand && a.Policy == PolicyKind.Focus;
                    cachedRegionBlocksMovement = !cachedHasHumanCommand && RegionBlocksAiArmy(a);
                    cachedReturning = !(cachedHasHumanCommand && a.Policy == PolicyKind.Focus)
                        && (a.Policy == PolicyKind.Retreat || a.Decision.Returning || a.Policy == 0 && world.Tick < a.Decision.HoldUntilTick);
                    cachedObservation = decisionObservations[s.Initial.FactionId - 1] ?? frames[s.Initial.FactionId - 1].Observation;
                    cachedHome = world.Cores[world.Factions[s.Initial.FactionId - 1].CoreId - 1].Definition.Position;
                    cachedArmyGoal = ArmyGoal(a);
                    cachedStagingPoint = pushWaveStates[s.Initial.FactionId - 1].StagingPoint;
                    cachedAllocated = a.Policy == 0 || a.Policy == PolicyKind.Focus;
                    cachedReserve = cachedAllocated && (a.Decision.Assignment == AssignmentKind.Reserve || a.Decision.Assignment == AssignmentKind.CoreDefense);
                    cachedDefend = a.Policy == PolicyKind.Defend || cachedAllocated && a.Decision.Assignment == AssignmentKind.Guard;
                    cachedSearchRange = Fix64.FromInt(cachedReserve ? 24 : cachedDefend ? 12 : 24);
                    cachedWaveStaging = PushWavesOn && IsPushWaveStagingArmy(a.Definition.Id);
                    cachedAuthoritativeAssault = IsLatePushAttack(a.Definition.Id);
                }
                bool hasHumanCommand = cachedHasHumanCommand;
                bool humanFocusMovement = cachedHumanFocusMovement;
                bool regionBlocksMovement = cachedRegionBlocksMovement;
                uint faction = s.Initial.FactionId;
                var observation = cachedObservation;
                var home = cachedHome;
                bool returning = cachedReturning;
                var mission = IsPushWaveAssaultSoldier(s.Initial.Id) ? cachedArmyGoal
                    : cachedWaveStaging ? cachedStagingPoint : cachedArmyGoal;
                var input = new TacticalInput(a.Definition.Id, world.Tick, s.Position, mission, home,
                    s.Parameters.Range, world.Config.Rules.CoreRadius, a.Policy, a.Decision.Assignment, returning);
                ArmyIntent intent;
                if (s.Initial.Kind == UnitKind.Scout && (a.ExplorePatrolActive || a.Policy == PolicyKind.Scout) && !returning)
                {
                    int nearbyEnemies = QueryVisibleEnemies(faction, s.Position, s.Parameters.Vision);
                    bool seesEnemy = PolicyDecision.ScoutSeesEnemy(observation, s.Position, s.Parameters.Vision,
                        spatialEnemyBuffer, nearbyEnemies);
                    if (seesEnemy)
                    {
                        if (a.ExplorePatrolActive)
                        {
                            uint lowestContact = 0; SimPoint enemyPosition = default;
                            for (int enemyIndex = 0; enemyIndex < nearbyEnemies; enemyIndex++)
                            {
                                var enemy = spatialEnemyBuffer[enemyIndex];
                                if (PolicyDecision.SegmentLength(enemy.Position, s.Position) > s.Parameters.Vision.Raw
                                    || lowestContact != 0 && enemy.ContactId >= lowestContact) continue;
                                lowestContact = enemy.ContactId; enemyPosition = enemy.Position;
                            }
                            if (lowestContact != 0)
                            {
                                int cell = world.Map.Cell(enemyPosition);
                                if (cell >= 0)
                                {
                                    a.ExploreAvoidChunkX = cell % world.Config.Map.WidthCells / world.Config.Economy.ExploreChunkCells;
                                    a.ExploreAvoidChunkZ = cell / world.Config.Map.WidthCells / world.Config.Economy.ExploreChunkCells;
                                    a.ExploreAvoidUntilTick = checked(world.Tick + world.Config.Economy.ExploreAvoidTicks);
                                }
                            }
                        }
                        s.Pursuit.Returning = true;
                    }

                    if (s.Pursuit.Returning)
                    {
                        int returnEnemies = QueryVisibleEnemies(faction, s.Position, Fix64.FromInt(12) + s.StepDistance);
                        var away = PolicyDecision.ScoutReturn(observation, s.Position, home, s.StepDistance,
                            spatialEnemyBuffer, returnEnemies);
                        intent = new ArmyIntent(a.Definition.Id, away, 0, default, true);
                    }
                    else intent = new ArmyIntent(a.Definition.Id, mission, 0, default, false);
                }
                else if (s.Initial.Kind == UnitKind.Monk && CultAllowed(faction))
                {
                    if (a.Policy == 0) intent = CultMonkIntent(ref s, a, faction, home, mission, observation);
                    else
                    {
                        // A human policy supplies movement only.  Do not let the common tactical policy turn a
                        // monk's opportunistic contact into an automatic conversion or an automatic retreat.
                        s.CultTargetContactId = 0;
                        var humanInput = new TacticalInput(a.Definition.Id, world.Tick, s.Position, mission, home,
                            s.Parameters.Range, world.Config.Rules.CoreRadius, a.Policy, a.Decision.Assignment,
                            a.Policy == PolicyKind.Retreat);
                        var controlled = PolicyDecision.Tactics(observation, humanInput, ref s.Pursuit);
                        intent = new ArmyIntent(a.Definition.Id, controlled.MoveGoal, 0, default, controlled.IsRetreating);
                    }
                }
                else
                {
                    bool allocated = cachedAllocated;
                    bool reserve = cachedReserve;
                    bool defend = cachedDefend;
                    SimPoint searchCenter = reserve ? home : defend ? mission : s.Position;
                    Fix64 searchRange = cachedSearchRange;
                    // Tactics clears a completed return order before evaluating enemies.  Keep the
                    // same query available on the arrival tick; suppressing it unconditionally
                    // changes the decision at the exact tick the soldier reaches its mission.
                    int nearbyEnemies = QueryVisibleEnemies(faction, searchCenter, searchRange);
                    intent = PolicyDecision.Tactics(observation, input, spatialEnemyBuffer, nearbyEnemies, ref s.Pursuit);
                }
                bool waveStaging = cachedWaveStaging
                    && !IsPushWaveAssaultSoldier(s.Initial.Id);
                if (waveStaging && intent.TargetContactId == 0 && intent.TargetObjective.Kind == GoalKind.None)
                    intent = new ArmyIntent(a.Definition.Id, pushWaveStates[faction - 1].StagingPoint, 0, default, false);
                // A SiegeByAge late push has an authoritative core target. Once a soldier is inside its attack
                // envelope, do not let a one-tick stale fog observation turn the final approach into idle movement.
                bool authoritativeAssault = cachedAuthoritativeAssault
                    && (IsPushWaveAssaultSoldier(s.Initial.Id) || !PushWavesOn);
                if (authoritativeAssault && a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Core
                    && (!PushWavesOn || intent.TargetContactId == 0))
                {
                    bool inCoreRange = InRange(s.Position, world.Cores[a.Goal.Id - 1].Definition.Position,
                        s.Parameters.Range + world.Config.Rules.CoreRadius);
                    // Keep the assault route fixed when normal Tactics has no unit to fight. A contact selected by
                    // Tactics above remains authoritative, so a soldier in engagement range attacks soldiers first.
                    intent = new ArmyIntent(a.Definition.Id, world.Cores[a.Goal.Id - 1].Definition.Position,
                        0, inCoreRange ? a.Goal : default, false);
                }
                // A human-owned region suppresses only autonomous movement.  Tactics still runs so an enemy already
                // in range (or an enemy core in range) gets a target and can be attacked while the soldier stays put.
                // A human Focus owns the route while DirectPathMovement is on.  Tactical contact selection still
                // supplies an attack target, but its pursuit/retreat point must not replace the clicked destination.
                if (humanFocusMovement) s.Pursuit = default;
                s.MoveGoal = regionBlocksMovement ? s.Position : humanFocusMovement ? mission : intent.MoveGoal;
                s.IsRetreating = regionBlocksMovement || humanFocusMovement ? false : intent.IsRetreating;
                if (s.IsRetreating && s.Initial.Kind == UnitKind.Scout && a.Policy != PolicyKind.Scout)
                {
                    int returnEnemies = QueryVisibleEnemies(faction, s.Position, Fix64.FromInt(12) + s.StepDistance);
                    s.MoveGoal = PolicyDecision.ScoutRetreatHome(observation, s.Position, s.MoveGoal, s.StepDistance,
                        spatialEnemyBuffer, returnEnemies);
                }
                if (intent.TargetContactId != 0) { s.TargetKind = 1; s.TargetId = InternalSoldierId(faction, intent.TargetContactId); }
                else if (intent.TargetObjective.Kind == GoalKind.Core) { s.TargetKind = 2; s.TargetId = intent.TargetObjective.Id; }
                PickRaidTarget(ref s);

            }
        }

        /// <summary>V3-13 #2: an autonomous cult monk uses only currently visible enemy contacts.</summary>
        private ArmyIntent CultMonkIntent(ref SoldierState monk, ArmyState army, uint faction, SimPoint home, SimPoint mission,
            FactionObservation observation)
        {
            int guardian = -1;
            foreach (uint id in army.SoldierIds)
            {
                int index = checked((int)id - 1);
                var candidate = world.Soldiers[index];
                if (!candidate.Alive || candidate.Initial.Kind == UnitKind.Monk || candidate.Class == UnitKind.Monk) continue;
                if (guardian < 0 || DistanceSquared(candidate.Position, monk.Position) < DistanceSquared(world.Soldiers[guardian].Position, monk.Position)) guardian = index;
            }
            if (guardian < 0) return new ArmyIntent(army.Definition.Id, home, 0, default, true);

            var escort = world.Soldiers[guardian];
            bool escortReady = InRange(monk.Position, escort.Position, Fix64.FromInt(12));

            // Keep a target while it remains visible and alive.  A newly visible, higher-value unit must not reset
            // conversion progress by causing a target switch every decision tick.
            VisibleEnemyView current = default;
            bool hasCurrent = TryVisibleCultTarget(observation, monk.CultTargetContactId, out current)
                && TryInternalSoldierId(faction, current.ContactId, out int currentIndex)
                && world.Soldiers[currentIndex].Alive;
            if (!hasCurrent) monk.CultTargetContactId = 0;

            int nearbyRanged = hasCurrent ? CultNearbyRanged(observation, current.Position, current.ContactId) : 0;
            if (nearbyRanged > 0 || CultNearbyRanged(observation, monk.Position, 0) > 0)
                return new ArmyIntent(army.Definition.Id, home, 0, default, true);

            uint claimed = 0;
            var targets = new List<VisibleEnemyView>();
            foreach (var visible in observation.VisibleEnemies.OrderBy(e => e.ContactId))
            {
                UnitKind kind = (UnitKind)visible.Kind;
                if (kind == UnitKind.Monk || !TryInternalSoldierId(faction, visible.ContactId, out int index)) continue;
                var enemy = world.Soldiers[index];
                if (!enemy.Alive || enemy.Initial.FactionId == faction) continue;
                if (CultTargetClaimedByEarlierMonk(army, monk.Initial.Id, visible.ContactId)) continue;
                targets.Add(new VisibleEnemyView(visible.ContactId, visible.Position, kind,
                    CultNearbyRanged(observation, visible.Position, visible.ContactId), DistanceRank(monk.Position, visible.Position)));
            }

            if (hasCurrent && !CultTargetClaimedByEarlierMonk(army, monk.Initial.Id, current.ContactId))
                claimed = current.ContactId;
            else
            {
                var choice = targets.OrderByDescending(t => CultTargetValue(t.Kind)).ThenBy(t => t.Danger)
                    .ThenBy(t => t.Distance).ThenBy(t => t.ContactId).FirstOrDefault();
                if (choice.ContactId != 0)
                {
                    monk.CultTargetContactId = choice.ContactId;
                    current = choice;
                    hasCurrent = true;
                    claimed = choice.ContactId;
                }
                else hasCurrent = false;
            }

            if (!hasCurrent || claimed == 0) return new ArmyIntent(army.Definition.Id, escort.Position, 0, default, false);
            // Selection is based on the faction's current visible contacts even while the monk catches up.  Until
            // the escort is close enough, the stored target is retained but movement remains with the escort.
            if (!escortReady) return new ArmyIntent(army.Definition.Id, escort.Position, 0, default, false);
            int target = checked((int)InternalSoldierId(faction, claimed) - 1);
            var prey = world.Soldiers[target];
            SimPoint approach = FixMath.MoveTowards(escort.Position, prey.Position, Fix64.FromInt(4));
            uint contact = claimed;
            if (contact != 0 && InRange(monk.Position, prey.Position, monk.Parameters.Range))
                return new ArmyIntent(army.Definition.Id, monk.Position, contact, default, false);
            return new ArmyIntent(army.Definition.Id, approach, contact, default, false);
        }

        private readonly struct VisibleEnemyView
        {
            internal readonly uint ContactId;
            internal readonly SimPoint Position;
            internal readonly UnitKind Kind;
            internal readonly int Danger, Distance;
            internal VisibleEnemyView(uint contactId, SimPoint position, UnitKind kind, int danger, int distance)
            { ContactId = contactId; Position = position; Kind = kind; Danger = danger; Distance = distance; }
        }

        private static int CultTargetValue(UnitKind kind)
        {
            return kind == UnitKind.HeavyInfantry ? 6 : kind == UnitKind.Cavalry ? 5 : kind == UnitKind.LightCavalry ? 4
                : kind == UnitKind.Mercenary ? 3 : kind == UnitKind.Archer ? 2 : kind == UnitKind.SkirmishArcher ? 2 : 1;
        }

        private bool TryVisibleCultTarget(FactionObservation observation, uint contactId, out VisibleEnemyView target)
        {
            foreach (var visible in observation.VisibleEnemies)
            {
                if (visible.ContactId != contactId) continue;
                UnitKind kind = (UnitKind)visible.Kind;
                if (kind == UnitKind.Monk) break;
                target = new VisibleEnemyView(contactId, visible.Position, kind,
                    CultNearbyRanged(observation, visible.Position, contactId), 0);
                return true;
            }
            target = default;
            return false;
        }

        private int CultNearbyRanged(FactionObservation observation, SimPoint point, uint excludeContactId)
        {
            int count = 0;
            foreach (var enemy in observation.VisibleEnemies)
            {
                UnitKind kind = (UnitKind)enemy.Kind;
                if (enemy.ContactId == excludeContactId || !CultIsRanged(kind)) continue;
                if (InRange(point, enemy.Position, Fix64.FromInt(16))) count++;
            }
            return count;
        }

        private static bool CultIsRanged(UnitKind kind) => kind == UnitKind.Archer || kind == UnitKind.SkirmishArcher;

        private bool CultTargetClaimedByEarlierMonk(ArmyState army, uint monkId, uint contactId)
        {
            foreach (uint id in army.SoldierIds)
            {
                if (id >= monkId) continue;
                var other = world.Soldiers[id - 1];
                if (other.Alive && other.Initial.Kind == UnitKind.Monk && other.CultTargetContactId == contactId) return true;
            }
            return false;
        }

        private static int DistanceRank(SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            long value = Math.Abs(dx / Fix64.FromInt(1).Raw) + Math.Abs(dz / Fix64.FromInt(1).Raw);
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        private uint InternalSoldierId(uint faction, uint contactId)
        {
            if (TryCombatContact(faction, contactId, out int mapped)) return checked((uint)mapped + 1);
            foreach (int i in world.SoldierTraversal)
                if (world.Factions[faction - 1].ContactIds[i] == contactId) return (uint)i + 1;
            throw new InvalidOperationException("Observation contact has no internal mapping.");
        }

        private bool TryInternalSoldierId(uint faction, uint contactId, out int index)
        {
            if (TryCombatContact(faction, contactId, out index)) return true;
            foreach (int i in world.SoldierTraversal)
                if (world.Factions[faction - 1].ContactIds[i] == contactId)
                { index = i; return true; }
            index = -1;
            return false;
        }

        private void Move()
        {
            movedSoldiers = 0;
            if (world.Config.Map.BlockedCellIds.Length != 0 || !world.Config.Map.DefaultPassable || world.BuildingCount > 0) PrepareArmyPaths();
            foreach (int i in world.SoldierTraversal)
            {
                var s = world.Soldiers[i];
                nextPositions[i] = s.Alive ? world.Map.ClipMove(s.Position, FixMath.MoveTowards(s.Position, s.MoveGoal, s.StepDistance), s.Initial.FactionId) : s.Position;
            }
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                s.IsMoving = s.Alive && !SamePoint(s.Position, nextPositions[i]);
                if (s.IsMoving) movedSoldiers++;
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
                if (s.Initial.Kind == UnitKind.Monk && !world.Config.Economy.MonksEnabled) continue;
                int target = checked((int)s.TargetId - 1);
                if (s.TargetKind == 1)
                {
                    var enemy = world.Soldiers[target];
                    if (!enemy.Alive || enemy.Initial.FactionId == s.Initial.FactionId || !IsVisibleTo(s.Initial.FactionId, enemy.Position) || !InRange(s.Position, enemy.Position, s.Parameters.Range)) continue;
                    if (RationsOn) { s.LastCombatTick = world.Tick; world.Soldiers[target].LastCombatTick = world.Tick; }
                    if (s.Initial.Kind == UnitKind.Monk)
                    {
                        // V3-5 #22: conversion is a progress tick, not combat damage. A target accepts at most one
                        // conversion tick per simulation tick, even when several monks select it.
                        if (enemy.Initial.Kind == UnitKind.Monk || enemy.Class == UnitKind.Monk) continue;
                        ref var conversionTarget = ref world.Soldiers[target];
                        if (conversionTarget.ConversionLastAttackTick != world.Tick)
                        {
                            if (conversionTarget.ConversionByFaction != s.Initial.FactionId)
                            {
                                conversionTarget.ConversionProgress = 0;
                                conversionTarget.ConversionByFaction = s.Initial.FactionId;
                            }
                            conversionTarget.ConversionProgress = checked(conversionTarget.ConversionProgress + 1);
                            conversionTarget.ConversionLastAttackTick = world.Tick;
                        }
                        else if (conversionTarget.ConversionByFaction != s.Initial.FactionId)
                        {
                            // A different faction can take over in the same tick, but the tick still counts only once.
                            conversionTarget.ConversionProgress = 1;
                            conversionTarget.ConversionByFaction = s.Initial.FactionId;
                        }
                    }
                    else
                    {
                        // V3-5 (32 #13): the counter triangle is read here, at the blow, so it needs no new state.
                        int damage = Rts.Decision.CombatMath.DamageAgainst(s.Parameters.Damage, s.Class, enemy.Class, world.Config.Economy.CounterBonusPermille);
                        damage = SanctuaryDamage(s.Initial.FactionId, damage);
                        damage = TollgateDefenceDamage(enemy.Initial.FactionId, enemy.Position, damage);
                        soldierDamage[target] = checked(soldierDamage[target] + damage);
                    }
                }
                else if (s.TargetKind == TargetVillager || s.TargetKind == TargetBuilding || s.TargetKind == TargetBelt) { if (RationsOn) s.LastCombatTick = world.Tick; AddRaidDamage(ref s); continue; }
                else
                {
                    var core = world.Cores[target];
                    if (core.Hp <= 0 || core.Definition.FactionId == s.Initial.FactionId
                        || !IsVisibleTo(s.Initial.FactionId, core.Definition.Position) || !InRange(s.Position, core.Definition.Position, s.Parameters.Range + world.Config.Rules.CoreRadius)) continue;
                    if (RationsOn) s.LastCombatTick = world.Tick;
                    coreDamage[target] = checked(coreDamage[target] + SanctuaryDamage(s.Initial.FactionId, SiegeDamage(s)));
                }
                s.NextAttackTick = checked(world.Tick + s.Parameters.AttackIntervalTicks);
                s.IsAttacking = true;
            }
            if (AgesOn) TowersShoot();
            CoresShoot();
            foreach (int i in world.SoldierTraversal)
                world.Soldiers[i].Hp = RemainingHp(world.Soldiers[i].Hp, soldierDamage[i]);
            foreach (var f in world.Factions)
                world.Cores[f.CoreId - 1].Hp = RemainingHp(world.Cores[f.CoreId - 1].Hp, coreDamage[f.CoreId - 1]);
            if (EconomyOn) ApplyRaidDamage();
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                if (s.Alive && s.ConversionLastAttackTick != world.Tick)
                {
                    s.ConversionProgress = 0;
                    s.ConversionByFaction = 0;
                }
            }
        }

        // HP has a semantic floor of zero; accumulated damage uses checked long, never saturating arithmetic.
        private static int RemainingHp(int hp, long damage) => damage >= hp ? 0 : checked(hp - (int)damage);

        /// <summary>V3-18 #1: applies the fixed-point army bonus only to soldiers of the sanctuary civilisation.</summary>
        private int SanctuaryDamage(uint faction, int damage)
        {
            if (!SanctuaryAllowed(faction) || damage <= 0) return damage;
            int shrines = ActiveSanctuaryCount(faction);
            // V3-18 #2: the holy relic replaces the per-sanctuary rate and the cap with its larger values.
            var (perSanctuary, cap) = SanctuaryBonusRules(faction);
            int bonus = Math.Min(checked(shrines * perSanctuary), cap);
            if (bonus <= 0) return damage;
            Fix64 multiplier = Fix64.FromRatio(1000L + bonus, 1000L);
            // Fixed-point multiplication is rounded to the nearest integer so a
            // nominal 10% bonus remains 110 damage for a 100-damage blow.
            long raw = (Fix64.FromInt(damage) * multiplier).Raw;
            return checked((int)((raw + 32768L) / 65536L));
        }

        private void ResolveConversions()
        {
            if (!world.Config.Economy.MonksEnabled || world.Config.Economy.ConversionTicks <= 0) return;
            foreach (int i in world.SoldierTraversal)
            {
                ref var target = ref world.Soldiers[i];
                uint faction = target.ConversionByFaction;
                if (!target.Alive || target.Hp <= 0 || faction == 0) continue;
                int ticks = ConversionTicksFor(faction);
                if (target.ConversionProgress < ticks) continue;
                UnitKind originalClass = target.Class;
                UnitKind convertedKind = originalClass != 0 ? originalClass : target.Initial.Kind;
                UnitKind spawnKind = originalClass != 0 ? UnitKind.Infantry : convertedKind;
                SimPoint position = target.Position;
                uint core = world.Factions[faction - 1].CoreId;
                target.ConversionProgress = 0;
                target.ConversionByFaction = 0;
                target.ConversionLastAttackTick = 0;
                target.Hp = 0;
                // The nearest-army assignment is a cult-side rule.  Keep the common conversion
                // path's historical reinforcement priority for every other faction.
                bool spawned = CultAllowed(faction)
                    ? SpawnConverted(faction, position, spawnKind, spawnKind)
                    : Spawn(faction, GoalKind.Core, core, position, spawnKind, spawnKind);
                if (spawned)
                    ApplyClass(world.SoldierCount - 1, originalClass == 0 ? 0 : convertedKind);
            }
        }

        /// <summary>Conversion duration is selected by the faction that is performing the conversion.</summary>
        private int ConversionTicksFor(uint faction)
        {
            int ticks = world.Config.Economy.ConversionTicks;
            if (CultAllowed(faction) && HasTech(faction, CultTech.Sermon)) ticks = checked(ticks * 2 / 3);
            return Math.Max(1, ticks);
        }

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
            measure?.Invoke("Visibility.Observation", true);
            try
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
                        if (faction.ContactIds[i] == 0) { faction.ContactIds[i] = faction.NextContactId; RememberCombatContact(faction.Id, faction.ContactIds[i], i); faction.NextContactId = checked(faction.NextContactId + 1); }
                        faction.ContactPositions[i] = s.Position;
                        faction.ContactLastSeenTicks[i] = world.Tick;
                        faction.ContactAbsent[i] = false;
                        if (CultOn)
                        {
                            UnitKind observedKind = s.Class != 0 ? s.Class : s.Initial.Kind;
                            if (CultObservedValue(observedKind) > 0)
                            {
                                faction.CultObservedContactIds[i] = faction.ContactIds[i];
                                faction.CultObservedKinds[i] = observedKind;
                                faction.CultObservedLastSeenTicks[i] = world.Tick;
                                faction.CultObservedValidUntilTicks[i] = checked(world.Tick + CultObservationValidityTicks);
                            }
                        }
                    }
                    else if (faction.ContactIds[i] != 0 && IsVisibleTo(faction.Id, faction.ContactPositions[i]))
                        faction.ContactAbsent[i] = true;
                }
                UpdateArmyContacts(ref faction);
            }
            }
            finally { measure?.Invoke("Visibility.Observation", false); }
        }

        private void UpdateVisibility()
        {
            EnsureVisibilityCaches();
            measure?.Invoke("Visibility.ChangeDetection", true);
            try
            {
            for (int i = 0; i < world.Soldiers.Length; i++)
            {
                ref var s = ref world.Soldiers[i];
                int faction = checked((int)s.Initial.FactionId) - 1;
                bool active = s.Alive && faction >= 0 && faction < world.Factions.Length;
                long visionRaw = s.Parameters.Vision.Raw;
                bool changed = visibilitySoldierActive[i] && (!active || visibilitySoldierPositions[i].X.Raw != s.Position.X.Raw
                    || visibilitySoldierPositions[i].Z.Raw != s.Position.Z.Raw || visibilitySoldierVisionRaw[i] != visionRaw)
                    || active && (!visibilitySoldierActive[i] || visibilitySoldierPositions[i].X.Raw != s.Position.X.Raw
                    || visibilitySoldierPositions[i].Z.Raw != s.Position.Z.Raw || visibilitySoldierVisionRaw[i] != visionRaw);
                if (changed)
                {
                    visibilityChangedSoldiers++;
                    if (useLegacyVisibility)
                    {
                        if (visibilitySoldierActive[i]) ChangeVisibilityLegacy(faction, visibilitySoldierPositions[i], visibilitySoldierVisionRaw[i], -1);
                        if (active) ChangeVisibilityLegacy(faction, s.Position, visionRaw, 1);
                    }
                    else if (visibilitySoldierActive[i] && active)
                        ChangeVisibilityDelta(faction, visibilitySoldierPositions[i], visibilitySoldierVisionRaw[i], s.Position, visionRaw);
                    else if (visibilitySoldierActive[i]) ChangeVisibility(faction, visibilitySoldierPositions[i], visibilitySoldierVisionRaw[i], -1);
                    else if (active) ChangeVisibility(faction, s.Position, visionRaw, 1);
                }
                visibilitySoldierActive[i] = active;
                visibilitySoldierPositions[i] = s.Position;
                visibilitySoldierVisionRaw[i] = visionRaw;
            }

            for (int f = 0; f < world.Factions.Length; f++)
            {
                ref var faction = ref world.Factions[f];
                var core = world.Cores[faction.CoreId - 1];
                bool active = core.Hp > 0;
                if (visibilityCoreActive[f] != active)
                {
                    if (useLegacyVisibility) ChangeVisibilityLegacy(f, core.Definition.Position, world.Config.Rules.OwnedObjectiveVision.Raw, active ? 1 : -1);
                    else ChangeVisibility(f, core.Definition.Position, world.Config.Rules.OwnedObjectiveVision.Raw, active ? 1 : -1);
                    visibilityCoreActive[f] = active;
                }
            }

            for (int i = 0; i < world.Outposts.Length; i++)
            {
                uint owner = world.Outposts[i].OwnerFactionId;
                if (visibilityOutpostOwners[i] != owner)
                {
                    if (visibilityOutpostOwners[i] >= 1 && visibilityOutpostOwners[i] <= world.Factions.Length)
                        ChangeVisibilityForMode(checked((int)visibilityOutpostOwners[i] - 1), world.Outposts[i].Definition.Position,
                            world.Config.Rules.OwnedObjectiveVision.Raw, -1);
                    if (owner >= 1 && owner <= world.Factions.Length)
                        ChangeVisibilityForMode(checked((int)owner - 1), world.Outposts[i].Definition.Position,
                            world.Config.Rules.OwnedObjectiveVision.Raw, 1);
                    visibilityOutpostOwners[i] = owner;
                }
            }

            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                bool active = AgesOn && IsActualBuilding(b) && b.Complete && Shoots(b.Kind)
                    && b.FactionId >= 1 && b.FactionId <= world.Factions.Length;
                int faction = checked((int)b.FactionId) - 1;
                SimPoint position = active ? FootprintCenter(b.OriginCell, SizeOf(b.Kind)) : default;
                long visionRaw = active ? Fix64.FromInt(b.Kind == BuildingKind.Castle
                    ? world.Config.Economy.CastleVision : world.Config.Economy.TowerVision).Raw : 0;
                bool changed = visibilityBuildingActive[i] != active
                    || active && (visibilityBuildingFactions[i] != faction
                        || visibilityBuildingPositions[i].X.Raw != position.X.Raw || visibilityBuildingPositions[i].Z.Raw != position.Z.Raw
                        || visibilityBuildingVisionRaw[i] != visionRaw);
                if (changed)
                {
                    if (visibilityBuildingActive[i])
                        ChangeVisibilityForMode(visibilityBuildingFactions[i], visibilityBuildingPositions[i], visibilityBuildingVisionRaw[i], -1);
                    if (active) ChangeVisibilityForMode(faction, position, visionRaw, 1);
                    visibilityBuildingActive[i] = active;
                    visibilityBuildingFactions[i] = faction;
                    visibilityBuildingPositions[i] = position;
                    visibilityBuildingVisionRaw[i] = visionRaw;
                }
            }

            }
            finally { measure?.Invoke("Visibility.ChangeDetection", false); }

            measure?.Invoke("Visibility.ResourceMemory", true);
            try { UpdateResourceObservations(); }
            finally { measure?.Invoke("Visibility.ResourceMemory", false); }
        }

        private void InitializeVisibilityCaches()
        {
            visibilitySoldierPositions = new SimPoint[world.Soldiers.Length];
            visibilitySoldierVisionRaw = new long[world.Soldiers.Length];
            visibilitySoldierActive = new bool[world.Soldiers.Length];
            visibilityOutpostOwners = new uint[world.Outposts.Length];
            visibilityCoreActive = new bool[world.Factions.Length];
            EnsureVisibilityCaches();
        }

        private void EnsureVisibilityCaches()
        {
            int soldierCapacity = world.Soldiers.Length;
            if (visibilitySoldierActive.Length < soldierCapacity)
            {
                Array.Resize(ref visibilitySoldierPositions, soldierCapacity);
                Array.Resize(ref visibilitySoldierVisionRaw, soldierCapacity);
                Array.Resize(ref visibilitySoldierActive, soldierCapacity);
            }
            int capacity = world.Buildings.Length;
            if (visibilityBuildingActive != null && visibilityBuildingActive.Length >= capacity) return;
            int old = visibilityBuildingActive == null ? 0 : visibilityBuildingActive.Length;
            Array.Resize(ref visibilityBuildingPositions, capacity);
            Array.Resize(ref visibilityBuildingVisionRaw, capacity);
            Array.Resize(ref visibilityBuildingFactions, capacity);
            Array.Resize(ref visibilityBuildingActive, capacity);
            for (int i = old; i < capacity; i++) visibilityBuildingFactions[i] = -1;
        }

        private void ChangeVisibilityForMode(int faction, SimPoint source, long radiusRaw, int delta)
        {
            if (useLegacyVisibility) ChangeVisibilityLegacy(faction, source, radiusRaw, delta);
            else ChangeVisibility(faction, source, radiusRaw, delta);
        }

        private void ChangeVisibility(int faction, SimPoint source, long radiusRaw, int delta)
        {
            measure?.Invoke("Visibility.CircleUpdate", true);
            try
            {
            if (faction < 0 || faction >= world.Factions.Length || radiusRaw < 0) return;
            ref var state = ref world.Factions[faction];
            long squared = checked(radiusRaw * radiusRaw);
            long cellRaw = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw;
            if (!AxisInterval(source.Z.Raw, radiusRaw, cellRaw, world.Config.Map.HeightCells, out int minZ, out int maxZ)) return;
            for (int z = minZ; z <= maxZ; z++)
            {
                if (!CircleRowInterval(source, radiusRaw, squared, cellRaw, z, world.Config.Map.WidthCells, out int minX, out int maxX)) continue;
                ApplyVisibilityRange(ref state, z, minX, maxX, delta);
            }
            }
            finally { measure?.Invoke("Visibility.CircleUpdate", false); }
        }

        private void ChangeVisibilityDelta(int faction, SimPoint oldSource, long oldRadiusRaw, SimPoint newSource, long newRadiusRaw)
        {
            measure?.Invoke("Visibility.CircleUpdate", true);
            try
            {
            if (faction < 0 || faction >= world.Factions.Length || oldRadiusRaw < 0 || newRadiusRaw < 0) return;
            ref var state = ref world.Factions[faction];
            long oldSquared = checked(oldRadiusRaw * oldRadiusRaw), newSquared = checked(newRadiusRaw * newRadiusRaw);
            long cellRaw = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw;
            bool oldRows = AxisInterval(oldSource.Z.Raw, oldRadiusRaw, cellRaw, world.Config.Map.HeightCells, out int oldMinZ, out int oldMaxZ);
            bool newRows = AxisInterval(newSource.Z.Raw, newRadiusRaw, cellRaw, world.Config.Map.HeightCells, out int newMinZ, out int newMaxZ);
            int minZ = Math.Min(oldRows ? oldMinZ : world.Config.Map.HeightCells, newRows ? newMinZ : world.Config.Map.HeightCells);
            int maxZ = Math.Max(oldRows ? oldMaxZ : -1, newRows ? newMaxZ : -1);
            for (int z = minZ; z <= maxZ; z++)
            {
                int oldMinX = 0, oldMaxX = -1, newMinX = 0, newMaxX = -1;
                bool oldRow = oldRows && CircleRowInterval(oldSource, oldRadiusRaw, oldSquared, cellRaw, z, world.Config.Map.WidthCells, out oldMinX, out oldMaxX);
                bool newRow = newRows && CircleRowInterval(newSource, newRadiusRaw, newSquared, cellRaw, z, world.Config.Map.WidthCells, out newMinX, out newMaxX);
                if (!oldRow)
                {
                    if (newRow) ApplyVisibilityRange(ref state, z, newMinX, newMaxX, 1);
                    continue;
                }
                if (!newRow)
                {
                    ApplyVisibilityRange(ref state, z, oldMinX, oldMaxX, -1);
                    continue;
                }
                if (oldMaxX < newMinX || newMaxX < oldMinX)
                {
                    ApplyVisibilityRange(ref state, z, oldMinX, oldMaxX, -1);
                    ApplyVisibilityRange(ref state, z, newMinX, newMaxX, 1);
                    continue;
                }
                if (oldMinX < newMinX) ApplyVisibilityRange(ref state, z, oldMinX, newMinX - 1, -1);
                if (newMinX < oldMinX) ApplyVisibilityRange(ref state, z, newMinX, oldMinX - 1, 1);
                if (newMaxX < oldMaxX) ApplyVisibilityRange(ref state, z, newMaxX + 1, oldMaxX, -1);
                if (oldMaxX < newMaxX) ApplyVisibilityRange(ref state, z, oldMaxX + 1, newMaxX, 1);
            }
            }
            finally { measure?.Invoke("Visibility.CircleUpdate", false); }
        }

        private void ApplyVisibilityRange(ref FactionState state, int z, int minX, int maxX, int delta)
        {
            if (minX > maxX) return;
            visibilityProcessedCells += maxX - minX + 1;
            int width = world.Config.Map.WidthCells;
            for (int x = minX; x <= maxX; x++)
            {
                int cell = z * width + x;
                int count = checked(state.VisibleCounts[cell] + delta);
                state.VisibleCounts[cell] = count;
                state.VisibleCells[cell] = count > 0;
                if (count > 0) state.ExploredCells[cell] = true;
            }
        }

        private static bool AxisInterval(long sourceRaw, long radiusRaw, long cellRaw, int cells, out int min, out int max)
        {
            min = max = 0;
            if (cells <= 0) return false;
            long half = cellRaw / 2;
            long lower = checked(sourceRaw - radiusRaw - half), upper = checked(sourceRaw + radiusRaw - half);
            long lowerCell = CeilDiv(lower, cellRaw), upperCell = FloorDiv(upper, cellRaw);
            min = (int)Math.Max(0, Math.Min(cells - 1L, lowerCell));
            max = (int)Math.Max(0, Math.Min(cells - 1L, upperCell));
            return lowerCell <= cells - 1L && upperCell >= 0 && min <= max;
        }

        private static bool CircleRowInterval(SimPoint source, long radiusRaw, long squared, long cellRaw, int z, int width,
            out int min, out int max)
        {
            min = max = 0;
            long centerZ = checked((long)z * cellRaw + cellRaw / 2);
            long dz = checked(source.Z.Raw - centerZ), dzSquared = checked(dz * dz);
            if (dzSquared > squared) return false;
            long horizontal = IntegerSqrt64(squared - dzSquared);
            return AxisInterval(source.X.Raw, horizontal, cellRaw, width, out min, out max);
        }

        private static long FloorDiv(long value, long divisor)
        {
            long quotient = value / divisor, remainder = value % divisor;
            return remainder != 0 && value < 0 ? quotient - 1 : quotient;
        }

        private static long CeilDiv(long value, long divisor)
        {
            long quotient = value / divisor, remainder = value % divisor;
            return remainder != 0 && value > 0 ? quotient + 1 : quotient;
        }

        private static long IntegerSqrt64(long value)
        {
            ulong remaining = (ulong)value, result = 0, bit = 1UL << 62;
            while (bit > remaining) bit >>= 2;
            while (bit != 0)
            {
                if (remaining >= result + bit) { remaining -= result + bit; result = (result >> 1) + bit; }
                else result >>= 1;
                bit >>= 2;
            }
            return checked((long)result);
        }

        private void ChangeVisibilityLegacy(int faction, SimPoint source, long radiusRaw, int delta)
        {
            if (faction < 0 || faction >= world.Factions.Length || radiusRaw < 0) return;
            ref var state = ref world.Factions[faction];
            long squared = checked(radiusRaw * radiusRaw);
            long cellRaw = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw;
            int minX = Math.Max(0, checked((int)((source.X.Raw - radiusRaw) / cellRaw) - 2));
            int maxX = Math.Min(world.Config.Map.WidthCells - 1, checked((int)((source.X.Raw + radiusRaw) / cellRaw) + 2));
            int minZ = Math.Max(0, checked((int)((source.Z.Raw - radiusRaw) / cellRaw) - 2));
            int maxZ = Math.Min(world.Config.Map.HeightCells - 1, checked((int)((source.Z.Raw + radiusRaw) / cellRaw) + 2));
            for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            {
                int cell = z * world.Config.Map.WidthCells + x;
                var center = world.Map.Center(cell);
                long dx = checked(source.X.Raw - center.X.Raw), dz = checked(source.Z.Raw - center.Z.Raw);
                if (checked(dx * dx + dz * dz) > squared) continue;
                int count = checked(state.VisibleCounts[cell] + delta);
                state.VisibleCounts[cell] = count;
                state.VisibleCells[cell] = count > 0;
                if (count > 0) state.ExploredCells[cell] = true;
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
            if (first || second)
            {
                world.Result = new MatchResult(true, first == second ? 0U : first ? 2U : 1U, first && second, false, false);
                return;
            }
            var rules = world.Config.Economy;
            if (!rules.Enabled || !rules.Ages || !rules.AgeVictoryEnabled) return;
            // (#21) Once both sides stand in the third age, being ahead no longer means anything by itself - the fight
            // alone decides from here, exactly as without the flag. Only one side may still be racing for this win.
            if (world.Economies[0].Age >= 3 && world.Economies[1].Age >= 3) return;
            bool firstReady = AgeVictoryReady(0, rules.AgeVictoryTicks);
            bool secondReady = AgeVictoryReady(1, rules.AgeVictoryTicks);
            if (firstReady || secondReady)
                world.Result = new MatchResult(true, firstReady == secondReady ? 0U : firstReady ? 1U : 2U,
                    firstReady && secondReady, false, false, true);
        }

        private bool AgeVictoryReady(int index, int requiredTicks)
        {
            var economy = world.Economies[index];
            if (economy.Age < 3 || world.Cores[world.Factions[index].CoreId - 1].Hp <= 0)
            {
                economy.AgeVictoryProgress = 0;
                world.Economies[index] = economy;
                return false;
            }
            economy.AgeVictoryProgress = checked(Math.Min(requiredTicks, economy.AgeVictoryProgress + 1));
            world.Economies[index] = economy;
            return economy.AgeVictoryProgress >= requiredTicks;
        }

        public FactionFrame Capture(uint factionId)
        {
            if (factionId < 1 || factionId > 2) throw new ArgumentOutOfRangeException(nameof(factionId));
            return frames[factionId - 1];
        }

        private UnitKind ArmyKind(ArmyState army)
        {
            var counts = new Dictionary<UnitKind, int>();
            UnitKind first = army.Definition.Role == "scout" ? UnitKind.Scout : UnitKind.Infantry;
            foreach (uint soldierId in army.SoldierIds)
            {
                var soldier = world.Soldiers[soldierId - 1];
                if (!soldier.Alive) continue;
                var kind = soldier.Class != 0 ? soldier.Class : soldier.Initial.Kind;
                if (counts.Count == 0) first = soldier.Initial.Kind;
                counts[kind] = counts.TryGetValue(kind, out int count) ? count + 1 : 1;
            }
            UnitKind result = first;
            int best = 0;
            foreach (var pair in counts)
                if (pair.Value > best) { best = pair.Value; result = pair.Key; }
            return CavalryOn ? result : first;
        }

        private static int EquipmentTechCount(ulong techs)
        {
            int count = 0;
            if ((techs & (1UL << ((int)TechKind.Weapons - 1))) != 0) count++;
            if ((techs & (1UL << ((int)TechKind.Armour - 1))) != 0) count++;
            if ((techs & (1UL << ((int)TechKind.SteelWeapons - 1))) != 0) count++;
            if ((techs & (1UL << ((int)TechKind.SteelArmour - 1))) != 0) count++;
            if ((techs & (1UL << ((int)TechKind.GemArmor - 1))) != 0) count++;
            return count;
        }

        // Each faction's command views in commandStates order. An ended command's view never changes again, so only the
        // views not yet final are rebuilt; the frames list exactly what rebuilding every view every tick did (10-09: that
        // grew with every command of a long match and slowed it down).
        // The shared arrays only grow by reallocation; an ended command's slot is written once, after which frames read it
        // from the array, so earlier frames keep seeing what they saw (SharedPrefixList).
        private readonly CommandView[][] commandViews = { new CommandView[64], new CommandView[64] };
        private readonly int[] commandViewCounts = new int[2];
        private readonly List<CommandState> unsettledViews = new List<CommandState>();
        private readonly SharedPrefixList<CommandView>[] frameCommandViews = new SharedPrefixList<CommandView>[2];
        private int viewedCommandCount;

        private void RefreshCommandViews()
        {
            for (; viewedCommandCount < commandStates.Count; viewedCommandCount++)
            {
                var c = commandStates[viewedCommandCount];
                uint faction = c.Order.Target.FactionId;
                if (faction < 1 || faction > 2) continue;
                int slot = commandViewCounts[faction - 1]++;
                if (slot == commandViews[faction - 1].Length)
                {
                    var grown = new CommandView[slot * 2];
                    Array.Copy(commandViews[faction - 1], grown, slot);
                    commandViews[faction - 1] = grown;
                }
                c.ViewIndex = slot;
                unsettledViews.Add(c);
            }
            var indices = new[] { new List<int>(), new List<int>() };
            var items = new[] { new List<CommandView>(), new List<CommandView>() };
            foreach (var command in unsettledViews)
            {
                int side = (int)command.Order.Target.FactionId - 1;
                var view = new CommandView(command.Order.CommandId, command.Order.Target, command.Order.Kind, command.Order.Goal,
                    command.Status, command.AcceptedTick, command.ApplyTick, command.Reason, command.Order.Source);
                // An ended command's view is final: written once into the shared array. A live one rides with the frame.
                if (Terminal(command)) commandViews[side][command.ViewIndex] = view;
                else { indices[side].Add(command.ViewIndex); items[side].Add(view); }
            }
            unsettledViews.RemoveAll(Terminal);
            for (int side = 0; side < 2; side++)
                frameCommandViews[side] = new SharedPrefixList<CommandView>(commandViews[side], commandViewCounts[side], indices[side], items[side]);
        }

        private void PublishFrames()
        {
            measure?.Invoke("FramesCommands", true);
            RefreshCommandViews();
            measure?.Invoke("FramesCommands", false);
            for (uint f = 1; f <= 2; f++)
            {
                measure?.Invoke("FramesUnits", true);
                var units = new List<RenderUnit>();
                var enemies = new List<VisibleEnemy>();
                var contacts = new List<EnemyContact>();
                var armies = new List<OwnArmyView>();
                var objectives = new List<KnownObjective>();
                foreach (int i in world.SoldierTraversal)
                {
                    var s = world.Soldiers[i];
                    if (!s.Alive) continue;
                    bool own = s.Initial.FactionId == f;
                    if (!own && !IsVisibleTo(f, s.Position)) continue;
                    uint id = own ? s.Initial.Id : world.Factions[f - 1].ContactIds[i];
                    // What a look at the soldier tells: its side's civilisation and how many arms and armour researches it
                    // carries. Nothing else of the enemy's economy is shown (owner, 10-10).
                    CivKind civ = CivKind.Primitive;
                    byte equipmentLevel = 0;
                    if (EconomyOn)
                    {
                        var ownerEconomy = world.Economies[s.Initial.FactionId - 1];
                        civ = ownerEconomy.Civ;
                        equipmentLevel = (byte)EquipmentTechCount(ownerEconomy.Techs);
                    }
                    units.Add(new RenderUnit(id, own, s.Class != 0 ? s.Class : s.Initial.Kind, s.Position, s.IsMoving, s.IsAttacking,
                        own && s.IsRetreating, own, own ? s.Hp : 0, civ, equipmentLevel, own ? s.Initial.ArmyId : 0));
                    if (!own)
                    {
                        // Cult's observation may use the trained class (for example HeavyInfantry) because that is
                        // the information used by its target valuation.  Keep the old visible-enemy bytes unchanged
                        // for every other scenario.
                        byte observedKind = CultOn && s.Class != 0 ? (byte)s.Class : (byte)s.Initial.Kind;
                        enemies.Add(new VisibleEnemy(id, s.Position, observedKind));
                        contacts.Add(Contact(f, i, true));
                    }
                }
                foreach (uint id in world.Factions[f - 1].ArmyIds)
                {
                    var a = world.Armies[id - 1];
                    int count = 0;
                    SimPoint position = world.Cores[world.Factions[f - 1].CoreId - 1].Definition.Position;
                    UnitKind kind = ArmyKind(a);
                    foreach (uint soldierId in a.SoldierIds)
                    {
                        var s = world.Soldiers[soldierId - 1];
                        if (!s.Alive) continue;
                        if (count++ == 0) position = s.Position;
                    }
                    var patrolObjective = a.ExplorePatrolActive && a.ExploreHasGoal
                        ? new PolicyGoal(GoalKind.Point, 0, a.ExploreGoal)
                        : default(PolicyGoal);
                    armies.Add(new OwnArmyView(id, f, kind, position, count, a.Definition.HomeObjective, patrolObjective));

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
                var observation = new FactionObservation(f, world.Tick, armies, enemies, contacts, objectives, world.Config.Rules.FactionCap);
                measure?.Invoke("FramesUnits", false);
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
                measure?.Invoke("FramesEconomy", true);
                var economyView = EconomyViewFor(f);
                measure?.Invoke("FramesEconomy", false);
                measure?.Invoke("FramesRegions", true);
                var regionViews = RegionViewsFor(f);
                var cellRegions = CellRegionsForFrame();
                measure?.Invoke("FramesRegions", false);
                measure?.Invoke("FramesFog", true);
                var fog = new FogView(world.Factions[f - 1].VisibleCells, world.Factions[f - 1].ExploredCells);
                measure?.Invoke("FramesFog", false);
                frames[f - 1] = new FactionFrame(world.Tick, f, units, observation, frameCommandViews[f - 1], visibleEvents,
                    fog, world.Result,
                    world.Factions[f - 1].AliveCount, world.Config.Rules.FactionCap, ReinforcementViews(f), economyView,
                    regionViews, cellRegions);
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
            int maxHp = kind == GoalKind.Core ? world.Cores[id - 1].Definition.Hp : 0;
            return new KnownObjective(kind, id, position, memory.OwnerKnown, memory.OwnerFactionId, memory.HpKnown, memory.Hp, memory.LastSeenTick,
                visible ? capturing : 0, visible ? captureTicks : 0, visible ? duration : 0, maxHp);
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
