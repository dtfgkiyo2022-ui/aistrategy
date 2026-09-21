using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>
    /// Writes a FactionObservation as the "state" sent to the model. Only what the faction could observe goes in
    /// (rule 6): every field here is read from the observation, never from the world.
    /// <para>
    /// The first version sent bare coordinates and counts. Measured over four matches the model answered "attack the
    /// enemy core" 50 times out of 58 with low confidence, which is what a reader who cannot tell north from south,
    /// nor who is ahead, would answer. So this version says things in the terms the question is asked in: which
    /// outpost is which, whose each objective is, how the two sides compare, and how old each sighting is. Nothing
    /// here is used to decide anything inside the game.
    /// </para>
    /// </summary>
    public static class JevState
    {
        /// <summary>Bumped whenever the content changes, because the answers are only comparable within one version.</summary>
        public const string Version = "s2";

        public static string Build(FactionObservation o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            var names = OutpostNames(o);
            var sb = new StringBuilder();
            sb.Append("{\"stateVersion\":\"").Append(Version).Append("\",\"tick\":").Append(o.Tick);
            sb.Append(",\"secondsElapsed\":").Append(o.Tick / 20);

            int myAlive = o.OwnArmies.Sum(a => a.AliveCount);
            sb.Append(",\"myTotalSoldiers\":").Append(myAlive);
            sb.Append(",\"myArmies\":[");
            for (int i = 0; i < o.OwnArmies.Count; i++)
            {
                var a = o.OwnArmies[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(a.Id).Append(",\"soldiers\":").Append(a.AliveCount)
                  .Append(",\"x\":").Append(M(a.Position.X)).Append(",\"z\":").Append(M(a.Position.Z))
                  .Append(",\"defends\":\"").Append(Name(names, a.HomeObjective.Kind, a.HomeObjective.Id)).Append('"');
                long nearest = NearestEnemyMeters(a.Position, o.Contacts);
                if (nearest >= 0) sb.Append(",\"nearestEnemyMeters\":").Append(nearest);
                sb.Append('}');
            }

            // Estimates are a range because a contact that has not been looked at recently is only bounded, not counted.
            int min = o.Contacts.Sum(c => Math.Max(c.EstimateMin, 0));
            int max = o.Contacts.Sum(c => c.EstimateMax < 0 ? c.AssumedStrength : c.EstimateMax);
            sb.Append("],\"enemy\":{\"knownSoldiersAtLeast\":").Append(min).Append(",\"knownSoldiersAtMost\":").Append(max)
              .Append(",\"maxPossibleSoldiers\":").Append(o.EnemyFactionCap)
              .Append(",\"sightings\":[");
            for (int i = 0; i < o.Contacts.Count; i++)
            {
                var c = o.Contacts[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"x\":").Append(M(c.LastPosition.X)).Append(",\"z\":").Append(M(c.LastPosition.Z))
                  .Append(",\"visibleNow\":").Append(Bool(c.IsCurrentlyVisible))
                  .Append(",\"sightingAgeSeconds\":").Append(Math.Max(0, o.Tick - c.LastSeenTick) / 20)
                  .Append(",\"soldiersAtLeast\":").Append(Math.Max(c.EstimateMin, 0))
                  .Append(",\"soldiersAtMost\":").Append(c.EstimateMax < 0 ? c.AssumedStrength : c.EstimateMax)
                  .Append(",\"strengthUnknown\":").Append(Bool(c.IsStrengthUnknown))
                  .Append(",\"goneFromHere\":").Append(Bool(c.IsAbsentAtLastPosition)).Append('}');
            }
            sb.Append("]},\"objectives\":[");
            for (int i = 0; i < o.Objectives.Count; i++)
            {
                var b = o.Objectives[i];
                if (i > 0) sb.Append(',');
                // A core is named by whose it is, because that is the only thing the question needs to tell apart.
                string name = b.Kind == GoalKind.Core
                    ? (b.IsOwnerKnown && b.OwnerFactionId == o.FactionId ? "my core" : "the enemy core")
                    : Name(names, b.Kind, b.Id);
                sb.Append("{\"name\":\"").Append(name).Append('"')
                  .Append(",\"x\":").Append(M(b.Position.X)).Append(",\"z\":").Append(M(b.Position.Z))
                  .Append(",\"heldBy\":\"").Append(Owner(b, o.FactionId)).Append('"');
                if (b.IsHpKnown) sb.Append(",\"hp\":").Append(b.Hp);
                if (b.CapturingFactionId != 0 && b.CaptureDurationTicks > 0)
                    sb.Append(",\"beingTakenBy\":\"").Append(b.CapturingFactionId == o.FactionId ? "me" : "the enemy")
                      .Append("\",\"capturePercent\":").Append(100 * b.CaptureTicks / b.CaptureDurationTicks);
                sb.Append(",\"lastSeenSecondsAgo\":").Append(Math.Max(0, o.Tick - b.LastSeenTick) / 20).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// The question offers "north" and "south", but the observation only has coordinates and the model has no map.
        /// Among the outposts that are visible to this faction, the one furthest along z is the northern one.
        /// </summary>
        private static Dictionary<uint, string> OutpostNames(FactionObservation o)
        {
            var names = new Dictionary<uint, string>();
            var outposts = o.Objectives.Where(b => b.Kind == GoalKind.Outpost)
                .OrderByDescending(b => b.Position.Z.Raw).ThenBy(b => b.Id).ToArray();
            if (outposts.Length == 2)
            {
                names[outposts[0].Id] = "the north outpost";
                names[outposts[1].Id] = "the south outpost";
            }
            else foreach (var b in outposts) names[b.Id] = "outpost " + b.Id;
            return names;
        }

        private static string Name(Dictionary<uint, string> names, GoalKind kind, uint id)
        {
            if (kind == GoalKind.Outpost) return names.TryGetValue(id, out var name) ? name : "outpost " + id;
            if (kind == GoalKind.Core) return "a core"; // only reached for an army home objective
            return "nothing in particular";
        }

        private static string Owner(KnownObjective b, uint me)
        {
            if (!b.IsOwnerKnown) return "unknown";
            if (b.OwnerFactionId == me) return "me";
            return b.OwnerFactionId == 0 ? "nobody" : "the enemy";
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static long NearestEnemyMeters(SimPoint from, IReadOnlyList<EnemyContact> contacts)
        {
            long best = -1;
            foreach (var c in contacts)
            {
                long dx = (from.X.Raw - c.LastPosition.X.Raw) / 65536, dz = (from.Z.Raw - c.LastPosition.Z.Raw) / 65536;
                // Whole metres are enough for a question, and squaring metres cannot overflow on a 256 m map.
                long squared = dx * dx + dz * dz;
                long metres = (long)Math.Sqrt(squared);
                if (best < 0 || metres < best) best = metres;
            }
            return best;
        }

        // Fix64 raw / 65536 = meters. Written with a fixed culture so a Japanese-locale PC prints the same text.
        private static string M(Fix64 v) => (v.Raw / 65536.0).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
