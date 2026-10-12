using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BricksTests
    {
        private const int Width = 128;

        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(ScenarioDefinition scenario)
            {
                Scenario = scenario;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Send(EconomyCommand.Auto(1, ++Sequence, false));
                Send(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);

            internal void Step(int count)
            {
                for (int i = 0; i < count && !Simulation.Capture(1).Result.HasEnded; i++) Gateway.Step();
            }
        }

        private static ScenarioDefinition Scenario(bool bricks)
        {
            var s = MapGenerator.GenerateTerrain(8811UL);
            s.Economy.Ages = true;
            s.Economy.Industry = true;
            s.Economy.ProcessingChain = true;
            s.Economy.Masonry = true;
            s.Economy.Bricks = bricks;
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.StartStone = 50000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            s.Economy.Age3Ticks = 1;
            s.Economy.BarracksWoodCost = 1;
            s.Economy.BarracksWork = 1;
            s.Economy.VillagerFoodCost = 0;
            s.Economy.QuarryWork = 1;
            s.Economy.QuarryIntervalTicks = 1;
            s.Economy.CharcoalKilnWork = 1;
            s.Economy.CharcoalTicks = 1;
            s.Economy.BrickKilnWork = 1;
            s.Economy.BrickTicks = 100;
            s.Economy.BrickStoneInput = 2;
            s.Economy.BrickCharcoalInput = 1;
            s.Economy.BeltTicksPerCell = 1;
            s.Economy.BeltLimit = 4096;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> fields, string name)
            => long.Parse(fields[name], CultureInfo.InvariantCulture);

        private static int CellOf(SimPoint p) => (int)(p.Z.Raw / 65536 / 2) * Width + (int)(p.X.Raw / 65536 / 2);

        private static (int cell, Facing toward) BeltToCore(ScenarioDefinition scenario)
        {
            int core = CellOf(scenario.Cores[0].Position);
            int cx = core % Width, cz = core / Width;
            foreach (var away in new[] { Facing.East, Facing.North, Facing.South, Facing.West })
            {
                int dx = away == Facing.East ? 1 : away == Facing.West ? -1 : 0;
                int dz = away == Facing.North ? 1 : away == Facing.South ? -1 : 0;
                int x = cx + dx * 3, z = cz + dz * 3;
                int cell = z * Width + x;
                if (x >= 0 && z >= 0 && x < scenario.Map.WidthCells && z < scenario.Map.HeightCells
                    && !scenario.Map.BlockedCellIds.Contains(cell) && scenario.ResourceNodes.All(n => CellOf(n.Position) != cell))
                    return (cell, (Facing)(((int)away + 2) % 4));
            }
            Assert.Fail("コアへ向かう煉瓦搬送ベルトの地点が見つからない");
            return (0, Facing.North);
        }

        private static void ReachCivilisation(Fixture fixture, CivKind civ)
        {
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, civ));
            fixture.Step(3);
            Assert.That(Fields(fixture.Simulation)["Economy[1].Civ"], Is.EqualTo(((byte)civ).ToString(CultureInfo.InvariantCulture)));
        }

        private static void ReachThirdAge(Fixture fixture)
        {
            ReachCivilisation(fixture, CivKind.Masonry);
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, CivKind.Masonry));
            fixture.Step(3);
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, CivKind.Masonry));
            fixture.Step(3);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].Age"), Is.EqualTo(3));
        }

        private static uint Place(Fixture fixture, BuildingKind kind)
        {
            int before = (int)Number(Fields(fixture.Simulation), "Buildings.Count");
            int cells = fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, kind, cell, Facing.North));
                fixture.Step(1);
                var fields = Fields(fixture.Simulation);
                int after = (int)Number(fields, "Buildings.Count");
                if (after == before + 1 && fields["Buildings[" + after + "].Kind"] == ((byte)kind).ToString(CultureInfo.InvariantCulture))
                    return (uint)after;
            }
            Assert.Fail("合法な建設地点が見つからない: " + kind);
            return 0;
        }

        private static uint PlaceWall(Fixture fixture)
        {
            int before = (int)Number(Fields(fixture.Simulation), "Buildings.Count");
            int cells = fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                fixture.Send(EconomyCommand.PlaceWall(1, ++fixture.Sequence, new[] { cell }));
                fixture.Step(1);
                var fields = Fields(fixture.Simulation);
                int after = (int)Number(fields, "Buildings.Count");
                if (after == before + 1 && fields["Buildings[" + after + "].Kind"] == ((byte)BuildingKind.Wall).ToString(CultureInfo.InvariantCulture))
                    return (uint)after;
            }
            Assert.Fail("合法な壁の建設地点が見つからない");
            return 0;
        }

        private static void Build(Fixture fixture, uint buildingId)
        {
            fixture.Send(EconomyCommand.Assign(1, ++fixture.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, buildingId));
            for (int i = 0; i < 1500 && Fields(fixture.Simulation)["Buildings[" + buildingId + "].Complete"] != "1"; i++) fixture.Step(1);
            Assert.That(Fields(fixture.Simulation)["Buildings[" + buildingId + "].Complete"], Is.EqualTo("1"));
        }

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private static void SetEconomy(Battle simulation, uint faction, string fieldName, int value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Economies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var economies = (Array)field.GetValue(world);
            var economy = economies.GetValue((int)faction - 1);
            economy.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(economy, value);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static void SetBuilding(Battle simulation, uint buildingId, string fieldName, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Buildings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var buildings = (Array)field.GetValue(world);
            for (int i = 0; i < buildings.Length; i++)
            {
                var building = buildings.GetValue(i);
                var id = (uint)building.GetType().GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(building);
                if (id != buildingId) continue;
                building.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(building, value);
                buildings.SetValue(building, i);
                return;
            }
            Assert.Fail("建物が見つからない: " + buildingId);
        }

        private static (Fixture fixture, uint kiln) ReadyKiln(int startStone = 2)
        {
            var scenario = Scenario(true);
            scenario.Economy.StartStone = startStone;
            var fixture = new Fixture(scenario);
            ReachCivilisation(fixture, CivKind.Masonry);
            uint charcoalKiln = Place(fixture, BuildingKind.CharcoalKiln);
            Build(fixture, charcoalKiln);
            uint brickKiln = Place(fixture, BuildingKind.BrickKiln);
            Build(fixture, brickKiln);
            return (fixture, brickKiln);
        }

        [Test]
        public void BricksExtensionUsesIdTwentyOneAndRoundTripsChangedValues()
        {
            var s = Scenario(true);
            s.Economy.BrickTicks = 117;
            s.Economy.BrickHpPermille = 1517;
            var bytes = ScenarioBinary.Encode(s);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 21).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.Bricks, Is.True);
            Assert.That(decoded.Economy.BrickTicks, Is.EqualTo(117));
            Assert.That(decoded.Economy.BrickHpPermille, Is.EqualTo(1517));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, s, Array.Empty<ScheduledInput>(), 180, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        [Test]
        public void BricksOffRejectsKilnAndLineWithoutChangingStateOrHash()
        {
            var fixture = new Fixture(Scenario(false));
            fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.CharcoalKiln, 0, Facing.North));
            fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.BrickKiln, 0, Facing.North));
            fixture.Send(EconomyCommand.RequestLineAt(1, ++fixture.Sequence, ProcessingLineKind.Bricks, 0));
            fixture.Step(1);
            var after = fixture.Simulation.CaptureDiagnostic();
            var control = new Fixture(Scenario(false));
            control.Send(EconomyCommand.Place(1, ++control.Sequence, BuildingKind.CharcoalKiln, 0, Facing.North));
            control.Send(EconomyCommand.Place(1, ++control.Sequence, BuildingKind.BrickKiln, 0, Facing.North));
            control.Send(EconomyCommand.RequestLineAt(1, ++control.Sequence, ProcessingLineKind.Bricks, 0));
            control.Step(1);
            Assert.That(after.CanonicalState, Is.EqualTo(control.Simulation.CaptureDiagnostic().CanonicalState));
            Assert.That(ReplayBinary.Hash(after.CanonicalState.ToArray()), Is.EqualTo(
                ReplayBinary.Hash(control.Simulation.CaptureDiagnostic().CanonicalState.ToArray())));
            Assert.That(Fields(fixture.Simulation).ContainsKey("Economy[1].Bricks"), Is.False);
        }

        [Test]
        public void NonMasonryCannotBuildBrickKilnEvenWhenBricksAreOn()
        {
            var fixture = new Fixture(Scenario(true));
            ReachCivilisation(fixture, CivKind.Agrarian);
            int before = (int)Number(Fields(fixture.Simulation), "Buildings.Count");
            fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, BuildingKind.BrickKiln, 0, Facing.North));
            fixture.Step(1);
            Assert.That(Number(Fields(fixture.Simulation), "Buildings.Count"), Is.EqualTo(before));
        }

        [Test]
        public void BrickKilnUsesTwoStoneAndOneCharcoalInExactlyOneHundredTicksAndRestarts()
        {
            var ready = ReadyKiln();
            var fixture = ready.fixture;
            uint kiln = ready.kiln;
            var before = Fields(fixture.Simulation);
            long producedBefore = Number(before, "Economy[1].BricksProduced");
            SetEconomy(fixture.Simulation, 1, "Stone", 0);
            SetEconomy(fixture.Simulation, 1, "Charcoal", 0);
            SetBuilding(fixture.Simulation, kiln, "Input", 2);
            SetBuilding(fixture.Simulation, kiln, "InputSecondary", 1);
            fixture.Step(1);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].BricksProduced"), Is.EqualTo(producedBefore));
            fixture.Step(99);
            var afterHundred = Fields(fixture.Simulation);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].BricksProduced"), Is.EqualTo(producedBefore + 1));
            Assert.That(Number(Fields(fixture.Simulation), "Buildings[" + kiln + "].Output"), Is.GreaterThanOrEqualTo(1));

            fixture.Step(100);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].BricksProduced"), Is.EqualTo(producedBefore + 1));
            SetEconomy(fixture.Simulation, 1, "Stone", 2);
            fixture.Step(101);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].BricksProduced"), Is.EqualTo(producedBefore + 2));
        }

        [Test]
        public void BricksOnACompletedBeltReachTheCoreStock()
        {
            var scenario = Scenario(true);
            scenario.Economy.BeltComponents = true;
            scenario.Economy.BeltTicksPerCell = 1;
            var route = BeltToCore(scenario);
            scenario.Belts = new[] { new BeltDefinition
            {
                Cell = route.cell, FactionId = 1, Facing = route.toward, Item = ResourceKind.Bricks
            } };
            var fixture = new Fixture(scenario);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].Bricks"), Is.EqualTo(1));
        }

        [Test]
        public void NewWallsAndTowersUseBricksForOnePointFiveTimesHpAndDoNotUseInsufficientStock()
        {
            var fixture = new Fixture(Scenario(true));
            ReachCivilisation(fixture, CivKind.Masonry);
            SetEconomy(fixture.Simulation, 1, "Bricks", 4);
            uint wall = PlaceWall(fixture);
            uint tower = Place(fixture, BuildingKind.Tower);
            var fields = Fields(fixture.Simulation);
            Assert.That(Number(fields, "Buildings[" + wall + "].Hp"), Is.EqualTo(fixture.Scenario.Economy.WallHp * 3 / 2));
            Assert.That(Number(fields, "Buildings[" + tower + "].Hp"), Is.EqualTo(fixture.Scenario.Economy.TowerHp * 3 / 2));
            Assert.That(Number(fields, "Economy[1].Bricks"), Is.EqualTo(0));

            var ordinary = new Fixture(Scenario(true));
            ReachCivilisation(ordinary, CivKind.Masonry);
            SetEconomy(ordinary.Simulation, 1, "Bricks", 0);
            uint ordinaryWall = PlaceWall(ordinary);
            uint ordinaryTower = Place(ordinary, BuildingKind.Tower);
            var unchanged = Fields(ordinary.Simulation);
            Assert.That(Number(unchanged, "Buildings[" + ordinaryWall + "].Hp"), Is.EqualTo(ordinary.Scenario.Economy.WallHp));
            Assert.That(Number(unchanged, "Buildings[" + ordinaryTower + "].Hp"), Is.EqualTo(ordinary.Scenario.Economy.TowerHp));
            Assert.That(Number(unchanged, "Economy[1].Bricks"), Is.EqualTo(0));
        }

        [Test]
        public void ExistingWallsAndTowersUpgradeOneByOneInIdOrderEveryHundredTicksAndKeepHpRatio()
        {
            var fixture = new Fixture(Scenario(true));
            ReachCivilisation(fixture, CivKind.Masonry);
            SetEconomy(fixture.Simulation, 1, "Bricks", 0);
            uint wall1 = PlaceWall(fixture);
            uint wall2 = PlaceWall(fixture);
            uint tower = Place(fixture, BuildingKind.Tower);
            Build(fixture, tower);
            SetBuilding(fixture.Simulation, wall1, "Hp", fixture.Scenario.Economy.WallHp / 2);
            SetBuilding(fixture.Simulation, wall2, "Hp", fixture.Scenario.Economy.WallHp / 2);
            SetBuilding(fixture.Simulation, tower, "Hp", fixture.Scenario.Economy.TowerHp / 2);
            SetEconomy(fixture.Simulation, 1, "Bricks", 5);

            int toBoundary = 100 - (int)(Number(Fields(fixture.Simulation), "Tick") % 100);
            fixture.Step(toBoundary - 1);
            Assert.That(Number(Fields(fixture.Simulation), "Economy[1].BricksUpgradedWalls"), Is.EqualTo(0));
            fixture.Step(1);
            var first = Fields(fixture.Simulation);
            Assert.That(Number(first, "Economy[1].BricksUpgradedWalls"), Is.EqualTo(1));
            Assert.That(first["Buildings[" + wall1 + "].BrickUpgraded"], Is.EqualTo("1"));
            Assert.That(first["Buildings[" + wall2 + "].BrickUpgraded"], Is.EqualTo("0"));
            Assert.That(Number(first, "Buildings[" + wall1 + "].Hp"), Is.EqualTo(fixture.Scenario.Economy.WallHp * 3 / 4));

            fixture.Step(100);
            var second = Fields(fixture.Simulation);
            Assert.That(Number(second, "Economy[1].BricksUpgradedWalls"), Is.EqualTo(2));
            Assert.That(second["Buildings[" + wall2 + "].BrickUpgraded"], Is.EqualTo("1"));
            Assert.That(Number(second, "Buildings[" + wall2 + "].Hp"), Is.EqualTo(fixture.Scenario.Economy.WallHp * 3 / 4));
            fixture.Step(100);
            var third = Fields(fixture.Simulation);
            Assert.That(Number(third, "Economy[1].BricksUpgradedTowers"), Is.EqualTo(1));
            Assert.That(third["Buildings[" + tower + "].BrickUpgraded"], Is.EqualTo("1"));
            Assert.That(Number(third, "Buildings[" + tower + "].Hp"), Is.EqualTo(fixture.Scenario.Economy.TowerHp * 3 / 4));
        }

        [Test]
        public void MasonryCastleConsumesTenBricksForSixTenthsStoneAndNineBricksDoNothing()
        {
            var discounted = new Fixture(Scenario(true));
            ReachThirdAge(discounted);
            SetEconomy(discounted.Simulation, 1, "Bricks", 10);
            var castleBefore = Fields(discounted.Simulation);
            long stoneBefore = Number(castleBefore, "Economy[1].Stone");
            uint castle = Place(discounted, BuildingKind.Castle);
            var after = Fields(discounted.Simulation);
            Assert.That(castle, Is.GreaterThan(0u));
            Assert.That(Number(after, "Economy[1].Stone"), Is.EqualTo(stoneBefore - discounted.Scenario.Economy.CastleStoneCost * 600 / 1000));
            Assert.That(Number(after, "Economy[1].Bricks"), Is.EqualTo(0));

            var ordinary = new Fixture(Scenario(true));
            ReachThirdAge(ordinary);
            SetEconomy(ordinary.Simulation, 1, "Bricks", 9);
            long ordinaryStone = Number(Fields(ordinary.Simulation), "Economy[1].Stone");
            Place(ordinary, BuildingKind.Castle);
            var unchanged = Fields(ordinary.Simulation);
            Assert.That(Number(unchanged, "Economy[1].Stone"), Is.EqualTo(ordinaryStone
                - ordinary.Scenario.Economy.CastleStoneCost * ordinary.Scenario.Economy.MasonryDefenceCostPermille / 1000));
            Assert.That(Number(unchanged, "Economy[1].Bricks"), Is.EqualTo(9));
        }

        [Test]
        public void MetallurgyCharcoalKilnIsIdenticalWithBricksOnAndOff()
        {
            var on = MetallurgyCharcoal(false);
            var off = MetallurgyCharcoal(true);
            Assert.That(on.charcoal, Is.EqualTo(off.charcoal));
            Assert.That(on.input, Is.EqualTo(off.input));
            Assert.That(on.output, Is.EqualTo(off.output));
            Assert.That(on.timer, Is.EqualTo(off.timer));
        }

        private static (int charcoal, int input, int output, int timer) MetallurgyCharcoal(bool bricks)
        {
            var fixture = new Fixture(Scenario(bricks));
            ReachCivilisation(fixture, CivKind.Metallurgy);
            fixture.Send(EconomyCommand.Advance(1, ++fixture.Sequence, CivKind.Metallurgy));
            fixture.Step(3);
            uint kiln = Place(fixture, BuildingKind.CharcoalKiln);
            Build(fixture, kiln);
            fixture.Step(120);
            var fields = Fields(fixture.Simulation);
            return ((int)Number(fields, "Economy[1].Charcoal"), (int)Number(fields, "Buildings[" + kiln + "].Input"),
                (int)Number(fields, "Buildings[" + kiln + "].Output"), (int)Number(fields, "Buildings[" + kiln + "].Timer"));
        }

        [Test]
        public void BricksScenarioReplayHasNoHashMismatch()
        {
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, Scenario(true), Array.Empty<ScheduledInput>(), 1200, identity);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, identity);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }
    }
}
