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
            for (int i = 0; i < (preset == "maintain" ? 1 : 3); i++)
            {
                var scope = new ScopeKey(faction, i == 2 ? ScopeKind.Outpost : ScopeKind.All, i == 2 ? 2U : 0U);
                var kind = i == 0 ? PolicyKind.MaintainReserve : i == 1 ? PolicyKind.Focus : PolicyKind.AllowAbandon;
                var order = new PolicyOrder(baseId + (ulong)i + 1, 0, CommandSource.Doctrine, scope, kind,
                    i == 1 ? new PolicyGoal(GoalKind.Outpost, 1, default) : default, 50, new LossBudget(300),
                    new EndCondition(EndKind.UntilReplaced, 0), i == 0 && preset == "maintain-legacy" ? (ushort)200 : (ushort)(i == 0 && preset == "maintain" ? 100 : 0),
                    sim.Revision(scope), sim.Versions(scope).Where(v => !v.Scope.Equals(scope)).ToArray(), i,
                    new Expiration(long.MaxValue, 0, ExpireFlags.None));
                var input = new ScheduledInput(baseId + (ulong)i + 1, InputKind.Proposal, i, i + 1, baseId + (ulong)i + 1, baseId + (ulong)i + 1, new[] { order });
                result.Add(input); sim.Step(i + 1, new[] { input });
            }
            return result.ToArray();
        }
    }
}
