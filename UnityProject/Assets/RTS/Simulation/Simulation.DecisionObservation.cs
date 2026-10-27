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
            bool cavalry = CavalryAllowed(faction);
            if (!cavalry) return decisionObservations[faction - 1] = frame;
            var raids = ObservedRaidTargets(faction, frame);
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
            var nearbyEnemies = new Dictionary<(long X, long Z), List<VisibleEnemy>>();
            const long cellRaw = 12L * 65536L;
            foreach (var enemy in frame.VisibleEnemies)
            {
                var key = (FloorCell(enemy.Position.X.Raw, cellRaw), FloorCell(enemy.Position.Z.Raw, cellRaw));
                if (!nearbyEnemies.TryGetValue(key, out var bucket))
                    nearbyEnemies[key] = bucket = new List<VisibleEnemy>();
                bucket.Add(enemy);
            }
            foreach (var enemy in frame.VisibleEnemies.OrderBy(e => e.ContactId))
            {
                if (!HasNearbyEnemy(enemy, nearbyEnemies, cellRaw))
                    result.Add(new ObservedRaidTarget(0x80000000U | enemy.ContactId, RaidTargetKind.IsolatedArmy, enemy.Position, 1));
            }
            return result.OrderBy(t => t.Kind).ThenBy(t => t.Id).ToArray();
        }

        private static long FloorCell(long raw, long size)
        {
            long cell = raw / size;
            return raw % size < 0 ? cell - 1 : cell;
        }

        private static bool HasNearbyEnemy(VisibleEnemy enemy, Dictionary<(long X, long Z), List<VisibleEnemy>> index, long cellRaw)
        {
            long x = FloorCell(enemy.Position.X.Raw, cellRaw), z = FloorCell(enemy.Position.Z.Raw, cellRaw);
            for (long dz = -1; dz <= 1; dz++)
                for (long dx = -1; dx <= 1; dx++)
                    if (index.TryGetValue((x + dx, z + dz), out var bucket))
                        for (int i = 0; i < bucket.Count; i++)
                        {
                            var other = bucket[i];
                            if (other.ContactId != enemy.ContactId && PolicyDecision.Within(other.Position, enemy.Position, 12)) return true;
                        }
            return false;
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
