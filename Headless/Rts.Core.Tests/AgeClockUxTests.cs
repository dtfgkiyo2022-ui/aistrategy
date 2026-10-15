using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AgeClockUxTests
    {
        private static ScenarioDefinition Scenario()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.AgeClock = true;
            scenario.Economy.AgeClockUx = true;
            scenario.Economy.AgeClockTicks1 = 8;
            scenario.Economy.AgeClockTicks2 = 20;
            scenario.Economy.AgeClockTicks3 = 40;
            scenario.Economy.AgeClockUxChoiceTicks = 5;
            scenario.Economy.StartFood = 1000;
            scenario.Economy.StartWood = 1000;
            return scenario;
        }

        [Test]
        public void ExtensionRoundTripsFlagAndChoiceWindow()
        {
            var scenario = Scenario();
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);

            Assert.That(decoded.Economy.AgeClockUx, Is.True);
            Assert.That(decoded.Economy.AgeClockUxChoiceTicks, Is.EqualTo(5));
            Assert.That(decoded.Extensions.Any(x => x.Id == 44), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void TurningTheUxFlagOffKeepsTheAgeClockBytesStable()
        {
            var withIgnoredUxValue = Scenario();
            withIgnoredUxValue.Economy.AgeClockUx = false;
            var ordinaryAgeClock = Scenario();
            ordinaryAgeClock.Economy.AgeClockUx = false;
            ordinaryAgeClock.Economy.AgeClockUxChoiceTicks = 600;
            Assert.That(ScenarioBinary.Encode(withIgnoredUxValue), Is.EqualTo(ScenarioBinary.Encode(ordinaryAgeClock)));
        }

        [Test]
        public void PrimitiveChoiceReservesWithoutPayingAndAdvanceNowPaysDiscountedPriceImmediately()
        {
            var sim = new Battle(Scenario());
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 2, CivKind.Agrarian));
            gateway.Step();

            var reserved = sim.Capture(1).Economy;
            Assert.That(reserved.Age, Is.EqualTo(0));
            Assert.That(reserved.Civ, Is.EqualTo(CivKind.Primitive));
            Assert.That(reserved.ReservedCiv, Is.EqualTo(CivKind.Agrarian));
            Assert.That(reserved.Food, Is.EqualTo(1000));
            Assert.That(reserved.Wood, Is.EqualTo(1000));

            gateway.SubmitEconomy(EconomyCommand.AdvanceNow(1, 3, CivKind.Agrarian));
            gateway.Step();
            var advanced = sim.Capture(1).Economy;
            Assert.That(advanced.Age, Is.EqualTo(1));
            Assert.That(advanced.Civ, Is.EqualTo(CivKind.Agrarian));
            Assert.That(advanced.AdvanceRemaining, Is.EqualTo(0));
            Assert.That(advanced.ReservedCiv, Is.EqualTo(CivKind.Primitive));
            Assert.That(advanced.Food, Is.LessThan(1000));
            Assert.That(advanced.Wood, Is.LessThan(1000));
        }

        [Test]
        public void AutoOffWaitsForChoiceWindowThenChoosesAutomatically()
        {
            var scenario = Scenario();
            scenario.Economy.StartFood = 0;
            scenario.Economy.StartWood = 0;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));

            for (int i = 0; i < 8; i++) gateway.Step();
            var waiting = sim.Capture(1).Economy;
            Assert.That(waiting.Age, Is.EqualTo(0));
            Assert.That(waiting.CivChoiceDeadlineTick, Is.EqualTo(13));

            for (int i = 0; i < 4; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(0));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(1));
        }

        [Test]
        public void ReservationDuringChoiceWindowCompletesImmediately()
        {
            var scenario = Scenario();
            scenario.Economy.StartFood = 0;
            scenario.Economy.StartWood = 0;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            for (int i = 0; i < 8; i++) gateway.Step();

            gateway.SubmitEconomy(EconomyCommand.Advance(1, 2, CivKind.Agrarian));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(1));
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Agrarian));
        }
    }
}
