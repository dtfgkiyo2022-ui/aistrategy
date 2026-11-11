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
    public sealed class PrecisionMetalReserveTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool reserve, bool precisionLine = true)
        {
            var scenario = LiveGameRules.Create(54510UL, true, true, false, false);
            scenario.Economy.CarriedInputs = false;
            scenario.Economy.PrecisionMetalReserve = reserve;
            scenario.Economy.ArmsFlow = true;
            scenario.Economy.PrecisionGearStockBonusWest = precisionLine ? 20 : 0;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Private).GetValue(simulation);

        private static (int stock, long held) RunMetalworks(Battle simulation)
        {
            var world = State(simulation);
            world.Buildings = new BuildingState[1];
            world.Buildings[0] = new BuildingState
            {
                Id = 1,
                FactionId = 1,
                Kind = BuildingKind.Metalworks,
                Alive = true,
                Complete = true
            };
            world.NextBuildingId = 2;
            var economy = world.Economies[0];
            economy.Metal = 3;
            world.Economies[0] = economy;
            typeof(Battle).GetMethod("AdvanceIndustry", Private).Invoke(simulation, null);
            return (world.Economies[0].Metal, simulation.MetalReserveHeld(1));
        }

        private static int PullableMetal(Battle simulation)
        {
            var world = State(simulation);
            var economy = world.Economies[0];
            economy.Metal = 3;
            world.Economies[0] = economy;
            return (int)typeof(Battle).GetMethod("MetalPullable", Private).Invoke(simulation, new object[] { 1U });
        }

        [Test]
        public void PrecisionLineKeepsTwoMetalInStockOnlyWhenFlagIsOn()
        {
            var on = new Battle(Scenario(true));
            Assert.That(PullableMetal(on), Is.EqualTo(1));
            var onResult = RunMetalworks(on);
            Assert.That(onResult.stock, Is.EqualTo(2));
            Assert.That(onResult.held, Is.Zero);

            var off = new Battle(Scenario(false));
            Assert.That(PullableMetal(off), Is.EqualTo(3));
            var offResult = RunMetalworks(off);
            Assert.That(offResult.stock, Is.EqualTo(2));
            Assert.That(off.MetalReserveHeld(1), Is.Zero);
        }

        [Test]
        public void FactionWithoutPrecisionLineUsesAllMetalEvenWhenFlagIsOn()
        {
            var simulation = new Battle(Scenario(true, false));
            Assert.That(PullableMetal(simulation), Is.EqualTo(3));
            var result = RunMetalworks(simulation);
            Assert.That(result.stock, Is.EqualTo(2));
            Assert.That(simulation.MetalReserveHeld(1), Is.Zero);
        }

        [Test]
        public void PrecisionLineRaisesAutomaticMetalLineTargetByOne()
        {
            var targetMethod = typeof(Battle).GetMethod("ArmsFlowTarget", Private);
            var snapshotType = targetMethod.GetParameters()[2].ParameterType;
            var snapshot = Activator.CreateInstance(snapshotType);
            snapshotType.GetField("Barracks", Private).SetValue(snapshot, 3);
            var kind = Enum.ToObject(targetMethod.GetParameters()[1].ParameterType, 3);

            var on = new Battle(Scenario(true));
            var onEconomy = State(on).Economies[0];
            onEconomy.Age = 2;
            State(on).Economies[0] = onEconomy;
            int onTarget = (int)targetMethod.Invoke(on, new[] { (object)1U, kind, snapshot });

            var off = new Battle(Scenario(false));
            var offEconomy = State(off).Economies[0];
            offEconomy.Age = 2;
            State(off).Economies[0] = offEconomy;
            int offTarget = (int)targetMethod.Invoke(off, new[] { (object)1U, kind, snapshot });

            Assert.That(onTarget, Is.EqualTo(offTarget + 1));
            Assert.That(onTarget, Is.LessThanOrEqualTo(State(on).Config.Economy.ArmsFlowMaxMetalLinesAge2 + 1));
        }

        /// <summary>CarriedInputs (the live rule): a hauler picks metal up at the core for building 1.</summary>
        private static int HaulerTakes(bool reserve, bool precisionLine, RecipeKind targetRecipe)
        {
            var scenario = Scenario(reserve, precisionLine);
            scenario.Economy.CarriedInputs = true;
            var simulation = new Battle(scenario);
            var world = State(simulation);
            world.Buildings = new BuildingState[1];
            world.Buildings[0] = new BuildingState
            {
                Id = 1, FactionId = 1, Kind = targetRecipe == RecipeKind.Precision ? BuildingKind.Assembler : BuildingKind.Metalworks,
                Recipe = targetRecipe, Alive = true, Complete = true
            };
            world.NextBuildingId = 2;
            var economy = world.Economies[0];
            economy.Metal = 3;
            world.Economies[0] = economy;
            var position = (SimPoint)typeof(Battle).GetMethod("CarriedOutletPosition", Private).Invoke(simulation, new object[] { 1U, 1U });
            var villager = new VillagerState
            {
                Id = 1, FactionId = 1, Alive = true, Position = position, Task = VillagerTask.ToPickup,
                HaulKind = ResourceKind.Metal, HaulTo = 1
            };
            var args = new object[] { villager };
            typeof(Battle).GetMethod("HaulCarriedMaterial", Private).Invoke(simulation, args);
            return ((VillagerState)args[0]).Carry;
        }

        [Test]
        public void CarriedHaulerLeavesTwoMetalForPrecisionOnlyWhenFlagIsOn()
        {
            Assert.That(HaulerTakes(true, true, RecipeKind.None), Is.EqualTo(1));
            Assert.That(HaulerTakes(false, true, RecipeKind.None), Is.EqualTo(3));
            Assert.That(HaulerTakes(true, false, RecipeKind.None), Is.EqualTo(3));
            Assert.That(HaulerTakes(true, true, RecipeKind.Precision), Is.EqualTo(3));
        }

        [Test]
        public void Extension107RoundTripsAndFlagOffDoesNotWriteIt()
        {
            Assert.That(LiveGameRules.Create(54511UL, true, true, false, false).Economy.PrecisionMetalReserve, Is.True);
            Assert.That(LiveGameRules.Create(54511UL, true, true, false, false, false, false, false, false, false, true).Economy.PrecisionMetalReserve, Is.False);
            var source = MapGenerator.GenerateTerrain(54512UL);
            source.Economy.PrecisionMetalReserve = true;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 107);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(BitConverter.ToInt32(extension.Data, 0), Is.EqualTo(1));
            Assert.That(decoded.Economy.PrecisionMetalReserve, Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            source.Economy.PrecisionMetalReserve = false;
            Assert.That(source.Extensions.Any(value => value.Id == 107), Is.False);
            Assert.That(ScenarioBinary.Encode(source).Length, Is.LessThan(bytes.Length));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(source)).Extensions.Any(value => value.Id == 107), Is.False);
        }
    }
}
