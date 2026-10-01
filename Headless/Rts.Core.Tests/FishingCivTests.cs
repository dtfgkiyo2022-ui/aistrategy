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
    public sealed class FishingCivTests
    {
        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition Scenario(bool fishingCiv = true, bool emptyFood = false)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.FishingEnabled = true;
            scenario.Economy.FishingCiv = fishingCiv;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.AutoVillagerTarget = 3;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000;
            if (emptyFood)
            {
                for (int i = 0; i < scenario.ResourceNodes.Length; i++)
                    if (scenario.ResourceNodes[i].Kind == ResourceKind.Food) scenario.ResourceNodes[i].Amount = 1;
                scenario.Economy.FishRegrowTicks = 100000;
            }
            return scenario;
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtFishingAge(bool auto = false, bool emptyFood = false)
        {
            var scenario = Scenario(true, emptyFood);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, auto));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Fishing));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 3);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Fishing));
            return (scenario, sim, gateway, sequence);
        }

        private static bool RiverAdjacent(ScenarioDefinition scenario, int origin)
        {
            int width = scenario.Map.WidthCells, size = scenario.Economy.HarborSizeCells;
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    int cell = origin + z * width + x;
                    if (x == 0 && origin % width > 0 && scenario.Map.Terrain[cell - 1] == (byte)TerrainKind.River
                        || x + 1 == size && origin % width + size < width && scenario.Map.Terrain[cell + 1] == (byte)TerrainKind.River
                        || z == 0 && origin / width > 0 && scenario.Map.Terrain[cell - width] == (byte)TerrainKind.River
                        || z + 1 == size && origin / width + size < scenario.Map.HeightCells && scenario.Map.Terrain[cell + width] == (byte)TerrainKind.River)
                        return true;
                }
            return false;
        }

        private static uint PlaceHarbor(ref (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) state)
        {
            int width = state.scenario.Map.WidthCells, cells = width * state.scenario.Map.HeightCells;
            int size = state.scenario.Economy.HarborSizeCells;
            ulong sequence = state.sequence;
            for (int origin = 0; origin < cells; origin++)
            {
                if (origin % width + size > width || origin / width + size > state.scenario.Map.HeightCells || !RiverAdjacent(state.scenario, origin)) continue;
                sequence++;
                state.gateway.SubmitEconomy(EconomyCommand.Place(1, sequence, BuildingKind.Harbor, origin, Facing.North));
                Steps(state.gateway, state.sim, 1);
                var harbor = state.sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Harbor);
                if (harbor.Id != 0) { state.sequence = sequence; return harbor.Id; }
            }
            state.sequence = sequence;
            return 0;
        }

        private static uint PlaceAndBuildHarbor(ref (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) state)
        {
            uint harbor = PlaceHarbor(ref state);
            Assert.That(harbor, Is.Not.EqualTo(0u));
            state.gateway.SubmitEconomy(EconomyCommand.Assign(1, ++state.sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, harbor));
            Steps(state.gateway, state.sim, 1500);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == harbor).Complete, Is.True);
            return harbor;
        }

        private static void AdvanceFishingAge(ref (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) state)
        {
            state.gateway.SubmitEconomy(EconomyCommand.Advance(1, ++state.sequence, CivKind.Fishing));
            Steps(state.gateway, state.sim, state.scenario.Economy.Age2Ticks + 3);
            Assert.That(state.sim.Capture(1).Economy.Age, Is.EqualTo(2));
        }

        private static int FishingScoreOf(Battle sim, SimPoint core, uint faction = 1)
        {
            var method = typeof(Battle).GetMethod("FishingScore", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (int)method.Invoke(null, new object[] { sim, faction, core, 0, 0 });
        }

        private static bool IsFishingNode(ScenarioDefinition scenario, ResourceNodeDefinition node)
        {
            long reach = Fix64.FromInt(scenario.Economy.FishReach).Raw;
            long limit = checked(reach * reach);
            int width = scenario.Map.WidthCells;
            for (int cell = 0; cell < scenario.Map.Terrain.Length; cell++)
            {
                if (scenario.Map.Terrain[cell] != (byte)TerrainKind.River) continue;
                long x = Fix64.FromInt((cell % width) * scenario.Map.CellSizeMeters + scenario.Map.CellSizeMeters / 2).Raw;
                long z = Fix64.FromInt((cell / width) * scenario.Map.CellSizeMeters + scenario.Map.CellSizeMeters / 2).Raw;
                long dx = node.Position.X.Raw - x, dz = node.Position.Z.Raw - z;
                if (checked(dx * dx + dz * dz) <= limit) return true;
            }
            return false;
        }

        private static int UsableFishingFishOf(Battle sim, SimPoint core, uint faction = 1)
        {
            var method = typeof(Battle).GetMethod("CountUsableFishingFish", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (int)method.Invoke(sim, new object[] { faction, core });
        }

        private static ScenarioDefinition ScenarioWithOnlyUsableFish(int desired)
        {
            const ulong seed = 4;
            var candidates = new List<SimPoint>();
            var source = MapGenerator.GenerateTerrain(seed);
            for (int i = 0; i < source.ResourceNodes.Length; i++)
            {
                if (source.ResourceNodes[i].Kind != ResourceKind.Food || !IsFishingNode(source, source.ResourceNodes[i])) continue;
                var probe = MapGenerator.GenerateTerrain(seed);
                probe.Economy.FishingEnabled = true;
                probe.Economy.FishingCiv = true;
                var selected = source.ResourceNodes[i];
                KeepFoodNodes(probe, new[] { selected.Position });
                var sim = new Battle(probe);
                if (UsableFishingFishOf(sim, probe.Cores[0].Position) == 1) candidates.Add(selected.Position);
            }
            Assert.That(candidates.Count, Is.GreaterThanOrEqualTo(desired), "seed 4 has enough individually usable fish");

            var scenario = MapGenerator.GenerateTerrain(seed);
            scenario.Economy.FishingEnabled = true;
            scenario.Economy.FishingCiv = true;
            KeepFoodNodes(scenario, candidates.Take(desired).ToArray());
            return scenario;
        }

        private static void KeepFoodNodes(ScenarioDefinition scenario, IReadOnlyCollection<SimPoint> keep)
        {
            var nodes = scenario.ResourceNodes.Where(node => node.Kind != ResourceKind.Food
                || keep.Any(position => position.X.Raw == node.Position.X.Raw && position.Z.Raw == node.Position.Z.Raw)).ToArray();
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                node.Id = (uint)i + 1;
                nodes[i] = node;
            }
            scenario.ResourceNodes = nodes;
        }

        private static ScenarioDefinition FishingMatchScenario(ulong seed)
        {
            var scenario = MapGenerator.GenerateTerrain(seed);
            scenario.Economy.FishingEnabled = true;
            scenario.Economy.FishingCiv = true;
            scenario.Economy.StartFood = 50000;
            scenario.Economy.StartWood = 50000;
            scenario.Economy.AutoVillagerTarget = 3;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.Age2FoodCost = 0;
            scenario.Economy.Age2WoodCost = 0;
            scenario.Economy.Age2Ticks = 1;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            return scenario;
        }

        [Test]
        public void FishingScoresAreTieredFromUsableExploredFish()
        {
            foreach (var expected in new[] { 0, 2, 3 })
            {
                var scenario = ScenarioWithOnlyUsableFish(expected == 0 ? 0 : expected == 2 ? 1 : 3);
                var sim = new Battle(scenario);
                Assert.That(UsableFishingFishOf(sim, scenario.Cores[0].Position), Is.EqualTo(expected == 0 ? 0 : expected == 2 ? 1 : 3));
                Assert.That(FishingScoreOf(sim, scenario.Cores[0].Position), Is.EqualTo(expected));
            }
        }

        [Test]
        public void FishingScoreIgnoresUnexploredFish()
        {
            var scenario = ScenarioWithOnlyUsableFish(1);
            var sim = new Battle(scenario);
            var fish = scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Food && n.Amount > 0);
            int cellSize = scenario.Map.CellSizeMeters;
            int cell = (int)(fish.Position.Z.Raw / Fix64.FromInt(cellSize).Raw) * scenario.Map.WidthCells
                + (int)(fish.Position.X.Raw / Fix64.FromInt(cellSize).Raw);
            var worldField = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic);
            var world = worldField.GetValue(sim);
            var factionsField = world.GetType().GetField("Factions", BindingFlags.Instance | BindingFlags.NonPublic);
            var factions = (Array)factionsField.GetValue(world);
            var faction = factions.GetValue(0);
            var exploredField = faction.GetType().GetField("ExploredCells", BindingFlags.Instance | BindingFlags.NonPublic);
            var explored = (bool[])exploredField.GetValue(faction);
            explored[cell] = false;
            Assert.That(FishingScoreOf(sim, scenario.Cores[0].Position), Is.EqualTo(0));
        }

        [Test]
        public void FishingScoreIgnoresFishWithNoHarborSite()
        {
            var scenario = ScenarioWithOnlyUsableFish(1);
            var before = new Battle(scenario);
            var fish = scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Food && n.Amount > 0);
            var clear = typeof(Battle).GetMethod("HarborSiteIsClear", BindingFlags.Instance | BindingFlags.NonPublic);
            var blocked = new HashSet<int>(scenario.Map.BlockedCellIds);
            int width = scenario.Map.WidthCells, height = scenario.Map.HeightCells, size = scenario.Economy.HarborSizeCells;
            long reach = Fix64.FromInt(scenario.Economy.FishReach * 2).Raw;
            long limit = checked(reach * reach);
            for (int origin = 0; origin < width * height; origin++)
            {
                if (origin % width + size > width || origin / width + size > height) continue;
                long centreX = Fix64.FromInt((origin % width) * scenario.Map.CellSizeMeters
                    + size * scenario.Map.CellSizeMeters / 2).Raw;
                long centreZ = Fix64.FromInt((origin / width) * scenario.Map.CellSizeMeters
                    + size * scenario.Map.CellSizeMeters / 2).Raw;
                long dx = centreX - fish.Position.X.Raw, dz = centreZ - fish.Position.Z.Raw;
                if (checked(dx * dx + dz * dz) > limit) continue;
                if (!(bool)clear.Invoke(before, new object[] { 1u, origin })) continue;
                for (int z = 0; z < size; z++) for (int x = 0; x < size; x++) blocked.Add(origin + z * width + x);
            }
            scenario.Map.BlockedCellIds = blocked.OrderBy(cell => cell).ToArray();
            var sim = new Battle(scenario);
            Assert.That(UsableFishingFishOf(sim, scenario.Cores[0].Position), Is.EqualTo(0));
            Assert.That(FishingScoreOf(sim, scenario.Cores[0].Position), Is.EqualTo(0));
        }

        [Test]
        public void FishingChoiceSeedsAreReported()
        {
            var choose = typeof(Battle).GetMethod("ChooseCiv", BindingFlags.Instance | BindingFlags.NonPublic);
            var fishing = new List<ulong>();
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var scenario = MapGenerator.GenerateTerrain(seed);
                scenario.Economy.FishingEnabled = true;
                scenario.Economy.FishingCiv = true;
                var sim = new Battle(scenario);
                var west = (CivKind)choose.Invoke(sim, new object[] { 1u });
                var east = (CivKind)choose.Invoke(sim, new object[] { 2u });
                TestContext.WriteLine("seed " + seed + ": " + west + "/" + east);
                if (west == CivKind.Fishing || east == CivKind.Fishing) fishing.Add(seed);
            }
            Assert.That(fishing.Count, Is.GreaterThan(0), "漁労が選ばれる種がある");
            TestContext.WriteLine("fishing choice seeds: " + string.Join(",", fishing));
        }

        [TestCase(CivKind.Fishing, CivKind.Agrarian)]
        [TestCase(CivKind.Agrarian, CivKind.Fishing)]
        [TestCase(CivKind.Fishing, CivKind.Metallurgy)]
        [TestCase(CivKind.Metallurgy, CivKind.Fishing)]
        public void FishingMatchesReachTheSecondAgeAndReplay(CivKind west, CivKind east)
        {
            var scenario = FishingMatchScenario(22);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, west));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 2, east));
            Steps(gateway, sim, 4);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(west));
            Assert.That(sim.Capture(2).Economy.Civ, Is.EqualTo(east));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 3, west));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 4, east));
            Steps(gateway, sim, 4);
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2));
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2));

            for (int i = 0; i < 20000 && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "fault at tick " + sim.Capture(1).Tick);
            }
            TestContext.WriteLine(west + " vs " + east + ": tick=" + sim.Capture(1).Tick
                + ", civs=" + sim.Capture(1).Economy.Civ + "/" + sim.Capture(2).Economy.Civ
                + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age);
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

        [Test]
        public void FishingNormalStartReplaysForTwentyThousandTicks()
        {
            var scenario = FishingMatchScenario(7);
            scenario.Economy.AutoVillagerTarget = 10;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 20000);
            var west = sim.Capture(1).Economy.Civ;
            var east = sim.Capture(2).Economy.Civ;
            TestContext.WriteLine("normal seed 7: civs=" + west + "/" + east + ", tick=" + sim.Capture(1).Tick
                + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age);
            Assert.That(west == CivKind.Fishing || east == CivKind.Fishing, Is.True, "通常開始で漁労が選ばれる");
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2));
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2));
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

        [Test]
        public void FishingCivOffKeepsOldBytesAndState()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.FishingEnabled = false;
            off.Economy.FishingCiv = false;
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
        public void FishingCivCannotBeChosenWithoutFishingEnabled()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.FishingEnabled = false;
            scenario.Economy.FishingCiv = false;
            scenario.Economy.StartFood = 5000; scenario.Economy.StartWood = 5000; scenario.Economy.AdvanceTicks = 1;
            var sim = new Battle(scenario); var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Fishing));
            Steps(gateway, sim, 3);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Primitive));
        }

        [Test]
        public void FishingCivCanPlaceHarborOnlyOnRiverBank()
        {
            var state = AtFishingAge();
            int before = state.sim.Capture(1).Economy.Buildings.Count;
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.Harbor, 0, Facing.North));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Count, Is.EqualTo(before));
            Assert.That(PlaceHarbor(ref state), Is.Not.EqualTo(0u));
        }

        [Test]
        public void FishingExtensionRoundTripsWithAcademyAndExistingExtension()
        {
            var scenario = Scenario();
            scenario.Economy.Academy = true;
            scenario.Extensions = new[] { new ScenarioExtensionData { Id = 1, Version = 1, Data = BitConverter.GetBytes(1234) } };
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.FishingCiv, Is.True);
            Assert.That(decoded.Extensions.Select(e => e.Id), Is.EquivalentTo(new[] { 1, 2, 4 }));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void FishingCivAutomaticallyBuildsHarborAndGathersFish()
        {
            var state = AtFishingAge(true);
            Steps(state.gateway, state.sim, 5000);
            var economy = state.sim.Capture(1).Economy;
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Harbor && b.Complete), Is.True);
            Assert.That(economy.Villagers.Any(v => v.Activity == VillagerActivity.Gathering || v.Activity == VillagerActivity.ToResource), Is.True);
        }

        [Test]
        public void FishingCivReplayIsDeterministicForTwentyThousandTicks()
        {
            var scenario = Scenario();
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

        [Test]
        public void FishingNetResearchRunsAtHarborAndOnlyRaisesFishCarry()
        {
            var state = AtFishingAge();
            uint harbor = PlaceAndBuildHarbor(ref state);
            AdvanceFishingAge(ref state);
            var before = state.sim.Capture(1).Economy;
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, harbor, FishingTech.FishingNet));
            Steps(state.gateway, state.sim, state.scenario.Economy.FishingNetTicks + 2);
            var after = state.sim.Capture(1).Economy;
            Assert.That(after.Techs & (1UL << 15), Is.Not.EqualTo(0));
            var carry = typeof(Battle).GetMethod("CarryFor", BindingFlags.Instance | BindingFlags.NonPublic);
            int normalBefore = (int)carry.Invoke(state.sim, new object[] { 1u, false });
            int fishAfter = (int)carry.Invoke(state.sim, new object[] { 1u, true });
            Assert.That(fishAfter, Is.GreaterThan(normalBefore));
            Assert.That(after.Techs & (1UL << ((int)TechKind.Tools - 1)), Is.EqualTo(before.Techs & (1UL << ((int)TechKind.Tools - 1))));
        }

        [Test]
        public void DriedFishResearchRunsAtTheThirdAgeAndStoresTheFasterRegrowEffect()
        {
            var state = AtFishingAge();
            uint harbor = PlaceAndBuildHarbor(ref state);
            AdvanceFishingAge(ref state);
            state.gateway.SubmitEconomy(EconomyCommand.Advance(1, ++state.sequence, CivKind.Fishing));
            Steps(state.gateway, state.sim, state.scenario.Economy.Age3Ticks + 3);
            Assert.That(state.sim.Capture(1).Economy.Age, Is.EqualTo(3));
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, harbor, FishingTech.DriedFish));
            Steps(state.gateway, state.sim, 1);
            var building = state.sim.Capture(1).Economy.Buildings.First(b => b.Id == harbor);
            Assert.That(building.Researching, Is.EqualTo(FishingTech.DriedFish));
            Steps(state.gateway, state.sim, state.scenario.Economy.DriedFishTicks + 2);
            Assert.That(state.sim.Capture(1).Economy.Techs & (1UL << 16), Is.Not.EqualTo(0));
            Assert.That(state.scenario.Economy.DriedFishRegrowIntervalPermille, Is.EqualTo(333));
        }

        [Test]
        public void FishingCivOpensFoodMarketAfterCoveredFishIsGone()
        {
            var state = AtFishingAge(false, true);
            PlaceAndBuildHarbor(ref state);
            state.gateway.SubmitEconomy(EconomyCommand.Auto(1, ++state.sequence, true));
            Steps(state.gateway, state.sim, 600);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Market), Is.True);
        }

        [Test]
        public void FishingExtensionVersionOneIsReadableAndVersionTwoRoundTrips()
        {
            var scenario = Scenario();
            byte[] current = ScenarioBinary.Encode(scenario);
            int marker = FindInt32(current, 0x4E545845);
            Assert.That(marker, Is.GreaterThanOrEqualTo(0));
            int record = marker + 12;
            Assert.That(BitConverter.ToInt32(current, record), Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(current, record + 4), Is.EqualTo(2));
            Assert.That(BitConverter.ToInt32(current, record + 8), Is.EqualTo(52));
            byte[] old = new byte[current.Length - 32];
            Buffer.BlockCopy(current, 0, old, 0, record + 12 + 20);
            Buffer.BlockCopy(current, record + 12 + 52, old, record + 12 + 20, current.Length - (record + 12 + 52));
            Array.Copy(BitConverter.GetBytes(BitConverter.ToInt32(current, marker + 8) - 32), 0, old, marker + 8, 4);
            Array.Copy(BitConverter.GetBytes(1), 0, old, record + 4, 4);
            Array.Copy(BitConverter.GetBytes(20), 0, old, record + 8, 4);
            var decoded = ScenarioBinary.Decode(old);
            Assert.That(decoded.Economy.FishingCiv, Is.True);
            byte[] upgraded = ScenarioBinary.Encode(decoded);
            Assert.That(BitConverter.ToInt32(upgraded, FindInt32(upgraded, 0x4E545845) + 20), Is.EqualTo(52));
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(upgraded)), Is.EqualTo(upgraded));
        }

        private static int FindInt32(byte[] bytes, int value)
        {
            byte[] pattern = BitConverter.GetBytes(value);
            for (int i = 0; i <= bytes.Length - pattern.Length; i++)
                if (bytes[i] == pattern[0] && bytes[i + 1] == pattern[1] && bytes[i + 2] == pattern[2] && bytes[i + 3] == pattern[3]) return i;
            return -1;
        }
    }
}
