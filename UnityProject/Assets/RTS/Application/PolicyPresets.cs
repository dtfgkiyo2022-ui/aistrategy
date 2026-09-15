using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Application
{
    public static class PolicyPresets
    {
        /// <summary>Ordinary doctrine proposals, captured in the replay input log.</summary>
        public static ScheduledInput[] InitialInputs(ScenarioDefinition scenario, string preset, uint faction = 2)
        {
            if (preset == "none") return Array.Empty<ScheduledInput>();
            if (preset != "maintain" && preset != "maintain-legacy" && preset != "concentrate") throw new ArgumentException("Preset must be none, maintain, maintain-legacy or concentrate.");
            if (scenario.Outposts.Length < 2) throw new ArgumentException("Preset needs north and south outposts.");
            var sim = new Simulation.Simulation(scenario);
            var result = new List<ScheduledInput>();
            ulong baseId = (ulong)(faction - 1) * 10UL;
            // This remains for old callers and for the comparison-only legacy preset.
            // Live presets are driven by PresetController through CommandGateway.
            for (int i = 0; i < (preset == "maintain" ? 1 : 3); i++)
            {
                var scope = new ScopeKey(faction, i == 2 ? ScopeKind.Outpost : ScopeKind.All, i == 2 ? 2U : 0U);
                var kind = i == 0 ? PolicyKind.MaintainReserve : i == 1 ? PolicyKind.Focus : PolicyKind.AllowAbandon;
                var order = new PolicyOrder(baseId + (ulong)i + 1, 0, CommandSource.Doctrine, scope, kind,
                    i == 1 ? new PolicyGoal(GoalKind.Outpost, 1, default) : default, 50, new LossBudget(300),
                    new EndCondition(i == 1 ? EndKind.ObjectiveOwned : EndKind.UntilReplaced, 0), i == 0 && preset == "maintain-legacy" ? (ushort)200 : (ushort)(i == 0 && preset == "maintain" ? 100 : 0),
                    sim.Revision(scope), sim.Versions(scope).Where(v => !v.Scope.Equals(scope)).ToArray(), i,
                    new Expiration(long.MaxValue, 0, ExpireFlags.None));
                var input = new ScheduledInput(baseId + (ulong)i + 1, InputKind.Proposal, i, i + 1, baseId + (ulong)i + 1, baseId + (ulong)i + 1, new[] { order });
                result.Add(input); sim.Step(i + 1, new[] { input });
            }
            return result.ToArray();
        }

        public static PresetController CreateController(string preset, uint faction, ICommandPort port)
            => new PresetController(preset, faction, port, (port as CommandGateway)?.FactionVersions(faction));

        /// <summary>Runs the recording-only proposal generator. Replay receives only its returned log.</summary>
        public static ScheduledInput[] RecordedInputs(ScenarioDefinition scenario, string westPreset, string eastPreset, long ticks)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            var sim = new Simulation.Simulation(scenario);
            var gateway = new CommandGateway(sim);
            var west = CreateController(westPreset ?? "none", 1, gateway);
            var east = CreateController(eastPreset ?? "none", 2, gateway);
            west.Initialize(); east.Initialize();
            for (long i = 0; i < ticks && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                var westFrame = sim.Capture(1);
                if (westFrame.Result.HasEnded) break;
                west.Step(westFrame);
                east.Step(sim.Capture(2));
            }
            return gateway.Inputs.ToArray();
        }
    }

    /// <summary>Faction-local, post-step doctrine state machine. It consumes no simulation state.</summary>
    public sealed class PresetController
    {
        private readonly string preset;
        private readonly uint faction;
        private readonly ICommandPort port;
        private readonly IFactionPolicyVersions versions;
        private ulong sequence = 1;
        private ulong northFocusCommandId;
        private bool concentrated;
        private int initialRetries, secondRetries;
        private ulong lastRetriedStale;
        private readonly Dictionary<uint, ulong> defendCommands = new Dictionary<uint, ulong>();
        private readonly Dictionary<uint, int> defendStaleRetries = new Dictionary<uint, int>();

        public PresetController(string preset, uint faction, ICommandPort port, IFactionPolicyVersions versionReader = null)
        {
            if (preset != "none" && preset != "maintain" && preset != "concentrate" && preset != "maintain-legacy") throw new ArgumentException("Preset must be none, maintain, maintain-legacy or concentrate.", nameof(preset));
            if (faction < 1 || faction > 2) throw new ArgumentOutOfRangeException(nameof(faction));
            this.preset = preset; this.faction = faction; this.port = port ?? throw new ArgumentNullException(nameof(port));
            versions = versionReader ?? port as IFactionPolicyVersions;
        }

        public void Initialize()
        {
            if (preset == "none") return;
            ProposeInitial(0);
        }

        public void Step(FactionFrame frame)
        {
            if (frame == null || frame.FactionId != faction) throw new ArgumentException("Wrong faction frame.", nameof(frame));
            if (preset == "none") return;
            LearnCommandIds(frame);
            RetryStale(frame);
            if (preset == "maintain") Maintain(frame);
            else if (preset == "concentrate") Concentrate(frame);
        }

        private void Maintain(FactionFrame frame)
        {
            foreach (var objective in frame.Objectives.Where(v => v.Kind == GoalKind.Outpost && v.IsOwnerKnown).OrderBy(v => v.Id))
            {
                if (objective.OwnerFactionId != faction) { defendCommands.Remove(objective.Id); defendStaleRetries.Remove(objective.Id); continue; }
                // A proposal is not visible in the frame until the gateway has applied it.  Keep
                // the zero entry during that interval (and after a rejection/terminal result):
                // maintain renews Defend on ownership reacquisition, not on every tick that an
                // earlier request is absent from the frame.
                if (defendCommands.ContainsKey(objective.Id)) continue;
                Propose(frame.Tick, new[] { Order(new ScopeKey(faction, ScopeKind.Outpost, objective.Id), PolicyKind.Defend,
                    new PolicyGoal(GoalKind.Outpost, objective.Id, default), 0, EndKind.UntilReplaced, ExpireFlags.OwnershipChanged) });
                defendCommands[objective.Id] = 0; // replaced by the deterministic command id when it reaches the frame
            }
        }

        private void Concentrate(FactionFrame frame)
        {
            if (concentrated || northFocusCommandId == 0) return;
            var focus = frame.Commands.FirstOrDefault(c => c.CommandId == northFocusCommandId);
            if (focus.CommandId == 0 || focus.Status != CommandStatus.Completed || focus.Reason != ReasonCode.None) return;
            concentrated = true;
            ProposeSecond(frame.Tick);
        }

        private void ProposeInitial(long tick, bool retry = false)
        {
            if (retry) initialRetries++;
            if (preset == "maintain" || preset == "maintain-legacy")
                Propose(tick, new[] { Order(All, PolicyKind.MaintainReserve, default, preset == "maintain" ? (ushort)100 : (ushort)200, EndKind.UntilReplaced, ExpireFlags.None) });
            else
                // The two All orders acquire their common revision atomically.
                Propose(tick, new[] {
                    Order(All, PolicyKind.MaintainReserve, default, 0, EndKind.UntilReplaced, ExpireFlags.None),
                    Order(All, PolicyKind.Focus, new PolicyGoal(GoalKind.Outpost, 1, default), 0, EndKind.ObjectiveOwned, ExpireFlags.None),
                    Order(new ScopeKey(faction, ScopeKind.Outpost, 2), PolicyKind.AllowAbandon, default, 0, EndKind.UntilReplaced, ExpireFlags.None) });
        }

        private void ProposeSecond(long tick, bool retry = false)
        {
            if (retry) secondRetries++;
            uint enemyCore = faction == 1 ? 2U : 1U;
            Propose(tick, new[] {
                Order(new ScopeKey(faction, ScopeKind.Outpost, 1), PolicyKind.AllowAbandon, default, 0, EndKind.UntilReplaced, ExpireFlags.None),
                Order(All, PolicyKind.Focus, new PolicyGoal(GoalKind.Core, enemyCore, default), 0, EndKind.UntilReplaced, ExpireFlags.None) });
        }

        private void RetryStale(FactionFrame frame)
        {
            var stale = frame.Commands.Where(c => c.Source == CommandSource.Doctrine && c.Status == CommandStatus.Expired && c.Reason == ReasonCode.StaleVersion && c.CommandId > lastRetriedStale)
                .OrderBy(c => c.CommandId).LastOrDefault();
            if (stale.CommandId == 0) return;
            lastRetriedStale = stale.CommandId;
            if ((stale.Kind == PolicyKind.MaintainReserve || stale.Kind == PolicyKind.Focus && stale.Goal.Kind == GoalKind.Outpost ||
                    stale.Kind == PolicyKind.AllowAbandon && stale.Target.Equals(new ScopeKey(faction, ScopeKind.Outpost, 2))) && initialRetries < 3)
            { northFocusCommandId = 0; ProposeInitial(frame.Tick, true); }
            else if (stale.Kind == PolicyKind.Focus && stale.Goal.Kind == GoalKind.Core && secondRetries < 3)
            { concentrated = false; ProposeSecond(frame.Tick, true); }
        }

        private void LearnCommandIds(FactionFrame frame)
        {
            if (preset == "concentrate" && northFocusCommandId == 0)
            {
                var focus = frame.Commands.Where(c => c.Source == CommandSource.Doctrine && c.Target.Equals(All) && c.Kind == PolicyKind.Focus && c.Goal.Kind == GoalKind.Outpost && c.Goal.Id == 1)
                    .OrderBy(c => c.CommandId).FirstOrDefault();
                northFocusCommandId = focus.CommandId;
            }
            foreach (uint id in defendCommands.Where(p => p.Value == 0).Select(p => p.Key).ToArray())
            {
                var defend = frame.Commands.Where(c => c.Source == CommandSource.Doctrine && c.Target.Equals(new ScopeKey(faction, ScopeKind.Outpost, id)) && c.Kind == PolicyKind.Defend)
                    .OrderByDescending(c => c.CommandId).FirstOrDefault();
                if (defend.CommandId != 0) defendCommands[id] = defend.CommandId;
            }
            foreach (uint id in defendCommands.Where(p => p.Value != 0).Select(p => p.Key).ToArray())
            {
                var defend = frame.Commands.FirstOrDefault(c => c.CommandId == defendCommands[id]);
                if (defend.Status == CommandStatus.Executing)
                {
                    defendStaleRetries[id] = 0;
                    continue;
                }
                if (defend.Status == CommandStatus.Expired && defend.Reason == ReasonCode.StaleVersion)
                {
                    int retries = defendStaleRetries.TryGetValue(id, out var value) ? value : 0;
                    if (retries < 3)
                    {
                        defendStaleRetries[id] = retries + 1;
                        defendCommands.Remove(id);
                    }
                }
            }
        }

        private ScopeKey All => new ScopeKey(faction, ScopeKind.All, 0);
        private void Propose(long tick, IReadOnlyList<PolicyOrder> orders)
        {
            var observed = orders.Select(o =>
            {
                var snapshot = versions == null ? Array.Empty<PolicyVersion>() : versions.Versions(o.Target).ToArray();
                var targets = snapshot.Where(v => v.Scope.Equals(o.Target)).ToArray();
                if (snapshot.Length != 0 && targets.Length != 1)
                    throw new InvalidOperationException("Expected exactly one target policy version.");
                ulong revision = snapshot.Length == 0 ? 0 : targets[0].Revision;
                return new PolicyOrder(o.CommandId, o.BatchId, o.Source, o.Target, o.Kind, o.Goal, o.Priority,
                    o.AllowedLoss, o.End, o.ReservePermille, revision, snapshot.Where(v => !v.Scope.Equals(o.Target)).ToArray(), tick, o.Expiration);
            }).ToArray();
            port.Propose(faction, sequence++, observed, checked(tick + 1));
        }
        private PolicyOrder Order(ScopeKey target, PolicyKind kind, PolicyGoal goal, ushort reserve, EndKind end, ExpireFlags flags)
            => new PolicyOrder(0, 0, CommandSource.Doctrine, target, kind, goal, 50, new LossBudget(300), new EndCondition(end, 0), reserve,
                0, Array.Empty<PolicyVersion>(), 0, new Expiration(long.MaxValue, 0, flags));
    }
}
