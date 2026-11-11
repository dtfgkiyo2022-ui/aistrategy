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
    public sealed class PrecisionAssemblerReserveTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Private).GetValue(simulation);

        private static ScenarioDefinition Scenario(bool reserve, bool lensReserve = true)
        {
            var scenario = LiveGameRules.Create(54504UL, true, true, false, false, false, false, !lensReserve, !reserve);
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }

        private static void SetEconomy(Battle simulation, int index, string name, object value)
        {
            var world = typeof(Battle).GetField("world", Private).GetValue(simulation);
            var economies = (Array)world.GetType().GetField("Economies", Private).GetValue(world);
            var economy = economies.GetValue(index);
            var field = economy.GetType().GetField(name, Private);
            field.SetValue(economy, Convert.ChangeType(value, field.FieldType));
            economies.SetValue(economy, index);
        }

        private static object Invoke(Battle simulation, string name, params object[] args)
            => typeof(Battle).GetMethod(name, Private).Invoke(simulation, args);

        private static void GiveAssemblerMaterials(Battle simulation, int planks)
        {
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Stone", 10000);
            SetEconomy(simulation, 0, "Planks", planks);
            SetEconomy(simulation, 0, "Frames", 0);
            SetEconomy(simulation, 0, "Bricks", 10000);
            SetEconomy(simulation, 0, "SiegeParts", 10000);
            SetEconomy(simulation, 0, "Fittings", 10000);
            SetEconomy(simulation, 0, "Rope", 10000);
        }

        [Test]
        public void PlankShortageReservesPrecisionAssemblerOnlyWhenFlagIsOn()
        {
            var on = new Battle(Scenario(true));
            GiveAssemblerMaterials(on, 0);
            uint reservedId = (uint)Invoke(on, "PlaceAssembler", 1u, -1, false, false, false, RecipeKind.Precision);
            Assert.That(reservedId, Is.GreaterThan(0));
            var reserved = State(on).Buildings[(int)reservedId - 1];
            Assert.That(reserved.Reserved, Is.True);
            Assert.That(reserved.Recipe, Is.EqualTo(RecipeKind.Precision));
            Assert.That(on.PrecisionReservations(1), Is.EqualTo(1));
            Assert.That(on.PrecisionAssemblers(1), Is.EqualTo(1));

            SetEconomy(on, 0, "Planks", 10000);
            for (long tick = 1; tick <= 4000; tick++)
            {
                on.Step(tick, Array.Empty<ScheduledInput>());
                var building = on.Capture(1).Economy.Buildings.SingleOrDefault(b => b.Id == reservedId);
                if (building.Id == reservedId && building.Complete)
                {
                    Assert.That(building.Recipe, Is.EqualTo(RecipeKind.Precision));
                    break;
                }
                if (tick == 4000) Assert.Fail("板材を追加した精密部品組立所の予約が完成しませんでした。");
            }

            var off = new Battle(Scenario(false));
            GiveAssemblerMaterials(off, 0);
            uint offId = (uint)Invoke(off, "PlaceAssembler", 1u, -1, false, false, false, RecipeKind.Precision);
            Assert.That(offId, Is.Zero);
            Assert.That(State(off).Buildings.Any(b => b.FactionId == 1 && b.Kind == BuildingKind.Assembler), Is.False);
            Assert.That(off.PrecisionReservations(1), Is.Zero);
            Assert.That(off.PrecisionAssemblers(1), Is.Zero);
        }

        [Test]
        public void PrecisionReserveIsIndependentOfLensReserve()
        {
            var simulation = new Battle(Scenario(true, false));
            GiveAssemblerMaterials(simulation, 0);
            uint reservedId = (uint)Invoke(simulation, "PlaceAssembler", 1u, -1, false, false, false, RecipeKind.Precision);
            Assert.That(reservedId, Is.GreaterThan(0));
            var reserved = State(simulation).Buildings[(int)reservedId - 1];
            Assert.That(reserved.Reserved, Is.True);
            Assert.That(reserved.Recipe, Is.EqualTo(RecipeKind.Precision));
            Assert.That(simulation.PrecisionReservations(1), Is.EqualTo(1));
        }

        [Test]
        public void Extension104RoundTripsAndFlagOffDoesNotWriteIt()
        {
            Assert.That(LiveGameRules.Create(54505UL, true, true, false, false).Economy.PrecisionAssemblerReserve, Is.True);
            Assert.That(LiveGameRules.Create(54505UL, false, true, false, false).Economy.PrecisionAssemblerReserve, Is.False);
            var source = MapGenerator.GenerateTerrain(54506UL);
            source.Economy.PrecisionAssemblerReserve = true;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 104);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(BitConverter.ToInt32(extension.Data, 0), Is.EqualTo(1));
            Assert.That(decoded.Economy.PrecisionAssemblerReserve, Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            source.Economy.PrecisionAssemblerReserve = false;
            Assert.That(source.Extensions.Any(x => x.Id == 104), Is.False);
            Assert.That(ScenarioBinary.Encode(source).Length, Is.LessThan(bytes.Length));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(source)).Extensions.Any(x => x.Id == 104), Is.False);
        }
    }
}
