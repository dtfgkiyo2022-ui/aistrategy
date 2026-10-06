using System.Text.Json;
using NUnit.Framework;
using Rts.UnityHost;

namespace Rts.Core.Tests
{
    public sealed class SmokeTests
    {
        [Test]
        public void ParsesSmokeArgumentsAndDefaults()
        {
            var args = new[]
            {
                "Game.exe", "-batchmode", "-rts-smoke",
                "-rts-smoke-west", "guarded-spear",
                "-rts-smoke-east", "numpy-mlp",
                "-rts-smoke-ticks", "120",
                "-rts-smoke-seed", "42",
                "-rts-smoke-out", "result.json"
            };

            Assert.That(SmokeOptions.TryParse(args, out var options, out var error), Is.True, error);
            Assert.That(options.WestTactic, Is.EqualTo("guarded-spear"));
            Assert.That(options.EastTactic, Is.EqualTo("numpy-mlp"));
            Assert.That(options.Ticks, Is.EqualTo(120));
            Assert.That(options.Seed, Is.EqualTo(42UL));
            Assert.That(options.OutputPath, Is.EqualTo("result.json"));

            Assert.That(SmokeOptions.TryParse(new[] { "-rts-smoke" }, out var defaults, out error), Is.True, error);
            Assert.That(defaults.Ticks, Is.EqualTo(SmokeOptions.DefaultTicks));
            Assert.That(defaults.WestTactic, Is.EqualTo("none"));
        }

        [Test]
        public void RejectsMalformedSmokeArguments()
        {
            Assert.That(SmokeOptions.TryParse(new[] { "-rts-smoke", "-rts-smoke-ticks", "0" }, out _, out var error), Is.False);
            Assert.That(error, Does.Contain("1以上"));
        }

        [Test]
        public void BuildsValidResultJsonWithDiagnostics()
        {
            string json = SmokeJson.Write(new SmokeResult
            {
                TicksRequested = 10,
                Tick = 10,
                Seed = 42,
                WinnerFactionId = 1,
                Winner = "west",
                West = new SmokeSideResult
                {
                    RequestedTactic = "guarded-spear",
                    Tactic = "guarded-spear",
                    Language = "js",
                    Loaded = true,
                    Calls = 1,
                    SentCommands = 2,
                    DiscardedCommands = 1,
                    Failures = 0,
                    ConsoleLog = new[] { "hello\"world" },
                    AcceptedAiCommands = 2,
                    ExecutedAiCommands = 1,
                    CallP50Milliseconds = 0.25
                },
                East = new SmokeSideResult { RequestedTactic = "none", Tactic = "none", ConsoleLog = System.Array.Empty<string>() }
            });

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.That(root.GetProperty("mode").GetString(), Is.EqualTo("rts-smoke"));
            Assert.That(root.GetProperty("winner").GetString(), Is.EqualTo("west"));
            Assert.That(root.GetProperty("west").GetProperty("executedAiCommands").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("west").GetProperty("consoleLog")[0].GetString(), Is.EqualTo("hello\"world"));
        }
    }
}
