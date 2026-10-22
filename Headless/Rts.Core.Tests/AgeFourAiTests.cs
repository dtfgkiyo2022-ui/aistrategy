using System;
using System.Collections.Generic;
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
    public sealed class AgeFourAiTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool ageFourAi)
        {
            var scenario = MapGenerator.GenerateTerrain(64064UL, false, true);
            var e = scenario.Economy;
            e.Ages = true; e.AgeFour = ageFourAi; e.AgeFourAi = ageFourAi; e.AgeFourAiIndustry = ageFourAi;
            e.Industry = true; e.ProcessingChain = ageFourAi;
            e.CraftTree1 = ageFourAi; e.CraftTree2 = ageFourAi; e.CraftTree3 = ageFourAi; e.CraftTree4 = ageFourAi;
            e.AgeClock = false; e.StartFood = 0; e.StartWood = 0; e.StartMetal = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }

        [Test]
        public void FlagOffRoundTripMatchesEveryTickAndEndState()
        {
            var source = WeekOneScenario.Create();
            source.VerificationTickLimit = 1;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Economy.AgeFourAi, Is.False);
            var left = new Battle(source); var right = new Battle(decoded);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
            Assert.That(left.Capture(1).Result.HasEnded, Is.EqualTo(right.Capture(1).Result.HasEnded));
        }

        [Test]
        public void Extension64RoundTripsDefaultsAndIsOmittedWhenOff()
        {
            var on = Scenario(true);
            on.Economy.AgeFourAiAssemblers = 4;
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 64).Version, Is.EqualTo(1));
            Assert.That((decoded.Economy.AgeFourAi, decoded.Economy.AgeFourAiIndustry, decoded.Economy.AgeFourAiAssemblers),
                Is.EqualTo((true, true, 4)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = Scenario(false);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 64), Is.False);
        }

        [Test]
        public void AgeFourResearchOnlyPreemptsWhenTotalWarOrGrandWorkshopIsAffordable()
        {
            var affordable = new Battle(Scenario(true));
            InstallBuildings(affordable, (BuildingKind.Farm, 1, 1u, RecipeKind.None, 0),
                (BuildingKind.Barracks, 1, 2u, RecipeKind.None, 0),
                (BuildingKind.Blacksmith, 1, 3u, RecipeKind.None, 0));
            SetEconomy(affordable, "Age", (byte)4); SetEconomy(affordable, "Civ", CivKind.Agrarian);
            SetEconomy(affordable, "Food", 100000); SetEconomy(affordable, "Wood", 100000); SetEconomy(affordable, "Metal", 100000);
            Invoke(affordable, "DecideResearch", 1u);
            Assert.That(Get(GetBuilding(affordable, 2), "Researching"), Is.EqualTo((int)TechKind.TotalWar));

            var normal = new Battle(Scenario(true));
            InstallBuildings(normal, (BuildingKind.Farm, 1, 1u, RecipeKind.None, 0),
                (BuildingKind.Barracks, 1, 2u, RecipeKind.None, 0),
                (BuildingKind.Blacksmith, 1, 3u, RecipeKind.None, 0));
            SetEconomy(normal, "Age", (byte)4); SetEconomy(normal, "Civ", CivKind.Agrarian);
            SetEconomy(normal, "Food", 100000); SetEconomy(normal, "Wood", 100); SetEconomy(normal, "Metal", 0);
            Invoke(normal, "DecideResearch", 1u);
            Assert.That(Get(GetBuilding(normal, 2), "Researching"), Is.EqualTo((int)TechKind.Tools));
        }

        [Test]
        public void FlagOnUsesTheFlagOffResearchStartsBeforeAgeFour()
        {
            var onScenario = Scenario(true);
            var offScenario = Scenario(true);
            offScenario.Economy.AgeFourAi = false;
            offScenario.Economy.AgeFourAiIndustry = false;
            var on = new Battle(onScenario); var off = new Battle(offScenario);
            var onPrevious = Researching(on); var offPrevious = Researching(off);
            var onStarts = new List<(long tick, int tech)>(); var offStarts = new List<(long tick, int tech)>();
            for (long tick = 1; tick <= 30000; tick++)
            {
                on.Step(tick, Array.Empty<ScheduledInput>()); off.Step(tick, Array.Empty<ScheduledInput>());
                var onResearching = Researching(on); var offResearching = Researching(off);
                foreach (var tech in onResearching)
                    if (!onPrevious.Contains(tech)) onStarts.Add((tick, tech));
                foreach (var tech in offResearching)
                    if (!offPrevious.Contains(tech)) offStarts.Add((tick, tech));
                onPrevious = onResearching; offPrevious = offResearching;
                if (GetEconomy(on, "Age") >= 4 || GetEconomy(off, "Age") >= 4) break;
            }
            Assert.That(onStarts, Is.EqualTo(offStarts));
        }

        [Test]
        public void AgeFourAiAssignsAssemblersInIdOrderAndNeverStartsThreeOfOneRecipe()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", (byte)4);
            SetEconomy(simulation, "SiegeParts", 2); SetEconomy(simulation, "Mechanism", 1);
            InstallBuildings(simulation,
                (BuildingKind.Assembler, 1, 1u, RecipeKind.Frames, 5),
                (BuildingKind.Assembler, 1, 2u, RecipeKind.None, 0),
                (BuildingKind.Assembler, 1, 3u, RecipeKind.None, 0));
            Invoke(simulation, "DecideAgeFourAiIndustry", 1u);
            var recipes = GetBuildings(simulation).Select(x => (RecipeKind)Get(x, "Recipe")).ToArray();
            Assert.That(recipes[0], Is.EqualTo(RecipeKind.Frames));
            Assert.That(Get(GetBuildings(simulation)[0], "Timer"), Is.EqualTo(5));
            Assert.That(recipes.Count(x => x == RecipeKind.SiegeEngine), Is.EqualTo(2));
        }

        [Test]
        public void AgeFourAiFallsBackToFramesWhenSiegePartsInputsAreMissing()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", (byte)4);
            SetEconomy(simulation, "Planks", 1); SetEconomy(simulation, "CutStone", 1);
            InstallBuilding(simulation, BuildingKind.Assembler, 1, 1);
            Invoke(simulation, "DecideAgeFourAiIndustry", 1u);
            Assert.That((RecipeKind)Get(GetBuildings(simulation)[0], "Recipe"), Is.EqualTo(RecipeKind.Frames));
        }

        [Test]
        public void AgeFourAiQueuesCannonWhenSiegeWorkshopAndEngineAreReady()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", (byte)4);
            SetEconomy(simulation, "Food", 100000); SetEconomy(simulation, "Wood", 100000);
            SetEconomy(simulation, "SiegeEngine", 1);
            InstallBuilding(simulation, BuildingKind.SiegeWorkshop, 1, 1);
            Invoke(simulation, "DecideSiege", 1u);
            var workshop = GetBuildings(simulation)[0];
            Assert.That(Get(workshop, "Queued"), Is.EqualTo(1));
            var kinds = (Array)workshop.GetType().GetField("QueueKinds", PrivateInstance).GetValue(workshop);
            Assert.That((UnitKind)kinds.GetValue(0), Is.EqualTo(UnitKind.Cannon));
        }

        [Test]
        public void AgeFourAiReservesCannonWoodOnlyForACompletedSiegeWorkshop()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", (byte)4); SetEconomy(simulation, "SiegeEngine", 1);
            Assert.That(InvokeResult(simulation, "AgeFourAiWoodReserve", 1u), Is.EqualTo(0));
            InstallBuilding(simulation, BuildingKind.SiegeWorkshop, 1, 1);
            Assert.That(InvokeResult(simulation, "AgeFourAiWoodReserve", 1u), Is.EqualTo(100));
            SetBuilding(simulation, 0, "Complete", false);
            Assert.That(InvokeResult(simulation, "AgeFourAiWoodReserve", 1u), Is.EqualTo(0));
        }

        [Test]
        public void AgeFourAiBuildsSiegeWorkshopForAnAgeFourSiegeEngine()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", (byte)4);
            SetEconomy(simulation, "Wood", 100000); SetEconomy(simulation, "SiegeEngine", 1);
            InstallBuildings(simulation, (BuildingKind.Barracks, 1, 1u, RecipeKind.None, 0));
            Invoke(simulation, "DecideSiege", 1u);
            Assert.That(GetBuildings(simulation).Any(x => (BuildingKind)x.GetType().GetField("Kind", PrivateInstance).GetValue(x) == BuildingKind.SiegeWorkshop), Is.True);
        }

        [Test]
        public void AgeFourAiStartsResearchWhenAgeThreeResearchIsAffordable()
        {
            var simulation = new Battle(Scenario(true));
            InstallBuildings(simulation, (BuildingKind.Farm, 1, 1u, RecipeKind.None, 0),
                (BuildingKind.Barracks, 1, 2u, RecipeKind.None, 0),
                (BuildingKind.Blacksmith, 1, 3u, RecipeKind.None, 0));
            SetEconomy(simulation, "Age", (byte)3);
            SetEconomy(simulation, "Civ", CivKind.Agrarian);
            SetEconomy(simulation, "Food", 100000); SetEconomy(simulation, "Wood", 100000); SetEconomy(simulation, "Metal", 100000);
            Invoke(simulation, "DecideResearch", 1u);
            Assert.That(Get(GetBuilding(simulation, 2), "Researching"), Is.Not.EqualTo(0));
        }

        [Test]
        public void AgeFourAiIndustryOffDoesNotAddProcessingBuildings()
        {
            var scenario = Scenario(true);
            scenario.Economy.AgeFourAiIndustry = false;
            var simulation = new Battle(scenario);
            Assert.That((bool)typeof(Battle).GetProperty("AgeFourAiOn", PrivateInstance).GetValue(simulation), Is.True);

            Invoke(simulation, "DecideAgeFourAiIndustry", 1u);
            Assert.That(GetBuildings(simulation).Any(x =>
                (BuildingKind)Get(x, "Kind") == BuildingKind.Sawmill
                || (BuildingKind)Get(x, "Kind") == BuildingKind.Stonecutter
                || (BuildingKind)Get(x, "Kind") == BuildingKind.CharcoalKiln
                || (BuildingKind)Get(x, "Kind") == BuildingKind.Assembler), Is.False);
        }

        [Test]
        public void AgeFourAiDoesNotAddProcessingBuildingsBeforeBarracksTarget()
        {
            var scenario = Scenario(true);
            scenario.Economy.EconomyScale = true;
            scenario.Economy.LargeEconomy = true;
            scenario.Economy.LargeBarracksVillagersPerBuilding = 1;
            var simulation = new Battle(scenario);
            SetEconomy(simulation, "Age", (byte)3);
            InstallBuilding(simulation, BuildingKind.Barracks, 1, 1);
            Invoke(simulation, "DecideAgeFourAiIndustry", 1u);
            Assert.That(GetBuildings(simulation).Any(x =>
                (BuildingKind)x.GetType().GetField("Kind", PrivateInstance).GetValue(x) == BuildingKind.Sawmill
                || (BuildingKind)x.GetType().GetField("Kind", PrivateInstance).GetValue(x) == BuildingKind.Stonecutter
                || (BuildingKind)x.GetType().GetField("Kind", PrivateInstance).GetValue(x) == BuildingKind.CharcoalKiln
                || (BuildingKind)x.GetType().GetField("Kind", PrivateInstance).GetValue(x) == BuildingKind.Assembler), Is.False);
        }

        [Test]
        public void AgeFourAiRecordReplayHasMatchingHashesForEveryTick()
        {
            var scenario = Scenario(true);
            var bytes = ScenarioBinary.Encode(scenario);
            var left = new Battle(scenario); var right = new Battle(ScenarioBinary.Decode(bytes));
            for (long tick = 1; tick <= 1200; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
            using (var stream = new System.IO.MemoryStream())
            {
                var result = ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 1200, new BuildIdentity());
                Assert.That(result.IsFault, Is.False);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(replay.IsFault, Is.False);
                Assert.That(replay.FirstMismatchTick, Is.Null);
            }
        }

        private static void Invoke(Battle simulation, string method, params object[] args)
            => typeof(Battle).GetMethod(method, PrivateInstance).Invoke(simulation, args);

        private static object InvokeResult(Battle simulation, string method, params object[] args)
            => typeof(Battle).GetMethod(method, PrivateInstance).Invoke(simulation, args);

        private static void Set(object value, string name, object fieldValue)
            => value.GetType().GetField(name, PrivateInstance).SetValue(value, fieldValue);

        private static int Get(object value, string name)
            => Convert.ToInt32(value.GetType().GetField(name, PrivateInstance).GetValue(value));

        private static int[] Researching(Battle simulation)
            => GetBuildings(simulation).Where(x => Get(x, "FactionId") == 1 && Get(x, "Alive") != 0
                && Get(x, "Reserved") == 0 && Get(x, "Researching") != 0)
                .Select(x => Get(x, "Researching")).OrderBy(x => x).ToArray();

        private static void SetEconomy(Battle simulation, string name, object value)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var economies = (Array)world.GetType().GetField("Economies", PrivateInstance).GetValue(world);
            var economy = economies.GetValue(0); Set(economy, name, value); economies.SetValue(economy, 0);
        }

        private static int GetEconomy(Battle simulation, string name)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var economies = (Array)world.GetType().GetField("Economies", PrivateInstance).GetValue(world);
            return Get(economies.GetValue(0), name);
        }

        private static object GetBuilding(Battle simulation, int index)
            => GetBuildings(simulation)[index];

        private static object[] GetBuildings(Battle simulation)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            var result = new object[buildings.Length];
            for (int i = 0; i < result.Length; i++) result[i] = buildings.GetValue(i);
            return result;
        }

        private static void SetBuilding(Battle simulation, int index, string name, object value)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            var building = buildings.GetValue(index);
            Set(building, name, value);
            buildings.SetValue(building, index);
        }

        private static void InstallBuilding(Battle simulation, BuildingKind kind, byte level, uint id)
            => InstallBuildings(simulation, (kind, level, id, RecipeKind.None, 0));

        private static void InstallBuildings(Battle simulation, params (BuildingKind kind, byte level, uint id, RecipeKind recipe, int timer)[] definitions)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var buildings = Array.CreateInstance(buildingType, definitions.Length);
            for (int i = 0; i < definitions.Length; i++)
            {
                var d = definitions[i]; var building = Activator.CreateInstance(buildingType);
                Set(building, "Id", d.id); Set(building, "FactionId", 1u); Set(building, "Kind", d.kind);
                Set(building, "Alive", true); Set(building, "Complete", true); Set(building, "Level", d.level);
                Set(building, "Recipe", d.recipe); Set(building, "Timer", d.timer);
                buildings.SetValue(building, i);
            }
            Set(world, "Buildings", buildings); Set(world, "NextBuildingId", (uint)(definitions.Length + 1));
        }

    }
}
