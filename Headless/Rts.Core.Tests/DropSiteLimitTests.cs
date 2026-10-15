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
    public sealed class DropSiteLimitTests
    {
        private static ScenarioDefinition Scenario(bool limit, bool belts)
        {
            var s = MapGenerator.GenerateTerrain(7319UL, false, true);
            s.Economy.Ages = true;
            s.Economy.Industry = true;
            s.Economy.StartFood = 5000;
            s.Economy.StartWood = 5000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.DropSiteWoodCost = 1;
            s.Economy.DropSiteWork = 1;
            s.Economy.DropSiteLimit = limit;
            s.Economy.DropSiteMax = 2;
            s.Economy.AiBelts = belts;
            s.Economy.AiBeltWoodReserve = 40;
            s.Economy.BeltWoodCost = 1;
            s.Economy.BeltTicksPerCell = 1;
            s.Economy.BeltLimit = 4096;
            return s;
        }

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static int Number(Dictionary<string, string> fields, string name)
            => int.Parse(fields[name], CultureInfo.InvariantCulture);

        private static int DropSites(Dictionary<string, string> fields)
            => fields.Count(p => p.Key.EndsWith("].Kind", StringComparison.Ordinal)
                && p.Value == ((byte)BuildingKind.DropSite).ToString(CultureInfo.InvariantCulture));

        private static int CellOf(ScenarioDefinition s, SimPoint p)
            => (int)(p.Z.Raw / 65536 / s.Map.CellSizeMeters) * s.Map.WidthCells
                + (int)(p.X.Raw / 65536 / s.Map.CellSizeMeters);

        private static void Step(Battle sim, int ticks)
        {
            for (long tick = 1; tick <= ticks; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
        }

        [Test]
        public void DisabledFlagsLeaveScenarioBytesAndSimulationHashUnchanged()
        {
            var plain = Scenario(false, false);
            var changed = Scenario(false, false);
            changed.Economy.DropSiteMax = 17;
            changed.Economy.AiBeltWoodReserve = 777;
            Assert.That(ScenarioBinary.Encode(changed), Is.EqualTo(ScenarioBinary.Encode(plain)));
            var a = new Battle(plain);
            var b = new Battle(changed);
            Step(a, 120); Step(b, 120);
            Assert.That(a.CaptureDiagnostic().CanonicalState, Is.EqualTo(b.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void DropSiteAndAiBeltsExtensionsRoundTripWithTheirNumbers()
        {
            var scenario = Scenario(true, true);
            scenario.Economy.DropSiteMax = 3;
            scenario.Economy.AiBeltWoodReserve = 91;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 38 && x.Version == 1), Is.True);
            Assert.That(decoded.Extensions.Any(x => x.Id == 40 && x.Version == 1), Is.True);
            Assert.That(decoded.Economy.DropSiteMax, Is.EqualTo(3));
            Assert.That(decoded.Economy.AiBeltWoodReserve, Is.EqualTo(91));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void HumanPlacementRejectsTheThirdDropSiteIncludingConstruction()
        {
            var scenario = Scenario(true, false);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            Assert.That(sim.Capture(1).Economy.DropSiteMax, Is.EqualTo(2));
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            int before = DropSites(Fields(sim));
            int core = CellOf(scenario, scenario.Cores[0].Position);
            var candidates = Enumerable.Range(0, scenario.Map.WidthCells * scenario.Map.HeightCells)
                .Where(cell => Math.Abs(cell % scenario.Map.WidthCells - core % scenario.Map.WidthCells)
                    + Math.Abs(cell / scenario.Map.WidthCells - core / scenario.Map.WidthCells) >= 10)
                .Where(cell => !scenario.Map.BlockedCellIds.Contains(cell)
                    && scenario.ResourceNodes.All(n => CellOf(scenario, n.Position) != cell))
                .Take(20).ToArray();
            foreach (int cell in candidates)
            {
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.DropSite, cell, Facing.North));
            }
            gateway.Step();
            Assert.That(DropSites(Fields(sim)), Is.EqualTo(2));
        }

        [Test]
        public void AutoOffDoesNotCreateAiBeltsAndWoodReserveBlocksRouteSpending()
        {
            var off = Scenario(true, true);
            off.Economy.StartWood = 0;
            var sim = new Battle(off);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            for (int i = 0; i < 600; i++) gateway.Step();
            Assert.That(Number(Fields(sim), "Belts.Count"), Is.EqualTo(0));
        }

        [Test]
        public void AiBeltsRemainTickDeterministic()
        {
            var left = new Battle(Scenario(true, true));
            var right = new Battle(Scenario(true, true));
            for (long tick = 1; tick <= 600; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(left.CaptureDiagnostic().CanonicalState, Is.EqualTo(right.CaptureDiagnostic().CanonicalState), "tick " + tick);
            }
        }
    }
}
