using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>One-seed-at-a-time measurement for the push-wave investigation.</summary>
    public sealed class PushWavesMeasurementTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const long MaxTicks = 30000;
        private static readonly long[] Checkpoints = { 9000, 12000, 15000, 18000 };

        [Test, Explicit("investigation"), Category("Measurement")]
        public void MeasurePushWaves()
        {
            string seedText = Environment.GetEnvironmentVariable("PUSH_WAVES_SEEDS") ?? "1,2,3,4,5,6,7,8";
            string output = Environment.GetEnvironmentVariable("PUSH_WAVES_DIR") ?? Path.Combine(FindRoot(), "waves3");
            bool force = Environment.GetEnvironmentVariable("PUSH_WAVES_FORCE") == "1";
            Directory.CreateDirectory(output);
            foreach (ulong seed in seedText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => ulong.Parse(value.Trim(), CultureInfo.InvariantCulture)))
            {
                string path = Path.Combine(output, "push-waves-seed-" + seed.ToString(CultureInfo.InvariantCulture) + ".txt");
                if (File.Exists(path) && !force) continue;
                var result = MeasureSeed(seed);
                File.WriteAllText(path, result.Text, new UTF8Encoding(false));
                TestContext.WriteLine(result.Text);
            }
            string summaryPath = Path.Combine(output, "push-waves-summary.csv");
            var summary = new StringBuilder("seed,ended,winner,endTick,deathsF1,deathsF2,assaultMedian");
            foreach (long checkpoint in Checkpoints) summary.Append(",f1_" + checkpoint + ",f2_" + checkpoint);
            summary.AppendLine();
            foreach (ulong seed in seedText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => ulong.Parse(value.Trim(), CultureInfo.InvariantCulture)))
            {
                string path = Path.Combine(output, "push-waves-seed-" + seed.ToString(CultureInfo.InvariantCulture) + ".txt");
                if (File.Exists(path)) summary.AppendLine(SummaryLine(File.ReadAllText(path), seed));
            }
            File.WriteAllText(summaryPath, summary.ToString(), new UTF8Encoding(false));
        }

        private static SeedResult MeasureSeed(ulong seed)
        {
            var scenario = LiveGameRules.Create(seed, false, true, false, false);
            if (int.TryParse(Environment.GetEnvironmentVariable("PUSH_WAVES_SIZE"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var waveSize)) scenario.Economy.WaveSize = waveSize;
            var sim = new Battle(scenario);
            object world = typeof(Battle).GetField("world", Private).GetValue(sim);
            var previousAlive = AliveById(world);
            var deaths = new int[2];
            var standing = Checkpoints.ToDictionary(tick => tick, _ => new int[2]);
            var waveLines = new List<string>();
            var assaultCounts = new List<int>();
            var soldierAttacks = new long[2];
            var coreAttacks = new long[2];
            while (sim.Capture(1).Tick < MaxTicks && !sim.Capture(1).Result.HasEnded)
            {
                long tick = sim.Capture(1).Tick + 1;
                sim.Step(tick, Array.Empty<ScheduledInput>());
                ObserveDeaths(world, previousAlive, deaths);
                for (int f = 0; f < 2; f++)
                {
                    foreach (var e in sim.Capture((uint)f + 1).Events.Where(e => e.Kind == EventKind.AiReport
                        && e.Reason == ReasonCode.None && e.Value >= 1 && e.Value <= 3))
                    {
                        string phase = e.Value == 1 ? "gathering" : e.Value == 2 ? "assault" : "retreat";
                        waveLines.Add("f" + (f + 1) + " " + phase + " tick=" + e.Tick
                            + (e.Value == 2 ? " count=" + e.CommandId : ""));
                        if (e.Value == 2) assaultCounts.Add(checked((int)e.CommandId));
                    }
                    if (Phase(sim, f) == "Assault") CountAttacks(world, f, soldierAttacks, coreAttacks);
                }
                if (standing.TryGetValue(tick, out var values))
                {
                    values[0] = Standing(world, 1);
                    values[1] = Standing(world, 2);
                }
            }
            var result = sim.Capture(1).Result;
            var text = new StringBuilder();
            text.AppendLine("seed=" + seed + " waveSize=" + scenario.Economy.WaveSize + " ended=" + result.HasEnded + " winner=" + result.WinnerFactionId
                + " endTick=" + sim.Capture(1).Tick);
            text.AppendLine("deaths faction1=" + deaths[0] + " faction2=" + deaths[1]);
            text.AppendLine("assaultCounts=" + string.Join(",", assaultCounts));
            text.AppendLine("attackCounts faction1 soldier=" + soldierAttacks[0] + " core=" + coreAttacks[0]
                + " faction2 soldier=" + soldierAttacks[1] + " core=" + coreAttacks[1]);
            foreach (long checkpoint in Checkpoints)
                text.AppendLine("standing tick=" + checkpoint + " faction1=" + standing[checkpoint][0]
                    + " faction2=" + standing[checkpoint][1]);
            text.AppendLine("waves=" + string.Join("; ", waveLines));
            return new SeedResult(text.ToString());
        }

        private static string SummaryLine(string text, ulong seed)
        {
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string head = lines.First(line => line.StartsWith("seed=", StringComparison.Ordinal));
            string deaths = lines.First(line => line.StartsWith("deaths ", StringComparison.Ordinal));
            string assault = lines.First(line => line.StartsWith("assaultCounts=", StringComparison.Ordinal));
            string[] fields = head.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string Value(string name) => fields.First(field => field.StartsWith(name + "=", StringComparison.Ordinal)).Substring(name.Length + 1);
            string[] deathFields = deaths.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string Death(string name) => deathFields.First(field => field.StartsWith(name + "=", StringComparison.Ordinal)).Substring(name.Length + 1);
            string counts = assault.Substring("assaultCounts=".Length);
            var values = counts.Length == 0 ? Array.Empty<int>() : counts.Split(',').Select(int.Parse).OrderBy(x => x).ToArray();
            string median = values.Length == 0 ? "" : values[(values.Length - 1) / 2].ToString(CultureInfo.InvariantCulture);
            var result = new List<string> { seed.ToString(CultureInfo.InvariantCulture), Value("ended"), Value("winner"), Value("endTick"), Death("faction1"), Death("faction2"), median };
            foreach (long checkpoint in Checkpoints)
            {
                string line = lines.FirstOrDefault(value => value.StartsWith("standing tick=" + checkpoint + " ", StringComparison.Ordinal));
                result.Add(line == null ? "" : Field(line, "faction1"));
                result.Add(line == null ? "" : Field(line, "faction2"));
            }
            return string.Join(",", result);
        }

        private static string Field(string line, string name) => line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .First(value => value.StartsWith(name + "=", StringComparison.Ordinal)).Substring(name.Length + 1);

        private static Dictionary<uint, bool> AliveById(object world)
        {
            var result = new Dictionary<uint, bool>();
            foreach (object soldier in (Array)Field(world, "Soldiers")) result[UInt(soldier, "Initial", "Id")] = Bool(soldier, "Alive");
            return result;
        }

        private static void ObserveDeaths(object world, Dictionary<uint, bool> previous, int[] deaths)
        {
            foreach (object soldier in (Array)Field(world, "Soldiers"))
            {
                bool alive = Bool(soldier, "Alive");
                uint id = UInt(soldier, "Initial", "Id");
                if (previous.TryGetValue(id, out var wasAlive) && wasAlive && !alive)
                    deaths[checked((int)UInt(soldier, "Initial", "FactionId") - 1)]++;
                previous[id] = alive;
            }
        }

        private static int Standing(object world, uint faction)
            => ((Array)Field(world, "Soldiers")).Cast<object>().Count(s => Bool(s, "Alive")
                && UInt(s, "Initial", "FactionId") == faction && (UnitKind)Byte(Field(s, "Initial"), "Kind") != UnitKind.Scout);

        private static void CountAttacks(object world, int faction, long[] soldiers, long[] cores)
        {
            foreach (object soldier in (Array)Field(world, "Soldiers"))
            {
                if (!Bool(soldier, "Alive") || UInt(soldier, "Initial", "FactionId") != (uint)faction + 1 || !Bool(soldier, "IsAttacking")) continue;
                int targetKind = Byte(soldier, "TargetKind");
                if (targetKind == 1) soldiers[faction]++;
                else if (targetKind == 2) cores[faction]++;
            }
        }

        private static string Phase(Battle sim, int faction)
        {
            var states = (Array)typeof(Battle).GetField("pushWaveStates", Private).GetValue(sim);
            return Field(states.GetValue(faction), "Phase").ToString();
        }

        private static object Field(object value, string name) => value.GetType().GetField(name, Private).GetValue(value);
        private static int Int(object value, string name) => (int)Field(value, name);
        private static byte Byte(object value, string name) => (byte)Field(value, name);
        private static bool Bool(object value, string name) => (bool)Field(value, name);
        private static uint UInt(object value, string outer, string inner) => Convert.ToUInt32(Field(Field(value, outer), inner), CultureInfo.InvariantCulture);
        private static string FindRoot()
        {
            for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Headless", "Rts.Headless.slnx"))) return directory.FullName;
            throw new DirectoryNotFoundException("repository root");
        }

        private sealed class SeedResult
        {
            internal readonly string Text;
            internal SeedResult(string text) { Text = text; }
        }
    }
}
