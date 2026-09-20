using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Application
{
    /// <summary>How the western player intervenes. All three are mechanical stand-ins for a human, not a judgement of one.</summary>
    public enum InterventionStyle
    {
        /// <summary>No player orders: fully automatic.</summary>
        Auto = 0,
        /// <summary>One order at tick 0 (keep a 30% reserve), then hands off.</summary>
        StartOnly = 1,
        /// <summary>The same start order, then a second order the tick the first enemy contact is reported.</summary>
        Change = 2,
        /// <summary>The start order, then at the first contact every army focuses on the enemy core.</summary>
        Push = 3,
        /// <summary>The start order, then at the first contact the north and south armies each focus on their own outpost.</summary>
        Secure = 4,
        /// <summary>The start order, then at the first contact only the reserve ratio changes (no core defence order).</summary>
        ChangeReserveOnly = 5,
        /// <summary>The start order, then at the first contact only the reserve army defends the own core (no reserve change).</summary>
        ChangeDefendOnly = 6
    }

    public sealed class InterventionCommandOutcome
    {
        public ulong CommandId;
        public string Kind = "";
        public string Target = "";
        public long AcceptedTick;
        public long ApplyTick;
        public string FinalStatus = "";
        public string Reason = "";
    }

    public sealed class InterventionResult
    {
        public ScheduledInput[] Inputs = Array.Empty<ScheduledInput>();
        public long LastTick;
        /// <summary>First tick the western frame reported any enemy contact; -1 if none.</summary>
        public long FirstContactTick = -1;
        public List<InterventionCommandOutcome> Commands = new List<InterventionCommandOutcome>();
    }

    /// <summary>
    /// Records a match in which the west (faction 1) player intervenes in a fixed style and the east follows a
    /// doctrine preset. Player orders go through the interpretation stub with the given reply delay (0 = direct).
    /// </summary>
    public static class InterventionRunner
    {
        public static InterventionResult Run(ScenarioDefinition scenario, long ticks, string eastPreset, InterventionStyle style, int delayTicks, AiTimingProfile profile = null, long triggerTick = -1, ushort changeReservePermille = 500)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (ticks < 0 || ticks > scenario.VerificationTickLimit) throw new ArgumentOutOfRangeException(nameof(ticks));
            if (delayTicks != 0 && delayTicks != 60 && delayTicks != 200 && delayTicks != 400)
                throw new ArgumentOutOfRangeException(nameof(delayTicks), "Delay must be 0, 60, 200 or 400 ticks.");

            var sim = new Simulation.Simulation(scenario);
            UserPolicyIntent? interpreting = null;
            DelayedPolicyProvider provider = delayTicks == 0 ? null : new DelayedPolicyProvider(delayTicks, request =>
            {
                if (interpreting == null) throw new InvalidOperationException("No player command is being interpreted.");
                var i = interpreting.Value;
                return new[] { new PolicyOrder(0, 0, CommandSource.Human, i.Target, i.Kind, i.Goal, i.Priority, i.AllowedLoss,
                    i.End, i.ReservePermille, 0, Array.Empty<PolicyVersion>(), request.StartedTick, i.Expiration) };
            }, profile);
            var gateway = new CommandGateway(sim, provider);
            var east = PolicyPresets.CreateController(eastPreset ?? "none", 2, gateway);
            east.Initialize();

            var result = new InterventionResult();
            var submitted = new List<ulong>();
            ulong sequence = 1;
            Action<UserPolicyIntent> send = intent =>
            {
                interpreting = intent;
                try { submitted.Add(provider == null ? gateway.Submit(intent) : gateway.SubmitInterpreted(intent)); }
                finally { interpreting = null; }
            };

            if (style != InterventionStyle.Auto) send(ReserveIntent(sequence++, 300));
            bool changed = false;

            for (long i = 0; i < ticks && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                var west = sim.Capture(1);
                if (west.Result.HasEnded) break;
                if (result.FirstContactTick < 0 && west.Observation.Contacts.Count > 0) result.FirstContactTick = west.Tick;
                // A fixed triggerTick replaces the first-contact sign, so the acceptance tick of the change can be swept.
                if (style >= InterventionStyle.Change && !changed && (triggerTick >= 0 ? west.Tick >= triggerTick : result.FirstContactTick >= 0))
                {
                    changed = true;
                    if (style == InterventionStyle.Change)
                    {
                        send(ReserveIntent(sequence++, changeReservePermille));
                        send(DefendCoreIntent(sequence++, scenario));
                    }
                    else if (style == InterventionStyle.ChangeReserveOnly) send(ReserveIntent(sequence++, changeReservePermille));
                    else if (style == InterventionStyle.ChangeDefendOnly) send(DefendCoreIntent(sequence++, scenario));
                    else if (style == InterventionStyle.Push) send(PushIntent(sequence++, scenario));
                    else
                    {
                        send(SecureIntent(sequence++, scenario, 0));
                        send(SecureIntent(sequence++, scenario, 1));
                    }
                }
                east.Step(sim.Capture(2));
            }

            var last = sim.Capture(1);
            result.LastTick = last.Tick;
            result.Inputs = gateway.Inputs.ToArray();
            foreach (var view in last.Commands.Where(c => c.Source == CommandSource.Human))
            {
                result.Commands.Add(new InterventionCommandOutcome
                {
                    CommandId = view.CommandId,
                    Kind = view.Kind.ToString(),
                    Target = view.Target.Kind + ":" + view.Target.Id,
                    AcceptedTick = view.AcceptedTick,
                    ApplyTick = view.ApplyTick,
                    FinalStatus = view.Status.ToString(),
                    Reason = view.Reason.ToString()
                });
            }
            return result;
        }

        private static UserPolicyIntent ReserveIntent(ulong sequence, ushort permille) => new UserPolicyIntent(sequence,
            new ScopeKey(1, ScopeKind.All, 0), PolicyKind.MaintainReserve, default, 50, new LossBudget(300),
            new EndCondition(EndKind.UntilReplaced, 0), permille, new Expiration(long.MaxValue, 0, ExpireFlags.SubjectGone));

        private static UserPolicyIntent PushIntent(ulong sequence, ScenarioDefinition scenario)
        {
            uint enemyCore = scenario.Factions.First(f => f.Id == 2).CoreId;
            return new UserPolicyIntent(sequence, new ScopeKey(1, ScopeKind.All, 0), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Core, enemyCore, default), 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.SubjectGone));
        }

        // The first two western armies (north, south) each take the outpost they are homed on.
        private static UserPolicyIntent SecureIntent(ulong sequence, ScenarioDefinition scenario, int index)
        {
            var army = scenario.Armies.Where(a => a.FactionId == 1).OrderBy(a => a.Id).Skip(index).First();
            return new UserPolicyIntent(sequence, new ScopeKey(1, ScopeKind.Army, army.Id), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Outpost, army.HomeObjective.Id, default), 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.SubjectGone));
        }

        // The reserve army defends the own core; armies are numbered per faction, the reserve is the third of the west.
        private static UserPolicyIntent DefendCoreIntent(ulong sequence, ScenarioDefinition scenario)
        {
            uint reserveArmy = scenario.Armies.Where(a => a.FactionId == 1).Select(a => a.Id).OrderBy(id => id).Skip(2).First();
            uint core = scenario.Factions.First(f => f.Id == 1).CoreId;
            return new UserPolicyIntent(sequence, new ScopeKey(1, ScopeKind.Army, reserveArmy), PolicyKind.Defend,
                new PolicyGoal(GoalKind.Core, core, default), 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.SubjectGone));
        }
    }
}
