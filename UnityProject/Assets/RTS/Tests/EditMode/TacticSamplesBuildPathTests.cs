using System.IO;
using NUnit.Framework;
using Rts.Editor;

namespace Rts.Tests.EditMode
{
    public sealed class TacticSamplesBuildPathTests
    {
        [Test]
        public void ResolvesRepositorySamplesToProductStreamingAssets()
        {
            var plan = TacticSamplesBuildPaths.Resolve("D:/repo", "D:/out/Astra.exe");
            Assert.That(plan.SourceDirectory, Is.EqualTo(Path.GetFullPath("D:/repo/TacticSamples")));
            Assert.That(plan.DestinationDirectory, Is.EqualTo(Path.GetFullPath("D:/out/Astra_Data/StreamingAssets/TacticSamples")));
            // The copy goes to the build output, never back into the repository (so the samples are not doubled).
            Assert.That(plan.DestinationDirectory, Does.Not.StartWith(Path.GetFullPath("D:/repo")));
        }

        [Test]
        public void UsesBuildFileNameForDataDirectory()
        {
            var plan = TacticSamplesBuildPaths.Resolve("D:/repo", "D:/out/My Game.exe");
            Assert.That(plan.DestinationDirectory, Does.EndWith(Path.Combine("My Game_Data", "StreamingAssets", "TacticSamples")));
        }
    }
}
