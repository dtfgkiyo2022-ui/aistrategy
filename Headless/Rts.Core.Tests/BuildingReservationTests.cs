using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
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
            source.Economy.NoNegativeStock = true;
            source.Economy.ReservationLimit = 7;
            source.Economy.ReservationCheckTicks = 13;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.BuildingReservations, Is.True);
            Assert.That(decoded.Economy.NoNegativeStock, Is.True);
            Assert.That(decoded.Economy.ReservationLimit, Is.EqualTo(7));
            Assert.That(decoded.Economy.ReservationCheckTicks, Is.EqualTo(13));
            Assert.That(decoded.Extensions.Any(x => x.Id == 51), Is.True);
            Assert.That(decoded.Extensions.Any(x => x.Id == 55), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void LiveRulesEnableReservationsOnlyForTheFullCivilisationSet()
        {
            Assert.That(LiveGameRules.Create(703, false, true, true, false).Economy.BuildingReservations, Is.True);
            Assert.That(LiveGameRules.Create(703, false, true, true, false).Economy.NoNegativeStock, Is.True);
            Assert.That(LiveGameRules.Create(704, false, false, false, false).Economy.BuildingReservations, Is.False);
        }

        [Test]
        public void LiveRuleSeedsNeverProduceNegativeResourceStock()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var battle = new Battle(LiveGameRules.Create((uint)seed, false, true, false, false));
                var gateway = new CommandGateway(battle);
                gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, true));
                gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, true));
                for (int tick = 0; tick < 10488; tick++)
                {
                    gateway.Step();
                    for (uint faction = 1; faction <= 2; faction++)
                    {
                        var economy = battle.Capture(faction).Economy;
                        Assert.That(economy.Food, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, food");
                        Assert.That(economy.Wood, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, wood");
                        Assert.That(economy.Stone, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, stone");
                        Assert.That(economy.Ore, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, ore");
                        Assert.That(economy.Metal, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, metal");
                        Assert.That(economy.Gems, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, gems");
                        Assert.That(economy.Gold, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, gold");
                        Assert.That(economy.Rations, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, rations");
                        Assert.That(economy.Bricks, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, bricks");
                        Assert.That(economy.Tack, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, tack");
                        Assert.That(economy.Planks, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, planks");
                        Assert.That(economy.Frames, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, frames");
                        Assert.That(economy.ArmsKit, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, arms");
                        Assert.That(economy.FineArmsKit, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, fine arms");
                        Assert.That(economy.SiegeParts, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, siege parts");
                        Assert.That(economy.Books, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, books");
                        Assert.That(economy.BowGear, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, bow gear");
                        Assert.That(economy.Tools, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, tools");
                        Assert.That(economy.Charms, Is.GreaterThanOrEqualTo(0), $"seed {seed}, tick {tick}, faction {faction}, charms");
                    }
                }
            }
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

        [Test]
        public void FlagOffKeepsTheHistoricalBytesAndEveryTickHash()
        {
            var leftScenario = MapGenerator.Generate(711, true);
            leftScenario.Economy.StartWood = 0;
            leftScenario.Economy.BarracksWoodCost = 100000;
            leftScenario.Economy.BuildingReservations = false;
            var rightScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(leftScenario));
            Assert.That(ScenarioBinary.Encode(leftScenario), Is.EqualTo(ScenarioBinary.Encode(rightScenario)));
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            var leftGateway = new CommandGateway(left);
            var rightGateway = new CommandGateway(right);
            ulong sequence = 0;
            leftGateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            rightGateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            leftGateway.Step();
            rightGateway.Step();
            int cell = FindPlaceableCell(leftScenario, BuildingKind.Barracks);
            leftGateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Barracks, cell));
            rightGateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Barracks, cell));
            for (int tick = 0; tick < 30000; tick++)
            {
                leftGateway.Step();
                rightGateway.Step();
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
                if (left.Capture(1).Tick >= 30000 || left.Capture(1).Result.HasEnded) break;
            }
            Assert.That(left.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Barracks), Is.False);
        }

        [Test]
        public void ReservationStartsConstructionAndEventuallyCompletesAfterWoodArrives()
        {
            var scenario = MapGenerator.Generate(712, true);
            scenario.Economy.BuildingReservations = true;
            scenario.Economy.ReservationLimit = 2;
            scenario.Economy.ReservationCheckTicks = 1;
            scenario.Economy.StartWood = 0;
            scenario.Economy.BarracksWoodCost = 40;
            var fixture = NewFixture(scenario);
            int cell = FindPlaceableCell(scenario, BuildingKind.Barracks);
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Barracks, cell));
            fixture.Gateway.Step();
            var reservation = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Reserved);
            Assert.That(fixture.Simulation.Capture(1).Economy.Wood, Is.EqualTo(0));
            fixture.Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++fixture.Sequence, true));
            fixture.Gateway.Step();
            for (int i = 0; i < 5000; i++)
            {
                fixture.Gateway.Step();
                var current = fixture.Simulation.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Id == reservation.Id);
                if (current.Id == reservation.Id && !current.Reserved && current.Complete) return;
            }
            var after = fixture.Simulation.Capture(1).Economy;
            var observed = after.Buildings.FirstOrDefault(b => b.Id == reservation.Id);
            Assert.Fail("予約が建設開始後に完成しませんでした。wood=" + after.Wood + " id=" + observed.Id
                + " reserved=" + observed.Reserved + " complete=" + observed.Complete + " progress=" + observed.Progress
                + " villagers=" + string.Join(",", after.Villagers.Select(v => v.Id + ":" + v.Activity)));
        }

        [Test]
        public void ReservationQueueKeepsACheaperLaterReservationWaitingForTheHead()
        {
            var scenario = MapGenerator.GenerateTerrain(713, processingChain: true);
            scenario.Economy.BuildingReservations = true;
            scenario.Economy.ReservationLimit = 2;
            scenario.Economy.ReservationCheckTicks = 1;
            scenario.Economy.StartWood = 1;
            scenario.Economy.BarracksWoodCost = 40;
            scenario.Economy.HouseWoodCost = 5;
            var fixture = NewFixture(scenario);
            int barracksCell = FindPlaceableCell(scenario, BuildingKind.Barracks);
            int houseCell = FindPlaceableCellAfter(scenario, BuildingKind.Barracks, barracksCell, BuildingKind.House);
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Barracks, barracksCell));
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.House, houseCell));
            fixture.Gateway.Step();
            fixture.Gateway.Step();
            var waiting = fixture.Simulation.Capture(1).Economy.Buildings.Where(b => b.Reserved).OrderBy(b => b.ReservationOrder).ToArray();
            Assert.That(waiting.Select(b => b.ReservationOrder), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(fixture.Simulation.Capture(1).Economy.Wood, Is.EqualTo(1));
        }

        [Test]
        public void ReservationCellRejectsBuildingsAndBeltsButDoesNotCloseTheMap()
        {
            var scenario = MapGenerator.Generate(715, true);
            scenario.Economy.BuildingReservations = true;
            scenario.Economy.ReservationLimit = 2;
            scenario.Economy.ReservationCheckTicks = 1;
            scenario.Economy.StartWood = 0;
            scenario.Economy.BarracksWoodCost = 100000;
            var fixture = NewFixture(scenario);
            int cell = FindPlaceableCell(scenario, BuildingKind.Barracks);
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Barracks, cell));
            fixture.Gateway.Step();
            var reservation = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Reserved);
            int before = fixture.Simulation.Capture(1).Economy.Buildings.Count;
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Barracks, cell));
            fixture.Gateway.Step();
            Assert.That(fixture.Simulation.Capture(1).Economy.Buildings.Count, Is.EqualTo(before));
            Assert.That(fixture.Simulation.Capture(1).Economy.ReservationCount, Is.EqualTo(1));
        }

        [Test]
        public void InvalidReservationIsCancelledWithoutChargingAndEmitsAnEvent()
        {
            var scenario = MapGenerator.Generate(716, true, true);
            scenario.Economy.BuildingReservations = true;
            scenario.Economy.ReservationLimit = 1;
            scenario.Economy.ReservationCheckTicks = 1;
            scenario.Economy.StartWood = 0;
            scenario.Economy.MineWoodCost = 100000;
            var ore = scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Ore);
            for (int i = 0; i < scenario.ResourceNodes.Length; i++)
            {
                var node = scenario.ResourceNodes[i];
                if (node.Id == ore.Id) node.Amount = 1;
                scenario.ResourceNodes[i] = node;
            }
            var fixture = NewFixture(scenario);
            int cell = FindMineCellForNode(scenario, ore.Id);
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Mine, cell));
            fixture.Gateway.Step();
            fixture.Gateway.SubmitEconomy(EconomyCommand.Assign(1, ++fixture.Sequence, new[] { 1U, 2U, 3U }, EconomyTargetKind.ResourceNode, ore.Id));
            fixture.Gateway.Step();
            for (int i = 0; i < 1000 && fixture.Simulation.Capture(1).Economy.ReservationCount != 0; i++) fixture.Gateway.Step();
            var frame = fixture.Simulation.Capture(1);
            var observedOre = frame.Economy.Resources.FirstOrDefault(r => r.Id == ore.Id);
            Assert.That(frame.Economy.ReservationCount, Is.Zero,
                "ore=" + observedOre.Remaining + " villagers=" + string.Join(",", frame.Economy.Villagers.Select(v => v.Id + ":" + v.Activity)));
            Assert.That(frame.Economy.Wood, Is.EqualTo(0));
            Assert.That(frame.Events.Any(e => e.Kind == EventKind.BuildingReservationCancelled), Is.True);
        }

        [Test]
        public void UnsupportedBuildingsIncludingBridgesAndTollgatesAreNotReserved()
        {
            var fixture = NewFixture(3);
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.House, 0));
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Bridge, 0));
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Tollgate, 0));
            fixture.Gateway.Step();
            Assert.That(fixture.Simulation.Capture(1).Economy.ReservationCount, Is.Zero);
            Assert.That(fixture.Simulation.Capture(1).Economy.Buildings.Any(b => b.Reserved), Is.False);
        }

        [Test]
        public void AutomaticEconomyDoesNotCreateReservationsOrUseAHumanReservationAsABarracks()
        {
            var scenario = MapGenerator.Generate(718, true);
            scenario.Economy.BuildingReservations = true;
            scenario.Economy.ReservationLimit = 2;
            scenario.Economy.ReservationCheckTicks = 1;
            scenario.Economy.StartWood = 0;
            scenario.Economy.BarracksWoodCost = 100000;
            var fixture = NewFixture(scenario);
            int cell = FindPlaceableCell(scenario, BuildingKind.Barracks);
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Barracks, cell));
            fixture.Gateway.Step();
            fixture.Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++fixture.Sequence, true));
            fixture.Gateway.Step();
            for (int i = 0; i < 200; i++) fixture.Gateway.Step();
            var frame = fixture.Simulation.Capture(1).Economy;
            Assert.That(frame.ReservationCount, Is.EqualTo(1));
            Assert.That(frame.Buildings.Count(b => b.Reserved), Is.EqualTo(1));
        }

        [Test]
        public void AutomaticEconomyDoesNotPlaceAnotherBarracksWhileTheHeadReservationWaits()
        {
            var scenario = MapGenerator.Generate(720, true);
            scenario.Economy.BuildingReservations = true;
            scenario.Economy.ReservationLimit = 2;
            scenario.Economy.ReservationCheckTicks = 100;
            scenario.Economy.StartWood = 90;
            scenario.Economy.BarracksWoodCost = 100;
            var fixture = NewFixture(scenario);
            int cell = FindPlaceableCell(scenario, BuildingKind.Barracks);
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Barracks, cell));
            fixture.Gateway.Step();
            fixture.Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++fixture.Sequence, true));
            for (int i = 0; i < 60; i++) fixture.Gateway.Step();

            var frame = fixture.Simulation.Capture(1).Economy;
            Assert.That(frame.Buildings.Count(b => b.Kind == BuildingKind.Barracks), Is.EqualTo(1));
            Assert.That(frame.Buildings.Count(b => b.Kind == BuildingKind.Barracks && b.Reserved), Is.EqualTo(1));
        }

        [Test]
        public void ARecordedReservationReplaysWithTheSameTickHashes()
        {
            var scenario = MapGenerator.Generate(719, true);
            scenario.Economy.BuildingReservations = true;
            scenario.Economy.ReservationLimit = 2;
            scenario.Economy.ReservationCheckTicks = 1;
            scenario.Economy.StartWood = 0;
            scenario.Economy.BarracksWoodCost = 100000;
            var fixture = NewFixture(scenario);
            int cell = FindPlaceableCell(scenario, BuildingKind.Barracks);
            fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.Barracks, cell));
            fixture.Gateway.Step();
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, fixture.Gateway.Inputs, 300, identity);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        private sealed class Fixture
        {
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal readonly ScenarioDefinition Scenario;
            internal readonly ulong Seed;
            internal ulong Sequence;
            internal Fixture(ScenarioDefinition scenario)
            {
                Scenario = scenario;
                Seed = scenario.Seed;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
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
            return NewFixture(scenario);
        }

        private static Fixture NewFixture(ScenarioDefinition scenario)
        {
            var fixture = new Fixture(scenario);
            fixture.Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++fixture.Sequence, false));
            fixture.Gateway.SubmitEconomy(EconomyCommand.Auto(2, ++fixture.Sequence, false));
            fixture.Gateway.Step();
            return fixture;
        }

        private static int FindPlaceableCell(ScenarioDefinition source, BuildingKind kind)
        {
            var probeScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            probeScenario.Economy.StartWood = 100000;
            probeScenario.Economy.BarracksWoodCost = 1;
            probeScenario.Economy.HouseWoodCost = 1;
            probeScenario.Economy.MineWoodCost = 1;
            var probe = new Battle(probeScenario);
            var gateway = new CommandGateway(probe);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.Step();
            for (int cell = 0; cell < probeScenario.Map.WidthCells * probeScenario.Map.HeightCells; cell++)
            {
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, kind, cell));
                gateway.Step();
                if (probe.Capture(1).Economy.Buildings.Any(b => b.Kind == kind && !b.Reserved)) return cell;
            }
            Assert.Fail("配置可能な検証地点が見つかりませんでした。");
            return -1;
        }

        private static int FindPlaceableCellAfter(ScenarioDefinition source, BuildingKind firstKind, int firstCell, BuildingKind secondKind)
        {
            var probeScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            probeScenario.Economy.StartWood = 100000;
            probeScenario.Economy.BarracksWoodCost = 1;
            probeScenario.Economy.HouseWoodCost = 1;
            probeScenario.Economy.MineWoodCost = 1;
            var probe = new Battle(probeScenario);
            var gateway = new CommandGateway(probe);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.Step();
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, firstKind, firstCell));
            gateway.Step();
            int firstCount = probe.Capture(1).Economy.Buildings.Count(b => b.Kind == firstKind && !b.Reserved);
            int cells = probeScenario.Map.WidthCells * probeScenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                if (cell == firstCell) continue;
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, secondKind, cell));
                gateway.Step();
                if (probe.Capture(1).Economy.Buildings.Count(b => b.Kind == firstKind && !b.Reserved) == firstCount
                    && probe.Capture(1).Economy.Buildings.Any(b => b.Kind == secondKind && !b.Reserved)) return cell;
            }
            Assert.Fail("既存建物の後に配置可能なセルが見つかりませんでした。");
            return -1;
        }

        private static int FindMineCellForNode(ScenarioDefinition source, uint nodeId)
        {
            var node = source.ResourceNodes.Single(n => n.Id == nodeId);
            int width = source.Map.WidthCells;
            int nodeCell = (int)(node.Position.Z.Raw / 65536 / source.Map.CellSizeMeters) * width
                + (int)(node.Position.X.Raw / 65536 / source.Map.CellSizeMeters);
            int size = source.Economy.MineSizeCells;
            var probeScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            probeScenario.Economy.StartWood = 100000;
            probeScenario.Economy.MineWoodCost = 1;
            var probe = new Battle(probeScenario);
            var gateway = new CommandGateway(probe);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.Step();
            int nodeX = nodeCell % width, nodeZ = nodeCell / width;
            for (int dz = 0; dz < size; dz++)
                for (int dx = 0; dx < size; dx++)
                {
                    int originX = nodeX - dx, originZ = nodeZ - dz;
                    if (originX < 0 || originZ < 0 || originX + size > width || originZ + size > source.Map.HeightCells) continue;
                    int origin = originZ * width + originX;
                    gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Mine, origin));
                    gateway.Step();
                    if (probe.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Mine && !b.Reserved)) return origin;
                }
            Assert.Fail("指定した資源ノードを覆う配置可能な鉱山セルが見つかりませんでした。");
            return -1;
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
