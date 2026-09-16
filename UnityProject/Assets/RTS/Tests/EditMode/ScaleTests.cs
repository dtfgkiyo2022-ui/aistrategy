using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Xml.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class ScaleTests
    {
        private static BuildIdentity Build() => new BuildIdentity { Commit = "scale-test", SourceHash = new string('a', 64), Backend = "test" };
        private static string Root()
        {
            for (var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "TestData"))) return d.FullName;
            throw new DirectoryNotFoundException("Repository TestData not found.");
        }
        private static XElement Json(string name)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(File.ReadAllBytes(Path.Combine(Root(), "TestData", name)), new System.Xml.XmlDictionaryReaderQuotas()))
                return XElement.Load(reader);
        }
        // JSON differs from the proven 40-soldier definition only in ID and soldier array.
        // Read actual saved positions/IDs, never regenerate them on the replay path.
        private static ScenarioDefinition Load(int total)
        {
            var data = Json("week3-" + total + ".json");
            var baseline = Json("week2-2routes.json");
            var s = WeekTwoScenario.Create();
            s.ScenarioId = data.Element("scenarioId").Value;
            s.Soldiers = data.Element("soldiers").Elements().Select(e => new SoldierDefinition
            {
                Id = (uint)e.Element("id"), FactionId = (uint)e.Element("factionId"), ArmyId = (uint)e.Element("armyId"),
                Kind = (UnitKind)(int)e.Element("kind"), Alive = (bool)e.Element("alive"), Hp = (int)e.Element("hp"),
                Position = new SimPoint(Fix64.FromInt((int)e.Element("positionMeters").Element("x")), Fix64.FromInt((int)e.Element("positionMeters").Element("z")))
            }).ToArray();
            foreach (var doc in new[] { data, baseline }) { doc.Element("scenarioId").Remove(); doc.Element("soldiers").Remove(); }
            Assert.That(XNode.DeepEquals(data, baseline), Is.True, "All non-soldier JSON settings must match week2.");
            return ScenarioBinary.Decode(ScenarioBinary.Encode(s));
        }
        [TestCase(20)]
        [TestCase(80)]
        public void ScaleScenarioHasSpecifiedArmiesIdsAndPositions(int total)
        {
            var s = Load(total); var sim = new Battle(s);
            int[] counts = total == 20 ? new[] { 4, 4, 1, 1 } : new[] { 16, 16, 6, 2 };
            Assert.That(s.Soldiers.Length, Is.EqualTo(total));
            Assert.That(s.Armies.Length, Is.EqualTo(8));
            Assert.That(s.Rules.FactionCap, Is.EqualTo(40));
            Assert.That(s.VerificationTickLimit, Is.EqualTo(36000));
            Assert.That(s.Map.BlockedCellIds, Is.EqualTo(WeekTwoScenario.Create().Map.BlockedCellIds));
            uint id = 1;
            for (uint f = 1; f <= 2; f++)
            {
                Assert.That(s.Soldiers.Count(p => p.FactionId == f), Is.EqualTo(total / 2));
                for (int a = 0; a < 4; a++)
                {
                    uint army = (f - 1) * 4 + (uint)a + 1;
                    Assert.That(s.Armies[army - 1].Id, Is.EqualTo(army));
                    Assert.That(s.Armies[army - 1].Capacity, Is.EqualTo(new[] { 16, 16, 6, 2 }[a]));
                    Assert.That(s.Soldiers.Count(p => p.ArmyId == army), Is.EqualTo(counts[a]));
                    for (int i = 0; i < counts[a]; i++, id++)
                    {
                        var p = s.Soldiers[id - 1]; int x = (a == 3 ? 28 : 24) + i % 4;
                        Assert.That(p.Id, Is.EqualTo(id)); Assert.That(p.ArmyId, Is.EqualTo(army));
                        Assert.That(p.FactionId, Is.EqualTo(f)); Assert.That(p.Alive, Is.True);
                        Assert.That(p.Kind, Is.EqualTo(a == 3 ? UnitKind.Scout : UnitKind.Infantry));
                        Assert.That(p.Position.X, Is.EqualTo(Fix64.FromInt(f == 1 ? x : 256 - x)));
                        Assert.That(p.Position.Z, Is.EqualTo(Fix64.FromInt(new[] { 96, 32, 64, 64 }[a] + i / 4)));
                    }
                }
            }
            Assert.That(sim.Capture(1).Result.IsFault, Is.False);
        }
        [Test]
        public void EightySoldiersAreDeterministicEveryTick()
        {
            var a = new Battle(Load(80)); var b = new Battle(Load(80));
            for (int tick = 0; tick <= 240; tick++)
            {
                if (tick > 0) { a.Step(tick, Array.Empty<ScheduledInput>()); b.Step(tick, Array.Empty<ScheduledInput>()); }
                CommandTestInput.AssertCanonicalEqual(a.CaptureDiagnostic(), b.CaptureDiagnostic(), "tick=" + tick);
                for (uint faction = 1; faction <= 2; faction++)
                    Assert.That(DiagnosticComparison.EventHash(a.Capture(faction)), Is.EqualTo(DiagnosticComparison.EventHash(b.Capture(faction))));
            }
        }
        [Test]
        public void EightySoldiersRecordReplayMatchesEveryTick()
        {
            var hashes = new List<byte[]>();
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, Load(80), Array.Empty<ScheduledInput>(), 240, Build(), (s, h, e) => hashes.Add(h.Concat(e).ToArray()));
                stream.Position = 0; int count = 0;
                var result = ReplayRunner.Replay(stream, Build(), (s, h, e) => Assert.That(h.Concat(e), Is.EqualTo(hashes[count++])));
                Assert.That(count, Is.EqualTo(241)); Assert.That(result.FirstMismatchTick, Is.Null); Assert.That(result.IsFault, Is.False);
            }
        }
        [Test]
        public void BenchmarkInstrumentationAndRecordingPreserveEveryHash()
        {
            var hashes = new List<byte[]>();
            var inputs = PolicyPresets.RecordedInputs(Load(80), "maintain", "concentrate", 240);
            using (var reference = new MemoryStream())
            {
                ReplayRunner.Record(reference, Load(80), inputs, 240, Build(), (s, h, e) => hashes.Add(h.Concat(e).ToArray()));
                foreach (bool recording in new[] { false, true })
                using (var output = new MemoryStream())
                {
                    int count = 0, calls = 0; var stack = new Stack<string>();
                    Action<string, bool> observer = (name, start) =>
                    {
                        calls++;
                        // Exercise the same external clock boundary as the CLI. Time is not supplied to the simulation.
                        System.Diagnostics.Stopwatch.GetTimestamp();
                        if (start) stack.Push(name); else Assert.That(stack.Pop(), Is.EqualTo(name));
                    };
                    ReplayRunner.Benchmark(recording ? output : null, Load(80), inputs, 240, Build(), observer,
                        (s, h, e) => Assert.That(h.Concat(e), Is.EqualTo(hashes[count++]), "tick=" + s.Tick));
                    Assert.That(count, Is.EqualTo(241)); Assert.That(calls, Is.GreaterThan(240)); Assert.That(stack, Is.Empty);
                    if (recording) Assert.That(output.ToArray(), Is.EqualTo(reference.ToArray()));
                }
            }
        }
    }
}
