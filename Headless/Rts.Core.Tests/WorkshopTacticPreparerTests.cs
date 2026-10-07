using System;
using System.IO;
using NUnit.Framework;
using Rts.Tactics;
using Rts.TacticsJs;

namespace Rts.Core.Tests
{
    public sealed class WorkshopTacticPreparerTests
    {
        [Test]
        public void CopiesPublishableFilesAndSkipsMemoryAndHiddenFiles()
        {
            string root = MakeRoot();
            try
            {
                string source = Path.Combine(root, "source");
                Directory.CreateDirectory(Path.Combine(source, "params"));
                Directory.CreateDirectory(Path.Combine(source, "memory"));
                Directory.CreateDirectory(Path.Combine(source, ".hidden-dir"));
                File.WriteAllText(Path.Combine(source, "tactic.json"), "{\"name\":\"Sample\",\"author\":\"a\",\"version\":\"1\",\"language\":\"js\",\"entry\":\"main.js\",\"apiVersion\":1,\"description\":\"metadata\"}");
                File.WriteAllText(Path.Combine(source, "main.js"), "function onTick(view) { return { commands: [] }; }");
                File.WriteAllText(Path.Combine(source, "README.md"), "# README title\nmore");
                File.WriteAllText(Path.Combine(source, "params", "settings.json"), "{}");
                File.WriteAllText(Path.Combine(source, "memory", "between-matches.json"), "secret");
                File.WriteAllText(Path.Combine(source, ".hidden-file"), "secret");

                var result = WorkshopTacticPreparer.Prepare(source, Path.Combine(root, "temp"), Path.Combine(root, "save"),
                    folder =>
                    {
                        var loaded = TacticFolder.Load(folder);
                        (loaded.Runtime as IDisposable)?.Dispose();
                        return loaded.IsSuccess ? null : loaded.Error;
                    });

                Assert.That(result.IsSuccess, Is.True, result.Error);
                Assert.That(File.Exists(Path.Combine(result.StagedFolder, "tactic.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(result.StagedFolder, "params", "settings.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(result.StagedFolder, "memory", "between-matches.json")), Is.False);
                Assert.That(File.Exists(Path.Combine(result.StagedFolder, ".hidden-file")), Is.False);
                Assert.That(result.Info.Title, Is.EqualTo("Sample"));
                Assert.That(result.Info.Description, Is.EqualTo("README title"));
                Assert.That(result.Info.Tags, Does.Contain("js"));
            }
            finally { Delete(root); }
        }

        [Test]
        public void RejectsFolderThatTacticFolderCannotLoad()
        {
            string root = MakeRoot();
            try
            {
                string source = Path.Combine(root, "bad");
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "tactic.json"), "{\"name\":\"Bad\",\"language\":\"js\",\"description\":\"x\",\"entry\":\"main.js\",\"apiVersion\":99,\"author\":\"a\",\"version\":\"1\"}");
                File.WriteAllText(Path.Combine(source, "main.js"), "");
                var result = WorkshopTacticPreparer.Prepare(source, Path.Combine(root, "temp"), Path.Combine(root, "save"),
                    folder => { var loaded = TacticFolder.Load(folder); return loaded.IsSuccess ? null : loaded.Error; });
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Error, Does.Contain("未対応"));
            }
            finally { Delete(root); }
        }

        [Test]
        public void PublicationMapKeepsTheSameIdAcrossSaveAndLoad()
        {
            string root = MakeRoot();
            try
            {
                string folder = Path.Combine(root, "tactic");
                Directory.CreateDirectory(folder);
                var map = WorkshopTacticPreparer.LoadPublicationMap(Path.Combine(root, "save"));
                map.Set(folder, 480UL);
                map.Save();
                var loaded = WorkshopTacticPreparer.LoadPublicationMap(Path.Combine(root, "save"));
                Assert.That(loaded.TryGet(folder, out var id), Is.True);
                Assert.That(id, Is.EqualTo(480UL));
            }
            finally { Delete(root); }
        }

        private static string MakeRoot()
        {
            string path = Path.Combine(Path.GetTempPath(), "rts-workshop-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Delete(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
