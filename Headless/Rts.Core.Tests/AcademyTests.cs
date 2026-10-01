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
    public sealed class AcademyTests
    {
        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition AcademyScenario(bool gold)
        {
            var scenario = MapGenerator.GenerateTerrain(1, gold: gold);
            scenario.Economy.Academy = true;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.AutoVillagerTarget = 3;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.GoldDangerMeters = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000;
            return scenario;
        }

        private static ScenarioDefinition AcademyMatchScenario(ulong seed)
        {
            var scenario = MapGenerator.GenerateTerrain(seed, gold: true);
            scenario.Economy.Academy = true;
            scenario.Economy.StartFood = 50000;
            scenario.Economy.StartWood = 50000;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.Age2FoodCost = 0;
            scenario.Economy.Age2WoodCost = 0;
            scenario.Economy.Age2Ticks = 1;
            scenario.Economy.Age3FoodCost = 0;
            scenario.Economy.Age3WoodCost = 0;
            scenario.Economy.Age3Ticks = 1;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            return scenario;
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtAcademyAge(bool gold = true)
        {
            var scenario = AcademyScenario(gold);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Academy));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            return (scenario, sim, gateway, sequence);
        }

        private static uint PlaceAndBuild(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, BuildingKind kind, ref ulong sequence)
        {
            uint before = (uint)sim.Capture(1).Economy.Buildings.Count;
            int width = scenario.Map.WidthCells;
            int core = scenario.Map.WidthCells * ((int)(scenario.Cores[0].Position.Z.Raw / 65536) / scenario.Map.CellSizeMeters)
                + (int)(scenario.Cores[0].Position.X.Raw / 65536) / scenario.Map.CellSizeMeters;
            int cx = core % width, cz = core / width;
            for (int radius = 5; radius <= 14; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius || cx + dx < 0 || cz + dz < 0
                            || cx + dx >= width - scenario.Economy.AcademySizeCells || cz + dz >= scenario.Map.HeightCells - scenario.Economy.AcademySizeCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, kind, (cz + dz) * width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var buildings = sim.Capture(1).Economy.Buildings;
                        if (buildings.Count > before && buildings[(int)before].Kind == kind)
                        {
                            uint id = buildings[(int)before].Id;
                            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
                            int work = kind == BuildingKind.Academy ? scenario.Economy.AcademyWork : scenario.Economy.BlacksmithWork;
                            Steps(gateway, sim, work == scenario.Economy.AcademyWork ? 900 : 1400);
                            return id;
                        }
                    }
            return 0;
        }

        private static void GatherGold(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            var core = scenario.Cores[0].Position;
            var gold = scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Gold)
                .OrderBy(n => DistanceSquared(n.Position, core)).First();
            uint node = gold.Id;
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.ResourceNode, node));
            for (int i = 0; i < 6000 && Gold(sim) < 100; i++) Steps(gateway, sim, 1);
            if (Gold(sim) < 100) SetGold(sim, 100 - Gold(sim));
        }

        private static void SetGold(Battle sim, int amount)
        {
            var addStock = typeof(Battle).GetMethod("AddStock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            addStock.Invoke(sim, new object[] { 1u, ResourceKind.Gold, amount });
        }

        private static int Gold(Battle sim)
            => int.Parse(DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value)["Economy[1].Gold"],
                System.Globalization.CultureInfo.InvariantCulture);

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static int Stock(Battle sim, ResourceKind kind)
        {
            string name = kind.ToString();
            return int.Parse(Fields(sim)["Economy[1]." + name], System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void SetStock(Battle sim, ResourceKind kind, int amount)
        {
            var addStock = typeof(Battle).GetMethod("AddStock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            addStock.Invoke(sim, new object[] { 1u, kind, amount - Stock(sim, kind) });
        }

        private static void SetKnowledge(Battle sim, ScenarioDefinition scenario, int[] observedGoldIndexes,
            bool allVisible, int dangerMeters)
        {
            var worldField = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic);
            var world = worldField.GetValue(sim);
            var factionsField = world.GetType().GetField("Factions", BindingFlags.Instance | BindingFlags.NonPublic);
            var factions = (Array)factionsField.GetValue(world);
            var faction = factions.GetValue(0);
            var explored = (bool[])faction.GetType().GetField("ExploredCells", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(faction);
            var visible = (bool[])faction.GetType().GetField("VisibleCells", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(faction);
            Array.Clear(explored, 0, explored.Length);
            Array.Clear(visible, 0, visible.Length);
            if (allVisible) for (int i = 0; i < visible.Length; i++) visible[i] = true;
            foreach (int index in observedGoldIndexes)
            {
                var node = scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Gold).ElementAt(index);
                int cell = (int)(node.Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                    + (int)(node.Position.X.Raw / 65536 / scenario.Map.CellSizeMeters);
                explored[cell] = true;
            }
            var core = scenario.Cores[0].Position;
            int coreCell = (int)(core.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                + (int)(core.X.Raw / 65536 / scenario.Map.CellSizeMeters);
            if (observedGoldIndexes.Length == 0 && !allVisible) explored[coreCell] = true;
            if (observedGoldIndexes.Length > 0)
            {
                // This is the decision-time known map for the unit cases: all terrain cells are known, except when a
                // case intentionally verifies that an unexplored corridor blocks the route.
                for (int i = 0; i < explored.Length; i++) explored[i] = true;
                for (int i = 0; i < scenario.ResourceNodes.Count(n => n.Kind == ResourceKind.Gold); i++)
                {
                    if (observedGoldIndexes.Contains(i)) continue;
                    var node = scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Gold).ElementAt(i);
                    int cell = (int)(node.Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                        + (int)(node.Position.X.Raw / 65536 / scenario.Map.CellSizeMeters);
                    explored[cell] = false;
                }
            }
            scenario.Economy.GoldDangerMeters = dangerMeters;
        }

        private static int AcademyGoldScore(Battle sim, ScenarioDefinition scenario)
        {
            var score = typeof(Battle).GetMethod("AcademyScore", BindingFlags.Static | BindingFlags.NonPublic);
            return (int)score.Invoke(null, new object[] { sim, 1u, scenario.Cores[0].Position, 0, 0 });
        }

        private static int AcademyGoldCount(Battle sim, ScenarioDefinition scenario)
        {
            var count = typeof(Battle).GetMethod("CountUsableAcademyGold", BindingFlags.Instance | BindingFlags.NonPublic);
            return (int)count.Invoke(sim, new object[] { 1u, scenario.Cores[0].Position });
        }

        private static void Advance(Battle sim, CommandGateway gateway, ScenarioDefinition scenario, ref ulong sequence)
        {
            int ticks = sim.Capture(1).Economy.Age == 1 ? scenario.Economy.Age2Ticks : scenario.Economy.Age3Ticks;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Academy));
            Steps(gateway, sim, ticks + 2);
        }

        private static long DistanceSquared(SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            return dx * dx + dz * dz;
        }

        [Test]
        public void AcademyOffKeepsTheOldBytesAndState()
        {
            var old = MapGenerator.GenerateTerrain(1, gold: false);
            var off = MapGenerator.GenerateTerrain(1, gold: false);
            off.Economy.Academy = false;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
            var left = new Battle(old); var right = new Battle(off);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void AcademyCannotBeChosenWithoutGold()
        {
            var state = AtAcademyAge(false);
            Assert.That(state.sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Primitive));
        }

        [Test]
        public void AcademyScoreCountsOnlyKnownSafeReachableGoldInTiers()
        {
            int[] cases = { 0, 1, 2, 3 };
            foreach (int amount in cases)
            {
                var scenario = AcademyScenario(true);
                var sim = new Battle(scenario);
                int[] observed = Enumerable.Range(0, amount).ToArray();
                SetKnowledge(sim, scenario, observed, allVisible: false, dangerMeters: 0);
                Assert.That(AcademyGoldCount(sim, scenario), Is.EqualTo(amount), "known gold=" + amount);
                Assert.That(AcademyGoldScore(sim, scenario), Is.EqualTo(amount == 0 ? 0 : amount == 1 ? 2 : 3),
                    "known gold=" + amount);
            }

            var unknown = AcademyScenario(true);
            var unknownSim = new Battle(unknown);
            SetKnowledge(unknownSim, unknown, Array.Empty<int>(), allVisible: false, dangerMeters: 0);
            Assert.That(AcademyGoldCount(unknownSim, unknown), Is.EqualTo(0));
            Assert.That(AcademyGoldScore(unknownSim, unknown), Is.EqualTo(0));

            var dangerous = AcademyScenario(true);
            dangerous.Economy.GoldDangerMeters = 1000;
            var dangerousSim = new Battle(dangerous);
            SetKnowledge(dangerousSim, dangerous, new[] { 0, 1 }, allVisible: true, dangerMeters: 1000);
            Assert.That(AcademyGoldCount(dangerousSim, dangerous), Is.EqualTo(0));
            Assert.That(AcademyGoldScore(dangerousSim, dangerous), Is.EqualTo(0));

            var unreachable = AcademyScenario(true);
            var unreachableSim = new Battle(unreachable);
            SetKnowledge(unreachableSim, unreachable, new[] { 0 }, allVisible: false, dangerMeters: 0);
            var worldField = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic);
            var world = worldField.GetValue(unreachableSim);
            var factions = (Array)world.GetType().GetField("Factions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            var faction = factions.GetValue(0);
            var explored = (bool[])faction.GetType().GetField("ExploredCells", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(faction);
            Array.Clear(explored, 0, explored.Length);
            int coreCell = (int)(unreachable.Cores[0].Position.Z.Raw / 65536 / unreachable.Map.CellSizeMeters) * unreachable.Map.WidthCells
                + (int)(unreachable.Cores[0].Position.X.Raw / 65536 / unreachable.Map.CellSizeMeters);
            int goldCell = (int)(unreachable.ResourceNodes.Where(n => n.Kind == ResourceKind.Gold).First().Position.Z.Raw / 65536 / unreachable.Map.CellSizeMeters) * unreachable.Map.WidthCells
                + (int)(unreachable.ResourceNodes.Where(n => n.Kind == ResourceKind.Gold).First().Position.X.Raw / 65536 / unreachable.Map.CellSizeMeters);
            explored[coreCell] = true; explored[goldCell] = true;
            Assert.That(AcademyGoldCount(unreachableSim, unreachable), Is.EqualTo(0));
            Assert.That(AcademyGoldScore(unreachableSim, unreachable), Is.EqualTo(0));
        }

        [Test]
        public void AcademyScoreKeepsTheExistingCivTieOrderWhenFlagsAreOff()
        {
            var withoutAcademy = MapGenerator.GenerateTerrain(1, gold: false);
            withoutAcademy.Economy.Academy = false;
            var withAcademyFlagButNoGold = MapGenerator.GenerateTerrain(1, gold: false);
            withAcademyFlagButNoGold.Economy.Academy = true;
            Assert.That(ScenarioBinary.Encode(withAcademyFlagButNoGold), Is.Not.EqualTo(ScenarioBinary.Encode(withoutAcademy)));
            var left = new Battle(withoutAcademy);
            var right = new Battle(withAcademyFlagButNoGold);
            var choose = typeof(Battle).GetMethod("ChooseCiv", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(choose.Invoke(right, new object[] { 1u }), Is.EqualTo(choose.Invoke(left, new object[] { 1u })));
        }

        [Test]
        public void AcademyResearchPaysGoldAndImprovesGathering()
        {
            var state = AtAcademyAge();
            Assert.That(state.scenario.Economy.GoldEnabled, Is.True);
            uint academy = PlaceAndBuild(state.scenario, state.sim, state.gateway, BuildingKind.Academy, ref state.sequence);
            Assert.That(academy, Is.Not.EqualTo(0u));
            GatherGold(state.scenario, state.sim, state.gateway, ref state.sequence);
            int gold = Gold(state.sim);
            Assert.That(gold, Is.GreaterThanOrEqualTo(state.scenario.Economy.AcademyToolsGoldCost));
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == academy).Complete, Is.True);
            uint foodNode = state.scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Food).Id;
            var gather = typeof(Battle).GetMethod("GatherTicksFor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int before = (int)gather.Invoke(state.sim, new object[] { 1u, foodNode });
            var research = EconomyCommand.Research(1, ++state.sequence, academy, TechKind.Tools);
            state.gateway.SubmitEconomy(research);
            Steps(state.gateway, state.sim, 1);
            Assert.That(Gold(state.sim), Is.EqualTo(gold - state.scenario.Economy.AcademyToolsGoldCost));
            Steps(state.gateway, state.sim, state.scenario.Economy.AcademyToolsTicks + 1);
            Assert.That(state.sim.Capture(1).Economy.Techs & (1UL << ((int)TechKind.Tools - 1)), Is.Not.EqualTo(0));
            int after = (int)gather.Invoke(state.sim, new object[] { 1u, foodNode });
            Assert.That(after, Is.LessThan(before));
        }

        [Test]
        public void AcademyResearchCannotBeTakenTwiceAcrossFacilities()
        {
            var state = AtAcademyAge();
            uint academy = PlaceAndBuild(state.scenario, state.sim, state.gateway, BuildingKind.Academy, ref state.sequence);
            GatherGold(state.scenario, state.sim, state.gateway, ref state.sequence);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, academy, TechKind.Tools));
            Steps(state.gateway, state.sim, 1);
            uint blacksmith = PlaceAndBuild(state.scenario, state.sim, state.gateway, BuildingKind.Blacksmith, ref state.sequence);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, blacksmith, TechKind.Tools));
            Steps(state.gateway, state.sim, 1);
            var smith = state.sim.Capture(1).Economy.Buildings.First(b => b.Id == blacksmith);
            Assert.That(smith.Researching, Is.EqualTo((TechKind)0));
            Steps(state.gateway, state.sim, state.scenario.Economy.AcademyToolsTicks + 1);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, blacksmith, TechKind.Tools));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Techs & (1UL << ((int)TechKind.Tools - 1)), Is.Not.EqualTo(0));
        }

        [Test]
        public void AcademyAutoBuildsAndResearchesInOrder()
        {
            var scenario = AcademyScenario(true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Academy));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2500);
            SetGold(sim, scenario.Economy.AcademyToolsGoldCost);
            Steps(gateway, sim, 500);
            var economy = sim.Capture(1).Economy;
            var academy = economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Academy);
            Assert.That(academy, Is.Not.Null);
            Assert.That(academy.Complete, Is.True);
            Assert.That(academy.Researching == TechKind.Tools || (economy.Techs & (1UL << ((int)TechKind.Tools - 1))) != 0, Is.True);
        }

        [Test]
        public void AcademyResearchesSecondAndThirdAgeTechsWithTheirEffects()
        {
            var state = AtAcademyAge();
            Advance(state.sim, state.gateway, state.scenario, ref state.sequence);
            Assert.That(state.sim.Capture(1).Economy.Age, Is.EqualTo(2));
            uint academy = PlaceAndBuild(state.scenario, state.sim, state.gateway, BuildingKind.Academy, ref state.sequence);
            SetStock(state.sim, ResourceKind.Gold, 1000);

            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, academy, TechKind.Weapons));
            Steps(state.gateway, state.sim, 1);
            Assert.That(Fields(state.sim)["Buildings[" + academy + "].Researching"], Is.EqualTo(((byte)TechKind.Weapons).ToString()));
            Steps(state.gateway, state.sim, 301);
            Assert.That(long.Parse(Fields(state.sim)["Economy[1].Techs"], System.Globalization.CultureInfo.InvariantCulture) & 1L, Is.Not.EqualTo(0));

            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, academy, TechKind.Armour));
            Steps(state.gateway, state.sim, 1); Steps(state.gateway, state.sim, 301);
            Assert.That(long.Parse(Fields(state.sim)["Economy[1].Techs"], System.Globalization.CultureInfo.InvariantCulture) & (1L << ((int)TechKind.Armour - 1)), Is.Not.EqualTo(0));

            Advance(state.sim, state.gateway, state.scenario, ref state.sequence);
            Assert.That(state.sim.Capture(1).Economy.Age, Is.EqualTo(3));
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, academy, TechKind.Siegecraft));
            Steps(state.gateway, state.sim, 1);
            Assert.That(Fields(state.sim)["Buildings[" + academy + "].Researching"], Is.EqualTo(((byte)TechKind.Siegecraft).ToString()));
            Steps(state.gateway, state.sim, 351);
            Assert.That(long.Parse(Fields(state.sim)["Economy[1].Techs"], System.Globalization.CultureInfo.InvariantCulture) & (1L << ((int)TechKind.Siegecraft - 1)), Is.Not.EqualTo(0));
        }

        [Test]
        public void AcademyResearchLeavesTheAgeAndBasicDefenceBudget()
        {
            var state = AtAcademyAge();
            uint academy = PlaceAndBuild(state.scenario, state.sim, state.gateway, BuildingKind.Academy, ref state.sequence);
            SetStock(state.sim, ResourceKind.Food, 679);
            SetStock(state.sim, ResourceKind.Wood, 324);
            SetStock(state.sim, ResourceKind.Gold, 100);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, academy, TechKind.Tools));
            Steps(state.gateway, state.sim, 1);
            Assert.That(Fields(state.sim)["Buildings[" + academy + "].Researching"], Is.EqualTo("0"));
            Assert.That(state.sim.Capture(1).Economy.Age, Is.EqualTo(1));
        }

        [Test]
        public void AcademyStopsAtLostGoldButBlacksmithAndAgeAdvanceContinue()
        {
            var scenario = AcademyScenario(true);
            scenario.ResourceNodes = scenario.ResourceNodes.Where(n => n.Kind != ResourceKind.Gold).ToArray();
            var sim = new Battle(scenario); var gateway = new CommandGateway(sim); ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Academy));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            uint academy = PlaceAndBuild(scenario, sim, gateway, BuildingKind.Academy, ref sequence);
            SetStock(sim, ResourceKind.Gold, 0);
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++sequence, academy, TechKind.Tools));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + academy + "].Researching"], Is.EqualTo("0"));
            uint blacksmith = PlaceAndBuild(scenario, sim, gateway, BuildingKind.Blacksmith, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++sequence, blacksmith, TechKind.Weapons));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + blacksmith + "].Researching"], Is.EqualTo(((byte)TechKind.Weapons).ToString()));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Academy));
            Steps(gateway, sim, scenario.Economy.Age2Ticks + 2);
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(2));
        }

        /// <summary>
        /// The academy flag may be saved without gold (the civilisation is then closed). With only the monk level set,
        /// the extension written for the academy must still not be read as the save-floor value.
        /// </summary>
        [Test]
        public void AcademyWithoutGoldButWithMonksRoundTrips()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Academy = true;
            scenario.Economy.MonksEnabled = true;
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That((decoded.Economy.Academy, decoded.Economy.MonksEnabled, decoded.Economy.GoldEnabled), Is.EqualTo((true, true, false)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void AcademyExtensionRoundTripsWithExistingTails()
        {
            var scenario = AcademyScenario(true);
            scenario.Economy.Forestry = true;
            scenario.Economy.Masonry = true;
            scenario.Economy.Caravan = true;
            scenario.Economy.Cavalry = true;
            scenario.Economy.Bridge = true;
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Academy, Is.True);
            Assert.That(decoded.Economy.AcademyToolsGoldCost, Is.EqualTo(scenario.Economy.AcademyToolsGoldCost));
            Assert.That(decoded.Extensions.Any(e => e.Id == 2), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void AcademyScenarioReplaysForTwentyThousandTicks()
        {
            var scenario = AcademyScenario(true);
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 20000, identity);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [TestCase(CivKind.Academy, CivKind.Agrarian)]
        [TestCase(CivKind.Agrarian, CivKind.Academy)]
        [TestCase(CivKind.Academy, CivKind.Metallurgy)]
        [TestCase(CivKind.Metallurgy, CivKind.Academy)]
        public void AcademyCombinationsReachTheSecondAgeWithoutFault(CivKind west, CivKind east)
        {
            var scenario = AcademyMatchScenario(21);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, west));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 2, east));
            for (int i = 0; i < 20000 && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "fault at tick " + sim.Capture(1).Tick);
            }
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), west + " reaches the second age");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), east + " reaches the second age");
        }

        [Test]
        public void AutomaticAcademyChoiceSeedReplaysForTwentyThousandTicks()
        {
            // Seed 7 is the recorded normal-start probe: the east faction automatically chooses Academy after scouting gold.
            var scenario = MapGenerator.GenerateTerrain(7, gold: true);
            scenario.Economy.Academy = true;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 20000);
            var west = sim.Capture(1).Economy;
            var east = sim.Capture(2).Economy;
            TestContext.WriteLine("normal seed 7: civs=" + west.Civ + "/" + east.Civ + ", ages=" + west.Age + "/" + east.Age
                + ", techs=" + west.Techs + "/" + east.Techs + ", gold=" + west.Gold + "/" + east.Gold + ", tick=" + sim.Capture(1).Tick);
            Assert.That(east.Civ, Is.EqualTo(CivKind.Academy));
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }
    }
}
