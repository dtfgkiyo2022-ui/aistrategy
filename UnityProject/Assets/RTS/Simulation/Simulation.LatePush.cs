using System;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private readonly bool[] latePushLatched = new bool[2];
        private bool LatePushOn => world.Config.Economy.Enabled && world.Config.Economy.LatePush;

        /// <summary>
        /// Creates the built-in late-game policy from the last published faction observation. It is deliberately
        /// separate from OffenseDecision and PolicyDecision: those algorithms keep selecting ordinary objectives.
        /// </summary>
        private void GenerateLatePushCommands()
        {
            if (!LatePushOn) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                var observation = frames[faction - 1]?.Observation;
                if (observation == null) continue;
                if (!latePushLatched[faction - 1] && LatePushCondition(observation, world.Config.Economy))
                    latePushLatched[faction - 1] = true;
                if (!latePushLatched[faction - 1]) continue;

                // A human combat policy is authoritative. Leave it in place and try again after it ends; the latch
                // is the condition itself, so a faction never returns to the ordinary objective contest.
                if (liveCommands.Any(c => !Terminal(c) && c.Order.Target.FactionId == faction
                    && c.Order.Source == CommandSource.Human && Combat(c.Order))) continue;

                var scope = new ScopeKey(faction, ScopeKind.All, 0);
                if (liveCommands.Any(c => !Terminal(c) && c.Order.Source == CommandSource.Doctrine
                    && c.Order.Target.Equals(scope) && c.Order.Kind == PolicyKind.Focus
                    && c.Order.Goal.Kind == GoalKind.Core)) continue;

                uint enemyFaction = faction == 1 ? 2U : 1U;
                uint enemyCore = world.Factions[enemyFaction - 1].CoreId;
                ulong commandId = nextCommandId++;
                var order = new PolicyOrder(commandId, nextBatchId++, CommandSource.Doctrine, scope, PolicyKind.Focus,
                    new PolicyGoal(GoalKind.Core, enemyCore, default), 50, new LossBudget(300),
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
