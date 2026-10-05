using Rts.Simulation;
using Rts.Tactics;

namespace Rts.Headless.Cli;

internal static class TacticRulebookCommand
{
    internal static int Run(Dictionary<string, string> options)
    {
        string output = Required(options, "--out");
        ScenarioDefinition scenario = options.TryGetValue("--map-seed", out var seed)
            ? MapGenerator.Generate(ulong.Parse(seed, System.Globalization.CultureInfo.InvariantCulture), true)
            : WeekOneScenario.Create();
        Directory.CreateDirectory(output);
        var rulebook = TacticRulebook.Write(scenario);
        File.WriteAllText(Path.Combine(output, "rulebook.md"), rulebook.markdown, new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "rulebook.json"), rulebook.json, new System.Text.UTF8Encoding(false));
        Console.WriteLine("tactic-rulebook: " + output);
        return 0;
    }

    private static string Required(Dictionary<string, string> options, string key)
        => options.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Missing " + key);
}
