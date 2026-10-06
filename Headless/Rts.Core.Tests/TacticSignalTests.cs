using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Providers;
using Rts.Simulation;
using Rts.Tactics;
using Rts.TacticsJs;
using Rts.UnityHost;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class TacticSignalTests
    {
        [TestCase("[{\"name\":\"hold-here\",\"label\":\"守れ\",\"needsPoint\":false}]")]
        [TestCase("[{\"name\":\"allIn\",\"label\":\"総攻撃\",\"needsPoint\":false},{\"name\":\"allIn\",\"label\":\"再攻撃\",\"needsPoint\":false}]")]
        [TestCase("[{\"name\":\"s1\",\"label\":\"1\",\"needsPoint\":false},{\"name\":\"s2\",\"label\":\"2\",\"needsPoint\":false},{\"name\":\"s3\",\"label\":\"3\",\"needsPoint\":false},{\"name\":\"s4\",\"label\":\"4\",\"needsPoint\":false},{\"name\":\"s5\",\"label\":\"5\",\"needsPoint\":false},{\"name\":\"s6\",\"label\":\"6\",\"needsPoint\":false},{\"name\":\"s7\",\"label\":\"7\",\"needsPoint\":false},{\"name\":\"s8\",\"label\":\"8\",\"needsPoint\":false},{\"name\":\"s9\",\"label\":\"9\",\"needsPoint\":false}]")]
        [TestCase("[{\"name\":\"allIn\",\"needsPoint\":false}]")]
        [TestCase("[{\"name\":\"allIn\",\"label\":\"総攻撃\",\"needsPoint\":\"false\"}]")]
        public void InvalidSignalMetadataIsRejected(string signals)
        {
            string folder = CreateTacticFolder(signals, "function onTick(view) { return { commands: [] }; }");
            try
            {
                var loaded = TacticFolder.Load(folder);
                Assert.That(loaded.IsSuccess, Is.False);
                Assert.That(loaded.Error, Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                Delete(folder);
            }
        }

        [Test]
        public void SendSignalValidatesQueuesInOrderAndDeliversOnce()
        {
            var definitions = new[]
            {
                new TacticSignalDefinition("allIn", "総攻撃", false),
                new TacticSignalDefinition("holdHere", "ここを守れ", true)
            };
            var simulation = new Battle(MapGenerator.Generate(3101, true));
            var gateway = new CommandGateway(simulation);
            var runtime = new RecordingSignalRuntime(definitions);
            var host = new TacticHost(1, new SimulationFrames(simulation), gateway, gateway, runtime);

            Assert.That(host.SendSignal("missing", null, out var reason), Is.False);
            Assert.That(reason, Does.Contain("見つかりません"));
            Assert.That(host.SendSignal("holdHere", null, out reason), Is.False);
            Assert.That(reason, Does.Contain("地点が必要"));
            Assert.That(host.SendSignal("allIn", new SimPoint(Fix(1), Fix(2)), out reason), Is.False);
            Assert.That(reason, Does.Contain("地点を付けられません"));

            var point = new SimPoint(Fix(12), Fix(34));
            Assert.That(host.SendSignal("allIn", null, out reason), Is.True, reason);
            Assert.That(host.SendSignal("holdHere", point, out reason), Is.True, reason);

            var first = host.Tick();
            Assert.That(first.Called, Is.True);
            Assert.That(first.Signals.Select(x => x.Name), Is.EqualTo(new[] { "allIn", "holdHere" }));
            Assert.That(first.Signals[1].Point, Is.EqualTo(point));
            using (var view = JsonDocument.Parse(runtime.Views[0]))
            {
                var signals = view.RootElement.GetProperty("signals");
                Assert.That(signals.GetArrayLength(), Is.EqualTo(2));
                Assert.That(signals[0].GetProperty("name").GetString(), Is.EqualTo("allIn"));
                Assert.That(signals[1].GetProperty("point").GetProperty("x").GetDecimal(), Is.EqualTo(12m));
            }

            TacticHostTickResult nextCall = null;
            for (int i = 0; i < TacticHost.DecisionIntervalTicks; i++)
            {
                gateway.Step();
                var result = host.Tick();
                if (result.Called) nextCall = result;
            }
            Assert.That(nextCall, Is.Not.Null);
            Assert.That(nextCall.Signals, Is.Empty);

            host.Dispose();
            Assert.That(host.SendSignal("allIn", null, out reason), Is.False);
            Assert.That(reason, Does.Contain("停止"));
        }

        [Test]
        public void CliSignalArrivesAtTheFollowingTacticCallAndBadSyntaxFails()
        {
            string folder = CreateTacticFolder(
                "[{\"name\":\"ping\",\"label\":\"通知\",\"needsPoint\":false}]",
                "function onTick(view) { console.log(String(view.tick) + ':' + view.signals.map(function (s) { return s.name; }).join(',')); return { commands: [] }; }");
            string log = Path.Combine(TestContext.CurrentContext.WorkDirectory, "signal-cli-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                int exit = Rts.Headless.Cli.Program.Main(new[]
                {
                    "tactic-match", "--map-seed", "3102", "--ticks", "21",
                    "--west-tactic", folder, "--east-tactic", "auto",
                    "--west-signal", "20:ping", "--log-out", log
                });
                Assert.That(exit, Is.EqualTo(0));
                var line = File.ReadLines(log).Single(x => x.Contains("\"tick\":20"));
                using (var record = JsonDocument.Parse(line))
                {
                    Assert.That(record.RootElement.GetProperty("signals")[0].GetProperty("name").GetString(), Is.EqualTo("ping"));
                    Assert.That(record.RootElement.GetProperty("console")[0].GetString(), Is.EqualTo("20:ping"));
                }

                int badExit = Rts.Headless.Cli.Program.Main(new[]
                {
                    "tactic-match", "--map-seed", "3102", "--ticks", "1",
                    "--west-tactic", folder, "--east-tactic", "auto", "--west-signal", "not-a-signal"
                });
                Assert.That(badExit, Is.EqualTo(3));
            }
            finally
            {
                Delete(folder);
                Delete(log);
            }
        }

        [Test]
        public void MatchPackKeepsDeliveredSignalWithPoint()
        {
            var scenario = MapGenerator.Generate(3103, true);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var runtime = new RecordingSignalRuntime(new[] { new TacticSignalDefinition("holdHere", "ここを守れ", true) });
            var host = new TacticHost(1, new SimulationFrames(simulation), gateway, gateway, runtime);
            string packPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "signal-pack-" + Guid.NewGuid().ToString("N"));
            var pack = new MatchPackWriter(packPath, scenario, "signal-test", "auto");
            try
            {
                var point = new SimPoint(Fix(8), Fix(9));
                Assert.That(host.SendSignal("holdHere", point, out var reason), Is.True, reason);
                pack.RecordInitial(simulation);
                for (int i = 0; i < 21; i++)
                {
                    pack.RecordTactic(host.Tick(), 1, "signal-test");
                    gateway.Step();
                    pack.RecordAfterStep(simulation);
                }
                pack.Complete(simulation, gateway.Inputs, 21);

                string line = File.ReadLines(Path.Combine(packPath, "tactic-log.jsonl"))
                    .Single(x => x.Contains("\"tick\":0"));
                using (var record = JsonDocument.Parse(line))
                {
                    var signal = record.RootElement.GetProperty("signals")[0];
                    Assert.That(signal.GetProperty("name").GetString(), Is.EqualTo("holdHere"));
                    Assert.That(signal.GetProperty("point").GetProperty("x").GetDecimal(), Is.EqualTo(8m));
                    Assert.That(signal.GetProperty("point").GetProperty("z").GetDecimal(), Is.EqualTo(9m));
                }
            }
            finally
            {
                Delete(packPath);
            }
        }

        [Test]
        public void SameCliSignalInputProducesTheSameCommandLog()
        {
            string folder = CreateTacticFolder(
                "[{\"name\":\"ping\",\"label\":\"通知\",\"needsPoint\":false}]",
                "function onTick(view) { return { commands: [] }; }");
            string first = Path.Combine(TestContext.CurrentContext.WorkDirectory, "signal-determinism-1-" + Guid.NewGuid().ToString("N") + ".jsonl");
            string second = Path.Combine(TestContext.CurrentContext.WorkDirectory, "signal-determinism-2-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                Assert.That(RunCli(folder, first, "20:ping", 21), Is.EqualTo(0));
                Assert.That(RunCli(folder, second, "20:ping", 21), Is.EqualTo(0));
                Assert.That(File.ReadAllText(second), Is.EqualTo(File.ReadAllText(first)));
            }
            finally
            {
                Delete(folder);
                Delete(first);
                Delete(second);
            }
        }

        [Test]
        public void StaffOfficerSignalCallsSendSignalAndRejectsUnknownName()
        {
            var simulation = new Battle(MapGenerator.Generate(3104, true));
            var gateway = new CommandGateway(simulation);
            string calledName = null;
            SimPoint? calledPoint = null;
            var fake = new FakeCommandInterpreter(0, _ => "{\"kind\":\"SendTacticSignal\",\"tacticSignal\":\"allIn\",\"tacticSignalX\":0,\"tacticSignalZ\":0,\"reason\":\"\"}");
            using (var port = new LiveAiCommandPort(gateway, fake, () => simulation.Capture(1),
                sendTacticSignal: (name, point) =>
                {
                    calledName = name;
                    calledPoint = point;
                    return AiTacticChangeResult.Ok("合図を送信しました");
                }, enrichSummary: summary => summary.SetTacticInfo("guarded-spear", new[] { "guarded-spear" },
                    Array.Empty<AiTacticParameterInfo>(), new[] { new AiTacticSignalInfo("allIn", "総攻撃", false) })))
            {
                port.BeginInterpretation("総攻撃", null, "gpt-6-luna");
                port.Poll(0);
                Assert.That(calledName, Is.EqualTo("allIn"));
                Assert.That(calledPoint.HasValue, Is.False);
                Assert.That(port.Instructions.Last().State, Is.EqualTo(AiInstructionState.Executing));
            }

            var badSimulation = new Battle(MapGenerator.Generate(3105, true));
            var badGateway = new CommandGateway(badSimulation);
            int calls = 0;
            var badFake = new FakeCommandInterpreter(0, _ => "{\"kind\":\"SendTacticSignal\",\"tacticSignal\":\"notDefined\",\"tacticSignalX\":0,\"tacticSignalZ\":0,\"reason\":\"\"}");
            using (var badPort = new LiveAiCommandPort(badGateway, badFake, () => badSimulation.Capture(1),
                sendTacticSignal: (name, point) =>
                {
                    calls++;
                    return AiTacticChangeResult.Ok("");
                }, enrichSummary: summary => summary.SetTacticInfo("guarded-spear", new[] { "guarded-spear" },
                    Array.Empty<AiTacticParameterInfo>(), new[] { new AiTacticSignalInfo("allIn", "総攻撃", false) })))
            {
                badPort.BeginInterpretation("存在しない合図", null, "gpt-6-luna");
                badPort.Poll(0);
                Assert.That(calls, Is.EqualTo(0));
                Assert.That(badPort.Instructions.Last().State, Is.EqualTo(AiInstructionState.Unknown));
                Assert.That(badPort.Instructions.Last().Reason, Does.Contain("名前表"));
            }
        }

        private static int RunCli(string folder, string log, string signal, int ticks)
            => Rts.Headless.Cli.Program.Main(new[]
            {
                "tactic-match", "--map-seed", "3102", "--ticks", ticks.ToString(),
                "--west-tactic", folder, "--east-tactic", "auto", "--west-signal", signal, "--log-out", log
            });

        private static string CreateTacticFolder(string signals, string source)
        {
            string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "tactic-signal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string json = "{\n" +
                "  \"name\": \"signal-test\",\n" +
                "  \"author\": \"tests\",\n" +
                "  \"version\": \"1.0.0\",\n" +
                "  \"language\": \"js\",\n" +
                "  \"entry\": \"main.js\",\n" +
                "  \"apiVersion\": 1,\n" +
                "  \"description\": \"signal test\",\n" +
                "  \"signals\": " + signals + "\n" +
                "}\n";
            File.WriteAllText(Path.Combine(folder, "tactic.json"), json, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "main.js"), source, new UTF8Encoding(false));
            return folder;
        }

        private static void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }

        private static Fix64 Fix(int value) => Fix64.FromInt(value);

        private sealed class SimulationFrames : IFrameSource
        {
            private readonly Battle simulation;
            internal SimulationFrames(Battle simulation) { this.simulation = simulation; }
            public FactionFrame Latest(uint factionId) => simulation.Capture(factionId);
        }

        private sealed class RecordingSignalRuntime : ITacticRuntime, ITacticSignalRuntime
        {
            private readonly IReadOnlyList<TacticSignalDefinition> definitions;
            internal RecordingSignalRuntime(IReadOnlyList<TacticSignalDefinition> definitions) { this.definitions = definitions; }
            public string Name => "signal-test";
            public IReadOnlyList<TacticSignalDefinition> Signals => definitions;
            public List<string> Views { get; } = new List<string>();
            public void Start(string setupJson) { }
            public string Tick(string viewJson)
            {
                Views.Add(viewJson);
                return "{\"version\":1,\"commands\":[]}";
            }
        }
    }
}
