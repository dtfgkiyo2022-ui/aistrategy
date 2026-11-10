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
    public sealed class LensAssemblerReserveTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Private).GetValue(simulation);

        private static ScenarioDefinition Scenario(bool reserve)
        {
            var scenario = LiveGameRules.Create(54501UL, true, true, false, false, false, false, !reserve);
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
        public void PlankShortageReservesLensAssemblerOnlyWhenFlagIsOn()
        {
            var on = new Battle(Scenario(true));
            GiveAssemblerMaterials(on, 0);
            uint reservedId = (uint)Invoke(on, "PlaceAssembler", 1u, -1, false, false, false, RecipeKind.Lens);
            Assert.That(reservedId, Is.GreaterThan(0));
            var reserved = State(on).Buildings[(int)reservedId - 1];
            Assert.That(reserved.Reserved, Is.True);
            Assert.That(reserved.Recipe, Is.EqualTo(RecipeKind.Lens));
            Assert.That(on.LensReservations(1), Is.EqualTo(1));

            SetEconomy(on, 0, "Planks", 10000);
            for (long tick = 1; tick <= 4000; tick++)
            {
                on.Step(tick, Array.Empty<ScheduledInput>());
                var building = on.Capture(1).Economy.Buildings.SingleOrDefault(b => b.Id == reservedId);
                if (building.Id == reservedId && building.Complete)
                {
                    Assert.That(building.Recipe, Is.EqualTo(RecipeKind.Lens));
                    break;
                }
                if (tick == 4000) Assert.Fail("板材を追加したレンズ組立所の予約が完成しませんでした。");
            }

            var off = new Battle(Scenario(false));
            GiveAssemblerMaterials(off, 0);
            uint offId = (uint)Invoke(off, "PlaceAssembler", 1u, -1, false, false, false, RecipeKind.Lens);
            Assert.That(offId, Is.Zero);
            Assert.That(State(off).Buildings.Any(b => b.FactionId == 1 && b.Kind == BuildingKind.Assembler), Is.False);
            Assert.That(off.LensReservations(1), Is.Zero);
        }

        [Test]
        public void ExistingGlassworksMakesLensAssemblerDecisionFirstWhenFlagIsOn()
        {
            var on = new Battle(Scenario(true));
            PrepareGlassworksDecision(on);
            Invoke(on, "DecideGlassworks", 1u);
            var onBuildings = State(on).Buildings.Where(b => b.FactionId == 1).ToArray();
            Assert.That(onBuildings.Count(b => b.Kind == BuildingKind.Assembler && b.Recipe == RecipeKind.Lens), Is.EqualTo(1));
            Assert.That(onBuildings.Count(b => b.Kind == BuildingKind.GlassWorks), Is.EqualTo(1));

            var off = new Battle(Scenario(false));
            PrepareGlassworksDecision(off);
            Invoke(off, "DecideGlassworks", 1u);
            var offBuildings = State(off).Buildings.Where(b => b.FactionId == 1).ToArray();
            Assert.That(offBuildings.Count(b => b.Kind == BuildingKind.Assembler && b.Recipe == RecipeKind.Lens), Is.Zero);
            Assert.That(offBuildings.Count(b => b.Kind == BuildingKind.GlassWorks), Is.EqualTo(2));
        }

        private static void PrepareGlassworksDecision(Battle simulation)
        {
            GiveAssemblerMaterials(simulation, 10000);
            var state = State(simulation);
            state.Buildings = new[]
            {
                new BuildingState { Id = 1, FactionId = 1, Kind = BuildingKind.GlassWorks, OriginCell = 0, Alive = true, Complete = true },
                new BuildingState { Id = 2, FactionId = 1, Kind = BuildingKind.ArcheryRange, OriginCell = 20, Alive = true, Complete = true },
                new BuildingState { Id = 3, FactionId = 1, Kind = BuildingKind.CharcoalKiln, OriginCell = 40, Alive = true, Complete = true }
            };
            state.NextBuildingId = 4;
        }

        [Test]
        public void Extension103RoundTripsAndFlagOffDoesNotWriteIt()
        {
            Assert.That(LiveGameRules.Create(54503UL, true, true, false, false).Economy.LensAssemblerReserve, Is.True);
            Assert.That(LiveGameRules.Create(54503UL, false, true, false, false).Economy.LensAssemblerReserve, Is.False);
            var source = MapGenerator.GenerateTerrain(54502UL);
            source.Economy.LensAssemblerReserve = true;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 103);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(BitConverter.ToInt32(extension.Data, 0), Is.EqualTo(1));
            Assert.That(decoded.Economy.LensAssemblerReserve, Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            source.Economy.LensAssemblerReserve = false;
            Assert.That(source.Extensions.Any(x => x.Id == 103), Is.False);
            Assert.That(ScenarioBinary.Encode(source).Length, Is.LessThan(bytes.Length));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(source)).Extensions.Any(x => x.Id == 103), Is.False);
        }
    }
}
