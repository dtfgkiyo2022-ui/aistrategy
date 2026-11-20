using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class HunterGearTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void Extension133RoundTripsAndOffOmitsIt()
        {
            var on = LiveGameRules.Create(1, true, true, false, false);
            Assert.That(on.Economy.HunterGear, Is.True);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
            Assert.That(decoded.Economy.HunterGear, Is.True);
            Assert.That(ScenarioBinary.Encode(on), Is.Not.Empty);
            var off = LiveGameRules.Create(1, true, true, false, false, hunterGearOff: true);
            Assert.That(off.Economy.HunterGear, Is.False);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 133), Is.False);
        }

        [Test]
        public void Extension133EncodesTerrainSeedTwo()
        {
            Assert.That(() => ScenarioBinary.Encode(LiveGameRules.Create(2, true, true, false, false)), Throws.Nothing);
        }

        [Test]
        public void HideTableAndTierThresholdsAreDeterministic()
        {
            var battle = new Battle(LiveGameRules.Create(2, true, true, false, false));
            object Hide(NeutralKind kind) => typeof(Battle).GetMethod("HunterHideReward", Private).Invoke(battle, new object[] { kind });
            Assert.That(Hide(NeutralKind.WolfPack), Is.EqualTo(2));
            Assert.That(Hide(NeutralKind.RockBeast), Is.EqualTo(3));
            Assert.That(Hide(NeutralKind.FlameHornBeast), Is.EqualTo(4));
            Assert.That(Hide(NeutralKind.SiegeBeast), Is.EqualTo(6));
            Assert.That(Hide(NeutralKind.Dragon), Is.EqualTo(8));
            Assert.That(Hide(NeutralKind.GreatLord), Is.EqualTo(12));
            var tier = typeof(Battle).GetMethod("HunterTierFor", Private);
            Assert.That(tier.Invoke(battle, new object[] { 0 }), Is.EqualTo((byte)1));
            Assert.That(tier.Invoke(battle, new object[] { 4 }), Is.EqualTo((byte)1));
            Assert.That(tier.Invoke(battle, new object[] { 5 }), Is.EqualTo((byte)2));
            Assert.That(tier.Invoke(battle, new object[] { 14 }), Is.EqualTo((byte)2));
            Assert.That(tier.Invoke(battle, new object[] { 15 }), Is.EqualTo((byte)3));
        }

        [Test]
        public void ThreeHidesQualifyAnEligibleTrainingUnit()
        {
            var battle = new Battle(LiveGameRules.Create(4, true, true, false, false));
            object world = typeof(Battle).GetField("world", Private).GetValue(battle);
            var economiesField = world.GetType().GetField("Economies", Private);
            Array economies = (Array)economiesField.GetValue(world);
            object economy = economies.GetValue(0);
            economy.GetType().GetField("Hides", Private).SetValue(economy, 3);
            economies.SetValue(economy, 0);
            var shouldQueue = typeof(Battle).GetMethod("ShouldQueueHunterGear", Private);
            Assert.That(shouldQueue.Invoke(battle, new object[] { 1U, UnitKind.Infantry }), Is.EqualTo(true));
        }

        [Test]
        public void MarksFiveAndFifteenChangeNeutralDamage()
        {
            var battle = new Battle(LiveGameRules.Create(5, true, true, false, false));
            object world = typeof(Battle).GetField("world", Private).GetValue(battle);
            var soldiersField = world.GetType().GetField("Soldiers", Private);
            Array soldiers = (Array)soldiersField.GetValue(world);
            object soldier = soldiers.GetValue(0);
            soldier.GetType().GetField("HunterEquipped", Private).SetValue(soldier, true);
            soldiers.SetValue(soldier, 0);
            var damage = typeof(Battle).GetMethod("HunterGearNeutralDamage", Private);
            Assert.That(damage.Invoke(battle, new object[] { 0, 100 }), Is.EqualTo(150));
            soldier = soldiers.GetValue(0);
            soldier.GetType().GetField("HunterMarks", Private).SetValue(soldier, 5);
            soldiers.SetValue(soldier, 0);
            Assert.That(damage.Invoke(battle, new object[] { 0, 100 }), Is.EqualTo(175));
            soldier = soldiers.GetValue(0);
            soldier.GetType().GetField("HunterMarks", Private).SetValue(soldier, 15);
            soldiers.SetValue(soldier, 0);
            Assert.That(damage.Invoke(battle, new object[] { 0, 100 }), Is.EqualTo(200));
        }

        [Test]
        public void TierOneParticipantGetsMarkAndTenPercentHealOnKill()
        {
            var battle = new Battle(LiveGameRules.Create(6, true, true, false, false));
            object world = typeof(Battle).GetField("world", Private).GetValue(battle);
            var soldiersField = world.GetType().GetField("Soldiers", Private);
            Array soldiers = (Array)soldiersField.GetValue(world);
            int soldierIndex = -1;
            for (int i = 0; i < soldiers.Length; i++)
            {
                object candidate = soldiers.GetValue(i);
                object initial = candidate.GetType().GetField("Initial", Private).GetValue(candidate);
                if ((uint)initial.GetType().GetField("FactionId", Private).GetValue(initial) == 1U)
                {
                    soldierIndex = i;
                    break;
                }
            }
            Assert.That(soldierIndex, Is.GreaterThanOrEqualTo(0));
            var neutralsField = world.GetType().GetField("Neutrals", Private);
            Array neutrals = (Array)neutralsField.GetValue(world);
            object neutral = neutrals.GetValue(0);
            object position = neutral.GetType().GetField("Position", Private).GetValue(neutral);
            neutral.GetType().GetField("LastHitFaction", Private).SetValue(neutral, 1U);
            neutrals.SetValue(neutral, 0);
            object soldier = soldiers.GetValue(soldierIndex);
            soldier.GetType().GetField("HunterEquipped", Private).SetValue(soldier, true);
            soldier.GetType().GetField("HunterMarks", Private).SetValue(soldier, 0);
            soldier.GetType().GetField("Position", Private).SetValue(soldier, position);
            int maxHp = (int)soldier.GetType().GetField("Parameters", Private).GetValue(soldier).GetType().GetField("Hp", Private)
                .GetValue(soldier.GetType().GetField("Parameters", Private).GetValue(soldier));
            int beforeHp = maxHp / 2;
            soldier.GetType().GetField("Hp", Private).SetValue(soldier, beforeHp);
            // The soldier hit a beast of the same camp within the look-back window.
            soldier.GetType().GetField("HunterLastCampId", Private).SetValue(soldier, (uint)neutral.GetType().GetField("CampId", Private).GetValue(neutral));
            soldier.GetType().GetField("HunterLastCampTick", Private).SetValue(soldier, 0L);
            soldiers.SetValue(soldier, soldierIndex);
            typeof(Battle).GetMethod("ResolveHunterNeutralDeath", Private).Invoke(battle, new object[] { 0 });
            soldier = soldiers.GetValue(soldierIndex);
            Assert.That((int)soldier.GetType().GetField("HunterMarks", Private).GetValue(soldier), Is.EqualTo(1));
            Assert.That((int)soldier.GetType().GetField("Hp", Private).GetValue(soldier), Is.GreaterThan(beforeHp));
        }

        [Test]
        public void FlagOffKeepsTwoRunsAtTheSameStateHash()
        {
            var left = new Battle(LiveGameRules.Create(3, true, true, false, false, hunterGearOff: true));
            var right = new Battle(LiveGameRules.Create(3, true, true, false, false, hunterGearOff: true));
            for (long tick = 1; tick <= 120; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(Convert.ToHexString(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState)),
                    Is.EqualTo(Convert.ToHexString(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState))), "tick " + tick);
            }
        }
    }
}
