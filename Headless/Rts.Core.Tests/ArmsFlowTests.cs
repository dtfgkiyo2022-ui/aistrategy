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
    public sealed class ArmsFlowTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = LiveGameRules.Create(78002UL, true, true, false, false);
            var e = scenario.Economy;
            e.CraftRequired = true;
            e.ArmsScale = enabled;
            e.ArmsFlow = enabled;
            e.AutoInfantryQueue = 0;
            e.AutoVillagerTarget = 0;
            e.StartFood = e.StartWood = e.StartStone = e.StartMetal = 100000;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }

        [Test]
        public void FlagOffKeepsHashesStableAndOmitsExtension82()
        {
            var source = Scenario(false);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Extensions.Any(x => x.Id == 82), Is.False);
            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void ArmsFlowDecisionsRunOnlyAtConfiguredInterval()
        {
            var scenario = Scenario(true);
            scenario.Economy.AiBelts = false;
            scenario.Economy.ArmsScaleDecisionIntervalTicks = 600;
            var decisionTicks = new List<long>();
            long currentTick = 0;
            Action<string, bool> measure = (name, start) =>
            {
                if (start && name == "AI.DecideEconomy.DecideIndustry.ArmsFlow") decisionTicks.Add(currentTick);
            };
            var simulation = new Battle(scenario, measure);
            for (long tick = 1; tick <= 5000; tick++)
            {
                currentTick = tick;
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            }
            var distinctTicks = decisionTicks.Distinct().OrderBy(tick => tick).ToArray();
            Assert.That(distinctTicks, Is.Not.Empty);
            Assert.That(distinctTicks.All(tick => tick % scenario.Economy.ArmsScaleDecisionIntervalTicks == 0), Is.True);
            Assert.That(distinctTicks.Zip(distinctTicks.Skip(1), (left, right) => right - left).All(gap => gap >= scenario.Economy.ArmsScaleDecisionIntervalTicks), Is.True);
        }

        [Test]
        public void Extension82RoundTripsArmsFlowRules()
        {
            var source = Scenario(true);
            var e = source.Economy;
            e.ArmsFlowFittingsLow = 21;
            e.ArmsFlowMaxMetalLinesAge2 = 5;
            e.ArmsFlowMaxMetalLinesAge3 = 7;
            e.ArmsFlowMaxMetalLinesAge4 = 9;
            e.ArmsFlowPlankHigh = 301;
            e.ArmsFlowSawmillsPerBarracksPermille = 751;
            e.ArmsFlowKilnsPerBarracksPermille = 752;
            e.ArmsFlowMetalLinesPerBarracksPermille = 376;
            e.ArmsFlowMetalworksPerBarracksPermille = 251;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Extensions.Single(x => x.Id == 82).Version, Is.EqualTo(2));
            Assert.That(decoded.Economy.ArmsFlow, Is.True);
            Assert.That(decoded.Economy.ArmsFlowFittingsLow, Is.EqualTo(21));
            Assert.That(decoded.Economy.ArmsFlowMaxMetalLinesAge4, Is.EqualTo(9));
            Assert.That(decoded.Economy.ArmsFlowPlankHigh, Is.EqualTo(301));
            Assert.That(decoded.Economy.ArmsFlowSawmillsPerBarracksPermille, Is.EqualTo(751));
            Assert.That(decoded.Economy.ArmsFlowKilnsPerBarracksPermille, Is.EqualTo(752));
            Assert.That(decoded.Economy.ArmsFlowMetalLinesPerBarracksPermille, Is.EqualTo(376));
            Assert.That(decoded.Economy.ArmsFlowMetalworksPerBarracksPermille, Is.EqualTo(251));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(source)));

            var oldDecoded = ScenarioBinary.Decode(DowngradeArmsFlowExtension(ScenarioBinary.Encode(source)));
            Assert.That(oldDecoded.Extensions.Single(x => x.Id == 82).Version, Is.EqualTo(1));
            Assert.That(oldDecoded.Economy.ArmsFlow, Is.True);
            Assert.That(oldDecoded.Economy.ArmsFlowFittingsLow, Is.EqualTo(21));
            Assert.That(oldDecoded.Economy.ArmsFlowSawmillsPerBarracksPermille, Is.EqualTo(750));
            Assert.That(oldDecoded.Economy.ArmsFlowMetalworksPerBarracksPermille, Is.EqualTo(250));
        }

        [Test]
        public void AgeTwoCapacityAddsSeveralKnownOreLinesWithinCap()
        {
            var scenario = Scenario(true);
            scenario.Economy.AiBelts = false;
            var simulation = new Battle(scenario);
            RevealAllCells(simulation, 0);
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 100000);
            SetEconomy(simulation, 0, "Stone", 10000);
            SetEconomy(simulation, 0, "Planks", 10000);
            SetEconomy(simulation, 0, "Frames", 10000);
            SetEconomy(simulation, 0, "Rope", 10000);
            SetEconomy(simulation, 0, "Fittings", 10000);
            SetEconomy(simulation, 0, "Metal", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            AddCompletedBarracks(simulation, 4);
            for (long tick = 1; tick <= 2000; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(simulation.Capture(1).Economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Barracks), Is.EqualTo(4));
            SetEconomy(simulation, 0, "Fittings", 0);
            SetEconomy(simulation, 0, "Metal", 0);
            for (int i = 0; i < 5; i++) Invoke(simulation, "EnsureArmsFlowCapacity", 1u);
            for (int pass = 0; pass < 8; pass++)
            {
                var currentWorld = World(simulation);
                var currentLines = (Array)currentWorld.GetType().GetField("ProcessingLines", Private).GetValue(currentWorld);
                for (int i = 0; i < currentLines.Length; i++)
                {
                    var line = currentLines.GetValue(i);
                    if ((uint)line.GetType().GetField("FactionId", Private).GetValue(line) == 1
                        && line.GetType().GetField("Kind", Private).GetValue(line).ToString() == "CoreMetal"
                        && (bool)line.GetType().GetField("ArmsFlow", Private).GetValue(line))
                    {
                        CompleteLineBuildings(simulation, i);
                        Invoke(simulation, "EnsureArmsFlowMetalLine", 1u, i);
                    }
                }
                Invoke(simulation, "EnsureArmsFlowCapacity", 1u);
            }
            for (long tick = 2001; tick <= 5000; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var economy = simulation.Capture(1).Economy;
            Assert.That(economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Assembler), Is.GreaterThanOrEqualTo(3));
            Assert.That(economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Mine), Is.GreaterThan(0));
            var world = World(simulation);
            var processingLines = (Array)world.GetType().GetField("ProcessingLines", Private).GetValue(world);
            var armsLines = processingLines.Cast<object>().Where(line =>
                (uint)line.GetType().GetField("FactionId", Private).GetValue(line) == 1
                && line.GetType().GetField("Kind", Private).GetValue(line).ToString() == "CoreMetal"
                && (bool)line.GetType().GetField("ArmsFlow", Private).GetValue(line)).ToArray();
            Assert.That(armsLines.Length, Is.GreaterThan(0));
            Assert.That(armsLines.Length, Is.LessThanOrEqualTo(4));
            var mineNodes = armsLines.Select(line => (uint)line.GetType().GetField("MineId", Private).GetValue(line))
                .Where(id => id != 0).Select(id =>
                {
                    var buildings = (Array)world.GetType().GetField("Buildings", Private).GetValue(world);
                    var building = buildings.GetValue((int)id - 1);
                    return (uint)building.GetType().GetField("NodeId", Private).GetValue(building);
                }).Where(id => id != 0).ToArray();
            Assert.That(mineNodes.Distinct().Count(), Is.EqualTo(mineNodes.Length));
        }

        [Test]
        public void ArmsStockDoesNotGateMetalLineCapacity()
        {
            var simulation = new Battle(Scenario(true));
            RevealAllCells(simulation, 0);
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 3);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            SetEconomy(simulation, 0, "Fittings", 0);
            SetEconomy(simulation, 0, "Metal", 0);
            AddCompletedBarracks(simulation, 4);
            Invoke(simulation, "EnsureArmsFlowCapacity", 1u);
            var world = World(simulation);
            var lines = (Array)world.GetType().GetField("ProcessingLines", Private).GetValue(world);
            Assert.That(lines.Cast<object>().Count(line => (uint)line.GetType().GetField("FactionId", Private).GetValue(line) == 1
                && (bool)line.GetType().GetField("ArmsFlow", Private).GetValue(line)), Is.GreaterThan(0));
        }

        [Test]
        public void BarracksRatiosAddSeveralKindsAndDoNotExceedTargets()
        {
            var simulation = new Battle(Scenario(true));
            RevealAllCells(simulation, 0);
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 100000);
            SetEconomy(simulation, 0, "Wood", 100000);
            SetEconomy(simulation, 0, "Stone", 100000);
            SetEconomy(simulation, 0, "Planks", 100000);
            SetEconomy(simulation, 0, "Frames", 100000);
            SetEconomy(simulation, 0, "Bricks", 100000);
            SetEconomy(simulation, 0, "SiegeParts", 100000);
            SetEconomy(simulation, 0, "Fittings", 100000);
            SetEconomy(simulation, 0, "Rope", 100000);
            AddCompletedBarracks(simulation, 4);
            for (long tick = 1; tick <= 6000; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var economy = simulation.Capture(1).Economy;
            Assert.That(economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Assembler), Is.EqualTo(4));
            Assert.That(economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Sawmill), Is.GreaterThanOrEqualTo(3));
            Assert.That(economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.CharcoalKiln), Is.GreaterThanOrEqualTo(3));
            Assert.That(economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Metalworks), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void BarracksRatioAddsTheNextChainBeforeAiExpandTargetIsFull()
        {
            var simulation = new Battle(Scenario(true));
            RevealAllCells(simulation, 0);
            AddCompletedBarracks(simulation, 1);
            object firstSnapshot = Invoke(simulation, "CaptureArmsFlowCapacity", 1u);
            int firstTarget = ArmsFlowTarget(simulation, firstSnapshot, 0);

            AddCompletedBarracks(simulation, 1);
            object secondSnapshot = Invoke(simulation, "CaptureArmsFlowCapacity", 1u);
            int secondTarget = ArmsFlowTarget(simulation, secondSnapshot, 0);

            Assert.That(firstTarget, Is.EqualTo(1));
            Assert.That(secondTarget, Is.EqualTo(2));
        }

        [Test]
        public void HighPlanksDoNotAddAnotherAutomaticSawmill()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Stone", 10000);
            SetEconomy(simulation, 0, "Planks", 300);
            int before = simulation.Capture(1).Economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Sawmill);
            Assert.That((bool)Invoke(simulation, "EnsureArmsScaleSawmill", 1u), Is.False);
            Assert.That(simulation.Capture(1).Economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Sawmill), Is.EqualTo(before));
        }

        [Test]
        public void OneJudgementCanAddSawmillAndAssembler()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Stone", 10000);
            SetEconomy(simulation, 0, "Planks", 0);
            SetEconomy(simulation, 0, "Frames", 10000);
            SetEconomy(simulation, 0, "Bricks", 10000);
            SetEconomy(simulation, 0, "SiegeParts", 10000);
            SetEconomy(simulation, 0, "Fittings", 10000);
            SetEconomy(simulation, 0, "Rope", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            for (long tick = 1; tick <= 2000; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            Invoke(simulation, "EnsureArmsScaleCapacity", 1u);
            var buildings = simulation.Capture(1).Economy.Buildings.Where(b => b.FactionId == 1).ToArray();
            Assert.That(buildings.Any(b => b.Kind == BuildingKind.Assembler), Is.True);
        }

        [Test]
        public void ExistingCapacityJudgementIsNotReplacedByArmsFlow()
        {
            var on = new Battle(Scenario(true));
            var off = new Battle(Scenario(false));
            PrepareCapacityScenario(on);
            PrepareCapacityScenario(off);
            for (int i = 0; i < 8; i++)
            {
                Invoke(on, "EnsureCraftRequiredAiMaterialCapacity", 1u);
                Invoke(off, "EnsureCraftRequiredAiMaterialCapacity", 1u);
            }

            object before = Invoke(on, "CaptureArmsFlowCapacity", 1u);
            object offSnapshot = Invoke(off, "CaptureArmsFlowCapacity", 1u);
            int kilnTarget = ArmsFlowTarget(on, before, 2);
            int beforeKilns = SnapshotCount(before, "Kilns");
            int offKilns = SnapshotCount(offSnapshot, "Kilns");
            Assert.That(beforeKilns, Is.GreaterThan(kilnTarget));

            int[] targets = Enumerable.Range(0, 5).Select(kind => ArmsFlowTarget(on, before, (byte)kind)).ToArray();
            int[] counts = Enumerable.Range(0, 5).Select(kind => SnapshotCount(before, SnapshotField(kind))).ToArray();
            Invoke(on, "EnsureArmsFlowCapacity", 1u);
            object after = Invoke(on, "CaptureArmsFlowCapacity", 1u);
            for (int kind = 0; kind < 5; kind++)
                if (counts[kind] >= targets[kind])
                    Assert.That(SnapshotCount(after, SnapshotField(kind)), Is.EqualTo(counts[kind]), "kind " + kind);
            Assert.That(SnapshotCount(after, "Kilns"), Is.GreaterThanOrEqualTo(beforeKilns));
            Assert.That(SnapshotCount(after, "Kilns"), Is.GreaterThanOrEqualTo(offKilns));
        }

        private static object World(Battle simulation) => typeof(Battle).GetField("world", Private).GetValue(simulation);

        private static void PrepareCapacityScenario(Battle simulation)
        {
            RevealAllCells(simulation, 0);
            SetEconomy(simulation, 0, "Age", 2);
            foreach (string name in new[] { "Food", "Wood", "Stone", "Planks", "Frames", "Bricks", "SiegeParts", "Fittings", "Rope", "Metal" })
                SetEconomy(simulation, 0, name, 100000);
            AddCompletedBarracks(simulation, 1);
        }

        private static string SnapshotField(int kind)
            => new[] { "Assemblers", "Sawmills", "Kilns", "MetalLines", "Metalworks" }[kind];

        private static int SnapshotCount(object snapshot, string field)
            => (int)snapshot.GetType().GetField(field, Private).GetValue(snapshot);

        private static byte[] DowngradeArmsFlowExtension(byte[] encoded)
        {
            var bytes = new List<byte>(encoded);
            byte[] marker = BitConverter.GetBytes(unchecked((int)0x4E545845));
            int extensionStart = -1;
            for (int i = 0; i <= bytes.Count - marker.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < marker.Length; j++) if (bytes[i + j] != marker[j]) { match = false; break; }
                if (match) { extensionStart = i; break; }
            }
            Assert.That(extensionStart, Is.GreaterThanOrEqualTo(0));
            int sectionLengthOffset = extensionStart + 8;
            int sectionLength = BitConverter.ToInt32(bytes.ToArray(), sectionLengthOffset);
            int cursor = extensionStart + 12;
            int sectionEnd = cursor + sectionLength;
            while (cursor < sectionEnd)
            {
                int id = BitConverter.ToInt32(bytes.ToArray(), cursor);
                int versionOffset = cursor + 4;
                int dataLengthOffset = cursor + 8;
                int dataLength = BitConverter.ToInt32(bytes.ToArray(), dataLengthOffset);
                int dataOffset = cursor + 12;
                if (id == 82)
                {
                    Assert.That(dataLength, Is.EqualTo(40));
                    var oldData = new byte[24];
                    Array.Copy(bytes.ToArray(), dataOffset, oldData, 0, 4);
                    Array.Copy(bytes.ToArray(), dataOffset + 20, oldData, 4, 20);
                    bytes[versionOffset] = 1; bytes[versionOffset + 1] = 0; bytes[versionOffset + 2] = 0; bytes[versionOffset + 3] = 0;
                    bytes[dataLengthOffset] = 24; bytes[dataLengthOffset + 1] = 0; bytes[dataLengthOffset + 2] = 0; bytes[dataLengthOffset + 3] = 0;
                    bytes.RemoveRange(dataOffset, 40);
                    bytes.InsertRange(dataOffset, oldData);
                    int newSectionLength = sectionLength - 16;
                    byte[] lengthBytes = BitConverter.GetBytes(newSectionLength);
                    for (int i = 0; i < 4; i++) bytes[sectionLengthOffset + i] = lengthBytes[i];
                    return bytes.ToArray();
                }
                cursor = dataOffset + dataLength;
            }
            Assert.Fail("extension 82 not found");
            return null;
        }

        private static void AddCompletedBarracks(Battle simulation, int count)
        {
            var world = World(simulation);
            var core = (Array)world.GetType().GetField("Cores", Private).GetValue(world);
            var coreDefinition = core.GetValue(0);
            var position = coreDefinition.GetType().GetField("Definition", Private).GetValue(coreDefinition);
            var point = position.GetType().GetField("Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(position);
            var map = world.GetType().GetField("Map", Private).GetValue(world);
            int coreCell = (int)map.GetType().GetMethod("Cell", Private | BindingFlags.Public).Invoke(map, new[] { point });
            int centre = (int)Invoke(simulation, "FindAiExpandBarracksSite", 1u, coreCell);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) centre = (int)Invoke(simulation, "FindAiExpandBarracksSite", 1u, centre);
                typeof(Battle).GetMethods(Private).Single(method => method.Name == "PlaceBuildingAt" && method.GetParameters().Length == 5)
                    .Invoke(simulation, new object[] { 1u, BuildingKind.Barracks, centre, Facing.North, 0u });
                var buildings = (Array)world.GetType().GetField("Buildings", Private).GetValue(world);
                uint nextBuildingId = (uint)world.GetType().GetField("NextBuildingId", Private).GetValue(world);
                int index = checked((int)nextBuildingId - 2);
                var building = buildings.GetValue(index);
                building.GetType().GetField("Complete", Private).SetValue(building, true);
                buildings.SetValue(building, index);
            }
        }

        private static void RevealAllCells(Battle simulation, int index)
        {
            var world = World(simulation);
            var factions = (Array)world.GetType().GetField("Factions", Private).GetValue(world);
            var faction = factions.GetValue(index);
            var cells = (bool[])faction.GetType().GetField("ExploredCells", Private).GetValue(faction);
            Array.Fill(cells, true);
            factions.SetValue(faction, index);
        }

        private static void CompleteLineBuildings(Battle simulation, int lineIndex)
        {
            var world = World(simulation);
            var lines = (Array)world.GetType().GetField("ProcessingLines", Private).GetValue(world);
            var line = lines.GetValue(lineIndex);
            var buildings = (Array)world.GetType().GetField("Buildings", Private).GetValue(world);
            foreach (string name in new[] { "MineId", "SmelterId" })
            {
                uint id = (uint)line.GetType().GetField(name, Private).GetValue(line);
                if (id == 0 || id > buildings.Length) continue;
                var building = buildings.GetValue((int)id - 1);
                building.GetType().GetField("Complete", Private).SetValue(building, true);
                buildings.SetValue(building, (int)id - 1);
            }
        }

        private static int ArmsFlowTarget(Battle simulation, object snapshot, byte kind)
        {
            var method = typeof(Battle).GetMethod("ArmsFlowTarget", Private);
            var kindType = method.GetParameters()[1].ParameterType;
            return (int)method.Invoke(simulation, new[] { (object)1u, Enum.ToObject(kindType, kind), snapshot });
        }

        private static void SetEconomy(Battle simulation, int index, string name, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Economies", Private);
            var economies = (Array)field.GetValue(world);
            var economy = economies.GetValue(index);
            var member = economy.GetType().GetField(name, Private);
            member.SetValue(economy, Convert.ChangeType(value, member.FieldType));
            economies.SetValue(economy, index);
        }

        private static object Invoke(Battle simulation, string name, params object[] args)
            => typeof(Battle).GetMethod(name, Private).Invoke(simulation, args);
    }
}
