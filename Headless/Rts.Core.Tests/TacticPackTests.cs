using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Rts.Tactics;
using Rts.TacticsJs;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class TacticPackTests
    {
        [Test]
        public void RulebookContainsDefinitionsAndIsDeterministic()
        {
            var scenario = MapGenerator.Generate(4123, true);
            var first = TacticRulebook.Write(scenario);
            var second = TacticRulebook.Write(scenario);
            Assert.That(first.markdown, Is.EqualTo(second.markdown));
            Assert.That(first.json, Is.EqualTo(second.json));
            Assert.That(first.markdown, Does.Contain("rulebookVersion"));
            foreach (var type in new[] { typeof(UnitKind), typeof(BuildingKind), typeof(ResourceKind), typeof(CivKind), typeof(TechKind), typeof(PolicyKind), typeof(GoalKind), typeof(ScopeKind), typeof(EndKind), typeof(EconomyPolicy), typeof(EconomyTargetKind) })
                foreach (Enum value in Enum.GetValues(type)) Assert.That(first.markdown, Does.Contain("`" + value + "`"), type.Name + ":" + value);
            using (JsonDocument.Parse(first.json)) { }
        }

        [Test]
        public void MatchPackHasLogsSnapshotsAndReplay()
        {
            const int ticks = 2400;
            var scenario = MapGenerator.Generate(4123, true);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var host = new TacticHost(1, new SimulationFrames(simulation), gateway, gateway, LoadSample());
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "tactic-pack-" + Guid.NewGuid().ToString("N"));
            var pack = new MatchPackWriter(path, scenario, "defend-then-push", "auto");
            pack.RecordInitial(simulation);
            int calls = 0;
            for (int i = 0; i < ticks && !simulation.Capture(1).Result.HasEnded; i++)
            {
                var result = host.Tick();
                if (result.Called) { calls++; pack.RecordTactic(result, 1, "defend-then-push"); }
                gateway.Step();
                pack.RecordAfterStep(simulation);
            }
            pack.Complete(simulation, gateway.Inputs, ticks);

            foreach (var name in new[] { "pack.json", "summary.json", "replay.rpl", "tactic-log.jsonl", "timeline.jsonl", "snapshots.jsonl", "README.md", "rulebook.md" })
                Assert.That(File.Exists(Path.Combine(path, name)), Is.True, name);
            using (var packJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "pack.json"))))
                Assert.That(packJson.RootElement.GetProperty("packVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(File.ReadLines(Path.Combine(path, "tactic-log.jsonl")).Count(), Is.EqualTo(calls));
            var snapshotTicks = File.ReadLines(Path.Combine(path, "snapshots.jsonl"))
                .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("tick").GetInt64()).ToArray();
            Assert.That(snapshotTicks.All(tick => tick % 600 == 0), Is.True);
            Assert.That(snapshotTicks, Is.EqualTo(new long[] { 0, 600, 1200, 1800, 2400 }));
            var timelineTicks = File.ReadLines(Path.Combine(path, "timeline.jsonl"))
                .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("tick").GetInt64()).ToArray();
            Assert.That(timelineTicks, Is.Ordered.Ascending);
            using (var replay = File.OpenRead(Path.Combine(path, "replay.rpl")))
            {
                var outcome = ReplayRunner.Replay(replay, new BuildIdentity());
                Assert.That(outcome.FirstMismatchTick, Is.Null);
            }
        }

        private static JsTacticRuntime LoadSample()
        {
            for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "TacticSamples", "defend-then-push", "main.js");
                if (File.Exists(path)) return new JsTacticRuntime(File.ReadAllText(path), "defend-then-push");
            }
            throw new FileNotFoundException("TacticSamples/defend-then-push/main.js");
        }

        private sealed class SimulationFrames : IFrameSource
        {
            private readonly Battle simulation;
            internal SimulationFrames(Battle simulation) { this.simulation = simulation; }
            public FactionFrame Latest(uint factionId) => simulation.Capture(factionId);
        }
    }
}
