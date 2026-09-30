using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BridgeTests
    {
        private static ScenarioDefinition Scenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed, bridge: true);
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.EngineerCampWork = 1;
            s.Economy.BridgeWork = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        [Test]
        public void BridgeTailRoundTripsAndOffBytesStayUnchanged()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var bridge = MapGenerator.GenerateTerrain(1, bridge: true);
            var oldBytes = ScenarioBinary.Encode(old);
            var bridgeBytes = ScenarioBinary.Encode(bridge);
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(oldBytes)), Is.EqualTo(oldBytes));
            Assert.That(bridgeBytes.Length, Is.GreaterThan(oldBytes.Length));
            var decoded = ScenarioBinary.Decode(bridgeBytes);
            Assert.That(decoded.Economy.Bridge, Is.True);
            Assert.That((decoded.Economy.EngineerCampSizeCells, decoded.Economy.BridgeWork, decoded.Economy.MaxBridgeLength),
                Is.EqualTo((2, 120, 6)));
        }

        [Test]
        public void ManualBridgeOpensAndRemovingItClosesTheRiverAgain()
        {
            var s = Scenario(7);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Bridge));
            Steps(gateway, sim, 4);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Bridge));

            uint camp = 0;
            for (int i = 0; i < 500 && camp == 0; i++)
            {
                Steps(gateway, sim, 1);
                camp = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.FactionId == 1 && b.Kind == BuildingKind.EngineerCamp && b.Complete).Id;
            }
            Assert.That(camp, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));

            var bridgeCells = FindBridgeCells(s, sim, 1);
            Assert.That(bridgeCells, Is.Not.Null);
            gateway.SubmitEconomy(EconomyCommand.PlaceBridge(1, ++sequence, bridgeCells, Facing.East));
            Steps(gateway, sim, 3000);
            var bridge = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.FactionId == 1 && b.Kind == BuildingKind.Bridge);
            Assert.That(bridge.Id, Is.Not.EqualTo(0u));
            Assert.That(bridge.Complete, Is.True);
            Assert.That(bridgeCells.All(cell => Passable(sim, cell)), Is.True);

            gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, ++sequence, bridge.Id));
            Steps(gateway, sim, 1);
            Assert.That(bridgeCells.All(cell => !Passable(sim, cell)), Is.True);
        }

        private static int[] FindBridgeCells(ScenarioDefinition s, Battle sim, uint faction)
        {
            var blocked = new bool[s.Map.WidthCells * s.Map.HeightCells];
            foreach (int cell in s.Map.BlockedCellIds) blocked[cell] = true;
            int width = s.Map.WidthCells, height = s.Map.HeightCells;
            for (int length = 1; length <= s.Economy.MaxBridgeLength; length++)
                for (int z = 0; z < height; z++)
                    for (int x = 0; x < width; x++)
                    {
                        foreach (int delta in new[] { 1, width })
                        {
                            int last = z * width + x + delta * (length - 1);
                            if (last < 0 || last >= blocked.Length || delta == 1 && last / width != z || delta == width && last % width != x) continue;
                            var cells = Enumerable.Range(0, length).Select(i => z * width + x + delta * i).ToArray();
                            if (!cells.All(c => s.Map.Terrain[c] == (byte)TerrainKind.River && blocked[c])) continue;
                            int before = cells[0] - delta, after = cells[cells.Length - 1] + delta;
                            if (before < 0 || after < 0 || before >= blocked.Length || after >= blocked.Length || blocked[before] || blocked[after]) continue;
                            if (delta == 1 && (before / width != z || after / width != z) || delta == width && (before % width != x || after % width != x)) continue;
                            return cells;
                        }
                    }
            return null;
        }

        private static bool Passable(Battle sim, int cell)
        {
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);
            var map = world.GetType().GetField("Map", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(world);
            return (bool)map.GetType().GetMethod("IsPassable").Invoke(map, new object[] { cell });
        }
    }
}
