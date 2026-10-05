using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rts.Contracts;

namespace Rts.Tactics
{
    /// <summary>Writes the version 1, faction-local tactic observation in a canonical order.</summary>
    public static class TacticViewWriter
    {
        public const int Version = 1;

        public static string Write(FactionFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var b = new StringBuilder(2048);
            b.Append("{\"version\":1,\"tick\":").Append(frame.Tick.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"factionId\":").Append(frame.FactionId.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"ownArmies\":[");
            var armies = (frame.Observation?.OwnArmies ?? Array.Empty<OwnArmyView>()).OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < armies.Length; i++)
            {
                if (i != 0) b.Append(',');
                var a = armies[i];
                b.Append("{\"id\":").Append(a.Id).Append(",\"kind\":").Append(TacticJson.Quote(a.Kind.ToString()));
                b.Append(",\"count\":").Append(a.AliveCount);
                b.Append(",\"position\":"); Point(b, a.Position);
                b.Append(",\"homeObjective\":"); Goal(b, a.HomeObjective);
                b.Append('}');
            }
            b.Append("],\"visibleEnemies\":[");
            var enemies = (frame.Observation?.VisibleEnemies ?? Array.Empty<VisibleEnemy>()).OrderBy(x => x.ContactId).ToArray();
            for (int i = 0; i < enemies.Length; i++)
            {
                if (i != 0) b.Append(','); var e = enemies[i];
                b.Append("{\"id\":").Append(e.ContactId).Append(",\"kind\":").Append(e.Kind).Append(",\"position\":"); Point(b, e.Position); b.Append('}');
            }
            b.Append("],\"contacts\":[");
            var contacts = (frame.Observation?.Contacts ?? Array.Empty<EnemyContact>()).OrderBy(x => x.ContactId).ToArray();
            for (int i = 0; i < contacts.Length; i++)
            {
                if (i != 0) b.Append(','); var c = contacts[i];
                b.Append("{\"id\":").Append(c.ContactId).Append(",\"position\":"); Point(b, c.LastPosition);
                b.Append(",\"lastSeenTick\":").Append(c.LastSeenTick).Append(",\"min\":").Append(c.EstimateMin).Append(",\"max\":").Append(c.EstimateMax);
                b.Append(",\"visible\":").Append(c.IsCurrentlyVisible ? "true" : "false").Append(",\"uncertain\":").Append(c.IsUncertain ? "true" : "false");
                b.Append(",\"strengthUnknown\":").Append(c.IsStrengthUnknown ? "true" : "false").Append(",\"absent\":").Append(c.IsAbsentAtLastPosition ? "true" : "false");
                b.Append(",\"covered\":["); var covered = (c.CoveredContactIds ?? Array.Empty<uint>()).OrderBy(x => x).ToArray();
                for (int j = 0; j < covered.Length; j++) { if (j != 0) b.Append(','); b.Append(covered[j]); }
                b.Append("]}");
            }
            b.Append("],\"objectives\":[");
            var objectives = (frame.Observation?.Objectives ?? Array.Empty<KnownObjective>()).OrderBy(x => x.Kind).ThenBy(x => x.Id).ToArray();
            for (int i = 0; i < objectives.Length; i++) { if (i != 0) b.Append(','); Objective(b, objectives[i]); }
            b.Append(']');
            b.Append(",\"economy\":"); Economy(b, frame);
            b.Append(",\"regions\":[");
            var regions = (frame.Regions ?? Array.Empty<RegionView>()).OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < regions.Length; i++) { if (i != 0) b.Append(','); var r = regions[i]; b.Append("{\"id\":").Append(r.Id).Append(",\"centerKind\":").Append(TacticJson.Quote(r.CenterKind.ToString())).Append(",\"centerId\":").Append(r.CenterId).Append(",\"center\":"); Point(b, r.Center); b.Append(",\"control\":").Append(TacticJson.Quote(r.Control.ToString())).Append(",\"policy\":").Append(TacticJson.Quote(r.Policy.ToString())).Append(",\"economyPolicy\":").Append(TacticJson.Quote(r.EconomyPolicy.ToString())).Append('}'); }
            b.Append("]}");
            return b.ToString();
        }

        private static void Objective(StringBuilder b, KnownObjective x)
        {
            b.Append("{\"kind\":").Append(TacticJson.Quote(x.Kind.ToString())).Append(",\"id\":").Append(x.Id).Append(",\"position\":"); Point(b, x.Position);
            b.Append(",\"ownerKnown\":").Append(x.IsOwnerKnown ? "true" : "false").Append(",\"ownerFactionId\":").Append(x.OwnerFactionId).Append(",\"hpKnown\":").Append(x.IsHpKnown ? "true" : "false").Append(",\"hp\":").Append(x.Hp).Append(",\"lastSeenTick\":").Append(x.LastSeenTick);
            b.Append(",\"capturingFactionId\":").Append(x.CapturingFactionId).Append(",\"captureTicks\":").Append(x.CaptureTicks).Append(",\"captureDurationTicks\":").Append(x.CaptureDurationTicks).Append('}');
        }

        private static void Goal(StringBuilder b, PolicyGoal x)
        {
            b.Append("{\"kind\":").Append(TacticJson.Quote(x.Kind.ToString())).Append(",\"id\":").Append(x.Id).Append(",\"point\":"); Point(b, x.Point); b.Append('}');
        }

        private static void Economy(StringBuilder b, FactionFrame frame)
        {
            var e = frame.Economy;
            if (e == null) { b.Append("null"); return; }
            b.Append("{\"food\":").Append(e.Food).Append(",\"wood\":").Append(e.Wood).Append(",\"ore\":").Append(e.Ore).Append(",\"metal\":").Append(e.Metal).Append(",\"stone\":").Append(e.Stone).Append(",\"gold\":").Append(e.Gold).Append(",\"gems\":").Append(e.Gems).Append(",\"population\":").Append(e.Population).Append(",\"populationCap\":").Append(e.PopulationCap).Append(",\"civilisation\":").Append(TacticJson.Quote(e.Civ.ToString())).Append(",\"age\":").Append(e.Age).Append(",\"auto\":").Append(e.AutoEconomy ? "true" : "false").Append(",\"policy\":").Append(TacticJson.Quote(e.Policy.ToString()));
            b.Append(",\"villagers\":["); var villagers = e.Villagers.Where(x => x.IsOwn && x.Id != 0).OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < villagers.Length; i++) { if (i != 0) b.Append(','); var v = villagers[i]; b.Append("{\"id\":").Append(v.Id).Append(",\"position\":"); Point(b, v.Position); b.Append(",\"activity\":").Append(TacticJson.Quote(v.Activity.ToString())).Append(",\"hp\":").Append(v.Hp).Append('}'); }
            b.Append("],\"buildings\":["); var buildings = e.Buildings.Where(x => x.FactionId == frame.FactionId).OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < buildings.Length; i++) { if (i != 0) b.Append(','); var x = buildings[i]; b.Append("{\"id\":").Append(x.Id).Append(",\"kind\":").Append(TacticJson.Quote(x.Kind.ToString())).Append(",\"position\":"); Point(b, x.Center); b.Append(",\"complete\":").Append(x.Complete ? "true" : "false").Append(",\"queued\":").Append(x.Queued).Append(",\"researching\":").Append(TacticJson.Quote(x.Researching.ToString())).Append('}'); }
            b.Append("],\"resources\":["); var resources = e.Resources.OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < resources.Length; i++) { if (i != 0) b.Append(','); var x = resources[i]; b.Append("{\"id\":").Append(x.Id).Append(",\"kind\":").Append(TacticJson.Quote(x.Kind.ToString())).Append(",\"position\":"); Point(b, x.Position); b.Append(",\"remaining\":").Append(x.Remaining).Append('}'); }
            b.Append("]}");
        }

        private static void Point(StringBuilder b, SimPoint p)
        {
            b.Append("{\"x\":").Append(Fix(p.X)).Append(",\"z\":").Append(Fix(p.Z)).Append('}');
        }

        private static string Fix(Fix64 value)
        {
            decimal exact = (decimal)value.Raw / 65536m;
            return decimal.Round(exact, 3, MidpointRounding.ToEven).ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
