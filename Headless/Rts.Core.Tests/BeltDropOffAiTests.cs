using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BeltDropOffAiTests
    {
        [Test]
        public void Extension76Version2RoundTripsAiRulesAndReadsVersion1()
        {
            var scenario = LiveGameRules.Create(1UL, true, true, false, false);
            var economy = scenario.Economy;
            economy.BeltDropOffAi = true;
            economy.BeltDropOffAiMinGatherers = 7;
            economy.BeltDropOffAiMaxRouteCells = 31;
            economy.BeltDropOffAiMaxLinesAge0 = 3;
            economy.BeltDropOffAiMaxLinesAge1 = 5;
            economy.BeltDropOffAiMaxLinesAge2 = 7;
            economy.BeltDropOffAiMaxLinesAge3 = 9;
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 76);
            Assert.That(extension.Version, Is.EqualTo(2));
            Assert.That(decoded.Economy.BeltDropOffAi, Is.True);
            Assert.That(decoded.Economy.BeltDropOffAiMinGatherers, Is.EqualTo(7));
            Assert.That(decoded.Economy.BeltDropOffAiMaxRouteCells, Is.EqualTo(31));
            Assert.That(decoded.Economy.BeltDropOffAiMaxLinesAge0, Is.EqualTo(3));
            Assert.That(decoded.Economy.BeltDropOffAiMaxLinesAge1, Is.EqualTo(5));
            Assert.That(decoded.Economy.BeltDropOffAiMaxLinesAge2, Is.EqualTo(7));
            Assert.That(decoded.Economy.BeltDropOffAiMaxLinesAge3, Is.EqualTo(9));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            byte[] oldBytes = DowngradeExtension76ToVersion1(bytes);
            var old = ScenarioBinary.Decode(oldBytes);
            Assert.That(old.Economy.BeltDropOff, Is.True);
            Assert.That(old.Economy.BeltDropOffAi, Is.False);
            Assert.That(old.Economy.BeltDropOffRangeCells, Is.EqualTo(economy.BeltDropOffRangeCells));
            Assert.That(old.Economy.BeltDropOffWaitTicks, Is.EqualTo(economy.BeltDropOffWaitTicks));
            // Without the AI the first version is written back, so older scenarios keep their bytes.
            Assert.That(old.Extensions.Single(value => value.Id == 76).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(old), Is.EqualTo(oldBytes));
        }

        [Test]
        public void LiveRulesEnableAutomaticBeltLinesOnlyOnTheLargeProfile()
        {
            var large = LiveGameRules.Create(1UL, true, true, false, false);
            var small = LiveGameRules.Create(1UL, false, true, false, false);
            Assert.That(large.Economy.BeltDropOff, Is.True);
            Assert.That(large.Economy.BeltDropOffAi, Is.True);
            Assert.That(small.Economy.BeltDropOff, Is.False);
            Assert.That(small.Economy.BeltDropOffAi, Is.False);
        }

        [Test]
        public void FlagOffKeepsTickHashesEqual()
        {
            var left = LiveGameRules.Create(1UL, true, true, false, false);
            var right = LiveGameRules.Create(1UL, true, true, false, false);
            left.Economy.BeltDropOffAi = false;
            right.Economy.BeltDropOffAi = false;
            var a = new Battle(left);
            var b = new Battle(right);
            for (long tick = 1; tick <= 600; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>());
                b.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
        }

        [Test]
        public void AutomaticEconomyCanLayARawClusterLine()
        {
            var scenario = LiveGameRules.Create(1UL, true, true, false, false);
            scenario.Economy.BeltDropOffAiMinGatherers = 1;
            scenario.Economy.BeltDropOffAiMaxRouteCells = 200;
            scenario.Economy.BeltDropOffAiMaxLinesAge0 = 100;
            scenario.Economy.BeltDropOffAiMaxLinesAge1 = 100;
            scenario.Economy.BeltDropOffAiMaxLinesAge2 = 100;
            scenario.Economy.BeltDropOffAiMaxLinesAge3 = 100;
            scenario.Economy.AiBeltWoodReserve = 0;
            scenario.Economy.AiFactory = false;
            scenario.Economy.AiBelts = false;
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 12000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            var observation = simulation.CaptureLargeEconomyObservation();
            Assert.That(observation.Factions[0].BeltDropOffAiLines + observation.Factions[1].BeltDropOffAiLines,
                Is.GreaterThan(0));
        }

        private static byte[] DowngradeExtension76ToVersion1(byte[] bytes)
        {
            const int id = 76, currentVersion = 2, currentDataLength = 10 * sizeof(int), oldDataLength = 3 * sizeof(int);
            int record = -1, section = -1;
            for (int i = 0; i + 12 <= bytes.Length; i += 1)
            {
                if (ReadInt(bytes, i) == id && ReadInt(bytes, i + 4) == currentVersion && ReadInt(bytes, i + 8) == currentDataLength)
                { record = i; break; }
            }
            Assert.That(record, Is.GreaterThanOrEqualTo(0), "version 2 extension record");
            for (int i = 0; i + 12 <= record; i++)
            {
                int length = ReadInt(bytes, i + 8);
                if (length >= 0 && i + 12 + length == bytes.Length && ReadInt(bytes, i + 4) == 1)
                { section = i; break; }
            }
            Assert.That(section, Is.GreaterThanOrEqualTo(0), "extension section");
            int removed = currentDataLength - oldDataLength;
            var result = new byte[bytes.Length - removed];
            Buffer.BlockCopy(bytes, 0, result, 0, record + 12 + oldDataLength);
            Buffer.BlockCopy(bytes, record + 12 + currentDataLength, result, record + 12 + oldDataLength,
                bytes.Length - (record + 12 + currentDataLength));
            WriteInt(result, record + 4, 1);
            WriteInt(result, record + 8, oldDataLength);
            WriteInt(result, section + 8, ReadInt(bytes, section + 8) - removed);
            return result;
        }

        private static int ReadInt(byte[] bytes, int offset)
            => BitConverter.ToInt32(bytes, offset);

        private static void WriteInt(byte[] bytes, int offset, int value)
            => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, sizeof(int));
    }
}
