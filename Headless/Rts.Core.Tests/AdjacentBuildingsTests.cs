using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Rts.Presentation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AdjacentBuildingsTests
    {
        [Test]
        public void AdjacentBuildingsExtensionRoundTripsAndDisabledModeOmitsIt()
        {
            var baseline = MapGenerator.Generate(1, true);
            var disabledBaseline = MapGenerator.Generate(1, true);
            disabledBaseline.Economy.AdjacentBuildings = false;
            Assert.That(ScenarioBinary.Encode(disabledBaseline), Is.EqualTo(ScenarioBinary.Encode(baseline)));

            var enabled = Scenario(true);
            enabled.Economy.AdjacentBuildingClearanceCells = 1;
            var enabledBytes = ScenarioBinary.Encode(enabled);
            var enabledDecoded = ScenarioBinary.Decode(enabledBytes);

            Assert.That(enabledDecoded.Economy.AdjacentBuildings, Is.True);
            Assert.That(enabledDecoded.Economy.AdjacentBuildingClearanceCells, Is.EqualTo(1));
            Assert.That(enabledDecoded.Extensions.Any(x => x.Id == 37), Is.True);
            Assert.That(ScenarioBinary.Encode(enabledDecoded), Is.EqualTo(enabledBytes));

            var disabled = Scenario(false);
            var disabledDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(disabled));
            Assert.That(disabledDecoded.Economy.AdjacentBuildings, Is.False);
            Assert.That(disabledDecoded.Extensions.Any(x => x.Id == 37), Is.False);
        }

        [Test]
        public void ExistingIndustryOutputCellCannotBeCoveredByManualPlacement()
        {
            const int width = 12;
            const int height = 12;
            var passable = new bool[width * height];
            for (int i = 0; i < passable.Length; i++) passable[i] = true;
            var mill = new BuildingView(1, 1, BuildingKind.Mill,
                new SimPoint(Fix64.FromInt(12), Fix64.FromInt(12)), 4, 1, 1,
                true, 1, 1, 0, 0, Facing.North, 0, 0);
            var economy = new EconomyView(100, 100, true, 1, 4, 2, width, height, 2,
                new[] { mill }, Array.Empty<ResourceView>(), Array.Empty<BeltView>(), passable, new bool[passable.Length]);

            Assert.That(AdjacentBuildingPlacementRules.CanPlace(economy, Array.Empty<KnownObjective>(), 1,
                BuildingKind.Barracks, 7 * width + 6, 2, Facing.North, 1, 0), Is.False);
        }

        [Test]
        public void AutomaticPlacementAndHashAreUnchangedByManualOnlyFlag()
        {
            var off = new Battle(Scenario(false));
            var on = new Battle(Scenario(true));
            for (long tick = 1; tick <= 120; tick++)
            {
                off.Step(tick, Array.Empty<ScheduledInput>());
                on.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(off.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.Not.EqualTo(ReplayBinary.Hash(on.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
                var offFields = Fields(off);
                var onFields = Fields(on);
                foreach (var key in offFields.Keys.Where(x => !x.StartsWith("Config.", StringComparison.Ordinal)
                    && !x.Contains("AdjacentBuildings") && !x.Contains("AdjacentBuildingClearanceCells")))
                    Assert.That(onFields[key], Is.EqualTo(offFields[key]), key + " tick " + tick);
            }
        }

        [Test]
        public void DisabledModeKeepsBaselineHashForEveryTick()
        {
            var left = MapGenerator.Generate(17, true);
            var right = ScenarioBinary.Decode(ScenarioBinary.Encode(left));
            Assert.That(right.Economy.AdjacentBuildings, Is.False);
            var leftSimulation = new Battle(left);
            var rightSimulation = new Battle(right);
            for (long tick = 1; tick <= 30000 && !leftSimulation.Capture(1).Result.HasEnded; tick++)
            {
                leftSimulation.Step(tick, Array.Empty<ScheduledInput>());
                rightSimulation.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(leftSimulation.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(rightSimulation.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
        }

        [Test]
        public void ManualPlacementAcceptsOnlyTheNewAdjacentClearance()
        {
            var on = ManualFixture(true);
            int site = FindAndPlace(on);
            int adjacent = FindAdjacentAndPlace(on, site, 3, expectSuccess: true);
            Assert.That(adjacent, Is.GreaterThanOrEqualTo(0));
            int before = BuildingCount(on.Simulation);
            Assert.That(before, Is.GreaterThan(0));

            var off = ManualFixture(false);
            Assert.That(PlaceAndCount(off, site), Is.True);
            before = BuildingCount(off.Simulation);
            Assert.That(PlaceAndCount(off, adjacent), Is.False);
            Assert.That(BuildingCount(off.Simulation), Is.EqualTo(before));
        }

        private sealed class ManualState
        {
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal ManualState(ScenarioDefinition scenario)
            {
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++Sequence, false));
                Gateway.SubmitEconomy(EconomyCommand.Auto(2, ++Sequence, false));
                Gateway.Step();
            }
        }

        private static ScenarioDefinition Scenario(bool adjacent)
        {
            var scenario = MapGenerator.Generate(1, true);
            scenario.Economy.AdjacentBuildings = adjacent;
            scenario.Economy.AdjacentBuildingClearanceCells = 1;
            scenario.Economy.StartWood = 10000;
            scenario.Economy.BarracksWoodCost = 1;
            scenario.Economy.BarracksWork = 1;
            return scenario;
        }

        private static ManualState ManualFixture(bool adjacent)
            => new ManualState(Scenario(adjacent));

        private static int FindAndPlace(ManualState state)
        {
            int width = 128, height = 64;
            for (int cell = 0; cell < width * height; cell++)
                if (PlaceAndCount(state, cell)) return cell;
            Assert.Fail("手動配置できる検証地点が見つかりませんでした。");
            return -1;
        }

        private static int FindAdjacentAndPlace(ManualState state, int origin, int size, bool expectSuccess)
        {
            int width = 128, height = 64, x = origin % width, z = origin / width;
            var candidates = new List<int>();
            if (x + size < width) candidates.Add(origin + size);
            if (x >= size) candidates.Add(origin - size);
            if (z + size < height) candidates.Add(origin + size * width);
            if (z >= size) candidates.Add(origin - size * width);
            foreach (int cell in candidates)
                if (PlaceAndCount(state, cell) == expectSuccess) return cell;
            return -1;
        }

        private static bool PlaceAndCount(ManualState state, int cell)
        {
            int before = BuildingCount(state.Simulation);
            state.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.Sequence,
                BuildingKind.Barracks, cell, Facing.North));
            state.Gateway.Step();
            return BuildingCount(state.Simulation) == before + 1;
        }

        private static int BuildingCount(Battle simulation)
            => int.Parse(Fields(simulation)["Buildings.Count"], CultureInfo.InvariantCulture);

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(x => x.Key, x => x.Value);

    }
}
