using System;
using System.Collections.Generic;
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
    public sealed class CraftRequiredTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(71071UL, false, true);
            var e = scenario.Economy;
            e.Ages = true;
            e.Industry = true;
            e.ProcessingChain = true;
            e.CraftTree1 = true;
            e.CraftTree2 = true;
            e.CraftRequired = enabled;
            e.CraftRequiredAiArmsStockTarget = 20;
            e.CraftRequiredAiArmsAssemblersAge1 = 1;
            e.CraftRequiredAiArmsAssemblersAge2 = 2;
            e.CraftRequiredAiArmsAssemblersAge3 = 4;
            e.CraftRequiredAiArmsAssemblersAge4 = 4;
            e.CraftRequiredAiWoodWorkerPermille = 50;
            e.CraftRequiredBuildingPlankPermille = 300;
            e.CraftRequiredResearchScholarlyAge2 = 2;
            e.CraftRequiredResearchScholarlyAge3 = 4;
            e.CraftRequiredResearchScholarlyAge4 = 6;
            e.CraftRequiredAiPlankSawmillsAge1 = 1;
            e.CraftRequiredAiPlankSawmillsAge2 = 2;
            e.CraftRequiredAiPlankSawmillsAge3 = 3;
            e.CraftRequiredAiScholarlyAssemblers = 1;
            e.CraftRequiredAiFramesStockTarget = 10;
            e.CraftRequiredAiScholarlyStockResearches = 2;
            e.CraftRequiredAiScholarlyMinAssemblers = 2;
            e.AutoVillagerTarget = 0;
            e.AutoInfantryQueue = 0;
            e.StartFood = 10000;
            e.StartWood = 10000;
            e.StartMetal = 10000;
            return scenario;
        }

        [Test]
        public void Extension71RoundTripsAndOffStateHasNoExtension()
        {
            var source = Scenario(true);
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 71);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftRequired, Is.True);
            Assert.That(decoded.Economy.CraftRequiredAiArmsStockTarget, Is.EqualTo(20));
            Assert.That(decoded.Economy.CraftRequiredAiArmsAssemblersAge1, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftRequiredAiArmsAssemblersAge2, Is.EqualTo(2));
            Assert.That(decoded.Economy.CraftRequiredAiArmsAssemblersAge3, Is.EqualTo(4));
            Assert.That(decoded.Economy.CraftRequiredAiArmsAssemblersAge4, Is.EqualTo(4));
            Assert.That(decoded.Economy.CraftRequiredAiWoodWorkerPermille, Is.EqualTo(50));
            Assert.That(decoded.Economy.CraftRequiredBuildingPlankPermille, Is.EqualTo(300));
            Assert.That(decoded.Economy.CraftRequiredResearchScholarlyAge2, Is.EqualTo(2));
            Assert.That(decoded.Economy.CraftRequiredResearchScholarlyAge3, Is.EqualTo(4));
            Assert.That(decoded.Economy.CraftRequiredResearchScholarlyAge4, Is.EqualTo(6));
            Assert.That(decoded.Economy.CraftRequiredAiPlankSawmillsAge1, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftRequiredAiPlankSawmillsAge2, Is.EqualTo(2));
            Assert.That(decoded.Economy.CraftRequiredAiPlankSawmillsAge3, Is.EqualTo(3));
            Assert.That(decoded.Economy.CraftRequiredAiScholarlyAssemblers, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftRequiredAiFramesStockTarget, Is.EqualTo(10));
            Assert.That(decoded.Economy.CraftRequiredAiScholarlyStockResearches, Is.EqualTo(2));
            Assert.That(decoded.Economy.CraftRequiredAiScholarlyMinAssemblers, Is.EqualTo(2));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = Scenario(false);
            var offDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(off));
            Assert.That(offDecoded.Economy.CraftRequired, Is.False);
            Assert.That(offDecoded.Extensions.Any(x => x.Id == 71), Is.False);
        }

        [Test]
        public void LegacySixteenFieldExtensionKeepsCraftRequiredDefaults()
        {
            var source = Scenario(true).Economy;
            var create = typeof(ScenarioBinary).GetMethod("CreateCraftRequiredExtension", BindingFlags.Static | BindingFlags.NonPublic);
            var read = typeof(ScenarioBinary).GetMethod("ReadCraftRequiredExtension", BindingFlags.Static | BindingFlags.NonPublic);
            var full = (ScenarioExtensionData)create.Invoke(null, new object[] { source });
            var legacyData = new byte[16 * sizeof(int)];
            Array.Copy(full.Data, legacyData, legacyData.Length);
            var legacy = new ScenarioExtensionData { Id = 71, Version = 1, Data = legacyData };
            var decoded = new EconomyRules();
            read.Invoke(null, new object[] { legacy, decoded });
            Assert.That(decoded.CraftRequired, Is.True);
            Assert.That(decoded.CraftRequiredAiFramesStockTarget, Is.EqualTo(10));
            Assert.That(decoded.CraftRequiredAiScholarlyStockResearches, Is.EqualTo(2));
            Assert.That(decoded.CraftRequiredAiScholarlyMinAssemblers, Is.EqualTo(2));
        }

        [Test, Explicit("CraftRequired 旗オフの30,000 tickハッシュ一致")]
        public void FlagOffMatchesItsRoundTripAtEveryTick()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))));
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void EconomyFrameReportsCraftRequiredFlag()
        {
            Assert.That(new Battle(Scenario(true)).Capture(1).Economy.CraftRequiredEnabled, Is.True);
            Assert.That(new Battle(Scenario(false)).Capture(1).Economy.CraftRequiredEnabled, Is.False);
        }

        [Test]
        public void TwoAssemblersReserveOneForTwoResearchesAndStopAtFourScholarly()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Civ", CivKind.Agrarian);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            SetEconomy(simulation, 0, "Scholarly", 0);
            SetBuildings(simulation,
                NewBuilding(BuildingKind.Assembler, true), NewBuilding(BuildingKind.Assembler, true),
                NewBuilding(BuildingKind.Blacksmith, true));

            Assert.That((bool)Invoke(simulation, "CraftRequiredAiNeedsScholarly", 1u), Is.True);
            Invoke(simulation, "AssignCraftRequiredAiRecipes", 1u);
            var world = GetWorld(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            Assert.That(buildings.Cast<object>().Count(value => (RecipeKind)Get(value, "Recipe") == RecipeKind.Scholarly), Is.EqualTo(1));

            SetEconomy(simulation, 0, "Planks", 1000);
            SetEconomy(simulation, 0, "Charcoal", 1000);
            SetEconomy(simulation, 0, "Food", 1000);
            for (int tick = 0; tick < 600 && GetEconomy(simulation, 0, "ScholarlyProduced") < 4; tick++)
                Invoke(simulation, "AdvanceIndustry");
            Assert.That(GetEconomy(simulation, 0, "ScholarlyProduced"), Is.EqualTo(4));
            Assert.That(GetEconomy(simulation, 0, "Scholarly"), Is.EqualTo(4));
            Assert.That((bool)Invoke(simulation, "CraftRequiredAiNeedsScholarly", 1u), Is.False);
        }

        [Test]
        public void EconomyFrameCraftCostsAreZeroWhenOffAndMatchPlacementRulesWhenOn()
        {
            var off = new Battle(Scenario(false)).Capture(1).Economy;
            Assert.That(off.BeltPlankCostPerTwoCells, Is.EqualTo(0));
            Assert.That(off.FastBeltPlankCost, Is.EqualTo(0));
            Assert.That(off.SplitterPlankCost, Is.EqualTo(0));
            Assert.That(off.SorterPlankCost, Is.EqualTo(0));
            Assert.That(off.UndergroundBeltPlankCost, Is.EqualTo(0));
            Assert.That(off.ResearchScholarlyCost, Is.EqualTo(0));
            Assert.That(off.AdvancePlanks, Is.EqualTo(0));
            Assert.That(off.AdvanceCutStone, Is.EqualTo(0));
            Assert.That(off.AdvanceFrames, Is.EqualTo(0));
            Assert.That(off.AdvanceScholarly, Is.EqualTo(0));
            Assert.That(off.AdvanceSiegeParts, Is.EqualTo(0));
            Assert.That(off.AdvanceMechanism, Is.EqualTo(0));

            var onSimulation = new Battle(Scenario(true));
            SetEconomy(onSimulation, 0, "Age", 2);
            Assert.That(GetEconomy(onSimulation, 0, "Age"), Is.EqualTo(2));
            onSimulation.Step(1, Array.Empty<ScheduledInput>());
            var on = onSimulation.Capture(1).Economy;
            Assert.That(on.CraftRequiredEnabled, Is.True);
            Assert.That(on.Age, Is.EqualTo(2));
            Assert.That(on.BeltPlankCostPerTwoCells, Is.EqualTo(1));
            Assert.That(on.FastBeltPlankCost, Is.EqualTo(1));
            Assert.That(on.SplitterPlankCost, Is.EqualTo(3));
            Assert.That(on.SorterPlankCost, Is.EqualTo(4));
            Assert.That(on.UndergroundBeltPlankCost, Is.EqualTo(2));
            Assert.That(on.ResearchScholarlyCost, Is.EqualTo(2));
            Assert.That(on.AdvanceFrames, Is.EqualTo(20));
            Assert.That(on.AdvanceScholarly, Is.EqualTo(10));
            Assert.That(on.BuildingPlankCosts[(int)BuildingKind.Scriptorium], Is.EqualTo(24));
            Assert.That(on.BuildingPlankCosts[(int)BuildingKind.House], Is.EqualTo(0));
        }

        [Test]
        public void AgeTwoSoldierNeedsArmsKitButAgeOneAndExcludedUnitsKeepNormalCosts()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Metal", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            SetEconomy(simulation, 0, "EliteKit", 0);
            var canPay = typeof(Battle).GetMethod("CanPay", PrivateInstance, null,
                new[] { typeof(uint), typeof(UnitKind) }, null);
            var cost = typeof(Battle).GetMethod("CostOf", PrivateInstance, null,
                new[] { typeof(uint), typeof(UnitKind) }, null);
            Assert.That(canPay, Is.Not.Null);
            Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, UnitKind.Infantry }), Is.False);
            Assert.That(GetEconomy(simulation, 0, "Wood"), Is.EqualTo(10000));

            SetEconomy(simulation, 0, "ArmsKit", 1);
            var infantryCost = cost.Invoke(simulation, new object[] { 1u, UnitKind.Infantry });
            Assert.That(Get(infantryCost, "Item2"), Is.EqualTo(0));
            Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, UnitKind.Infantry }), Is.True);

            foreach (var kind in new[] { UnitKind.Villager, UnitKind.Scout, UnitKind.Monk })
            {
                if (kind == UnitKind.Monk) SetConfig(simulation, "MonksEnabled", true);
                Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, kind }), Is.True, kind.ToString());
            }

            SetEconomy(simulation, 0, "Age", 1);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            var ageOneCost = cost.Invoke(simulation, new object[] { 1u, UnitKind.Infantry });
            Assert.That(Get(ageOneCost, "Item2"), Is.GreaterThan(0));
            Assert.That((bool)canPay.Invoke(simulation, new object[] { 1u, UnitKind.Infantry }), Is.True);
        }

        [Test]
        public void EnqueueConsumesArmsKitAndCancelRefundsItWithoutRefundingZeroWood()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Metal", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 1);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var building = Activator.CreateInstance(buildingType);
            Set(building, "Kind", BuildingKind.Barracks);
            var enqueue = typeof(Battle).GetMethod("Enqueue", PrivateInstance);
            var cancel = typeof(Battle).GetMethod("CancelLast", PrivateInstance);
            var beforeWood = GetEconomy(simulation, 0, "Wood");
            var args = new object[] { 1u, building, UnitKind.Infantry };
            enqueue.Invoke(simulation, args);
            Assert.That(GetEconomy(simulation, 0, "ArmsKit"), Is.EqualTo(0));
            Assert.That(GetEconomy(simulation, 0, "Wood"), Is.EqualTo(beforeWood));
            Assert.That(Get(args[1], "Queued"), Is.EqualTo(1));
            var cancelArgs = new[] { (object)1u, args[1] };
            cancel.Invoke(simulation, cancelArgs);
            Assert.That(GetEconomy(simulation, 0, "ArmsKit"), Is.EqualTo(1));
            Assert.That(GetEconomy(simulation, 0, "Wood"), Is.EqualTo(beforeWood));
            Assert.That(Get(cancelArgs[1], "Queued"), Is.EqualTo(0));
        }

        [Test]
        public void ArmsTargetBlocksFramesAndScholarlyButAllowsArmsAcrossCraftTimer()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            SetEconomy(simulation, 0, "Planks", 0);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var economyType = typeof(Battle).Assembly.GetType("Rts.Simulation.FactionEconomy");
            var advance = typeof(Battle).GetMethod("AdvanceAssembler", PrivateInstance);
            var frames = Activator.CreateInstance(buildingType);
            var framesEconomy = Activator.CreateInstance(economyType);
            Set(frames, "FactionId", 1u); Set(frames, "Kind", BuildingKind.Assembler); Set(frames, "Recipe", RecipeKind.Frames);
            Set(framesEconomy, "Planks", 1); Set(framesEconomy, "Bricks", 1);
            advance.Invoke(simulation, new[] { frames, framesEconomy });
            Assert.That(Get(framesEconomy, "Planks"), Is.EqualTo(1));

            var scholarly = Activator.CreateInstance(buildingType);
            var scholarlyEconomy = Activator.CreateInstance(economyType);
            Set(scholarly, "FactionId", 1u); Set(scholarly, "Kind", BuildingKind.Assembler); Set(scholarly, "Recipe", RecipeKind.Scholarly);
            Set(scholarlyEconomy, "Planks", 1); Set(scholarlyEconomy, "Charcoal", 1); Set(scholarlyEconomy, "Food", 2);
            advance.Invoke(simulation, new[] { scholarly, scholarlyEconomy });
            Assert.That(Get(scholarlyEconomy, "Planks"), Is.EqualTo(1));

            SetEconomy(simulation, 0, "ArmsKit", 20);
            var arms = Activator.CreateInstance(buildingType);
            var armsEconomy = Activator.CreateInstance(economyType);
            Set(arms, "FactionId", 1u); Set(arms, "Kind", BuildingKind.Assembler); Set(arms, "Recipe", RecipeKind.ArmsKit);
            Set(armsEconomy, "Planks", 1); Set(armsEconomy, "Charcoal", 2);
            var args = new[] { arms, armsEconomy };
            advance.Invoke(simulation, args);
            Assert.That(Get(args[1], "Planks"), Is.EqualTo(0));
            Assert.That(Get(args[0], "Timer"), Is.EqualTo(80));
            for (int i = 0; i < 80; i++) advance.Invoke(simulation, args);
            Assert.That(Get(args[1], "ArmsKit"), Is.EqualTo(1));
        }

        [Test]
        public void BuildingsUsePlanksAfterAllExistingDiscountsAndKeepExcludedBuildingsWoodOnly()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Frames", 0);
            var costMethod = typeof(Battle).GetMethod("BuildingCostForPlacement", PrivateInstance);
            var cost = costMethod.Invoke(simulation, new object[] { BuildingKind.Scriptorium, 1u, 0 });
            Assert.That(Get(cost, "Wood"), Is.EqualTo(56));
            Assert.That(Get(cost, "Planks"), Is.EqualTo(24));
            var sawmillCost = costMethod.Invoke(simulation, new object[] { BuildingKind.Sawmill, 1u, 0 });
            Assert.That(Get(sawmillCost, "Wood"), Is.EqualTo(80));
            Assert.That(Get(sawmillCost, "Planks"), Is.EqualTo(0));

            SetEconomy(simulation, 0, "Age", 3);
            var joineryCost = costMethod.Invoke(simulation, new object[] { BuildingKind.Joinery, 1u, 0 });
            Assert.That(Get(joineryCost, "Planks"), Is.EqualTo(0));
        }

        [Test]
        public void PlaceBeltsConsumesOnePlankOnOddCellsOfOneCommand()
        {
            var cells = FindBeltCells(new Battle(Scenario(true)), 3);
            var facings = new[] { Facing.North, Facing.North, Facing.North };
            var command = EconomyCommand.PlaceBelt(1, 1, cells, facings);
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 1);
            SetEconomy(simulation, 0, "Wood", 0);
            SetEconomy(simulation, 0, "Planks", 2);
            Invoke(simulation, "PlaceBelts", 1u, command, true);
            Assert.That(CountBelts(simulation, 1), Is.EqualTo(3));
            Assert.That(GetEconomy(simulation, 0, "Planks"), Is.EqualTo(0));

            var limited = new Battle(Scenario(true));
            SetEconomy(limited, 0, "Age", 1);
            SetEconomy(limited, 0, "Wood", 0);
            SetEconomy(limited, 0, "Planks", 1);
            Invoke(limited, "PlaceBelts", 1u, command, true);
            Assert.That(CountBelts(limited, 1), Is.EqualTo(2));
        }

        [Test]
        public void RemovingPlankPaidBeltRefundsNoWood()
        {
            var cells = FindBeltCells(new Battle(Scenario(true)), 1);
            var simulation = new Battle(Scenario(true));
            SetConfig(simulation, "EasyRebuild", true);
            SetEconomy(simulation, 0, "Age", 1);
            SetEconomy(simulation, 0, "Wood", 0);
            SetEconomy(simulation, 0, "Planks", 1);
            Invoke(simulation, "PlaceBelts", 1u, EconomyCommand.PlaceBelt(1, 1, cells, new[] { Facing.North }), true);
            Assert.That(CountBelts(simulation, 1), Is.EqualTo(1));
            Invoke(simulation, "RemoveBelt", 1u, cells[0]);
            Assert.That(CountBelts(simulation, 1), Is.EqualTo(0));
            Assert.That(GetEconomy(simulation, 0, "Wood"), Is.EqualTo(0));
            Assert.That(GetEconomy(simulation, 0, "Planks"), Is.EqualTo(0));
        }

        [Test]
        public void MechanismResearchStillPaysRequiredScholarly()
        {
            var scenario = Scenario(true);
            scenario.Economy.CraftTree3 = true;
            var simulation = new Battle(scenario);
            SetEconomy(simulation, 0, "Age", 3);
            SetEconomy(simulation, 0, "Civ", CivKind.Agrarian);
            SetEconomy(simulation, 0, "Food", 10000); SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Mechanism", 1);
            SetEconomy(simulation, 0, "Scholarly", 3);
            Assert.That((bool)Invoke(simulation, "StartResearch", 1u, NewBuilding(BuildingKind.Blacksmith, true), TechKind.Masonry, true), Is.False);
            SetEconomy(simulation, 0, "Scholarly", 4);
            Assert.That((bool)Invoke(simulation, "StartResearch", 1u, NewBuilding(BuildingKind.Blacksmith, true), TechKind.Masonry, true), Is.True);
            Assert.That(GetEconomy(simulation, 0, "Scholarly"), Is.EqualTo(0));
            Assert.That(GetEconomy(simulation, 0, "Mechanism"), Is.EqualTo(0));
        }

        [Test]
        public void ResearchUsesRequiredScholarlyOrAcademyBooksOneForOne()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Civ", CivKind.Agrarian);
            SetEconomy(simulation, 0, "Food", 10000); SetEconomy(simulation, 0, "Wood", 10000);
            var smith = NewBuilding(BuildingKind.Blacksmith, true);
            SetEconomy(simulation, 0, "Scholarly", 1);
            Assert.That((bool)Invoke(simulation, "StartResearch", 1u, smith, TechKind.Weapons, true), Is.False);
            SetEconomy(simulation, 0, "Scholarly", 2);
            smith = NewBuilding(BuildingKind.Blacksmith, true);
            Assert.That((bool)Invoke(simulation, "StartResearch", 1u, smith, TechKind.Weapons, true), Is.True);
            Assert.That(GetEconomy(simulation, 0, "Scholarly"), Is.EqualTo(0));

            var books = new Battle(Scenario(true));
            SetConfig(books, "Books", true);
            SetEconomy(books, 0, "Age", 2); SetEconomy(books, 0, "Civ", CivKind.Academy);
            SetEconomy(books, 0, "Food", 10000); SetEconomy(books, 0, "Wood", 10000); SetEconomy(books, 0, "Books", 2);
            Assert.That((bool)Invoke(books, "StartResearch", 1u, NewBuilding(BuildingKind.Blacksmith, true), TechKind.Weapons, true), Is.True);
            Assert.That(GetEconomy(books, 0, "Books"), Is.EqualTo(0));
        }

        [Test]
        public void AdvanceNeedsCraftMaterialsButClockCompletionDoesNot()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 1); SetEconomy(simulation, 0, "Civ", CivKind.Agrarian);
            SetEconomy(simulation, 0, "Food", 10000); SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Planks", 19); SetEconomy(simulation, 0, "CutStone", 20);
            Assert.That((bool)Invoke(simulation, "CanAdvance", 1u, CivKind.Agrarian), Is.False);
            SetEconomy(simulation, 0, "Planks", 20);
            Assert.That((bool)Invoke(simulation, "CanAdvance", 1u, CivKind.Agrarian), Is.True);

            var clock = Scenario(true);
            clock.Economy.AgeClock = true; clock.Economy.AgeClockTicks1 = 1; clock.Economy.AgeClockTicks2 = 2;
            var clockSimulation = new Battle(clock);
            clockSimulation.Step(1, Array.Empty<ScheduledInput>());
            Assert.That(GetEconomy(clockSimulation, 0, "Age"), Is.GreaterThanOrEqualTo(1));
        }

        [Test, Explicit("CraftRequired AI の age1 assembler・ArmsKit・兵士生成を確認")]
        public void AiBuildsArmsKitsBeforeTrainingAgeTwoSoldiers()
        {
            var scenario = LiveGameRules.Create(71071UL, true, true, false, false);
            scenario.Economy.CraftRequired = true;
            scenario.Economy.AutoInfantryQueue = 1;
            scenario.Economy.CraftRequiredAiArmsStockTarget = 20;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 30000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            bool produced = false, assembler = false, ageTwo = false, soldier = false;
            for (int faction = 0; faction < 2; faction++)
            {
                var economy = GetEconomyObject(simulation, faction);
                produced |= Get(economy, "ArmsKitProduced") > 0;
                ageTwo |= Get(economy, "Age") >= 2;
                var soldiers = (Array)GetWorld(simulation).GetType().GetField("Soldiers", PrivateInstance).GetValue(GetWorld(simulation));
                soldier |= soldiers.Cast<object>().Any(value => Get(value, "Alive") != 0
                    && Get(GetObject(value, "Initial"), "FactionId") == faction + 1
                    && Get(GetObject(value, "Initial"), "Kind") == (int)UnitKind.Infantry);
            }
            var world = GetWorld(simulation);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            foreach (var value in buildings)
            {
                if ((BuildingKind)Get(value, "Kind") == BuildingKind.Assembler && Get(value, "Complete") != 0)
                    assembler = true;
            }
            Assert.That(ageTwo, Is.True);
            Assert.That(assembler, Is.True);
            Assert.That(produced, Is.True);
            Assert.That(soldier, Is.True);
        }

        [Test, Explicit("CraftRequired AI の時代1先行製材所と時代2武具在庫を確認")]
        public void AiPrebuildsAgeTwoSawmillsAndHasArmsAtAgeTwo()
        {
            var scenario = LiveGameRules.Create(71071UL, true, true, false, false);
            scenario.Economy.CraftRequired = true;
            scenario.Economy.AiExpand = false;
            scenario.Economy.FreeTowns = false;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.StartFood = scenario.Economy.StartWood = scenario.Economy.StartStone = scenario.Economy.StartMetal = 100000;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            var simulation = new Battle(scenario);
            int ageOneSawmills = 0, armsAtAgeTwo = 0;
            long ageTwoTick = 0;
            for (long tick = 1; tick <= 16000; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                var economy = simulation.Capture(1).Economy;
                int sawmills = economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Sawmill);
                if (economy.Age < 2) ageOneSawmills = Math.Max(ageOneSawmills, sawmills);
                else if (ageTwoTick == 0)
                {
                    ageTwoTick = tick;
                    armsAtAgeTwo = economy.ArmsKit + economy.FineArmsKit;
                }
            }
            Assert.That(ageTwoTick, Is.GreaterThan(0));
            Assert.That(ageOneSawmills, Is.GreaterThanOrEqualTo(scenario.Economy.CraftRequiredAiArmsAssemblersAge2));
            Assert.That(armsAtAgeTwo, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void EnabledReplayStaysDeterministic()
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

        private static object GetWorld(Battle simulation)
            => typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);

        private static object GetEconomyObject(Battle simulation, int index)
        {
            var economies = (Array)GetWorld(simulation).GetType().GetField("Economies", PrivateInstance).GetValue(GetWorld(simulation));
            return economies.GetValue(index);
        }

        private static int GetEconomy(Battle simulation, int index, string name)
            => Get(GetEconomyObject(simulation, index), name);

        private static void SetEconomy(Battle simulation, int index, string name, object value)
        {
            var world = GetWorld(simulation);
            var economiesField = world.GetType().GetField("Economies", PrivateInstance);
            var economies = (Array)economiesField.GetValue(world);
            var economy = economies.GetValue(index);
            Set(economy, name, value);
            economies.SetValue(economy, index);
        }

        private static void SetConfig(Battle simulation, string name, object value)
        {
            var world = GetWorld(simulation);
            var config = world.GetType().GetField("Config", PrivateInstance).GetValue(world);
            var economy = config.GetType().GetField("Economy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(config);
            Set(economy, name, value);
        }

        private static void Set(object value, string name, object fieldValue)
        {
            var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fieldValue != null && !field.FieldType.IsInstanceOfType(fieldValue))
                fieldValue = field.FieldType.IsEnum
                    ? Enum.ToObject(field.FieldType, fieldValue)
                    : Convert.ChangeType(fieldValue, Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType);
            field.SetValue(value, fieldValue);
        }

        private static int Get(object value, string name)
        {
            var field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var fieldValue = field.GetValue(value);
            return fieldValue is bool b ? (b ? 1 : 0) : Convert.ToInt32(fieldValue);
        }

        private static object GetObject(object value, string name)
            => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);

        private static object NewBuilding(BuildingKind kind, bool complete)
        {
            var type = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var value = Activator.CreateInstance(type);
            Set(value, "Kind", kind); Set(value, "Complete", complete); Set(value, "FactionId", 1u);
            return value;
        }

        private static void SetBuildings(Battle simulation, params object[] values)
        {
            var world = GetWorld(simulation);
            var field = world.GetType().GetField("Buildings", PrivateInstance);
            var array = Array.CreateInstance(field.FieldType.GetElementType(), values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                Set(values[i], "Id", (uint)(i + 1));
                Set(values[i], "Alive", true);
                array.SetValue(values[i], i);
            }
            field.SetValue(world, array);
            Set(world, "NextBuildingId", (uint)(values.Length + 1));
        }

        private static object Invoke(Battle simulation, string method, params object[] args)
            => typeof(Battle).GetMethod(method, PrivateInstance).Invoke(simulation, args);

        private static int CountBelts(Battle simulation, uint faction)
        {
            var belts = (Array)GetWorld(simulation).GetType().GetField("Belts", PrivateInstance).GetValue(GetWorld(simulation));
            return belts.Cast<object>().Count(value => Get(value, "FactionId") == (int)faction);
        }

        private static List<int> FindBeltCells(Battle simulation, int count)
        {
            var world = GetWorld(simulation);
            var map = world.GetType().GetField("Map", PrivateInstance).GetValue(world);
            var passable = map.GetType().GetMethod("IsPassable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var inside = typeof(Battle).GetMethod("InsideAnyCore", PrivateInstance);
            var nodes = typeof(Battle).GetMethod("IsNodeCell", PrivateInstance);
            var result = new List<int>();
            var belts = (Array)world.GetType().GetField("Belts", PrivateInstance).GetValue(world);
            for (int cell = 0; cell < belts.Length && result.Count < count; cell++)
                if ((bool)passable.Invoke(map, new object[] { cell })
                    && !(bool)inside.Invoke(simulation, new object[] { cell })
                    && !(bool)nodes.Invoke(simulation, new object[] { cell })) result.Add(cell);
            return result;
        }
    }
}
