using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AugmentTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState World(Battle battle)
            => (WorldState)typeof(Battle).GetField("world", Private).GetValue(battle);

        private static object Invoke(Battle battle, string method, params object[] args)
            => typeof(Battle).GetMethod(method, Private).Invoke(battle, args);

        private static Battle WithChoice(out WorldState world)
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            world = World(battle);
            Invoke(battle, "BeginAugmentChoice", 1U, AugmentGrade.Silver, false);
            return battle;
        }

        [Test]
        public void PoolHasExactlyTwentyAugmentsAndTickZeroHasNoChoice()
        {
            Assert.That(Enum.GetValues(typeof(AugmentKind)).Length, Is.EqualTo(21));
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            Assert.That(battle.Capture(1).AugmentGrade, Is.EqualTo(AugmentGrade.None));
            Assert.That(battle.Capture(1).AugmentChoices.All(x => x == AugmentKind.None), Is.True);
        }

        [Test]
        public void SameSeedProducesSameGradeAndCandidatesForBothRuns()
        {
            var first = WithChoice(out var firstWorld);
            var second = WithChoice(out var secondWorld);
            Assert.That(firstWorld.Factions[0].AugmentGrade, Is.EqualTo(secondWorld.Factions[0].AugmentGrade));
            Assert.That(firstWorld.Factions[0].AugmentChoices, Is.EqualTo(secondWorld.Factions[0].AugmentChoices));
        }

        [Test]
        public void RerollIsAcceptedOnlyOnceAndChooseCommandTakesCard()
        {
            var battle = WithChoice(out var world);
            var before = world.Factions[0].AugmentChoices.ToArray();
            var reroll = (bool)Invoke(battle, "ApplyRerollAugment", EconomyCommand.RerollAugment(1, 1));
            Assert.That(reroll, Is.True);
            Assert.That(world.Factions[0].AugmentRerollsRemaining, Is.Zero);
            Assert.That((bool)Invoke(battle, "ApplyRerollAugment", EconomyCommand.RerollAugment(1, 2)), Is.False);
            Assert.That(world.Factions[0].AugmentChoices, Is.Not.EqualTo(before));
            Assert.That((bool)Invoke(battle, "ApplyChooseAugment", EconomyCommand.ChooseAugment(1, 3, 0)), Is.True);
            Assert.That(world.Factions[0].AugmentOwnedCount, Is.EqualTo(1));
            Assert.That(world.Factions[0].AugmentChoices.All(x => x == AugmentKind.None), Is.True);
        }

        [Test]
        public void AutomaticEconomyPicksAfterThreeHundredTicks()
        {
            var battle = WithChoice(out var world);
            world.Economies[0].AutoOff = false;
            Assert.That(world.Factions[0].AugmentOwnedCount, Is.Zero);
            world.Tick = 299;
            Invoke(battle, "AugmentThink");
            Assert.That(world.Factions[0].AugmentOwnedCount, Is.Zero);
            world.Tick = 300;
            Invoke(battle, "AugmentThink");
            Assert.That(world.Factions[0].AugmentOwnedCount, Is.EqualTo(1));
        }

        [Test]
        public void AutomaticChoiceWaitsForTheDeadline()
        {
            var battle = WithChoice(out var world);
            world.Economies[0].AutoOff = true;
            Assert.That(world.Factions[0].AugmentOwnedCount, Is.Zero);
            world.Tick = 1199;
            Invoke(battle, "AugmentThink");
            Assert.That(world.Factions[0].AugmentOwnedCount, Is.Zero);
            world.Tick = 1200;
            Invoke(battle, "AugmentThink");
            Assert.That(world.Factions[0].AugmentOwnedCount, Is.EqualTo(1));
        }

        [Test]
        public void AugmentsExtensionRoundTripsByteForByte()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 123), Is.True);
            Assert.That(decoded.Economy.Augments, Is.True);
            Assert.That(decoded.Extensions.Single(x => x.Id == 123).Data, Is.EqualTo(new byte[] { 1 }));
        }
    }
}
