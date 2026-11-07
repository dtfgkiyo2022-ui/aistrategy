using System;
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
    public sealed class FarmHaulIdleTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void AutomaticFarmHaulerGathersInsteadOfWaitingAtTheFarm()
        {
            var started = StartFarm(true);
            var world = World(started.simulation);
            var farm = world.Buildings.Single(b => b.Alive && b.Kind == BuildingKind.Farm);
            farm.Complete = true;
            farm.Output = 0;
            world.Buildings[(int)farm.Id - 1] = farm;
            ref var villager = ref PrepareHauler(world, farm.Id);

            InvokeHaul(started.simulation, ref villager);
            Assert.That(villager.Task, Is.EqualTo(VillagerTask.ToNode));
            Assert.That(villager.HaulFrom, Is.EqualTo(farm.Id));
            uint haulerId = villager.Id;

            int waitingAtFarm = 0;
            for (long tick = 0; tick < 800; tick++)
            {
                world.Tick = tick;
                Invoke(started.simulation, "AdvanceIndustry");
                Invoke(started.simulation, "MoveVillagers");
                Invoke(started.simulation, "AdvanceEconomyVillagers", (uint)world.VillagerCount);
                villager = world.Villagers.First(v => v.Id == haulerId);
                var currentFarm = world.Buildings[(int)farm.Id - 1];
                if (villager.Task == VillagerTask.ToPickup && villager.Position.Equals(world.Map.Center(currentFarm.WorkCell))
                    && !villager.IsMoving) waitingAtFarm++;
            }

            Assert.That(waitingAtFarm, Is.EqualTo(0));
        }

        [Test]
        public void AutomaticFarmHaulerPicksUpTenAndDeliversFoodToTheCore()
        {
            var started = StartFarm(true);
            var world = World(started.simulation);
            var farm = world.Buildings.Single(b => b.Alive && b.Kind == BuildingKind.Farm);
            farm.Complete = true;
            farm.Output = 10;
            world.Buildings[(int)farm.Id - 1] = farm;
            ref var villager = ref PrepareHauler(world, farm.Id);
            int before = world.Economies[0].Food;

            world.Tick = 0;
            InvokeHaul(started.simulation, ref villager);
            Assert.That(villager.Carry, Is.EqualTo(10));
            Assert.That(villager.Task, Is.EqualTo(VillagerTask.ToDropOff));

            for (long tick = 1; tick < 1600 && world.Economies[0].Food == before; tick++)
            {
                world.Tick = tick;
                Invoke(started.simulation, "MoveVillagers");
                Invoke(started.simulation, "AdvanceEconomyVillagers", (uint)world.VillagerCount);
            }

            Assert.That(world.Economies[0].Food, Is.GreaterThan(before));
        }

        [Test]
        public void FarmBufferLimitIsThirtyOnlyWhenTheFlagIsOn()
        {
            var on = StartFarm(true);
            var off = StartFarm(false);
            Assert.That(AdvanceFarmProduction(on.simulation, 3000), Is.EqualTo(30));
            Assert.That(AdvanceFarmProduction(off.simulation, 3000), Is.EqualTo(10));
        }

        [Test]
        public void HeldFarmHaulerStillWaitsAtAnEmptyFarm()
        {
            var started = StartFarm(true);
            var world = World(started.simulation);
            var farm = world.Buildings.Single(b => b.Alive && b.Kind == BuildingKind.Farm);
            farm.Complete = true;
            farm.Output = 0;
            world.Buildings[(int)farm.Id - 1] = farm;
            ref var villager = ref PrepareHauler(world, farm.Id);
            villager.Held = true;

            for (long tick = 0; tick < 500; tick++)
            {
                world.Tick = tick;
                InvokeHaul(started.simulation, ref villager);
            }

            Assert.That(villager.Task, Is.EqualTo(VillagerTask.ToPickup));
            Assert.That(villager.Carry, Is.EqualTo(0));
            Assert.That(villager.HaulFrom, Is.EqualTo(farm.Id));
        }

        [Test]
        public void ConnectedFarmHasNoAutomaticHauler()
        {
            var started = StartFarm(true);
            var world = World(started.simulation);
            var farm = world.Buildings.Single(b => b.Alive && b.Kind == BuildingKind.Farm);
            farm.Complete = true;
            farm.Held = false;
            world.Buildings[(int)farm.Id - 1] = farm;
            Invoke(started.simulation, "DecideFarms", 1U);
            Invoke(started.simulation, "DecideFarms", 1U);

            Assert.That(started.simulation.ConnectedFarmCount(1), Is.EqualTo(1));
            Assert.That(world.Villagers.Any(v => v.Alive && v.FactionId == 1 && !v.Held && v.HaulFrom == farm.Id), Is.False);
        }

        [Test]
        public void HumanFarmHaulerIsNotRemoved()
        {
            var started = StartFarm(false);
            var world = World(started.simulation);
            var farm = world.Buildings.Single(b => b.Alive && b.Kind == BuildingKind.Farm);
            farm.Complete = true;
            world.Buildings[(int)farm.Id - 1] = farm;
            int index = Array.FindIndex(world.Villagers, v => v.Alive && v.FactionId == 1);
            if (index < 0) Assert.Fail("開始時の村人が見つかりません");
            ref var villager = ref world.Villagers[index];
            villager.Held = true;
            villager.HaulFrom = farm.Id;
            villager.Task = VillagerTask.ToPickup;
            Invoke(started.simulation, "SetFarmHauler", 1U, farm.Id, 0);

            Assert.That(world.Villagers[index].Held, Is.True);
            Assert.That(world.Villagers[index].HaulFrom, Is.EqualTo(farm.Id));
            Assert.That(world.Villagers[index].Task, Is.EqualTo(VillagerTask.ToPickup));
        }

        [Test]
        public void FarmHaulExtensionRoundTripsEnabledAndBufferLimitV2()
        {
            var scenario = Scenario(true);
            scenario.Economy.FarmHaulBufferLimit = 37;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(e => e.Id == 99);

            Assert.That(extension.Version, Is.EqualTo(2));
            Assert.That(decoded.Economy.FarmHaulIdle, Is.True);
            Assert.That(decoded.Economy.FarmHaulBufferLimit, Is.EqualTo(37));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        private static int AdvanceFarmProduction(Battle simulation, int ticks)
        {
            var world = World(simulation);
            var farm = world.Buildings.Single(b => b.Alive && b.Kind == BuildingKind.Farm);
            farm.Complete = true;
            world.Buildings[(int)farm.Id - 1] = farm;
            for (long tick = 0; tick < ticks; tick++)
            {
                world.Tick = tick;
                Invoke(simulation, "AdvanceIndustry");
            }
            return world.Buildings[(int)farm.Id - 1].Output;
        }

        private static (ScenarioDefinition scenario, Battle simulation, CommandGateway gateway) StartFarm(bool idle)
        {
            var scenario = Scenario(idle);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            PlaceFirstFarm(scenario, simulation, gateway);
            return (scenario, simulation, gateway);
        }

        private static ScenarioDefinition Scenario(bool idle)
        {
            var scenario = MapGenerator.GenerateTerrain(1, processingChain: true);
            var e = scenario.Economy;
            e.FarmHaulIdle = idle;
            e.FarmHaulBufferLimit = 30;
            e.OpenFarms = true;
            e.OpenFarmsPrimitiveAiFarms = 1;
            e.OpenFarmsOtherCivAiFarms = 1;
            e.AiBelts = true;
            e.AiExpand = false;
            e.AiFactory = false;
            e.AutoVillagerTarget = 0;
            e.AutoInfantryQueue = 0;
            e.StartFood = e.StartWood = e.StartStone = e.StartMetal = 50000;
            e.FarmWoodCost = 0;
            e.BeltWoodCost = 0;
            e.BeltLimit = 8192;
            return scenario;
        }

        private static void PlaceFirstFarm(ScenarioDefinition scenario, Battle simulation, CommandGateway gateway)
        {
            int width = scenario.Map.WidthCells;
            int core = (int)(scenario.Cores[0].Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * width
                + (int)(scenario.Cores[0].Position.X.Raw / 65536 / scenario.Map.CellSizeMeters);
            int cx = core % width, cz = core / width;
            for (int r = 5; r <= 14; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x = cx + dx - 1, z = cz + dz - 1;
                        if (x < 0 || z < 0 || x + 2 > width || z + 2 > scenario.Map.HeightCells) continue;
                        int origin = z * width + x;
                        int before = simulation.Capture(1).Economy.Buildings.Count;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, 1, BuildingKind.Farm, origin, Facing.North));
                        gateway.Step();
                        if (simulation.Capture(1).Economy.Buildings.Count > before) return;
                    }
            Assert.Fail("農場を置ける場所が見つかりません");
        }

        private static WorldState World(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(simulation);

        private static ref VillagerState PrepareHauler(WorldState world, uint farmId)
        {
            int index = Array.FindIndex(world.Villagers, v => v.Alive && v.FactionId == 1);
            if (index < 0) Assert.Fail("開始時の村人が見つかりません");
            ref var villager = ref world.Villagers[index];
            villager.Held = false;
            villager.HaulFrom = farmId;
            villager.HaulTo = 0;
            villager.HaulNodeId = 0;
            villager.HaulKind = 0;
            villager.Carry = 0;
            villager.Task = VillagerTask.ToPickup;
            villager.Position = world.Map.Center(world.Buildings[(int)farmId - 1].WorkCell);
            return ref villager;
        }

        private static void Invoke(Battle simulation, string name, params object[] arguments)
            => typeof(Battle).GetMethod(name, Hidden).Invoke(simulation, arguments);

        private static void InvokeHaul(Battle simulation, ref VillagerState villager)
        {
            var arguments = new object[] { villager };
            typeof(Battle).GetMethod("Haul", Hidden).Invoke(simulation, arguments);
            villager = (VillagerState)arguments[0];
        }
    }
}
