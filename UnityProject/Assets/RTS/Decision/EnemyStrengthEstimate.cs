using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Rts.Contracts;

namespace Rts.Decision
{
    /// <summary>
    /// An observation-only, faction-wide budget. Build it before selecting a local region:
    /// independently clipping each route would allocate the same population several times.
    /// Observed upper bounds have priority over doctrine-only warnings.
    /// </summary>
    internal static class EnemyStrengthEstimate
    {
        private const int WarningStrength = 10;
        private sealed class Budget
        {
            internal EnemyContact[] Contacts;
            internal EnemyContact[] Observed;
            internal long ObservedTotal;
            internal WarningRegion[] Warnings;
        }
        private readonly struct WarningRegion
        {
            internal readonly long X, Z;
            internal readonly SimPoint[] Positions;
            internal WarningRegion(long x, long z, SimPoint[] positions) { X = x; Z = z; Positions = positions; }
        }
        private static readonly ConditionalWeakTable<FactionObservation, Budget> budgets = new ConditionalWeakTable<FactionObservation, Budget>();

        private static Budget For(FactionObservation o) => budgets.GetValue(o, Build);

        private static Budget Build(FactionObservation o)
        {
            var contacts = PolicyDecision.CountableContacts(o).OrderBy(c => c.IsArmyContact).ThenBy(c => c.ContactId).ToArray();
            var observed = contacts.Where(c => !Unknown(o, c)).ToArray();
            var warningPositions = contacts.Where(c => Unknown(o, c)).Select(c => c.LastPosition)
                .Concat(o.Objectives.Where(g => UnknownObjective(o, g)
                    && !(g.IsOwnerKnown && g.OwnerFactionId == o.FactionId)
                    && !contacts.Any(c => !Unknown(o, c) && PolicyDecision.Within(c.LastPosition, g.Position, 24)))
                    .Select(g => g.Position)).ToArray();
            var warnings = warningPositions.GroupBy(p => (X: Region(p.X.Raw), Z: Region(p.Z.Raw)))
                .OrderBy(g => g.Key.X).ThenBy(g => g.Key.Z)
                .Select(g => new WarningRegion(g.Key.X, g.Key.Z, g.ToArray())).ToArray();
            return new Budget { Contacts = contacts, Observed = observed, ObservedTotal = observed.Sum(c => (long)Math.Max(0, c.EstimateMax)), Warnings = warnings };
        }
        // A warning region is the diameter of the 24 m contact evaluation circle.
        private static long Region(long raw)
        {
            long size = Fix64.FromInt(48).Raw;
            long cell = raw / size;
            return raw % size < 0 ? cell - 1 : cell;
        }
        private static bool Unknown(FactionObservation o, EnemyContact c)
            => c.IsStrengthUnknown || c.EstimateMax < 0 || o.Tick - c.LastSeenTick >= 600;
        internal static bool UnknownObjective(FactionObservation o, KnownObjective goal)
            => (goal.Kind == GoalKind.Outpost ? !goal.IsOwnerKnown : goal.Kind == GoalKind.Core && !goal.IsHpKnown)
                || o.Tick - goal.LastSeenTick >= 600;

        internal static int InRegion(FactionObservation o, Func<SimPoint, bool> contains, KnownObjective? target = null)
        {
            var budget = For(o);
            var contacts = budget.Contacts;
            var observed = budget.Observed;
            long total = budget.ObservedTotal;
            int cap = Math.Max(0, o.EnemyFactionCap);
            int observedBudget = (int)Math.Min(cap, total);
            long cumulative = 0, allocated = 0;
            int count = 0;
            foreach (var c in observed)
            {
                cumulative += Math.Max(0, c.EstimateMax);
                long next = total == 0 ? 0 : cumulative * observedBudget / total;
                if (contains(c.LastPosition)) count += (int)(next - allocated);
                allocated = next;
            }

            // Old contact rows are location hypotheses, not independent groups of ten.
            // Include all unobserved objectives in the same budget, even when querying one route.
            int warningBudget = (int)Math.Min(cap - observedBudget, (long)budget.Warnings.Length * WarningStrength);
            int warningCount = 0;
            for (int i = 0; i < budget.Warnings.Length; i++)
            {
                int share = (int)((long)(i + 1) * warningBudget / budget.Warnings.Length - (long)i * warningBudget / budget.Warnings.Length);
                // Objective warnings apply only when evaluating that objective; contact warnings
                // apply to local combat too. A region's share is never added twice.
                bool relevant = contacts.Any(c => Unknown(o, c) && contains(c.LastPosition)
                    && Region(c.LastPosition.X.Raw) == budget.Warnings[i].X && Region(c.LastPosition.Z.Raw) == budget.Warnings[i].Z);
                if (target.HasValue && UnknownObjective(o, target.Value)
                    && budget.Warnings[i].Positions.Any(p => p.Equals(target.Value.Position))) relevant = true;
                if (relevant) warningCount += share;
            }
            // A path union is one evaluation region, regardless of its contact/history count.
            return count + Math.Min(WarningStrength, warningCount);
        }
    }
}
