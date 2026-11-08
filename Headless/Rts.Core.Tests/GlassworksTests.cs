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
    [TestFixture]
    public sealed class GlassworksTests
    {
        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(ScenarioDefinition scenario)
            {
                Scenario = scenario;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++Sequence, false));
                Gateway.SubmitEconomy(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Step(int count)
            {
                for (int i = 0; i < count && !Simulation.Capture(1).Result.HasEnded; i++) Gateway.Step();
            }
        }

        private static ScenarioDefinition ProductionScenario()
        {
            var scenario = MapGenerator.GenerateTerrain(9601UL);
            scenario.Economy.Industry = true;
            scenario.Economy.ProcessingChain = true;
            scenario.Economy.CarriedInputs = false;
            scenario.Economy.Glassworks = true;
            scenario.Economy.GlassTicks = 3;
            scenario.Economy.LensTicks = 3;
            return scenario;
        }

        private static uint Place(Fixture fixture, BuildingKind kind)
        {
            WorldState state = State(fixture.Simulation);
            int before = state.BuildingCount;
            int cells = fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, kind, cell, Facing.North));
                fixture.Step(1);
                if (state.BuildingCount == before + 1 && state.Buildings[before].Kind == kind) return (uint)(before + 1);
            }
            Assert.Fail("building placement failed: " + kind);
            return 0;
        }

        [Test]
        public void ContractValuesAreAppended()
        {
            Assert.That((byte)ResourceKind.Sand, Is.EqualTo(37));
            Assert.That((byte)ResourceKind.Glass, Is.EqualTo(38));
            Assert.That((byte)ResourceKind.Lens, Is.EqualTo(39));
            Assert.That((byte)BuildingKind.GlassWorks, Is.EqualTo(54));
            Assert.That((byte)RecipeKind.Lens, Is.EqualTo(10));
        }

        [Test]
        public void LargeGlassMapAddsTwentyFourPointSymmetricSandNodesAfterTheOldNodes()
        {
            var scenario = MapGenerator.GenerateLarge(9602UL, glassworks: true);
            var sand = scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Sand).ToArray();
            Assert.That(sand.Length, Is.EqualTo(24));
            Assert.That(sand[0].Id, Is.EqualTo(scenario.ResourceNodes.Length - 23u));
            for (int i = 0; i < sand.Length; i += 2)
            {
                Assert.That(sand[i + 1].Kind, Is.EqualTo(ResourceKind.Sand));
                Assert.That(sand[i].Amount, Is.EqualTo(sand[i + 1].Amount));
                Assert.That(sand[i + 1].Position.X.Raw, Is.EqualTo(Fix64.FromInt(512).Raw - sand[i].Position.X.Raw));
                Assert.That(sand[i + 1].Position.Z.Raw, Is.EqualTo(Fix64.FromInt(256).Raw - sand[i].Position.Z.Raw));
            }
        }

        [Test]
        public void FlagOffLeavesLargeMapAndScenarioExtensionUnchanged()
        {
            var first = MapGenerator.GenerateLarge(9603UL);
            var second = MapGenerator.GenerateLarge(9603UL, glassworks: false);
            Assert.That(first.ResourceNodes.Select(n => n.Kind), Is.EqualTo(second.ResourceNodes.Select(n => n.Kind)));
            Assert.That(ScenarioBinary.Encode(first), Is.EqualTo(ScenarioBinary.Encode(second)));
            Assert.That(first.ResourceNodes.Any(n => n.Kind == ResourceKind.Sand), Is.False);
            Assert.That(first.Extensions.Any(e => e.Id == 96), Is.False);
        }

        [Test]
        public void Extension96RoundTripsTheExactTwentyFourInt32Payload()
        {
            var source = MapGenerator.GenerateTerrain(9604UL);
            var e = source.Economy;
            e.Glassworks = true;
            e.GlassSandInput = 12; e.GlassCharcoalInput = 13; e.GlassTicks = 14;
            e.GlassSandInputCapacity = 15; e.GlassCharcoalInputCapacity = 16; e.GlassOutputCapacity = 17;
            e.GlassWorksSizeCells = 18; e.GlassWorksWoodCost = 19; e.GlassWorksStoneCost = 20;
            e.GlassWorksWork = 21; e.GlassWorksHp = 22; e.LensGlassInput = 23; e.LensGearsInput = 24; e.LensTicks = 25;
            e.ArcherLensCost = 26; e.TowerGlassCost = 27; e.CastleGlassCost = 28; e.TowerGlassVisionBonus = 29;
            e.GlassMaxLines = 30; e.SandNearNodes = 31; e.SandClusterNodes = 32;
            e.LensGearStockBonusWest = 33; e.LensGearStockBonusEast = 34;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Glassworks, Is.True);
            Assert.That(new[] { decoded.Economy.GlassSandInput, decoded.Economy.GlassCharcoalInput, decoded.Economy.GlassTicks,
                decoded.Economy.GlassSandInputCapacity, decoded.Economy.GlassCharcoalInputCapacity, decoded.Economy.GlassOutputCapacity,
                decoded.Economy.GlassWorksSizeCells, decoded.Economy.GlassWorksWoodCost, decoded.Economy.GlassWorksStoneCost,
                decoded.Economy.GlassWorksWork, decoded.Economy.GlassWorksHp, decoded.Economy.LensGlassInput,
                decoded.Economy.LensGearsInput, decoded.Economy.LensTicks, decoded.Economy.ArcherLensCost,
                decoded.Economy.TowerGlassCost, decoded.Economy.CastleGlassCost, decoded.Economy.TowerGlassVisionBonus,
                decoded.Economy.GlassMaxLines, decoded.Economy.SandNearNodes, decoded.Economy.SandClusterNodes,
                decoded.Economy.LensGearStockBonusWest, decoded.Economy.LensGearStockBonusEast },
                Is.EqualTo(Enumerable.Range(12, 23).ToArray()));
            Assert.That(decoded.Extensions.Any(e2 => e2.Id == 96 && e2.Version == 1 && e2.Data.Length == 24 * sizeof(int)), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void LensGearStockBonusRoundTripsWithGlassworksAndMatchesTheSavedRun()
        {
            var source = ProductionScenario();
            var sourceSimulation = new Battle(source);
            WorldState sourceState = State(sourceSimulation);
            sourceState.Economies[0].Age = 1;
            sourceState.Buildings = new[] { new BuildingState { Id = 1, FactionId = 1,
                Kind = BuildingKind.Assembler, Alive = true, Complete = true, Recipe = RecipeKind.Lens } };
            sourceState.NextBuildingId = 2;
            MethodInfo decide = typeof(Battle).GetMethod("DecideGlassworks", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(decide, Is.Not.Null);
            decide.Invoke(sourceSimulation, new object[] { 1u });
            Assert.That(sourceState.LensGearStockBonus[0], Is.EqualTo(20));
            source.Economy.LensGearStockBonusWest = sourceState.LensGearStockBonus[0];
            source.Economy.LensGearStockBonusEast = sourceState.LensGearStockBonus[1];
            var saved = ScenarioBinary.Encode(source);
            var loaded = ScenarioBinary.Decode(saved);
            var simulation = new Battle(loaded);
            WorldState state = State(simulation);
            Assert.That(state.LensGearStockBonus, Is.EqualTo(new[] { 20, 0 }));

            var original = new Battle(source);
            WorldState originalState = State(original);
            for (int tick = 1; tick <= 1200; tick++)
            {
                original.Step(tick, Array.Empty<ScheduledInput>());
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(original.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState)), "tick=" + tick);
            }
            Assert.That(originalState.LensGearStockBonus, Is.EqualTo(state.LensGearStockBonus));
        }

        [Test]
        public void GlassWorksConsumesSandAndCharcoalAndProducesOneGlassAfterEightyTicks()
        {
            var fixture = new Fixture(ProductionScenario());
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Stone = 10000;
            uint id = Place(fixture, BuildingKind.GlassWorks);
            ref BuildingState glassworks = ref state.Buildings[(int)id - 1];
            glassworks.Complete = true;
            glassworks.Input = fixture.Scenario.Economy.GlassSandInput;
            glassworks.InputSecondary = fixture.Scenario.Economy.GlassCharcoalInput;
            int before = state.Economies[0].GlassProduced;
            fixture.Step(fixture.Scenario.Economy.GlassTicks);
            Assert.That(state.Economies[0].GlassProduced, Is.EqualTo(before + 1));
            Assert.That(glassworks.Input, Is.EqualTo(0));
            Assert.That(glassworks.InputSecondary, Is.EqualTo(0));
            Assert.That(glassworks.Output, Is.EqualTo(1));
        }

        [Test]
        public void LensRecipeConsumesGlassAndGearsAtAgeOneAssembler()
        {
            var fixture = new Fixture(ProductionScenario());
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            uint id = Place(fixture, BuildingKind.Assembler);
            ref BuildingState assembler = ref state.Buildings[(int)id - 1];
            assembler.Complete = true;
            assembler.Recipe = RecipeKind.Lens;
            state.Economies[0].Glass = fixture.Scenario.Economy.LensGlassInput;
            state.Economies[0].Gears = fixture.Scenario.Economy.LensGearsInput;
            int before = state.Economies[0].LensProduced;
            fixture.Step(fixture.Scenario.Economy.LensTicks + 1);
            Assert.That(state.Economies[0].LensProduced, Is.EqualTo(before + 1));
            Assert.That(state.Economies[0].Glass, Is.EqualTo(0));
            Assert.That(state.Economies[0].Gears, Is.EqualTo(0));
        }

        [Test]
        public void GearWorkshopProducesStockGearsThatLensAssemblerCanConsume()
        {
            var scenario = ProductionScenario();
            scenario.Economy.Ages = true;
            scenario.Economy.CraftTree1 = true;
            scenario.Economy.CraftTree2 = true;
            scenario.Economy.BeltStack = true;
            scenario.Economy.CraftRequired = true;
            scenario.Economy.BeltCraft = true;
            scenario.Economy.GearTicks = 3;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Stone = 10000;
            state.Economies[0].Metal = scenario.Economy.GearMetalInput;
            uint gearId = Place(fixture, BuildingKind.GearWorkshop);
            ref BuildingState gearWorkshop = ref state.Buildings[(int)gearId - 1];
            gearWorkshop.Complete = true;
            uint assemblerId = Place(fixture, BuildingKind.Assembler);
            ref BuildingState assembler = ref state.Buildings[(int)assemblerId - 1];
            assembler.Complete = true;
            assembler.Recipe = RecipeKind.Lens;
            state.Economies[0].Glass = scenario.Economy.LensGlassInput;
            int before = state.Economies[0].LensProduced;

            fixture.Step(scenario.Economy.GearTicks + scenario.Economy.LensTicks + 2);

            Assert.That(state.Economies[0].GearsProduced, Is.EqualTo(scenario.Economy.GearsPerBatch));
            Assert.That(state.Economies[0].LensProduced, Is.EqualTo(before + 1));
            Assert.That(state.Economies[0].GearsConsumed, Is.EqualTo(scenario.Economy.LensGearsInput));
        }

        [Test]
        public void GlassworksFlagDoesNotLetSmelterTakeOreFromCoreStock()
        {
            var scenario = ProductionScenario();
            scenario.Economy.CarriedInputs = true;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Civ = CivKind.Metallurgy;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Ore = scenario.Economy.OrePerMetal;
            uint smelterId = Place(fixture, BuildingKind.Smelter);
            ref BuildingState smelter = ref state.Buildings[(int)smelterId - 1];
            smelter.Complete = true;

            fixture.Step(1);

            Assert.That(state.Economies[0].Ore, Is.EqualTo(scenario.Economy.OrePerMetal));
            Assert.That(smelter.Input, Is.EqualTo(0));
            Assert.That(smelter.Output, Is.EqualTo(0));
        }

        [Test]
        public void LensAssemblerUsesOnlyInputsDeliveredToItsSlots()
        {
            var scenario = ProductionScenario();
            scenario.Economy.CarriedInputs = true;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            uint assemblerId = Place(fixture, BuildingKind.Assembler);
            ref BuildingState assembler = ref state.Buildings[(int)assemblerId - 1];
            assembler.Complete = true;
            assembler.Recipe = RecipeKind.Lens;
            state.Economies[0].Glass = scenario.Economy.LensGlassInput;
            state.Economies[0].Gears = scenario.Economy.LensGearsInput;

            fixture.Step(1);

            Assert.That(state.Economies[0].Glass, Is.EqualTo(scenario.Economy.LensGlassInput));
            Assert.That(state.Economies[0].Gears, Is.EqualTo(scenario.Economy.LensGearsInput));
            Assert.That(state.Economies[0].LensProduced, Is.EqualTo(0));
            Assert.That(assembler.Timer, Is.EqualTo(0));

            assembler.Input = scenario.Economy.LensGlassInput;
            assembler.InputSecondary = scenario.Economy.LensGearsInput;
            assembler.InputSecondaryKind = ResourceKind.Gears;
            int before = state.Economies[0].LensProduced;
            fixture.Step(scenario.Economy.LensTicks + 1);

            Assert.That(state.Economies[0].LensProduced, Is.EqualTo(before + 1));
            Assert.That(assembler.Input, Is.EqualTo(0));
            Assert.That(assembler.InputSecondary, Is.EqualTo(0));
        }

        [Test]
        public void GlassworksOutletGlassIsDeliveredToLensAssemblerByAHauler()
        {
            var scenario = ProductionScenario();
            scenario.Economy.CarriedInputs = true;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Stone = 10000;
            uint glassworksId = Place(fixture, BuildingKind.GlassWorks);
            uint assemblerId = Place(fixture, BuildingKind.Assembler);
            ref BuildingState glassworks = ref state.Buildings[(int)glassworksId - 1];
            ref BuildingState assembler = ref state.Buildings[(int)assemblerId - 1];
            glassworks.Complete = true;
            glassworks.Output = scenario.Economy.LensGlassInput;
            assembler.Complete = true;
            assembler.Recipe = RecipeKind.Lens;
            state.Economies[0].Glass = 0;
            state.Villagers[0].Task = VillagerTask.Idle;
            state.Villagers[0].BuildingId = 0;

            MethodInfo schedule = typeof(Battle).GetMethod("SetGlassworksPriorityHauler", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(schedule, Is.Not.Null);
            schedule.Invoke(fixture.Simulation, new object[] { 1u, glassworksId, assemblerId });
            int haulerIndex = Array.FindIndex(state.Villagers, v => v.Alive && v.FactionId == 1 && v.HaulFrom == glassworksId
                && v.HaulTo == assemblerId);
            Assert.That(haulerIndex, Is.GreaterThanOrEqualTo(0));
            state.Villagers[haulerIndex].Position = state.Map.Center(glassworks.WorkCell);
            fixture.Step(1);
            state.Villagers[haulerIndex].Position = state.Map.Center(assembler.WorkCell);
            fixture.Step(1);

            var view = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == assemblerId);
            Assert.That(view.Input, Is.EqualTo(scenario.Economy.LensGlassInput));
            Assert.That(state.Economies[0].Glass, Is.EqualTo(0));
        }

        [Test]
        public void CoreGearsReachLensAssemblerByCarriedHaulerWithoutGearWorkshop()
        {
            var scenario = ProductionScenario();
            scenario.Economy.CarriedInputs = true;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Stone = 10000;
            uint glassworksId = Place(fixture, BuildingKind.GlassWorks);
            uint assemblerId = Place(fixture, BuildingKind.Assembler);
            ref BuildingState glassworks = ref state.Buildings[(int)glassworksId - 1];
            ref BuildingState assembler = ref state.Buildings[(int)assemblerId - 1];
            glassworks.Complete = true;
            assembler.Complete = true;
            assembler.Recipe = RecipeKind.Lens;
            state.Economies[0].Gears = scenario.Economy.LensGearsInput;
            for (int i = 0; i < state.VillagerCount; i++)
            {
                state.Villagers[i].Task = VillagerTask.Idle;
                state.Villagers[i].BuildingId = 0;
            }

            MethodInfo schedule = typeof(Battle).GetMethod("LayGlassworksRoutes", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(schedule, Is.Not.Null);
            schedule.Invoke(fixture.Simulation, new object[] { 1u });
            int haulerIndex = Array.FindIndex(state.Villagers, v => v.Alive && v.FactionId == 1
                && v.HaulTo == assemblerId && v.HaulKind == ResourceKind.Gears);
            Assert.That(haulerIndex, Is.GreaterThanOrEqualTo(0));

            MethodInfo outlet = typeof(Battle).GetMethod("CarriedOutletPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(outlet, Is.Not.Null);
            state.Villagers[haulerIndex].Position = (SimPoint)outlet.Invoke(fixture.Simulation, new object[] { 1u, assemblerId });
            fixture.Step(1);
            state.Villagers[haulerIndex].Position = state.Map.Center(assembler.WorkCell);
            fixture.Step(1);

            var view = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == assemblerId);
            Assert.That(view.InputSecondary, Is.EqualTo(scenario.Economy.LensGearsInput));
            Assert.That(state.Economies[0].Gears, Is.EqualTo(0));
        }

        [Test]
        public void GlassworksMetalSourceUsesTheFirstProductiveSmelter()
        {
            var fixture = new Fixture(ProductionScenario());
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Civ = CivKind.Metallurgy;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Stone = 10000;
            uint firstSmelterId = Place(fixture, BuildingKind.Smelter);
            uint secondSmelterId = Place(fixture, BuildingKind.Smelter);
            state.Buildings[(int)firstSmelterId - 1].Complete = true;
            state.Buildings[(int)firstSmelterId - 1].Output = 1;
            state.Buildings[(int)secondSmelterId - 1].Complete = true;
            state.Buildings[(int)secondSmelterId - 1].Output = 2;

            MethodInfo select = typeof(Battle).GetMethod("SelectGlassworksMetalSource", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(select, Is.Not.Null);
            Assert.That((uint)select.Invoke(fixture.Simulation, new object[] { 1u }), Is.EqualTo(firstSmelterId));

            state.Buildings[(int)firstSmelterId - 1].Output = 0;
            Assert.That((uint)select.Invoke(fixture.Simulation, new object[] { 1u }), Is.EqualTo(secondSmelterId));
        }

        [Test]
        public void GlassworksAutomationDoesNotRewriteAnExistingAssemblerRecipe()
        {
            var fixture = new Fixture(ProductionScenario());
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Stone = 10000;
            uint glassworksId = Place(fixture, BuildingKind.GlassWorks);
            uint assemblerId = Place(fixture, BuildingKind.Assembler);
            state.Buildings[(int)glassworksId - 1].Complete = true;
            ref BuildingState assembler = ref state.Buildings[(int)assemblerId - 1];
            assembler.Complete = true;
            assembler.Recipe = RecipeKind.Frames;

            MethodInfo ensure = typeof(Battle).GetMethod("EnsureGlassLensLine", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(ensure, Is.Not.Null);
            ensure.Invoke(fixture.Simulation, new object[] { 1u });

            Assert.That(state.Buildings[(int)assemblerId - 1].Recipe, Is.EqualTo(RecipeKind.Frames));
        }

        [Test]
        public void AgeTwoArcherQueuesAndConsumesOneLensWhileAgeOneDoesNotRequireIt()
        {
            var scenario = ProductionScenario();
            scenario.Economy.Ages = true;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Food = 10000;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Lens = 0;

            MethodInfo lensCost = typeof(Battle).GetMethod("LensCostFor", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo canPay = typeof(Battle).GetMethod("CanPay", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(uint), typeof(UnitKind), typeof(BuildingKind) }, null);
            MethodInfo enqueue = typeof(Battle).GetMethod("Enqueue", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(lensCost, Is.Not.Null);
            Assert.That(canPay, Is.Not.Null);
            Assert.That(enqueue, Is.Not.Null);
            Assert.That((int)lensCost.Invoke(fixture.Simulation, new object[] { 1u, UnitKind.Archer }), Is.EqualTo(0));

            state.Economies[0].Age = 2;
            state.Economies[0].Lens = 1;
            uint rangeId = Place(fixture, BuildingKind.ArcheryRange);
            ref BuildingState range = ref state.Buildings[(int)rangeId - 1];
            range.Complete = true;
            Assert.That((int)lensCost.Invoke(fixture.Simulation, new object[] { 1u, UnitKind.Archer }),
                Is.EqualTo(scenario.Economy.ArcherLensCost));
            Assert.That((bool)canPay.Invoke(fixture.Simulation, new object[] { 1u, UnitKind.Archer, BuildingKind.ArcheryRange }), Is.True);

            object[] args = { 1u, range, UnitKind.Archer };
            enqueue.Invoke(fixture.Simulation, args);
            state.Buildings[(int)rangeId - 1] = (BuildingState)args[1];
            Assert.That(state.Economies[0].Lens, Is.EqualTo(0));
            Assert.That(state.Buildings[(int)rangeId - 1].QueuedLens, Is.EqualTo(scenario.Economy.ArcherLensCost));
        }

        [Test]
        public void GlassworksCountsOnlySandDrillsAndAssignsSandVillagersAsFallback()
        {
            var scenario = ProductionScenario();
            var node = scenario.ResourceNodes[0];
            node.Kind = ResourceKind.Sand;
            node.Position = scenario.Cores[0].Position;
            scenario.ResourceNodes[0] = node;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Stone = 10000;
            uint id = Place(fixture, BuildingKind.GlassWorks);
            state.Buildings[(int)id - 1].Complete = true;
            fixture.Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++fixture.Sequence, true));

            fixture.Step(5000);

            Assert.That(fixture.Simulation.SandDrillCount(1), Is.EqualTo(0));
            Assert.That(fixture.Simulation.SandVillagerAssignments(1), Is.GreaterThan(0));
            Assert.That(fixture.Simulation.SandVillagerCount(1), Is.GreaterThan(0));
        }

        [Test]
        public void GlassworksSandDrillAttemptRecordsAReasonWhenEverySandSiteIsBlocked()
        {
            var scenario = MapGenerator.GenerateLarge(9606UL, glassworks: true);
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Stone = 10000;
            uint id = Place(fixture, BuildingKind.GlassWorks);
            state.Buildings[(int)id - 1].Complete = true;
            fixture.Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++fixture.Sequence, true));

            fixture.Step(5000);

            Assert.That(fixture.Simulation.SandDrillAttempts(1), Is.GreaterThan(0));
            long failures = Enumerable.Range(0, 8).Sum(reason => fixture.Simulation.SandDrillFailure(1, reason));
            Assert.That(failures, Is.EqualTo(fixture.Simulation.SandDrillAttempts(1) - fixture.Simulation.SandDrillPlacements(1)));
        }

        [Test]
        public void EconomyViewExposesGlassworksValuesOnlyWhenTheFlagIsOn()
        {
            var off = new Battle(MapGenerator.GenerateTerrain(9605UL));
            Assert.That(off.Capture(1).Economy.GlassworksEnabled, Is.False);
            var onScenario = ProductionScenario();
            var on = new Battle(onScenario);
            var view = on.Capture(1).Economy;
            Assert.That(view.GlassworksEnabled, Is.True);
            Assert.That(view.GlassSandInput, Is.EqualTo(onScenario.Economy.GlassSandInput));
            Assert.That(view.LensTicks, Is.EqualTo(onScenario.Economy.LensTicks));
        }
    }
}
