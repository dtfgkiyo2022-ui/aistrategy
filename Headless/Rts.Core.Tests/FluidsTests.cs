using System;
using System.Collections.Generic;
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
    [TestFixture]
    public sealed class FluidsTests
    {
        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(bool fluids)
            {
                Scenario = LiveGameRules.Create(65001UL, false, true, false, false);
                Scenario.Economy.Industry = true; Scenario.Economy.ProcessingChain = true; Scenario.Economy.CarriedInputs = false;
                Scenario.Economy.Fluids = fluids;
                Scenario.Economy.FluidPumpTicksPerWater = 1; Scenario.Economy.FluidWellTicksPerWater = 1;
                Scenario.Economy.FluidBreweryFoodInput = 1; Scenario.Economy.FluidBreweryWaterInput = 1; Scenario.Economy.FluidBreweryTicks = 3;
                Scenario.Economy.SmeltTicks = 100;
                Simulation = new Battle(Scenario); Gateway = new CommandGateway(Simulation);
                var initial = State(Simulation); initial.Economies[0].Food = 10000; initial.Economies[0].Wood = 100000; initial.Economies[0].Stone = 100000;
                Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++Sequence, false)); Gateway.SubmitEconomy(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Step(int ticks)
            { for (int i = 0; i < ticks; i++) Gateway.Step(); }

            internal int Place(BuildingKind kind, int age = 0)
            {
                var state = State(Simulation); state.Economies[0].Age = Math.Max(state.Economies[0].Age, (byte)age);
                int before = state.BuildingCount, cells = Scenario.Map.WidthCells * Scenario.Map.HeightCells;
                for (int cell = 0; cell < cells; cell++)
                {
                    Gateway.SubmitEconomy(EconomyCommand.Place(1, ++Sequence, kind, cell)); Step(1);
                    if (state.BuildingCount > before && state.Buildings[before].Kind == kind)
                    {
                        state.Buildings[before].Complete = true; state.FluidsDirty = Scenario.Economy.Fluids; Step(1); return before;
                    }
                }
                Assert.Fail("placement failed: " + kind); return -1;
            }

            internal int[] PlacePipes(IEnumerable<int> cells)
            {
                var state = State(Simulation); int before = state.BuildingCount;
                Gateway.SubmitEconomy(EconomyCommand.PlacePipe(1, ++Sequence, cells.ToArray())); Step(1);
                for (int i = before; i < state.BuildingCount; i++) state.Buildings[i].Complete = true;
                state.FluidsDirty = true; Step(1);
                return Enumerable.Range(before, state.BuildingCount - before).ToArray();
            }
        }

        private static int[] PassableCells(Fixture f, int count = 1)
        {
            var s = State(f.Simulation); var cells = new List<int>();
            int width = f.Scenario.Map.WidthCells, height = f.Scenario.Map.HeightCells;
            for (int i = 0; i < width * height; i++)
            {
                if (i % width + count > width || i / width + count > height) continue;
                bool valid = true;
                for (int offset = 0; offset < count; offset++)
                {
                    int cell = i + offset;
                    if (!s.Map.IsPassable(cell) || f.Scenario.Map.Terrain[cell] == (byte)TerrainKind.River) { valid = false; break; }
                }
                if (valid) { for (int offset = 0; offset < count; offset++) cells.Add(i + offset); break; }
            }
            return cells.ToArray();
        }

        [Test]
        public void ContractValuesAndFilterCommandAreStable()
        {
            Assert.That((byte)FluidKind.Oil, Is.EqualTo(2)); Assert.That((byte)BuildingKind.Brewery, Is.EqualTo(59));
            var f = new Fixture(true); var state = State(f.Simulation); int before = state.BuildingCount;
            f.Gateway.SubmitEconomy(EconomyCommand.PlacePipe(1, ++f.Sequence, new[] { f.Scenario.Map.BlockedCellIds[0] })); f.Step(1);
            Assert.That(state.BuildingCount, Is.EqualTo(before));
        }

        [Test]
        public void WellAndBreweryProduceAleOnlyWhenConnectedToWater()
        {
            var f = new Fixture(true); var state = State(f.Simulation); int well = f.Place(BuildingKind.Well); state.Economies[0].Age = 2; int brewery = f.Place(BuildingKind.Brewery);
            var cells = PassableCells(f, 8); f.PlacePipes(cells);
            var network = state.FluidNetworks.FirstOrDefault(n => n.FactionId == 1); network.Kind = FluidKind.Water; network.Amount = 20;
            for (int i = 0; i < state.FluidNetworks.Length; i++) if (state.FluidNetworks[i].FactionId == 1) state.FluidNetworks[i] = network;
            state.Buildings[well].FluidNetworkId = network.Number; state.Buildings[brewery].FluidNetworkId = network.Number;
            int before = state.Economies[0].AleProduced; f.Step(10);
            Assert.That(state.Economies[0].AleProduced, Is.GreaterThan(before));
        }

        [Test]
        public void SplitAndJoinConserveNetworkAmount()
        {
            var f = new Fixture(true); var cells = PassableCells(f, 5); f.PlacePipes(cells);
            var state = State(f.Simulation); Assert.That(state.FluidNetworks.Length, Is.EqualTo(1));
            state.FluidNetworks[0].Kind = FluidKind.Water; state.FluidNetworks[0].Amount = 65;
            f.Gateway.SubmitEconomy(EconomyCommand.RemovePipe(1, ++f.Sequence, cells[2])); f.Step(1);
            Assert.That(state.FluidNetworks.Sum(n => n.Amount), Is.EqualTo(65));
        }

        [Test]
        public void OilNetworkDoesNotAcceptWater()
        {
            var f = new Fixture(true); var cells = PassableCells(f, 3); f.PlacePipes(cells);
            var state = State(f.Simulation); state.FluidNetworks[0].Kind = FluidKind.Oil; state.FluidNetworks[0].Amount = 7; f.Step(3);
            Assert.That(state.FluidNetworks[0].Amount, Is.EqualTo(7)); Assert.That(state.FluidNetworks[0].Kind, Is.EqualTo(FluidKind.Oil));
        }

        [Test]
        public void WaterCoolingShortensSmelterAndNoWaterKeepsTimer()
        {
            var wet = new Fixture(true); var ws = State(wet.Simulation); ws.Economies[0].Civ = CivKind.Metallurgy; int smelter = wet.Place(BuildingKind.Smelter, 2); ws.Buildings[smelter].Input = ws.Config.Economy.OrePerMetal;
            ws.FluidNetworks = new[] { new FluidNetworkState { FactionId = 1, Number = 1, Kind = FluidKind.Water, Amount = 3, Capacity = 20, PipeCells = new[] { 1 } } };
            ws.Buildings[smelter].FluidNetworkId = 1; wet.Step(1); int wetTimer = ws.Buildings[smelter].Timer;
            var dry = new Fixture(true); var ds = State(dry.Simulation); ds.Economies[0].Civ = CivKind.Metallurgy; int drySmelter = dry.Place(BuildingKind.Smelter, 2); ds.Buildings[drySmelter].Input = ds.Config.Economy.OrePerMetal;
            ds.FluidNetworks = new[] { new FluidNetworkState { FactionId = 1, Number = 1, Kind = FluidKind.None, Amount = 0, Capacity = 20, PipeCells = new[] { 1 } } };
            ds.Buildings[drySmelter].FluidNetworkId = 1; dry.Step(1);
            Assert.That(wetTimer, Is.LessThan(ds.Buildings[drySmelter].Timer));
        }

        [Test]
        public void FlagOffCanonicalStateMatchesTheUnextendedScenario()
        {
            var a = new Fixture(false); var b = new Fixture(false);
            for (int i = 0; i < 20; i++) { a.Simulation.Step(i + 2, Array.Empty<ScheduledInput>()); b.Simulation.Step(i + 2, Array.Empty<ScheduledInput>()); }
            Assert.That(ReplayBinary.Hash(a.Simulation.CaptureDiagnostic().CanonicalState), Is.EqualTo(ReplayBinary.Hash(b.Simulation.CaptureDiagnostic().CanonicalState)));
            Assert.That(ScenarioBinary.Encode(a.Scenario), Is.EqualTo(ScenarioBinary.Encode(b.Scenario)));
        }
    }
}
