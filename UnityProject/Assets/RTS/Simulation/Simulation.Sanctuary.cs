using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-18 #2: the sanctuary civilisation's outpost targeting, its two shrine researches (pilgrimage and holy relic),
    /// and the pilgrimage heal. Everything here is inert unless the faction is in the sanctuary civilisation.
    /// </summary>
    public sealed partial class Simulation
    {
        private static readonly TechKind[] SanctuaryResearchOrder = { SanctuaryTech.Pilgrimage, SanctuaryTech.HolyRelic };

        /// <summary>A completed own shrine whose outpost this faction still owns: that outpost is a sanctuary.</summary>
        private bool ActiveSanctuaryShrine(uint faction, BuildingState shrine)
            => shrine.Alive && shrine.Complete && shrine.FactionId == faction && shrine.Kind == BuildingKind.Shrine
                && shrine.SanctuaryOutpostId != 0 && shrine.SanctuaryOutpostId <= world.Outposts.Length
                && world.Outposts[shrine.SanctuaryOutpostId - 1].OwnerFactionId == faction;

        private int ActiveSanctuaryCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (ActiveSanctuaryShrine(faction, world.Buildings[i])) count++;
            return count;
        }

        /// <summary>Per-sanctuary attack bonus and its cap; the holy relic raises both.</summary>
        private (int perSanctuary, int cap) SanctuaryBonusRules(uint faction)
        {
            var rules = world.Config.Economy;
            return HasTech(faction, SanctuaryTech.HolyRelic)
                ? (rules.SanctuaryRelicBonusPermille, rules.SanctuaryRelicMaxBonusPermille)
                : (rules.SanctuaryAttackBonusPermille, rules.SanctuaryMaxBonusPermille);
        }

        /// <summary>
        /// The sanctuary civilisation is focused on outposts only until it holds its first one: while it owns no outpost
        /// and has observed (owner known) at least one that is not its own. Once it has a foothold it falls back to the
        /// common behaviour, so an outpost it cannot take never keeps it from the enemy core (no stalemate).
        /// </summary>
        private bool SanctuaryOutpostFocus(uint faction, FactionObservation observation)
        {
            if (!SanctuaryAllowed(faction)) return false;
            bool observedOther = false;
            foreach (var objective in observation.Objectives)
                if (objective.Kind == GoalKind.Outpost && objective.IsOwnerKnown)
                {
                    if (objective.OwnerFactionId == faction) return false;
                    observedOther = true;
                }
            return observedOther;
        }

        /// <summary>While focused on outposts, the enemy core is not a candidate (its route is reported unreachable).</summary>
        private bool SanctuarySkipsGoal(uint faction, bool focus, GoalKind kind, uint id)
            => focus && kind == GoalKind.Core && id != world.Factions[faction - 1].CoreId;

        /// <summary>
        /// Decision-side candidate list for the sanctuary civilisation. The common allocation and offense policies are
        /// unchanged: while focused they receive an observation whose outposts are only those this faction has actually
        /// observed (owner known), so the existing ordering - fewest visible enemies along the route, then the shortest
        /// route - picks the easiest observed outpost and an outpost it has never seen is not a target.
        /// Any other faction (or an unfocused sanctuary) receives the same instance.
        /// </summary>
        private FactionObservation SanctuaryTargetObservation(uint faction, FactionObservation observation)
        {
            if (!SanctuaryOutpostFocus(faction, observation)) return observation;
            bool unobserved = false;
            foreach (var objective in observation.Objectives)
                if (objective.Kind == GoalKind.Outpost && !objective.IsOwnerKnown) unobserved = true;
            if (!unobserved) return observation;
            var kept = new List<KnownObjective>(observation.Objectives.Count);
            foreach (var objective in observation.Objectives)
                if (objective.Kind != GoalKind.Outpost || objective.IsOwnerKnown) kept.Add(objective);
            return new FactionObservation(observation.FactionId, observation.Tick, observation.OwnArmies, observation.VisibleEnemies,
                observation.Contacts, kept, observation.EnemyFactionCap, observation.RaidTargets);
        }

        /// <summary>
        /// V3-18 #3: the shrine (the sanctuary's foundation, see FoundationNeedsStone) can only stand by an owned
        /// outpost, so stone for it is wanted only while the faction owns one (measured before: without starting stone,
        /// an owned outpost and no shrine for 20000 ticks).
        /// </summary>
        private bool SanctuaryFoundationReady(uint faction)
        {
            for (int i = 0; i < world.Outposts.Length; i++)
                if (world.Outposts[i].OwnerFactionId == faction) return true;
            return false;
        }

        /// <summary>AI phase: the sanctuary civilisation researches its own techs at an idle completed shrine.</summary>
        private bool DecideSanctuaryResearch(uint faction)
        {
            if (!SanctuaryAllowed(faction)) return false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var shrine = ref world.Buildings[i];
                if (!shrine.Alive || shrine.FactionId != faction || shrine.Kind != BuildingKind.Shrine
                    || !shrine.Complete || shrine.Held || shrine.Researching != 0) continue;
                foreach (var tech in SanctuaryResearchOrder)
                    if (TechOpen(faction, tech) && !BeingResearched(faction, tech))
                        return StartResearch(faction, ref shrine, tech, false);
                return false;
            }
            return false;
        }

        /// <summary>Economy step: every pilgrimage interval, heal researched sanctuary soldiers near an own sanctuary.</summary>
        private void AdvanceSanctuaryPilgrimage()
        {
            if (!SanctuaryOn) return;
            int interval = world.Config.Economy.SanctuaryPilgrimageIntervalTicks;
            if (interval <= 0 || world.Tick % interval != 0) return;
            SanctuaryPilgrimagePulse();
        }

        private void SanctuaryPilgrimagePulse()
        {
            var rules = world.Config.Economy;
            if (rules.SanctuaryPilgrimageHeal <= 0) return;
            var radius = Fix64.FromInt(rules.SanctuaryPilgrimageRadius);
            for (uint faction = 1; faction <= 2; faction++)
            {
                if (!SanctuaryAllowed(faction) || !HasTech(faction, SanctuaryTech.Pilgrimage)) continue;
                var sites = new List<SimPoint>();
                for (int i = 0; i < world.BuildingCount; i++)
                    if (ActiveSanctuaryShrine(faction, world.Buildings[i]))
                        sites.Add(world.Outposts[world.Buildings[i].SanctuaryOutpostId - 1].Definition.Position);
                if (sites.Count == 0) continue;
                foreach (int index in world.SoldierTraversal)
                {
                    ref var soldier = ref world.Soldiers[index];
                    if (!soldier.Alive || soldier.Hp <= 0 || soldier.Initial.FactionId != faction || soldier.Hp >= soldier.Parameters.Hp) continue;
                    bool near = false;
                    foreach (var site in sites)
                        if (InRange(soldier.Position, site, radius)) { near = true; break; }
                    if (!near) continue;
                    soldier.Hp = Math.Min(soldier.Parameters.Hp, checked(soldier.Hp + rules.SanctuaryPilgrimageHeal));
                }
            }
        }
    }
}
