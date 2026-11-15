using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class WardsTests
    {
        [Test]
        public void WardsExtension117RoundTripsAndOffOmitsIt()
        {
            var on = LiveGameRules.Create(1, true, true, false, false);
            on.Extensions = Array.Empty<ScenarioExtensionData>();
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
            var extension = decoded.Extensions.Single(x => x.Id == 117);
            Assert.That(decoded.Economy.Wards, Is.True);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(13 * sizeof(int)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(on)));

            var off = LiveGameRules.Create(1, true, true, false, false, wardsOff: true);
            off.Extensions = Array.Empty<ScenarioExtensionData>();
            var offDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(off));
            Assert.That(offDecoded.Economy.Wards, Is.False);
            Assert.That(offDecoded.Extensions.Any(x => x.Id == 117), Is.False);
        }

        [Test]
        public void WardsArePublishedInFactionFrameAndOwnEnemyLists()
        {
            var observation = new FactionObservation(1, 0, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>(), wards: new[] {
                    new WardView(1, 1, WardKind.Hidden, new SimPoint(Fix64.FromInt(2), Fix64.FromInt(2)), 30, 0, 10, true) });
            var frame = new FactionFrame(0, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(),
                Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()), default,
                ownWards: observation.Wards, visibleEnemyWards: Array.Empty<WardView>());
            Assert.That(frame.Wards.Count, Is.EqualTo(1));
            Assert.That(frame.OwnWards[0].Kind, Is.EqualTo(WardKind.Hidden));
            Assert.That(frame.VisibleEnemyWards.Count, Is.Zero);
        }

        [Test]
        public void LargeLiveRulesEnableWardsByDefaultAndFlagDisablesThem()
        {
            Assert.That(LiveGameRules.Create(1, true, true, false, false).Economy.Wards, Is.True);
            Assert.That(LiveGameRules.Create(1, true, true, false, false, wardsOff: true).Economy.Wards, Is.False);
        }

        [Test]
        public void WardsOffPreservesTheExistingSimulationSurface()
        {
            var simulation = new Battle(LiveGameRules.Create(1, false, true, false, false, wardsOff: true));
            simulation.Step(1, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Wards, Is.Empty);
            Assert.That(simulation.Capture(1).OwnWards, Is.Empty);
            Assert.That(simulation.Capture(1).VisibleEnemyWards, Is.Empty);
        }

        [Test]
        public void WardMatchDoesNotIndexPastItsRuntimeWardStorage()
        {
            var simulation = new Battle(LiveGameRules.Create(1, true, true, false, false));
            for (long tick = 1; tick <= 10000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
        }
    }
}
