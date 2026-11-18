using System;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
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

        [Test]
        public void AutomaticWorkerWalksAndPlacesWardOnlyAfterAgeOne()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            scenario.Economy.Ages = true;
            scenario.Economy.Wards = true;
            scenario.Economy.AgeClock = true;
            scenario.Economy.AgeClockTicks1 = 1;
            scenario.Economy.AgeClockTicks2 = 10000;
            scenario.Economy.AgeClockTicks3 = 20000;
            scenario.Economy.AgeClockTicks4 = 30000;
            scenario.Economy.WardHiddenWoodCost = 0;
            scenario.Economy.WardPlacementTicks = 2;
            scenario.Economy.WardHiddenDurationTicks = 10000;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            for (int tick = 0; tick < 1600 && simulation.WardPlacedCount(1, WardKind.Hidden) == 0; tick++) gateway.Step();
            Assert.That(simulation.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(1));
            Assert.That(simulation.WardPlacedCount(1, WardKind.Hidden), Is.GreaterThan(0));
            foreach (var ward in simulation.Capture(1).OwnWards)
                Assert.That(DistanceSquared(ward.Position, scenario.Cores[0].Position), Is.GreaterThan(8L * 8L * 65536L * 65536L));
        }

        [Test]
        public void AgeZeroDoesNotPlaceAutomaticWard()
        {
            var simulation = new Battle(LiveGameRules.Create(2, false, true, false, false));
            for (int tick = 0; tick < 300; tick++) simulation.Step(tick + 1, Array.Empty<ScheduledInput>());
            Assert.That(simulation.WardPlacedCount(1), Is.Zero);
            Assert.That(simulation.WardPlacedCount(2), Is.Zero);
        }

        [Test]
        public void NearbyUnitDestroysEnemyWardAndUpdatesFactionCounters()
        {
            var scenario = LiveGameRules.Create(3, false, true, false, false);
            scenario.Economy.Wards = true;
            var soldier = scenario.Soldiers.First(s => s.FactionId == 1 && s.Alive);
            scenario.Wards = new[] { new WardDefinition { Id = 1, FactionId = 2, Kind = WardKind.Revealing,
                Position = soldier.Position, Hp = 1, PlacedTick = 0, ExpiryTick = 0 } };
            scenario.Economy.WardHp = 1;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            for (int i = 0; i < 20; i++) gateway.Step();
            Assert.That(simulation.WardDestroyedByEnemyCount(2, WardKind.Revealing), Is.EqualTo(1));
            Assert.That(simulation.WardDestroyedCount(2, WardKind.Revealing), Is.EqualTo(1));
        }

        private static long DistanceSquared(SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            return checked(dx * dx + dz * dz);
        }
    }
}
