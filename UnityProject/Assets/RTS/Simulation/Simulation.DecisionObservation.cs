using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        // Decision receives a new immutable DTO snapshot; it never receives WorldState or a query delegate.
        private readonly FactionObservation[] decisionObservations = new FactionObservation[2];
        private FactionObservation ObserveForDecision(uint faction)
        {
            var frame = frames[faction - 1].Observation;
            var raids = CavalryAllowed(faction) ? ObservedRaidTargets(faction, frame) : Array.Empty<ObservedRaidTarget>();
            return decisionObservations[faction - 1] = new FactionObservation(faction, frame.Tick,
                frame.OwnArmies, frame.VisibleEnemies, frame.Contacts, frame.Objectives, frame.EnemyFactionCap, raids);
        }

        private IReadOnlyList<ObservedRaidTarget> ObservedRaidTargets(uint faction, FactionObservation frame)
        {
            var result = new List<ObservedRaidTarget>();
            var visibleCells = world.Factions[faction - 1].VisibleCells;
            foreach (var node in world.Nodes)
                if (node.Remaining > 0 && visibleCells[world.Map.Cell(node.Definition.Position)])
                    result.Add(new ObservedRaidTarget(node.Definition.Id, RaidTargetKind.Resource, node.Definition.Position));
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var villager = world.Villagers[i];
                if (villager.Alive && villager.FactionId != faction && IsVisibleTo(faction, villager.Position))
                    result.Add(new ObservedRaidTarget(0x40000000U | villager.Id, RaidTargetKind.Carrier, villager.Position, 1));
            }
            foreach (var enemy in frame.VisibleEnemies.OrderBy(e => e.ContactId))
            {
                int nearby = frame.VisibleEnemies.Count(other => other.ContactId != enemy.ContactId
                    && PolicyDecision.Within(other.Position, enemy.Position, 12));
                if (nearby == 0)
                    result.Add(new ObservedRaidTarget(0x80000000U | enemy.ContactId, RaidTargetKind.IsolatedArmy, enemy.Position, 1));
            }
            return result.OrderBy(t => t.Kind).ThenBy(t => t.Id).ToArray();
        }
        private void WriteDecisionObservation(StateWriter w, int faction, string prefix)
        {
            var o = decisionObservations[faction];
            w.Value(prefix + "Present", o != null);
            if (o == null) return;
            w.Value(prefix + "Tick", o.Tick);
            w.Value(prefix + "ContactCount", o.Contacts.Count);
            for (int i = 0; i < o.Contacts.Count; i++)
            {
                var c = o.Contacts[i]; string p = prefix + "Contacts[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "].";
                w.Value(p + "Id", c.ContactId); w.Point(p + "Position", c.LastPosition); w.Value(p + "LastSeenTick", c.LastSeenTick);
                w.Value(p + "ArmyContact", c.IsArmyContact); w.Value(p + "Uncertain", c.IsUncertain); w.Value(p + "Unknown", c.IsStrengthUnknown); w.Value(p + "Assumed", c.AssumedStrength); w.Value(p + "Absent", c.IsAbsentAtLastPosition);
                w.Value(p + "Min", c.EstimateMin); w.Value(p + "Max", c.EstimateMax); w.Value(p + "Visible", c.IsCurrentlyVisible);
                w.Ids(p + "Covered", (c.CoveredContactIds ?? Array.Empty<uint>()).ToArray());
            }
            w.Value(prefix + "ObjectiveCount", o.Objectives.Count);
            for (int i = 0; i < o.Objectives.Count; i++)
            {
                var g = o.Objectives[i]; string p = prefix + "Objectives[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "].";
                w.Value(p + "Kind", (byte)g.Kind); w.Value(p + "Id", g.Id); w.Point(p + "Position", g.Position);
                w.Value(p + "OwnerKnown", g.IsOwnerKnown); w.Value(p + "Owner", g.OwnerFactionId); w.Value(p + "HpKnown", g.IsHpKnown); w.Value(p + "Hp", g.Hp);
                w.Value(p + "LastSeenTick", g.LastSeenTick);
                w.Value(p + "CapturingFaction", g.CapturingFactionId); w.Value(p + "CaptureTicks", g.CaptureTicks); w.Value(p + "CaptureDurationTicks", g.CaptureDurationTicks);
            }
            if (CavalryOn)
            {
                w.Value(prefix + "RaidTargetCount", o.RaidTargets.Count);
                for (int i = 0; i < o.RaidTargets.Count; i++)
                {
                    var t = o.RaidTargets[i]; string p = prefix + "RaidTargets[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "].";
                    w.Value(p + "Id", t.Id); w.Value(p + "Kind", (byte)t.Kind); w.Point(p + "Position", t.Position); w.Value(p + "Strength", t.StrengthEstimate);
                }
            }
        }
    }
}
