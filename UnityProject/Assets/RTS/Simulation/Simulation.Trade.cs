using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 markets and siege (technical-design-v3 32 #9, #19). A finished own market trades TradeLot of food, wood or stone for
    /// TradeReturn of another, or GemsTradeReturn of Gems. Gems can only be received. The siege workshop (second age) trains rams: they fight as infantry, but strike buildings
    /// and cores with RamSiegeDamage. Maps with ages only.
    /// </summary>
    public sealed partial class Simulation
    {
        private const int TradeRich = 600, TradePoor = 150, AutoRams = 2, AutoTradeVillagers = 2;

        private static bool Tradable(ResourceKind kind) => kind == ResourceKind.Food || kind == ResourceKind.Wood || kind == ResourceKind.Stone;

        private static bool TradeTakeable(ResourceKind kind) => Tradable(kind) || kind == ResourceKind.Gems;

        private static int StockOf(FactionEconomy e, ResourceKind kind)
            => kind == ResourceKind.Food ? e.Food : kind == ResourceKind.Wood ? e.Wood : kind == ResourceKind.Stone ? e.Stone : kind == ResourceKind.Gems ? e.Gems : 0;

        private void TradeAtMarket(uint faction, ResourceKind give, ResourceKind take)
        {
            if (!AgesOn || give == take || !Tradable(give) || !TradeTakeable(take)) return;
            int market = OwnBuildingIndex(faction, BuildingKind.Market);
            if (market < 0 || !world.Buildings[market].Complete) return;
            var rules = world.Config.Economy;
            if (StockOf(world.Economies[faction - 1], give) < rules.TradeLot) return;
            AddStock(faction, give, -rules.TradeLot);
            int returned = take == ResourceKind.Gems ? rules.GemsTradeReturn
                : rules.TradeReturn + (HasTech(faction, TechKind.Banking) ? rules.BankingTradeReturn : 0);
            AddStock(faction, take, returned);
        }

        private int OwnFinishedMarketIndex(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Market) return i;
            }
            return -1;
        }

        /// <summary>Assigns the living villagers in an economy command to the deterministic first finished own market.</summary>
        private void StartTradeRoute(uint faction, IReadOnlyList<uint> villagers)
        {
            if (!AgesOn) return;
            int market = OwnFinishedMarketIndex(faction);
            if (market < 0) return;
            uint marketId = world.Buildings[market].Id;
            foreach (uint id in villagers)
            {
                if (id == 0 || id > world.VillagerCount) continue;
                ref var v = ref world.Villagers[id - 1];
                if (!v.Alive || v.FactionId != faction) continue;
                v.NodeId = 0;
                v.HaulFrom = 0;
                v.HaulTo = 0;
                v.BuildingId = marketId;
                v.Task = VillagerTask.ToTradeMarket;
                v.Route = System.Array.Empty<int>();
                v.RouteCursor = 0;
                v.RouteGoal = v.Position;
                if (IndustryOn) v.Held = true;
            }
        }

        /// <summary>Returns true while both ends of a route remain a living own core and finished own market.</summary>
        private bool TradeRouteActive(VillagerState v)
        {
            if (!AgesOn || !v.Alive || v.BuildingId == 0 || v.BuildingId > world.BuildingCount) return false;
            var market = world.Buildings[v.BuildingId - 1];
            return market.Alive && market.Complete && market.FactionId == v.FactionId && market.Kind == BuildingKind.Market
                && OwnCore(v.FactionId).Hp > 0;
        }

        private static bool IsTradeRouteTask(VillagerTask task)
            => task == VillagerTask.ToTradeMarket || task == VillagerTask.ToTradeCore;

        private void StopTradeRoute(ref VillagerState v)
        {
            v.Task = VillagerTask.Idle;
            v.NodeId = 0;
            v.BuildingId = 0;
            v.HaulFrom = 0;
            v.HaulTo = 0;
            v.Route = System.Array.Empty<int>();
            v.RouteCursor = 0;
            v.RouteGoal = v.Position;
            v.MoveGoal = v.Position;
        }

        /// <summary>Completes one end of the route. Wood is created only on arrival at the own core.</summary>
        private void AdvanceTradeRoute(ref VillagerState v)
        {
            if (!TradeRouteActive(v)) { StopTradeRoute(ref v); return; }
            var market = world.Buildings[v.BuildingId - 1];
            if (v.Task == VillagerTask.ToTradeMarket)
            {
                if (InRange(v.Position, world.Map.Center(market.WorkCell), GatherReach)) v.Task = VillagerTask.ToTradeCore;
                return;
            }
            if (v.Task != VillagerTask.ToTradeCore) return;
            if (!InRange(v.Position, OwnCore(v.FactionId).Definition.Position, world.Config.Rules.CoreRadius)) return;
            if (v.Carry > 0)
            {
                AddStock(v.FactionId, v.CarryKind, v.Carry);
                v.Carry = 0;
            }
            AddStock(v.FactionId, ResourceKind.Wood, world.Config.Economy.TradeRouteWood);
            v.Task = VillagerTask.ToTradeMarket;
            v.Route = System.Array.Empty<int>();
            v.RouteCursor = 0;
            v.RouteGoal = v.Position;
        }

        /// <summary>Automatic economy: one idle villager is assigned until two active routes exist.</summary>
        private void DecideTradeRoute(uint faction)
        {
            if (!AgesOn || SavingToAdvance(faction)) return;
            int market = OwnFinishedMarketIndex(faction);
            if (market < 0 || world.Buildings[market].Held) return;
            int active = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && IsTradeRouteTask(v.Task)) active++;
            }
            if (active >= AutoTradeVillagers) return;
            uint marketId = world.Buildings[market].Id;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || v.Task != VillagerTask.Idle) continue;
                v.NodeId = 0;
                v.HaulFrom = 0;
                v.HaulTo = 0;
                v.BuildingId = marketId;
                v.Task = VillagerTask.ToTradeMarket;
                v.Route = System.Array.Empty<int>();
                v.RouteCursor = 0;
                v.RouteGoal = v.Position;
                return;
            }
        }

        /// <summary>What a soldier deals to a building or a core: a ram its siege damage, everyone else their damage.</summary>
        private int SiegeDamage(SoldierState s)
        {
            if (s.Class != UnitKind.Ram) return s.Parameters.Damage;
            var rules = world.Config.Economy;
            return rules.RamSiegeDamage + (HasTech(s.Initial.FactionId, TechKind.Siegecraft) ? rules.SiegecraftSiegeDamage : 0);
        }

        /// <summary>
        /// AI phase, once the civilisation's line stands and a blacksmith too: a market; then, while one of food, wood and
        /// stone is TradeRich or more and another is under TradePoor, one lot from the richest to the poorest each cycle.
        /// </summary>
        private void DecideMarket(uint faction)
        {
            if (!AgesOn || !CivLineStarted(faction) || SavingToAdvance(faction)) return;
            var rules = world.Config.Economy;
            int market = OwnBuildingIndex(faction, BuildingKind.Market);
            if (market < 0)
            {
                if (world.Economies[faction - 1].Wood < rules.MarketWoodCost + TradePoor) return;
                int origin = FindSite(faction, rules.MarketSizeCells);
                if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Market, origin, Facing.North, 0);
                return;
            }
            if (!world.Buildings[market].Complete || world.Buildings[market].Held) return;
            var e = world.Economies[faction - 1];
            ResourceKind rich = 0, poor = 0;
            // Gems are a special final-research currency, not part of the three-resource balancing decision.
            foreach (var kind in new[] { ResourceKind.Food, ResourceKind.Wood, ResourceKind.Stone })
            {
                if (rich == 0 || StockOf(e, kind) > StockOf(e, rich)) rich = kind;
                if (poor == 0 || StockOf(e, kind) < StockOf(e, poor)) poor = kind;
            }
            if (rich != poor && StockOf(e, rich) >= TradeRich && StockOf(e, poor) < TradePoor) TradeAtMarket(faction, rich, poor);
        }

        /// <summary>AI phase, in the second age: a siege workshop, then up to AutoRams rams at a time.</summary>
        private void DecideSiege(uint faction)
        {
            if (!AgesOn || world.Economies[faction - 1].Age < 2 || SavingToAdvance(faction)) return;
            var rules = world.Config.Economy;
            int workshop = OwnBuildingIndex(faction, BuildingKind.SiegeWorkshop);
            if (workshop < 0)
            {
                if (world.Economies[faction - 1].Wood < rules.WorkshopWoodCost) return;
                int origin = FindSite(faction, rules.WorkshopSizeCells);
                if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.SiegeWorkshop, origin, Facing.North, 0);
                return;
            }
            ref var b = ref world.Buildings[workshop];
            if (!b.Complete || b.Held || b.Queued > 0 || CountClass(faction, UnitKind.Ram) >= AutoRams) return;
            if (HasRoomFor(faction, UnitKind.Ram) && CanPay(faction, UnitKind.Ram)) Enqueue(faction, ref b, UnitKind.Ram);
        }
    }
}
