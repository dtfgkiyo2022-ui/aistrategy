using System;
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
    /// <summary>V3-5 32 #18: a fixed-yield route between a finished own market and own core.</summary>
    public sealed class TradeRouteTests
    {
        private const int Width = 128;

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition FastAgeScenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.StartFood = 100000;
            s.Economy.StartWood = 100000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.MarketWoodCost = 0;
            s.Economy.MarketWork = 1;
            s.Economy.VillagerSpeed = Fix64.FromInt(16);
            return s;
        }

        private static void StopAutoAndAdvance(CommandGateway gateway, Battle sim, ref ulong sequence)
        {
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 5);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Agrarian));
        }

        private static uint PlaceMarket(CommandGateway gateway, Battle sim, ScenarioDefinition scenario, ref ulong sequence)
        {
            int core = (int)(scenario.Cores[0].Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * Width
                + (int)(scenario.Cores[0].Position.X.Raw / 65536 / scenario.Map.CellSizeMeters);
            int cx = core % Width, cz = core / Width;
            for (int r = 5; r <= 14; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || cx + dx < 0 || cz + dz < 0 || cx + dx > Width - 4 || cz + dz > 60) continue;
                        int before = sim.Capture(1).Economy.Buildings.Count;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Market, (cz + dz) * Width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var market = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.FactionId == 1 && b.Kind == BuildingKind.Market);
                        if (sim.Capture(1).Economy.Buildings.Count > before && market.Id != 0) return market.Id;
                    }
            return 0;
        }

        private static void FinishMarket(CommandGateway gateway, Battle sim, uint market, ref ulong sequence)
        {
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, market));
            for (int i = 0; i < 240; i++) Steps(gateway, sim, 1);
            var own = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Id == market);
            Assert.That(own.Id, Is.EqualTo(market));
            Assert.That(own.Complete, Is.True, "the finished market is required before a route can start");
        }

        private static void DestroyBuildingForTest(Battle sim, uint building)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var world = typeof(Battle).GetField("world", flags).GetValue(sim);
            var states = (Array)world.GetType().GetField("Buildings", flags).GetValue(world);
            object state = states.GetValue((int)building - 1);
            state.GetType().GetField("Hp", flags).SetValue(state, 0);
            states.SetValue(state, (int)building - 1);
        }

        [Test]
        public void CommandIsRejectedWithoutAFinishedMarket()
        {
            var s = FastAgeScenario(18);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            StopAutoAndAdvance(gateway, sim, ref sequence);
            int wood = sim.Capture(1).Economy.Wood;

            gateway.SubmitEconomy(EconomyCommand.TradeRoute(1, ++sequence, new uint[] { 1 }));
            Steps(gateway, sim, 1);

            Assert.That(sim.Capture(1).Economy.Wood, Is.EqualTo(wood));
            Assert.That(sim.Capture(1).Economy.Villagers.Any(v => v.IsOwn && v.Activity == VillagerActivity.Trading), Is.False);
        }

        [Test]
        public void AFinishedMarketAddsExactlyTradeRouteWoodAtEveryCoreArrivalAndReplays()
        {
            var s = FastAgeScenario(19);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            StopAutoAndAdvance(gateway, sim, ref sequence);
            uint market = PlaceMarket(gateway, sim, s, ref sequence);
            Assert.That(market, Is.Not.EqualTo(0u), "a legal market site is required");
            FinishMarket(gateway, sim, market, ref sequence);

            gateway.SubmitEconomy(EconomyCommand.TradeRoute(1, ++sequence, new uint[] { 1 }));
            Steps(gateway, sim, 1);
            int previousWood = sim.Capture(1).Economy.Wood;
            int rounds = 0;
            for (int tick = 0; tick < 1200; tick++)
            {
                gateway.Step();
                int currentWood = sim.Capture(1).Economy.Wood;
                int delta = currentWood - previousWood;
                Assert.That(delta == 0 || delta == s.Economy.TradeRouteWood, Is.True, "wood changed away from a core arrival at tick " + tick);
                if (delta != 0) rounds++;
                previousWood = currentWood;
            }
            Assert.That(rounds, Is.GreaterThan(2), "the route completed several round trips in the fixed 1200-tick window");

            var routeInput = gateway.Inputs.Last(i => i.Kind == InputKind.Economy && i.Economy.Kind == EconomyCommandKind.TradeRoute);
            var routeCopy = InputBinary.Decode(InputBinary.Encode(routeInput));
            Assert.That(routeCopy.Economy.Kind, Is.EqualTo(EconomyCommandKind.TradeRoute));
            Assert.That(routeCopy.Economy.VillagerIds, Is.EqualTo(new uint[] { 1 }));

            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, s, gateway.Inputs, sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void DestroyedMarketReturnsItsTraderToIdle()
        {
            var s = FastAgeScenario(21);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            StopAutoAndAdvance(gateway, sim, ref sequence);
            uint market = PlaceMarket(gateway, sim, s, ref sequence);
            Assert.That(market, Is.Not.EqualTo(0u));
            FinishMarket(gateway, sim, market, ref sequence);

            gateway.SubmitEconomy(EconomyCommand.TradeRoute(1, ++sequence, new uint[] { 1 }));
            Steps(gateway, sim, 1);
            Assert.That(sim.Capture(1).Economy.Villagers.Single(v => v.Id == 1).Activity, Is.EqualTo(VillagerActivity.Trading));

            DestroyBuildingForTest(sim, market);
            gateway.Step();

            Assert.That(sim.Capture(1).Economy.Villagers.Single(v => v.Id == 1).Activity, Is.EqualTo(VillagerActivity.Idle));
        }

        [Test]
        public void AutomaticEconomyAddsOneTraderPerDecisionUntilTwoAreActive()
        {
            var s = FastAgeScenario(22);
            s.Economy.StartFood = 0;
            s.Economy.StartWood = 0;
            s.Economy.AutoVillagerTarget = 0;
            s.ResourceNodes = Array.Empty<ResourceNodeDefinition>();
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            StopAutoAndAdvance(gateway, sim, ref sequence);
            uint market = PlaceMarket(gateway, sim, s, ref sequence);
            Assert.That(market, Is.Not.EqualTo(0u));
            FinishMarket(gateway, sim, market, ref sequence);
            gateway.SubmitEconomy(EconomyCommand.ReturnToAuto(1, ++sequence));
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));

            int maximum = 0;
            for (int i = 0; i < 100; i++)
            {
                gateway.Step();
                int active = sim.Capture(1).Economy.Villagers.Count(v => v.IsOwn && v.Activity == VillagerActivity.Trading);
                maximum = Math.Max(maximum, active);
            }
            Assert.That(maximum, Is.LessThanOrEqualTo(2));
            Assert.That(maximum, Is.EqualTo(2), "automatic economy eventually assigns its two route villagers");
        }

        [Test]
        public void NoAgeMapNeverStartsATradeRoute()
        {
            var s = MapGenerator.Generate(20, true, true);
            s.Economy.StartWood = 100000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.TradeRoute(1, ++sequence, new uint[] { 1, 2, 3 }));
            int initialWood = sim.Capture(1).Economy.Wood;
            Steps(gateway, sim, 1200);

            Assert.That(sim.Capture(1).Economy.Wood, Is.EqualTo(initialWood));
            Assert.That(sim.Capture(1).Economy.Villagers.Any(v => v.IsOwn && v.Activity == VillagerActivity.Trading), Is.False);
        }
    }
}
