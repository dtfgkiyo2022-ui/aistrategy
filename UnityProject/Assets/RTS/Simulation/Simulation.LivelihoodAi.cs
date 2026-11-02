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
            EnsureLivelihoodFactories(faction, BuildingKind.DryingRack, ProcessingLineKind.Preserves, preservesTarget, CountLivelihoodBuildings(faction, BuildingKind.DryingRack), DecidePreserves);
            EnsureLivelihoodFactories(faction, BuildingKind.ToolShop, ProcessingLineKind.Tools, toolsTarget, CountLivelihoodBuildings(faction, BuildingKind.ToolShop), DecideTools);
            EnsureLivelihoodFactories(faction, BuildingKind.Weaver, ProcessingLineKind.TradeGoods, tradeGoodsTarget, CountLivelihoodBuildings(faction, BuildingKind.Weaver), DecideTradeGoods);
            EnsureLivelihoodFactories(faction, BuildingKind.IncenseHouse, ProcessingLineKind.Incense, incenseTarget, CountLivelihoodBuildings(faction, BuildingKind.IncenseHouse), DecideIncense);
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
            int target = checked((required * margin + outputPerMinute * 1000 - 1) / (outputPerMinute * 1000));
            return Math.Min(world.Config.Economy.LivelihoodMaxLines, target);
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

        private void EnsureLivelihoodFactories(uint faction, BuildingKind kind, ProcessingLineKind lineKind, int target, int current, Action<uint, int> decide)
        {
            // Review 11-02: a line's decision also lays its belt to the core and sets its haulers once the building
            // stands. The extra lines were decided only while being placed, so their goods piled up at 10 in the
            // building and never reached the stock (seed 1: the west's two racks never paid a single preserve).
            // Look after every existing line at each livelihood decision.
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var existing = world.ProcessingLines[i];
                if (existing.FactionId == faction && existing.Kind == lineKind && !existing.ArmsFlow)
                    decide(faction, existing.LivelihoodLineNumber);
            }
            current = CountLivelihoodBuildings(faction, kind);
            while (current < target)
            {
                int before = CountLivelihoodBuildings(faction, kind);
                decide(faction, current);
                int after = CountLivelihoodBuildings(faction, kind);
                if (after <= before) break;
                current = after;
            }
        }
    }
}
