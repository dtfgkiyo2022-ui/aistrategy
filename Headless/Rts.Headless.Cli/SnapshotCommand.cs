using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Application;
using Rts.Contracts;

namespace Rts.Headless.Cli;

/// <summary>
/// Issue 87: plays a preset-vs-preset match and writes one faction's observation every N ticks as JSON lines.
/// The lines are input for an external AI provider probe (Tools/provider-probe); the simulation is not changed.
/// </summary>
internal static class SnapshotCommand
{
    // Fix64 becomes a plain number of meters. This is for reading by a model or a person only, never for replay.
    private sealed class MetersConverter : JsonConverter<Fix64>
    {
        public override Fix64 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter w, Fix64 value, JsonSerializerOptions options) =>
            w.WriteNumberValue(Math.Round(value.Raw / 65536.0, 2));
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        Converters = { new MetersConverter(), new JsonStringEnumConverter() }
    };

    internal static int Run(Dictionary<string, string> options)
    {
        string Required(string key) => options.TryGetValue(key, out var v) ? v : throw new InvalidDataException("Missing " + key);
        var scenario = JsonInput.Scenario(Required("--scenario"));
        string output = Required("--out");
        long ticks = long.Parse(Required("--ticks"), CultureInfo.InvariantCulture);
        long every = long.Parse(options.GetValueOrDefault("--every") ?? "500", CultureInfo.InvariantCulture);
        uint faction = uint.Parse(options.GetValueOrDefault("--faction") ?? "1", CultureInfo.InvariantCulture);
        if (every <= 0 || (faction != 1 && faction != 2)) throw new InvalidDataException("--every must be positive and --faction 1 or 2.");
        if (ticks < 0 || ticks > scenario.VerificationTickLimit) throw new InvalidDataException("--ticks is outside the scenario limit.");
        string west = options.GetValueOrDefault("--west-preset") ?? "maintain", east = options.GetValueOrDefault("--east-preset") ?? "concentrate";

        var sim = new Simulation.Simulation(scenario);
        var gateway = new CommandGateway(sim);
        var westController = PolicyPresets.CreateController(west, 1, gateway);
        var eastController = PolicyPresets.CreateController(east, 2, gateway);
        westController.Initialize(); eastController.Initialize();

        int count = 0;
        using var file = new StreamWriter(output, false, new System.Text.UTF8Encoding(false));
        for (long i = 0; i < ticks && !sim.Capture(1).Result.HasEnded; i++)
        {
            gateway.Step();
            var westFrame = sim.Capture(1);
            if (westFrame.Result.HasEnded) break;
            westController.Step(westFrame);
            eastController.Step(sim.Capture(2));
            var frame = faction == 1 ? westFrame : sim.Capture(2);
            if (frame.Tick > 0 && frame.Tick % every == 0)
            {
                // The lifecycle phase is by elapsed time so latency can be compared for small and large observations.
                string phase = frame.Tick < ticks / 3 ? "early" : frame.Tick < 2 * ticks / 3 ? "mid" : "late";
                file.WriteLine(JsonSerializer.Serialize(new { tick = frame.Tick, phase, faction, observation = frame.Observation }, Json));
                count++;
            }
        }
        Console.WriteLine("Wrote " + count + " snapshots (faction " + faction + ", every " + every + " ticks) to " + output);
        return 0;
    }
}
