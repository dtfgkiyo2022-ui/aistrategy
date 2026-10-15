using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>Craft balance (32.38): reserve wood for the automatic goods market for a bounded time.</summary>
    public sealed partial class Simulation
    {
        private bool CraftBalanceHasSellableGoods(uint faction)
        {
            var economy = world.Economies[faction - 1];
            var rules = world.Config.Economy;
            foreach (var goods in GoodsKinds)
            {
                int cap = GoodsCap(rules, goods);
                int threshold = cap > 0 ? System.Math.Min(rules.GoodsAutoSellAt, cap) : rules.GoodsAutoSellAt;
                int stock = StockOf(economy, goods);
                if (stock >= threshold && stock - rules.GoodsTradeLot >= rules.GoodsAutoKeep) return true;
            }
            return false;
        }

        /// <summary>Begins or continues the bounded save. A negative start marks a timed-out opportunity.</summary>
        private bool CraftBalanceMarketSaving(uint faction)
        {
            if (!CraftBalanceOn || world.Economies[faction - 1].AutoOff) return false;
            ref var economy = ref world.Economies[faction - 1];
            if (OwnBuildingIndex(faction, BuildingKind.Market) >= 0)
            {
                economy.CraftBalanceMarketSaveStartedTick = 0;
                return false;
            }
            if (SavingToAdvance(faction) || !CraftBalanceHasSellableGoods(faction))
            {
                economy.CraftBalanceMarketSaveStartedTick = 0;
                return false;
            }
            if (economy.Wood >= world.Config.Economy.MarketWoodCost + world.Config.Economy.GoodsMarketWoodReserve)
            {
                economy.CraftBalanceMarketSaveStartedTick = 0;
                return false;
            }
            if (economy.CraftBalanceMarketSaveStartedTick < 0) return false;
            if (economy.CraftBalanceMarketSaveStartedTick == 0) economy.CraftBalanceMarketSaveStartedTick = world.Tick;
            if (world.Tick - economy.CraftBalanceMarketSaveStartedTick >= world.Config.Economy.CraftBalanceMarketSaveTicks)
            {
                economy.CraftBalanceMarketSaveStartedTick = -1;
                return false;
            }
            return true;
        }

        /// <summary>Wood uses that may wait for the market. Barracks and all training paths remain outside this gate.</summary>
        private bool CraftBalanceShouldDelayWood(uint faction) => CraftBalanceMarketSaving(faction);
    }
}
