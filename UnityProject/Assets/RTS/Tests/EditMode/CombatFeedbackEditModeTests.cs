using NUnit.Framework;
using Rts.Presentation;
using UnityEngine;

namespace Rts.Tests.EditMode
{
    public sealed class CombatFeedbackEditModeTests
    {
        [Test]
        public void HitsOnOneUnitCloseTogetherAddToOneNumber()
        {
            var feedback = new CombatFeedback();
            feedback.RegisterHit(7, Vector3.zero, 8, false);
            feedback.Advance(0.1f);
            feedback.RegisterHit(7, Vector3.one, 5, false);
            Assert.AreEqual(1, feedback.Numbers.Count);
            Assert.AreEqual(13, feedback.Numbers[0].Amount);
            feedback.Advance(CombatFeedback.NumberMergeSeconds);
            feedback.RegisterHit(7, Vector3.one, 4, false);
            Assert.AreEqual(2, feedback.Numbers.Count, "a hit after the merge window starts a new number");
        }

        [Test]
        public void NumbersFadeAwayAndNeverPassTheLimit()
        {
            var feedback = new CombatFeedback();
            for (ulong key = 0; key < CombatFeedback.MaxNumbers + 10; key++) feedback.RegisterHit(key, Vector3.zero, 1, true);
            Assert.AreEqual(CombatFeedback.MaxNumbers, feedback.Numbers.Count);
            feedback.Advance(CombatFeedback.NumberSeconds);
            Assert.AreEqual(0, feedback.Numbers.Count);
        }

        [Test]
        public void AFightIsMarkedWithBothSidesLossesAndFadesWhenQuiet()
        {
            var feedback = new CombatFeedback();
            var at = new Vector3(100f, 0f, 100f);
            for (int i = 0; i < CombatFeedback.BattleMinimumEvents - 3; i++) feedback.RegisterHit((ulong)i, at, 5, i % 2 == 0);
            feedback.RegisterDeath(at, true);
            feedback.RegisterDeath(at, false);
            Assert.AreEqual(0, feedback.MarkedBattles().Count, "a skirmish under the minimum is not marked");
            feedback.RegisterDeath(at + Vector3.right, false);
            var marked = feedback.MarkedBattles();
            Assert.AreEqual(1, marked.Count);
            Assert.AreEqual(1, marked[0].OwnLost);
            Assert.AreEqual(2, marked[0].EnemyLost);
            feedback.Advance(CombatFeedback.BattleQuietSeconds);
            Assert.AreEqual(0, feedback.MarkedBattles().Count);
        }

        [Test]
        public void ZeroOrNegativeDamageIsNotAHit()
        {
            var feedback = new CombatFeedback();
            feedback.RegisterHit(1, Vector3.zero, 0, false);
            feedback.RegisterHit(1, Vector3.zero, -3, false);
            Assert.AreEqual(0, feedback.Numbers.Count);
        }

        [Test]
        public void BattleCuesHaveStandInSoundsAndOthersDoNot()
        {
            Assert.Greater(ProceduralSounds.For(SoundCue.MeleeHit).Length, 0);
            Assert.Greater(ProceduralSounds.For(SoundCue.UnitDeath).Length, 0);
            Assert.Greater(ProceduralSounds.For(SoundCue.ArrowHit).Length, 0);
            Assert.AreEqual(0, ProceduralSounds.For(SoundCue.Click).Length);
            Assert.AreSame(ProceduralSounds.For(SoundCue.MeleeHit), ProceduralSounds.For(SoundCue.MeleeHit), "made once and reused");
        }
    }
}
