using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Rts.Replay;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CraftDepthTests
    {
        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(72072UL, false, true);
            var e = scenario.Economy;
            e.Ages = true; e.Industry = true; e.ProcessingChain = true;
            e.CraftTree1 = true; e.CraftTree2 = true; e.CraftTree3 = true; e.CraftRequired = true; e.CraftDepth = enabled;
            e.StartFood = 10000; e.StartWood = 10000; e.StartMetal = 10000;
            return scenario;
        }

        [Test]
        public void AiPlacesNoMetalworksBeforeAgeOneAndABarracks()
        {
            var simulation = new Battle(Scenario(true));
            var decide = typeof(Battle).GetMethod("DecideCraftDepth", BindingFlags.Instance | BindingFlags.NonPublic);
            var count = typeof(Battle).GetMethod("CountPlannedBuildings", BindingFlags.Instance | BindingFlags.NonPublic);
            decide.Invoke(simulation, new object[] { 1u });
            Assert.That((int)count.Invoke(simulation, new object[] { 1u, BuildingKind.Metalworks }), Is.EqualTo(0));
            Assert.That((int)count.Invoke(simulation, new object[] { 1u, BuildingKind.Smelter }), Is.EqualTo(0));
        }

        [Test]
        public void MetalBuildingsOpenInAgeOneButInfantryStillNeedsNoMetal()
        {
            var simulation = new Battle(Scenario(true));
            var economy = GetEconomyObject(simulation, 0);
            Set(economy, "Civ", CivKind.Agrarian); Set(economy, "Age", (byte)0); SetEconomy(simulation, 0, economy);
            var allowed = typeof(Battle).GetMethod("MetalBuildingsAllowed", BindingFlags.Instance | BindingFlags.NonPublic);
            var infantryMetal = typeof(Battle).GetMethod("InfantryMetalFor", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)allowed.Invoke(simulation, new object[] { 1u }), Is.False);
            economy = GetEconomyObject(simulation, 0); Set(economy, "Age", (byte)1); SetEconomy(simulation, 0, economy);
            Assert.That((bool)allowed.Invoke(simulation, new object[] { 1u }), Is.True);
            Assert.That((int)infantryMetal.Invoke(simulation, new object[] { 1u }), Is.EqualTo(0));
        }

        [Test]
        public void WireValuesAreAppended()
        {
            Assert.That((int)ResourceKind.Fittings, Is.EqualTo(33));
            Assert.That((int)ResourceKind.Rope, Is.EqualTo(34));
            Assert.That((int)BuildingKind.Metalworks, Is.EqualTo(49));
            Assert.That((int)BuildingKind.RopeHut, Is.EqualTo(50));
            Assert.That((int)ProcessingLineKind.Fittings, Is.EqualTo(21));
            Assert.That((int)ProcessingLineKind.Rope, Is.EqualTo(22));
        }

        [Test]
        public void Extension72RoundTripsAndFlagOffHasNoExtension()
        {
            var source = Scenario(true);
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 72).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftDepth, Is.True);
            Assert.That(decoded.Economy.CraftDepthFittingsTicks, Is.EqualTo(40));
            Assert.That(decoded.Economy.CraftDepthRopeTicks, Is.EqualTo(30));
            Assert.That(decoded.Economy.CraftDepthFramesRopeInput, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftDepthSiegePartsFittingsInput, Is.EqualTo(2));
            Assert.That(decoded.Economy.CraftDepthSiegePartsRopeInput, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftDepthSiegePartsTicks, Is.EqualTo(120));
            Assert.That(decoded.Economy.CraftDepthMechanismFittingsInput, Is.EqualTo(2));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))).Extensions.Any(x => x.Id == 72), Is.False);
        }

        [Test]
        public void FlagOffKeepsTheBaselineBytes()
        {
            var a = Scenario(false);
            var b = Scenario(false);
            Assert.That(ScenarioBinary.Encode(a), Is.EqualTo(ScenarioBinary.Encode(b)));
            Assert.That(new Battle(a).Capture(1).Economy.CraftRequiredEnabled, Is.True);
        }

        [Test]
        public void EconomyFrameExposesCraftDepthWhenEnabled()
        {
            var frame = new Battle(Scenario(true)).Capture(1).Economy;
            var property = frame.GetType().GetProperty("CraftDepthEnabled", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null);
            Assert.That(property.GetValue(frame), Is.EqualTo(true));
        }

        [Test]
        public void MetalworksAndRopeHutTakeBeltOrStockInputAndAddTwoToTheStock()
        {
            // One input arrives by belt and the stock holds none: two goods reach the stock once.
            var metal = SingleProcessing(new Battle(Scenario(true)), BuildingKind.Metalworks, ResourceKind.Metal, 40, true, 0);
            Assert.That(metal.produced, Is.EqualTo(2));
            Assert.That(metal.stock, Is.EqualTo(2));
            Assert.That(metal.inputStock, Is.EqualTo(0));
            var rope = SingleProcessing(new Battle(Scenario(true)), BuildingKind.RopeHut, ResourceKind.Wood, 30, true, 0);
            Assert.That(rope.produced, Is.EqualTo(2));
            Assert.That(rope.stock, Is.EqualTo(2));
            // Without a belt the input comes from the stock, like an assembler.
            var fromStock = SingleProcessing(new Battle(Scenario(true)), BuildingKind.Metalworks, ResourceKind.Metal, 40, false, 1);
            Assert.That(fromStock.produced, Is.EqualTo(2));
            Assert.That(fromStock.stock, Is.EqualTo(2));
            Assert.That(fromStock.inputStock, Is.EqualTo(0));
        }

        [Test]
        public void ArmsKitUsesEightyTicksWithFittingsAndOneSixtyWithoutThem()
        {
            var simulation = new Battle(Scenario(true));
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var economyType = typeof(Battle).Assembly.GetType("Rts.Simulation.FactionEconomy");
            var advance = typeof(Battle).GetMethod("AdvanceAssembler", BindingFlags.Instance | BindingFlags.NonPublic);
            var building = Activator.CreateInstance(buildingType); var economy = Activator.CreateInstance(economyType);
            Set(building, "FactionId", 1u); Set(building, "Recipe", RecipeKind.ArmsKit); Set(economy, "Planks", 1); Set(economy, "Fittings", 1); Set(economy, "Charcoal", 1);
            var args = new object[] { building, economy }; advance.Invoke(simulation, args);
            Assert.That(Get(args[0], "Timer"), Is.EqualTo(80));
            for (int i = 0; i < 80; i++) advance.Invoke(simulation, args);
            Assert.That(Get(args[1], "ArmsKit"), Is.EqualTo(1));

            building = Activator.CreateInstance(buildingType); economy = Activator.CreateInstance(economyType);
            Set(building, "FactionId", 1u); Set(building, "Recipe", RecipeKind.ArmsKit); Set(economy, "Planks", 1); Set(economy, "Charcoal", 2); Set(economy, "Age", (byte)2);
            args = new object[] { building, economy }; advance.Invoke(simulation, args);
            Assert.That(Get(args[0], "Timer"), Is.EqualTo(160));
            for (int i = 0; i < 160; i++) advance.Invoke(simulation, args);
            Assert.That(Get(args[1], "ArmsKit"), Is.EqualTo(1));
        }

        [Test]
        public void NewRecipesConsumeRopeAndFittings()
        {
            var simulation = new Battle(Scenario(true));
            var worldEconomy = GetEconomyObject(simulation, 0); Set(worldEconomy, "ArmsKit", 20); SetEconomy(simulation, 0, worldEconomy);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var economyType = typeof(Battle).Assembly.GetType("Rts.Simulation.FactionEconomy");
            var advance = typeof(Battle).GetMethod("AdvanceAssembler", BindingFlags.Instance | BindingFlags.NonPublic);
            var building = Activator.CreateInstance(buildingType); var economy = Activator.CreateInstance(economyType);
            Set(building, "FactionId", 1u); Set(building, "Recipe", RecipeKind.Frames); Set(economy, "Planks", 2); Set(economy, "CutStone", 1); Set(economy, "Rope", 1);
            var args = new object[] { building, economy }; advance.Invoke(simulation, args);
            Assert.That(Get(args[0], "Timer"), Is.EqualTo(80)); Assert.That(Get(args[1], "Rope"), Is.EqualTo(0));
            building = Activator.CreateInstance(buildingType); economy = Activator.CreateInstance(economyType);
            Set(building, "FactionId", 1u); Set(building, "Recipe", RecipeKind.SiegeParts); Set(economy, "Frames", 1); Set(economy, "Fittings", 2); Set(economy, "Rope", 1);
            args = new object[] { building, economy }; advance.Invoke(simulation, args);
            Assert.That(Get(args[0], "Timer"), Is.EqualTo(120)); Assert.That(Get(args[1], "Fittings"), Is.EqualTo(0)); Assert.That(Get(args[1], "Rope"), Is.EqualTo(0));
            building = Activator.CreateInstance(buildingType); economy = Activator.CreateInstance(economyType);
            Set(building, "FactionId", 1u); Set(building, "Recipe", RecipeKind.Mechanism); Set(economy, "Fittings", 2); Set(economy, "Scholarly", 1); Set(economy, "CutStone", 1);
            args = new object[] { building, economy }; advance.Invoke(simulation, args);
            Assert.That(Get(args[0], "Timer"), Is.EqualTo(150)); Assert.That(Get(args[1], "Fittings"), Is.EqualTo(0));
        }

        [Test]
        public void AgeTwoCostsRequireFittingsOnlyForAssemblerAndDrill()
        {
            var simulation = new Battle(Scenario(true));
            var economy = GetEconomyObject(simulation, 0); Set(economy, "Age", (byte)2); SetEconomy(simulation, 0, economy);
            var cost = typeof(Battle).GetMethod("BuildingCostForPlacement", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(GetCost(cost.Invoke(simulation, new object[] { BuildingKind.Assembler, 1u, 0 }), "Fittings"), Is.EqualTo(5));
            Assert.That(GetCost(cost.Invoke(simulation, new object[] { BuildingKind.Drill, 1u, 0 }), "Fittings"), Is.EqualTo(4));
            Assert.That(GetCost(cost.Invoke(simulation, new object[] { BuildingKind.Metalworks, 1u, 0 }), "Fittings"), Is.EqualTo(0));
        }

        [Test]
        public void CraftDepthEnabledReplayHasNoMismatch()
        {
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, Scenario(true), Array.Empty<ScheduledInput>(), 1200, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        [Test]
        public void AutoCraftDepthKeepsTrainingInfantry()
        {
            // The fittings and rope lines themselves are a large-map behaviour (CraftDepth is live only there) and are
            // checked by the large-map measurement; on this small map the AI must simply keep its army coming.
            var simulation = new Battle(Scenario(true));
            for (long tick = 1; tick <= 12000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            bool infantry = false;
            for (uint faction = 1; faction <= 2; faction++)
                infantry |= simulation.Capture(faction).Observation.OwnArmies.Sum(x => x.AliveCount) > 0;
            Assert.That(infantry, Is.True);
        }

        private static (int produced, int stock, int inputStock) SingleProcessing(Battle simulation, BuildingKind kind, ResourceKind inputKind,
            int ticks, bool feedByBelt, int inputStock)
        {
            var worldField = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic);
            var world = worldField.GetValue(simulation);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var building = Activator.CreateInstance(buildingType);
            Set(building, "Id", 1u);
            Set(building, "FactionId", 1u);
            Set(building, "Kind", kind);
            Set(building, "Alive", true);
            Set(building, "Complete", true);
            Set(building, "OriginCell", 0);
            Set(building, "WorkCell", 0);
            Set(building, "Facing", Facing.North);
            var buildings = Array.CreateInstance(buildingType, 4);
            buildings.SetValue(building, 0);
            SetField(world, "Buildings", buildings);
            SetField(world, "NextBuildingId", 2u);
            string inputName = inputKind == ResourceKind.Metal ? "Metal" : "Wood";
            var startEconomy = Field(world, "Economies").GetValue(0);
            Set(startEconomy, inputName, inputStock);
            Field(world, "Economies").SetValue(startEconomy, 0);
            if (feedByBelt)
            {
                var feed = typeof(Battle).GetMethod("TryFeedBuilding", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That((bool)feed.Invoke(simulation, new object[] { 0, 1u, inputKind }), Is.True);
            }
            var advance = typeof(Battle).GetMethod("AdvanceIndustry", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < ticks; i++) advance.Invoke(simulation, null);
            var currentWorld = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);
            var economy = Field(currentWorld, "Economies").GetValue(0);
            return (Get(economy, kind == BuildingKind.Metalworks ? "FittingsProduced" : "RopeProduced"),
                Get(economy, kind == BuildingKind.Metalworks ? "Fittings" : "Rope"), Get(economy, inputName));
        }

        private static Array Field(object value, string name) => (Array)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
        private static void SetField(object value, string name, object fieldValue) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(value, fieldValue);
        private static void SetEconomy(Battle simulation, int index, object economy)
        {
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);
            ((Array)Field(world, "Economies")).SetValue(economy, index);
        }
        private static int GetCost(object value, string name) => Convert.ToInt32(value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value));

        private static void Set(object value, string name, object fieldValue) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(value, fieldValue);
        private static int Get(object value, string name) => Convert.ToInt32(value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value));
        private static object GetEconomyObject(Battle simulation, int index)
        {
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);
            return ((Array)Field(world, "Economies")).GetValue(index);
        }
    }
}
