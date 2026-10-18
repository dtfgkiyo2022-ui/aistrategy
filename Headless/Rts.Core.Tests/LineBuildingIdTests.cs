using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class LineBuildingIdTests
    {
        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> fields, string name)
            => long.Parse(fields[name], CultureInfo.InvariantCulture);

        [Test]
        public void EveryAutomaticLineBuildingIdKeepsItsExpectedKindAfterRaidRebuild()
        {
            var simulation = new Battle(LiveGameRules.Create(1, false, true, false, false));
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, true));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, true));

            for (int step = 0; step < 30000 && !simulation.Capture(1).Result.HasEnded; step++)
            {
                gateway.Step();
                AssertLineBuildingKinds(Fields(simulation), simulation.Capture(1).Tick);
            }
        }

        [Test]
        public void LineBuildingCheckExtension54RoundTripsAndOffOmitsIt()
        {
            var on = LiveGameRules.Create(1, false, true, false, false);
            byte[] onBytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(onBytes);
            Assert.That(decoded.Economy.LineBuildingCheck, Is.True);
            Assert.That(decoded.Extensions.Any(x => x.Id == 54), Is.True);
            Assert.That(decoded.Extensions.Single(x => x.Id == 54).Version, Is.EqualTo(1));
            Assert.That(decoded.Extensions.Single(x => x.Id == 54).Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(onBytes));

            var off = LiveGameRules.Create(1, false, true, false, false);
            off.Economy.LineBuildingCheck = false;
            byte[] offBytes = ScenarioBinary.Encode(off);
            var offDecoded = ScenarioBinary.Decode(offBytes);
            Assert.That(offDecoded.Economy.LineBuildingCheck, Is.False);
            Assert.That(offDecoded.Extensions.Any(x => x.Id == 54), Is.False);
        }

        [Test]
        public void LineBuildingCheckOffKeepsStateHashesIdentical()
        {
            var leftScenario = LiveGameRules.Create(1, false, true, false, false);
            leftScenario.Economy.LineBuildingCheck = false;
            var rightScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(leftScenario));
            Assert.That(ScenarioBinary.Encode(rightScenario), Is.EqualTo(ScenarioBinary.Encode(leftScenario)));
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            for (long tick = 1; tick <= 6000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void LineBuildingCheckOnRecordsAndReplaysWithTheSameHashes()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            using (var stream = new System.IO.MemoryStream())
            {
                var result = ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 600, new BuildIdentity());
                Assert.That(result.IsFault, Is.False);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        private static void AssertLineBuildingKinds(Dictionary<string, string> fields, long tick)
        {
            if (!fields.ContainsKey("ProcessingLines.Count")) return;
            int count = checked((int)Number(fields, "ProcessingLines.Count"));
            for (int i = 0; i < count; i++)
            {
                string prefix = "ProcessingLines[" + i.ToString(CultureInfo.InvariantCulture) + "].";
                if (Number(fields, prefix + "Manager") != 0) continue;
                var kind = (ProcessingLineKind)Number(fields, prefix + "Kind");
                foreach (var required in RequiredBuildingIds(kind, prefix, fields))
                {
                    if (required.id == 0) continue;
                    string building = "Buildings[" + required.id.ToString(CultureInfo.InvariantCulture) + "].";
                    Assert.That(fields.ContainsKey(building + "Kind"), Is.True,
                        "tick=" + tick + ", line=" + kind + ", field=" + required.name + ", id=" + required.id);
                    Assert.That((BuildingKind)Number(fields, building + "Kind"), Is.EqualTo(required.kind),
                        "tick=" + tick + ", line=" + kind + ", field=" + required.name + ", id=" + required.id);
                }
            }
        }

        private static IEnumerable<(string name, uint id, BuildingKind kind)> RequiredBuildingIds(
            ProcessingLineKind line, string prefix, Dictionary<string, string> fields)
        {
            IEnumerable<(string, BuildingKind)> expected = line == ProcessingLineKind.CoreMetal
                ? new[] { ("MineId", BuildingKind.Mine), ("SmelterId", BuildingKind.Smelter) }
                : line == ProcessingLineKind.Steel
                ? new[] { ("MineId", BuildingKind.Mine), ("SmelterId", BuildingKind.Smelter), ("KilnId", BuildingKind.CharcoalKiln), ("SteelworksId", BuildingKind.Steelworks) }
                : line == ProcessingLineKind.CoreWood
                ? new[] { ("LumberCampId", BuildingKind.LumberCamp) }
                : line == ProcessingLineKind.BowGear
                ? new[] { ("LumberCampId", BuildingKind.LumberCamp), ("FletcherId", BuildingKind.Fletcher) }
                : line == ProcessingLineKind.Rations
                ? new[] { ("MillId", BuildingKind.Mill) }
                : line == ProcessingLineKind.Bricks
                ? new[] { ("QuarryId", BuildingKind.Quarry), ("KilnId", BuildingKind.CharcoalKiln), ("BrickKilnId", BuildingKind.BrickKiln) }
                : line == ProcessingLineKind.Tack
                ? new[] { ("SaddleryId", BuildingKind.Saddlery) }
                : line == ProcessingLineKind.Planks
                ? new[] { ("LumberCampId", BuildingKind.LumberCamp), ("SawmillId", BuildingKind.Sawmill) }
                : line == ProcessingLineKind.Books
                ? new[] { ("ScriptoriumId", BuildingKind.Scriptorium) }
                : line == ProcessingLineKind.Preserves
                ? new[] { ("DryingRackId", BuildingKind.DryingRack) }
                : line == ProcessingLineKind.Jewels
                ? new[] { ("LapidaryId", BuildingKind.Lapidary) }
                : line == ProcessingLineKind.TradeGoods
                ? new[] { ("WeaverId", BuildingKind.Weaver) }
                : line == ProcessingLineKind.Incense
                ? new[] { ("IncenseHouseId", BuildingKind.IncenseHouse) }
                : line == ProcessingLineKind.Charms
                ? new[] { ("CharmWorkshopId", BuildingKind.CharmWorkshop) }
                : line == ProcessingLineKind.RepairKits
                ? new[] { ("RepairYardId", BuildingKind.RepairYard) }
                : line == ProcessingLineKind.Tools
                ? new[] { ("ToolShopId", BuildingKind.ToolShop) }
                : line == ProcessingLineKind.CutStone
                ? new[] { ("StonecutterId", BuildingKind.Stonecutter) }
                : line == ProcessingLineKind.Frames
                ? new[] { ("JoineryId", BuildingKind.Joinery) }
                : line == ProcessingLineKind.Assembler
                ? new[] { ("AssemblerId", BuildingKind.Assembler), ("KilnId", BuildingKind.CharcoalKiln) }
                : Array.Empty<(string, BuildingKind)>();
            foreach (var item in expected)
            {
                string name = prefix + item.Item1;
                yield return (item.Item1, fields.ContainsKey(name) ? checked((uint)Number(fields, name)) : 0, item.Item2);
            }
        }
    }
}
