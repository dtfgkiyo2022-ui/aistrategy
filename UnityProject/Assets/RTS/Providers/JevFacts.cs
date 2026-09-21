using System;
using System.Linq;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>
    /// The statements the noul questions make. Each one is worked out here from the same observation the model is sent,
    /// for two reasons: the answers can be scored against the truth instead of trusted, and the order table below can
    /// say plainly which facts it is combining.
    /// <para>
    /// Measured: an action-shaped noul ("should we retreat now") sat near 0.3 whatever happened, while this shape
    /// answered 20 out of 20 correctly, averaging 0.98 when true and 0.04 when false. So a question here must always be
    /// a statement about what is observed, never about what to do.
    /// </para>
    /// <para>
    /// A statement must also name the words the state actually uses: "at least one outpost is not mine" scored 8 out of
    /// 22, because the state also has outposts held by nobody and the wording did not say whether those counted.
    /// </para>
    /// <para>
    /// Every statement here is something this code can work out for itself, so asking buys no information. What it buys
    /// is evidence that the model is reading the state at all, which is the only handle on whether to trust the one
    /// answer that is a judgement rather than a fact: where the match is about to be decided.
    /// </para>
    /// </summary>
    public static class JevFacts
    {
        public const string Outnumbering = "outnumbering";
        public const string EnemyNearMyCore = "enemy_near_my_core";
        public const string OutpostHeldByEnemy = "outpost_held_by_enemy";

        /// <summary>Fixed order, so a report of the answers reads the same way every time.</summary>
        public static readonly string[] All = { Outnumbering, EnemyNearMyCore, OutpostHeldByEnemy };

        /// <summary>How close a sighting has to be to the core to count as near it, in metres.</summary>
        public const int NearCoreMetres = 40;

        public static bool Truth(string name, FactionObservation o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            switch (name)
            {
                case Outnumbering:
                    return o.OwnArmies.Sum(a => a.AliveCount) > o.Contacts.Sum(c => c.EstimateMax < 0 ? c.AssumedStrength : c.EstimateMax);
                case EnemyNearMyCore:
                {
                    var core = o.Objectives.FirstOrDefault(b => b.Kind == GoalKind.Core && b.IsOwnerKnown && b.OwnerFactionId == o.FactionId);
                    if (core.Kind != GoalKind.Core) return false; // this faction cannot see its own core: say nothing
                    // The same number the state prints, so the answer is scored against what the model was shown.
                    long metres = JevState.NearestEnemyMetres(core.Position, o.Contacts);
                    return metres >= 0 && metres <= NearCoreMetres;
                }
                case OutpostHeldByEnemy:
                    // "not mine" was ambiguous: the state also has outposts held by nobody, and the answers to that
                    // wording were right 8 times out of 22. This wording names the exact word the state uses.
                    return o.Objectives.Any(b => b.Kind == GoalKind.Outpost && b.IsOwnerKnown
                        && b.OwnerFactionId != o.FactionId && b.OwnerFactionId != 0);
                default:
                    throw new ArgumentException("Unknown fact " + name + ".", nameof(name));
            }
        }
    }
}
