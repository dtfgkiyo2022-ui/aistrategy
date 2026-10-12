using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private enum PushWavePhase : byte
        {
            Inactive,
            Gathering,
            Assault,
            Reforming
        }

        private sealed class PushWaveState
        {
            internal PushWavePhase Phase;
            internal long GatheringStartedTick;
            internal int AssaultStartCount;
            internal int AssaultNumber;
            internal SimPoint StagingPoint;
            internal uint[] AssaultSoldierIds = Array.Empty<uint>();
        }

        private readonly bool[] latePushLatched = new bool[2];
        private readonly PushWaveState[] pushWaveStates = { new PushWaveState(), new PushWaveState() };
        private bool LatePushOn => world.Config.Economy.Enabled && world.Config.Economy.LatePush;
        private bool PushWavesOn => LatePushOn && world.Config.Economy.PushWaves;

        /// <summary>
        /// Creates the built-in late-game policy from the last published faction observation. It is deliberately
        /// separate from OffenseDecision and PolicyDecision: those algorithms keep selecting ordinary objectives.
        /// </summary>
        private void GenerateLatePushCommands()
        {
            if (!LatePushOn) return;
            if (PushWavesOn)
            {
                GeneratePushWaveCommands();
                return;
            }
            for (uint faction = 1; faction <= 2; faction++)
            {
                var observation = frames[faction - 1]?.Observation;
                if (observation == null) continue;
                bool reachedThirdAge = world.Config.Economy.SiegeByAge && world.Economies[faction - 1].Age >= 3;
                if (!latePushLatched[faction - 1] && (reachedThirdAge || LatePushCondition(observation, world.Config.Economy)))
                    latePushLatched[faction - 1] = true;
                if (!latePushLatched[faction - 1]) continue;

                // A human combat policy is authoritative. Leave it in place and try again after it ends; the latch
                // is the condition itself, so a faction never returns to the ordinary objective contest.
                if (liveCommands.Any(c => !Terminal(c) && c.Order.Target.FactionId == faction
                    && c.Order.Source == CommandSource.Human && Combat(c.Order))) continue;

                var scope = new ScopeKey(faction, ScopeKind.All, 0);
                if (world.Config.Economy.SiegeByAge && !Affected(scope).Any(id => world.Armies[id - 1].SoldierIds.Any(s => world.Soldiers[s - 1].Alive)))
                    continue;
                var current = liveCommands.FirstOrDefault(c => !Terminal(c) && c.Order.Source == CommandSource.Doctrine
                    && c.Order.Target.Equals(scope) && c.Order.Kind == PolicyKind.Focus
                    && c.Order.Goal.Kind == GoalKind.Core);
                if (current != null)
                {
                    AttachLatePushArmies(current);
                    continue;
                }

                uint enemyFaction = faction == 1 ? 2U : 1U;
                uint enemyCore = world.Factions[enemyFaction - 1].CoreId;
                ulong commandId = nextCommandId++;
                var order = new PolicyOrder(commandId, nextBatchId++, CommandSource.Doctrine, scope, PolicyKind.Focus,
                    new PolicyGoal(GoalKind.Core, enemyCore, default), 50, new LossBudget((ushort)LatePushLossPermille(world.Config.Economy)),
                    new EndCondition(EndKind.UntilReplaced, 0), 0, Revision(scope),
                    Versions(scope).Where(v => !v.Scope.Equals(scope)).ToArray(), observation.Tick,
                    new Expiration(long.MaxValue, 0, ExpireFlags.None));
                var state = new CommandState { Order = order, RequestId = nextRequestId++, LogIndex = commandId,
                    AcceptedTick = Math.Max(0, world.Tick - 1), ApplyTick = world.Tick, DeadlineTick = world.Tick,
                    Status = CommandStatus.Pending, ReservedArmies = Affected(scope), Acquired = true };
                Advance(scope, Field(order.Kind));
                state.ExecutionRevision = Revision(scope);
                state.Dependencies = Versions(scope).ToArray();
                AddCommandState(state);
                Notice(state, ReasonCode.None);
            }
        }

        private void GeneratePushWaveCommands()
        {
            for (uint faction = 1; faction <= 2; faction++)
            {
                var observation = frames[faction - 1]?.Observation;
                if (observation == null) continue;
                var rules = world.Config.Economy;
                bool reachedThirdAge = rules.SiegeByAge && world.Economies[faction - 1].Age >= 3;
                if (!latePushLatched[faction - 1] && (reachedThirdAge || LatePushCondition(observation, rules)))
                    latePushLatched[faction - 1] = true;
                // The late-push latch is the start signal. Once it fires, waves also operate in age 2;
                // SiegeByAge scales that assault instead of delaying it until age 3.
                if (!latePushLatched[faction - 1]) continue;

                var wave = pushWaveStates[faction - 1];
                if (wave.Phase == PushWavePhase.Inactive)
                {
                    wave.Phase = PushWavePhase.Gathering;
                    wave.GatheringStartedTick = world.Tick;
                    wave.StagingPoint = WaveStagingPoint(faction, rules.WaveStagingDistance);
                    WaveNotice(faction, wave.Phase);
                }

                if (wave.Phase == PushWavePhase.Gathering)
                {
                    int standing = WaveStandingSoldiers(faction);
                    bool timedOut = world.Tick - wave.GatheringStartedTick >= rules.WaveMaxWaitTicks;
                    if (standing >= rules.WaveSize || timedOut && standing >= rules.WaveMinSize)
                    {
                        wave.Phase = PushWavePhase.Assault;
                        wave.AssaultStartCount = standing;
                        wave.AssaultNumber = checked(wave.AssaultNumber + 1);
                        wave.AssaultSoldierIds = WaveStandingSoldierIds(faction);
                        WaveNotice(faction, wave.Phase);
                    }
                }
                else if (wave.Phase == PushWavePhase.Assault)
                {
                    int alive = wave.AssaultSoldierIds.Count(id => world.Soldiers[id - 1].Alive);
                    if (wave.AssaultStartCount > 0 && 1000L * alive <= (long)rules.WaveRetreatPermille * wave.AssaultStartCount)
                    {
                        wave.Phase = PushWavePhase.Reforming;
                        WaveNotice(faction, wave.Phase);
                    }
                }
                else if (wave.Phase == PushWavePhase.Reforming && WaveStagingComplete(faction, wave.StagingPoint))
                {
                    wave.Phase = PushWavePhase.Gathering;
                    wave.GatheringStartedTick = world.Tick;
                    wave.AssaultStartCount = 0;
                    wave.AssaultSoldierIds = Array.Empty<uint>();
                    WaveNotice(faction, wave.Phase);
                }

                EnsurePushWaveCommands(faction, wave);
            }
        }

        private void EnsurePushWaveCommands(uint faction, PushWaveState wave)
        {
            SimPoint goal = wave.Phase == PushWavePhase.Assault
                ? world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position : wave.StagingPoint;
            foreach (uint armyId in world.Factions[faction - 1].ArmyIds.OrderBy(id => id))
            {
                if (HasHumanOrTacticOrder(armyId)) continue;
                var live = world.Armies[armyId - 1].SoldierIds.Where(id => world.Soldiers[id - 1].Alive).OrderBy(id => id).ToArray();
                if (live.Length == 0) continue;
                var scope = new ScopeKey(faction, ScopeKind.Army, armyId);
                var current = liveCommands.FirstOrDefault(c => !Terminal(c) && c.Order.Source == CommandSource.Doctrine
                    && c.Order.Target.Equals(scope) && c.Order.Kind == PolicyKind.Focus
                    && c.Order.Goal.Kind == (wave.Phase == PushWavePhase.Assault ? GoalKind.Core : GoalKind.Point)
                    && (wave.Phase == PushWavePhase.Assault ? c.Order.Goal.Id == world.Factions[2 - faction].CoreId : SamePoint(c.Order.Goal.Point, goal)));
                if (current != null) continue;

                ulong commandId = nextCommandId++;
                var order = new PolicyOrder(commandId, nextBatchId++, CommandSource.Doctrine, scope, PolicyKind.Focus,
                    wave.Phase == PushWavePhase.Assault
                        ? new PolicyGoal(GoalKind.Core, world.Factions[2 - faction].CoreId, default)
                        : new PolicyGoal(GoalKind.Point, 0, goal),
                    50, new LossBudget((ushort)LatePushLossPermille(world.Config.Economy)),
                    new EndCondition(EndKind.UntilReplaced, 0), 0, Revision(scope),
                    Versions(scope).Where(v => !v.Scope.Equals(scope)).ToArray(), Math.Max(0, world.Tick - 1),
                    new Expiration(long.MaxValue, 0, ExpireFlags.None));
                var state = new CommandState { Order = order, RequestId = nextRequestId++, LogIndex = commandId,
                    AcceptedTick = Math.Max(0, world.Tick - 1), ApplyTick = world.Tick, DeadlineTick = world.Tick,
                    Status = CommandStatus.Pending, ReservedArmies = Affected(scope), Acquired = true };
                Advance(scope, Field(order.Kind));
                state.ExecutionRevision = Revision(scope);
                state.Dependencies = Versions(scope).ToArray();
                AddCommandState(state);
                Notice(state, ReasonCode.None);
            }
        }

        private bool HasHumanOrTacticOrder(uint armyId)
            => liveCommands.Any(c => !Terminal(c)
                && (c.Order.Source == CommandSource.Human || c.Order.Source == CommandSource.Tactic)
                && Affected(c.Order.Target).Contains(armyId));

        private int WaveStandingSoldiers(uint faction)
            => WaveStandingSoldierIds(faction).Length;

        private uint[] WaveStandingSoldierIds(uint faction)
        {
            var ids = new List<uint>();
            foreach (uint armyId in world.Factions[faction - 1].ArmyIds.OrderBy(id => id))
            {
                if (HasHumanOrTacticOrder(armyId)) continue;
                foreach (uint soldierId in world.Armies[armyId - 1].SoldierIds.OrderBy(id => id))
                {
                    var soldier = world.Soldiers[soldierId - 1];
                    if (soldier.Alive && soldier.Initial.Kind != UnitKind.Scout) ids.Add(soldierId);
                }
            }
            return ids.ToArray();
        }

        private SimPoint WaveStagingPoint(uint faction, int distanceMeters)
        {
            var own = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
            var enemy = world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position;
            var path = world.Map.SharedRoute(world.Map.Cell(own), enemy, faction);
            if (path.Length == 0) return own;
            int cells = Math.Max(0, distanceMeters / Math.Max(1, world.Config.Map.CellSizeMeters));
            return world.Map.Center(path[Math.Min(cells, path.Length - 1)]);
        }

        private bool WaveStagingComplete(uint faction, SimPoint staging)
        {
            foreach (uint soldierId in WaveStandingSoldierIds(faction))
                // A formation occupies several cells around its point; use the same 8 m slot joining envelope as navigation.
                if (!InRange(world.Soldiers[soldierId - 1].Position, staging, Fix64.FromInt(8))) return false;
            return true;
        }

        private bool IsPushWaveAssaultSoldier(uint soldierId)
        {
            if (!PushWavesOn) return false;
            uint faction = world.Soldiers[soldierId - 1].Initial.FactionId;
            var wave = pushWaveStates[faction - 1];
            return wave.Phase == PushWavePhase.Assault && Array.IndexOf(wave.AssaultSoldierIds, soldierId) >= 0
                && !HasHumanOrTacticOrder(world.Soldiers[soldierId - 1].Initial.ArmyId);
        }

        private bool IsPushWaveAssaultArmy(uint armyId)
        {
            if (!PushWavesOn) return false;
            uint faction = world.Armies[armyId - 1].Definition.FactionId;
            return pushWaveStates[faction - 1].Phase == PushWavePhase.Assault
                && !HasHumanOrTacticOrder(armyId);
        }

        private bool IsPushWaveStagingArmy(uint armyId)
        {
            if (!PushWavesOn) return false;
            uint faction = world.Armies[armyId - 1].Definition.FactionId;
            return pushWaveStates[faction - 1].Phase != PushWavePhase.Inactive
                && !HasHumanOrTacticOrder(armyId);
        }

        private bool IsPushWaveStagingPathArmy(uint armyId)
        {
            if (!IsPushWaveStagingArmy(armyId)) return false;
            uint faction = world.Armies[armyId - 1].Definition.FactionId;
            return pushWaveStates[faction - 1].Phase != PushWavePhase.Assault;
        }

        private bool IsPushWaveDoctrine(PolicyOrder order)
        {
            if (!PushWavesOn || order.Source != CommandSource.Doctrine || order.Kind != PolicyKind.Focus
                || order.Target.Kind != ScopeKind.Army) return false;
            uint faction = world.Armies[order.Target.Id - 1].Definition.FactionId;
            var wave = pushWaveStates[faction - 1];
            if (wave.Phase == PushWavePhase.Inactive) return false;
            // During an active wave every automatic army-focus doctrine in the faction is wave-owned. Do not
            // require the point/core payload here: this helper is also called while a replacement is pending.
            return true;
        }

        private void WaveNotice(uint faction, PushWavePhase phase)
        {
            // AiReport has no wave-specific payload. Keep the phase in Value and carry the assault start count in the
            // otherwise unused CommandId field so timeline.jsonl can report every wave without changing Contracts.
            ulong assaultCount = phase == PushWavePhase.Assault ? (ulong)pushWaveStates[faction - 1].AssaultStartCount : 0;
            commandEvents.Add(new GameEvent(world.Tick, (uint)commandEvents.Count, EventKind.AiReport,
                (byte)(1 << ((int)faction - 1)), faction, assaultCount, pushWaveStates[faction - 1].StagingPoint,
                (int)phase, ReasonCode.None));
        }

        private static int LatePushLossPermille(EconomyRules rules) => rules != null && rules.SiegeByAge ? 1000 : 300;

        private void AttachLatePushArmies(CommandState command)
        {
            if (!world.Config.Economy.SiegeByAge || command.Status != CommandStatus.Executing) return;
            foreach (uint id in Affected(command.Order.Target))
            {
                uint[] soldiers = world.Armies[id - 1].SoldierIds.Where(s => world.Soldiers[s - 1].Alive).OrderBy(s => s).ToArray();
                if (soldiers.Length == 0) continue;
                var active = command.Armies.FirstOrDefault(a => a.ArmyId == id && a.Active && !a.Failed && !a.Finished);
                if (active != null)
                {
                    // A 100% late-push loss budget is about replacing a spent assault group, not about
                    // permanently putting the army into Retreat. Rebase the execution on newly trained soldiers.
                    if (active.Returning)
                    {
                        active.Returning = false;
                        active.StartIds = soldiers;
                        active.N0 = soldiers.Length;
                        active.Deaths = 0;
                    }
                    continue;
                }
                command.Armies.RemoveAll(a => a.ArmyId == id && (!a.Active || a.Failed || a.Finished));
                command.Armies.Add(new ArmyExecution { ArmyId = id, StartIds = soldiers, N0 = soldiers.Length });
            }
        }

        public static bool LatePushCondition(FactionObservation observation, EconomyRules rules)
        {
            if (observation == null || rules == null || !rules.LatePush) return false;
            if (observation.Tick >= rules.LatePushAfterTicks) return true;
            if (observation.Tick < rules.LatePushAdvantageAfterTicks) return false;

            int knownOutposts = observation.Objectives.Count(o => o.Kind == GoalKind.Outpost && o.IsOwnerKnown);
            int ownedOutposts = observation.Objectives.Count(o => o.Kind == GoalKind.Outpost && o.IsOwnerKnown
                && o.OwnerFactionId == observation.FactionId);
            if (knownOutposts > 0 && ownedOutposts * 2 > knownOutposts) return true;

            int ownSoldiers = observation.OwnArmies.Sum(a => Math.Max(0, a.AliveCount));
            int visibleEnemySoldiers = observation.VisibleEnemies.Count;
            // No visible enemy is no information, not an advantage: under the fog this would fire for everyone at 8 minutes.
            return ownSoldiers > 0 && visibleEnemySoldiers > 0
                && (long)ownSoldiers * 1000L >= (long)visibleEnemySoldiers * rules.LatePushEnemyMultiplierPermille;
        }
    }
}
