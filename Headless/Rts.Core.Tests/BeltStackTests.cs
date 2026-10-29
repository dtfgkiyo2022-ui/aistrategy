using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>38.15: bounded same-kind belt stacks and their opt-in serialization.</summary>
    public sealed class BeltStackTests
    {
        [Test]
        public void Extension79RoundTripsAndIsAbsentWhenDisabled()
        {
            var enabled = MapGenerator.GenerateTerrain(3815, false, true);
            enabled.Economy.Industry = true;
            enabled.Economy.BeltStack = true;
            enabled.Economy.BeltStackSize = 7;
            enabled.Economy.BeltStackAiMaxLinesAge0 = 2;
            enabled.Economy.BeltStackAiMaxLinesAge1 = 5;
            enabled.Economy.BeltStackAiMaxLinesAge2 = 8;
            enabled.Economy.BeltStackAiMaxLinesAge3 = 11;
            enabled.Economy.BeltStackAiMaxRouteCells = 47;
            var bytes = ScenarioBinary.Encode(enabled);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.BeltStack, Is.True);
            Assert.That(decoded.Economy.BeltStackSize, Is.EqualTo(7));
            Assert.That(decoded.Economy.BeltStackAiMaxLinesAge0, Is.EqualTo(2));
            Assert.That(decoded.Economy.BeltStackAiMaxLinesAge3, Is.EqualTo(11));
            Assert.That(decoded.Economy.BeltStackAiMaxRouteCells, Is.EqualTo(47));
            Assert.That(decoded.Extensions.Any(value => value.Id == 79 && value.Version == 1), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            enabled.Economy.BeltStack = false;
            var off = ScenarioBinary.Encode(enabled);
            var offDecoded = ScenarioBinary.Decode(off);
            Assert.That(offDecoded.Extensions.Any(value => value.Id == 79), Is.False);
        }

        [Test]
        public void FlagOffKeepsCanonicalHashesAndDiagnosticShapeStable()
        {
            var leftScenario = MapGenerator.GenerateTerrain(3816, false, true);
            leftScenario.Economy.Industry = true;
            leftScenario.Economy.BeltStack = false;
            var rightScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(leftScenario));
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            for (long tick = 1; tick <= 400; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
            Assert.That(left.CaptureDiagnostic().CanonicalState.ToArray(), Is.EqualTo(right.CaptureDiagnostic().CanonicalState.ToArray()));
            Assert.That(DiagnosticComparison.Fields(left.CaptureDiagnostic()).Any(value => value.Key.EndsWith(".StackCount", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void AStackedLineUsesTheConfiguredCapacityAndConservesItems()
        {
            var scenario = MapGenerator.Generate(3817, true, true);
            scenario.Economy.BeltStack = true;
            scenario.Economy.BeltStackSize = 4;
            scenario.Economy.BeltTicksPerCell = 1;
            var core = CellOf(scenario, scenario.Cores[0].Position);
            int width = scenario.Map.WidthCells;
            var cells = new[] { core - 4, core - 3, core - 2, core - 1 };
            Assume.That(cells.All(cell => cell >= 0 && cell < scenario.Map.WidthCells * scenario.Map.HeightCells));
            scenario.Belts = cells.Select((cell, index) => new BeltDefinition
            {
                Cell = cell, FactionId = 1, Facing = Facing.East, Item = index < 3 ? ResourceKind.Ore : 0
            }).ToArray();
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 2000; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var fields = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(value => value.Key, value => value.Value);
                long onBelts = fields.Where(value => value.Key.StartsWith("Belts[", StringComparison.Ordinal)
                        && value.Key.EndsWith("].StackCount", StringComparison.Ordinal))
                    .Sum(value => long.Parse(value.Value, CultureInfo.InvariantCulture));
                long stock = long.Parse(fields["Economy[1].Ore"], CultureInfo.InvariantCulture);
                Assert.That(onBelts + stock, Is.EqualTo(3), "tick " + tick);
                foreach (var value in fields.Where(value => value.Key.EndsWith(".StackCount", StringComparison.Ordinal)))
                    Assert.That(int.Parse(value.Value, CultureInfo.InvariantCulture), Is.InRange(0, 4));
            }
        }

        [Test]
        public void StackSizeIsBoundedByWorldRules()
        {
            var scenario = MapGenerator.GenerateTerrain(3819, false, true);
            scenario.Economy.BeltStack = true;
            scenario.Economy.BeltStackSize = 17;
            Assert.Throws<ArgumentException>(() => new Battle(scenario));
        }

        [Test]
        public void StackEnabledWorldCanAuthoredlyExceedTheLegacyTwoHundredCellLimit()
        {
            var scenario = MapGenerator.GenerateTerrain(3820, false, true);
            scenario.Economy.BeltStack = true;
            var nodes = new HashSet<int>(scenario.ResourceNodes.Select(value => CellOf(scenario, value.Position)));
            var blocked = new HashSet<int>(scenario.Map.BlockedCellIds);
            var cells = Enumerable.Range(0, scenario.Map.WidthCells * scenario.Map.HeightCells)
                .Where(cell => !blocked.Contains(cell) && !nodes.Contains(cell)).Take(201).ToArray();
            Assume.That(cells.Length, Is.EqualTo(201));
            scenario.Belts = cells.Select(cell => new BeltDefinition { Cell = cell, FactionId = 1, Facing = Facing.East }).ToArray();
            Assert.DoesNotThrow(() => new Battle(scenario));

            scenario.Economy.BeltStack = false;
            Assert.Throws<ArgumentException>(() => new Battle(scenario));
        }

        [Test]
        public void BeltViewDefaultCountPreservesTheExistingPresentationContract()
        {
            var oldView = new BeltView(1, 1, Facing.East, ResourceKind.Ore, 0);
            var stackView = new BeltView(1, 1, Facing.East, ResourceKind.Ore, 0, false,
                BeltComponentKind.None, (ResourceKind)0, -1, BeltSpeed.Normal, 4);
            Assert.That(oldView.Count, Is.EqualTo(1));
            Assert.That(stackView.Count, Is.EqualTo(4));
        }

        private static int CellOf(ScenarioDefinition scenario, SimPoint point)
            => (int)(point.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                + (int)(point.X.Raw / 65536 / scenario.Map.CellSizeMeters);
    }
}
