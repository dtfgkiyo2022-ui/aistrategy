using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Headless.Cli;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.Headless
{
    public sealed class TuningRuleTests
    {
        private static ScenarioDefinition Load(string rules)
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "TestData"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null);
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(dir.FullName, "TestData/week2-2routes.json")));
            foreach (var p in JsonNode.Parse(rules).AsObject()) json["rules"][p.Key] = p.Value.DeepClone();
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "tuning-rule-test.json");
            try { File.WriteAllText(path, json.ToJsonString()); return JsonInput.Scenario(path); }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        private static System.Collections.Generic.Dictionary<string,string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        [TestCase(100, TestName = "JsonThreatMemory100ReachesDecision")]
        [TestCase(400, TestName = "JsonThreatMemory400ReachesDecision")]
        public void JsonThreatMemoryReachesDecision(int memory)
        {
            var scenario = Load("{\"occupationThreatMemoryTicks\":" + memory + "}");
            var sim = new Battle(scenario);
            scenario.Rules.OccupationThreatMemoryTicks = 0; // Simulation must own its configuration copy.
            for (long tick = 1; tick <= 2000; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var fields = Fields(sim);
                var threats = fields.Where(p => p.Key.EndsWith(".ThreatUntilTick") && long.Parse(p.Value) > tick).ToArray();
                if (threats.Length == 0) continue;
                foreach (var threat in threats)
                {
                    string faction = threat.Key.Substring(0, threat.Key.IndexOf("Approaches[", StringComparison.Ordinal));
                    long observedTick = long.Parse(fields[faction + "Observation.Tick"]);
                    Assert.That(long.Parse(threat.Value), Is.EqualTo(observedTick + memory),
                        "Threat lifetime starts at the input observation tick, not the next simulation tick.");
                }
                return;
            }
            Assert.Fail("Scenario must exercise an approaching visible infantry contact.");
        }

        [Test]
        public void JsonReserveChangesAllocationAndExplicitPolicyTakesPrecedence()
        {
            var zero = Load("{\"defaultReservePermille\":0}");
            var full = Load("{\"defaultReservePermille\":1000}");
            var a = new Battle(zero); var b = new Battle(full);
            for (long tick=1;tick<=20;tick++) { a.Step(tick,Array.Empty<ScheduledInput>()); b.Step(tick,Array.Empty<ScheduledInput>()); }
            int Reserve(Battle sim) => Fields(sim).Count(p => p.Key.StartsWith("Ai.Armies[") && p.Key.EndsWith(".Assignment") && p.Value == ((byte)AssignmentKind.Reserve).ToString());
            Assert.That(Reserve(b), Is.GreaterThan(Reserve(a)));
            var inputs = PolicyPresets.RecordedInputs(zero,"maintain","maintain",80);
            a = new Battle(zero); b = new Battle(full);
            for(long tick=1;tick<=80;tick++)
            {
                var batch=inputs.Where(i=>i.AcceptedTick+1==tick).ToArray();
                a.Step(tick,batch); b.Step(tick,batch);
            }
            var af=Fields(a); var bf=Fields(b);
            foreach(var p in af.Where(p=>p.Key.StartsWith("Ai.Armies[") && p.Key.EndsWith(".Assignment")))
                Assert.That(bf[p.Key],Is.EqualTo(p.Value),p.Key);
        }

        [Test]
        public void ExplicitDefaultsPreserveBinaryAndEveryTickHash()
        {
            var implicitDefaults=Load("{}");
            var explicitDefaults=Load("{\"occupationThreatMemoryTicks\":200,\"defaultReservePermille\":100}");
            Assert.That(implicitDefaults.Rules.OccupationThreatMemoryTicks,Is.EqualTo(explicitDefaults.Rules.OccupationThreatMemoryTicks));
            Assert.That(implicitDefaults.Rules.DefaultReservePermille,Is.EqualTo(explicitDefaults.Rules.DefaultReservePermille));
            var bytes=ScenarioBinary.Encode(implicitDefaults);
            Assert.That(BitConverter.ToInt32(bytes,0),Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(explicitDefaults),Is.EqualTo(bytes));
            var a=new Battle(implicitDefaults); var b=new Battle(explicitDefaults);
            for(long tick=0;tick<=1200;tick++)
            {
                if(tick>0) { a.Step(tick,Array.Empty<ScheduledInput>()); b.Step(tick,Array.Empty<ScheduledInput>()); }
                Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState),Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)),"tick "+tick);
            }
        }

        [Test]
        public void TunedBinaryAndReplayRoundTripPreserveRulesAndHashes()
        {
            var scenario=Load("{\"occupationThreatMemoryTicks\":100,\"defaultReservePermille\":0}");
            var bytes=ScenarioBinary.Encode(scenario);
            Assert.That(BitConverter.ToInt32(bytes,0),Is.EqualTo(2));
            var copy=ScenarioBinary.Decode(bytes);
            Assert.That(copy.Rules.OccupationThreatMemoryTicks,Is.EqualTo(scenario.Rules.OccupationThreatMemoryTicks));
            Assert.That(copy.Rules.DefaultReservePermille,Is.EqualTo(scenario.Rules.DefaultReservePermille));
            Assert.That(ScenarioBinary.Encode(copy),Is.EqualTo(bytes));
            var legacy=ScenarioBinary.Decode(ScenarioBinary.Encode(Load("{}")));
            Assert.That(legacy.Rules.OccupationThreatMemoryTicks,Is.EqualTo(new RuleDefinition().OccupationThreatMemoryTicks));
            Assert.That(legacy.Rules.DefaultReservePermille,Is.EqualTo(new RuleDefinition().DefaultReservePermille));
            using(var stream=new MemoryStream())
            {
                var build=new BuildIdentity();
                ReplayRunner.Record(stream,scenario,Array.Empty<ScheduledInput>(),1200,build);
                stream.Position=0;
                var outcome=ReplayRunner.Replay(stream,build);
                Assert.That(outcome.FirstMismatchTick,Is.Null); Assert.That(outcome.IsFault,Is.False);
                Assert.That(outcome.LastTick,Is.EqualTo(1200));
            }
        }

        [TestCase(100, 100, TestName = "MemoryOnlyIsStoredAndHashed")]
        [TestCase(200, 0, TestName = "ReserveOnlyIsStoredAndHashed")]
        public void SingleTuningValueIsStoredAndHashed(int memory, int reserve)
        {
            var original = Load("{}");
            var tuned = Load("{\"occupationThreatMemoryTicks\":" + memory + ",\"defaultReservePermille\":" + reserve + "}");
            var bytes = ScenarioBinary.Encode(tuned);
            Assert.That(BitConverter.ToInt32(bytes, 0), Is.EqualTo(2));
            var copy = ScenarioBinary.Decode(bytes);
            Assert.That(copy.Rules.OccupationThreatMemoryTicks, Is.EqualTo(memory));
            Assert.That(copy.Rules.DefaultReservePermille, Is.EqualTo(reserve));
            Assert.That(Fields(new Battle(copy))["Config.Hash"], Is.Not.EqualTo(Fields(new Battle(original))["Config.Hash"]));
            var future = (byte[])bytes.Clone();
            BitConverter.GetBytes(4).CopyTo(future, 0); // schema 3 is the Ver.3 map; 4 does not exist yet
            Assert.Throws<InvalidDataException>(() => ScenarioBinary.Decode(future));
        }

        [TestCase(-1,100, TestName = "NegativeThreatMemoryIsRejected")]
        [TestCase(200,1001, TestName = "ReserveAbovePermilleIsRejected")]
        public void InvalidTuningRulesAreRejected(int memory, int reserve)
        {
            var s=Load("{}"); s.Rules.OccupationThreatMemoryTicks=memory; s.Rules.DefaultReservePermille=checked((ushort)reserve);
            Assert.Throws<ArgumentException>(()=>new Battle(s));
        }
    }
}
