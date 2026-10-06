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
        public void CatalogPutsRecommendedTacticsFirst()
        {
            string parent = Path.Combine(Path.GetTempPath(), "rts-tactic-recommended-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            try
            {
                string ordinary = Directory.CreateDirectory(Path.Combine(parent, "alpha-ordinary")).FullName;
                File.WriteAllText(Path.Combine(ordinary, "tactic.json"), Metadata("ordinary"));
                File.WriteAllText(Path.Combine(ordinary, "main.js"), "function onTick(view) { return {commands:[]}; }");
                string recommended = Directory.CreateDirectory(Path.Combine(parent, "zulu-recommended")).FullName;
                File.WriteAllText(Path.Combine(recommended, "tactic.json"), Metadata("recommended", "partner", true));
                File.WriteAllText(Path.Combine(recommended, "main.js"), "function onTick(view) { return {commands:[]}; }");

                var entries = TacticCatalog.Scan(parent);
                Assert.That(entries.Select(x => x.FolderName).ToArray(), Is.EqualTo(new[] { "zulu-recommended", "alpha-ordinary" }));
                Assert.That(entries[0].Metadata.Style, Is.EqualTo("partner"));
                Assert.That(entries[0].Metadata.Recommended, Is.True);
            }
            finally
            {
                if (Directory.Exists(parent)) Directory.Delete(parent, true);
            }
        }

        [Test]
        public void MetadataReadsStylesAndRecommendedAndRejectsInvalidValues()
        {
            string parent = Path.Combine(Path.GetTempPath(), "rts-tactic-metadata-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            try
            {
                string valid = Directory.CreateDirectory(Path.Combine(parent, "valid")).FullName;
                File.WriteAllText(Path.Combine(valid, "tactic.json"), Metadata("valid", "auto", true));
                File.WriteAllText(Path.Combine(valid, "main.js"), "function onTick(view) { return {commands:[]}; }");
                var loaded = TacticFolder.Load(valid);
                Assert.That(loaded.IsSuccess, Is.True);
                Assert.That(loaded.Metadata.Style, Is.EqualTo("auto"));
                Assert.That(loaded.Metadata.Recommended, Is.True);

                string invalidStyle = Directory.CreateDirectory(Path.Combine(parent, "invalid-style")).FullName;
                File.WriteAllText(Path.Combine(invalidStyle, "tactic.json"), Metadata("invalid", "other", false));
                File.WriteAllText(Path.Combine(invalidStyle, "main.js"), "function onTick(view) { return {commands:[]}; }");
                var badStyle = TacticFolder.Load(invalidStyle);
                Assert.That(badStyle.IsSuccess, Is.False);
                Assert.That(badStyle.Error, Does.Contain("style"));

                string invalidRecommended = Directory.CreateDirectory(Path.Combine(parent, "invalid-recommended")).FullName;
                File.WriteAllText(Path.Combine(invalidRecommended, "tactic.json"), MetadataWithRecommendedText("invalid"));
                File.WriteAllText(Path.Combine(invalidRecommended, "main.js"), "function onTick(view) { return {commands:[]}; }");
                var badRecommended = TacticFolder.Load(invalidRecommended);
                Assert.That(badRecommended.IsSuccess, Is.False);
                Assert.That(badRecommended.Error, Does.Contain("recommended"));
            }
            finally
            {
                if (Directory.Exists(parent)) Directory.Delete(parent, true);
            }
        }

        [Test]
        public void BundledRecommendedSamplesDeclareTheirTwoStyles()
        {
            string root = FindRepositoryRoot();
            var auto = TacticFolder.Load(Path.Combine(root, "TacticSamples", "guarded-spear"));
            var partner = TacticFolder.Load(Path.Combine(root, "TacticSamples", "adjutant"));
            Assert.That(auto.IsSuccess, Is.True, auto.Error);
            Assert.That(partner.IsSuccess, Is.True, partner.Error);
            Assert.That(auto.Metadata.Style, Is.EqualTo("auto"));
            Assert.That(auto.Metadata.Recommended, Is.True);
            Assert.That(partner.Metadata.Style, Is.EqualTo("partner"));
            Assert.That(partner.Metadata.Recommended, Is.True);
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

        private static string Metadata(string name, string style = null, bool recommended = false)
        {
            return "{\"name\":\"" + name + "\",\"author\":\"test\",\"version\":\"1\",\"language\":\"js\",\"entry\":\"main.js\",\"apiVersion\":1,\"description\":\"test\""
                + (style == null ? "" : ",\"style\":\"" + style + "\",\"recommended\":" + (recommended ? "true" : "false")) + "}";
        }

        private static string MetadataWithRecommendedText(string name)
        {
            return "{\"name\":\"" + name + "\",\"author\":\"test\",\"version\":\"1\",\"language\":\"js\",\"entry\":\"main.js\",\"apiVersion\":1,\"description\":\"test\",\"recommended\":\"yes\"}";
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "TacticSamples"))) directory = directory.Parent;
            return directory == null ? null : directory.FullName;
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
