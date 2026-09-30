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

        [Test]
        public void AutomaticBridgeIsBuiltForAUsefulObservedDestination()
        {
            var s = Scenario(7);
            s.Rules.OwnedObjectiveVision = Fix64.FromInt(512);
            var cells = FindBridgeCells(s, new Battle(s), 1);
            Assert.That(cells, Is.Not.Null);
            int width = s.Map.WidthCells, delta = cells.Length > 1 ? cells[1] - cells[0] : 1;
            int targetCell = cells[0] - delta;
            var target = new SimPoint(Fix64.FromInt((targetCell % width) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2),
                Fix64.FromInt((targetCell / width) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2));
            var node = s.ResourceNodes[0];
            node.Position = target;
            node.Amount = 100000;
            s.ResourceNodes = new[] { node };
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Bridge));
            Steps(gateway, sim, 7000);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Bridge && !b.PlayerHeld), Is.True);
        }

        [Test]
        public void AutomaticBridgeWaitsBeforeRebuildingADestroyedBridge()
        {
            var s = Scenario(7);
            s.Rules.OwnedObjectiveVision = Fix64.FromInt(512);
            var cells = FindBridgeCells(s, new Battle(s), 1);
            Assert.That(cells, Is.Not.Null);
            int delta = cells.Length > 1 ? cells[1] - cells[0] : 1;
            var node = s.ResourceNodes[0];
            int target = cells[0] - delta;
            node.Position = new SimPoint(Fix64.FromInt((target % s.Map.WidthCells) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2),
                Fix64.FromInt((target / s.Map.WidthCells) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2));
            node.Amount = 100000;
            s.ResourceNodes = new[] { node };
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Bridge));
            Steps(gateway, sim, 7000);
            var bridge = sim.Capture(1).Economy.Buildings.First(b => b.Kind == BuildingKind.Bridge && !b.PlayerHeld);
            SetBuildingHp(sim, bridge.Id, 0);
            MoveFactionUnits(sim, 2, s.Cores[1].Position);
            Steps(gateway, sim, 1);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Bridge), Is.False);
            Steps(gateway, sim, 100);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Bridge), Is.False);
            Steps(gateway, sim, 140);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Bridge && !b.PlayerHeld), Is.True);
        }

        [Test]
        public void ManualBridgePreventsAutomaticReplacement()
        {
            var s = Scenario(7);
            s.Rules.OwnedObjectiveVision = Fix64.FromInt(512);
            var cells = FindBridgeCells(s, new Battle(s), 1);
            Assert.That(cells, Is.Not.Null);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Bridge));
            Steps(gateway, sim, 4);
            uint camp = 0;
            for (int i = 0; i < 500 && camp == 0; i++)
            {
                Steps(gateway, sim, 1);
                camp = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.EngineerCamp && b.Complete).Id;
            }
            Assert.That(camp, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.PlaceBridge(1, ++sequence, cells, Facing.East));
            Steps(gateway, sim, 3000);
            var bridges = sim.Capture(1).Economy.Buildings.Where(b => b.Kind == BuildingKind.Bridge).ToArray();
            Assert.That(bridges.Length, Is.EqualTo(1));
            Assert.That(bridges[0].PlayerHeld, Is.True);
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

        private static void SetBuildingHp(Battle sim, uint id, int hp)
        {
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);
            var buildings = (Array)world.GetType().GetField("Buildings", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(world);
            object state = buildings.GetValue((int)id - 1);
            state.GetType().GetField("Hp", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, hp);
            buildings.SetValue(state, (int)id - 1);
        }

        private static void MoveFactionUnits(Battle sim, uint faction, SimPoint point)
        {
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(world);
            for (int i = 0; i < soldiers.Length; i++)
            {
                object state = soldiers.GetValue(i);
                var initial = state.GetType().GetField("Initial", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
                if ((uint)initial.GetType().GetField("FactionId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(initial) != faction) continue;
                state.GetType().GetField("Position", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, point);
                state.GetType().GetField("MoveGoal", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, point);
                soldiers.SetValue(state, i);
            }
            var villagers = (Array)world.GetType().GetField("Villagers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(world);
            for (int i = 0; i < villagers.Length; i++)
            {
                object state = villagers.GetValue(i);
                if ((uint)state.GetType().GetField("FactionId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state) != faction) continue;
                state.GetType().GetField("Position", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, point);
                state.GetType().GetField("MoveGoal", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, point);
                villagers.SetValue(state, i);
            }
        }
    }
}
