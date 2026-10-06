using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    [NonParallelizable]
    public sealed class TacticPyodideTests
    {
        private static string Root => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../.."));
        private static string Runtimes => Path.Combine(Root, "UnityProject/Assets/StreamingAssets/TacticRuntimes");
        private static string Sample => Path.Combine(Root, "TacticSamples/numpy-mlp");

        [Test]
        public void NumpyMlpRuns1200TicksAgainstAutoAndReplayMatches()
        {
            var loaded = TacticFolder.Load(Sample, Runtimes);
            Assert.That(loaded.IsSuccess, Is.True, loaded.Error);
            var runtime = (PyodideTacticRuntime)loaded.Runtime;
            var scenario = MapGenerator.Generate(2, true);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            using var host = new TacticHost(1, new Frames(simulation), gateway, gateway, runtime, versions: scope => gateway.FactionVersions(1).Versions(scope));
            var startup = Stopwatch.StartNew();
            host.Start("{\"matchSeed\":2,\"factionId\":1}");
            startup.Stop();
            Assert.That(host.Failures, Is.Empty);
            int calls = 0;
            var timings = new List<double>();
            var liveHashes = new List<byte[]> { ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState) };
            for (int i = 0; i < 1200; i++)
            {
                var watch = Stopwatch.StartNew();
                var result = host.Tick();
                watch.Stop();
                if (result.Called) { calls++; timings.Add(watch.Elapsed.TotalMilliseconds); }
                Assert.That(result.Failure, Is.Null, result.Failure?.Reason);
                Assert.That(result.Commands.Rejected, Is.Empty);
                gateway.Step();
                liveHashes.Add(ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState));
            }
            Assert.That(calls, Is.EqualTo(60));
            Assert.That(host.SentCommandCount, Is.GreaterThan(0));
            Assert.That(host.Failures, Is.Empty);
            using var stream = new MemoryStream();
            var build = new BuildIdentity();
            ReplayRunner.Record(stream, scenario, gateway.Inputs, 1200, build);
            stream.Position = 0;
            var replay = ReplayRunner.Replay(stream, build, (state, hash, events) =>
                Assert.That(hash, Is.EqualTo(liveHashes[(int)state.Tick]), "live/replay tick=" + state.Tick));
            Assert.That(replay.FirstMismatchTick, Is.Null);
            Assert.That(replay.IsFault, Is.False);
            timings.Sort();
            TestContext.WriteLine("Pyodide startup={0:F3}ms (with on_start={1:F3}ms), host call p50={2:F3}ms p95={3:F3}ms; calls={4}, commands={5}",
                runtime.StartupMilliseconds, startup.Elapsed.TotalMilliseconds, timings[timings.Count / 2], timings[(int)Math.Ceiling(timings.Count * .95) - 1], calls, host.SentCommandCount);
            int pid = runtime.ProcessId.Value;
            host.Dispose();
            AssertExited(pid);
        }

        [Test]
        public void PythonAndJsBridgeCannotEscapeDenoPermissions()
        {
            string old = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "rts-test-secret-not-for-child");
            try
            {
                // A NODEFS permission denial can escape the Python Exception handler.
                // Test each attack at the C# boundary, where the host also catches it.
                foreach (string expression in new[] {
                    "open('C:/Windows/win.ini').read()",
                    "os.environ['ANTHROPIC_API_KEY']",
                    "js.Deno.readTextFileSync('C:/Windows/win.ini')",
                    "js.Deno.env.get('ANTHROPIC_API_KEY')",
                    "js.Deno.Command.new('cmd').outputSync()",
                    "open('models/keep.json', 'w')",
                    "js.Deno.writeTextFileSync(js.Deno.args[0] + '/models/keep.json', 'changed')",
                    "js.Deno.dlopen('C:/Windows/System32/kernel32.dll', js.Object.new())"
                })
                {
                    using var fixture = new Fixture("import os, js\ndef on_tick(view):\n    " + expression + "\n    return {'commands': []}\n");
                    fixture.Runtime.Start("{}");
                    var error = Assert.Throws<InvalidOperationException>(() => fixture.Runtime.Tick("{}"), expression);
                    Assert.That(error.Message, Does.Not.Contain("NameError").And.Not.Contain("SyntaxError"));
                    TestContext.WriteLine(expression + ": blocked (" + error.Message.Split('\n')[0] + ")");
                    Assert.That(File.ReadAllText(Path.Combine(fixture.Path, "models/keep.json")), Is.EqualTo("{\"value\":7}"));
                }
                using var fetch = new Fixture(@"
import js
def on_tick(view):
    if view.get('probe'):
        js.globalThis.fetchProbe = 'pending'
        js.fetch('https://example.com').then(js.Function.new(""globalThis.fetchProbe = 'ALLOWED'""), js.Function.new(""globalThis.fetchProbe = 'blocked'""))
    return {'commands': [], 'fetch': str(js.globalThis.fetchProbe)}
");
                fetch.Runtime.Start("{}");
                fetch.Runtime.Tick("{\"probe\":true}");
                string status = "pending";
                var deadline = Stopwatch.StartNew();
                while (status == "pending" && deadline.ElapsedMilliseconds < 1000)
                {
                    using var result = JsonDocument.Parse(fetch.Runtime.Tick("{}"));
                    status = result.RootElement.GetProperty("fetch").GetString();
                }
                Assert.That(status, Is.EqualTo("blocked"));
                TestContext.WriteLine("js.fetch: blocked");
            }
            finally { Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", old); }
        }

        [Test]
        public void MountedJsonModelAndNumpyAreUsable()
        {
            using var fixture = new Fixture("import json, numpy as np\ndef on_tick(view):\n    with open('models/keep.json') as f: model = json.load(f)\n    return {'commands': [], 'model': model['value'], 'numpy': float(np.linalg.norm(np.arange(10.0)))}\n");
            fixture.Runtime.Start("{}");
            using var result = JsonDocument.Parse(fixture.Runtime.Tick("{}"));
            Assert.That(result.RootElement.GetProperty("model").GetInt32(), Is.EqualTo(7));
            Assert.That(result.RootElement.GetProperty("numpy").GetDouble(), Is.GreaterThan(16));
        }

        [TestCase("def on_tick(view):\n    while True: pass\n")]
        [TestCase("def on_start(setup):\n    while True: pass\ndef on_tick(view): return {'commands': []}\n")]
        public void InfiniteLoopDisablesAfterThreeFailuresAndLeavesNoProcess(string source)
        {
            using var fixture = new Fixture(source);
            var simulation = new Battle(MapGenerator.Generate(2, true));
            var gateway = new CommandGateway(simulation);
            using var host = new TacticHost(1, new Frames(simulation), gateway, gateway, fixture.Runtime);
            host.Start("{}");
            int pid = fixture.Runtime.ProcessId.Value;
            for (int i = 0; i < 61; i++) { host.Tick(); gateway.Step(); }
            Assert.That(host.Failures, Has.Count.EqualTo(3));
            Assert.That(host.Failures.All(x => x.Reason.Contains("50ms")), Is.True);
            Assert.That(host.Disabled, Is.True);
            Assert.That(fixture.Runtime.IsStopped, Is.True);
            AssertExited(pid);
            Assert.That(host.Tick().Called, Is.False);
        }

        [Test]
        public void ProcessCrashFallsBackWithoutStoppingTheMatch()
        {
            using var fixture = new Fixture("import js\ndef on_tick(view): js.Deno.exit(9)\n");
            var simulation = new Battle(MapGenerator.Generate(2, true));
            var gateway = new CommandGateway(simulation);
            using var host = new TacticHost(1, new Frames(simulation), gateway, gateway, fixture.Runtime);
            host.Start("{}");
            for (int i = 0; i < 61; i++) { host.Tick(); gateway.Step(); }
            Assert.That(host.Disabled, Is.True);
            Assert.That(host.Failures, Has.Count.EqualTo(3));
            Assert.That(simulation.Capture(1).Tick, Is.EqualTo(61));
            AssertExited(fixture.Runtime.ProcessId.Value);
        }

        [Test]
        public void RandomSeedAndConsoleLimitsAreStableAcrossMatches()
        {
            const string source = "import random, numpy as np\ndef on_tick(view):\n    for i in range(25): print('あ' * 300)\n    return {'commands': [], 'r': random.random(), 'n': float(np.random.random()), 'seed': match_seed}\n";
            string first;
            using (var fixture = new Fixture(source))
            {
                fixture.Runtime.Start("{\"matchSeed\":18446744073709551615}");
                first = fixture.Runtime.Tick("{}");
                var logs = fixture.Runtime.TakeConsoleLines();
                Assert.That(logs, Has.Count.EqualTo(20));
                Assert.That(logs.All(x => x.Length == 200), Is.True);
                Assert.That(fixture.Runtime.TakeConsoleLines(), Is.Empty);
            }
            using (var fixture = new Fixture(source))
            {
                fixture.Runtime.Start("{\"matchSeed\":18446744073709551615}");
                Assert.That(fixture.Runtime.Tick("{}"), Is.EqualTo(first));
                Assert.That(first, Does.Contain("18446744073709551615"));
            }
        }

        [Test]
        public void MissingRuntimesKeepPythonVisibleWithAReason()
        {
            var entries = TacticCatalog.Scan(new[] { Path.Combine(Root, "TacticSamples") }, Path.Combine(Root, "not-present-runtimes"));
            var python = entries.Single(x => x.FolderName == "numpy-mlp");
            Assert.That(python.DisplayName, Does.Contain("numpy"));
            Assert.That(python.IsSelectable, Is.False);
            Assert.That(python.Reason, Does.Contain("deno"));
            Assert.That(entries.Single(x => x.FolderName == "rush").IsSelectable, Is.True);
            var available = TacticCatalog.Scan(new[] { Path.Combine(Root, "TacticSamples") }, Runtimes);
            Assert.That(available.Single(x => x.FolderName == "numpy-mlp").IsSelectable, Is.True);
        }

        [Test]
        public void DisposeImmediatelyAfterStartStopsChild()
        {
            using var fixture = new Fixture("def on_tick(view): return {'commands': []}\n");
            fixture.Runtime.Start("{}");
            int pid = fixture.Runtime.ProcessId.Value;
            fixture.Runtime.Dispose();
            fixture.Runtime.Dispose();
            AssertExited(pid);
        }

        [Test]
        public void LateOutputsAreDiscardedAndSuccessfulCallsResetTimeoutCount()
        {
            using var fixture = new Fixture("import time\ndef on_tick(view):\n    if view.get('slow'):\n        end = time.perf_counter() + 0.070\n        while time.perf_counter() < end: pass\n    return {'commands': [], 'id': view['id']}\n");
            fixture.Runtime.Start("{}");
            for (int i = 0; i < 3; i++)
            {
                var watch = Stopwatch.StartNew();
                Assert.Throws<TimeoutException>(() => fixture.Runtime.Tick("{\"slow\":true,\"id\":1}"));
                Assert.That(watch.ElapsedMilliseconds, Is.LessThan(500));
                using var result = JsonDocument.Parse(fixture.Runtime.Tick("{\"id\":2}"));
                Assert.That(result.RootElement.GetProperty("id").GetInt32(), Is.EqualTo(2));
                Assert.That(fixture.Runtime.IsStopped, Is.False);
            }
        }

        [Test]
        public void StartupTimeoutStopsChildWithinTenSecondBudget()
        {
            using var fixture = new Fixture("def on_tick(view): return {'commands': []}\n");
            string root = Path.Combine(fixture.Path, "runtimes");
            Directory.CreateDirectory(Path.Combine(root, "deno"));
            Directory.CreateDirectory(Path.Combine(root, "pyodide"));
            File.Copy(Path.Combine(Runtimes, "deno/deno.exe"), Path.Combine(root, "deno/deno.exe"));
            File.WriteAllText(Path.Combine(root, "pyodide-host.mjs"), "while (true) {}");
            foreach (string asset in new[] { "pyodide.mjs", "pyodide.asm.mjs", "pyodide.asm.wasm", "python_stdlib.zip", "pyodide-lock.json", PyodideTacticRuntime.NumpyWheel })
                File.WriteAllText(Path.Combine(root, "pyodide", asset), "");
            using var runtime = new PyodideTacticRuntime(fixture.Path, root);
            var watch = Stopwatch.StartNew();
            Assert.Throws<TimeoutException>(() => runtime.Start("{}"));
            Assert.That(watch.Elapsed.TotalSeconds, Is.InRange(9.5, 12));
            Assert.That(runtime.IsStopped, Is.True);
            AssertExited(runtime.ProcessId.Value);
        }

        [Test]
        public void MissingPyodideKeepsPythonVisibleWithAReason()
        {
            using var fixture = new Fixture("def on_tick(view): return {'commands': []}\n");
            string root = Path.Combine(fixture.Path, "runtimes");
            Directory.CreateDirectory(Path.Combine(root, "deno"));
            File.WriteAllText(Path.Combine(root, "deno/deno.exe"), "");
            File.WriteAllText(Path.Combine(root, "pyodide-host.mjs"), "");
            var entries = TacticCatalog.Scan(new[] { Path.Combine(Root, "TacticSamples") }, root);
            var python = entries.Single(x => x.FolderName == "numpy-mlp");
            Assert.That(python.IsSelectable, Is.False);
            Assert.That(python.Reason, Does.Contain("pyodide.mjs"));
        }

        [TestCase("def on_tick(view):\n    deep = []\n    for i in range(80): deep = [deep]\n    return {'commands': [], 'deep': deep}\n")]
        [TestCase("import js\ndef on_tick(view):\n    js.Deno.stdout.writeSync(js.TextEncoder.new().encode('[' * 80 + ']' * 80 + '\\n'))\n    return {'commands': []}\n")]
        public void DeepCommandOrRawProtocolJsonCannotOverflowTheHostStack(string source)
        {
            using var fixture = new Fixture(source);
            fixture.Runtime.Start("{}");
            Assert.Throws<FormatException>(() => fixture.Runtime.Tick("{}"));
        }

        private static void AssertExited(int pid)
        {
            try { using var child = Process.GetProcessById(pid); Assert.That(child.HasExited, Is.True); }
            catch (ArgumentException) { }
        }

        private sealed class Frames : IFrameSource
        {
            private readonly Battle simulation;
            public Frames(Battle simulation) { this.simulation = simulation; }
            public FactionFrame Latest(uint factionId) => simulation.Capture(factionId);
        }

        private sealed class Fixture : IDisposable
        {
            public string Path { get; }
            public PyodideTacticRuntime Runtime { get; }
            public Fixture(string source)
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rts-py-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(System.IO.Path.Combine(Path, "models"));
                File.WriteAllText(System.IO.Path.Combine(Path, "main.py"), source);
                File.WriteAllText(System.IO.Path.Combine(Path, "models/keep.json"), "{\"value\":7}");
                Runtime = new PyodideTacticRuntime(Path, Runtimes);
            }
            public void Dispose() { Runtime.Dispose(); Directory.Delete(Path, true); }
        }
    }
}
