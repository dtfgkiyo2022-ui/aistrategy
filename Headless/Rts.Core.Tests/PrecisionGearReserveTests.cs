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
    public sealed class PrecisionGearReserveTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Private).GetValue(simulation);

        private static ScenarioDefinition Scenario(bool reserve, bool precisionLine = true)
        {
            var scenario = LiveGameRules.Create(54507UL, true, true, false, false, false, false, false, false, !reserve);
            scenario.Economy.PrecisionGearStockBonusWest = precisionLine ? 20 : 0;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }

        private static int OpenCell(Battle simulation)
        {
            var world = State(simulation);
            var reserved = typeof(Battle).GetMethod("IsReservedBuildingCell", Private);
            var node = typeof(Battle).GetMethod("IsNodeCell", Private);
            var core = typeof(Battle).GetMethod("InsideAnyCore", Private);
            for (int cell = 0; cell < world.Belts.Length; cell++)
                if (world.Map.IsPassable(cell) && world.Belts[cell].FactionId == 0
                    && !(bool)reserved.Invoke(simulation, new object[] { cell })
                    && !(bool)node.Invoke(simulation, new object[] { cell })
                    && !(bool)core.Invoke(simulation, new object[] { cell })) return cell;
            Assert.Fail("配置可能なベルトセルが見つかりませんでした。");
            return -1;
        }

        private static EconomyCommand InserterCommand(int cell)
            => new EconomyCommand(1, 1, EconomyCommandKind.PlaceBeltComponent, BuildingKind.Hut, cell, 0, UnitKind.Villager,
                Array.Empty<uint>(), EconomyTargetKind.None, 0x80000000U, true, Array.Empty<int>(), Array.Empty<Facing>(), Facing.North);

        private static void GiveInserterMaterials(Battle simulation)
        {
            var economy = State(simulation).Economies[0];
            economy.Age = 2;
            economy.Wood = 10000;
            economy.Fittings = 10000;
            economy.Gears = 5;
            State(simulation).Economies[0] = economy;
        }

        private static void Place(Battle simulation, EconomyCommand command, bool automatic)
        {
            typeof(Battle).GetField("automaticEconomyAction", Private).SetValue(simulation, automatic);
            typeof(Battle).GetMethod("PlaceBeltComponent", Private).Invoke(simulation, new object[] { 1U, command, false });
        }

        [Test]
        public void AutomaticInserterPreservesPrecisionGearsOnlyWhenFlagIsOn()
        {
            var on = new Battle(Scenario(true));
            int onCell = OpenCell(on);
            GiveInserterMaterials(on);
            Place(on, InserterCommand(onCell), true);
            Assert.That(State(on).Belts[onCell].FactionId, Is.Zero);
            Assert.That(on.GearReserveBlocked(1), Is.EqualTo(1));

            var off = new Battle(Scenario(false));
            int offCell = OpenCell(off);
            GiveInserterMaterials(off);
            Place(off, InserterCommand(offCell), true);
            Assert.That(State(off).Belts[offCell].FactionId, Is.EqualTo(1));
            Assert.That(off.GearReserveBlocked(1), Is.Zero);
        }

        [Test]
        public void HumanInserterAndFactionWithoutPrecisionLineCanSpendGears()
        {
            var human = new Battle(Scenario(true));
            int humanCell = OpenCell(human);
            GiveInserterMaterials(human);
            Place(human, InserterCommand(humanCell), false);
            Assert.That(State(human).Belts[humanCell].FactionId, Is.EqualTo(1));

            var noLine = new Battle(Scenario(true, false));
            int noLineCell = OpenCell(noLine);
            GiveInserterMaterials(noLine);
            Place(noLine, InserterCommand(noLineCell), true);
            Assert.That(State(noLine).Belts[noLineCell].FactionId, Is.EqualTo(1));
        }

        private static bool LayMerger(Battle simulation, bool reserveOn, out int cell)
        {
            cell = OpenCell(simulation);
            var world = State(simulation);
            world.Belts[cell] = new BeltState { FactionId = 1, Facing = Facing.North, Hp = world.Config.Economy.BeltHp };
            var economy = world.Economies[0];
            economy.Planks = 10000;
            economy.Gears = 5;
            world.Economies[0] = economy;
            return (bool)typeof(Battle).GetMethod("LayAutomaticMerger", Private).Invoke(simulation, new object[] { 1U, cell });
        }

        [Test]
        public void AutomaticMergerPreservesPrecisionGearsOnlyWhenFlagIsOn()
        {
            var on = new Battle(Scenario(true));
            Assert.That(LayMerger(on, true, out int onCell), Is.False);
            Assert.That(State(on).Belts[onCell].Component, Is.EqualTo(BeltComponentKind.None));
            Assert.That(State(on).Economies[0].Gears, Is.EqualTo(5));
            Assert.That(on.GearReserveBlocked(1), Is.EqualTo(1));

            var off = new Battle(Scenario(false));
            Assert.That(LayMerger(off, false, out int offCell), Is.True);
            Assert.That(State(off).Belts[offCell].Component, Is.EqualTo(BeltComponentKind.Merger));
            Assert.That(State(off).Economies[0].Gears, Is.EqualTo(5 - State(off).Config.Economy.MergerGears));
            Assert.That(off.GearReserveBlocked(1), Is.Zero);

            var noLine = new Battle(Scenario(true, false));
            Assert.That(LayMerger(noLine, true, out _), Is.True);
        }

        [Test]
        public void Extension105RoundTripsAndFlagOffDoesNotWriteIt()
        {
            Assert.That(LiveGameRules.Create(54508UL, true, true, false, false).Economy.PrecisionGearReserve, Is.True);
            Assert.That(LiveGameRules.Create(54508UL, true, true, false, false, false, false, false, false, true).Economy.PrecisionGearReserve, Is.False);
            var source = MapGenerator.GenerateTerrain(54509UL);
            source.Economy.PrecisionGearReserve = true;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 105);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(BitConverter.ToInt32(extension.Data, 0), Is.EqualTo(1));
            Assert.That(decoded.Economy.PrecisionGearReserve, Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            source.Economy.PrecisionGearReserve = false;
            Assert.That(source.Extensions.Any(value => value.Id == 105), Is.False);
            Assert.That(ScenarioBinary.Encode(source).Length, Is.LessThan(bytes.Length));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(source)).Extensions.Any(value => value.Id == 105), Is.False);
        }
    }
}
