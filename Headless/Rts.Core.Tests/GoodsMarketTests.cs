using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
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
        {
            scenario = MapGenerator.GenerateTerrain(7333UL);
            scenario.Economy.Ages = true; scenario.Economy.Industry = true; scenario.Economy.ProcessingChain = true; scenario.Economy.Tack = true; scenario.Economy.GoodsMarket = true;
            scenario.Economy.StartFood = 1000; scenario.Economy.StartWood = 5000;
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
            return sim;
        }

        private static void AddStock(Battle sim, ResourceKind kind, int amount)
            => typeof(Battle).GetMethod("AddStock", PrivateInstance).Invoke(sim, new object[] { 1u, kind, amount });

        private static void SetTick(Battle sim, long tick)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(sim);
            world.GetType().GetField("Tick", PrivateInstance).SetValue(world, tick);
        }
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
    }
}
