using System;
using System.Reflection;
using NUnit.Framework;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class SoldierRegenTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static WorldState World(Battle b) => (WorldState)typeof(Battle).GetField("world", Private).GetValue(b);
        private static void Regen(Battle b) => typeof(Battle).GetMethod("ApplySoldierRegen", Private).Invoke(b, null);

        [Test]
        public void OffRoundTripsWithoutExtensionAndDoesNotHeal()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.SoldierRegen = false;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.SoldierRegen, Is.False);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var battle = new Battle(decoded);
            var world = World(battle);
            world.Soldiers[0].Hp--;
            world.Tick = 200;
            Regen(battle);
            Assert.That(world.Soldiers[0].Hp, Is.EqualTo(world.Soldiers[0].Initial.Hp - 1));
        }

        [Test]
        public void OnHealsAfterDelayAtIntervalAndStopsAtMaximum()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.SoldierRegen = true;
            scenario.Economy.SoldierRegenDelayTicks = 200;
            scenario.Economy.SoldierRegenIntervalTicks = 20;
            scenario.Economy.SoldierRegenPermille = 20;
            var battle = new Battle(scenario);
            var world = World(battle);
            var soldier = world.Soldiers[0];
            soldier.Hp = soldier.Parameters.Hp - 10;
            soldier.LastDamagedTick = 0;
            soldier.LastAttackTick = 0;
            world.Soldiers[0] = soldier;
            world.Tick = 199; Regen(battle);
            Assert.That(world.Soldiers[0].Hp, Is.EqualTo(soldier.Hp));
            world.Tick = 200; Regen(battle);
            int first = world.Soldiers[0].Hp;
            Assert.That(first, Is.GreaterThan(soldier.Hp));
            world.Tick = 220; Regen(battle);
            Assert.That(world.Soldiers[0].Hp, Is.GreaterThanOrEqualTo(first));
            world.Soldiers[0].Hp = world.Soldiers[0].Parameters.Hp;
            world.Tick = 240; Regen(battle);
            Assert.That(world.Soldiers[0].Hp, Is.EqualTo(world.Soldiers[0].Parameters.Hp));
        }
    }
}
