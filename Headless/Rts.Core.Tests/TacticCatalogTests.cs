using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Tactics;
using Rts.TacticsJs;

namespace Rts.Core.Tests
{
    public sealed class TacticCatalogTests
    {
        [Test]
        public void CatalogListsFoldersInNameOrderAndKeepsReasonsForUnreadableFolders()
        {
            string parent = Path.Combine(Path.GetTempPath(), "rts-tactic-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            try
            {
                string valid = Directory.CreateDirectory(Path.Combine(parent, "alpha")).FullName;
                File.WriteAllText(Path.Combine(valid, "tactic.json"), Metadata("Alpha"));
                File.WriteAllText(Path.Combine(valid, "main.js"), "function onTick(view) { return {commands:[]}; }");
                Directory.CreateDirectory(Path.Combine(parent, "bravo-no-metadata"));
                string broken = Directory.CreateDirectory(Path.Combine(parent, "charlie-broken")).FullName;
                File.WriteAllText(Path.Combine(broken, "tactic.json"), "{broken");

                var entries = TacticCatalog.Scan(parent);
                Assert.That(entries.Select(x => x.FolderName).ToArray(), Is.EqualTo(new[] { "alpha", "bravo-no-metadata", "charlie-broken" }));
                Assert.That(entries[0].IsSelectable, Is.True);
                Assert.That(entries[0].DisplayName, Is.EqualTo("Alpha"));
                Assert.That(entries[1].IsSelectable, Is.False);
                Assert.That(entries[1].Reason, Does.Contain("tactic.json"));
                Assert.That(entries[2].IsSelectable, Is.False);
                Assert.That(entries[2].Reason, Does.Contain("JSON"));
            }
            finally
            {
                if (Directory.Exists(parent)) Directory.Delete(parent, true);
            }
        }

        [Test]
        public void MatchSetupMakesSelectedTacticOwnTheSideAndLeavesNoneToThePreset()
        {
            var none = TacticMatchSetup.Create(1, TacticMatchSetup.None, null, null, null, null);
            Assert.That(none.HasTactic, Is.False);
            Assert.That(none.Preset, Is.Null);

            var selected = TacticMatchSetup.Create(1, "folder", _ => new IdleTactic(),
                new EmptyFrames(), new EmptyCommandPort(), null);
            Assert.That(selected.HasTactic, Is.True);
            Assert.That(selected.Host.Name, Is.EqualTo("idle"));
            Assert.That(selected.Preset, Is.EqualTo("none"));
        }

        private static string Metadata(string name)
        {
            return "{\"name\":\"" + name + "\",\"author\":\"test\",\"version\":\"1\",\"language\":\"js\",\"entry\":\"main.js\",\"apiVersion\":1,\"description\":\"test\"}";
        }

        private sealed class EmptyFrames : IFrameSource
        {
            public FactionFrame Latest(uint factionId) { throw new NotSupportedException(); }
        }

        private sealed class EmptyCommandPort : ICommandPort
        {
            public ulong Submit(UserPolicyIntent intent) { return 0; }
            public void Cancel(ulong requestId) { }
            public ulong Propose(uint faction, ulong sequence, System.Collections.Generic.IReadOnlyList<PolicyOrder> orders, long applyTick) { return 0; }
        }
    }
}
