using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        [Test]
        public void BridgeSecondAgeResearchChangesBridgeHpAndConstructionWork()
        {
            var s = Scenario(7);
            s.Economy.BridgeWork = 100;
            s.Economy.BridgeworksWorkReduction = 40;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Bridge));
            Steps(gateway, sim, 4);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Bridge));
            Steps(gateway, sim, 4);
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(2));

            uint camp = Place(gateway, sim, s, BuildingKind.EngineerCamp, ref sequence);
            Assume.That(camp, Is.Not.EqualTo(0u));
            BuildUp(gateway, sim, camp, ref sequence);
            uint smith = Place(gateway, sim, s, BuildingKind.Blacksmith, ref sequence);
            Assume.That(smith, Is.Not.EqualTo(0u));
            BuildUp(gateway, sim, smith, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++sequence, smith, BridgeTech.Bridgeworks));
            Steps(gateway, sim, 1);
            Assert.That(sim.Capture(1).Economy.Buildings.First(b => b.Id == smith).Researching,
                Is.EqualTo(BridgeTech.Bridgeworks));
            Steps(gateway, sim, s.Economy.BridgeworksTicks + 1);
            Assert.That(sim.Capture(1).Economy.Techs & (1UL << 12), Is.Not.EqualTo(0UL), "bridgeworks is researched outside TechKind");

            var cells = FindBridgeCells(s, sim, 1);
            Assume.That(cells, Is.Not.Null);
            gateway.SubmitEconomy(EconomyCommand.PlaceBridge(1, ++sequence, cells, Facing.East));
            Steps(gateway, sim, 2000);
            var bridge = sim.Capture(1).Economy.Buildings.First(b => b.Kind == BuildingKind.Bridge && b.PlayerHeld);
            Assert.That(bridge.MaxHp, Is.EqualTo(s.Economy.BridgeHp + s.Economy.BridgeworksHpBonus));
            Assert.That(bridge.Work, Is.EqualTo(s.Economy.BridgeWork - s.Economy.BridgeworksWorkReduction));
            Assert.That(bridge.Hp, Is.EqualTo(bridge.MaxHp));

            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, s, gateway.Inputs, sim.Capture(1).Tick, build);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, build);
                Assert.That(replay.FirstMismatchTick, Is.Null, "bridge research replay is deterministic");
                Assert.That(replay.IsFault, Is.False);
            }
        }

        [Test]
        public void BridgeThirdAgeResearchSpeedsExistingSiegeWorkshopRams()
        {
            var s = Scenario(7);
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Bridge));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 4);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Bridge));
            Steps(gateway, sim, s.Economy.Age2Ticks + 4);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Bridge));
            Steps(gateway, sim, s.Economy.Age3Ticks + 4);
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(3));

            uint smith = Place(gateway, sim, s, BuildingKind.Blacksmith, ref sequence);
            Assume.That(smith, Is.Not.EqualTo(0u));
            BuildUp(gateway, sim, smith, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++sequence, smith, BridgeTech.SiegeDeployment));
            Steps(gateway, sim, 1);
            Steps(gateway, sim, s.Economy.SiegeDeploymentTicks + 1);

            uint workshop = Place(gateway, sim, s, BuildingKind.SiegeWorkshop, ref sequence);
            Assume.That(workshop, Is.Not.EqualTo(0u));
            BuildUp(gateway, sim, workshop, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++sequence, workshop, UnitKind.Ram));
            Steps(gateway, sim, 1);
            var queued = sim.Capture(1).Economy.Buildings.First(b => b.Id == workshop);
            Assert.That(queued.TrainRemaining, Is.EqualTo(s.Economy.RamTicks - s.Economy.SiegeDeploymentRamTicksReduction - 1));
        }

        [Test]
        public void ExistingVillagerAndRamRouteSearchGetsShorterWhenTheBridgeOpens()
        {
            var s = Scenario(7);
            var cells = FindBridgeCells(s, new Battle(s), 1);
            Assume.That(cells, Is.Not.Null);
            int width = s.Map.WidthCells;
            int delta = cells.Length > 1 ? cells[1] - cells[0] : 1;
            int start = cells[0] - delta, goal = cells[cells.Length - 1] + delta;
            var withoutBridge = new GridMap(s.Map);
            int without = withoutBridge.SharedRoute(start, withoutBridge.Center(goal)).Length;
            var opened = new MapDefinition
            {
                WidthMeters = s.Map.WidthMeters, HeightMeters = s.Map.HeightMeters, CellSizeMeters = s.Map.CellSizeMeters,
                WidthCells = s.Map.WidthCells, HeightCells = s.Map.HeightCells, DefaultPassable = s.Map.DefaultPassable,
                BlockedCellIds = s.Map.BlockedCellIds.Where(cell => !cells.Contains(cell)).ToArray(),
                Terrain = (byte[])s.Map.Terrain.Clone()
            };
            var withBridge = new GridMap(opened);
            int with = withBridge.SharedRoute(start, withBridge.Center(goal)).Length;
            Assume.That(without, Is.GreaterThan(0));
            Assert.That(with, Is.GreaterThan(0));
            Assert.That(with, Is.LessThan(without), "the existing shared route is shorter across the bridge");

            // Both movement consumers use this same cell route. Convert the route length to their
            // configured travel ticks to show the expected difference for a villager and a ram.
            long villagerWithout = TravelTicks(without, s.Economy.VillagerSpeed, s.Map.CellSizeMeters);
            long villagerWith = TravelTicks(with, s.Economy.VillagerSpeed, s.Map.CellSizeMeters);
            long ramWithout = TravelTicks(without, s.Economy.RamSpeed, s.Map.CellSizeMeters);
            long ramWith = TravelTicks(with, s.Economy.RamSpeed, s.Map.CellSizeMeters);
            Assert.That(villagerWith, Is.LessThan(villagerWithout), "resource round trip leg is shorter for villagers");
            Assert.That(ramWith, Is.LessThan(ramWithout), "reinforcement/siege arrival leg is shorter for rams");
        }

        private static long TravelTicks(int routeCells, Fix64 speed, int cellSize)
            => checked((long)Math.Max(0, routeCells - 1) * cellSize * 20 * Fix64.FromInt(1).Raw / speed.Raw);

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(pair => pair.Key, pair => pair.Value);

        private static long Number(Dictionary<string, string> fields, string name)
            => long.Parse(fields[name], CultureInfo.InvariantCulture);

        private static uint Place(CommandGateway gateway, Battle sim, ScenarioDefinition s, BuildingKind kind, ref ulong sequence)
        {
            const int width = 128;
            int core = (int)(s.Cores[0].Position.Z.Raw / 65536 / 2) * width + (int)(s.Cores[0].Position.X.Raw / 65536 / 2);
            int cx = core % width, cz = core / width;
            for (int radius = 5; radius <= 14; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius || cx + dx < 0 || cz + dz < 0
                            || cx + dx > width - 4 || cz + dz > 60) continue;
                        long before = Number(Fields(sim), "Buildings.Count");
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, kind, (cz + dz) * width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var fields = Fields(sim);
                        if (Number(fields, "Buildings.Count") > before
                            && fields["Buildings[" + (before + 1) + "].Kind"] == ((byte)kind).ToString(CultureInfo.InvariantCulture)) return (uint)(before + 1);
                    }
            return 0;
        }

        private static void BuildUp(CommandGateway gateway, Battle sim, uint building, ref ulong sequence)
        {
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, building));
            for (int i = 0; i < 4000 && !sim.Capture(1).Economy.Buildings.First(b => b.Id == building).Complete; i += 20) Steps(gateway, sim, 20);
            Assert.That(sim.Capture(1).Economy.Buildings.First(b => b.Id == building).Complete, Is.True);
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
