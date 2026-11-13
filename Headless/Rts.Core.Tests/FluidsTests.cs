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
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);

        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(bool fluids = true)
            {
                Scenario = LiveGameRules.Create(65001UL, false, true, false, false);
                Scenario.Economy.Industry = true;
                Scenario.Economy.ProcessingChain = true;
                Scenario.Economy.CarriedInputs = false;
                Scenario.Economy.Fluids = fluids;
                Scenario.Economy.AdjacentBuildings = true;
                Scenario.Economy.FluidPumpTicksPerWater = 1;
                Scenario.Economy.FluidWellTicksPerWater = 1;
                Scenario.Economy.FluidBreweryFoodInput = 1;
                Scenario.Economy.FluidBreweryWaterInput = 1;
                Scenario.Economy.FluidBreweryTicks = 3;
                Scenario.Economy.SmeltTicks = 100;
                Simulation = new Battle(Scenario);
                Gateway = new CommandGateway(Simulation);
                var state = State(Simulation);
                state.Economies[0].Food = 10000;
                state.Economies[0].Wood = 100000;
                state.Economies[0].Stone = 100000;
                state.Economies[0].Ore = 100000;
                state.Economies[0].Age = 2;
                Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++Sequence, false));
                Gateway.SubmitEconomy(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Step(int ticks)
            { for (int i = 0; i < ticks; i++) Gateway.Step(); }

            internal int FindOpenCell()
            {
                var state = State(Simulation);
                for (int cell = 0; cell < state.Pipes.Length; cell++)
                    if (state.Map.IsPassable(cell) && Scenario.Map.Terrain[cell] != (byte)TerrainKind.River)
                        return cell;
                Assert.Fail("no open cell");
                return -1;
            }

            internal int PlacePipe()
            {
                var state = State(Simulation);
                for (int cell = 0; cell < state.Pipes.Length; cell++)
                {
                    if (!state.Map.IsPassable(cell) || Scenario.Map.Terrain[cell] == (byte)TerrainKind.River) continue;
                    Gateway.SubmitEconomy(EconomyCommand.PlacePipe(1, ++Sequence, new[] { cell }));
                    Step(1);
                    if (state.Pipes[cell].FactionId == 1) return cell;
                }
                Assert.Fail("pipe placement failed");
                return -1;
            }

            internal int PlaceBuilding(BuildingKind kind, int age = 2)
            {
                var state = State(Simulation);
                state.Economies[0].Age = (byte)Math.Max(state.Economies[0].Age, age);
                int before = state.BuildingCount;
                for (int cell = 0; cell < Scenario.Map.WidthCells * Scenario.Map.HeightCells; cell++)
                {
                    Gateway.SubmitEconomy(EconomyCommand.Place(1, ++Sequence, kind, cell));
                    Step(1);
                    for (int i = before; i < state.BuildingCount; i++)
                        if (state.Buildings[i].Alive && !state.Buildings[i].Reserved && state.Buildings[i].Kind == kind)
                        {
                            state.Buildings[i].Complete = true;
                            state.FluidsDirty = Scenario.Economy.Fluids;
                            Step(1);
                            return i;
                        }
                }
                Assert.Fail("placement failed: " + kind);
                return -1;
            }

            internal void AutomaticCommand(EconomyCommand command)
            {
                typeof(Battle).GetField("automaticEconomyAction", PrivateInstance).SetValue(Simulation, true);
                try { typeof(Battle).GetMethod("ApplyEconomyCommand", PrivateInstance).Invoke(Simulation, new object[] { command }); }
                finally { typeof(Battle).GetField("automaticEconomyAction", PrivateInstance).SetValue(Simulation, false); }
            }

            internal bool AutomaticPlace(BuildingKind kind)
            {
                var state = State(Simulation);
                int before = state.BuildingCount;
                for (int cell = 0; cell < state.Pipes.Length; cell++)
                {
                    AutomaticCommand(EconomyCommand.Place(1, ++Sequence, kind, cell));
                    if (state.BuildingCount > before && state.Buildings.Skip(before).Any(b => b.Alive && b.Kind == kind)) return true;
                }
                return false;
            }

            internal void AutomaticDecideFluids()
            {
                typeof(Battle).GetField("automaticEconomyAction", PrivateInstance).SetValue(Simulation, true);
                try { typeof(Battle).GetMethod("DecideFluids", PrivateInstance).Invoke(Simulation, new object[] { 1u }); }
                finally { typeof(Battle).GetField("automaticEconomyAction", PrivateInstance).SetValue(Simulation, false); }
            }
        }

        [Test]
        public void ContractValuesAndFilterCommandAreStable()
        {
            Assert.That((byte)FluidKind.Oil, Is.EqualTo(2));
            Assert.That((byte)BuildingKind.Brewery, Is.EqualTo(59));
            var f = new Fixture(true);
            int before = State(f.Simulation).Pipes.Count(p => p.FactionId != 0);
            f.Gateway.SubmitEconomy(EconomyCommand.PlacePipe(1, ++f.Sequence, new[] { f.Scenario.Map.BlockedCellIds[0] }));
            f.Step(1);
            Assert.That(State(f.Simulation).Pipes.Count(p => p.FactionId != 0), Is.EqualTo(before));
        }

        [Test]
        public void PipeIsCompleteImmediatelyAndRemainsWalkable()
        {
            var f = new Fixture();
            int cell = f.PlacePipe();
            var state = State(f.Simulation);
            Assert.That(state.Pipes[cell].Hp, Is.EqualTo(f.Scenario.Economy.FluidPipeHp));
            Assert.That(state.Map.IsPassable(cell), Is.True);
            Assert.That(state.Map.FindPath(state.Map.Cell(state.Villagers[0].Position), state.Map.Center(cell), 1).Length, Is.GreaterThan(0));
            Assert.That(state.Buildings.Any(b => b.Alive && b.Kind == BuildingKind.Pipe), Is.False);
        }

        [Test]
        public void BuildingCanTouchPipeOnAnEdge()
        {
            var f = new Fixture();
            var state = State(f.Simulation);
            int width = f.Scenario.Map.WidthCells;
            state.Economies[0].Civ = CivKind.Metallurgy;
            int smelter = f.PlaceBuilding(BuildingKind.Smelter, 2);
            int origin = state.Buildings[smelter].OriginCell, size = 1;
            foreach (int cell in new[] { origin - 1, origin + size, origin - width, origin + width })
            {
                if (cell < 0 || cell >= state.Pipes.Length || !state.Map.IsPassable(cell)
                    || f.Scenario.Map.Terrain[cell] == (byte)TerrainKind.River) continue;
                f.Gateway.SubmitEconomy(EconomyCommand.PlacePipe(1, ++f.Sequence, new[] { cell }));
                f.Step(1);
                if (state.Pipes[cell].FactionId == 1) return;
            }
            Assert.Fail("could not place an edge-adjacent pipe");
        }

        [Test]
        public void AutomaticFluidBuildingReservesWhenItCannotPay()
        {
            var f = new Fixture();
            var state = State(f.Simulation);
            state.Economies[0].Wood = 0;
            state.Economies[0].Stone = 0;
            Assert.That(f.AutomaticPlace(BuildingKind.Well), Is.True);
            Assert.That(state.Buildings.Any(b => b.Alive && b.Reserved && b.Kind == BuildingKind.Well), Is.True);
        }

        [Test]
        public void AutomaticFluidBuildingIsNotInNetworkBeforeConstruction()
        {
            var f = new Fixture();
            var state = State(f.Simulation);
            Assert.That(f.AutomaticPlace(BuildingKind.Well), Is.True);
            f.Step(1);
            var well = state.Buildings.First(b => b.Alive && b.Kind == BuildingKind.Well);
            Assert.That(well.Complete, Is.False);
            Assert.That(well.FluidNetworkId, Is.EqualTo(-1));
            Assert.That(state.Economies[0].WaterProduced, Is.EqualTo(0));
        }

        [Test]
        public void AutomaticDoesNotPlaceAnotherWaterSourceWhileOneIsUnderConstruction()
        {
            var f = new Fixture();
            var state = State(f.Simulation);
            Assert.That(f.AutomaticPlace(BuildingKind.Well), Is.True);
            int planned = state.Buildings.Count(b => b.Alive && b.FactionId == 1 && b.Kind == BuildingKind.Well);
            f.AutomaticDecideFluids();
            Assert.That(state.Buildings.Count(b => b.Alive && b.FactionId == 1 && b.Kind == BuildingKind.Well), Is.EqualTo(planned));
        }

        [Test]
        public void AutomaticDoesNotPlaceAWellWhenTheCoolingBuildingHasNoRoute()
        {
            var f = new Fixture();
            var state = State(f.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Civ = CivKind.Metallurgy;
            int smelter = f.PlaceBuilding(BuildingKind.Smelter, 1);
            int origin = state.Buildings[smelter].OriginCell, width = f.Scenario.Map.WidthCells;
            int[] neighbours = { origin - 1, origin + 1, origin - width, origin + width };
            foreach (int cell in neighbours) if (cell >= 0 && cell < state.Pipes.Length) state.Map.SetPassable(cell, false);
            int before = state.Buildings.Count(b => b.Alive && b.FactionId == 1 && b.Kind == BuildingKind.Well);
            var method = typeof(Battle).GetMethod("TryAiCoolingWell", PrivateInstance);
            bool placed = (bool)method.Invoke(f.Simulation, new object[] { 1u, origin, 1 });
            Assert.That(placed, Is.False);
            Assert.That(state.Buildings.Count(b => b.Alive && b.FactionId == 1 && b.Kind == BuildingKind.Well), Is.EqualTo(before));
        }

        [Test]
        public void EnemySoldierBreaksPipeAndMarksFluidNetworksDirty()
        {
            var f = new Fixture();
            var state = State(f.Simulation);
            int pipe = f.PlacePipe();
            var soldierIndex = Enumerable.Range(0, state.SoldierCount).First(i => state.Soldiers[i].Alive && state.Soldiers[i].Initial.FactionId == 2);
            var soldier = state.Soldiers[soldierIndex];
            soldier.Position = state.Map.Center(pipe);
            soldier.TargetKind = 6;
            soldier.TargetId = (uint)pipe + 1;
            state.Soldiers[soldierIndex] = soldier;
            state.Pipes[pipe].Hp = 1;
            Array.Fill(state.Factions[1].VisibleCells, true);
            f.Simulation.RebuildSpatialCombatIndexForTest();
            typeof(Battle).GetMethod("ClearRaidDamage", PrivateInstance).Invoke(f.Simulation, null);
            var add = typeof(Battle).GetMethod("AddRaidDamage", PrivateInstance);
            var args = new object[] { soldier };
            add.Invoke(f.Simulation, args);
            typeof(Battle).GetMethod("ApplyRaidDamage", PrivateInstance).Invoke(f.Simulation, null);
            typeof(Battle).GetMethod("ResolveRaidDeaths", PrivateInstance).Invoke(f.Simulation, null);
            Assert.That(state.Pipes[pipe].FactionId, Is.EqualTo(0));
            Assert.That(state.FluidsDirty, Is.True);
        }

        [Test]
        public void AutomaticAgeOneCoolingCreatesWaterSourceAndRoute()
        {
            var f = new Fixture();
            var state = State(f.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Civ = CivKind.Metallurgy;
            var trySource = typeof(Battle).GetMethod("TryAiFluidPlacement", PrivateInstance);
            trySource.Invoke(f.Simulation, new object[] { 1u, BuildingKind.Well, 2, false });
            var source = state.Buildings.FirstOrDefault(b => b.Alive && b.Kind == BuildingKind.Well);
            Assert.That(source.Alive, Is.True);
            state.Buildings[(int)source.Id - 1].Complete = true;
            state.FluidsDirty = true;
            f.Step(1);

            int width = f.Scenario.Map.WidthCells, smelter = -1;
            int px = source.OriginCell % width, pz = source.OriginCell / width;
            var nearbyCells = Enumerable.Range(0, state.Pipes.Length)
                .OrderBy(cell => Math.Abs(cell % width - px) + Math.Abs(cell / width - pz));
            foreach (int cell in nearbyCells)
            {
                if (smelter >= 0) break;
                int x = cell % width, z = cell / width;
                if (Math.Max(Math.Abs(x - px), Math.Abs(z - pz)) > 15) continue;
                int before = state.BuildingCount;
                f.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++f.Sequence, BuildingKind.Smelter, cell));
                f.Step(1);
                for (int i = before; i < state.BuildingCount; i++)
                    if (state.Buildings[i].Alive && !state.Buildings[i].Reserved && state.Buildings[i].Kind == BuildingKind.Smelter)
                    { smelter = i; state.Buildings[i].Complete = true; state.FluidsDirty = true; break; }
            }
            Assert.That(smelter, Is.GreaterThanOrEqualTo(0));
            state.Buildings[smelter].Input = state.Config.Economy.OrePerMetal;
            f.AutomaticDecideFluids();
            for (int i = 0; i < 1200; i++) f.Step(1);
            Assert.That(state.Pipes.Any(p => p.FactionId == 1), Is.True);
            state.Buildings[smelter].Input = state.Config.Economy.OrePerMetal;
            f.Step(200);
            Assert.That(state.Buildings[smelter].FluidNetworkId, Is.GreaterThanOrEqualTo(0));
            Assert.That(state.Economies[0].CoolingCount, Is.GreaterThan(0));
        }

        [Test]
        public void BeltAndPipeCannotShareACellInEitherOrder()
        {
            var first = new Fixture();
            int pipe = first.PlacePipe();
            first.Gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, ++first.Sequence, new[] { pipe }, new[] { Facing.North }));
            first.Step(1);
            Assert.That(State(first.Simulation).Belts[pipe].FactionId, Is.EqualTo(0));

            var second = new Fixture();
            var state = State(second.Simulation);
            int cell = second.FindOpenCell();
            second.Gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, ++second.Sequence, new[] { cell }, new[] { Facing.North }));
            second.Step(1);
            second.Gateway.SubmitEconomy(EconomyCommand.PlacePipe(1, ++second.Sequence, new[] { cell }));
            second.Step(1);
            Assert.That(state.Pipes[cell].FactionId, Is.EqualTo(0));
        }

        [Test]
        public void SplitAndJoinConserveNetworkAmount()
        {
            var f = new Fixture();
            var state = State(f.Simulation);
            int width = f.Scenario.Map.WidthCells;
            var cells = Enumerable.Range(0, state.Pipes.Length).Where(c => c % width <= width - 5 && state.Map.IsPassable(c)
                && Enumerable.Range(0, 5).All(i => state.Map.IsPassable(c + i) && f.Scenario.Map.Terrain[c + i] != (byte)TerrainKind.River)).Take(1)
                .SelectMany(c => Enumerable.Range(c, 5)).ToArray();
            f.Gateway.SubmitEconomy(EconomyCommand.PlacePipe(1, ++f.Sequence, cells));
            f.Step(1);
            Assert.That(state.FluidNetworks.Length, Is.EqualTo(1));
            state.FluidNetworks[0].Kind = FluidKind.Water;
            state.FluidNetworks[0].Amount = 65;
            f.Gateway.SubmitEconomy(EconomyCommand.RemovePipe(1, ++f.Sequence, cells[2]));
            f.Step(1);
            Assert.That(state.FluidNetworks.Sum(n => n.Amount), Is.EqualTo(65));
        }

        [Test]
        public void FlagOffCanonicalStateIsUnchangedByFluidCode()
        {
            var a = new Fixture(false);
            var b = new Fixture(false);
            for (int i = 0; i < 20; i++) { a.Simulation.Step(i + 2, Array.Empty<ScheduledInput>()); b.Simulation.Step(i + 2, Array.Empty<ScheduledInput>()); }
            Assert.That(ReplayBinary.Hash(a.Simulation.CaptureDiagnostic().CanonicalState), Is.EqualTo(ReplayBinary.Hash(b.Simulation.CaptureDiagnostic().CanonicalState)));
            Assert.That(ScenarioBinary.Encode(a.Scenario), Is.EqualTo(ScenarioBinary.Encode(b.Scenario)));
        }
    }
}
