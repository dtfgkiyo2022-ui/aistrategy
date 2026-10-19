using System;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class UiInfoEditModeTests
    {
        [Test]
        public void BuildingDescriptionsCoverEveryBuildingKind()
        {
            bool japanese = UiText.Japanese;
            try
            {
                UiText.Japanese = true;
                foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
                    Assert.That(SelectionInfoText.BuildingDescription(kind), Is.Not.Null.And.Not.Empty, kind.ToString());
            }
            finally { UiText.Japanese = japanese; }
        }

        [TestCase(0, 0, 0f)]
        [TestCase(2, 10, 0.2f)]
        [TestCase(-2, 10, 0f)]
        [TestCase(20, 10, 1f)]
        public void ConstructionRatioIsBoundedAndSafe(int progress, int work, float expected)
        {
            Assert.That(UiInfoRules.ConstructionRatio(progress, work), Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void CoreClearanceCornersMatchChebyshevSquare()
        {
            var corners = CoreClearanceGeometry.CalculateCorners(5 * 10 + 4, 3, 10);
            Assert.That(corners.Length, Is.EqualTo(4));
            Assert.That(corners[0].X, Is.EqualTo(2)); Assert.That(corners[0].Z, Is.EqualTo(3));
            Assert.That(corners[1].X, Is.EqualTo(6)); Assert.That(corners[1].Z, Is.EqualTo(3));
            Assert.That(corners[2].X, Is.EqualTo(6)); Assert.That(corners[2].Z, Is.EqualTo(7));
            Assert.That(corners[3].X, Is.EqualTo(2)); Assert.That(corners[3].Z, Is.EqualTo(7));
        }

        [Test]
        public void ObjectiveSelectionTextShowsCoreHpAndCaptureInJapanese()
        {
            bool japanese = UiText.Japanese;
            try
            {
                UiText.Japanese = true;
                var core = new KnownObjective(GoalKind.Core, 1, default(SimPoint), true, 1, true, 4476, 0, 6000);
                Assert.That(SelectionInfoText.ObjectiveTitle(core, 1), Is.EqualTo("自分のコア"));
                StringAssert.Contains("HP 4,476 / 6,000（75%）", SelectionInfoText.ObjectiveDetail(core, 1, 0));

                var unknown = new KnownObjective(GoalKind.Core, 2, default(SimPoint), true, 2, false, 0, 200);
                StringAssert.Contains("HP：不明（最後に見たのは 10秒前）", SelectionInfoText.ObjectiveDetail(unknown, 1, 400));

                var outpost = new KnownObjective(GoalKind.Outpost, 1, default(SimPoint), true, 2, false, 0, 0,
                    1, 100, 200);
                Assert.That(SelectionInfoText.ObjectiveTitle(outpost, 1), Is.EqualTo("前哨地（敵）"));
                StringAssert.Contains("占領中：自分 50%", SelectionInfoText.ObjectiveDetail(outpost, 1, 0));
            }
            finally { UiText.Japanese = japanese; }
        }
    }
}
