using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Rts.Core.Tests
{
    public sealed class SayCommandTests
    {
        [Test]
        public void SayAcceptsGeneratedTerrainAndAgesMap()
        {
            string script = Path.Combine(TestContext.CurrentContext.WorkDirectory, "say-map-" + Guid.NewGuid().ToString("N") + ".json");
            string output = Path.Combine(TestContext.CurrentContext.WorkDirectory, "say-map-" + Guid.NewGuid().ToString("N") + ".out.json");
            try
            {
                File.WriteAllText(script, "{\"commands\":[{\"tick\":0,\"instruction\":\"守って\",\"response\":\"{\\\"commands\\\":[],\\\"say\\\":\\\"了解\\\",\\\"reason\\\":null,\\\"unknown\\\":false}\"}]}", new UTF8Encoding(false));
                int exit = Rts.Headless.Cli.Program.Main(new[]
                {
                    "say", "--map-seed", "3106", "--terrain", "--ages", "--ticks", "1",
                    "--say", script, "--out", output
                });
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(File.Exists(output), Is.True);
                Assert.That(File.ReadAllText(output), Does.Contain("\\u4E86\\u89E3"));
            }
            finally
            {
                if (File.Exists(script)) File.Delete(script);
                if (File.Exists(output)) File.Delete(output);
            }
        }

        [Test]
        public void SayRequiresExactlyOneScenarioSource()
        {
            string script = Path.Combine(TestContext.CurrentContext.WorkDirectory, "say-source-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(script, "{\"commands\":[]}", new UTF8Encoding(false));
                int both = Rts.Headless.Cli.Program.Main(new[]
                {
                    "say", "--scenario", "missing.json", "--map-seed", "1", "--ticks", "1", "--say", script
                });
                int neither = Rts.Headless.Cli.Program.Main(new[]
                {
                    "say", "--ticks", "1", "--say", script
                });
                Assert.That(both, Is.EqualTo(3));
                Assert.That(neither, Is.EqualTo(3));
            }
            finally
            {
                if (File.Exists(script)) File.Delete(script);
            }
        }
    }
}
