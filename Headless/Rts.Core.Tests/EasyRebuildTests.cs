using System;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class EasyRebuildTests
    {
        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(4917UL, false, true);
            scenario.Economy.Industry = true;
            scenario.Economy.EasyRebuild = enabled;
            scenario.Economy.MoveBuildingPauseTicks = 60;
            scenario.Economy.StartWood = 1000;
            return scenario;
        }

        [Test]
        public void Extension49RoundTripsAndOffAddsNoExtension()
        {
            var off = Scenario(false);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(Scenario(false))));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 49), Is.False);

            var on = Scenario(true);
            on.Economy.MoveBuildingPauseTicks = 73;
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 49).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.EasyRebuild, Is.True);
            Assert.That(decoded.Economy.MoveBuildingPauseTicks, Is.EqualTo(73));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void MoveAndRemoveCommandsAreNoOpsWhenFlagIsOff()
        {
            var scenario = Scenario(false);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.MoveBuilding(1, 1, 1, 20, Facing.East));
            gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, 2, 1));
            gateway.Step();
            var control = new Battle(Scenario(false));
            var controlGateway = new CommandGateway(control);
            controlGateway.SubmitEconomy(EconomyCommand.MoveBuilding(1, 1, 1, 20, Facing.East));
            controlGateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, 2, 1));
            controlGateway.Step();
            Assert.That(simulation.CaptureDiagnostic().CanonicalState, Is.EqualTo(control.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void MoveCommandRoundTripsThroughInputBinary()
        {
            var input = new ScheduledInput(7, 0, 1, EconomyCommand.MoveBuilding(1, 4, 12, 345, Facing.West));
            var bytes = InputBinary.Encode(input);
            Assert.That(InputBinary.Encode(InputBinary.Decode(bytes)), Is.EqualTo(bytes));
            Assert.That(InputBinary.Decode(bytes).Economy.Kind, Is.EqualTo(EconomyCommandKind.MoveBuilding));
        }
    }
}
