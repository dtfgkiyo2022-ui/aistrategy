using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>38.27: deterministic, once-per-minute livelihood payments and population effects.</summary>
    public sealed partial class Simulation
    {
        private const int LivelihoodPreservesBit = 1;
        private const int LivelihoodToolsBit = 2;
        private const int LivelihoodTradeGoodsBit = 4;
        private const int LivelihoodIncenseBit = 8;
        private const int LivelihoodAleBit = 16;

        private int LivelihoodNeed(uint faction, ResourceKind kind, int villagers)
        {
            var rules = world.Config.Economy;
            int per100 = kind == ResourceKind.Ale ? rules.FluidAlePer100
                : kind == ResourceKind.Preserves ? rules.LivelihoodPreservesPer100
                : kind == ResourceKind.Tools ? rules.LivelihoodToolsPer100
                : kind == ResourceKind.TradeGoods ? rules.LivelihoodTradeGoodsPer100
                : rules.LivelihoodIncensePer100;
            var civ = world.Economies[faction - 1].Civ;
            bool civBonus = (kind == ResourceKind.Ale && civ == CivKind.Agrarian)
                || (kind == ResourceKind.Preserves && civ == CivKind.Fishing)
                || (kind == ResourceKind.TradeGoods && civ == CivKind.Caravan)
                || (kind == ResourceKind.Incense && civ == CivKind.Cult)
                || (kind == ResourceKind.Tools && civ == CivKind.Metropolis);
            if (civBonus) per100 = (per100 + 1) / 2;
            return checked((villagers * per100 + 99) / 100);
        }

        private int LivelihoodRequiredFor(byte age)
            => !LivelihoodOn || age < 2 ? 0 : 2 + (age >= 3 ? 1 : 0) + (age >= 4 ? 1 : 0)
                + (FluidsOn && age >= 3 ? 1 : 0);

        private int LivelihoodStock(uint faction, ResourceKind kind)
        {
            var economy = world.Economies[faction - 1];
            return kind == ResourceKind.Preserves ? economy.Preserves
                : kind == ResourceKind.Tools ? economy.Tools
                : kind == ResourceKind.TradeGoods ? economy.TradeGoods
                : kind == ResourceKind.Incense ? economy.Incense : economy.Ale;
        }

        private int LivelihoodStockLimit(uint faction, ResourceKind kind)
        {
            var rules = world.Config.Economy;
            int baseLimit = kind == ResourceKind.Ale ? 30 : kind == ResourceKind.Tools ? rules.ToolStockLimit : rules.IncenseStockLimit;
            int threeMinuteNeed = checked(3 * LivelihoodNeed(faction, kind, world.Economies[faction - 1].LivelihoodVillagers));
            return Math.Max(baseLimit, threeMinuteNeed);
        }

        /// <summary>
        /// 38.27 (review 11-02): preserves and trade goods had no stock limit before, because every one was turned into
        /// food or stone at once. Kept as livelihood goods they would pile up without end and the drying racks would
        /// keep eating food; stop at three minutes of need (at least 10). Fishing and caravans turn the surplus over,
        /// so their stock never passes the need.
        /// </summary>
        private bool LivelihoodGoodFull(uint faction, ResourceKind kind)
        {
            if (!LivelihoodOn) return false;
            if (kind == ResourceKind.Preserves && LivelihoodPreserveEffectOn(faction)) return false;
            if (kind == ResourceKind.TradeGoods && LivelihoodTradeGoodsEffectOn(faction)) return false;
            if (kind != ResourceKind.Preserves && kind != ResourceKind.TradeGoods) return false;
            int limit = Math.Max(10, checked(3 * LivelihoodNeed(faction, kind, world.Economies[faction - 1].LivelihoodVillagers)));
            return LivelihoodStock(faction, kind) >= limit;
        }

        private void PayLivelihood(uint faction, ResourceKind kind, int amount, int bit, ref FactionEconomy economy)
        {
            if (kind == ResourceKind.Preserves) economy.Preserves = checked(economy.Preserves - amount);
            else if (kind == ResourceKind.Tools) economy.Tools = checked(economy.Tools - amount);
            else if (kind == ResourceKind.TradeGoods) economy.TradeGoods = checked(economy.TradeGoods - amount);
            else if (kind == ResourceKind.Incense) economy.Incense = checked(economy.Incense - amount);
            else economy.Ale = checked(economy.Ale - amount);
            economy.LivelihoodMetCount++;
            economy.LivelihoodShortGoods &= ~bit;
            if (kind == ResourceKind.Preserves) economy.LivelihoodPreservesPaid = checked(economy.LivelihoodPreservesPaid + amount);
            else if (kind == ResourceKind.Tools) economy.LivelihoodToolsPaid = checked(economy.LivelihoodToolsPaid + amount);
            else if (kind == ResourceKind.TradeGoods) economy.LivelihoodTradeGoodsPaid = checked(economy.LivelihoodTradeGoodsPaid + amount);
            else if (kind == ResourceKind.Incense) economy.LivelihoodIncensePaid = checked(economy.LivelihoodIncensePaid + amount);
            else economy.AlePaid = checked(economy.AlePaid + amount);
        }

        private void UpdateLivelihoodPayments()
        {
            if (!LivelihoodOn || world.Tick <= 0 || world.Tick % world.Config.Economy.LivelihoodDecisionIntervalTicks != 0) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                ref var economy = ref world.Economies[faction - 1];
                byte age = economy.Age;
                economy.LivelihoodVillagers = LivingVillagers(faction);
                if (economy.LivelihoodObservedAge != age)
                {
                    // The first minute after an age transition retains the previous result.
                    economy.LivelihoodObservedAge = age;
                    economy.LivelihoodAgeChangedTick = world.Tick;
                    continue;
                }
                int required = LivelihoodRequiredFor(age);
                economy.LivelihoodRequiredCount = required;
                economy.LivelihoodMetCount = 0;
                economy.LivelihoodShortGoods = 0;
                if (required == 0)
                {
                    economy.LivelihoodMet = true;
                    continue;
                }
                int villagers = LivingVillagers(faction);
                int preserves = LivelihoodNeed(faction, ResourceKind.Preserves, villagers);
                int tools = LivelihoodNeed(faction, ResourceKind.Tools, villagers);
                int tradeGoods = LivelihoodNeed(faction, ResourceKind.TradeGoods, villagers);
                int incense = LivelihoodNeed(faction, ResourceKind.Incense, villagers);
                int ale = LivelihoodNeed(faction, ResourceKind.Ale, villagers);
                if (economy.Preserves >= preserves) PayLivelihood(faction, ResourceKind.Preserves, preserves, LivelihoodPreservesBit, ref economy);
                else economy.LivelihoodShortGoods |= LivelihoodPreservesBit;
                if (economy.Tools >= tools) PayLivelihood(faction, ResourceKind.Tools, tools, LivelihoodToolsBit, ref economy);
                else economy.LivelihoodShortGoods |= LivelihoodToolsBit;
                if (age >= 3)
                {
                    if (economy.TradeGoods >= tradeGoods) PayLivelihood(faction, ResourceKind.TradeGoods, tradeGoods, LivelihoodTradeGoodsBit, ref economy);
                    else economy.LivelihoodShortGoods |= LivelihoodTradeGoodsBit;
                }
                if (age >= 4)
                {
                    if (economy.Incense >= incense) PayLivelihood(faction, ResourceKind.Incense, incense, LivelihoodIncenseBit, ref economy);
                    else economy.LivelihoodShortGoods |= LivelihoodIncenseBit;
                }
                if (FluidsOn && age >= 3)
                {
                    if (economy.Ale >= ale) PayLivelihood(faction, ResourceKind.Ale, ale, LivelihoodAleBit, ref economy);
                    else economy.LivelihoodShortGoods |= LivelihoodAleBit;
                }
                else if (FluidsOn && age >= 2 && economy.Ale > 0)
                {
                    // Keep the tactic-match usage counter observable before the normal age-three livelihood tier.
                    economy.Ale--;
                    economy.AlePaid++;
                }
                economy.LivelihoodMet = economy.LivelihoodMetCount == required;
            }
        }

        private int LivelihoodPopulationPermille(uint faction)
        {
            if (!LivelihoodOn || world.Economies[faction - 1].Age < 2) return 1000;
            var economy = world.Economies[faction - 1];
            if (economy.LivelihoodMet || economy.LivelihoodRequiredCount <= 0) return 1000;
            return checked(world.Config.Economy.LivelihoodUnmetHousePermille
                + (1000 - world.Config.Economy.LivelihoodUnmetHousePermille) * economy.LivelihoodMetCount / economy.LivelihoodRequiredCount);
        }

        private int LivelihoodHousePopulation(uint faction, int basePopulation)
            => checked(basePopulation * LivelihoodPopulationPermille(faction) / 1000);

        private int LivelihoodRequiredForDisplay(uint faction, ResourceKind kind)
            => LivelihoodNeed(faction, kind, LivingVillagers(faction));
    }
}
