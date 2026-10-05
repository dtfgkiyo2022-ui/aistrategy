using NUnit.Framework;
using Rts.Presentation;
using UnityEngine;

namespace Rts.Tests.EditMode
{
    public sealed class UiLayoutTests
    {
        private static readonly Vector2[] Sizes =
        {
            new Vector2(1280f, 720f),
            new Vector2(1600f, 900f),
            new Vector2(1920f, 1080f),
            new Vector2(2560f, 1440f)
        };

        [Test]
        public void AllFoldedRegionsStayInsideScreenAndDoNotOverlap()
        {
            foreach (var size in Sizes)
            {
                var layout = UiLayout.Calculate(size.x, size.y);
                var regions = new[]
                {
                    layout.TopLeft, layout.TopCenter, layout.TopRight, layout.Supply,
                    layout.Commands, layout.Economy, layout.Strategist, layout.LogToggle, layout.Timeline
                };
                AssertInside(regions, size);
                AssertNoOverlap(regions, size);
            }
        }

        [Test]
        public void ExpandedLogAndSetupEachStayInsideScreenAndDoNotOverlap()
        {
            foreach (var size in Sizes)
            {
                var layout = UiLayout.Calculate(size.x, size.y);
                var withLog = new[]
                {
                    layout.TopLeft, layout.TopCenter, layout.TopRight, layout.Supply,
                    layout.Commands, layout.Economy, layout.Strategist, layout.LogToggle,
                    layout.LogStatus, layout.LogEntries, layout.Timeline
                };
                AssertInside(withLog, size);
                AssertNoOverlap(withLog, size);

                var withSetup = new[]
                {
                    layout.TopLeft, layout.TopCenter, layout.TopRight, layout.Setup,
                    layout.Commands, layout.Economy, layout.Strategist, layout.LogToggle, layout.Timeline
                };
                AssertInside(withSetup, size);
                AssertNoOverlap(withSetup, size);
            }
        }

        private static void AssertInside(Rect[] regions, Vector2 size)
        {
            foreach (var rect in regions)
            {
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(size.x));
                Assert.That(rect.yMax, Is.LessThanOrEqualTo(size.y));
            }
        }

        private static void AssertNoOverlap(Rect[] regions, Vector2 size)
        {
            for (int i = 0; i < regions.Length; i++)
                for (int j = i + 1; j < regions.Length; j++)
                    Assert.That(regions[i].Overlaps(regions[j]), Is.False,
                        "overlap at " + size.x + "x" + size.y + ": " + i + "," + j);
        }
    }

    public sealed class UiHitAreasTests
    {
        [Test]
        public void RegisteredAreaIsUsedOnTheFollowingFrameOnly()
        {
            var areas = new UiHitAreas();
            areas.BeginFrame(10);
            areas.Register(new Rect(10f, 20f, 100f, 50f));
            Assert.IsFalse(areas.ContainsGui(new Vector2(20f, 30f)));

            areas.BeginFrame(11);
            Assert.IsTrue(areas.ContainsGui(new Vector2(20f, 30f)));
            Assert.IsFalse(areas.ContainsGui(new Vector2(200f, 30f)));
        }

        [Test]
        public void ScreenCoordinatesAreConvertedFromBottomLeft()
        {
            var areas = new UiHitAreas();
            areas.BeginFrame(1);
            areas.Register(new Rect(10f, 20f, 100f, 50f));
            areas.BeginFrame(2);
            Assert.IsTrue(areas.ContainsScreen(new Vector2(20f, 730f), 800f));
            Assert.IsFalse(areas.ContainsScreen(new Vector2(20f, 650f), 800f));
        }
    }
}
