using NUnit.Framework;
using Rts.Presentation;
using UnityEngine;

namespace Rts.Tests.EditMode
{
    public sealed class HumanOrderDisplayEditModeTests
    {
        [Test]
        public void BuildingHealthTextUsesTheRequestedJapaneseFormat()
        {
            Assert.That(BuildingHealthDisplayRules.Format(1234, 3000), Is.EqualTo("体力 1234 / 3000"));
        }

        [Test]
        public void BuildingHealthTextExpiresAtSixSeconds()
        {
            Assert.That(BuildingHealthDisplayRules.IsVisible(105.99f, 100f), Is.True);
            Assert.That(BuildingHealthDisplayRules.IsVisible(106f, 100f), Is.False);
            Assert.That(BuildingHealthDisplayRules.IsVisible(106f, -1f), Is.False);
        }

        [Test]
        public void StructureNumbersMergeForHalfASecondButUnitNumbersKeepTheOldWindow()
        {
            var feedback = new CombatFeedback();
            feedback.RegisterStructureHit(1UL << 63 | 4, Vector3.zero, 10, false);
            feedback.Advance(0.49f);
            feedback.RegisterStructureHit(1UL << 63 | 4, Vector3.zero, 5, false);
            Assert.That(feedback.Numbers.Count, Is.EqualTo(1));
            Assert.That(feedback.Numbers[0].Amount, Is.EqualTo(15));
            feedback.Advance(0.02f);
            feedback.RegisterStructureHit(1UL << 63 | 4, Vector3.zero, 3, false);
            Assert.That(feedback.Numbers.Count, Is.EqualTo(2));
        }

        [Test]
        public void VillagerMatchingHidesTheMissingMiddleWithoutMovingTheOtherDolls()
        {
            var previous = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(20f, 0f, 0f) };
            var current = new[] { new Vector3(0.1f, 0f, 0f), new Vector3(19.9f, 0f, 0f) };
            var matches = EnemyVillagerVisualRules.Assign(previous, current, 2f);
            Assert.That(matches, Is.EqualTo(new[] { 0, 2 }));
        }
    }
}
