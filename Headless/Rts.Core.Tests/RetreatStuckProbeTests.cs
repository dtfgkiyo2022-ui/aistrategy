using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// Issue #110 probe: finds armies that stay in the retreat for a long time and writes, soldier by soldier, why the
    /// army is not "home" yet (every live soldier within 4 m of the core centre). Investigation only; it asserts nothing.
    /// </summary>
    public sealed class RetreatStuckProbeTests
    {
        [Test, Explicit("investigation"), Category("Measurement")]
        public void WriteLongRetreatsSoldierBySoldier()
        {
            ulong seed = ulong.Parse(Environment.GetEnvironmentVariable("PROBE_SEED") ?? "1", CultureInfo.InvariantCulture);
            var s = MapGenerator.GenerateTerrain(seed);
            var sim = new Battle(s);
            var since = new Dictionary<uint, long>();
            var dumped = new HashSet<string>();
            var report = new StringBuilder();
            report.AppendLine($"seed {seed}");
            for (long tick = 1; tick <= 24000 && !sim.Capture(1).Result.HasEnded; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                if (tick % 100 != 0) continue;
                var f = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
                uint armies = uint.Parse(f["Armies.Count"], CultureInfo.InvariantCulture);
                for (uint a = 1; a <= armies; a++)
                {
                    string ai = "Ai.Armies[" + a + "].";
                    if (!f.TryGetValue(ai + "Returning", out var r) || r != "1") { since.Remove(a); continue; }
                    if (!since.ContainsKey(a)) since[a] = tick;
                    long length = tick - since[a];
                    foreach (long mark in new long[] { 1000, 3000, 6000 })
                    {
                        if (length < mark || !dumped.Add(a + "@" + mark)) continue;
                        report.AppendLine(Dump(f, s, a, tick, length));
                    }
                }
            }
            string dir = @"D:\rts-verify\110";
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"probe_seed{seed}.txt"), report.ToString(), new UTF8Encoding(false));
            }
            catch (UnauthorizedAccessException)
            {
                string fallback = Path.Combine(Path.GetTempPath(), "rts-verify-110");
                Directory.CreateDirectory(fallback);
                File.WriteAllText(Path.Combine(fallback, $"probe_seed{seed}.txt"), report.ToString(), new UTF8Encoding(false));
            }
            TestContext.WriteLine(report.ToString());
        }

        private static double M(string raw) => long.Parse(raw, CultureInfo.InvariantCulture) / 65536.0;

        private static string Dump(Dictionary<string, string> f, ScenarioDefinition s, uint army, long tick, long length)
        {
            string n = "Armies[" + army + "].";
            int count = int.Parse(f[n + "SoldierIds.Count"], CultureInfo.InvariantCulture);
            var b = new StringBuilder();
            b.AppendLine($"--- tick {tick}: army {army} retreating for {length} ticks; PathImpossible={f[n + "PathImpossible"]} Path.Count={f[n + "Path.Count"]} PathCursor={f[n + "PathCursor"]}");
            int live = 0, far = 0;
            for (int i = 0; i < count; i++)
            {
                string id = f[n + "SoldierIds[" + i + "]"];
                string p = "Soldiers[" + id + "].";
                if (f[p + "Alive"] != "1") continue;
                live++;
                uint faction = uint.Parse(f[p + "FactionId"], CultureInfo.InvariantCulture);
                var core = s.Cores.First(c => c.FactionId == faction).Position;
                double x = M(f[p + "Position.X.Raw"]), z = M(f[p + "Position.Z.Raw"]);
                double gx = M(f[p + "MoveGoal.X.Raw"]), gz = M(f[p + "MoveGoal.Z.Raw"]);
                double home = Math.Sqrt(Math.Pow(x - core.X.Raw / 65536.0, 2) + Math.Pow(z - core.Z.Raw / 65536.0, 2));
                if (home > 4) far++;
                b.AppendLine($"  soldier {id} kind {f[p + "Kind"]} faction {faction} home {home:0.0} m at ({x:0.0},{z:0.0}) goal ({gx:0.0},{gz:0.0}) moving {f[p + "IsMoving"]} retreating {f[p + "IsRetreating"]} target {f[p + "TargetKind"]}");
            }
            b.AppendLine($"  live {live}, outside 4 m of home {far}");
            return b.ToString();
        }
    }
}
