using System.Collections.Generic;
using NUnit.Framework;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class SettingsRulesEditModeTests
    {
        [Test]
        public void QualityPresetsMatchTheSettingsTableAndDetectCustomValues()
        {
            Assert.AreEqual(600, GraphicsQualityRules.For(GraphicsQualityLevel.Low).ForestTrees);
            Assert.AreEqual(360, GraphicsQualityRules.For(GraphicsQualityLevel.Low).Decorations);
            Assert.AreEqual(2, GraphicsQualityRules.For(GraphicsQualityLevel.Medium).Msaa);
            Assert.AreEqual(4, GraphicsQualityRules.For(GraphicsQualityLevel.High).Msaa);
            Assert.AreEqual(1.25f, GraphicsQualityRules.For(GraphicsQualityLevel.Ultra).RenderScale);
            Assert.AreEqual(GraphicsQualityLevel.Custom,
                GraphicsQualityRules.Detect(new GraphicsQualityValues { ShadowResolution = 2048, ShadowDistance = 51f }));
        }

        [Test]
        public void ModelDetailFollowsThePresetAndFallsBackToALowerTier()
        {
            Assert.AreEqual(0, GraphicsQualityRules.For(GraphicsQualityLevel.Low).ModelDetail);
            Assert.AreEqual(1, GraphicsQualityRules.For(GraphicsQualityLevel.Medium).ModelDetail);
            Assert.AreEqual(2, GraphicsQualityRules.For(GraphicsQualityLevel.High).ModelDetail);
            Assert.AreEqual(3, GraphicsQualityRules.For(GraphicsQualityLevel.Ultra).ModelDetail);
            var value = GraphicsQualityRules.For(GraphicsQualityLevel.Low);
            value.ModelDetail = 3;
            Assert.AreEqual(GraphicsQualityLevel.Custom, GraphicsQualityRules.Detect(value));

            int before = ArtDetail.Level;
            try
            {
                ArtDetail.Set(3);
                // Only the High file exists: Ultra falls back to it. None exists: Low (the original files).
                Assert.AreEqual(2, ArtDetail.Resolve("HD/", "Farm", path => path == "HD/Farm_H" ? new UnityEngine.GameObject() : null));
                Assert.AreEqual(0, ArtDetail.Resolve("HD/", "Farm", path => null));
                ArtDetail.Set(0);
                Assert.AreEqual(0, ArtDetail.Resolve("HD/", "Farm", path => new UnityEngine.GameObject()));
            }
            finally { ArtDetail.Set(before); }
        }

        [Test]
        public void CustomPipelineKeepsLowBaseWhenOnlyDecorationsChange()
        {
            var value = GraphicsQualityRules.For(GraphicsQualityLevel.Low);
            value.ForestTrees = 900;
            Assert.AreEqual(GraphicsQualityLevel.Low, GraphicsQualityRules.PipelineLevel(value));
        }

        [Test]
        public void KeyConflictsAreReportedAndSwapRemovesTheConflict()
        {
            var values = new List<KeyBindingPair>
            {
                new KeyBindingPair(KeyBindingAction.CameraUp, 119),
                new KeyBindingPair(KeyBindingAction.CameraDown, 115),
                new KeyBindingPair(KeyBindingAction.Rotate, 119)
            };
            Assert.That(KeyBindingRules.FindConflicts(values), Does.Contain(KeyBindingAction.CameraUp));
            Assert.That(KeyBindingRules.FindConflicts(values), Does.Contain(KeyBindingAction.Rotate));
            // Swapping starts from bindings without a conflict: R (114) for rotate takes S, and camera-down gets R.
            var clean = new List<KeyBindingPair>
            {
                new KeyBindingPair(KeyBindingAction.CameraUp, 119),
                new KeyBindingPair(KeyBindingAction.CameraDown, 115),
                new KeyBindingPair(KeyBindingAction.Rotate, 114)
            };
            KeyBindingRules.Swap(clean, KeyBindingAction.Rotate, 115);
            Assert.IsEmpty(KeyBindingRules.FindConflicts(clean));
            Assert.AreEqual(115, clean[2].Key);
            Assert.AreEqual(114, clean[1].Key);
        }

        [Test]
        public void ResolutionRulesRemoveDuplicateSizesAndInvalidEntries()
        {
            var result = ResolutionRules.Deduplicate(new[]
            {
                new ResolutionKey(1920, 1080), new ResolutionKey(1920, 1080),
                new ResolutionKey(1280, 720), new ResolutionKey(0, 720), new ResolutionKey(1280, 0)
            });
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(new ResolutionKey(1920, 1080), result[0]);
            Assert.AreEqual(new ResolutionKey(1280, 720), result[1]);
        }
    }
}
