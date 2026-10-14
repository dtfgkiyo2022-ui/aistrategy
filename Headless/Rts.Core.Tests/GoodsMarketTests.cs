using System;
using System.Collections.Generic;
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
    public sealed class GoodsMarketTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static Battle MarketSimulation(out ScenarioDefinition scenario)
            => MarketSimulation(_ => { }, out scenario);

        private static Battle MarketSimulation(Action<EconomyRules> configure, out ScenarioDefinition scenario)
        {
            scenario = MapGenerator.GenerateTerrain(7333UL);
            scenario.Economy.Ages = true; scenario.Economy.Industry = true; scenario.Economy.ProcessingChain = true; scenario.Economy.Tack = true; scenario.Economy.GoodsMarket = true;
            scenario.Economy.StartFood = 1000; scenario.Economy.StartWood = 5000;
            configure(scenario.Economy);
            var sim = new Battle(scenario);
            int origin = (int)typeof(Battle).GetMethod("FindSite", PrivateInstance).Invoke(sim, new object[] { 1u, scenario.Economy.MarketSizeCells });
            Assert.That(origin, Is.GreaterThanOrEqualTo(0));
            typeof(Battle).GetMethod("PlaceBuildingAt", PrivateInstance, null,
                new[] { typeof(uint), typeof(BuildingKind), typeof(int), typeof(Facing), typeof(uint) }, null)
                .Invoke(sim, new object[] { 1u, BuildingKind.Market, origin, Facing.North, 0u });
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(sim);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            for (int i = 0; i < buildings.Length; i++)
            {
                var building = buildings.GetValue(i);
                if ((BuildingKind)building.GetType().GetField("Kind", PrivateInstance).GetValue(building) != BuildingKind.Market) continue;
                building.GetType().GetField("Complete", PrivateInstance).SetValue(building, true);
                buildings.SetValue(building, i);
                break;
            }
            typeof(Battle).GetMethod("PublishFrames", PrivateInstance).Invoke(sim, null);
            return sim;
        }

        private static void AddStock(Battle sim, ResourceKind kind, int amount)
            => typeof(Battle).GetMethod("AddStock", PrivateInstance).Invoke(sim, new object[] { 1u, kind, amount });

        private static void SetTick(Battle sim, long tick)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(sim);
            world.GetType().GetField("Tick", PrivateInstance).SetValue(world, tick);
        }

        private static object World(Battle sim) => typeof(Battle).GetField("world", PrivateInstance).GetValue(sim);

        private static object Economy(Battle sim)
        {
            var world = World(sim);
            var economies = (Array)world.GetType().GetField("Economies", PrivateInstance).GetValue(world);
            return economies.GetValue(0);
        }

        private static void SetEconomy(Battle sim, string name, object value)
        {
            var world = World(sim);
            var field = world.GetType().GetField("Economies", PrivateInstance);
            var economies = (Array)field.GetValue(world);
            var economy = economies.GetValue(0);
            economy.GetType().GetField(name, PrivateInstance).SetValue(economy, value);
            economies.SetValue(economy, 0);
            field.SetValue(world, economies);
        }

        private static T EconomyValue<T>(Battle sim, string name)
            => (T)Economy(sim).GetType().GetField(name, PrivateInstance).GetValue(Economy(sim));

        private static void SetFirstMarketComplete(Battle sim, bool complete)
        {
            var world = World(sim);
            var buildings = (Array)world.GetType().GetField("Buildings", PrivateInstance).GetValue(world);
            for (int i = 0; i < buildings.Length; i++)
            {
                var building = buildings.GetValue(i);
                if ((BuildingKind)building.GetType().GetField("Kind", PrivateInstance).GetValue(building) != BuildingKind.Market) continue;
                building.GetType().GetField("Complete", PrivateInstance).SetValue(building, complete);
                buildings.SetValue(building, i);
                return;
            }
            Assert.Fail("market was not found");
        }

        private static int MarketCount(Battle sim)
            => sim.Capture(1).Economy.Buildings.Count(x => x.Kind == BuildingKind.Market);

        private static string Hash(Battle sim)
            => Convert.ToHexString(ReplayBinary.Hash(sim.CaptureDiagnostic().CanonicalState));

        private static void Refresh(Battle sim)
            => typeof(Battle).GetMethod("PublishFrames", PrivateInstance).Invoke(sim, null);
        [Test]
        public void GoodsMarketExtension33RoundTripsAllValues()
        {
            var scenario = MapGenerator.GenerateTerrain(7331UL);
            var e = scenario.Economy;
            e.Ages = true; e.Industry = true; e.ProcessingChain = true; e.GoodsMarket = true;
            e.GoodsTradeLot = 7; e.GoodsAutoSellAt = 19; e.GoodsAutoSellIntervalTicks = 120; e.GoodsAutoKeep = 6; e.GoodsMarketWoodReserve = 24;
            e.GoodsValueSteel = 81; e.GoodsValueBowGear = 42; e.GoodsValueRations = 63; e.GoodsValueBricks = 64;
            e.GoodsValueTack = 85; e.GoodsValuePlanks = 66; e.GoodsValueBooks = 67; e.GoodsValueIncense = 68;
            e.GoodsValueCharms = 69; e.GoodsValueRepairKits = 47; e.GoodsValueTools = 48;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();

            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario));
            Assert.That(decoded.Extensions.Any(x => x.Id == 33), Is.True);
            Assert.That(decoded.Economy.GoodsMarket, Is.True);
            Assert.That(decoded.Economy.GoodsTradeLot, Is.EqualTo(7));
            Assert.That(decoded.Economy.GoodsValueTools, Is.EqualTo(48));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(scenario)));
        }

        [Test]
        public void ManualGoodsTradeUsesLotValueAndMarketModifiersPath()
        {
            var sim = MarketSimulation(out var scenario);
            AddStock(sim, ResourceKind.Tack, scenario.Economy.GoodsTradeLot);
            typeof(Battle).GetMethod("TradeAtMarket", PrivateInstance).Invoke(sim, new object[] { 1u, ResourceKind.Tack, ResourceKind.Food });
            var fields = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(x => x.Key, x => x.Value);
            Assert.That(fields["Economy[1].Tack"], Is.EqualTo("0"));
            Assert.That(fields["Economy[1].Food"], Is.EqualTo((scenario.Economy.StartFood + scenario.Economy.GoodsValueTack * scenario.Economy.GoodsTradeLot).ToString()));
        }

        [Test]
        public void AutomaticGoodsTradeSelectsOneLotAtTheInterval()
        {
            var sim = MarketSimulation(out var scenario);
            AddStock(sim, ResourceKind.Tack, scenario.Economy.GoodsAutoSellAt);
            SetTick(sim, scenario.Economy.GoodsAutoSellIntervalTicks);
            typeof(Battle).GetMethod("DecideGoodsMarket", PrivateInstance).Invoke(sim, new object[] { 1u });
            var fields = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(x => x.Key, x => x.Value);
            Assert.That(fields["Economy[1].Tack"], Is.EqualTo((scenario.Economy.GoodsAutoSellAt - scenario.Economy.GoodsTradeLot).ToString()));
            Assert.That(fields["Economy[1].GoodsMarket.Tack.SoldCount"], Is.EqualTo("1"));
        }

        [Test]
        public void GoodsMarketSnapshotIsFlagGatedAndHasStableOrder()
        {
            var scenario = MapGenerator.GenerateTerrain(7332UL);
            scenario.Economy.Ages = true; scenario.Economy.Industry = true; scenario.Economy.ProcessingChain = true; scenario.Economy.GoodsMarket = true;
            var sim = new Battle(scenario);
            var snapshot = sim.CaptureMatchPackSnapshot();
            Assert.That(snapshot.Factions[0].GoodsMarket.Select(x => x.Goods), Is.EqualTo(new[] { "Steel", "BowGear", "Rations", "Bricks", "Tack", "Planks", "Books", "Incense", "Charms", "RepairKits", "Tools" }));

            scenario.Economy.GoodsMarket = false;
            var off = new Battle(scenario);
            Assert.That(off.CaptureMatchPackSnapshot().Factions[0].GoodsMarket, Is.Empty);
        }

        [Test]
        public void FlagOffKeepsLegacyBytesAndEveryTickHash()
        {
            var scenario = LiveGameRules.Create(5, false, false, false, false);
            scenario.Economy.GoodsMarket = false;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            var bytes = ScenarioBinary.Encode(scenario);
            var legacy = ScenarioBinary.Decode(bytes);
            Assert.That(legacy.Extensions.Any(x => x.Id == 33), Is.False);
            Assert.That(ScenarioBinary.Encode(legacy), Is.EqualTo(bytes));

            var left = new Battle(scenario);
            var right = new Battle(legacy);
            var reservation = new ScheduledInput(1, 0, 1, EconomyCommand.Advance(1, 1, CivKind.Cavalry));
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                var inputs = tick == 1 ? new[] { reservation } : Array.Empty<ScheduledInput>();
                left.Step(tick, inputs);
                right.Step(tick, inputs);
                Assert.That(Hash(left), Is.EqualTo(Hash(right)), "tick " + tick);
            }
        }

        [Test]
        public void FlagOffGoodsTradeDoesNothing()
        {
            var scenario = MapGenerator.GenerateTerrain(7334UL);
            scenario.Economy.Ages = true; scenario.Economy.Industry = true; scenario.Economy.ProcessingChain = true;
            scenario.Economy.GoodsMarket = false;
            var sim = new Battle(scenario);
            AddStock(sim, ResourceKind.Tack, scenario.Economy.GoodsTradeLot + 10);
            int before = sim.Capture(1).Economy.Tack;
            typeof(Battle).GetMethod("TradeAtMarket", PrivateInstance).Invoke(sim, new object[] { 1u, ResourceKind.Tack, ResourceKind.Food });
            Assert.That(sim.Capture(1).Economy.Tack, Is.EqualTo(before));
        }

        [Test]
        public void GoodsTradeRejectsMissingMarketBuildingAndInvalidTakeGive()
        {
            var noMarketScenario = MapGenerator.GenerateTerrain(7335UL);
            noMarketScenario.Economy.Ages = true; noMarketScenario.Economy.Industry = true; noMarketScenario.Economy.ProcessingChain = true; noMarketScenario.Economy.GoodsMarket = true;
            var noMarket = new Battle(noMarketScenario);
            AddStock(noMarket, ResourceKind.Tack, noMarketScenario.Economy.GoodsTradeLot);
            int noMarketBefore = noMarket.Capture(1).Economy.Tack;
            typeof(Battle).GetMethod("TradeAtMarket", PrivateInstance).Invoke(noMarket, new object[] { 1u, ResourceKind.Tack, ResourceKind.Food });
            Assert.That(noMarket.Capture(1).Economy.Tack, Is.EqualTo(noMarketBefore));

            var building = MarketSimulation(out var scenario);
            SetFirstMarketComplete(building, false);
            AddStock(building, ResourceKind.Tack, scenario.Economy.GoodsTradeLot);
            int buildingBefore = building.Capture(1).Economy.Tack;
            typeof(Battle).GetMethod("TradeAtMarket", PrivateInstance).Invoke(building, new object[] { 1u, ResourceKind.Tack, ResourceKind.Food });
            Assert.That(building.Capture(1).Economy.Tack, Is.EqualTo(buildingBefore));

            foreach (var pair in new[] {
                (ResourceKind.Gems, ResourceKind.Food), (ResourceKind.Gold, ResourceKind.Food),
                (ResourceKind.Tack, ResourceKind.Tack), (ResourceKind.Tack, ResourceKind.Rations),
                (ResourceKind.Tack, ResourceKind.TradeGoods), (ResourceKind.Tack, ResourceKind.Charcoal),
                (ResourceKind.Tack, ResourceKind.Metal) })
            {
                var each = MarketSimulation(out var eachScenario);
                AddStock(each, pair.Item1, eachScenario.Economy.GoodsTradeLot);
                var before = each.Capture(1).Economy;
                typeof(Battle).GetMethod("TradeAtMarket", PrivateInstance).Invoke(each, new object[] { 1u, pair.Item1, pair.Item2 });
                Assert.That(each.Capture(1).Economy.Tack, Is.EqualTo(before.Tack), pair.ToString());
                Assert.That(each.Capture(1).Economy.Food, Is.EqualTo(before.Food), pair.ToString());
            }
            var shortStock = MarketSimulation(out var shortScenario);
            AddStock(shortStock, ResourceKind.Tack, shortScenario.Economy.GoodsTradeLot - 1);
            int shortBefore = shortStock.Capture(1).Economy.Tack;
            typeof(Battle).GetMethod("TradeAtMarket", PrivateInstance).Invoke(shortStock, new object[] { 1u, ResourceKind.Tack, ResourceKind.Food });
            Assert.That(shortStock.Capture(1).Economy.Tack, Is.EqualTo(shortBefore));
        }

        [Test]
        public void ThreeGoodsAndThreeResourcesUseValueTimesLot()
        {
            var sim = MarketSimulation(out var scenario);
            var goods = new[] { (ResourceKind.Steel, 11), (ResourceKind.Tack, 13), (ResourceKind.Tools, 17) };
            var takes = new[] { ResourceKind.Food, ResourceKind.Wood, ResourceKind.Stone };
            foreach (var good in goods)
                foreach (var take in takes)
                {
                    var one = MarketSimulation(e => { e.GoodsTradeLot = 7; e.GoodsValueSteel = 11; e.GoodsValueTack = 13; e.GoodsValueTools = 17; }, out var s);
                    AddStock(one, good.Item1, s.Economy.GoodsTradeLot);
                    int before = (int)Economy(one).GetType().GetField(take.ToString(), PrivateInstance).GetValue(Economy(one));
                    typeof(Battle).GetMethod("TradeAtMarket", PrivateInstance).Invoke(one, new object[] { 1u, good.Item1, take });
                    int after = (int)Economy(one).GetType().GetField(take.ToString(), PrivateInstance).GetValue(Economy(one));
                    Assert.That(after - before, Is.EqualTo(good.Item2 * s.Economy.GoodsTradeLot), good.Item1 + " -> " + take);
                }
        }

        [Test]
        public void BankingRaisesManualAndEconomyViewGoodsPricesTogether()
        {
            var sim = MarketSimulation(out var scenario);
            int before = sim.Capture(1).Economy.GoodsPrices.Single(x => x.Goods == ResourceKind.Tack).Amount;
            ulong banking = 1UL << ((int)TechKind.Banking - 1);
            SetEconomy(sim, "Techs", banking);
            Refresh(sim);
            int after = sim.Capture(1).Economy.GoodsPrices.Single(x => x.Goods == ResourceKind.Tack).Amount;
            Assert.That(after - before, Is.EqualTo(scenario.Economy.BankingTradeReturn));
            var manual = (int)typeof(Battle).GetMethod("MarketReturnFor", PrivateInstance).Invoke(sim, new object[] { 1u, scenario.Economy.GoodsValueTack * scenario.Economy.GoodsTradeLot });
            Assert.That(after, Is.EqualTo(manual));
        }

        [Test]
        public void AutomaticGoodsTradeKeepsFloorAndReadsPerKindCap()
        {
            var keep = MarketSimulation(e => { e.GoodsAutoSellAt = 10; e.GoodsAutoKeep = 5; e.GoodsTradeLot = 6; }, out var scenario);
            AddStock(keep, ResourceKind.Tack, 10);
            SetTick(keep, scenario.Economy.GoodsAutoSellIntervalTicks);
            typeof(Battle).GetMethod("DecideGoodsMarket", PrivateInstance).Invoke(keep, new object[] { 1u });
            Refresh(keep);
            Assert.That(EconomyValue<int>(keep, "Tack"), Is.EqualTo(10), "selling would cross the keep floor");

            var cap = MarketSimulation(e => { e.Tools = true; e.GoodsAutoSellAt = 20; e.GoodsAutoKeep = 0; e.GoodsTradeLot = 5; e.ToolStockLimit = 10; }, out var capScenario);
            AddStock(cap, ResourceKind.Tools, 10);
            SetTick(cap, capScenario.Economy.GoodsAutoSellIntervalTicks);
            typeof(Battle).GetMethod("DecideGoodsMarket", PrivateInstance).Invoke(cap, new object[] { 1u });
            Refresh(cap);
            Assert.That(EconomyValue<int>(cap, "Tools"), Is.EqualTo(5));

            var changed = MarketSimulation(e => { e.Tools = true; e.GoodsAutoSellAt = 20; e.GoodsAutoKeep = 0; e.GoodsTradeLot = 5; e.ToolStockLimit = 12; }, out var changedScenario);
            AddStock(changed, ResourceKind.Tools, 12);
            SetTick(changed, changedScenario.Economy.GoodsAutoSellIntervalTicks);
            typeof(Battle).GetMethod("DecideGoodsMarket", PrivateInstance).Invoke(changed, new object[] { 1u });
            Refresh(changed);
            Assert.That(EconomyValue<int>(changed, "Tools"), Is.EqualTo(7));
        }

        [Test]
        public void AutomaticGoodsMarketUsesWoodReserveAndDoesNotDuplicateOrBuildWhileSaving()
        {
            var scenario = MapGenerator.GenerateTerrain(7333UL);
            scenario.Economy.Ages = true; scenario.Economy.Industry = true; scenario.Economy.ProcessingChain = true; scenario.Economy.GoodsMarket = true;
            scenario.Economy.StartWood = scenario.Economy.MarketWoodCost + scenario.Economy.GoodsMarketWoodReserve;
            scenario.Economy.Tack = true;
            var sim = new Battle(scenario);
            AddStock(sim, ResourceKind.Tack, scenario.Economy.GoodsAutoSellAt);
            SetTick(sim, scenario.Economy.GoodsAutoSellIntervalTicks);
            typeof(Battle).GetMethod("DecideGoodsMarket", PrivateInstance).Invoke(sim, new object[] { 1u });
            Refresh(sim);
            Assert.That(MarketCount(sim), Is.EqualTo(1));
            typeof(Battle).GetMethod("DecideGoodsMarket", PrivateInstance).Invoke(sim, new object[] { 1u });
            Assert.That(MarketCount(sim), Is.EqualTo(1), "a construction-site market is not duplicated");

            var shortScenario = MapGenerator.GenerateTerrain(7333UL);
            shortScenario.Economy.Ages = true; shortScenario.Economy.Industry = true; shortScenario.Economy.ProcessingChain = true; shortScenario.Economy.GoodsMarket = true;
            shortScenario.Economy.StartWood = shortScenario.Economy.MarketWoodCost + shortScenario.Economy.GoodsMarketWoodReserve - 1;
            shortScenario.Economy.Tack = true;
            var shortSim = new Battle(shortScenario); AddStock(shortSim, ResourceKind.Tack, shortScenario.Economy.GoodsAutoSellAt); SetTick(shortSim, shortScenario.Economy.GoodsAutoSellIntervalTicks);
            typeof(Battle).GetMethod("DecideGoodsMarket", PrivateInstance).Invoke(shortSim, new object[] { 1u });
            Assert.That(MarketCount(shortSim), Is.EqualTo(0));

            var saving = new Battle(scenario); AddStock(saving, ResourceKind.Tack, scenario.Economy.GoodsAutoSellAt);
            SetEconomy(saving, "Civ", CivKind.Primitive); SetEconomy(saving, "AdvanceRemaining", 0L);
            SetTick(saving, scenario.Economy.GoodsAutoSellIntervalTicks);
            typeof(Battle).GetMethod("DecideGoodsMarket", PrivateInstance).Invoke(saving, new object[] { 1u });
            Assert.That(MarketCount(saving), Is.EqualTo(0), "saving for the age keeps the market closed");
        }

        [Test]
        public void GoodsMarketCavalryReservationRecordsAndReplaysEveryTick()
        {
            var scenario = LiveGameRules.Create(7338UL, false, true, false, false);
            var input = new ScheduledInput(1, 0, 1, EconomyCommand.Advance(1, 1, CivKind.Cavalry));
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, new[] { input }, 12000, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }
    }
}
