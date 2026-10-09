using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    public sealed class TacticJsTests
    {
        [Test]
        public void SamplesLoadAndReturnCommands()
        {
            foreach (string name in new[] { "rush", "defend-then-push", "adjutant", "steel-economy" })
            {
                var loaded = TacticFolder.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "TacticSamples", name));
                Assert.That(loaded.IsSuccess, Is.True, loaded.Error);
                loaded.Runtime.Start("{\"matchSeed\":7,\"factionId\":1}");
                string output = loaded.Runtime.Tick("{\"version\":1,\"tick\":0,\"factionId\":1,\"ownArmies\":[{\"id\":1,\"kind\":\"Infantry\",\"count\":10,\"controlledBy\":\"None\",\"homeObjective\":{\"kind\":\"Core\",\"id\":1,\"point\":{\"x\":0,\"z\":0}}}],\"visibleEnemies\":[],\"contacts\":[],\"orders\":[],\"objectives\":[{\"kind\":\"Core\",\"id\":1,\"position\":{\"x\":0,\"z\":0},\"ownerKnown\":true,\"ownerFactionId\":1,\"hpKnown\":true,\"hp\":1000,\"lastSeenTick\":0,\"capturingFactionId\":0,\"captureTicks\":0,\"captureDurationTicks\":0}],\"economy\":null,\"regions\":[],\"params\":{\"guardPriority\":90,\"guardLossPermille\":200,\"preferOutpost\":true}}");
                Assert.That(output, Does.Contain("\"version\":1"));
                Assert.That(output, Does.Contain("\"commands\""));
            }
        }

        [Test]
        public void BothSamplesRunAgainstAutoAndReplay()
        {
                foreach (string name in new[] { "rush", "defend-then-push", "adjutant" })
            {
                var loaded = TacticFolder.Load(SamplePath(name));
                Assert.That(loaded.IsSuccess, Is.True, loaded.Error);
                var scenario = MapGenerator.Generate(2, true);
                var simulation = new Battle(scenario);
                var gateway = new CommandGateway(simulation);
                var host = new TacticHost(1, new SimulationFrames(simulation), gateway, gateway, loaded.Runtime);
                host.Start("{\"matchSeed\":" + scenario.Seed + ",\"factionId\":1}");
                int calls = 0, commands = 0;
                for (int i = 0; i < 2400 && !simulation.Capture(1).Result.HasEnded; i++)
                {
                    var result = host.Tick();
                    if (result.Called)
                    {
                        calls++;
                        Assert.That(result.Failure, Is.Null, result.Failure == null ? "" : result.Failure.Reason);
                        commands += result.SentPolicies + result.SentEconomy;
                    }
                    gateway.Step();
                }
                Assert.That(calls, Is.GreaterThan(100));
                Assert.That(commands, Is.GreaterThan(0));
                Assert.That(host.Disabled, Is.False);
                using (var stream = new MemoryStream())
                {
                    var build = new BuildIdentity();
                    ReplayRunner.Record(stream, scenario, gateway.Inputs, simulation.Capture(1).Tick, build);
                    stream.Position = 0;
                    var outcome = ReplayRunner.Replay(stream, build);
                    Assert.That(outcome.FirstMismatchTick, Is.Null);
                    Assert.That(outcome.IsFault, Is.False);
                }
            }
        }

        [Test]
        public void AdjutantLeavesHumanControlledArmyAlone()
        {
            var loaded = TacticFolder.Load(SamplePath("adjutant"));
            Assert.That(loaded.IsSuccess, Is.True, loaded.Error);
            loaded.Runtime.Start("{\"matchSeed\":7,\"factionId\":1,\"params\":{\"guardPriority\":90,\"guardLossPermille\":200,\"preferOutpost\":true}}");
            string view = "{\"version\":1,\"tick\":0,\"factionId\":1," +
                "\"ownArmies\":[" +
                "{\"id\":1,\"kind\":\"Infantry\",\"count\":10,\"controlledBy\":\"Human\",\"homeObjective\":{\"kind\":\"Core\",\"id\":1,\"point\":{\"x\":0,\"z\":0}}}," +
                "{\"id\":2,\"kind\":\"Infantry\",\"count\":10,\"controlledBy\":\"None\",\"homeObjective\":{\"kind\":\"Core\",\"id\":1,\"point\":{\"x\":0,\"z\":0}}}]," +
                "\"orders\":[{\"id\":4,\"source\":\"Human\",\"kind\":\"Focus\",\"target\":{\"kind\":\"Army\",\"id\":1},\"goal\":{\"kind\":\"Core\",\"id\":2,\"point\":{\"x\":0,\"z\":0}},\"status\":\"Executing\"}]," +
                "\"objectives\":[{\"kind\":\"Core\",\"id\":1,\"ownerKnown\":true,\"ownerFactionId\":1}]," +
                "\"economy\":null,\"regions\":[],\"params\":{\"guardPriority\":90,\"guardLossPermille\":200,\"preferOutpost\":true}}";
            // The first call of a fresh engine can exceed the 50ms call budget when the full suite loads the machine
            // (seen once in four runs); the behaviour under test is checked on the warmed second call.
            try { loaded.Runtime.Tick(view); } catch (TimeoutException) { }
            using (var doc = System.Text.Json.JsonDocument.Parse(loaded.Runtime.Tick(view)))
            {
                var commands = doc.RootElement.GetProperty("commands").EnumerateArray().ToArray();
                Assert.That(commands.Any(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetUInt32() == 1), Is.False);
                Assert.That(commands.Any(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetUInt32() == 2), Is.True);
            }
        }

        [Test]
        public void FolderRejectsInvalidMetadataWithAReason()
        {
            string path = CreateFolder("{\"name\":\"bad\",\"author\":\"test\",\"version\":\"1\",\"language\":\"lua\",\"entry\":\"main.js\",\"apiVersion\":99,\"description\":\"bad\"}", "");
            var result = TacticFolder.Load(path);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("apiVersion"));
            Directory.Delete(path, true);
        }

        [Test]
        public void FolderValidatesTacticParams()
        {
            string valid = CreateFolder("{\"name\":\"params\",\"author\":\"test\",\"version\":\"1\",\"language\":\"js\",\"entry\":\"main.js\",\"apiVersion\":1,\"description\":\"test\",\"params\":[{\"name\":\"attackThreshold\",\"label\":\"threshold\",\"type\":\"int\",\"default\":30,\"min\":5,\"max\":100,\"step\":5}]}", "function onTick(view) { return {commands:[]}; }");
            try { Assert.That(TacticFolder.Load(valid).IsSuccess, Is.True); }
            finally { Directory.Delete(valid, true); }
            foreach (string bad in new[]
            {
                "{\"name\":\"params\",\"author\":\"test\",\"version\":\"1\",\"language\":\"js\",\"entry\":\"main.js\",\"apiVersion\":1,\"description\":\"test\",\"params\":[{\"name\":\"bad_name\",\"label\":\"x\",\"type\":\"int\",\"default\":30,\"min\":5,\"max\":100,\"step\":5}]}",
                "{\"name\":\"params\",\"author\":\"test\",\"version\":\"1\",\"language\":\"js\",\"entry\":\"main.js\",\"apiVersion\":1,\"description\":\"test\",\"params\":[{\"name\":\"x\",\"label\":\"x\",\"type\":\"int\",\"default\":101,\"min\":5,\"max\":100,\"step\":5}]}",
                "{\"name\":\"params\",\"author\":\"test\",\"version\":\"1\",\"language\":\"js\",\"entry\":\"main.js\",\"apiVersion\":1,\"description\":\"test\",\"params\":[{\"name\":\"x\",\"label\":\"x\",\"type\":\"int\",\"default\":\"30\",\"min\":5,\"max\":100,\"step\":5}]}"
            })
            {
                string path = CreateFolder(bad, "function onTick(view) { return {commands:[]}; }");
                try { var result = TacticFolder.Load(path); Assert.That(result.IsSuccess, Is.False); Assert.That(result.Error, Is.Not.Empty); }
                finally { Directory.Delete(path, true); }
            }
        }

        [Test]
        public void ParamValuesReachJsAndSetParamAppliesOnNextCall()
        {
            var definition = new TacticParamDefinition("attackThreshold", "threshold", "int", 30, 5, 100, 5);
            var runtime = new JsTacticRuntime("function onStart(s){ console.log('start:' + s.params.attackThreshold); } function onTick(v){ console.log('view:' + v.params.attackThreshold); return {commands:[]}; }", "params", new[] { definition });
            var simulation = new Battle(MapGenerator.Generate(99, true));
            var gateway = new CommandGateway(simulation);
            var host = new TacticHost(1, new SimulationFrames(simulation), gateway, gateway, runtime);
            var first = host.Tick();
            Assert.That(first.ViewJson, Does.Contain("\"attackThreshold\":30"));
            Assert.That(host.SetParam("attackThreshold", 50), Is.True);
            Assert.That(host.SetParam("attackThreshold", 101), Is.False);
            for (int i = 0; i < 20; i++) gateway.Step();
            var second = host.Tick();
            Assert.That(second.ViewJson, Does.Contain("\"attackThreshold\":50"));
            Assert.That(second.ParamChanges, Has.Count.EqualTo(1));
            Assert.That(second.ParamChanges[0].From, Is.EqualTo(30));
            Assert.That(second.ParamChanges[0].To, Is.EqualTo(50));
            Assert.That(second.ConsoleLines.Any(x => x == "view:50"), Is.True);
        }

        [Test]
        public void RandomIsStableAndConsoleIsBounded()
        {
            const string source = "function onTick(view) { for (var i=0;i<25;i++) console.log('x'.repeat(300)); return {value:Math.random(),commands:[]}; }";
            var first = new JsTacticRuntime(source); var second = new JsTacticRuntime(source);
            first.Start("{\"matchSeed\":1234}"); second.Start("{\"matchSeed\":1234}");
            string view = "{}";
            Assert.That(first.Tick(view), Is.EqualTo(second.Tick(view)));
            var lines = first.TakeConsoleLines();
            Assert.That(lines, Has.Count.EqualTo(JsTacticRuntime.ConsoleLineLimit));
            Assert.That(lines.All(x => x.Length <= JsTacticRuntime.ConsoleCharacterLimit), Is.True);
        }

        [Test]
        public void SandboxRejectsForbiddenApisAndBudgetOverruns()
        {
            Assert.That(() => Call("function onTick(v){ require('x'); return {commands:[]}; }"), Throws.Exception);
            Assert.That(() => Call("function onTick(v){ fetch('x'); return {commands:[]}; }"), Throws.Exception);
            Assert.That(() => Call("function onTick(v){ importNamespace('System'); return {commands:[]}; }"), Throws.Exception);
            Assert.That(() => Call("function onTick(v){ System.Console.WriteLine('x'); return {commands:[]}; }"), Throws.Exception);
            Assert.That(() => Call("function onTick(v){ while(true){} return {commands:[]}; }"), Throws.Exception);
            Assert.That(() => Call("function loop(n){ return n === 0 ? 0 : loop(n-1); } function onTick(v){ loop(1000); return {commands:[]}; }"), Throws.Exception);
            var memoryFailure = Assert.Catch(() => Call("function onTick(v){ var a = new Array(10000000).fill('0123456789'); return {commands:[]}; }"));
            TestContext.WriteLine("memory guard: " + memoryFailure.GetType().Name + ": " + memoryFailure.Message);
            Assert.That(memoryFailure.ToString(), Does.Contain("memory").IgnoreCase);
        }

        [Test]
        public void OnStartMayTakeLongerThanATickButATickMayNot()
        {
            // 80ms is over the 50ms of a tick and far below the 1000ms of the first call.
            const string wait = "var t = Date.now(); while (Date.now() - t < 80) {}";
            var runtime = new JsTacticRuntime("function onStart(s){ " + wait + " } function onTick(v){ return {commands:[]}; }", "slow-start");
            Assert.That(() => runtime.Start("{}"), Throws.Nothing);
            Assert.That(runtime.Tick("{}"), Does.Contain("commands"));
            var slowTick = new JsTacticRuntime("function onTick(v){ " + wait + " return {commands:[]}; }", "slow-tick");
            slowTick.Start("{}");
            Assert.That(() => slowTick.Tick("{}"), Throws.Exception.With.Message.Contains("50ms"));
        }

        [Test]
        public void MeasuresJsCallTimeForReport()
        {
            var runtime = new JsTacticRuntime("function onTick(v){ return {commands:[]}; }");
            runtime.Start("{\"matchSeed\":1}");
            var samples = new List<long>();
            for (int i = 0; i < 25; i++)
            {
                var watch = Stopwatch.StartNew(); runtime.Tick("{}"); watch.Stop(); samples.Add(watch.ElapsedTicks);
            }
            samples.Sort();
            double tickMs = 1000.0 / Stopwatch.Frequency;
            TestContext.WriteLine("JS Tick p50={0:0.###}ms p95={1:0.###}ms", samples[samples.Count / 2] * tickMs, samples[(int)Math.Ceiling(samples.Count * 0.95) - 1] * tickMs);
        }

        private static string Call(string source)
        {
            var runtime = new JsTacticRuntime(source); runtime.Start("{}"); return runtime.Tick("{}");
        }

        private static string CreateFolder(string metadata, string source)
        {
            string path = Path.Combine(Path.GetTempPath(), "rts-tactic-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "tactic.json"), metadata);
            File.WriteAllText(Path.Combine(path, "main.js"), source);
            return path;
        }

        [Test]
        public void SteelEconomySampleAsksForALineInThePlayedGame()
        {
            // Seed 11 of the played game's rules gives the west metallurgy; the sample asks for its metal line in a region.
            var loaded = TacticFolder.Load(SamplePath("steel-economy"));
            Assert.That(loaded.IsSuccess, Is.True, loaded.Error);
            var scenario = LiveGameRules.Create(11, false, true, false, false);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var host = new TacticHost(1, new SimulationFrames(simulation), gateway, gateway, loaded.Runtime);
            host.Start("{\"matchSeed\":" + scenario.Seed + ",\"factionId\":1}");
            ScheduledInput request = null;
            for (int i = 0; i < 12000 && request == null && !simulation.Capture(1).Result.HasEnded; i++)
            {
                var result = host.Tick();
                if (result.Called) Assert.That(result.Failure, Is.Null, result.Failure == null ? "" : result.Failure.Reason);
                gateway.Step();
                request = gateway.Inputs.FirstOrDefault(input => input.Economy != null && input.Economy.Kind == EconomyCommandKind.RequestLine);
            }
            Assert.That(request, Is.Not.Null, "the sample never asked for a line");
            Assert.That(request.Economy.Line, Is.EqualTo(ProcessingLineKind.CoreMetal));
            Assert.That(request.Economy.RegionId, Is.GreaterThan(0u));
            for (int i = 0; i < 400; i++) { host.Tick(); gateway.Step(); }
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, simulation.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void EarlierFramesKeepTheirCommandsWhileTheMatchGoesOn()
        {
            // Frames share one growing array of command views (10-10). A frame must keep listing what it listed when it
            // was made, however many commands start or end afterwards.
            var loaded = TacticFolder.Load(SamplePath("guarded-spear"));
            Assert.That(loaded.IsSuccess, Is.True, loaded.Error);
            var scenario = LiveGameRules.Create(11, false, true, false, false);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var host = new TacticHost(1, new SimulationFrames(simulation), gateway, gateway, loaded.Runtime,
                versions: scope => gateway.FactionVersions(1).Versions(scope));
            host.Start("{\"matchSeed\":" + scenario.Seed + ",\"factionId\":1}");
            var kept = new List<(FactionFrame Frame, CommandView[] Commands)>();
            for (int i = 1; i <= 3000 && !simulation.Capture(1).Result.HasEnded; i++)
            {
                host.Tick();
                gateway.Step();
                if (i % 7 == 0) kept.Add((simulation.Capture(1), simulation.Capture(1).Commands.ToArray()));
            }
            Assert.That(kept.Last().Commands.Count(c => c.Status >= CommandStatus.Completed), Is.GreaterThan(0), "no ended command to check");
            Assert.That(kept.Any(k => k.Commands.Any(c => c.Status < CommandStatus.Completed)), Is.True, "no live command to check");
            foreach (var (frame, commands) in kept)
                Assert.That(frame.Commands.ToArray(), Is.EqualTo(commands), "frame of tick " + frame.Tick + " changed afterwards");
        }

        private static string SamplePath(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "TacticSamples", name);

        private sealed class SimulationFrames : IFrameSource
        {
            private readonly Battle simulation;
            internal SimulationFrames(Battle simulation) { this.simulation = simulation; }
            public FactionFrame Latest(uint factionId) => simulation.Capture(factionId);
        }
    }
}
