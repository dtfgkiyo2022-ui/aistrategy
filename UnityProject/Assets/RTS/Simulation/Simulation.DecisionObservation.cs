using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        // Decision's observation boundary. The prototype rendering frame remains omniscient;
        // neither strategic nor tactical decisions consume that enemy/objective list directly.
        private readonly FactionObservation[] decisionObservations = new FactionObservation[2];
        private FactionObservation ObserveForDecision(uint faction)
        {
            var frame = frames[faction - 1].Observation;
            var previous = decisionObservations[faction - 1];
            var sensors = world.Soldiers.Take(world.SoldierCount).Where(s => s.Alive && s.Initial.FactionId == faction)
                .Select(s => (Position: s.Position, Radius: s.Parameters.Vision)).ToList();
            sensors.AddRange(world.Cores.Where(c => c.Definition.FactionId == faction && c.Hp > 0).Select(c => (c.Definition.Position, world.Config.Rules.OwnedObjectiveVision)));
            sensors.AddRange(world.Outposts.Where(p => p.OwnerFactionId == faction).Select(p => (p.Definition.Position, world.Config.Rules.OwnedObjectiveVision)));
            bool Visible(SimPoint p) => sensors.Any(s => InRange(s.Position, world.Map.Center(world.Map.Cell(p)), s.Radius));
            var enemies = frame.VisibleEnemies.Where(e => Visible(e.Position)).ToArray();
            var contacts = new List<EnemyContact>();
            if (previous != null)
                foreach (var contact in previous.Contacts)
                    if (!enemies.Any(e => e.ContactId == contact.ContactId) && !Visible(contact.LastPosition))
                        contacts.Add(new EnemyContact(contact.ContactId, contact.LastPosition, contact.LastSeenTick, contact.EstimateMin, contact.EstimateMax, false, contact.CoveredContactIds));
            contacts.AddRange(frame.Contacts.Where(c => enemies.Any(e => e.ContactId == c.ContactId)));
            var objectives = frame.Objectives.Select(g => {
                bool own = g.OwnerFactionId == faction;
                if (own || Visible(g.Position)) return g;
                var old = previous == null ? default : previous.Objectives.FirstOrDefault(x => x.Kind == g.Kind && x.Id == g.Id);
                if (old.Kind == g.Kind && old.Id == g.Id && old.OwnerFactionId != faction) return old;
                // Core affiliation and all objective positions are public map data; enemy HP/ownership are not.
                return new KnownObjective(g.Kind, g.Id, g.Position, g.Kind == GoalKind.Core, g.Kind == GoalKind.Core ? g.OwnerFactionId : 0, false, 0, frame.Tick);
            }).ToArray();
            return decisionObservations[faction - 1] = new FactionObservation(faction, frame.Tick, frame.OwnArmies, enemies,
                contacts.OrderBy(c => c.ContactId).ToArray(), objectives);
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
        }
    }
}
