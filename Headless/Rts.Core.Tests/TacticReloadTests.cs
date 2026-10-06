using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Rts.Tactics;

namespace Rts.Core.Tests
{
    public sealed class TacticReloadTests
    {
        [Test]
        public void ReloadBuilderCarriesOnlyValuesAcceptedByTheNewDefinition()
        {
            var oldDefinition = new TacticParamDefinition("threshold", "しきい値", "int", 30, 5, 100, 5);
            var newDefinition = new TacticParamDefinition("threshold", "しきい値", "int", 20, 10, 60, 10);
            var oldRuntime = new ParameterRuntime("old", oldDefinition);
            var oldHost = new TacticHost(1, new EmptyFrames(), new EmptyPort(), null, oldRuntime);
            Assert.That(oldHost.SetParam("threshold", 50), Is.True);

            var replacement = TacticReloadBuilder.CreateReplacement(1, "folder", _ => new ParameterRuntime("new", newDefinition),
                new EmptyFrames(), new EmptyPort(), null, oldHost);

            Assert.That(replacement.Host.ParamValues["threshold"], Is.EqualTo(50));
            Assert.That(replacement.Host.Name, Is.EqualTo("new"));
        }

        [Test]
        public void CarryoverRejectsAValueOutsideTheNewRange()
        {
            var old = new TacticParamDefinition("threshold", "しきい値", "int", 30, 5, 100, 5);
            var next = new TacticParamDefinition("threshold", "しきい値", "int", 20, 10, 40, 10);
            var values = TacticParameterCarryover.CompatibleValues(new[] { old },
                new Dictionary<string, object> { { "threshold", 45 } }, new[] { next });
            Assert.That(values.ContainsKey("threshold"), Is.False);
        }

        [Test]
        public void FileStampChangesWhenAnyWatchedFileIsEdited()
        {
            string folder = Path.Combine(Path.GetTempPath(), "rts-tactic-reload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "tactic.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "main.js"), "");
                var before = TacticFileStamp.Capture(folder);
                File.SetLastWriteTimeUtc(Path.Combine(folder, "main.js"), DateTime.UtcNow.AddMinutes(1));
                var after = TacticFileStamp.Capture(folder);
                Assert.That(after, Is.Not.EqualTo(before));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void PollerAllowsOneCheckPerWallClockInterval()
        {
            var poller = new TacticReloadPoller(TimeSpan.FromSeconds(3));
            var start = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
            Assert.That(poller.ShouldCheck(start), Is.True);
            Assert.That(poller.ShouldCheck(start.AddSeconds(2.9)), Is.False);
            Assert.That(poller.ShouldCheck(start.AddSeconds(3)), Is.True);
        }

        [Test]
        public void MatchPackWritesReloadRecordWithOutcomeAndTime()
        {
            var scenario = MapGenerator.Generate(99, true);
            string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "tactic-reload-pack-" + Guid.NewGuid().ToString("N"));
            var pack = new MatchPackWriter(folder, scenario, "old", "auto");
            pack.RecordTacticReload(new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Utc), 20, 1, "old", false, true, "bad json");
            pack.Complete(Array.Empty<ScheduledInput>(), 0);
            string line = File.ReadAllText(Path.Combine(folder, "tactic-log.jsonl"));
            Assert.That(line, Does.Contain("\"kind\":\"reload\""));
            Assert.That(line, Does.Contain("\"success\":false"));
            Assert.That(line, Does.Contain("\"automatic\":true"));
            Assert.That(line, Does.Contain("2026-10-06T01:02:03.0000000Z"));
        }

        private sealed class ParameterRuntime : ITacticRuntime, ITacticParameterRuntime
        {
            private readonly IReadOnlyList<TacticParamDefinition> definitions;
            public ParameterRuntime(string name, params TacticParamDefinition[] definitions)
            {
                Name = name; this.definitions = definitions;
            }
            public string Name { get; }
            public IReadOnlyList<TacticParamDefinition> Parameters { get { return definitions; } }
            public void Start(string setupJson) { }
            public string Tick(string viewJson) { return "{\"version\":1,\"commands\":[]}"; }
            public void SetParameters(IReadOnlyDictionary<string, object> values) { }
        }

        private sealed class EmptyFrames : IFrameSource
        {
            public FactionFrame Latest(uint factionId) { return null; }
        }

        private sealed class EmptyPort : ICommandPort
        {
            public ulong Submit(UserPolicyIntent intent) { return 0; }
            public void Cancel(ulong requestId) { }
            public ulong Propose(uint faction, ulong sequence, IReadOnlyList<PolicyOrder> orders, long applyTick) { return 0; }
        }
    }
}
