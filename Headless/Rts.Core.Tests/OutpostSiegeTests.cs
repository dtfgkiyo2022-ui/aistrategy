using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    [TestFixture]
    public sealed class OutpostSiegeTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = LiveGameRules.Create(68112UL, true, true, false, false);
            scenario.Economy.OutpostSiege = enabled;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            return scenario;
        }

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Private).GetValue(simulation);

        private static void Apply(Battle simulation)
            => typeof(Battle).GetMethod("ApplyOutpostSiege", Private).Invoke(simulation, null);

        [TestCase(1, 30)]
        [TestCase(2, 60)]
        public void HeldOutpostsDamageTheEnemyCoreAtTheThreeHundredTick(int held, int damage)
        {
            var simulation = new Battle(Scenario(true));
            var world = State(simulation);
            Assert.That(world.Outposts.Length, Is.GreaterThanOrEqualTo(held));
            for (int i = 0; i < world.Outposts.Length; i++) world.Outposts[i].OwnerFactionId = i < held ? 1U : 0U;
            world.Cores[1].Hp = 1000;
            world.Tick = 300;

            Apply(simulation);

            Assert.That(world.Cores[1].Hp, Is.EqualTo(1000 - damage));
        }

        [Test]
        public void NoHeldOutpostDoesNotDamageAndFlagOffDoesNotDamage()
        {
            var on = new Battle(Scenario(true));
            var onWorld = State(on);
            for (int i = 0; i < onWorld.Outposts.Length; i++) onWorld.Outposts[i].OwnerFactionId = 0;
            onWorld.Cores[1].Hp = 1000;
            onWorld.Tick = 300;
            Apply(on);
            Assert.That(onWorld.Cores[1].Hp, Is.EqualTo(1000));

            var off = new Battle(Scenario(false));
            var offWorld = State(off);
            for (int i = 0; i < offWorld.Outposts.Length; i++) offWorld.Outposts[i].OwnerFactionId = 1;
            offWorld.Cores[1].Hp = 1000;
            offWorld.Tick = 300;
            Apply(off);
            Assert.That(offWorld.Cores[1].Hp, Is.EqualTo(1000));
        }

        [Test]
        public void OutpostDamageUsesTheNormalCoreVictoryResult()
        {
            var simulation = new Battle(Scenario(true));
            var world = State(simulation);
            world.Outposts[0].OwnerFactionId = 1;
            for (int i = 1; i < world.Outposts.Length; i++) world.Outposts[i].OwnerFactionId = 0;
            world.Cores[1].Hp = EconomyRules.OutpostSiegeDamage;
            world.Tick = 300;
            Apply(simulation);
            typeof(Battle).GetMethod("ResolveVictory", Private).Invoke(simulation, null);

            Assert.That(world.Result.HasEnded, Is.True);
            Assert.That(world.Result.WinnerFactionId, Is.EqualTo(1U));
            Assert.That(world.Result.IsDraw, Is.False);
        }

        [Test]
        public void Extension112RoundTripsAndFlagOffWritesNoBits()
        {
            var source = MapGenerator.GenerateTerrain(68113UL);
            source.Economy.OutpostSiege = true;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 112);

            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(BitConverter.ToInt32(extension.Data, 0), Is.EqualTo(1));
            Assert.That(decoded.Economy.OutpostSiege, Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            source.Economy.OutpostSiege = false;
            Assert.That(ScenarioBinary.Encode(source).Length, Is.LessThan(bytes.Length));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(source)).Extensions.Any(value => value.Id == 112), Is.False);
        }

        [Test]
        public void FlagOffMatchesTheSameScenarioBeforeTheFeatureWasAdded()
        {
            var baseline = LiveGameRules.Create(68114UL, true, true, false, false);
            baseline.Economy.OutpostSiege = false;
            var explicitOff = LiveGameRules.Create(68114UL, true, true, false, false, false, false, false, false, false, false, true);
            Assert.That(ScenarioBinary.Encode(explicitOff), Is.EqualTo(ScenarioBinary.Encode(baseline)));

            var left = new Battle(baseline);
            var right = new Battle(explicitOff);
            for (long tick = 1; tick <= 32; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void AiFocusesAnObservedEnemyOwnedOutpost()
        {
            var simulation = new Battle(Scenario(true));
            var world = State(simulation);
            var observed = (FactionObservation)typeof(Battle).GetMethod("ObserveForDecision", Private)
                .Invoke(simulation, new object[] { 1U });
            var army = observed.OwnArmies.FirstOrDefault(value => value.Kind == UnitKind.Infantry && value.AliveCount > 0);
            Assert.That(army.Id, Is.GreaterThan(0U));
            var outpost = world.Outposts[0].Definition;
            var objective = new KnownObjective(GoalKind.Outpost, outpost.Id, outpost.Position, true, 2, false, 0, 0);
            var observation = new FactionObservation(1, 0, new[] { army }, Array.Empty<VisibleEnemy>(),
                Array.Empty<EnemyContact>(), new[] { objective });
            var input = new ArmyDecisionInput(army, default(PolicyView), false,
                new ArmyDecisionMemory(AssignmentKind.Advance, default(PolicyGoal), false, 0, 0),
                new[] { new ObjectiveRoute(new PolicyGoal(GoalKind.Outpost, outpost.Id, default(SimPoint)), 10) });
            var allocations = new[] { new ArmyDecisionMemory(AssignmentKind.Advance, default(PolicyGoal), false, 0, 0) };

            typeof(Battle).GetMethod("ApplyOutpostSiegeFocus", Private)
                .Invoke(simulation, new object[] { 1U, observation, new[] { input }, allocations });

            Assert.That(allocations[0].Assignment, Is.EqualTo(AssignmentKind.Advance));
            Assert.That(allocations[0].Goal.Kind, Is.EqualTo(GoalKind.Outpost));
            Assert.That(allocations[0].Goal.Id, Is.EqualTo(outpost.Id));
        }
    }
}
