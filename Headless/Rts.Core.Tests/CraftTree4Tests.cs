using System;
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
    public sealed class CraftTree4Tests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(62042UL, false, true);
            scenario.Economy.Ages = true; scenario.Economy.AgeFour = true; scenario.Economy.Industry = true;
            scenario.Economy.ProcessingChain = true; scenario.Economy.CraftTree1 = true;
            scenario.Economy.CraftTree2 = true; scenario.Economy.CraftTree3 = true;
            scenario.Economy.CraftTree4 = enabled; scenario.Economy.SiegeByAge = false;
            return scenario;
        }

        [Test]
        public void Extension63RoundTripsAndIsOmittedWhenOff()
        {
            var source = Scenario(true); source.Economy.SiegeEngineTicks = 201; source.Economy.CannonAutoLimit = 6;
            var bytes = ScenarioBinary.Encode(source); var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 63).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.SiegeEngineTicks, Is.EqualTo(201));
            Assert.That(decoded.Economy.CannonAutoLimit, Is.EqualTo(6));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var off = Scenario(false);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 63), Is.False);
        }

        [Test]
        public void FourthAgeRecipesAreGatedAndUseTheirConfiguredTimers()
        {
            var simulation = new Battle(Scenario(true));
            var allowed = typeof(Battle).GetMethod("CraftTree4Allowed", PrivateInstance);
            Assert.That((bool)allowed.Invoke(simulation, new object[] { 1u }), Is.False);
            SetEconomy(simulation, 0, "Age", (byte)4);
            Assert.That((bool)allowed.Invoke(simulation, new object[] { 1u }), Is.True);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var economyType = typeof(Battle).Assembly.GetType("Rts.Simulation.FactionEconomy");
            var building = Activator.CreateInstance(buildingType); var economy = Activator.CreateInstance(economyType);
            Set(building, "FactionId", 1u); Set(building, "Recipe", RecipeKind.SiegeEngine); Set(economy, "SiegeParts", 2); Set(economy, "Mechanism", 1);
            var advance = typeof(Battle).GetMethod("AdvanceAssembler", PrivateInstance);
            var args = new object[] { building, economy }; advance.Invoke(simulation, args);
            Assert.That(Get(args[0], "Timer"), Is.EqualTo(200));
            for (int i = 0; i < 200; i++) advance.Invoke(simulation, args);
            Assert.That(Get(args[1], "SiegeEngine"), Is.EqualTo(1));
        }

        [Test]
        public void EliteKitPrecedesArmsKitAndCannonConsumesOneEngine()
        {
            var simulation = new Battle(Scenario(true)); SetEconomy(simulation, 0, "Age", (byte)4);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var building = Activator.CreateInstance(buildingType); Set(building, "FactionId", 1u); Set(building, "Kind", BuildingKind.Barracks);
            SetEconomy(simulation, 0, "Food", 10000); SetEconomy(simulation, 0, "Wood", 10000); SetEconomy(simulation, 0, "Metal", 10000);
            SetEconomy(simulation, 0, "EliteKit", 1); SetEconomy(simulation, 0, "ArmsKit", 1);
            Enqueue(simulation, building, UnitKind.Infantry);
            Assert.That(GetEconomy(simulation, 0, "EliteKitConsumed"), Is.EqualTo(1));
            Assert.That(GetEconomy(simulation, 0, "ArmsKitConsumed"), Is.EqualTo(0));

            building = Activator.CreateInstance(buildingType); Set(building, "FactionId", 1u); Set(building, "Kind", BuildingKind.SiegeWorkshop);
            SetEconomy(simulation, 0, "SiegeEngine", 1); Enqueue(simulation, building, UnitKind.Cannon);
            Assert.That(GetEconomy(simulation, 0, "SiegeEngine"), Is.EqualTo(0));
            Assert.That(GetEconomy(simulation, 0, "SiegeEngineConsumed"), Is.EqualTo(1));
        }

        [Test]
        public void CannonUsesConfiguredDoubleRamSiegeDamage()
        {
            var simulation = new Battle(Scenario(true));
            var damage = typeof(Battle).GetMethod("SiegeDamage", PrivateInstance);
            var soldierType = typeof(Battle).Assembly.GetType("Rts.Simulation.SoldierState");
            var cannon = Activator.CreateInstance(soldierType); Set(cannon, "Initial", new SoldierDefinition { FactionId = 1, Kind = UnitKind.Cannon }); Set(cannon, "Class", UnitKind.Cannon);
            var ram = Activator.CreateInstance(soldierType); Set(ram, "Initial", new SoldierDefinition { FactionId = 1, Kind = UnitKind.Ram }); Set(ram, "Class", UnitKind.Ram);
            Assert.That((int)damage.Invoke(simulation, new[] { cannon }), Is.EqualTo(120));
            Assert.That((int)damage.Invoke(simulation, new[] { ram }), Is.EqualTo(60));
        }

        [Test]
        public void AgeFourAiChoosesSiegeEngineWhenInputsExist()
        {
            var simulation = new Battle(Scenario(true)); SetEconomy(simulation, 0, "Age", (byte)4);
            InstallBuilding(simulation, BuildingKind.Assembler, 1, 1);
            SetEconomy(simulation, 0, "SiegeParts", 2); SetEconomy(simulation, 0, "Mechanism", 1);
            typeof(Battle).GetMethod("DecideCraftTree4", PrivateInstance).Invoke(simulation, new object[] { 1u });
            Assert.That(GetBuilding(simulation, 0, "Recipe"), Is.EqualTo(RecipeKind.SiegeEngine));
        }

        [Test, Explicit("30000 tickの旗オフ全tick一致検証")]
        public void CraftTree4OffMatchesAfterRoundTripEveryTick()
        {
            var scenario = Scenario(false); var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario));
            var left = new Battle(scenario); var right = new Battle(decoded);
            for (long tick = 1; tick <= 30000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState), Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void CraftTree4RecordAndReplayHaveMatchingTickHashes()
        {
            using (var stream = new System.IO.MemoryStream())
            {
                var result = ReplayRunner.Record(stream, Scenario(true), Array.Empty<ScheduledInput>(), 1200, new BuildIdentity());
                Assert.That(result.IsFault, Is.False); stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(replay.IsFault, Is.False); Assert.That(replay.FirstMismatchTick, Is.Null);
            }
        }

        private static void Enqueue(Battle simulation, object building, UnitKind kind)
        {
            var method = typeof(Battle).GetMethod("Enqueue", PrivateInstance);
            var args = new object[] { 1u, building, kind }; method.Invoke(simulation, args);
        }

        private static void Set(object value, string name, object fieldValue)
        {
            var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing field " + name);
            field.SetValue(value, fieldValue);
        }

        private static int Get(object value, string name) => Convert.ToInt32(value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value));

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

        private static void InstallBuilding(Battle simulation, BuildingKind kind, byte level, uint id)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var buildings = Array.CreateInstance(buildingType, 4); var building = Activator.CreateInstance(buildingType);
            Set(building, "Id", id); Set(building, "FactionId", 1u); Set(building, "Kind", kind); Set(building, "Alive", true); Set(building, "Complete", true); Set(building, "Level", level);
            buildings.SetValue(building, (int)id - 1); Set(world, "Buildings", buildings); Set(world, "NextBuildingId", id + 1u);
        }

        private static object GetBuilding(Battle simulation, int index, string name)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            return buildings.GetValue(index).GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(buildings.GetValue(index));
        }
    }
}
