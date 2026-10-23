using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class InserterTests
    {
        private const int Width = 128, Height = 64;

        private static ScenarioDefinition Scenario(bool enabled = true)
        {
            var s = LiveGameRules.Create(1, false, enabled, false, false);
            s.Economy.StartFood = 10000;
            s.Economy.StartWood = 250;
            s.Economy.StartStone = 10000;
            s.Economy.InserterWoodCost = 0;
            s.Economy.InserterIntervalTicks = 10;
            s.Economy.AiInserterCoreReserve = 100;
            s.Economy.BeltTicksPerCell = 100;
            s.Economy.AutoVillagerTarget = 0;
            return s;
        }

        private static int CellOf(SimPoint point)
            => (int)(point.Z.Raw / 65536 / 2) * Width + (int)(point.X.Raw / 65536 / 2);

        private static bool InsideCore(ScenarioDefinition s, int cell)
        {
            long x = (cell % Width) * 2 + 1, z = (cell / Width) * 2 + 1;
            foreach (var core in s.Cores)
            {
                long dx = x - core.Position.X.Raw / 65536, dz = z - core.Position.Z.Raw / 65536;
                if (dx * dx + dz * dz <= 16) return true;
            }
            return false;
        }

        private static bool Open(ScenarioDefinition s, int cell)
        {
            if (cell < 0 || cell >= Width * Height || s.Map.BlockedCellIds.Contains(cell)) return false;
            if (s.Map.Terrain != null && s.Map.Terrain.Length == Width * Height
                && (s.Map.Terrain[cell] == (byte)TerrainKind.River || s.Map.Terrain[cell] == (byte)TerrainKind.Mountain)) return false;
            return !s.ResourceNodes.Any(n => CellOf(n.Position) == cell);
        }

        private static int Next(int cell, Facing facing)
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

        private static (int rear, int inserter, int front) FreeLine(ScenarioDefinition s, Facing facing = Facing.East)
        {
            for (int z = 6; z < Height - 6; z++)
                for (int x = 6; x < Width - 6; x++)
                {
                    int inserter = z * Width + x;
                    int rear = Next(inserter, (Facing)(((int)facing + 2) % 4));
                    int front = Next(inserter, facing);
                    if (Open(s, rear) && Open(s, inserter) && Open(s, front)
                        && !InsideCore(s, rear) && !InsideCore(s, inserter) && !InsideCore(s, front))
                        return (rear, inserter, front);
                }
            throw new InvalidOperationException("free inserter line not found");
        }

        private static (int rear, int inserter, int front) CoreLine(ScenarioDefinition s)
        {
            int core = CellOf(s.Cores[0].Position);
            for (int radius = 1; radius < 5; radius++)
                foreach (Facing facing in new[] { Facing.East, Facing.North, Facing.West, Facing.South })
                {
                    int rear = core;
                    for (int i = 0; i < radius; i++) rear = Next(rear, facing);
                    int inserter = Next(rear, facing), front = Next(inserter, facing);
                    if (rear >= 0 && InsideCore(s, rear) && inserter >= 0 && front >= 0
                        && Open(s, inserter) && Open(s, front) && !InsideCore(s, inserter) && !InsideCore(s, front))
                        return (rear, inserter, front);
                }
            throw new InvalidOperationException("core-adjacent inserter line not found");
        }

        private static void Quiet(CommandGateway gateway)
        {
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            gateway.Step();
        }

        private static void Steps(CommandGateway gateway, int count)
        {
            for (int i = 0; i < count; i++) gateway.Step();
        }

        private static BeltView Belt(Battle sim, int cell)
            => sim.Capture(1).Economy.Belts.First(b => b.Cell == cell);

        private static IEnumerable<int> Origins(int cell)
        {
            yield return cell;
            yield return cell - 1;
            yield return cell - Width;
            yield return cell - Width - 1;
        }

        [Test]
        public void BuildingOutputOnTheNonOutputSideFeedsABelt()
        {
            var s = Scenario();
            s.Economy.DrillIntervalTicks = 10;
            var node = s.ResourceNodes.First(n => n.Kind == ResourceKind.Wood);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Quiet(gateway);
            uint drill = 0;
            int origin = -1;
            ulong sequence = 10;
            foreach (int candidate in Origins(CellOf(node.Position)))
            {
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Drill, candidate, Facing.East));
                gateway.Step();
                var placed = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Drill);
                if (placed.Id != 0) { drill = placed.Id; origin = candidate; break; }
            }
            Assume.That(drill, Is.Not.EqualTo(0u), "a drill site");
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { 1u, 2u, 3u }, EconomyTargetKind.Building, drill));
            for (int i = 0; i < 3000 && !sim.Capture(1).Economy.Buildings.First(b => b.Id == drill).Complete; i++) gateway.Step();
            Assume.That(sim.Capture(1).Economy.Buildings.First(b => b.Id == drill).Complete, Is.True);
            Steps(gateway, 20);

            int size = s.Economy.DrillSizeCells;
            int x0 = origin % Width, z0 = origin / Width;
            int inserter = (z0 + size / 2) * Width + x0 - 1;
            int belt = inserter - 1;
            gateway.SubmitEconomy(EconomyCommand.PlaceInserter(1, ++sequence, inserter, Facing.West, 0));
            gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, ++sequence, new[] { belt }, new[] { Facing.West }));
            Steps(gateway, 10);
            Assert.That(Belt(sim, belt).Item, Is.EqualTo(ResourceKind.Wood));
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, uint building, int origin, int inserter, int rear) SawmillLine(ResourceKind item, bool coreSource)
        {
            var s = Scenario();
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.PlankTicks = 1;
            s.Economy.PlankWoodInput = 1;
            s.Economy.CraftTree1 = false;
            s.Economy.PrimitiveCraft = false; s.Economy.AiFactory = false;
            s.Economy.CraftTree2 = false;
            s.Economy.CraftTree3 = false;
            int core = CellOf(s.Cores[0].Position);
            int size = s.Economy.SawmillSizeCells;
            int chosen = -1, inserter = -1, rear = -1;
            for (int x = core % Width + 5; x < core % Width + 30 && chosen < 0; x++)
                for (int z = Math.Max(5, core / Width - 8); z < Math.Min(Height - size - 3, core / Width + 8); z++)
                {
                    int origin = z * Width + x;
                    int candidateInserter = origin - (coreSource ? 2 : 1) + Width;
                    int candidateRear = candidateInserter - 1;
                    int candidateFront = candidateInserter + 1;
                    bool footprint = true;
                    for (int dz = 0; dz < size; dz++) for (int dx = 0; dx < size; dx++)
                        footprint &= Open(s, origin + dz * Width + dx) && !InsideCore(s, origin + dz * Width + dx);
                    bool line = Open(s, candidateInserter) && Open(s, candidateRear) && !InsideCore(s, candidateInserter)
                        && (!coreSource || (InsideCore(s, candidateRear) && Open(s, candidateFront) && !InsideCore(s, candidateFront)));
                    if (footprint && line) { chosen = origin; inserter = candidateInserter; rear = candidateRear; }
            }
            Assume.That(chosen, Is.GreaterThan(0), "a sawmill site");
            var belts = new List<BeltDefinition>
            {
                new BeltDefinition { Cell = inserter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Inserter, SorterKind = coreSource ? ResourceKind.Wood : 0 }
            };
            if (!coreSource) belts.Add(new BeltDefinition { Cell = rear, FactionId = 1, Facing = Facing.East, Item = item });
            else belts.Add(new BeltDefinition { Cell = inserter + 1, FactionId = 1, Facing = Facing.East });
            s.Belts = belts.ToArray();
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Quiet(gateway);
            ulong sequence = 10;
            gateway.SubmitEconomy(EconomyCommand.AdvanceNow(1, ++sequence, CivKind.Bridge));
            gateway.Step();
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Sawmill, chosen, Facing.East));
            gateway.Step();
            var view = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Sawmill);
            Assume.That(view.Id, Is.Not.EqualTo(0u), "a sawmill placement");
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { 1u, 2u, 3u }, EconomyTargetKind.Building, view.Id));
            for (int i = 0; i < 3000 && !sim.Capture(1).Economy.Buildings.First(b => b.Id == view.Id).Complete; i++) gateway.Step();
            Assume.That(sim.Capture(1).Economy.Buildings.First(b => b.Id == view.Id).Complete, Is.True);
            return (s, sim, gateway, view.Id, chosen, inserter, rear);
        }

        [Test]
        public void RejectedBuildingInputStaysOnTheRearBelt()
        {
            var rejected = SawmillLine(ResourceKind.Ore, false);
            Steps(rejected.gateway, 12);
            Assert.That(Belt(rejected.sim, rejected.rear).Item, Is.EqualTo(ResourceKind.Ore));
        }

        [Test]
        public void AcceptedBuildingInputReachesSawmillAndProducesPlanks()
        {
            var accepted = SawmillLine(ResourceKind.Wood, false);
            Steps(accepted.gateway, 20);
            var building = accepted.sim.Capture(1).Economy.Buildings.First(b => b.Id == accepted.building);
            Assert.That(Belt(accepted.sim, accepted.rear).Item, Is.EqualTo((ResourceKind)0));
            Assert.That(building.Output, Is.GreaterThan(0));
        }

        [Test]
        public void CoreWoodThroughInserterAndBeltProducesPlanks()
        {
            var line = SawmillLine(ResourceKind.Wood, true);
            Steps(line.gateway, 220);
            var building = line.sim.Capture(1).Economy.Buildings.First(b => b.Id == line.building);
            Assert.That(building.Output, Is.GreaterThan(0));
        }

        [Test]
        public void CoreSelectorFeedsABeltAndNoSelectorDoesNotPickCoreStock()
        {
            var s = Scenario();
            var line = CoreLine(s);
            s.Belts = new[]
            {
                new BeltDefinition { Cell = line.inserter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Inserter, SorterKind = ResourceKind.Wood },
                new BeltDefinition { Cell = line.front, FactionId = 1, Facing = Facing.East }
            };
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Quiet(gateway);
            int woodBefore = sim.Capture(1).Economy.Wood;
            Steps(gateway, 10);
            Assert.That(Belt(sim, line.front).Item, Is.EqualTo(ResourceKind.Wood));
            Assert.That(sim.Capture(1).Economy.Wood, Is.EqualTo(woodBefore - 1));

            var noSelector = Scenario();
            noSelector.Belts = new[]
            {
                new BeltDefinition { Cell = line.inserter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Inserter },
                new BeltDefinition { Cell = line.front, FactionId = 1, Facing = Facing.East }
            };
            var noSelectorSim = new Battle(noSelector);
            var noSelectorGateway = new CommandGateway(noSelectorSim);
            Quiet(noSelectorGateway);
            int unchanged = noSelectorSim.Capture(1).Economy.Wood;
            Steps(noSelectorGateway, 20);
            Assert.That(Belt(noSelectorSim, line.front).Item, Is.EqualTo((ResourceKind)0));
            Assert.That(noSelectorSim.Capture(1).Economy.Wood, Is.EqualTo(unchanged));
        }

        [Test]
        public void InserterTransfersExactlyOnEveryTenthTick()
        {
            var s = Scenario();
            var line = FreeLine(s);
            s.Belts = new[]
            {
                new BeltDefinition { Cell = line.rear, FactionId = 1, Facing = Facing.East, Item = ResourceKind.Wood },
                new BeltDefinition { Cell = line.inserter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Inserter },
                new BeltDefinition { Cell = line.front, FactionId = 1, Facing = Facing.East }
            };
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Quiet(gateway);
            Steps(gateway, 8);
            Assert.That(Belt(sim, line.front).Item, Is.EqualTo((ResourceKind)0));
            gateway.Step();
            Assert.That(Belt(sim, line.front).Item, Is.EqualTo(ResourceKind.Wood));
        }

        [Test]
        public void InserterCommandIsRejectedWhenTheFlagIsOffAndTheOldMatchIsUnchanged()
        {
            var baseline = Scenario(false);
            var command = Scenario(false);
            var left = new Battle(baseline);
            var right = new Battle(command);
            var leftGateway = new CommandGateway(left);
            var rightGateway = new CommandGateway(right);
            var line = FreeLine(command);
            leftGateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            leftGateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            rightGateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            rightGateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            leftGateway.SubmitEconomy(EconomyCommand.PlaceInserter(1, 3, line.inserter, Facing.East, ResourceKind.Wood));
            rightGateway.SubmitEconomy(EconomyCommand.PlaceInserter(1, 3, line.inserter, Facing.East, ResourceKind.Wood));
            for (int tick = 0; tick < 30000; tick++) { leftGateway.Step(); rightGateway.Step(); }
            Assert.That(right.Capture(1).Economy.Belts.Any(b => b.Cell == line.inserter), Is.False);
            Assert.That(right.CaptureDiagnostic().CanonicalState, Is.EqualTo(left.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void InserterExtensionAndReplayRoundTrip()
        {
            var s = Scenario();
            s.Economy.InserterWoodCost = 15;
            s.Economy.InserterIntervalTicks = 10;
            s.Economy.AiInserterCoreReserve = 100;
            var bytes = ScenarioBinary.Encode(s);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(e => e.Id == 48).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.Inserters, Is.True);
            Assert.That(decoded.Economy.InserterWoodCost, Is.EqualTo(15));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var line = FreeLine(s);
            s.Belts = new[] { new BeltDefinition { Cell = line.inserter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Inserter } };
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Quiet(gateway);
            gateway.SubmitEconomy(EconomyCommand.PlaceInserter(1, 3, line.front, Facing.East, ResourceKind.Wood));
            Steps(gateway, 20);
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, s, gateway.Inputs, sim.Capture(1).Tick, new BuildIdentity());
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void InserterSelectionDescriptionIsAvailable()
        {
            Assert.That(SelectionInfoText.BeltComponentDescription(BeltComponentKind.Inserter), Does.Contain("feeds"));
        }
    }
}
