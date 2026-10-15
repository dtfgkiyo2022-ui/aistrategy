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

        private static int DropSites(Dictionary<string, string> fields, uint faction = 1)
            => fields.Count(p => p.Key.EndsWith("].Kind", StringComparison.Ordinal)
                && p.Value == ((byte)BuildingKind.DropSite).ToString(CultureInfo.InvariantCulture)
                && fields.TryGetValue(p.Key.Substring(0, p.Key.Length - ".Kind".Length) + ".Alive", out string alive)
                && alive == "1"
                && fields.TryGetValue(p.Key.Substring(0, p.Key.Length - ".Kind".Length) + ".FactionId", out string owner)
                && owner == faction.ToString(CultureInfo.InvariantCulture));

        private static uint[] DropSiteIds(Dictionary<string, string> fields, uint faction = 1)
            => fields.Where(p => p.Key.StartsWith("Buildings[", StringComparison.Ordinal)
                    && p.Key.EndsWith("].Kind", StringComparison.Ordinal)
                    && p.Value == ((byte)BuildingKind.DropSite).ToString(CultureInfo.InvariantCulture)
                    && fields.TryGetValue(p.Key.Substring(0, p.Key.Length - ".Kind".Length) + ".Alive", out string alive)
                    && alive == "1"
                    && fields.TryGetValue(p.Key.Substring(0, p.Key.Length - ".Kind".Length) + ".FactionId", out string owner)
                    && owner == faction.ToString(CultureInfo.InvariantCulture))
                .Select(p => uint.Parse(p.Key.Substring("Buildings[".Length, p.Key.Length - "Buildings[".Length - "].Kind".Length), CultureInfo.InvariantCulture))
                .OrderBy(id => id)
                .ToArray();

        private static int CellOf(ScenarioDefinition s, SimPoint p)
            => (int)(p.Z.Raw / 65536 / s.Map.CellSizeMeters) * s.Map.WidthCells
                + (int)(p.X.Raw / 65536 / s.Map.CellSizeMeters);

        private static bool InsideCore(ScenarioDefinition s, int cell)
        {
            int width = s.Map.WidthCells;
            long x = (cell % width) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2;
            long z = (cell / width) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2;
            foreach (var core in s.Cores)
            {
                long dx = x - core.Position.X.Raw / 65536, dz = z - core.Position.Z.Raw / 65536;
                long radius = s.Rules.CoreRadius.Raw / 65536;
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
            return false;
        }

        private static bool InsideOwnCore(ScenarioDefinition s, int cell)
        {
            int width = s.Map.WidthCells;
            long x = (cell % width) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2;
            long z = (cell / width) * s.Map.CellSizeMeters + s.Map.CellSizeMeters / 2;
            long dx = x - s.Cores[0].Position.X.Raw / 65536, dz = z - s.Cores[0].Position.Z.Raw / 65536;
            long radius = s.Rules.CoreRadius.Raw / 65536;
            return dx * dx + dz * dz <= radius * radius;
        }

        private static int Neighbour(ScenarioDefinition s, int cell, Facing facing)
        {
            int width = s.Map.WidthCells, height = s.Map.HeightCells, x = cell % width, z = cell / width;
            if (facing == Facing.North) return z + 1 < height ? cell + width : -1;
            if (facing == Facing.East) return x + 1 < width ? cell + 1 : -1;
            if (facing == Facing.South) return z > 0 ? cell - width : -1;
            return x > 0 ? cell - 1 : -1;
        }

        private static int OutputCell(ScenarioDefinition s, int origin, Facing facing)
        {
            int width = s.Map.WidthCells, size = s.Economy.StorageSizeCells, x0 = origin % width, z0 = origin / width;
            int x = facing == Facing.East ? x0 + size : facing == Facing.West ? x0 - 1 : x0 + size / 2;
            int z = facing == Facing.North ? z0 + size : facing == Facing.South ? z0 - 1 : z0 + size / 2;
            return x < 0 || z < 0 || x >= width || z >= s.Map.HeightCells ? -1 : z * width + x;
        }

        private static int[] Footprint(ScenarioDefinition s, int origin)
        {
            int width = s.Map.WidthCells, size = s.Economy.StorageSizeCells;
            var cells = new int[size * size];
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++) cells[z * size + x] = origin + z * width + x;
            return cells;
        }

        private static (int[] cells, Facing[] facings) Route(ScenarioDefinition s, int start, HashSet<int> taken)
        {
            var blocked = new HashSet<int>(s.Map.BlockedCellIds);
            var nodes = new HashSet<int>(s.ResourceNodes.Select(n => CellOf(s, n.Position)));
            bool Open(int cell) => cell >= 0 && !blocked.Contains(cell) && !nodes.Contains(cell) && !taken.Contains(cell)
                && !InsideCore(s, cell) && (s.Map.Terrain.Length == 0 || s.Map.Terrain[cell] != (byte)TerrainKind.River);
            if (!Open(start)) return (null, null);
            var from = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                foreach (var facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    int next = Neighbour(s, cell, facing);
                    if (next < 0) continue;
                    if (InsideOwnCore(s, next))
                    {
                        var path = new List<int>();
                        for (int c = cell; c >= 0; c = from[c]) path.Add(c);
                        path.Reverse();
                        var facings = new Facing[path.Count];
                        for (int i = 0; i + 1 < path.Count; i++)
                            foreach (var direction in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                                if (Neighbour(s, path[i], direction) == path[i + 1]) { facings[i] = direction; break; }
                        facings[facings.Length - 1] = facing;
                        return (path.ToArray(), facings);
                    }
                    if (Open(next) && !from.ContainsKey(next)) { from[next] = cell; queue.Enqueue(next); }
                }
            }
            return (null, null);
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, uint storage, ResourceNodeDefinition node, int[] route, Facing[] facings)
            PrepareStorageScenario(bool connected)
        {
            var s = Scenario(true, true);
            s.Economy.BeltComponents = true;
            s.Economy.StorageWoodCost = 1;
            s.Economy.StorageWork = 1;
            s.Economy.StorageCapacity = 100;
            s.Economy.BeltWoodCost = 1;
            s.Economy.BeltTicksPerCell = 1;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            var core = CellOf(s, s.Cores[0].Position);
            var node = s.ResourceNodes.Where(n => n.Kind == ResourceKind.Food && CellOf(s, n.Position) % s.Map.WidthCells < s.Map.WidthCells / 2)
                .OrderByDescending(n => Math.Abs(CellOf(s, n.Position) % s.Map.WidthCells - core % s.Map.WidthCells)
                    + Math.Abs(CellOf(s, n.Position) / s.Map.WidthCells - core / s.Map.WidthCells)).First();
            int nodeCell = CellOf(s, node.Position);
            int nodeX = nodeCell % s.Map.WidthCells, nodeZ = nodeCell / s.Map.WidthCells;
            int routeCandidates = 0, placedCandidates = 0;
            var origins = Enumerable.Range(0, s.Map.WidthCells * s.Map.HeightCells)
                .Where(c => Math.Max(Math.Abs(c % s.Map.WidthCells - nodeX), Math.Abs(c / s.Map.WidthCells - nodeZ)) <= 12)
                .OrderBy(c => Math.Abs(c % s.Map.WidthCells - nodeX) + Math.Abs(c / s.Map.WidthCells - nodeZ));
            foreach (int origin in origins)
            {
                foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    var route = Route(s, OutputCell(s, origin, facing), new HashSet<int>(Footprint(s, origin)));
                    if (connected && route.cells == null) continue;
                    routeCandidates++;
                    long before = Number(Fields(sim), "Buildings.Count");
                    gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Storage, origin, facing));
                    gateway.Step();
                    if (Number(Fields(sim), "Buildings.Count") == before) continue;
                    placedCandidates++;
                    uint storage = (uint)(before + 1);
                    gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, storage));
                    for (int i = 0; i < 3000 && Fields(sim)["Buildings[" + storage + "].Complete"] != "1"; i++) gateway.Step();
                    if (Fields(sim)["Buildings[" + storage + "].Complete"] != "1") continue;
                    if (connected) gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, ++sequence, route.cells, route.facings));
                    gateway.Step();
                    return (s, sim, gateway, storage, node, route.cells, route.facings);
                }
            }
            Assert.Fail("倉庫の配置候補を作れなかった route=" + routeCandidates + " placed=" + placedCandidates + " node=" + nodeCell);
            return default;
        }

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
            for (long tick = 1; tick <= 30000; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>());
                b.Step(tick, Array.Empty<ScheduledInput>());
                var left = a.CaptureDiagnostic();
                var right = b.CaptureDiagnostic();
                Assert.That(ReplayBinary.Hash(left.CanonicalState), Is.EqualTo(ReplayBinary.Hash(right.CanonicalState)), "tick " + tick);
                if (a.Capture(1).Result.HasEnded || b.Capture(1).Result.HasEnded) break;
            }
            Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState), Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)));
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
            scenario.Economy.DropSiteWork = 100;
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
            Assert.That(Number(Fields(sim), "Buildings[1].Complete"), Is.EqualTo(0));
            Assert.That(Number(Fields(sim), "Buildings[2].Complete"), Is.EqualTo(0));
            long afterTwo = Number(Fields(sim), "Economy[1].Wood");
            uint removedDropSite = DropSiteIds(Fields(sim)).First();
            gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, ++sequence, removedDropSite));
            gateway.Step();
            Assert.That(DropSites(Fields(sim)), Is.EqualTo(1));
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.DropSite, candidates[19], Facing.North));
            gateway.Step();
            Assert.That(DropSites(Fields(sim)), Is.EqualTo(2));
            Assert.That(Number(Fields(sim), "Economy[1].Wood"), Is.EqualTo(afterTwo - scenario.Economy.DropSiteWoodCost));
        }

        [Test]
        public void AiNeverExceedsTwoDropSitesAtAnyTickAndCanReplaceADestroyedOne()
        {
            var scenario = Scenario(true, false);
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 12000 && !sim.Capture(1).Result.HasEnded; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var fields = Fields(sim);
                Assert.That(DropSites(fields, 1), Is.LessThanOrEqualTo(2), "faction 1 tick " + tick);
                Assert.That(DropSites(fields, 2), Is.LessThanOrEqualTo(2), "faction 2 tick " + tick);
            }
        }

        [Test]
        public void ConnectedStorageAcceptsRawVillagerLoadsButDisconnectedStorageDoesNot()
        {
            var connected = PrepareStorageScenario(true);
            connected.gateway.SubmitEconomy(EconomyCommand.Assign(1, 20, new uint[] { 1, 2 }, EconomyTargetKind.ResourceNode, connected.node.Id));
            bool stored = false;
            for (int i = 0; i < 2500 && !stored; i++)
            {
                connected.gateway.Step();
                stored = Number(Fields(connected.sim), "Buildings[" + connected.storage + "].StorageCount") > 0;
            }
            if (!stored)
            {
                var detail = Fields(connected.sim);
                Assert.Fail("接続済み倉庫に素資源が入る storage=" + detail["Buildings[" + connected.storage + "].StorageCount"]
                    + " complete=" + detail["Buildings[" + connected.storage + "].Complete"]
                    + " belts=" + detail["Belts.Count"] + " food=" + detail["Economy[1].Food"]
                    + " node=" + detail["ResourceNodes[" + connected.node.Id + "].Remaining"]
                    + " task=" + detail["Villagers[1].Task"] + " carry=" + detail["Villagers[1].Carry"]
                    + " origin=" + detail["Buildings[" + connected.storage + "].OriginCell"]
                    + " facing=" + detail["Buildings[" + connected.storage + "].Facing"]
                    + " route=" + connected.route.Length + " first=" + connected.route[0]
                    + " last=" + connected.route[connected.route.Length - 1]
                    + " lastFace=" + detail["Belts[" + connected.route[connected.route.Length - 1] + "].Facing"]
                    + " lastFaction=" + detail["Belts[" + connected.route[connected.route.Length - 1] + "].FactionId"]);
            }

            var disconnected = PrepareStorageScenario(false);
            long foodBefore = Number(Fields(disconnected.sim), "Economy[1].Food");
            disconnected.gateway.SubmitEconomy(EconomyCommand.Assign(1, 20, new uint[] { 1, 2 }, EconomyTargetKind.ResourceNode, disconnected.node.Id));
            for (int i = 0; i < 2500; i++) disconnected.gateway.Step();
            var fields = Fields(disconnected.sim);
            Assert.That(Number(fields, "Buildings[" + disconnected.storage + "].StorageCount"), Is.EqualTo(0));
            Assert.That(Number(fields, "Economy[1].Food"), Is.GreaterThan(foodBefore));
        }

        [Test]
        public void AiBeltsMatchReplaysWithTheSameHashForEveryTick()
        {
            var scenario = Scenario(true, true);
            using (var stream = new System.IO.MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 12000, new BuildIdentity());
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
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
