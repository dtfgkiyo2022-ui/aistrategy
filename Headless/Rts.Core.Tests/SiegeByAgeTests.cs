using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    [TestFixture]
    public sealed class SiegeByAgeTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void SiegeByAgeExtensionIdSeventeenRoundTripsItsFlagAndPermilles()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.SiegeByAge = true;
            scenario.Economy.SiegeAge0Permille = 450;
            scenario.Economy.SiegeAge1Permille = 1100;
            scenario.Economy.SiegeAge2Permille = 2100;
            scenario.Economy.SiegeAge3Permille = 3900;

            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 17);

            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.SiegeByAge, Is.True);
            Assert.That(decoded.Economy.SiegeAge0Permille, Is.EqualTo(450));
            Assert.That(decoded.Economy.SiegeAge1Permille, Is.EqualTo(1100));
            Assert.That(decoded.Economy.SiegeAge2Permille, Is.EqualTo(2100));
            Assert.That(decoded.Economy.SiegeAge3Permille, Is.EqualTo(3900));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void SiegeByAgeScalesOnlyCoreAndBuildingDamageByTheAttackerAge()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.SiegeByAge = true;
            var method = typeof(Battle).GetMethod("SiegeDamage", Private);
            Assert.That(method, Is.Not.Null);

            int baseDamage = scenario.UnitParameters.First(p => p.Kind == UnitKind.Infantry).Damage;
            // Base damage 10 with the played multipliers 100, 250, 600 and 4000 permille (rounded down, never under 1).
            int[] expected = { 1, 2, 6, 40 };
            for (byte age = 0; age <= 3; age++)
            {
                var sim = new Battle(scenario);
                SetAge(sim, 1, age);
                object soldier = GetWorldArray(sim, "Soldiers").GetValue(0);
                int damage = (int)method.Invoke(sim, new[] { soldier });
                Assert.That(damage, Is.EqualTo(expected[age]), "age " + age);
                Assert.That(scenario.UnitParameters.First(p => p.Kind == UnitKind.Infantry).Damage, Is.EqualTo(baseDamage));
            }
        }

        [Test]
        public void SiegeByAgeOffKeepsTheHistoricalSiegeDamage()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.SiegeByAge = false;
            var sim = new Battle(scenario);
            object soldier = GetWorldArray(sim, "Soldiers").GetValue(0);
            int damage = (int)typeof(Battle).GetMethod("SiegeDamage", Private).Invoke(sim, new[] { soldier });
            Assert.That(damage, Is.EqualTo(scenario.UnitParameters.First(p => p.Kind == UnitKind.Infantry).Damage));
        }

        private static Array GetWorldArray(Battle sim, string name)
        {
            object world = typeof(Battle).GetField("world", Private).GetValue(sim);
            return (Array)world.GetType().GetField(name, Private).GetValue(world);
        }

        private static void SetAge(Battle sim, uint faction, byte age)
        {
            object world = typeof(Battle).GetField("world", Private).GetValue(sim);
            Array economies = (Array)world.GetType().GetField("Economies", Private).GetValue(world);
            object economy = economies.GetValue(faction - 1);
            economy.GetType().GetField("Age", Private).SetValue(economy, age);
            economies.SetValue(economy, faction - 1);
        }
    }
}
