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
            // Per-soldier marks (records made before chapter 85.6).
            var scenario = LiveGameRules.Create(5, true, true, false, false);
            scenario.Economy.HunterMarksShared = false;
            var battle = new Battle(scenario);
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
            // Per-soldier marks (records made before chapter 85.6).
            var scenario = LiveGameRules.Create(6, true, true, false, false);
            scenario.Economy.HunterMarksShared = false;
            var battle = new Battle(scenario);
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
        [Test]
        public void Extension135RoundTripsAndOffOmitsIt()
        {
            var on = LiveGameRules.Create(1, true, true, false, false);
            Assert.That(on.Economy.HunterMarksShared, Is.True);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(on)).Economy.HunterMarksShared, Is.True);
            on.Economy.HunterMarksShared = false;
            var off = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
            Assert.That(off.Economy.HunterMarksShared, Is.False);
            Assert.That(off.Extensions.Any(x => x.Id == 135), Is.False);
            var gearOff = LiveGameRules.Create(1, true, true, false, false, hunterGearOff: true);
            Assert.That(gearOff.Economy.HunterMarksShared, Is.False);
        }

        [Test]
        public void SharedMarksGiveEveryHunterTheFactionTier()
        {
            var battle = new Battle(LiveGameRules.Create(5, true, true, false, false));
            int index = FirstSoldierOf(battle, 1U);
            SetSoldier(battle, index, "HunterEquipped", true);
            var damage = typeof(Battle).GetMethod("HunterGearNeutralDamage", Private);
            Assert.That(damage.Invoke(battle, new object[] { index, 100 }), Is.EqualTo(150));
            // The soldier's own count no longer matters; the faction's marks set the tier.
            SetSoldier(battle, index, "HunterMarks", 15);
            Assert.That(damage.Invoke(battle, new object[] { index, 100 }), Is.EqualTo(150));
            SetEconomy(battle, 0, "HunterMarks", 5);
            Assert.That(damage.Invoke(battle, new object[] { index, 100 }), Is.EqualTo(175));
            SetEconomy(battle, 0, "HunterMarks", 15);
            Assert.That(damage.Invoke(battle, new object[] { index, 100 }), Is.EqualTo(200));
        }

        [Test]
        public void SharedMarksAddOneMarkPerBeastAndHealEveryParticipant()
        {
            var battle = new Battle(LiveGameRules.Create(6, true, true, false, false));
            object world = World(battle);
            Array neutrals = (Array)world.GetType().GetField("Neutrals", Private).GetValue(world);
            object neutral = neutrals.GetValue(0);
            object position = neutral.GetType().GetField("Position", Private).GetValue(neutral);
            uint campId = (uint)neutral.GetType().GetField("CampId", Private).GetValue(neutral);
            neutral.GetType().GetField("LastHitFaction", Private).SetValue(neutral, 1U);
            neutrals.SetValue(neutral, 0);
            var hunters = SoldiersOf(battle, 1U).Take(2).ToArray();
            Assert.That(hunters.Length, Is.EqualTo(2));
            var halfHp = new int[2];
            for (int h = 0; h < 2; h++)
            {
                int i = hunters[h];
                SetSoldier(battle, i, "HunterEquipped", true);
                SetSoldier(battle, i, "Position", position);
                SetSoldier(battle, i, "HunterLastCampId", campId);
                SetSoldier(battle, i, "HunterLastCampTick", 0L);
                halfHp[h] = MaxHp(battle, i) / 2;
                SetSoldier(battle, i, "Hp", halfHp[h]);
            }
            typeof(Battle).GetMethod("ResolveHunterNeutralDeath", Private).Invoke(battle, new object[] { 0 });
            Assert.That(GetEconomy(battle, 0, "HunterMarks"), Is.EqualTo(1), "one mark per beast, not one per hunter");
            for (int h = 0; h < 2; h++)
            {
                Assert.That(GetSoldier(battle, hunters[h], "HunterMarks"), Is.EqualTo(0));
                Assert.That((int)GetSoldier(battle, hunters[h], "Hp"), Is.GreaterThan(halfHp[h]));
            }
        }

        [Test]
        public void SharedMarksOutliveADeadHunter()
        {
            var battle = new Battle(LiveGameRules.Create(7, true, true, false, false));
            var soldiers = SoldiersOf(battle, 1U).Take(2).ToArray();
            SetEconomy(battle, 0, "HunterMarks", 15);
            SetSoldier(battle, soldiers[0], "HunterEquipped", true);
            SetSoldier(battle, soldiers[0], "Alive", false);
            typeof(Battle).GetMethod("AddHunterGearToSoldier", Private).Invoke(battle, new object[] { soldiers[1] });
            var damage = typeof(Battle).GetMethod("HunterGearNeutralDamage", Private);
            Assert.That(damage.Invoke(battle, new object[] { soldiers[1], 100 }), Is.EqualTo(200));
            Assert.That(GetEconomy(battle, 0, "HunterMarks"), Is.EqualTo(15));
        }

        private static object World(Battle battle) => typeof(Battle).GetField("world", Private).GetValue(battle);

        private static Array Soldiers(Battle battle)
        {
            object world = World(battle);
            return (Array)world.GetType().GetField("Soldiers", Private).GetValue(world);
        }

        private static System.Collections.Generic.IEnumerable<int> SoldiersOf(Battle battle, uint faction)
        {
            Array soldiers = Soldiers(battle);
            for (int i = 0; i < soldiers.Length; i++)
            {
                object soldier = soldiers.GetValue(i);
                object initial = soldier.GetType().GetField("Initial", Private).GetValue(soldier);
                if (initial == null) continue;
                if ((uint)initial.GetType().GetField("FactionId", Private).GetValue(initial) == faction
                    && (bool)soldier.GetType().GetField("Alive", Private).GetValue(soldier)) yield return i;
            }
        }

        private static int FirstSoldierOf(Battle battle, uint faction) => SoldiersOf(battle, faction).First();

        private static object GetSoldier(Battle battle, int index, string field)
        {
            object soldier = Soldiers(battle).GetValue(index);
            return soldier.GetType().GetField(field, Private).GetValue(soldier);
        }

        private static void SetSoldier(Battle battle, int index, string field, object value)
        {
            Array soldiers = Soldiers(battle);
            object soldier = soldiers.GetValue(index);
            soldier.GetType().GetField(field, Private).SetValue(soldier, value);
            soldiers.SetValue(soldier, index);
        }

        private static int MaxHp(Battle battle, int index)
        {
            object parameters = GetSoldier(battle, index, "Parameters");
            return (int)parameters.GetType().GetField("Hp", Private).GetValue(parameters);
        }

        private static int GetEconomy(Battle battle, int index, string field)
        {
            object world = World(battle);
            Array economies = (Array)world.GetType().GetField("Economies", Private).GetValue(world);
            object economy = economies.GetValue(index);
            return (int)economy.GetType().GetField(field, Private).GetValue(economy);
        }

        private static void SetEconomy(Battle battle, int index, string field, int value)
        {
            object world = World(battle);
            Array economies = (Array)world.GetType().GetField("Economies", Private).GetValue(world);
            object economy = economies.GetValue(index);
            economy.GetType().GetField(field, Private).SetValue(economy, value);
            economies.SetValue(economy, index);
        }
    }
}
