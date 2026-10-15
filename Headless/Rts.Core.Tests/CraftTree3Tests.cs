using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Application;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CraftTree3Tests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(62042UL, false, true);
            scenario.Economy.Ages = true; scenario.Economy.Industry = true; scenario.Economy.ProcessingChain = true;
            scenario.Economy.CraftTree1 = true; scenario.Economy.CraftTree2 = true; scenario.Economy.CraftTree3 = enabled;
            return scenario;
        }

        [Test]
        public void Extension43RoundTripsAndIsOmittedWhenOff()
        {
            var source = Scenario(true);
            source.Economy.SiegePartsTicks = 121; source.Economy.MechanismResearchTimePermille = 499;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 43).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftTree3, Is.True);
            Assert.That(decoded.Economy.SiegePartsTicks, Is.EqualTo(121));
            Assert.That(decoded.Economy.MechanismResearchTimePermille, Is.EqualTo(499));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = Scenario(false);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 43), Is.False);
        }

        [Test, Explicit("30000 tickの旗オフ全tick一致検証")]
        public void CraftTree3OffMatchesAfterRoundTripEveryTick()
        {
            var scenario = LiveGameRules.Create(5, false, true, false, false);
            scenario.Economy.CraftTree3 = false;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 43), Is.False);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var left = new Battle(scenario); var right = new Battle(decoded);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void ThirdAgeRecipesAreAgeGatedAndCraftAtConfiguredTicks()
        {
            var simulation = new Battle(Scenario(true));
            var allowed = typeof(Battle).GetMethod("CraftTree3Allowed", PrivateInstance);
            Assert.That((bool)allowed.Invoke(simulation, new object[] { 1u }), Is.False);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var economyType = typeof(Battle).Assembly.GetType("Rts.Simulation.FactionEconomy");
            var advance = typeof(Battle).GetMethod("AdvanceAssembler", PrivateInstance);
            var building = Activator.CreateInstance(buildingType); var economy = Activator.CreateInstance(economyType);
            Set(building, "Recipe", RecipeKind.SiegeParts); Set(economy, "Frames", 1); Set(economy, "ArmsKit", 1);
            var args = new object[] { building, economy }; advance.Invoke(simulation, args);
            Assert.That(Get(args[0], "Timer"), Is.EqualTo(120));
            for (int i = 0; i < 120; i++) advance.Invoke(simulation, args);
            Assert.That(Get(args[1], "SiegeParts"), Is.EqualTo(1));
        }

        [Test]
        public void LevelThreeUsesMechanismAndHalfTimeAndReducedMaterial()
        {
            var simulation = new Battle(Scenario(true));
            var type = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var building = Activator.CreateInstance(type);
            Set(building, "Level", (byte)3);
            var ticks = typeof(Battle).GetMethod("CraftTicksFor", PrivateInstance);
            var input = typeof(Battle).GetMethod("CraftInputFor", PrivateInstance);
            Assert.That(ticks.Invoke(simulation, new object[] { building, 151 }), Is.EqualTo(76));
            Assert.That(input.Invoke(simulation, new object[] { building, 10 }), Is.EqualTo(9));
        }

        [Test]
        public void LevelOneCannotJumpToLevelThreeAndUpgradeConsumesMechanism()
        {
            var simulation = new Battle(Scenario(true));
            InstallBuilding(simulation, BuildingKind.Assembler, 1, 1);
            SetEconomy(simulation, 0, "Mechanism", 1);
            var upgrade = typeof(Battle).GetMethod("UpgradeProcessingBuilding", PrivateInstance);
            upgrade.Invoke(simulation, new object[] { 1u, 1u });
            Assert.That(GetBuilding(simulation, 0, "Level"), Is.EqualTo(1));

            SetEconomy(simulation, 0, "Frames", 2);
            upgrade.Invoke(simulation, new object[] { 1u, 1u });
            Assert.That(GetBuilding(simulation, 0, "Level"), Is.EqualTo(2));
            SetEconomy(simulation, 0, "Mechanism", 1);
            upgrade.Invoke(simulation, new object[] { 1u, 1u });
            Assert.That(GetBuilding(simulation, 0, "Level"), Is.EqualTo(3));
            Assert.That(GetEconomy(simulation, 0, "MechanismConsumed"), Is.EqualTo(1));
        }

        [Test]
        public void SiegePartsEffectIsControlledByTheThirdTree()
        {
            var scenario = Scenario(true); scenario.Economy.SiegeByAge = false; scenario.Economy.CastleStoneCost = 100;
            var simulation = new Battle(scenario);
            var damage = typeof(Battle).GetMethod("SiegeDamage", PrivateInstance);
            Assert.That(damage, Is.Not.Null);
            Assert.That(typeof(ResourceKind).GetField("SiegeParts"), Is.Not.Null);
            Assert.That(typeof(ResourceKind).GetField("Mechanism"), Is.Not.Null);

            var soldierType = typeof(Battle).Assembly.GetType("Rts.Simulation.SoldierState");
            var soldier = Activator.CreateInstance(soldierType);
            Set(soldier, "Initial", new SoldierDefinition { FactionId = 1, Kind = UnitKind.Ram });
            Set(soldier, "Class", UnitKind.Ram); Set(soldier, "SiegePartsEnhanced", true);
            Assert.That((100 * scenario.Economy.SiegePartsHpPermille + 999) / 1000, Is.EqualTo(150));
            Assert.That(damage.Invoke(simulation, new[] { soldier }), Is.EqualTo(78));

            SetEconomy(simulation, 0, "Age", (byte)3); SetEconomy(simulation, 0, "SiegeParts", 3);
            var stone = typeof(Battle).GetMethod("StoneOf", PrivateInstance);
            Assert.That(stone.Invoke(simulation, new object[] { BuildingKind.Castle, 1u }), Is.EqualTo(70));
        }

        [Test]
        public void MechanismAndScholarlyAreSeparateResources()
        {
            Assert.That((byte)RecipeKind.SiegeParts, Is.EqualTo(4));
            Assert.That((byte)RecipeKind.Mechanism, Is.EqualTo(5));
            Assert.That((byte)ResourceKind.SiegeParts, Is.EqualTo(27));
            Assert.That((byte)ResourceKind.Mechanism, Is.EqualTo(28));
        }

        [Test]
        public void MechanismResearchDiscountIsThirdAgeOnlyAndPrecedesScholarly()
        {
            var scenario = Scenario(true); var simulation = new Battle(scenario);
            var thirdAge = typeof(Battle).GetMethod("IsThirdAgeResearch", PrivateInstance);
            var discount = typeof(Battle).GetMethod("ScholarlyDiscount", PrivateInstance);
            Assert.That((bool)thirdAge.Invoke(simulation, new object[] { TechKind.Siegecraft }), Is.True);
            Assert.That((bool)thirdAge.Invoke(simulation, new object[] { TechKind.Weapons }), Is.False);
            Assert.That(discount.Invoke(simulation, new object[] { 101, 500 }), Is.EqualTo(51));

            InstallBuilding(simulation, BuildingKind.Blacksmith, 1, 1);
            SetEconomy(simulation, 0, "Age", (byte)3); SetEconomy(simulation, 0, "Civ", CivKind.Agrarian);
            SetEconomy(simulation, 0, "Food", 10000); SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Metal", 10000); SetEconomy(simulation, 0, "Ore", 10000);
            SetEconomy(simulation, 0, "Gems", 10000); SetEconomy(simulation, 0, "Mechanism", 1); SetEconomy(simulation, 0, "Scholarly", 1);
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            var blacksmith = buildings.GetValue(0);
            var start = typeof(Battle).GetMethod("StartResearch", PrivateInstance);
            var args = new object[] { 1u, blacksmith, TechKind.Siegecraft, false };
            Assert.That((bool)start.Invoke(simulation, args), Is.True);
            Assert.That(GetEconomy(simulation, 0, "Mechanism"), Is.EqualTo(0));
            Assert.That(GetEconomy(simulation, 0, "Scholarly"), Is.EqualTo(1));
            Assert.That(Get(args[1], "TrainRemaining"), Is.GreaterThan(0));
        }

        [Test]
        public void CraftTree3ReplayWithRecipeAndUpgradeCommandsStaysDeterministic()
        {
            var scenario = Scenario(true);
            var inputs = new[]
            {
                new ScheduledInput(1, 0, 1, EconomyCommand.SetRecipe(1, 1, 999, RecipeKind.SiegeParts)),
                new ScheduledInput(2, 0, 1, EconomyCommand.UpgradeBuilding(1, 2, 999))
            };
            Assert.That(inputs[0].Economy.Kind, Is.EqualTo(EconomyCommandKind.SetRecipe));
            Assert.That(inputs[1].Economy.Kind, Is.EqualTo(EconomyCommandKind.UpgradeBuilding));
            using (var stream = new System.IO.MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, inputs, 1200, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        private static void Set(object value, string name, object fieldValue)
        {
            var field = value?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing field " + name + " on " + value?.GetType().FullName);
            field.SetValue(value, fieldValue);
        }

        private static int Get(object value, string name)
            => Convert.ToInt32(value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value));

        private static void InstallBuilding(Battle simulation, BuildingKind kind, byte level, uint id)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var buildings = Array.CreateInstance(buildingType, 4);
            var building = Activator.CreateInstance(buildingType);
            Set(building, "Id", id); Set(building, "FactionId", 1u); Set(building, "Kind", kind);
            Set(building, "Alive", true); Set(building, "Complete", true); Set(building, "Level", level);
            buildings.SetValue(building, 0);
            Set(world, "Buildings", buildings); Set(world, "NextBuildingId", id + 1u);
        }

        private static object GetBuilding(Battle simulation, int index, string name)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            return buildings.GetValue(index).GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(buildings.GetValue(index));
        }

        private static void SetEconomy(Battle simulation, int index, string name, object value)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var economies = (Array)world.GetType().GetField("Economies", PrivateInstance).GetValue(world);
            var economy = economies.GetValue(index); Set(economy, name, value); economies.SetValue(economy, index);
        }

        private static int GetEconomy(Battle simulation, int index, string name)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var economies = (Array)world.GetType().GetField("Economies", PrivateInstance).GetValue(world);
            return Get(economies.GetValue(index), name);
        }
    }
}
