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
    /// <para>
    /// s5 (G-3): armies and sightings are grouped (by the objective they defend and by region) instead of listed one
    /// by one. On 2026-10-04 the one-by-one state averaged about 5,000 input tokens a call (up to about 7,300), which
    /// at one call every 10 seconds is about 2.9 yen a match - the whole default budget. The target is about 800.
    /// </para>
    /// </summary>
    public static class JevState
    {
        /// <summary>Bumped whenever the content changes, because the answers are only comparable within one version.</summary>
        public const string Version = "s5";

        public static string Build(FactionObservation o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            var names = OutpostNames(o);
            var sb = new StringBuilder();
            sb.Append("{\"stateVersion\":\"").Append(Version).Append("\",\"tick\":").Append(o.Tick)
              .Append(",\"secondsElapsed\":").Append(o.Tick / 20);
            int myAlive = o.OwnArmies.Sum(a => a.AliveCount);
            sb.Append(",\"myTotalSoldiers\":").Append(myAlive).Append(",\"myArmies\":[");
            var armyGroups = o.OwnArmies.GroupBy(a => Name(names, a.HomeObjective.Kind, a.HomeObjective.Id) + "|" + a.Kind)
                .OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < armyGroups.Length; i++)
            {
                var group = armyGroups[i];
                var first = group.First();
                if (i > 0) sb.Append(',');
                string defends = Name(names, first.HomeObjective.Kind, first.HomeObjective.Id);
                int count = group.Count();
                int soldiers = group.Sum(v => v.AliveCount);
                long nearest = group.Select(v => NearestEnemyMetres(v.Position, o.Contacts)).Where(v => v >= 0)
                    .DefaultIfEmpty(-1).Min();
                sb.Append("{\"defends\":\"").Append(defends).Append("\",\"unit\":\"").Append(first.Kind)
                  .Append("\",\"count\":").Append(count).Append(",\"soldiers\":").Append(soldiers)
                  .Append(",\"state\":\"").Append(ArmyState(nearest)).Append('"');
                if (count == 1)
                {
                    sb.Append(",\"x\":").Append(M(first.Position.X)).Append(",\"z\":").Append(M(first.Position.Z));
                    if (nearest >= 0) sb.Append(",\"nearestEnemyMeters\":").Append(nearest);
                }
                sb.Append('}');
            }
            // Estimates are a range because a contact that has not been looked at recently is only bounded, not counted.
            int min = o.Contacts.Sum(c => Math.Max(c.EstimateMin, 0));
            int max = o.Contacts.Sum(c => c.EstimateMax < 0 ? c.AssumedStrength : c.EstimateMax);
            sb.Append("],\"enemy\":{\"knownSoldiersAtLeast\":").Append(min)
              .Append(",\"knownSoldiersAtMost\":").Append(max)
              .Append(",\"maxPossibleSoldiers\":").Append(o.EnemyFactionCap).Append(",\"sightings\":[");
            var sightings = o.Contacts.GroupBy(c => RegionName(c.LastPosition, o, names))
                .OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < sightings.Length; i++)
            {
                var group = sightings[i];
                var first = group.First();
                if (i > 0) sb.Append(',');
                int lower = group.Sum(v => Math.Max(v.EstimateMin, 0));
                int upper = group.Sum(v => v.EstimateMax < 0 ? v.AssumedStrength : v.EstimateMax);
                long age = group.Max(v => Math.Max(0, o.Tick - v.LastSeenTick)) / 20;
                sb.Append("{\"region\":\"").Append(group.Key).Append("\",\"count\":").Append(group.Count())
                  .Append(",\"x\":").Append(M(first.LastPosition.X)).Append(",\"z\":").Append(M(first.LastPosition.Z))
                  .Append(",\"visibleNow\":").Append(Bool(group.Any(v => v.IsCurrentlyVisible)))
                  .Append(",\"sightingAgeSeconds\":").Append(age)
                  .Append(",\"soldiersAtLeast\":").Append(lower)
                  .Append(",\"soldiersAtMost\":").Append(upper)
                  .Append(",\"strengthUnknown\":").Append(Bool(group.Any(v => v.IsStrengthUnknown)))
                  .Append(",\"goneFromHere\":").Append(Bool(group.All(v => v.IsAbsentAtLastPosition))).Append('}');
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
                // The kind is spelled out because a question that distinguishes outposts from cores can only do it by
                // a word the state carries: relying on the name alone, an answer about outposts also matched the core.
                sb.Append("{\"name\":\"").Append(name).Append('"')
                  .Append(",\"kind\":\"").Append(b.Kind == GoalKind.Core ? "core" : "outpost").Append('"')
                  .Append(",\"x\":").Append(M(b.Position.X)).Append(",\"z\":").Append(M(b.Position.Z))
                  .Append(",\"heldBy\":\"").Append(Owner(b, o.FactionId)).Append('"');
                if (b.IsHpKnown) sb.Append(",\"hp\":").Append(b.Hp);
                long near = NearestEnemyMetres(b.Position, o.Contacts);
                if (near >= 0) sb.Append(",\"nearestEnemyMeters\":").Append(near);
                if (b.CapturingFactionId != 0 && b.CaptureDurationTicks > 0)
                    sb.Append(",\"beingTakenBy\":\"").Append(b.CapturingFactionId == o.FactionId ? "me" : "the enemy")
                      .Append("\",\"capturePercent\":").Append(100 * b.CaptureTicks / b.CaptureDurationTicks);
                sb.Append(",\"lastSeenSecondsAgo\":").Append(Math.Max(0, o.Tick - b.LastSeenTick) / 20).Append('}');
            }
            return sb.Append("]}").ToString();
        }

        /// <summary>
        /// The id the state called the north (or south) outpost, so an answer naming one can be turned into an order
        /// for the same objective. 0 when this faction cannot see two outposts and the names were never used.
        /// </summary>
        public static uint OutpostId(FactionObservation o, bool north)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            var outposts = o.Objectives.Where(b => b.Kind == GoalKind.Outpost)
                .OrderByDescending(b => b.Position.Z.Raw).ThenBy(b => b.Id).ToArray();
            if (outposts.Length != 2) return 0;
            return north ? outposts[0].Id : outposts[1].Id;
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
                names[OutpostId(o, true)] = "the north outpost";
                names[OutpostId(o, false)] = "the south outpost";
            }
            else foreach (var b in outposts) names[b.Id] = "outpost " + b.Id;
            return names;
        }

        private static string RegionName(SimPoint position, FactionObservation o, Dictionary<uint, string> names)
        {
            var objectives = o.Objectives.Where(b => b.Kind == GoalKind.Outpost).ToArray();
            if (objectives.Length == 0) return "unassigned";
            var nearest = objectives.OrderBy(b => DistanceSquared(position, b.Position)).ThenBy(b => b.Id).First();
            return Name(names, nearest.Kind, nearest.Id);
        }

        private static long DistanceSquared(SimPoint a, SimPoint b)
        {
            long dx = (a.X.Raw - b.X.Raw) / 65536, dz = (a.Z.Raw - b.Z.Raw) / 65536;
            return dx * dx + dz * dz;
        }

        private static string ArmyState(long nearest) => nearest < 0 ? "unknown" : nearest <= 40 ? "contact" : "clear";
        private static string Name(Dictionary<uint, string> names, GoalKind kind, uint id) =>
            kind == GoalKind.Outpost ? (names.TryGetValue(id, out var name) ? name : "outpost " + id) :
            kind == GoalKind.Core ? "a core" : "nothing in particular";
        private static string Owner(KnownObjective b, uint me)
        {
            if (!b.IsOwnerKnown) return "unknown";
            if (b.OwnerFactionId == me) return "me";
            return b.OwnerFactionId == 0 ? "nobody" : "the enemy";
        }
        private static string Bool(bool value) => value ? "true" : "false";

        /// <summary>
        /// Whole metres to the closest sighting, or -1 when nothing has been seen. Public because a question about a
        /// distance can only be answered by reading it, and the truth it is scored against has to be the same number:
        /// measured, the two facts that could be read straight off the state were right 214 times out of 214, while
        /// this one, which had to be worked out from coordinates, averaged 0.63 when it was true.
        /// </summary>
        public static long NearestEnemyMetres(SimPoint from, IReadOnlyList<EnemyContact> contacts)
        {
            long best = -1;
            foreach (var c in contacts)
            {
                // Whole metres are enough for a question, and squaring metres cannot overflow on a 256 m map.
                long dx = (from.X.Raw - c.LastPosition.X.Raw) / 65536;
                long dz = (from.Z.Raw - c.LastPosition.Z.Raw) / 65536;
                long metres = (long)Math.Sqrt(dx * dx + dz * dz);
                if (best < 0 || metres < best) best = metres;
            }
            return best;
        }

        // Fix64 raw / 65536 = meters. Written with a fixed culture so a Japanese-locale PC prints the same text.
        private static string M(Fix64 v) => (v.Raw / 65536.0).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
