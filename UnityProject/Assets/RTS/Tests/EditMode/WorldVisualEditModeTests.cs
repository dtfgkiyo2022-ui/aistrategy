using System.Collections.Generic;
using NUnit.Framework;
using Rts.Presentation;
using UnityEngine;

namespace Rts.Tests.EditMode
{
    public sealed class WorldVisualEditModeTests
    {
        [Test]
        public void SplatWeightsFollowTerrainKinds()
        {
            var kinds = new byte[9]; kinds[4] = 1; kinds[8] = 3;
            var blocked = new List<int> { 8 };
            var map = new TerrainMap(3, 3, 2f, blocked, kinds);
            TerrainSplatWeights forest = TerrainVisualMath.GetSplatWeights(map, 1, 1);
            TerrainSplatWeights mountain = TerrainVisualMath.GetSplatWeights(map, 2, 2);
            Assert.Greater(forest.Forest, 0.20f);
            Assert.Greater(mountain.Rock, 0.20f);
            Assert.That(forest.Sum, Is.EqualTo(1f).Within(0.001f));
            Assert.That(mountain.Sum, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void DecorationsAreSeedStableAndCapped()
        {
            var kinds = new byte[12 * 8];
            for (int i = 0; i < kinds.Length; i++) if (i % 7 == 0) kinds[i] = 1;
            var map = new TerrainMap(12, 8, 2f, new List<int>(), kinds);
            List<TerrainDecorationInstance> first = TerrainVisualMath.GetDecorations(map, 41721, 24);
            List<TerrainDecorationInstance> second = TerrainVisualMath.GetDecorations(map, 41721, 24);
            Assert.LessOrEqual(first.Count, 24);
            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Kind, second[i].Kind);
                Assert.AreEqual(first[i].X, second[i].X);
                Assert.That(first[i].LocalPosition, Is.EqualTo(second[i].LocalPosition));
            }
        }

        [Test]
        public void PainterlyTexturesMeetAtBothEdges()
        {
            Texture2D texture = TerrainTextureGenerator.Create(TerrainTextureKind.Grass, 32);
            try
            {
                for (int y = 0; y < texture.height; y++)
                {
                    // The two edges meet like neighbouring pixels: the painterly noise itself moves up to a few levels
                    // (of 255) from one pixel to the next, so the bound is that step, not zero.
                    Assert.That(MaxChannelDifference(texture.GetPixel(0, y), texture.GetPixel(texture.width - 1, y)), Is.LessThan(0.04f));
                    Assert.That(MaxChannelDifference(texture.GetPixel(y, 0), texture.GetPixel(y, texture.height - 1)), Is.LessThan(0.04f));
                }
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static float MaxChannelDifference(Color a, Color b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
        }
    }
}
