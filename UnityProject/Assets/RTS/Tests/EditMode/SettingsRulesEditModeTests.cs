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
            KeyBindingRules.Swap(values, KeyBindingAction.Rotate, 115);
            Assert.IsEmpty(KeyBindingRules.FindConflicts(values));
            Assert.AreEqual(119, values[2].Key);
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
