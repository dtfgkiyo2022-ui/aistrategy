using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private void DecideLivelihood(uint faction)
        {
            if (!LivelihoodOn) return;
            int villagers = LivingVillagers(faction);
            int preservesTarget = LivelihoodAiDue(faction, 2) ? LivelihoodFactoryTarget(faction, ResourceKind.Preserves, villagers, 20) : 0;
            int toolsTarget = LivelihoodAiDue(faction, 2) ? LivelihoodFactoryTarget(faction, ResourceKind.Tools, villagers, 15) : 0;
            int tradeGoodsTarget = LivelihoodAiDue(faction, 3)
                ? LivelihoodFactoryTarget(faction, ResourceKind.TradeGoods, villagers, 15) : 0;
            int incenseTarget = LivelihoodAiDue(faction, 4)
                ? LivelihoodFactoryTarget(faction, ResourceKind.Incense, villagers, 15) : 0;
            EnsureLivelihoodFactories(faction, BuildingKind.DryingRack, preservesTarget, CountLivelihoodBuildings(faction, BuildingKind.DryingRack), DecidePreserves);
            EnsureLivelihoodFactories(faction, BuildingKind.ToolShop, toolsTarget, CountLivelihoodBuildings(faction, BuildingKind.ToolShop), DecideTools);
            EnsureLivelihoodFactories(faction, BuildingKind.Weaver, tradeGoodsTarget, CountLivelihoodBuildings(faction, BuildingKind.Weaver), DecideTradeGoods);
            EnsureLivelihoodFactories(faction, BuildingKind.IncenseHouse, incenseTarget, CountLivelihoodBuildings(faction, BuildingKind.IncenseHouse), DecideIncense);
        }

        private bool LivelihoodAiDue(uint faction, int requiredAge)
        {
            int age = world.Economies[faction - 1].Age;
            if (age >= requiredAge) return true;
            if (!AgeClockOn || age + 1 != requiredAge) return false;
            long next = NextAgeClockTick(faction);
            return next != 0 && next - world.Tick <= world.Config.Economy.LivelihoodDecisionIntervalTicks;
        }

        private int LivelihoodFactoryTarget(uint faction, ResourceKind kind, int villagers, int outputPerMinute)
        {
            int required = LivelihoodNeed(faction, kind, villagers);
            int margin = world.Config.Economy.LivelihoodAiMarginPermille;
            return checked((required * margin + outputPerMinute * 1000 - 1) / (outputPerMinute * 1000));
        }

        private int CountLivelihoodBuildings(uint faction, BuildingKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (IsPlannedBuilding(building) && building.FactionId == faction && building.Kind == kind) count++;
            }
            return count;
        }

        private void EnsureLivelihoodFactories(uint faction, BuildingKind kind, int target, int current, Action<uint> decide)
        {
            while (current < target)
            {
                int before = CountLivelihoodBuildings(faction, kind);
                decide(faction);
                int after = CountLivelihoodBuildings(faction, kind);
                if (after <= before) break;
                current = after;
            }
        }
    }
}
