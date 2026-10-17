using System;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BuildingReservationTests
    {
        [Test]
        public void DisabledRulesKeepTheHistoricalScenarioBytes()
        {
            var left = MapGenerator.Generate(701, true);
            var right = MapGenerator.Generate(701, true);
            right.Economy.BuildingReservations = false;
            Assert.That(ScenarioBinary.Encode(left), Is.EqualTo(ScenarioBinary.Encode(right)));
        }

        [Test]
        public void Extension51RoundTripsTheFlagAndDefaults()
        {
            var source = MapGenerator.Generate(702, true);
            source.Economy.BuildingReservations = true;
            source.Economy.ReservationLimit = 7;
            source.Economy.ReservationCheckTicks = 13;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.BuildingReservations, Is.True);
            Assert.That(decoded.Economy.ReservationLimit, Is.EqualTo(7));
            Assert.That(decoded.Economy.ReservationCheckTicks, Is.EqualTo(13));
            Assert.That(decoded.Extensions.Any(x => x.Id == 51), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void LiveRulesEnableReservationsOnlyForTheFullCivilisationSet()
        {
            Assert.That(LiveGameRules.Create(703, false, true, true, false).Economy.BuildingReservations, Is.True);
            Assert.That(LiveGameRules.Create(704, false, false, false, false).Economy.BuildingReservations, Is.False);
        }

        [Test]
        public void HumanPlacementCreatesAnUnpaidReservationWithinTheLimit()
        {
            var fixture = NewFixture(2);
            var building = PlaceUntilReserved(fixture);
            Assert.That(building.Reserved, Is.True);
            Assert.That(fixture.Simulation.Capture(1).Economy.ReservationCount, Is.EqualTo(1));
            Assert.That(fixture.Simulation.Capture(1).Economy.Wood, Is.EqualTo(0));
            Assert.That(building.Complete, Is.False);
        }

        [Test]
        public void ReservationsAreFactionLocalAndHiddenFromTheEnemy()
        {
            var fixture = NewFixture(2);
            PlaceUntilReserved(fixture);
            Assert.That(fixture.Simulation.Capture(2).Economy.Buildings.Any(b => b.Reserved), Is.False);
        }

        [Test]
        public void ReservationsUseIdOrderAndRespectTheConfiguredLimit()
        {
            var fixture = NewFixture(2);
            PlaceUntilReserved(fixture);
            var view = fixture.Simulation.Capture(1).Economy;
            Assert.That(view.ReservationCount, Is.EqualTo(1));
            var reservations = view.Buildings.Where(b => b.Reserved).ToArray();
            Assert.That(reservations.Select(b => b.ReservationOrder), Is.EqualTo(new[] { 1 }));
            for (int cell = 0; cell < 128 * 64; cell++)
            {
                fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Barracks, cell));
                fixture.Gateway.Step();
            }
            Assert.That(fixture.Simulation.Capture(1).Economy.ReservationCount, Is.EqualTo(2));
        }

        [Test]
        public void RemovingAReservationDoesNotRefundOrChangeTerrain()
        {
            var fixture = NewFixture(2);
            var building = PlaceUntilReserved(fixture);
            int wood = fixture.Simulation.Capture(1).Economy.Wood;
            fixture.Gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, ++fixture.Sequence, building.Id));
            fixture.Gateway.Step();
            Assert.That(fixture.Simulation.Capture(1).Economy.ReservationCount, Is.Zero);
            Assert.That(fixture.Simulation.Capture(1).Economy.Wood, Is.EqualTo(wood));
        }

        [Test]
        public void ReservationFrameExposesTheConfiguredCheckInterval()
        {
            var fixture = NewFixture(3);
            Assert.That(fixture.Simulation.Capture(1).Economy.BuildingReservations, Is.True);
            Assert.That(fixture.Simulation.Capture(1).Economy.ReservationLimit, Is.EqualTo(3));
            Assert.That(fixture.Simulation.Capture(1).Economy.ReservationCheckTicks, Is.EqualTo(1));
        }

        private sealed class Fixture
        {
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal readonly ulong Seed;
            internal ulong Sequence;
            internal Fixture(Battle simulation, ulong seed)
            {
                Simulation = simulation;
                Seed = seed;
                Gateway = new CommandGateway(simulation);
                Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++Sequence, false));
                Gateway.SubmitEconomy(EconomyCommand.Auto(2, ++Sequence, false));
                Gateway.Step();
            }
        }

        private static Fixture NewFixture(int limit)
        {
            var scenario = MapGenerator.Generate(705 + (ulong)limit, true);
            scenario.Economy.BuildingReservations = true;
            scenario.Economy.ReservationLimit = limit;
            scenario.Economy.ReservationCheckTicks = 1;
            scenario.Economy.StartWood = 0;
            scenario.Economy.BarracksWoodCost = 100000;
            return new Fixture(new Battle(scenario), 705 + (ulong)limit);
        }

        private static BuildingView PlaceUntilReserved(Fixture fixture)
        {
            Assert.That(fixture.Simulation.Capture(1).Economy.BuildingReservations, Is.True);
            Assert.That(fixture.Simulation.Capture(1).Economy.Wood, Is.EqualTo(0));
            Assert.That(fixture.Simulation.Capture(1).Economy.BarracksWoodCost, Is.EqualTo(100000));
            Assert.That(fixture.Simulation.Capture(1).Economy.ReservationLimit, Is.EqualTo(2));
            var probeScenario = MapGenerator.Generate(fixture.Seed, true);
            probeScenario.Economy.StartWood = 10000;
            probeScenario.Economy.BarracksWoodCost = 1;
            var probe = new Battle(probeScenario);
            var probeGateway = new CommandGateway(probe);
            ulong probeSequence = 0;
            probeGateway.SubmitEconomy(EconomyCommand.Auto(1, ++probeSequence, false));
            probeGateway.SubmitEconomy(EconomyCommand.Auto(2, ++probeSequence, false));
            probeGateway.Step();
            int candidate = -1;
            int target = Math.Max(1, probe.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Barracks)
                + fixture.Simulation.Capture(1).Economy.ReservationCount);
            for (int cell = 0; cell < 128 * 64; cell++)
            {
                probeGateway.SubmitEconomy(EconomyCommand.Place(1, ++probeSequence, BuildingKind.Barracks, cell));
                probeGateway.Step();
                if (probe.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Barracks) > target)
                { candidate = cell; break; }
            }
            Assert.That(candidate, Is.GreaterThanOrEqualTo(0), "配置可能な検証地点が見つかりませんでした。");
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Barracks, candidate));
            fixture.Gateway.Step();
            var reservation = fixture.Simulation.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Reserved);
            Assert.That(reservation.Id, Is.Not.EqualTo(0), "予約が作成されませんでした。");
            return reservation;
        }
    }
}
