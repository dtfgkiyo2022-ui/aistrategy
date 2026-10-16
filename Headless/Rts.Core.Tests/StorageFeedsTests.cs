using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class StorageFeedsTests
    {
        private const int Width = 128, Height = 64;

        private sealed class Match
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Match(ScenarioDefinition scenario)
            {
                Scenario = scenario;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Send(EconomyCommand.Auto(1, ++Sequence, false));
                Send(EconomyCommand.Auto(2, ++Sequence, false));
                Steps(1);
            }

            internal void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);

            internal void Steps(int count)
            {
                for (int i = 0; i < count && !Simulation.Capture(1).Result.HasEnded; i++) Gateway.Step();
            }

            internal Dictionary<string, string> Fields()
                => DiagnosticComparison.Fields(Simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
        }

        private static long Number(Dictionary<string, string> fields, string name)
            => long.Parse(fields[name], CultureInfo.InvariantCulture);

        private static ScenarioDefinition Scenario(bool storageFeedsProcessing)
        {
            var scenario = MapGenerator.GenerateTerrain(7406UL, false, true);
            var e = scenario.Economy;
            e.Ages = true;
            e.Industry = true;
            e.ProcessingChain = true;
            e.BeltComponents = true;
            e.AiBelts = true;
            e.StorageFeedsProcessing = storageFeedsProcessing;
            e.StartFood = 1000;
            e.StartWood = 10000;
            e.AdvanceFoodCost = 0;
            e.AdvanceWoodCost = 0;
            e.AdvanceTicks = 1;
            e.Age2FoodCost = 0;
            e.Age2WoodCost = 0;
            e.Age2Ticks = 1;
            e.VillagerFoodCost = 0;
            e.GatherIntervalTicks = 10;
            e.BeltTicksPerCell = 1;
            e.BufferLimit = 10;
            return scenario;
        }

        private static int CellOf(SimPoint point)
            => (int)(point.Z.Raw / 65536 / 2) * Width + (int)(point.X.Raw / 65536 / 2);

        private static int Neighbour(int cell, Facing facing)
        {
            int x = cell % Width, z = cell / Width;
            switch (facing)
            {
                case Facing.North: return z + 1 < Height ? cell + Width : -1;
                case Facing.East: return x + 1 < Width ? cell + 1 : -1;
                case Facing.South: return z > 0 ? cell - Width : -1;
                default: return x > 0 ? cell - 1 : -1;
            }
        }

        private static int[] Footprint(int origin, int size)
        {
            var result = new int[size * size];
            int n = 0;
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++) result[n++] = origin + z * Width + x;
            return result;
        }

        private static int OutputCell(int origin, int size, Facing facing)
        {
            int x0 = origin % Width, z0 = origin / Width, x, z;
            switch (facing)
            {
                case Facing.North: x = x0 + size / 2; z = z0 + size; break;
                case Facing.East: x = x0 + size; z = z0 + size / 2; break;
                case Facing.South: x = x0 + size / 2; z = z0 - 1; break;
                default: x = x0 - 1; z = z0 + size / 2; break;
            }
            return x < 0 || z < 0 || x >= Width || z >= Height ? -1 : z * Width + x;
        }

        private static bool InsideCore(ScenarioDefinition scenario, int cell)
        {
            long x = (cell % Width) * 2 + 1, z = (cell / Width) * 2 + 1;
            foreach (var core in scenario.Cores)
            {
                long dx = x - core.Position.X.Raw / 65536, dz = z - core.Position.Z.Raw / 65536;
                if (dx * dx + dz * dz <= 16) return true;
            }
            return false;
        }

        private static IEnumerable<int> Ring(int centre, int from, int to)
        {
            int cx = centre % Width, cz = centre / Width;
            for (int radius = from; radius <= to; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == radius
                            && cx + dx >= 0 && cz + dz >= 0 && cx + dx < Width - 3 && cz + dz < Height - 3)
                            yield return (cz + dz) * Width + cx + dx;
        }

        private static (int[] cells, Facing[] facings) Route(ScenarioDefinition scenario, int start,
            Func<int, bool> destination, HashSet<int> taken)
        {
            var blocked = new HashSet<int>(scenario.Map.BlockedCellIds);
            var nodes = new HashSet<int>(scenario.ResourceNodes.Select(n => CellOf(n.Position)));
            bool Open(int cell) => cell >= 0 && !blocked.Contains(cell) && !nodes.Contains(cell)
                && !taken.Contains(cell) && !InsideCore(scenario, cell);
            if (!Open(start)) return (null, null);
            var previous = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    int next = Neighbour(cell, facing);
                    if (next < 0) continue;
                    if (destination(next))
                    {
                        var path = new List<int>();
                        for (int current = cell; current != -1; current = previous[current]) path.Add(current);
                        path.Reverse();
                        var facings = new Facing[path.Count];
                        for (int i = 0; i + 1 < path.Count; i++)
                            facings[i] = new[] { Facing.North, Facing.East, Facing.South, Facing.West }
                                .First(f => Neighbour(path[i], f) == path[i + 1]);
                        facings[path.Count - 1] = facing;
                        return (path.ToArray(), facings);
                    }
                    if (Open(next) && !previous.ContainsKey(next))
                    {
                        previous[next] = cell;
                        queue.Enqueue(next);
                    }
                }
            }
            return (null, null);
        }

        private static void AdvanceToMetallurgy(Match match)
        {
            match.Send(EconomyCommand.Advance(1, ++match.Sequence, CivKind.Metallurgy));
            match.Steps(3);
            Assert.That(match.Fields()["Economy[1].Civ"], Is.EqualTo(((byte)CivKind.Metallurgy).ToString(CultureInfo.InvariantCulture)));
        }

        private static (uint id, int origin) Place(Match match, BuildingKind kind, Facing facing, IEnumerable<int> origins)
        {
            foreach (int origin in origins)
            {
                long before = Number(match.Fields(), "Buildings.Count");
                match.Send(EconomyCommand.Place(1, ++match.Sequence, kind, origin, facing));
                match.Steps(1);
                if (Number(match.Fields(), "Buildings.Count") > before) return ((uint)(before + 1), origin);
            }
            return (0, -1);
        }

        private static void Build(Match match, uint buildingId)
        {
            match.Send(EconomyCommand.Assign(1, ++match.Sequence, new uint[] { 1, 2, 3 },
                EconomyTargetKind.Building, buildingId));
            for (int i = 0; i < 3000 && match.Fields()["Buildings[" + buildingId + "].Complete"] != "1"; i += 20)
                match.Steps(20);
            Assert.That(match.Fields()["Buildings[" + buildingId + "].Complete"], Is.EqualTo("1"));
        }

        private static (Match match, uint storage, uint smelter, uint oreId) ProcessingLine(bool enabled)
        {
            var match = new Match(Scenario(enabled));
            AdvanceToMetallurgy(match);
            var fields = match.Fields();
            int core = CellOf(match.Scenario.Cores[0].Position);
            var ore = match.Scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Ore)
                .OrderBy(n => Math.Abs(CellOf(n.Position) % Width - core % Width)
                    + Math.Abs(CellOf(n.Position) / Width - core / Width)).First();
            var storageOrigins = Ring(CellOf(ore.Position), 2, 10);
            var storage = Place(match, BuildingKind.Storage, Facing.North, storageOrigins);
            Assume.That(storage.id, Is.Not.EqualTo(0u), "a storage site near an ore point");
            Build(match, storage.id);
            int output = OutputCell(storage.origin, match.Scenario.Economy.StorageSizeCells, Facing.North);
            int smelterSize = match.Scenario.Economy.SmelterSizeCells;
            uint smelter = 0;
            int smelterOrigin = -1;
            int[] routeCells = null;
            Facing[] routeFacings = null;
            foreach (int origin in Ring(output, 2, 15))
            {
                var footprint = new HashSet<int>(Footprint(origin, smelterSize));
                var route = Route(match.Scenario, output, footprint.Contains,
                    new HashSet<int>(Footprint(storage.origin, match.Scenario.Economy.StorageSizeCells).Concat(footprint)));
                if (route.cells == null) continue;
                var placed = Place(match, BuildingKind.Smelter, Facing.North, new[] { origin });
                if (placed.id == 0) continue;
                smelter = placed.id;
                smelterOrigin = placed.origin;
                routeCells = route.cells;
                routeFacings = route.facings;
                break;
            }
            Assume.That(smelter, Is.Not.EqualTo(0u), "a smelter with a route from the storage");
            Build(match, smelter);
            match.Send(EconomyCommand.PlaceBelt(1, ++match.Sequence, routeCells, routeFacings));
            match.Send(EconomyCommand.Assign(1, ++match.Sequence, new uint[] { 1 },
                EconomyTargetKind.ResourceNode, ore.Id));
            match.Steps(4000);
            return (match, storage.id, smelter, ore.Id);
        }

        [Test]
        public void StorageFeedsAnOreReceiverOnlyWhenTheFlagIsOn()
        {
            var on = ProcessingLine(true);
            var onFields = on.match.Fields();
            Assert.That(Number(onFields, "Buildings[" + on.smelter + "].Input"), Is.GreaterThan(0), "ore reached the smelter through storage");

            var off = ProcessingLine(false);
            var offFields = off.match.Fields();
            Assert.That(Number(offFields, "Economy[1].Metal"), Is.EqualTo(0), "without the flag the villager uses the core");
            Assert.That(Number(offFields, "Buildings[" + off.smelter + "].Input"), Is.EqualTo(0));
        }

        [Test]
        public void AStorageRouteIsCheckedForTheCarriedResourceKind()
        {
            // The ore line from ProcessingLine leads only to a smelter, which takes ore and not wood: once the villager
            // switches to wood, the storage must not take wood in (it would sit there with nowhere to go).
            var line = ProcessingLine(true);
            int core = CellOf(line.match.Scenario.Cores[0].Position);
            var wood = line.match.Scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Wood)
                .OrderBy(n => Math.Abs(CellOf(n.Position) % Width - core % Width)
                    + Math.Abs(CellOf(n.Position) / Width - core / Width)).First();
            long woodBefore = Number(line.match.Fields(), "Economy[1].Wood");
            line.match.Send(EconomyCommand.Assign(1, ++line.match.Sequence, new uint[] { 1 }, EconomyTargetKind.ResourceNode, wood.Id));
            for (int step = 0; step < 40; step++)
            {
                line.match.Steps(100);
                var fields = line.match.Fields();
                Assert.That(Number(fields, "Buildings[" + line.storage + "].StorageKind"), Is.Not.EqualTo((int)(byte)ResourceKind.Wood),
                    "a storage whose belt leads only to an ore receiver took wood");
            }
            Assert.That(Number(line.match.Fields(), "Economy[1].Wood"), Is.GreaterThan(woodBefore), "the wood went to the core instead");
        }

        [Test]
        public void AStorageStillFeedsTheCoreWithTheExistingRule()
        {
            var match = new Match(Scenario(false));
            AdvanceToMetallurgy(match);
            int core = CellOf(match.Scenario.Cores[0].Position);
            var ore = match.Scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Ore)
                .OrderBy(n => Math.Abs(CellOf(n.Position) % Width - core % Width)
                    + Math.Abs(CellOf(n.Position) / Width - core / Width)).First();
            var storage = Place(match, BuildingKind.Storage, Facing.North, Ring(CellOf(ore.Position), 2, 10));
            Assume.That(storage.id, Is.Not.EqualTo(0u));
            Build(match, storage.id);
            int output = OutputCell(storage.origin, match.Scenario.Economy.StorageSizeCells, Facing.North);
            var route = Route(match.Scenario, output, cell => InsideCore(match.Scenario, cell),
                new HashSet<int>(Footprint(storage.origin, match.Scenario.Economy.StorageSizeCells)));
            Assume.That(route.cells, Is.Not.Null);
            match.Send(EconomyCommand.PlaceBelt(1, ++match.Sequence, route.cells, route.facings));
            match.Send(EconomyCommand.Assign(1, ++match.Sequence, new uint[] { 1 }, EconomyTargetKind.ResourceNode, ore.Id));
            match.Steps(4000);
            Assert.That(Number(match.Fields(), "Economy[1].Ore"), Is.GreaterThan(0));
        }

        [Test]
        public void StorageFeedsProcessingExtensionAndReplayAreDeterministic()
        {
            var scenario = Scenario(true);
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.StorageFeedsProcessing, Is.True);
            Assert.That(decoded.Extensions.Single(x => x.Id == 46).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 240, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }

            var left = new Battle(Scenario(false));
            var right = new Battle(Scenario(false));
            for (long tick = 1; tick <= 400; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
        }
    }
}
