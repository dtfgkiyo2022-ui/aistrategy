using NUnit.Framework;
using Rts.Presentation;
using System.Collections.Generic;

namespace Rts.Core.Tests
{
    public sealed class CombatAlertRulesTests
    {
        private static CombatAlertRules.HealthSample Own(uint id, float x, float z, int hp) => new CombatAlertRules.HealthSample(id, x, z, hp, true);
        [Test] public void TenMetersApartMergeIntoOneAlert()
        { var p = new List<CombatAlertRules.HealthSample> { Own(1, 0, 0, 10), Own(2, 4, 0, 10), Own(3, 8, 0, 10) }; var c = new List<CombatAlertRules.HealthSample> { Own(1, 0, 0, 9), Own(2, 4, 0, 9), Own(3, 8, 0, 9) }; Assert.That(CombatAlertRules.Detect(p, c, 1f), Has.Count.EqualTo(1)); }
        [Test] public void FiftyMetersApartStaySeparate()
        { var p = new List<CombatAlertRules.HealthSample> { Own(1, 0, 0, 10), Own(2, 50, 0, 10) }; var c = new List<CombatAlertRules.HealthSample> { Own(1, 0, 0, 9), Own(2, 50, 0, 9) }; Assert.That(CombatAlertRules.Detect(p, c, 1f), Has.Count.EqualTo(2)); }
        [Test] public void AlertExpiresAfterFiveSeconds()
        { var a = new List<CombatAlertRules.Alert> { new CombatAlertRules.Alert(0, 0, 1f, 1, 0, true) }; Assert.That(CombatAlertRules.Live(a, 6f), Is.Empty); }
        [Test] public void NorthEastDirectionIsJapaneseAndEnglish()
        { Assert.That(CombatAlertRules.Direction(0, 0, 2, 2, true), Is.EqualTo("北東")); Assert.That(CombatAlertRules.Direction(0, 0, 2, 2, false), Is.EqualTo("Northeast")); }
        [Test] public void MinimapCornersAndCentreMapExactly()
        { var bl = MinimapRules.ToMinimap(0, 0, 100, 50, 10, 20, 200, 100); var tr = MinimapRules.ToMinimap(100, 50, 100, 50, 10, 20, 200, 100); var mid = MinimapRules.ToMinimap(50, 25, 100, 50, 10, 20, 200, 100); Assert.That(bl.X, Is.EqualTo(10)); Assert.That(bl.Y, Is.EqualTo(20)); Assert.That(tr.X, Is.EqualTo(210)); Assert.That(tr.Y, Is.EqualTo(120)); Assert.That(mid.X, Is.EqualTo(110)); Assert.That(mid.Y, Is.EqualTo(70)); }
        [Test] public void FoggedEnemyIsNeverDrawable()
        { Assert.That(MinimapRules.CanDrawEnemy(false), Is.False); Assert.That(MinimapRules.CanDrawEnemy(true), Is.True); }
        [Test] public void SameFightOverManyFramesStaysOneLine()
        { var running = new List<CombatAlertRules.Alert>(); for (int f = 0; f < 20; f++) CombatAlertRules.Accumulate(running, new List<CombatAlertRules.Alert> { new CombatAlertRules.Alert(10f + f * 0.1f, 0f, f * 0.05f, 1, 0, true) }, f * 0.05f); Assert.That(running, Has.Count.EqualTo(1)); }
        [Test] public void NewFightGoesFirstAndOldFightsExpire()
        { var running = new List<CombatAlertRules.Alert>(); CombatAlertRules.Accumulate(running, new List<CombatAlertRules.Alert> { new CombatAlertRules.Alert(0f, 0f, 1f, 1, 0, true) }, 1f); CombatAlertRules.Accumulate(running, new List<CombatAlertRules.Alert> { new CombatAlertRules.Alert(100f, 0f, 2f, 0, 1, false) }, 2f); Assert.That(running[0].X, Is.EqualTo(100f)); CombatAlertRules.Accumulate(running, null, 6.5f); Assert.That(running, Has.Count.EqualTo(1)); }
        [Test] public void DirectionUsesEightSlices()
        { Assert.That(CombatAlertRules.Direction(0, 0, 1, 50, true), Is.EqualTo("北")); Assert.That(CombatAlertRules.Direction(0, 0, 50, -3, true), Is.EqualTo("東")); Assert.That(CombatAlertRules.Direction(0, 0, -30, -30, false), Is.EqualTo("Southwest")); }
        [Test] public void MinimapPointGoesBackToTheSameMapPoint()
        { var p = MinimapRules.ToMinimap(30, 40, 100, 50, 0, 0, 200, 100); var w = MinimapRules.ToWorld(p.X, p.Y, 100, 50, 0, 0, 200, 100); Assert.That(w.X, Is.EqualTo(30f).Within(0.001f)); Assert.That(w.Y, Is.EqualTo(40f).Within(0.001f)); }
    }
}
