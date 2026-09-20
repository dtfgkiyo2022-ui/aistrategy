using System.Globalization;
using System.Text;
using Rts.Contracts;

namespace Rts.Headless.Cli;

/// <summary>
/// Issue 4-5 / 5-x diagnosis: writes a sampled time series of the same diagnostic fields the indicators aggregate.
/// The aggregates say a match stalled; this says when each army stopped being available for an offense.
/// Sampling never changes the replay: rows are dropped, not the ticks themselves.
/// </summary>
internal sealed class IndicatorTrace
{
    private readonly long every;
    private readonly List<string> lines = new();
    private uint[] factions, cores, outposts, armies;
    private string lastRow;
    private bool lastRowKept;

    internal IndicatorTrace(long every)
    {
        if (every <= 0) throw new InvalidDataException("--trace-every must be positive.");
        this.every = every;
    }

    private static long N(Dictionary<string, string> d, string key) => long.Parse(d[key], CultureInfo.InvariantCulture);
    private static uint[] Entities(Dictionary<string, string> d, string prefix) =>
        d.Keys.Where(k => k.StartsWith(prefix + "[", StringComparison.Ordinal) && k.EndsWith("].Id", StringComparison.Ordinal))
            .Select(k => checked((uint)N(d, k))).OrderBy(x => x).ToArray();

    internal void Observe(long tick, Dictionary<string, string> d, bool ended)
    {
        if (factions == null)
        {
            factions = Entities(d, "Factions");
            cores = Entities(d, "Cores");
            outposts = Entities(d, "Outposts");
            armies = Entities(d, "Armies");
            var header = new List<string> { "Tick" };
            foreach (uint f in factions) header.Add("Alive." + f);
            foreach (uint c in cores) header.Add("CoreHp." + c);
            foreach (uint o in outposts) header.Add("Outpost." + o + ".Owner");
            foreach (uint f in factions)
            {
                header.Add("Offense." + f + ".Phase");
                header.Add("Offense." + f + ".Goal");
                header.Add("Offense." + f + ".Advancing");
                header.Add("Offense." + f + ".Shortfall");
            }
            foreach (uint a in armies) { header.Add("Army." + a + ".Assignment"); header.Add("Army." + a + ".Alive"); }
            lines.Add(string.Join(",", header));
        }
        var soldiers = Entities(d, "Soldiers");
        var row = new List<string> { tick.ToString(CultureInfo.InvariantCulture) };
        foreach (uint f in factions) row.Add(d["Factions[" + f + "].AliveCount"]);
        foreach (uint c in cores) row.Add(d["Cores[" + c + "].Hp"]);
        foreach (uint o in outposts) row.Add(d["Outposts[" + o + "].OwnerFactionId"]);
        foreach (uint f in factions)
        {
            string p = "Ai.Factions[" + f + "].Offense.";
            row.Add(((OffensivePhase)N(d, p + "Phase")).ToString());
            row.Add(((GoalKind)N(d, p + "Goal.Kind")) + ":" + N(d, p + "Goal.Id"));
            row.Add(N(d, p + "Advancing.Count").ToString(CultureInfo.InvariantCulture));
            row.Add(d["Ai.Factions[" + f + "].ReserveShortfall"]);
        }
        foreach (uint a in armies)
        {
            row.Add(((AssignmentKind)N(d, "Ai.Armies[" + a + "].Assignment")).ToString());
            row.Add(soldiers.Count(s => N(d, "Soldiers[" + s + "].ArmyId") == a && N(d, "Soldiers[" + s + "].Alive") != 0)
                .ToString(CultureInfo.InvariantCulture));
        }
        lastRow = string.Join(",", row);
        // Every tick builds a row, but only samples and the decided tick are kept; Write adds the final tick.
        lastRowKept = tick % every == 0 || ended;
        if (lastRowKept) lines.Add(lastRow);
    }

    internal void Write(string path)
    {
        if (lastRow != null && !lastRowKept) lines.Add(lastRow);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
    }
}
