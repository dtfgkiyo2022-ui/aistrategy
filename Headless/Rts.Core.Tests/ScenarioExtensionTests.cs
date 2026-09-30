using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ScenarioExtensionTests
    {
        [Test]
        public void ExistingTailCombinationsRemainExtensionFreeAndByteStable()
        {
            for (int mask = 0; mask < 64; mask++)
            {
                var scenario = MapGenerator.GenerateTerrain(1);
                var e = scenario.Economy;
                e.Forestry = (mask & 1) != 0;
                e.Masonry = (mask & 2) != 0;
                e.Caravan = (mask & 4) != 0;
                e.Cavalry = (mask & 8) != 0;
                e.ProcessingChain = (mask & 16) != 0;
                e.Bridge = (mask & 32) != 0;
                scenario.Extensions = Array.Empty<ScenarioExtensionData>();
                var bytes = ScenarioBinary.Encode(scenario);
                Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(bytes)), Is.EqualTo(bytes), "mask " + mask);
                Assert.That(FindExtension(bytes), Is.EqualTo(-1), "mask " + mask);
            }
        }

        [Test]
        public void TestExtensionRoundTripsAndIsWrittenAfterExistingTails()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Extensions = new[] { TestExtension(123456789) };
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Length, Is.EqualTo(1));
            Assert.That(decoded.Extensions[0].Id, Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(decoded.Extensions[0].Data, 0), Is.EqualTo(123456789));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        /// <summary>
        /// The monk, save-floor and fishing levels are optional "bytes remain" levels. With only one of them set, the
        /// extension marker must not be read as the next level's value.
        /// </summary>
        [TestCase("monks")]
        [TestCase("floor")]
        [TestCase("fishing")]
        [TestCase("gold")]
        public void TestExtensionRoundTripsAfterEachPartialOptionalLevel(string level)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            var e = scenario.Economy;
            if (level == "monks") e.MonksEnabled = true;
            else if (level == "floor") e.Age2SaveArmyFloor = 7;
            else if (level == "fishing") e.FishingEnabled = true;
            else e.GoldEnabled = true;
            scenario.Extensions = new[] { TestExtension(4242) };
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Length, Is.EqualTo(1), level);
            Assert.That(BitConverter.ToInt32(decoded.Extensions[0].Data, 0), Is.EqualTo(4242), level);
            Assert.That((decoded.Economy.MonksEnabled, decoded.Economy.Age2SaveArmyFloor, decoded.Economy.FishingEnabled, decoded.Economy.GoldEnabled),
                Is.EqualTo((e.MonksEnabled, e.Age2SaveArmyFloor, e.FishingEnabled, e.GoldEnabled)), level);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes), level);
        }

        [Test]
        public void TestExtensionRoundTripsWithRepresentativeTailCombinations()
        {
            foreach (int mask in new[] { 0, 3, 8, 16, 32, 63 })
            {
                var scenario = MapGenerator.GenerateTerrain(1);
                var e = scenario.Economy;
                e.Forestry = (mask & 1) != 0;
                e.Masonry = (mask & 2) != 0;
                e.Caravan = (mask & 4) != 0;
                e.Cavalry = (mask & 8) != 0;
                e.ProcessingChain = (mask & 16) != 0;
                e.Bridge = (mask & 32) != 0;
                scenario.Extensions = new[] { TestExtension(mask) };
                var bytes = ScenarioBinary.Encode(scenario);
                Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(bytes)), Is.EqualTo(bytes), "mask " + mask);
            }
        }

        [Test]
        public void InvalidExtensionRecordsAreRejected()
        {
            var valid = ScenarioBinary.Encode(WithTestExtension(7));
            int offset = FindExtension(valid);
            int bodyLength = BitConverter.ToInt32(valid, offset + 8);
            var body = valid.Skip(offset + 12).Take(bodyLength).ToArray();

            var duplicate = body.Concat(body).ToArray();
            Assert.Throws<InvalidDataException>(() => ScenarioBinary.Decode(WithPayload(valid, offset, duplicate)));

            var unknown = (byte[])valid.Clone();
            WriteInt32(unknown, offset + 12, 999);
            Assert.Throws<InvalidDataException>(() => ScenarioBinary.Decode(unknown));

            var tooLong = (byte[])valid.Clone();
            WriteInt32(tooLong, offset + 12 + 8, bodyLength + 1);
            Assert.Throws<InvalidDataException>(() => ScenarioBinary.Decode(tooLong));

            var truncated = valid.Take(valid.Length - 1).ToArray();
            Assert.Throws<InvalidDataException>(() => ScenarioBinary.Decode(truncated));

            var sectionMismatch = new byte[valid.Length + 1];
            Buffer.BlockCopy(valid, 0, sectionMismatch, 0, valid.Length);
            sectionMismatch[sectionMismatch.Length - 1] = 0x7f;
            Assert.Throws<InvalidDataException>(() => ScenarioBinary.Decode(sectionMismatch));
        }

        [Test]
        public void CommonRouteDistanceMatchesSharedRouteWithoutChangingMapState()
        {
            var map = new MapDefinition { WidthMeters = 5, HeightMeters = 3, CellSizeMeters = 1,
                WidthCells = 5, HeightCells = 3, DefaultPassable = true, BlockedCellIds = new[] { 6 } };
            var grid = new GridMap(map);
            var basePassable = new bool[15];
            var known = new bool[15];
            for (int i = 0; i < 15; i++) { basePassable[i] = grid.IsPassable(i); known[i] = true; }
            int before = grid.SharedRoute(0, grid.Center(8)).Length - 1;
            int measuredBefore = RouteDistance.Measure(5, 3, basePassable, known, 0, 8, null);
            Assert.That(measuredBefore, Is.EqualTo(before));

            var opened = new[] { 6 };
            int measuredAfter = RouteDistance.Measure(5, 3, basePassable, known, 0, 8, opened);
            var openedMap = new MapDefinition { WidthMeters = 5, HeightMeters = 3, CellSizeMeters = 1,
                WidthCells = 5, HeightCells = 3, DefaultPassable = true, BlockedCellIds = Array.Empty<int>() };
            var openedGrid = new GridMap(openedMap);
            Assert.That(measuredAfter, Is.EqualTo(openedGrid.SharedRoute(0, openedGrid.Center(8)).Length - 1));
            Assert.That(grid.IsPassable(6), Is.False);
        }

        private static ScenarioDefinition WithTestExtension(int value)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Extensions = new[] { TestExtension(value) };
            return scenario;
        }

        private static ScenarioExtensionData TestExtension(int value)
            => new ScenarioExtensionData { Id = 1, Version = 1, Data = BitConverter.GetBytes(value) };

        private static int FindExtension(byte[] bytes)
        {
            byte[] marker = { 0x45, 0x58, 0x54, 0x4e };
            for (int i = bytes.Length - marker.Length; i >= 0; i--)
                if (marker.SequenceEqual(bytes.Skip(i).Take(marker.Length))) return i;
            return -1;
        }

        private static byte[] WithPayload(byte[] original, int offset, byte[] payload)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(original, 0, offset);
                writer.Write(0x4E545845);
                writer.Write(1);
                writer.Write(payload.Length);
                writer.Write(payload);
                return stream.ToArray();
            }
        }

        private static void WriteInt32(byte[] bytes, int offset, int value)
            => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, sizeof(int));
    }
}
