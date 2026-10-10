using System;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AgeClockTests
    {
        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        [Test]
        public void AgeClockExtensionRoundTripsItsFlagAndTicks()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.AgeClock = true;
            scenario.Economy.AgeClockTicks1 = 4800;
            scenario.Economy.AgeClockTicks2 = 10800;
            scenario.Economy.AgeClockTicks3 = 15600;

            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario));
            Assert.That(decoded.Economy.AgeClock, Is.True);
            Assert.That(decoded.Economy.AgeClockTicks1, Is.EqualTo(4800));
            Assert.That(decoded.Economy.AgeClockTicks2, Is.EqualTo(10800));
            Assert.That(decoded.Economy.AgeClockTicks3, Is.EqualTo(15600));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(scenario)));
        }

        [Test]
        public void AgeClockAdvancesAllThreeAgesWithoutResourcesOrConditions()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.AgeClock = true;
            scenario.Economy.AgeClockTicks1 = 20;
            scenario.Economy.AgeClockTicks2 = 40;
            scenario.Economy.AgeClockTicks3 = 60;
            scenario.Economy.StartFood = 0;
            scenario.Economy.StartWood = 0;

            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 65);

            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(3));
            Assert.That(sim.Capture(2).Economy.Age, Is.EqualTo(3));
        }

        [Test]
        public void AgeClockAllowsManualAdvanceAtDueTickWhenAutomaticEconomyIsOff()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.AgeClock = true;
            scenario.Economy.AgeClockTicks1 = 20;
            scenario.Economy.AgeClockTicks2 = 40;
            scenario.Economy.AgeClockTicks3 = 60;
            scenario.Economy.StartFood = 0;
            scenario.Economy.StartWood = 0;

            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            Steps(gateway, sim, 19);
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(0));

            gateway.SubmitEconomy(EconomyCommand.Advance(1, 3, CivKind.Agrarian));
            Steps(gateway, sim, 1);
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(1));
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Agrarian));
        }
    }
}
