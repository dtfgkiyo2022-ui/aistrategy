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
    public sealed class CarriedInputsTests
    {
        private const int Width = 128, Height = 64;

        [Test]
        public void CarriedInputsExtension88RoundTripsItsFlagAndOutletLimit()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            scenario.Economy.CarriedInputs = true;
            scenario.Economy.CarriedInputsOutletBuildingLimit = 3;
            scenario.Economy.CarriedHaulerCap = 7;
            scenario.Economy.CarriedPlankReserve = 13;
            scenario.Economy.CarriedSawmillRoutePlanks = 41;
            scenario.Economy.CarriedBeltMaxCells = 33;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(e => e.Id == 88).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CarriedInputs, Is.True);
            Assert.That(decoded.Economy.CarriedInputsOutletBuildingLimit, Is.EqualTo(3));
            Assert.That(decoded.Economy.CarriedHaulerCap, Is.EqualTo(7));
            Assert.That(decoded.Economy.CarriedPlankReserve, Is.EqualTo(13));
            Assert.That(decoded.Economy.CarriedSawmillRoutePlanks, Is.EqualTo(41));
            Assert.That(decoded.Economy.CarriedBeltMaxCells, Is.EqualTo(33));
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
        public void CarriedInputsDisconnectedSawmillReceivesWoodAndMakesPlanks()
        {
            var withoutDelivery = StartSawmill(false);
            var withoutWorld = World(withoutDelivery.sim);
            int haulerIndex = Array.FindIndex(withoutWorld.Villagers, v => v.Alive && v.FactionId == 1);
            Assert.That(haulerIndex, Is.GreaterThanOrEqualTo(0));
            ref var hauler = ref withoutWorld.Villagers[haulerIndex];
            hauler.HaulFrom = 0;
            hauler.HaulTo = withoutDelivery.id;
            hauler.HaulKind = ResourceKind.Wood;
            hauler.Task = VillagerTask.ToPickup;
            hauler.Position = withoutWorld.Cores[0].Definition.Position;
            InvokeHaul(withoutDelivery.sim, ref hauler);
            hauler.Position = withoutWorld.Map.Center(withoutWorld.Buildings[(int)withoutDelivery.id - 1].WorkCell);
            InvokeHaul(withoutDelivery.sim, ref hauler);
            Step(withoutDelivery.gateway, withoutDelivery.sim, 120);
            var withoutFrame = withoutDelivery.sim.Capture(1).Economy.Buildings.Single(b => b.Id == withoutDelivery.id);
            Assert.That(withoutFrame.Output, Is.GreaterThan(0),
                "input=" + withoutFrame.Input + " output=" + withoutFrame.Output
                + " routes=" + withoutDelivery.sim.CarriedSupplyRouteCount(1)
                + " haulers=" + withoutDelivery.sim.CarriedMaterialHaulerCount(1));

            var withDelivery = StartSawmill(true);
            Step(withDelivery.gateway, withDelivery.sim, 120);
            Assert.That(withDelivery.sim.Capture(1).Economy.Buildings.Single(b => b.Id == withDelivery.id).Output,
                Is.GreaterThan(0));
        }

        [Test]
        public void CarriedInputsAssignsThreeUnreachableGroupsWithoutExceedingCap()
        {
            var simulation = NewUnconnectedSawmillSimulation(3);
            var world = World(simulation);
            Invoke(simulation, "DecideCarriedSupplyRoutes", 1U);

            var haulers = world.Villagers.Where(v => v.Alive && v.FactionId == 1 && v.HaulKind == ResourceKind.Wood).ToArray();
            Assert.That(haulers.Length, Is.EqualTo(3));
            Assert.That(haulers.Select(v => v.HaulTo).Distinct().Count(), Is.EqualTo(3));
            Assert.That(haulers.Length, Is.LessThanOrEqualTo(world.Config.Economy.CarriedHaulerCap));
        }

        [Test]
        public void CarriedInputsDoesNotSearchRouteWhenBoardsCannotPay()
        {
            var simulation = NewAssemblerSimulation(aiInserterParts: true, planks: 0);
            var world = World(simulation);
            Invoke(simulation, "DecideCarriedSupplyRoutes", 1U);

            Assert.That(world.CarriedSupplyRoutes, Is.Empty);
            Assert.That(simulation.CarriedSupplyUnaffordableCount(1), Is.GreaterThan(0));
            Assert.That(simulation.CarriedSupplyFailedCount(1), Is.GreaterThanOrEqualTo(0));
            Assert.That(world.Villagers.Any(v => v.Alive && v.FactionId == 1 && v.HaulTo == 1
                && v.HaulKind == ResourceKind.Planks), Is.True);
        }

        [Test]
        public void CarriedInputsFailedGroupWaitsSixHundredTicksBeforeRetry()
        {
            var simulation = NewAssemblerSimulation(aiInserterParts: true, planks: 10000);
            var world = World(simulation);
            world.CarriedSupplyRoutes = new[]
            {
                new CarriedSupplyRouteState
                {
                    BuildingId = 1, Material = ResourceKind.Planks, Failed = true,
                    NextAttemptTick = world.Tick + 600, BeltCells = Array.Empty<int>(), BeltFacings = Array.Empty<Facing>()
                }
            };
            Invoke(simulation, "DecideCarriedSupplyRoutes", 1U);

            var failed = world.CarriedSupplyRoutes.Single(route => route.Material == ResourceKind.Planks);
            Assert.That(failed.Failed, Is.True, "tick=" + world.Tick);
            Assert.That(failed.NextAttemptTick, Is.EqualTo(world.Tick + 600), "tick=" + world.Tick);
            Assert.That(simulation.CarriedSupplyFailedCount(1), Is.GreaterThanOrEqualTo(0));
            Assert.That(world.Villagers.Any(v => v.Alive && v.FactionId == 1 && v.HaulTo == 1
                && v.HaulKind == ResourceKind.Planks), Is.True);
        }

        [Test]
        public void CarriedInputsAssignsAHandHaulerWhenAnAssemblerRouteCannotBeLaid()
        {
            var simulation = NewAssemblerSimulation(aiInserterParts: false, planks: 10000);
            var world = World(simulation);
            Invoke(simulation, "DecideCarriedSupplyRoutes", 1U);

            Assert.That(world.Villagers.Any(v => v.Alive && v.FactionId == 1 && v.HaulTo == 1
                && v.HaulKind == ResourceKind.Planks), Is.True);
            Assert.That(world.CarriedSupplyRoutes, Is.Empty);
        }

        [Test]
        public void CarriedInputsHandHaulerLeavesCoreReserveAndDeliversMaterial()
        {
            var simulation = NewAssemblerSimulation(aiInserterParts: false, planks: 200);
            var world = World(simulation);
            int villagerIndex = Array.FindIndex(world.Villagers, v => v.Alive && v.FactionId == 1);
            Assert.That(villagerIndex, Is.GreaterThanOrEqualTo(0));

            ref var villager = ref world.Villagers[villagerIndex];
            villager.Task = VillagerTask.ToPickup;
            villager.HaulFrom = 0;
            villager.HaulTo = 1;
            villager.HaulKind = ResourceKind.Planks;
            villager.Position = world.Cores[0].Definition.Position;
            InvokeHaul(simulation, ref villager);

            Assert.That(world.Economies[0].Planks, Is.GreaterThanOrEqualTo(100));
            Assert.That(world.Economies[0].Planks, Is.LessThan(200));
            Assert.That(villager.CarryKind, Is.EqualTo(ResourceKind.Planks));
            Assert.That(villager.Carry, Is.GreaterThan(0));

            villager.Task = VillagerTask.ToDeliver;
            villager.Position = world.Map.Center(world.Buildings[0].WorkCell);
            InvokeHaul(simulation, ref villager);

            Assert.That(world.Buildings[0].Input, Is.GreaterThan(0));
        }

        private static Battle NewAssemblerSimulation(bool aiInserterParts, int planks)
        {
            var scenario = LiveGameRules.Create(6001, true, true, false, false);
            scenario.Economy.CarriedInputs = true;
            scenario.Economy.AiInserterParts = aiInserterParts;
            scenario.Economy.AiBeltParts = aiInserterParts;
            scenario.Economy.BeltCraft = aiInserterParts;
            scenario.Economy.Inserters = true;
            scenario.Economy.BeltComponents = true;
            scenario.Economy.CraftTree2 = true;
            scenario.Economy.CraftRequired = true;
            var simulation = new Battle(scenario);
            var world = World(simulation);
            int coreCell = CellOf(world.Cores[0].Definition.Position);
            int origin = -1;
            int size = world.Config.Economy.AssemblerSizeCells;
            for (int candidate = coreCell + 12; candidate < world.Belts.Length - size * Width; candidate++)
            {
                bool open = true;
                for (int z = 0; z < size && open; z++) for (int x = 0; x < size; x++)
                {
                    int cell = candidate + z * Width + x;
                    if (!world.Map.IsPassable(cell) || world.Nodes.Any(n => CellOf(n.Definition.Position) == cell)) open = false;
                }
                if (open) { origin = candidate; break; }
            }
            Assert.That(origin, Is.GreaterThan(0));
            world.Buildings = new[]
            {
                new BuildingState
                {
                    Id = 1, FactionId = 1, Kind = BuildingKind.Assembler,
                    OriginCell = origin, WorkCell = origin - 1, Alive = true, Complete = true,
                    Recipe = RecipeKind.Scholarly
                }
            };
            world.NextBuildingId = 2;
            world.ProcessingLines = new[]
            {
                new ProcessingLineState { Id = 1, FactionId = 1, Kind = ProcessingLineKind.Assembler,
                    Manager = LineManager.Automatic, AssemblerId = 1, BeltCells = Array.Empty<int>(),
                    BeltFacings = Array.Empty<Facing>() }
            };
            ref var economy = ref world.Economies[0];
            economy.Age = 2;
            economy.Planks = planks;
            for (int i = 0; i < world.Villagers.Length; i++)
            {
                ref var villager = ref world.Villagers[i];
                if (!villager.Alive || villager.FactionId != 1) continue;
                villager.Task = VillagerTask.Idle;
                villager.Held = false;
                villager.HaulFrom = 0;
                villager.HaulTo = 0;
                villager.HaulKind = 0;
                villager.Position = world.Cores[0].Definition.Position;
            }
            return simulation;
        }

        private static Battle NewUnconnectedSawmillSimulation(int cap)
        {
            var simulation = NewAssemblerSimulation(aiInserterParts: false, planks: 0);
            var world = World(simulation);
            world.Config.Economy.CarriedHaulerCap = cap;
            world.Economies[0].Wood = 0;
            int origin = world.Buildings[0].OriginCell;
            world.Buildings = Enumerable.Range(0, 3).Select(i => new BuildingState
            {
                Id = (uint)(i + 1), FactionId = 1, Kind = BuildingKind.Sawmill,
                OriginCell = origin + i * 8, WorkCell = origin + i * 8 - 1, Alive = true, Complete = true
            }).ToArray();
            world.NextBuildingId = 4;
            world.ProcessingLines = Enumerable.Range(0, 3).Select(i => new ProcessingLineState
            {
                Id = (uint)(i + 1), FactionId = 1, Kind = ProcessingLineKind.Planks,
                Manager = LineManager.Automatic, SawmillId = (uint)(i + 1),
                BeltCells = Array.Empty<int>(), BeltFacings = Array.Empty<Facing>()
            }).ToArray();
            return simulation;
        }

        private static WorldState World(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private static void Invoke(Battle simulation, string name, uint faction)
            => typeof(Battle).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(simulation, new object[] { faction });

        private static void InvokeHaul(Battle simulation, ref VillagerState villager)
        {
            object boxed = villager;
            var arguments = new[] { boxed };
            typeof(Battle).GetMethod("Haul", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(simulation, arguments);
            villager = (VillagerState)arguments[0];
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
            e.Inserters = true;
            e.BeltComponents = true;
            e.Ages = true;
            e.Planks = true;
            e.PrimitiveCraft = false;
            e.CraftTree1 = true;
            e.CraftTree2 = true;
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
            var liveWorld = World(sim);
            if (!liveWorld.ProcessingLines.Any(line => line.SawmillId == building.Id && line.Manager == LineManager.Automatic))
            {
                Array.Resize(ref liveWorld.ProcessingLines, liveWorld.ProcessingLines.Length + 1);
                liveWorld.ProcessingLines[liveWorld.ProcessingLines.Length - 1] = new ProcessingLineState
                {
                    Id = liveWorld.NextProcessingLineId++, FactionId = 1, Kind = ProcessingLineKind.Planks,
                    Manager = LineManager.Automatic, SawmillId = building.Id,
                    BeltCells = Array.Empty<int>(), BeltFacings = Array.Empty<Facing>()
                };
            }
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
