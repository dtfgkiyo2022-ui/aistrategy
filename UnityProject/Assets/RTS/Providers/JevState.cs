using System.Globalization;
using System.Text;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>
    /// Writes a FactionObservation as the "state" sent to the model. Only what the faction could observe goes in
    /// (rule 6). Meters and counts are for the model to read; nothing here is used to decide anything in the game.
    /// </summary>
    public static class JevState
    {
        public static string Build(FactionObservation o)
        {
            var sb = new StringBuilder();
            sb.Append("{\"tick\":").Append(o.Tick).Append(",\"faction\":").Append(o.FactionId);
            sb.Append(",\"ownArmies\":[");
            for (int i = 0; i < o.OwnArmies.Count; i++)
            {
                var a = o.OwnArmies[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(a.Id).Append(",\"alive\":").Append(a.AliveCount)
                  .Append(",\"x\":").Append(M(a.Position.X)).Append(",\"z\":").Append(M(a.Position.Z)).Append('}');
            }
            sb.Append("],\"enemyContacts\":[");
            for (int i = 0; i < o.Contacts.Count; i++)
            {
                var c = o.Contacts[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"x\":").Append(M(c.LastPosition.X)).Append(",\"z\":").Append(M(c.LastPosition.Z))
                  .Append(",\"visible\":").Append(c.IsCurrentlyVisible ? "true" : "false")
                  .Append(",\"min\":").Append(c.EstimateMin).Append(",\"max\":").Append(c.EstimateMax).Append('}');
            }
            sb.Append("],\"objectives\":[");
            for (int i = 0; i < o.Objectives.Count; i++)
            {
                var b = o.Objectives[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"kind\":\"").Append(b.Kind).Append("\",\"id\":").Append(b.Id)
                  .Append(",\"owner\":").Append(b.IsOwnerKnown ? b.OwnerFactionId : 0u);
                if (b.IsHpKnown) sb.Append(",\"hp\":").Append(b.Hp);
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // Fix64 raw / 65536 = meters. Written with a fixed culture so a Japanese-locale PC prints the same text.
        private static string M(Fix64 v) => (v.Raw / 65536.0).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
