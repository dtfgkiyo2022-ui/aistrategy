using System;
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
    public sealed class CraftTree2Tests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(62042UL, false, true);
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.ProcessingChain = true;
            scenario.Economy.CraftTree1 = true;
            scenario.Economy.CraftTree2 = enabled;
            scenario.Economy.CraftTreeJoineryPlankInput = 2;
            scenario.Economy.CraftTreeJoineryCutStoneInput = 1;
            scenario.Economy.CraftTreeJoineryTicks = 80;
            scenario.Economy.AssemblerSizeCells = 2;
            scenario.Economy.AssemblerWoodCost = 120;
            scenario.Economy.AssemblerWork = 300;
            scenario.Economy.AssemblerHp = 600;
            scenario.Economy.ArmsKitTicks = 80;
            scenario.Economy.ScholarlyTicks = 100;
            scenario.Economy.ArmsKitAttack = 2;
            scenario.Economy.ArmsKitHpPermille = 1100;
            scenario.Economy.FineArmsKitAttack = 3;
            scenario.Economy.FineArmsKitHpPermille = 1150;
            scenario.Economy.ScholarlyPerResearch = 1;
            scenario.Economy.ScholarlyCostPermille = 600;
            scenario.Economy.ScholarlyTimePermille = 700;
            scenario.Economy.GoodsValueArmsKit = 6;
            scenario.Economy.GoodsValueScholarly = 8;
            scenario.Economy.GoodsValueFineArmsKit = 14;
            return scenario;
        }

        [Test]
        public void Extension42RoundTripsAllCraftTree2Rules()
        {
            var source = Scenario(true);
            source.Economy.ArmsKitTicks = 83;
            source.Economy.ScholarlyTimePermille = 677;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 42);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftTree2, Is.True);
            Assert.That(decoded.Economy.ArmsKitTicks, Is.EqualTo(83));
            Assert.That(decoded.Economy.ScholarlyTimePermille, Is.EqualTo(677));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void CraftTree2RequiresCraftTree1AndOffStateHasNoExtension()
        {
            var source = Scenario(true);
            source.Economy.CraftTree1 = false;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Economy.CraftTree2, Is.False);
            Assert.That(decoded.Extensions.Any(x => x.Id == 42), Is.False);
        }

        [Test]
        public void SetRecipeInputRoundTripsWithRecipeByte()
        {
            var input = new ScheduledInput(12, 0, 1, EconomyCommand.SetRecipe(1, 4, 9, RecipeKind.Scholarly));
            var copy = InputBinary.Decode(InputBinary.Encode(input));
            Assert.That(copy.Economy.Kind, Is.EqualTo(EconomyCommandKind.SetRecipe));
            Assert.That(copy.Economy.ProducerId, Is.EqualTo(9u));
            Assert.That(copy.Economy.Recipe, Is.EqualTo(RecipeKind.Scholarly));
            Assert.That(InputBinary.Encode(copy), Is.EqualTo(InputBinary.Encode(input)));
        }

        [Test]
        public void AssemblerIsAgeTwoOnlyAndCraftTimeUsesConfiguredTicks()
        {
            var simulation = new Battle(Scenario(true));
            var method = typeof(Battle).GetMethod("CraftTicksFor", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var building = Activator.CreateInstance(buildingType);
            buildingType.GetField("Level", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(building, (byte)1);
            Assert.That(method.Invoke(simulation, new[] { building, (object)80 }), Is.EqualTo(80));
            var allowed = typeof(Battle).GetMethod("AssemblerAllowed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(allowed, Is.Not.Null);
            Assert.That(allowed.Invoke(simulation, new object[] { 1u }), Is.False);
        }

        [Test]
        public void ArmsKitExcludesVillagerScoutAndMonk()
        {
            var simulation = new Battle(Scenario(true));
            var method = typeof(Battle).GetMethod("UsesArmsKit", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Assert.That((bool)method.Invoke(simulation, new object[] { UnitKind.Infantry }), Is.True);
            Assert.That((bool)method.Invoke(simulation, new object[] { UnitKind.Villager }), Is.False);
            Assert.That((bool)method.Invoke(simulation, new object[] { UnitKind.Scout }), Is.False);
            Assert.That((bool)method.Invoke(simulation, new object[] { UnitKind.Monk }), Is.False);
        }

        [Test]
        public void AssemblerDoesNothingWithoutRecipeThenProducesArmsAndRefundsBufferedInputs()
        {
            var simulation = new Battle(Scenario(true));
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var economyType = typeof(Battle).Assembly.GetType("Rts.Simulation.FactionEconomy");
            var call = typeof(Battle).GetMethod("AdvanceAssembler", PrivateInstance);
            var building = Activator.CreateInstance(buildingType);
            var economy = Activator.CreateInstance(economyType);
            Set(building, "Recipe", RecipeKind.None); Set(economy, "Planks", 1); Set(economy, "Charcoal", 2);
            var args = new object[] { building, economy };
            call.Invoke(simulation, args);
            Assert.That(Get(args[1], "ArmsKit"), Is.EqualTo(0));
            Assert.That(Get(args[1], "Planks"), Is.EqualTo(1));

            building = Activator.CreateInstance(buildingType); economy = Activator.CreateInstance(economyType);
            Set(building, "Recipe", RecipeKind.ArmsKit); Set(economy, "Planks", 1); Set(economy, "Charcoal", 2);
            args = new object[] { building, economy }; call.Invoke(simulation, args);
            Assert.That(Get(args[0], "Timer"), Is.EqualTo(80));
            Assert.That(Get(args[1], "Planks"), Is.EqualTo(0));
            for (int i = 0; i < 80; i++) call.Invoke(simulation, args);
            Assert.That(Get(args[1], "ArmsKit"), Is.EqualTo(1));

            var refund = typeof(Battle).GetMethod("RefundAssemblerInputs", PrivateInstance);
            building = Activator.CreateInstance(buildingType);
            Set(building, "Recipe", RecipeKind.ArmsKit); Set(building, "Input", 1); Set(building, "InputSecondary", 2); Set(building, "InputSecondaryKind", ResourceKind.Charcoal);
            refund.Invoke(simulation, new object[] { 1u, building });
            Assert.That(GetEconomy(simulation, 0, "Planks"), Is.EqualTo(1));
            Assert.That(GetEconomy(simulation, 0, "Charcoal"), Is.EqualTo(2));
        }

        [Test]
        public void BrickSubstitutesCutStoneAndSteelMakesFineArmsKit()
        {
            var simulation = new Battle(Scenario(true));
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var economyType = typeof(Battle).Assembly.GetType("Rts.Simulation.FactionEconomy");
            var call = typeof(Battle).GetMethod("AdvanceAssembler", PrivateInstance);
            var building = Activator.CreateInstance(buildingType);
            var economy = Activator.CreateInstance(economyType);
            Set(building, "Recipe", RecipeKind.Frames); Set(economy, "Planks", 2); Set(economy, "Bricks", 1);
            var args = new object[] { building, economy }; call.Invoke(simulation, args);
            Assert.That(Get(args[1], "Bricks"), Is.EqualTo(0));
            Assert.That(Get(args[0], "Timer"), Is.GreaterThan(0));

            building = Activator.CreateInstance(buildingType); economy = Activator.CreateInstance(economyType);
            Set(building, "Recipe", RecipeKind.ArmsKit); Set(economy, "Planks", 1); Set(economy, "Steel", 1);
            args = new object[] { building, economy }; call.Invoke(simulation, args);
            Assert.That(Get(args[0], "InputSecondaryKind"), Is.EqualTo((int)ResourceKind.Steel));
            for (int i = 0; i < 80; i++) call.Invoke(simulation, args);
            Assert.That(Get(args[1], "FineArmsKit"), Is.EqualTo(1));
        }

        [Test]
        public void ScholarlyDiscountUsesCeiling()
        {
            var simulation = new Battle(Scenario(true));
            var discount = typeof(Battle).GetMethod("ScholarlyDiscount", PrivateInstance);
            Assert.That(discount.Invoke(simulation, new object[] { 100, 600 }), Is.EqualTo(60));
            Assert.That(discount.Invoke(simulation, new object[] { 101, 600 }), Is.EqualTo(61));
        }

        [Test, Explicit("30000 tickの旗オフ全tick一致検証")]
        public void CraftTree2OffMatchesAfterRoundTripEveryTick()
        {
            var scenario = LiveGameRules.Create(5, false, true, false, false);
            scenario.Economy.CraftTree2 = false;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.CraftTree2, Is.False);
            Assert.That(decoded.Extensions.Any(x => x.Id == 42), Is.False);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var left = new Battle(scenario);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test, Explicit("長い自動対自動の検証")]
        public void AutoEconomyBuildsAssemblerSelectsRecipeAndProducesCraftedGoods()
        {
            var simulation = new Battle(LiveGameRules.Create(1, false, true, false, false));
            for (long tick = 1; tick <= 30000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            bool selected = false, produced = false;
            for (int faction = 0; faction < 2; faction++)
            {
                var economy = GetEconomyObject(simulation, faction);
                produced |= Get(economy, "ArmsKitProduced") > 0 || Get(economy, "FineArmsKitProduced") > 0 || Get(economy, "ScholarlyProduced") > 0;
                var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
                var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
                foreach (var building in buildings)
                    if ((BuildingKind)building.GetType().GetField("Kind", PrivateInstance).GetValue(building) == BuildingKind.Assembler
                        && (RecipeKind)building.GetType().GetField("Recipe", PrivateInstance).GetValue(building) != RecipeKind.None)
                        selected = true;
            }
            Assert.That(selected, Is.True);
            Assert.That(produced, Is.True);
        }

        [Test]
        public void CraftTree2ReplayStaysDeterministicWhenEnabled()
        {
            using (var stream = new MemoryStream())
            {
                var scenario = Scenario(true);
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 1200, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        private static void Set(object value, string name, object fieldValue)
        {
            value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(value, fieldValue);
        }

        private static int Get(object value, string name)
        {
            object fieldValue = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
            return fieldValue is Enum ? Convert.ToInt32(fieldValue) : Convert.ToInt32(fieldValue);
        }

        private static int GetEconomy(Battle simulation, int index, string name)
        {
            return Get(GetEconomyObject(simulation, index), name);
        }

        private static object GetEconomyObject(Battle simulation, int index)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var economies = (Array)world.GetType().GetField("Economies", PrivateInstance).GetValue(world);
            return economies.GetValue(index);
        }
    }
}
