using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;

namespace Rts.Headless.Cli;

internal static class BenchmarkCommand
{
    internal static int Run(Dictionary<string, string> options, BuildIdentity build)
    {
        string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Missing " + key);
        long ticks = long.Parse(Required("--ticks"), CultureInfo.InvariantCulture);
        int warmup = int.Parse(options.GetValueOrDefault("--warmup") ?? "200", CultureInfo.InvariantCulture);
        var scenario = JsonInput.Scenario(Required("--scenario"));
        if (ticks <= 0 || ticks > scenario.VerificationTickLimit || warmup < 0 || warmup > scenario.VerificationTickLimit)
            throw new InvalidDataException("Ticks must be positive and warmup nonnegative, within the scenario limit.");
        var inputs = options.TryGetValue("--inputs", out var inputPath)
            ? JsonSerializer.Deserialize<ScheduledInput[]>(File.ReadAllText(inputPath), JsonInput.Options) ?? throw new InvalidDataException("Null inputs.")
            : Array.Empty<ScheduledInput>();
        // Reject overlapping paths before opening any output.
        var paths = new[] { options.GetValueOrDefault("--scenario"), inputPath, options.GetValueOrDefault("--out"), options.GetValueOrDefault("--record") }
            .Where(p => p != null).Select(Path.GetFullPath).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new InvalidDataException("Scenario, inputs, report and replay paths must differ.");

        // Separate instance: measurement always starts at S0 and includes the first N ticks.
        ReplayRunner.Benchmark(null, scenario, inputs.Where(i => i.AcceptedTick < warmup), warmup, build, null);
        // --alloc-types: sample allocations by type (runtime AllocationTick events, about one per 100 KB).
        var typeListener = options.ContainsKey("--alloc-types") ? new AllocTypeListener() : null;
        var samples = new Collector();
        var elapsed = Stopwatch.StartNew();
        ReplayOutcome outcome;
        string record = options.GetValueOrDefault("--record");
        using (var output = record != null ? File.Create(record) : null)
            outcome = ReplayRunner.Benchmark(output, scenario, inputs, ticks, build, samples.Phase);
        typeListener?.Dispose();
        elapsed.Stop(); // Includes initialization, S0, header, End and final stream flush/close.
        var assembly = typeof(BenchmarkCommand).Assembly;
        string configuration = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
        var report = new
        {
            Scenario = scenario.ScenarioId, ScenarioPath = Path.GetFullPath(Required("--scenario")),
            Configuration = configuration, Recording = record != null, ReplayPath = record,
            RequestedTicks = ticks, MeasuredTicks = outcome.LastTick, WarmupTicks = warmup,
            InitialSoldiers = scenario.Soldiers.Length, FactionCap = scenario.Rules.FactionCap,
            outcome.IsFault, WallTotalMs = elapsed.Elapsed.TotalMilliseconds,
            Compute = Stats.From(samples.Compute), ReplayIO = Stats.From(samples.IO), TickWithIO = Stats.From(samples.Total),
            Phases = samples.Stages.ToDictionary(p => p.Key, p => Stats.From(p.Value)),
            AllocTypesKB = typeListener?.Top(25),
            AllocKBPerTick = samples.AllocBytes.ToDictionary(p => p.Key, p => Math.Round(p.Value / 1024.0 / Math.Max(1, samples.Total.Count), 2)),
            PercentileMethod = "nearest-rank ceil(p*N); S0 and independent warmup excluded",
            Timing = "Compute = Step + canonical state and state/event hashes; replay serialization/writes excluded. Phases exclusive; Pathfinding deducted from caller. WallTotal includes setup and final flush. Buffered file I/O; no per-tick fsync.",
            Build = build
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
        if (options.TryGetValue("--out", out var reportPath)) File.WriteAllText(reportPath, json + Environment.NewLine);
        Console.WriteLine(json);
        return outcome.IsFault ? 4 : 0;
    }

    private sealed class Stats
    {
        public int Count { get; init; }
        public double TotalMs { get; init; }
        public double MeanMs { get; init; }
        public double P50Ms { get; init; }
        public double P95Ms { get; init; }
        public double P99Ms { get; init; }
        public double MaxMs { get; init; }
        internal static Stats From(List<double> values)
        {
            var sorted = values.Order().ToArray();
            if (sorted.Length == 0) return new Stats();
            double Percentile(double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];
            double sum = values.Sum();
            return new Stats { Count = values.Count, TotalMs = sum, MeanMs = sum / values.Count,
                P50Ms = Percentile(.50), P95Ms = Percentile(.95), P99Ms = Percentile(.99), MaxMs = sorted[^1] };
        }
    }

    private sealed class Collector
    {
        internal readonly List<double> Compute = new(), IO = new(), Total = new();
        internal readonly Dictionary<string, List<double>> Stages = new();
        private readonly Dictionary<string, long> current = new();
        private readonly Stack<(string Name, long Start, long Children, long AllocStart, long AllocChildren)> stack = new();
        // Bytes allocated by this thread inside each stage (its own, children excluded), summed over all measured ticks.
        internal readonly Dictionary<string, long> AllocBytes = new();
        private long tickStart;
        private bool active;
        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        internal void Phase(string name, bool start)
        {
            long now = Stopwatch.GetTimestamp();
            if (name == "Tick")
            {
                if (start) { active = true; tickStart = now; current.Clear(); stack.Clear(); return; }
                if (stack.Count != 0) throw new InvalidOperationException("Unbalanced phase scopes.");
                double total = Ms(now - tickStart), io = Ms(current.GetValueOrDefault("ReplayIO"));
                Total.Add(total); IO.Add(io); Compute.Add(total - io);
                foreach (var stage in current.Keys.Where(k => k != "ReplayIO"))
                    if (!Stages.ContainsKey(stage)) Stages.Add(stage, Enumerable.Repeat(0.0, Total.Count - 1).ToList());
                foreach (var stage in Stages) stage.Value.Add(Ms(current.GetValueOrDefault(stage.Key)));
                active = false;
                return;
            }
            if (!active) return;
            long allocNow = GC.GetAllocatedBytesForCurrentThread();
            if (start) { stack.Push((name, now, 0, allocNow, 0)); return; }
            var scope = stack.Pop();
            if (scope.Name != name) throw new InvalidOperationException("Mismatched phase scopes.");
            long duration = now - scope.Start;
            long allocated = allocNow - scope.AllocStart;
            current[name] = current.GetValueOrDefault(name) + duration - scope.Children;
            AllocBytes[name] = AllocBytes.GetValueOrDefault(name) + allocated - scope.AllocChildren;
            if (stack.Count != 0)
            {
                var parent = stack.Pop();
                stack.Push((parent.Name, parent.Start, parent.Children + duration, parent.AllocStart, parent.AllocChildren + allocated));
            }
        }
    }
}

// Aggregates the runtime's sampled allocation events by type name. Diagnostic only; it never touches the simulation.
internal sealed class AllocTypeListener : System.Diagnostics.Tracing.EventListener
{
    private readonly Dictionary<string, long> bytes = new();
    private readonly object gate = new();
    protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
    }
    protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
    {
        if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal) || e.Payload == null) return;
        string type = null; long amount = 0;
        for (int i = 0; i < e.PayloadNames!.Count; i++)
        {
            if (e.PayloadNames[i] == "TypeName") type = e.Payload[i] as string;
            else if (e.PayloadNames[i] == "AllocationAmount64") amount = Convert.ToInt64(e.Payload[i], CultureInfo.InvariantCulture);
        }
        if (type == null) return;
        lock (gate) bytes[type] = bytes.GetValueOrDefault(type) + amount;
    }
    internal Dictionary<string, long> Top(int count)
    {
        lock (gate) return bytes.OrderByDescending(p => p.Value).Take(count).ToDictionary(p => p.Key, p => p.Value / 1024);
    }
}

