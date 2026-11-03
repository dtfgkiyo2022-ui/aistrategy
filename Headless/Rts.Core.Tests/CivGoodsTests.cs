using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CivGoodsTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(38291UL);
            var e = scenario.Economy;
            e.Enabled = true; e.Ages = true; e.Industry = true; e.ProcessingChain = true;
            e.CraftTree1 = true; e.CraftTree2 = true; e.CraftTree3 = true;
            e.CraftRequired = true;
            e.CivGoodsRequired = enabled; e.CivGoodsMaxLines = 6;
            e.Bricks = true; e.Tack = true; e.RepairKits = true;
            e.StartFood = 10000; e.StartWood = 10000; e.StartStone = 10000; e.StartMetal = 10000;
            e.AdvanceFoodCost = 0; e.AdvanceWoodCost = 0; e.AdvanceTicks = 1;
            e.Age2FoodCost = 0; e.Age2WoodCost = 0; e.Age2Ticks = 1;
            e.Age3FoodCost = 0; e.Age3WoodCost = 0; e.Age3Ticks = 1;
            return scenario;
        }

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", Private).GetValue(simulation);

        private static void SetEconomy(Battle simulation, uint faction, string name, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Economies", Private | BindingFlags.Public);
            var economies = (Array)field.GetValue(world);
            var economy = economies.GetValue((int)faction - 1);
            var target = economy.GetType().GetField(name, Private | BindingFlags.Public);
            target.SetValue(economy, value.GetType() == target.FieldType ? value : Convert.ChangeType(value, target.FieldType));
            economies.SetValue(economy, (int)faction - 1);
        }

        private static bool InvokeBool(Battle simulation, string name, uint faction)
            => (bool)typeof(Battle).GetMethod(name, Private).Invoke(simulation, new object[] { faction });

        private static object NewBuilding(Battle simulation, BuildingKind kind, RecipeKind recipe)
        {
            var type = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var building = Activator.CreateInstance(type);
            type.GetField("Kind", Private | BindingFlags.Public).SetValue(building, kind);
            type.GetField("FactionId", Private | BindingFlags.Public).SetValue(building, 1u);
            type.GetField("Recipe", Private | BindingFlags.Public).SetValue(building, recipe);
            type.GetField("InputSecondaryKind", Private | BindingFlags.Public).SetValue(building, ResourceKind.Steel);
            return building;
        }

        private static object[] Recipe(Battle simulation, object building)
        {
            var method = typeof(Battle).GetMethod("CraftFlowRecipe", Private);
            var args = new object[] { building, (ResourceKind)0, (ResourceKind)0, 0, (ResourceKind)0, 0, (ResourceKind)0, 0, 0, 0 };
            Assert.That((bool)method.Invoke(simulation, args), Is.True);
            return args;
        }

        [Test]
        public void Extension91RoundTripsAndOffStateOmitsIt()
        {
            var on = Scenario(true);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
            Assert.That(decoded.Economy.CivGoodsRequired, Is.True);
            Assert.That(decoded.Economy.CivGoodsMaxLines, Is.EqualTo(6));
            Assert.That(decoded.Extensions.Single(x => x.Id == 91).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(on)));

            var off = Scenario(false);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 91), Is.False);
        }

        [Test]
        public void AllCivilisationsCanUseTheThreeBuildingsFromAgeTwoOnlyWhenFlagIsOn()
        {
            foreach (var civ in Enum.GetValues<CivKind>().Where(x => x != CivKind.Primitive))
            {
                var on = new Battle(Scenario(true));
                SetEconomy(on, 1, "Age", 1);
                SetEconomy(on, 1, "Civ", civ);
                Assert.That(InvokeBool(on, "BricksAllowed", 1), Is.True, civ + " bricks");
                Assert.That(InvokeBool(on, "TackAllowed", 1), Is.True, civ + " tack");
                Assert.That(InvokeBool(on, "RepairKitsAllowed", 1), Is.True, civ + " repair");
            }

            var off = new Battle(Scenario(false));
            SetEconomy(off, 1, "Age", 1); SetEconomy(off, 1, "Civ", CivKind.Agrarian);
            Assert.That(InvokeBool(off, "BricksAllowed", 1), Is.False);
            Assert.That(InvokeBool(off, "TackAllowed", 1), Is.False);
            Assert.That(InvokeBool(off, "RepairKitsAllowed", 1), Is.False);
        }

        [Test]
        public void AgeThreeAssemblerRecipesExposeTackAndBricksAsThirdInputs()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 1, "Age", 2);
            SetEconomy(simulation, 1, "Civ", CivKind.Agrarian);
            var arms = Recipe(simulation, NewBuilding(simulation, BuildingKind.Assembler, RecipeKind.ArmsKit));
            Assert.That(arms[6], Is.EqualTo(ResourceKind.Tack));
            Assert.That(arms[7], Is.EqualTo(1));
            var frames = Recipe(simulation, NewBuilding(simulation, BuildingKind.Assembler, RecipeKind.Frames));
            Assert.That(frames[6], Is.EqualTo(ResourceKind.Bricks));
            Assert.That(frames[7], Is.EqualTo(1));

            SetEconomy(simulation, 1, "Age", 1);
            var ageTwo = Recipe(simulation, NewBuilding(simulation, BuildingKind.Assembler, RecipeKind.ArmsKit));
            Assert.That(ageTwo[6], Is.EqualTo((ResourceKind)0));
        }

        // Review (Claude, 11-03): rams and cannons take 2 repair kits from age 2 with the flag, nothing else and nothing without it.
        [Test]
        public void SiegeTrainingNeedsTwoRepairKitsOnlyFromAgeTwoWithTheFlag()
        {
            var on = new Battle(Scenario(true));
            var cost = typeof(Battle).GetMethod("SiegeRepairKitCost", Private);
            SetEconomy(on, 1, "Age", 2);
            Assert.That(cost.Invoke(on, new object[] { 1u, UnitKind.Ram }), Is.EqualTo(2));
            Assert.That(cost.Invoke(on, new object[] { 1u, UnitKind.Cannon }), Is.EqualTo(2));
            Assert.That(cost.Invoke(on, new object[] { 1u, UnitKind.Infantry }), Is.EqualTo(0));
            SetEconomy(on, 1, "Age", 1);
            Assert.That(cost.Invoke(on, new object[] { 1u, UnitKind.Ram }), Is.EqualTo(0));
            var off = new Battle(Scenario(false));
            SetEconomy(off, 1, "Age", 2);
            Assert.That(cost.Invoke(off, new object[] { 1u, UnitKind.Ram }), Is.EqualTo(0));
        }

        // Review: with the flag off the scenario and every tick's state stay as they were.
        [Test]
        public void FlagOffKeepsEveryTickHash()
        {
            var source = LiveGameRules.Create(38292UL, true, true, false, false);
            Assert.That(source.Economy.CivGoodsRequired, Is.True);
            source.Economy.CivGoodsRequired = false;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Extensions.Any(x => x.Id == 91), Is.False);
            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 200; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(Rts.Replay.ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(Rts.Replay.ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void OriginalCivilisationCraftSpeedIsThreeQuartersCeiled()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 1, "Age", 1);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var building = Activator.CreateInstance(buildingType);
            buildingType.GetField("FactionId", Private | BindingFlags.Public).SetValue(building, 1u);
            buildingType.GetField("Kind", Private | BindingFlags.Public).SetValue(building, BuildingKind.BrickKiln);
            SetEconomy(simulation, 1, "Civ", CivKind.Masonry);
            var method = typeof(Battle).GetMethod("CivGoodsCraftTicks", Private);
            Assert.That(method.Invoke(simulation, new[] { building, (object)101 }), Is.EqualTo(76));
        }
    }
}
