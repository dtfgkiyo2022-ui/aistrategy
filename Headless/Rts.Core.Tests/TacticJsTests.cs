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
            foreach (string name in new[] { "rush", "defend-then-push" })
            {
                var loaded = TacticFolder.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "TacticSamples", name));
                Assert.That(loaded.IsSuccess, Is.True, loaded.Error);
                loaded.Runtime.Start("{\"matchSeed\":7,\"factionId\":1}");
                string output = loaded.Runtime.Tick("{\"version\":1,\"tick\":0,\"factionId\":1,\"ownArmies\":[{\"id\":1,\"kind\":\"Infantry\",\"count\":10}],\"visibleEnemies\":[],\"contacts\":[],\"objectives\":[{\"kind\":\"Core\",\"id\":1,\"position\":{\"x\":0,\"z\":0},\"ownerKnown\":true,\"ownerFactionId\":1,\"hpKnown\":true,\"hp\":1000,\"lastSeenTick\":0,\"capturingFactionId\":0,\"captureTicks\":0,\"captureDurationTicks\":0}],\"economy\":null,\"regions\":[]}");
                Assert.That(output, Does.Contain("\"version\":1"));
                Assert.That(output, Does.Contain("\"commands\""));
            }
        }

        [Test]
        public void BothSamplesRunAgainstAutoAndReplay()
        {
            foreach (string name in new[] { "rush", "defend-then-push" })
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
        public void FolderRejectsInvalidMetadataWithAReason()
        {
            string path = CreateFolder("{\"name\":\"bad\",\"author\":\"test\",\"version\":\"1\",\"language\":\"lua\",\"entry\":\"main.js\",\"apiVersion\":99,\"description\":\"bad\"}", "");
            var result = TacticFolder.Load(path);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("apiVersion"));
            Directory.Delete(path, true);
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

        private static string SamplePath(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "TacticSamples", name);

        private sealed class SimulationFrames : IFrameSource
        {
            private readonly Battle simulation;
            internal SimulationFrames(Battle simulation) { this.simulation = simulation; }
            public FactionFrame Latest(uint factionId) => simulation.Capture(factionId);
        }
    }
}
