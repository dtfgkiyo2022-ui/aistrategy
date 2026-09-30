using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>V3-9 #1: the first caravan/commerce slice. These tests deliberately do not tune its economy.</summary>
    public sealed class CaravanTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Caravan = true;
            s.Economy.StartFood = 100000; s.Economy.StartWood = 100000;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.MarketWoodCost = 0; s.Economy.MarketWork = 1;
            s.Economy.CaravanseraiWoodCost = 0; s.Economy.CaravanseraiWork = 1;
            s.Economy.CaravanOutpostReach = 24; s.Economy.CaravanMinimumDistance = 10;
            s.Economy.VillagerSpeed = Fix64.FromInt(16); s.Economy.AutoVillagerTarget = 0;
            s.Economy.Age2FoodCost = 1000000; s.Economy.Age2WoodCost = 1000000;
            s.Cores[0].Hp = 1000000; s.Cores[1].Hp = 1000000;
            s.Outposts[0].OwnerFactionId = 1;
            s.Outposts[1].OwnerFactionId = 2;
            return s;
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count; i++) gateway.Step();
        }

        private static int Cell(ScenarioDefinition s, SimPoint p)
            => (int)(p.Z.Raw / 65536 / s.Map.CellSizeMeters) * s.Map.WidthCells
                + (int)(p.X.Raw / 65536 / s.Map.CellSizeMeters);

        private static void EnterCaravan(CommandGateway gateway, Battle sim, ref ulong sequence)
        {
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Caravan));
            Steps(gateway, sim, 4);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Caravan));
        }

        private static uint FindBuilding(Battle sim, BuildingKind kind, uint faction = 1)
            => sim.Capture(faction).Economy.Buildings.FirstOrDefault(b => b.Kind == kind && b.FactionId == faction).Id;

        private static uint PlaceMarket(Battle sim, CommandGateway gateway, ScenarioDefinition s, ref ulong sequence)
        {
            int width = s.Map.WidthCells;
            int core = Cell(s, s.Cores[0].Position);
            int cx = core % width, cz = core / width;
            for (int r = 5; r <= 18; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x + s.Economy.MarketSizeCells > width || z + s.Economy.MarketSizeCells > s.Map.HeightCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Market, z * width + x, Facing.North));
                        Steps(gateway, sim, 1);
                        uint id = FindBuilding(sim, BuildingKind.Market);
                        if (id != 0) return id;
                    }
            return 0;
        }

        private static uint PlaceNearCore(Battle sim, CommandGateway gateway, ScenarioDefinition s, BuildingKind kind, ref ulong sequence)
        {
            int width = s.Map.WidthCells;
            int core = Cell(s, s.Cores[0].Position);
            int cx = core % width, cz = core / width;
            int size = kind == BuildingKind.Castle ? s.Economy.CastleSizeCells : 1;
            for (int r = 5; r <= 18; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x + size > width || z + size > s.Map.HeightCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, kind, z * width + x, Facing.North));
                        Steps(gateway, sim, 1);
                        uint id = FindBuilding(sim, kind);
                        if (id != 0) return id;
                    }
            return 0;
        }

        private static uint FinishBuilding(Battle sim, CommandGateway gateway, uint id, ref ulong sequence)
        {
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            Steps(gateway, sim, 240);
            var building = sim.Capture(1).Economy.Buildings.First(b => b.Id == id);
            var tasks = new List<string>();
            for (uint villager = 1; villager <= 3; villager++)
            {
                object state = State(sim, "Villagers", villager);
                tasks.Add(villager + ":" + Get<byte>(state, "Task") + "/" + Get<uint>(state, "BuildingId"));
            }
            Assert.That(building.Complete, Is.True, $"building {id} kind={building.Kind} progress={building.Progress}/{building.Work} tick={sim.Capture(1).Tick} villagers={string.Join(",", tasks)}");
            return id;
        }

        private static uint PlaceCaravanserai(Battle sim, CommandGateway gateway, ScenarioDefinition s, ref ulong sequence, int outpostIndex = 0)
        {
            int width = s.Map.WidthCells, post = Cell(s, s.Outposts[outpostIndex].Position);
            int cx = post % width, cz = post / width;
            for (int r = 0; r <= 18; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x + s.Economy.CaravanseraiSizeCells > width || z + s.Economy.CaravanseraiSizeCells > s.Map.HeightCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Caravanserai, z * width + x, Facing.North));
                        Steps(gateway, sim, 1);
                        uint id = FindBuilding(sim, BuildingKind.Caravanserai);
                        if (id != 0) return id;
                    }
            return 0;
        }

        private static object World(Battle sim) => typeof(Battle).GetField("world", Private).GetValue(sim);
        private static object State(Battle sim, string array, uint id)
        {
            var values = (Array)World(sim).GetType().GetField(array, Private).GetValue(World(sim));
            return values.GetValue((int)id - 1);
        }
        private static T Get<T>(object value, string field) => (T)value.GetType().GetField(field, Private).GetValue(value);
        private static void Set<T>(object value, string field, T data) => value.GetType().GetField(field, Private).SetValue(value, data);
        private static void SetEconomy(Battle sim, uint faction, string field, object value)
        {
            var economies = (Array)World(sim).GetType().GetField("Economies", Private).GetValue(World(sim));
            object economy = economies.GetValue((int)faction - 1);
            economy.GetType().GetField(field, Private).SetValue(economy, value);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static void SetEnemyPosition(Battle sim, SimPoint position, bool alive)
        {
            object world = World(sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Private).GetValue(world);
            bool changed = false;
            for (int i = 0; i < soldiers.Length; i++)
            {
                object soldier = soldiers.GetValue(i);
                var initial = Get<SoldierDefinition>(soldier, "Initial");
                if (initial.FactionId != 2) continue;
                Set(soldier, "Alive", alive); Set(soldier, "Hp", alive ? 100 : 0); Set(soldier, "Position", position); Set(soldier, "MoveGoal", position);
                soldiers.SetValue(soldier, i);
                changed = true;
            }
            if (!changed) throw new AssertionException("テスト用の敵兵が見つからない");
        }

        private static void SetFactionCellVisible(Battle sim, ScenarioDefinition s, SimPoint position, bool visible)
        {
            object world = World(sim);
            var factions = (Array)world.GetType().GetField("Factions", Private).GetValue(world);
            object faction = factions.GetValue(0);
            var cells = Get<bool[]>(faction, "VisibleCells");
            cells[Cell(s, position)] = visible;
            factions.SetValue(faction, 0);
        }

        private static void StoreVillager(Battle sim, object value)
        {
            object world = World(sim);
            var villagers = (Array)world.GetType().GetField("Villagers", Private).GetValue(world);
            villagers.SetValue(value, (int)Get<uint>(value, "Id") - 1);
        }

        private static SimPoint MapCenter(Battle sim, int cell)
        {
            object map = World(sim).GetType().GetField("Map", Private).GetValue(World(sim));
            return (SimPoint)map.GetType().GetMethod("Center", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(map, new object[] { cell });
        }

        [TestCase(0, CivKind.Agrarian)]
        [TestCase(1, CivKind.Caravan)]
        [TestCase(2, CivKind.Caravan)]
        public void CaravanScoreUsesZeroOneOrMultipleUsableOutposts(int caravan, CivKind expected)
        {
            Assert.That(EconomyDecision.ChooseCiv(0, 3, 0, 0, caravan, 3), Is.EqualTo(expected));
        }

        [Test]
        public void MultipleSeedsIncludeARecognisedCaravanTerrainChoice()
        {
            var choices = new HashSet<CivKind>();
            var score = typeof(Battle).GetMethod("CountUsableCaravanOutposts", Private);
            var choose = typeof(Battle).GetMethod("ChooseCiv", Private);
            Assert.That(score, Is.Not.Null);
            Assert.That(choose, Is.Not.Null);
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var s = MapGenerator.GenerateTerrain(seed);
                s.Economy.Ages = true;
                s.Economy.Caravan = true;
                s.Economy.CaravanOutpostReach = 24;
                var sim = new Battle(s);
                int caravan = (int)score.Invoke(sim, new object[] { 1u, s.Cores[0].Position });
                var civ = (CivKind)choose.Invoke(sim, new object[] { 1u });
                TestContext.WriteLine("seed " + seed + ": usable outposts=" + caravan + " -> " + civ);
                choices.Add(civ);
            }
            Assert.That(choices, Does.Contain(CivKind.Caravan), "隊商が選ばれる地図がある");
        }

        [Test]
        public void CaravanTailRoundTripsInEveryForestryMasonryCombination()
        {
            foreach (bool forestry in new[] { false, true })
                foreach (bool masonry in new[] { false, true })
                {
                    var s = Scenario(31);
                    s.Economy.Forestry = forestry; s.Economy.Masonry = masonry;
                    var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(s)).Economy;
                    Assert.That(decoded.Caravan, Is.True);
                    Assert.That(decoded.Forestry, Is.EqualTo(forestry));
                    Assert.That(decoded.Masonry, Is.EqualTo(masonry));
                }
            var old = ScenarioBinary.Encode(MapGenerator.GenerateTerrain(31));
            var off = ScenarioBinary.Encode(Scenario(31));
            Assert.That(off.Length, Is.GreaterThan(old.Length));
            Assert.That(ScenarioBinary.Decode(old).Economy.Caravan, Is.False);
        }

        [Test]
        public void CaravanseraiNeedsFinishedMarketAndStoresThePairAndReward()
        {
            var s = Scenario(32); var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            int width = s.Map.WidthCells, post = Cell(s, s.Outposts[0].Position);
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Caravanserai, post, Facing.North));
            Steps(gateway, sim, 2);
            Assert.That(FindBuilding(sim, BuildingKind.Caravanserai), Is.EqualTo(0u));

            uint market = PlaceMarket(sim, gateway, s, ref sequence);
            Assert.That(market, Is.Not.EqualTo(0u));
            FinishBuilding(sim, gateway, market, ref sequence);
            uint host = PlaceCaravanserai(sim, gateway, s, ref sequence);
            Assert.That(host, Is.Not.EqualTo(0u));
            FinishBuilding(sim, gateway, host, ref sequence);

            object state = State(sim, "Buildings", host);
            uint storedMarket = Get<uint>(state, "CaravanMarketId");
            uint storedOutpost = Get<uint>(state, "CaravanOutpostId");
            Fix64 storedDistance = Get<Fix64>(state, "CaravanDistance");
            int storedReward = Get<int>(state, "CaravanWoodReward");
            Assert.That(storedMarket, Is.EqualTo(market));
            Assert.That(storedOutpost, Is.EqualTo(1u));
            Assert.That(storedDistance.Raw, Is.GreaterThan(0));
            Assert.That(storedReward, Is.InRange(1, 30));
            var hostView = sim.Capture(1).Economy.Buildings.First(b => b.Id == host);
            Assert.That(hostView.CaravanMarketId, Is.EqualTo(market));
            Assert.That(hostView.CaravanOutpostId, Is.EqualTo(1u));
            Assert.That(hostView.CaravanWoodReward, Is.EqualTo(storedReward));
            Assert.That(hostView.CaravanStopReason, Is.EqualTo(CaravanStopReason.Normal));

            uint second = PlaceMarket(sim, gateway, s, ref sequence);
            Assert.That(second, Is.Not.EqualTo(0u));
            state = State(sim, "Buildings", host);
            Assert.That(Get<uint>(state, "CaravanMarketId"), Is.EqualTo(storedMarket));
            Assert.That(Get<Fix64>(state, "CaravanDistance").Raw, Is.EqualTo(storedDistance.Raw));
            Assert.That(Get<int>(state, "CaravanWoodReward"), Is.EqualTo(storedReward));
        }

        [Test]
        public void CaravanLoadsAtHostAndPaysOnlyOnMarketReturn()
        {
            var s = Scenario(33); var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            uint market = PlaceMarket(sim, gateway, s, ref sequence); Assert.That(market, Is.Not.EqualTo(0u)); FinishBuilding(sim, gateway, market, ref sequence);
            uint host = PlaceCaravanserai(sim, gateway, s, ref sequence); Assert.That(host, Is.Not.EqualTo(0u)); FinishBuilding(sim, gateway, host, ref sequence);
            int reward = Get<int>(State(sim, "Buildings", host), "CaravanWoodReward");
            int before = sim.Capture(1).Economy.Wood;
            gateway.SubmitEconomy(EconomyCommand.CaravanRoute(1, ++sequence, host, new uint[] { 1 }));
            bool loaded = false, paid = false;
            for (int i = 0; i < 1600; i++)
            {
                gateway.Step();
                object villager = State(sim, "Villagers", 1);
                int cargo = Get<int>(villager, "CaravanWood");
                if (cargo > 0)
                {
                    loaded = true;
                    Assert.That(sim.Capture(1).Economy.Wood, Is.EqualTo(before));
                    var villagerView = sim.Capture(1).Economy.Villagers.First(v => v.Id == 1);
                    Assert.That(villagerView.CaravanMarketId, Is.EqualTo(market));
                    Assert.That(villagerView.CaravanseraiId, Is.EqualTo(host));
                    Assert.That(villagerView.CaravanWood, Is.EqualTo(cargo));
                    Assert.That(villagerView.CaravanStopReason, Is.EqualTo(CaravanStopReason.Normal));
                }
                if (sim.Capture(1).Economy.Wood == before + reward) { paid = true; break; }
            }
            Assert.That(loaded, Is.True, "隊商宿到着時に専用荷物へ積載される");
            Assert.That(paid, Is.True, "市場帰着時だけ固定報酬が入金される");
        }

        [Test]
        public void ObservedDangerStopsOutboundCaravanAndClearTicksResumeIt()
        {
            var s = Scenario(41); var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            uint market = PlaceMarket(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, market, ref sequence);
            uint host = PlaceCaravanserai(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, host, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.CaravanRoute(1, ++sequence, host, new uint[] { 1 }));
            gateway.Step();
            object v = State(sim, "Villagers", 1);
            SimPoint hostWork = MapCenter(sim, Get<int>(State(sim, "Buildings", host), "WorkCell"));
            Set(v, "Task", Enum.ToObject(v.GetType().GetField("Task", Private).FieldType, 11));
            Set(v, "CaravanStage", (byte)2); Set(v, "Position", hostWork);
            StoreVillager(sim, v);
            SetEnemyPosition(sim, hostWork, true); SetFactionCellVisible(sim, s, hostWork, false);
            Assert.That((bool)typeof(Battle).GetMethod("CaravanDangerous", Private).Invoke(sim, new[] { v }), Is.False, "未観測の敵は停止理由にしない");
            SetFactionCellVisible(sim, s, hostWork, true);
            Assert.That((bool)typeof(Battle).GetMethod("CaravanDangerous", Private).Invoke(sim, new[] { v }), Is.True);
            object[] stopArgs = { v };
            typeof(Battle).GetMethod("StopCaravanOutboundForDanger", Private).Invoke(sim, stopArgs);
            v = stopArgs[0]; StoreVillager(sim, v);
            Assert.That(Get<bool>(v, "CaravanDangerStopped"), Is.True, "観測済みの敵で自主停止する");
            Assert.That(Get<byte>(v, "CaravanStage"), Is.EqualTo(3));

            SimPoint marketWork = MapCenter(sim, Get<int>(State(sim, "Buildings", market), "WorkCell"));
            v = State(sim, "Villagers", 1);
            Set(v, "Task", Enum.ToObject(v.GetType().GetField("Task", Private).FieldType, 10));
            Set(v, "CaravanStage", (byte)3); Set(v, "Position", marketWork); Set(v, "CaravanWood", 0); Set(v, "CaravanGems", 0);
            StoreVillager(sim, v); SetEnemyPosition(sim, hostWork, false); SetFactionCellVisible(sim, s, hostWork, false);
            for (int i = 0; i < 19; i++)
            {
                object[] holdArgs = { State(sim, "Villagers", 1) };
                Assert.That((bool)typeof(Battle).GetMethod("HoldCaravanAtMarket", Private).Invoke(sim, holdArgs), Is.True);
                StoreVillager(sim, holdArgs[0]);
            }
            object[] resumeArgs = { State(sim, "Villagers", 1) };
            Assert.That((bool)typeof(Battle).GetMethod("HoldCaravanAtMarket", Private).Invoke(sim, resumeArgs), Is.False, "安全継続tick後に再開する");
            Assert.That(Get<bool>(resumeArgs[0], "CaravanDangerStopped"), Is.False);
        }

        [Test]
        public void LosingTheOutpostStopsNewLoadingButAllowsLoadedReturn()
        {
            var s = Scenario(34); var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            uint market = PlaceMarket(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, market, ref sequence);
            uint host = PlaceCaravanserai(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, host, ref sequence);
            int reward = Get<int>(State(sim, "Buildings", host), "CaravanWoodReward"); int before = sim.Capture(1).Economy.Wood;
            gateway.SubmitEconomy(EconomyCommand.CaravanRoute(1, ++sequence, host, new uint[] { 1 }));
            bool loaded = false;
            for (int i = 0; i < 1000 && !loaded; i++) { gateway.Step(); loaded = Get<int>(State(sim, "Villagers", 1), "CaravanWood") > 0; }
            Assert.That(loaded, Is.True);
            var outposts = (Array)World(sim).GetType().GetField("Outposts", Private).GetValue(World(sim));
            object outpost = outposts.GetValue(0); Set(outpost, "OwnerFactionId", 2u); outposts.SetValue(outpost, 0);
            bool paid = false;
            for (int i = 0; i < 1000; i++) { gateway.Step(); if (sim.Capture(1).Economy.Wood == before + reward) { paid = true; break; } }
            Assert.That(paid, Is.True, "喪失前に積んだ荷物は市場帰着で一度だけ入金できる");
            Assert.That(Get<int>(State(sim, "Villagers", 1), "CaravanWood"), Is.EqualTo(0));
        }

        [Test]
        public void ReissuingAnActiveCaravanOrderDoesNotDuplicateTheLoad()
        {
            var s = Scenario(35); var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            uint market = PlaceMarket(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, market, ref sequence);
            uint host = PlaceCaravanserai(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, host, ref sequence);
            int reward = Get<int>(State(sim, "Buildings", host), "CaravanWoodReward"); int before = sim.Capture(1).Economy.Wood;
            var command = EconomyCommand.CaravanRoute(1, ++sequence, host, new uint[] { 1 }); gateway.SubmitEconomy(command);
            bool loaded = false;
            for (int i = 0; i < 1000 && !loaded; i++) { gateway.Step(); loaded = Get<int>(State(sim, "Villagers", 1), "CaravanWood") > 0; }
            Assert.That(loaded, Is.True);
            gateway.SubmitEconomy(EconomyCommand.CaravanRoute(1, ++sequence, host, new uint[] { 1 }));
            for (int i = 0; i < 1000 && sim.Capture(1).Economy.Wood == before; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Wood, Is.EqualTo(before + reward));
            for (int i = 0; i < 1000; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Wood, Is.LessThanOrEqualTo(before + reward * 2));
        }

        [Test]
        public void AutomaticCaravanAssignmentUsesIdleVillagersAndLeavesHeldRoutesAlone()
        {
            var s = Scenario(37); var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            uint market = PlaceMarket(sim, gateway, s, ref sequence); Assert.That(market, Is.Not.EqualTo(0u)); FinishBuilding(sim, gateway, market, ref sequence);
            uint host = PlaceCaravanserai(sim, gateway, s, ref sequence); Assert.That(host, Is.Not.EqualTo(0u)); FinishBuilding(sim, gateway, host, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.CaravanRoute(1, ++sequence, host, new uint[] { 1 }));
            Steps(gateway, sim, 2);
            object heldVillager = State(sim, "Villagers", 1);
            Assert.That(Get<bool>(heldVillager, "Held"), Is.True);
            Assert.That(Get<byte>(heldVillager, "CaravanStage"), Is.GreaterThan(0));

            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            Steps(gateway, sim, 40);
            heldVillager = State(sim, "Villagers", 1);
            Assert.That(Get<bool>(heldVillager, "Held"), Is.True);
            Assert.That(Get<byte>(heldVillager, "CaravanStage"), Is.GreaterThan(0));
            int active = Enumerable.Range(1, 3).Count(id => IsCaravanTask(Get<byte>(State(sim, "Villagers", (uint)id), "Task")));
            Assert.That(active, Is.LessThanOrEqualTo(s.Economy.CaravanAutoVillagers));
        }

        private static bool IsCaravanTask(byte task)
            => task == 10 || task == 11 || task == 12;

        [Test]
        public void CaravanRouteReplayIsDeterministicWhenTheFlagIsOn()
        {
            var s = Scenario(36); var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            uint market = PlaceMarket(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, market, ref sequence);
            uint host = PlaceCaravanserai(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, host, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.CaravanRoute(1, ++sequence, host, new uint[] { 1 }));
            Steps(gateway, sim, 1200);
            using (var stream = new System.IO.MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, s, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        [Test]
        public void AutomaticCaravanAssignmentDistributesAcrossFinishedHostsInIdOrder()
        {
            var s = Scenario(38); s.Economy.CaravanAutoVillagers = 1; s.Outposts[1].OwnerFactionId = 1;
            var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            uint market = PlaceMarket(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, market, ref sequence);
            uint first = PlaceCaravanserai(sim, gateway, s, ref sequence, 0); FinishBuilding(sim, gateway, first, ref sequence);
            uint second = PlaceCaravanserai(sim, gateway, s, ref sequence, 1); FinishBuilding(sim, gateway, second, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.ReturnToAuto(1, ++sequence));
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            Steps(gateway, sim, 80);
            var assigned = Enumerable.Range(1, sim.Capture(1).Economy.Villagers.Count)
                .Select(id => State(sim, "Villagers", (uint)id))
                .Where(v => IsCaravanTask(Get<byte>(v, "Task")))
                .Select(v => Get<uint>(v, "CaravanseraiId"))
                .Distinct().ToArray();
            Assert.That(assigned, Does.Contain(first));
            Assert.That(assigned, Does.Contain(second));
        }

        [Test]
        public void SecondAgeCaravanLoadsOneGemAndPaysItOnceOnReturn()
        {
            var s = Scenario(39); var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            uint market = PlaceMarket(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, market, ref sequence);
            uint host = PlaceCaravanserai(sim, gateway, s, ref sequence); FinishBuilding(sim, gateway, host, ref sequence);
            SetEconomy(sim, 1, "Age", (byte)2);
            int beforeGems = sim.Capture(1).Economy.Gems;
            gateway.SubmitEconomy(EconomyCommand.CaravanRoute(1, ++sequence, host, new uint[] { 1 }));
            bool loaded = false;
            for (int i = 0; i < 1600 && !loaded; i++)
            {
                gateway.Step();
                object v = State(sim, "Villagers", 1);
                loaded = Get<int>(v, "CaravanWood") > 0;
                if (loaded) Assert.That(Get<int>(v, "CaravanGems"), Is.EqualTo(1));
            }
            Assert.That(loaded, Is.True);
            for (int i = 0; i < 1600 && sim.Capture(1).Economy.Gems == beforeGems; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Gems, Is.EqualTo(beforeGems + 1));
            // The route is intentionally repeatable; this only guards against a second deposit for the same load.
            for (int i = 0; i < 5; i++) gateway.Step();
            Assert.That(sim.Capture(1).Economy.Gems, Is.EqualTo(beforeGems + 1));
        }

        [Test]
        public void CaravanAutomaticallyHiresOneMercenaryFromAThirdAgeCastle()
        {
            var s = Scenario(40); s.Economy.StartStone = 100000; var sim = new Battle(s); var gateway = new CommandGateway(sim); ulong sequence = 0;
            EnterCaravan(gateway, sim, ref sequence);
            SetEconomy(sim, 1, "Age", (byte)3);
            SetEconomy(sim, 1, "Gems", 20);
            uint castle = PlaceNearCore(sim, gateway, s, BuildingKind.Castle, ref sequence);
            Assert.That(castle, Is.Not.EqualTo(0u));
            FinishBuilding(sim, gateway, castle, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.ReturnToAuto(1, ++sequence));
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            Steps(gateway, sim, 20);
            object state = State(sim, "Buildings", castle);
            Assert.That(Get<int>(state, "Queued"), Is.EqualTo(1));
            Assert.That(sim.Capture(1).Economy.Gems, Is.EqualTo(0));
        }
    }
}
