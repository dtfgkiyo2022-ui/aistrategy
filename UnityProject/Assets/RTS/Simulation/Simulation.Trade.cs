using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 markets and siege (technical-design-v3 32 #9). A finished own market trades TradeLot of food, wood or stone for
    /// TradeReturn of another. The siege workshop (second age) trains rams: they fight as infantry, but strike buildings
    /// and cores with RamSiegeDamage. Maps with ages only.
    /// </summary>
    public sealed partial class Simulation
    {
        private const int TradeRich = 600, TradePoor = 150, AutoRams = 2;

        private static bool Tradable(ResourceKind kind) => kind == ResourceKind.Food || kind == ResourceKind.Wood || kind == ResourceKind.Stone;

        private static int StockOf(FactionEconomy e, ResourceKind kind)
            => kind == ResourceKind.Food ? e.Food : kind == ResourceKind.Wood ? e.Wood : kind == ResourceKind.Stone ? e.Stone : 0;

        private void TradeAtMarket(uint faction, ResourceKind give, ResourceKind take)
        {
            if (!AgesOn || give == take || !Tradable(give) || !Tradable(take)) return;
            int market = OwnBuildingIndex(faction, BuildingKind.Market);
            if (market < 0 || !world.Buildings[market].Complete) return;
            var rules = world.Config.Economy;
            if (StockOf(world.Economies[faction - 1], give) < rules.TradeLot) return;
            AddStock(faction, give, -rules.TradeLot);
            AddStock(faction, take, rules.TradeReturn);
        }

        /// <summary>What a soldier deals to a building or a core: a ram its siege damage, everyone else their damage.</summary>
        private int SiegeDamage(SoldierState s) => s.Class == UnitKind.Ram ? world.Config.Economy.RamSiegeDamage : s.Parameters.Damage;

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
