using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CarriedInputsTests
    {
        private const int Width = 128, Height = 64;

        [Test]
        public void CarriedInputsExtension88RoundTripsItsFlagAndOutletLimit()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            scenario.Economy.CarriedInputs = true;
            scenario.Economy.CarriedInputsOutletBuildingLimit = 3;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(e => e.Id == 88).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CarriedInputs, Is.True);
            Assert.That(decoded.Economy.CarriedInputsOutletBuildingLimit, Is.EqualTo(3));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void CarriedInputsOffPreservesEveryTickHash()
        {
            var left = LiveGameRules.Create(1, false, true, false, false);
            var right = LiveGameRules.Create(1, false, true, false, false);
            right.Economy.CarriedInputs = false;
            var a = new Battle(left);
            var b = new Battle(right);
            for (long tick = 1; tick <= 300; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>());
                b.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void CarriedInputsNeedsDeliveredWoodBeforeSawmillCanWork()
        {
            var withoutDelivery = StartSawmill(false);
            Step(withoutDelivery.gateway, withoutDelivery.sim, 120);
            Assert.That(withoutDelivery.sim.Capture(1).Economy.Buildings.Single(b => b.Id == withoutDelivery.id).Output,
                Is.EqualTo(0));

            var withDelivery = StartSawmill(true);
            Step(withDelivery.gateway, withDelivery.sim, 120);
            Assert.That(withDelivery.sim.Capture(1).Economy.Buildings.Single(b => b.Id == withDelivery.id).Output,
                Is.GreaterThan(0));
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, uint id) StartSawmill(bool delivery)
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            var e = scenario.Economy;
            e.CarriedInputs = true;
            e.CarriedInputsOutletBuildingLimit = 3;
            e.StartFood = 10000;
            e.StartWood = 10000;
            e.AutoVillagerTarget = 0;
            e.AiFactory = false;
            e.AiExpand = false;
            e.PrimitiveCraft = false;
            e.CraftTree1 = false;
            e.CraftTree2 = false;
            e.CraftTree3 = false;
            e.PlankTicks = 1;
            e.PlankWoodInput = 1;

            int core = CellOf(scenario.Cores[0].Position);
            int size = e.SawmillSizeCells;
            int origin = -1, inserter = -1;
            for (int x = core % Width + 5; x < core % Width + 30 && origin < 0; x++)
                for (int z = Math.Max(5, core / Width - 8); z < Math.Min(Height - size - 3, core / Width + 8); z++)
                {
                    int candidate = z * Width + x;
                    int candidateInserter = candidate - 2 + Width;
                    if (!FootprintOpen(scenario, candidate, size) || !Open(scenario, candidateInserter)
                        || !Open(scenario, candidateInserter + 1) || InsideCore(scenario, candidateInserter)
                        || !InsideCore(scenario, candidateInserter - 1))
                        continue;
                    origin = candidate;
                    inserter = candidateInserter;
                }
            Assume.That(origin, Is.GreaterThan(0), "a sawmill site");
            if (delivery)
            {
                scenario.Belts = new[]
                {
                    new BeltDefinition { Cell = inserter, FactionId = 1, Facing = Facing.East,
                        Component = BeltComponentKind.Inserter, SorterKind = ResourceKind.Wood },
                    new BeltDefinition { Cell = inserter + 1, FactionId = 1, Facing = Facing.East }
                };
            }

            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.Step();
            gateway.SubmitEconomy(EconomyCommand.AdvanceNow(1, ++sequence, CivKind.Bridge));
            gateway.Step();
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Sawmill, origin, Facing.East));
            gateway.Step();
            var building = sim.Capture(1).Economy.Buildings.Single(b => b.Kind == BuildingKind.Sawmill);
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { 1u, 2u, 3u },
                EconomyTargetKind.Building, building.Id));
            for (int i = 0; i < 1000 && !sim.Capture(1).Economy.Buildings.Single(b => b.Id == building.Id).Complete; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Buildings.Single(b => b.Id == building.Id).Complete, Is.True);
            return (scenario, sim, gateway, building.Id);
        }

        private static void Step(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static int CellOf(SimPoint point)
            => (int)(point.Z.Raw / 65536 / 2) * Width + (int)(point.X.Raw / 65536 / 2);

        private static bool Open(ScenarioDefinition scenario, int cell)
            => cell >= 0 && cell < Width * Height && !scenario.Map.BlockedCellIds.Contains(cell)
                && !scenario.ResourceNodes.Any(n => CellOf(n.Position) == cell);

        private static bool InsideCore(ScenarioDefinition scenario, int cell)
        {
            long x = (cell % Width) * 2 + 1, z = (cell / Width) * 2 + 1;
            return scenario.Cores.Any(core =>
            {
                long dx = x - core.Position.X.Raw / 65536, dz = z - core.Position.Z.Raw / 65536;
                return dx * dx + dz * dz <= 16;
            });
        }

        private static bool FootprintOpen(ScenarioDefinition scenario, int origin, int size)
        {
            for (int dz = 0; dz < size; dz++) for (int dx = 0; dx < size; dx++)
                if (!Open(scenario, origin + dz * Width + dx) || InsideCore(scenario, origin + dz * Width + dx)) return false;
            return true;
        }
    }
}
