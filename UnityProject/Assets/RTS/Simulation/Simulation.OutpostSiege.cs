using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public partial class Simulation
    {
        private bool OutpostSiegeOn => world.Config.Economy.Enabled && world.Config.Economy.OutpostSiege;

        private void ApplyOutpostSiege()
        {
            if (!OutpostSiegeOn || world.Tick <= 0 || world.Tick % 300 != 0) return;

            int westOutposts = 0;
            int eastOutposts = 0;
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                uint owner = world.Outposts[i].OwnerFactionId;
                if (owner == 1) westOutposts++;
                else if (owner == 2) eastOutposts++;
            }

            if (westOutposts > 0)
            {
                int coreIndex = (int)world.Factions[1].CoreId - 1;
                world.Cores[coreIndex].Hp = RemainingHp(world.Cores[coreIndex].Hp,
                    (long)westOutposts * EconomyRules.OutpostSiegeDamage);
            }
            if (eastOutposts > 0)
            {
                int coreIndex = (int)world.Factions[0].CoreId - 1;
                world.Cores[coreIndex].Hp = RemainingHp(world.Cores[coreIndex].Hp,
                    (long)eastOutposts * EconomyRules.OutpostSiegeDamage);
            }
        }

        /// <summary>
        /// Uses only observed objective ownership to make the ordinary AI send one available army to an enemy-held
        /// outpost. Core defence and committed push-wave assaults retain priority.
        /// </summary>
        private void ApplyOutpostSiegeFocus(uint faction, FactionObservation observation,
            IReadOnlyList<ArmyDecisionInput> inputs, ArmyDecisionMemory[] allocations)
        {
            if (!OutpostSiegeOn || observation == null || inputs == null || allocations == null) return;
            if (observation.Objectives.All(o => o.Kind != GoalKind.Outpost || !o.IsOwnerKnown
                || o.OwnerFactionId == 0 || o.OwnerFactionId == faction)) return;
            if (inputs.Any(i => IsLatePushAttack(i.Army.Id))) return;

            int selected = -1;
            int selectedDistance = int.MaxValue;
            uint selectedArmy = uint.MaxValue;
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                if (input.Policy.Kind != 0 || input.Army.AliveCount <= 0 || input.IsReserveRole
                    || input.Memory.Returning || input.Army.Kind == UnitKind.Scout
                    || allocations[i].Returning || allocations[i].Assignment == AssignmentKind.CoreDefense
                    || allocations[i].Assignment == AssignmentKind.Guard) continue;
                int distance = input.Routes
                    .Where(r => r.Goal.Kind == GoalKind.Outpost && observation.Objectives.Any(o => o.Kind == GoalKind.Outpost
                        && o.Id == r.Goal.Id && o.IsOwnerKnown && o.OwnerFactionId != 0 && o.OwnerFactionId != faction))
                    .Select(r => r.Distance).DefaultIfEmpty(int.MaxValue).Min();
                if (distance == int.MaxValue) continue;
                if (distance < selectedDistance || (distance == selectedDistance && input.Army.Id < selectedArmy))
                {
                    selected = i;
                    selectedDistance = distance;
                    selectedArmy = input.Army.Id;
                }
            }
            if (selected < 0) return;

            var target = observation.Objectives
                .Where(o => o.Kind == GoalKind.Outpost && o.IsOwnerKnown && o.OwnerFactionId != 0 && o.OwnerFactionId != faction)
                .OrderBy(o => inputRouteDistance(inputs[selected], o.Id))
                .ThenBy(o => o.Id)
                .First();
            allocations[selected].Assignment = AssignmentKind.Advance;
            allocations[selected].Goal = new PolicyGoal(GoalKind.Outpost, target.Id, default);
        }

        private static int inputRouteDistance(ArmyDecisionInput input, uint objectiveId)
        {
            return input.Routes.Where(r => r.Goal.Kind == GoalKind.Outpost && r.Goal.Id == objectiveId)
                .Select(r => r.Distance).DefaultIfEmpty(int.MaxValue).First();
        }
    }
}
