using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private void ApplyIncenseHealing()
        {
            if (!IncenseOn) return;
            var rules = world.Config.Economy;
            if (rules.IncenseHealIntervalTicks <= 0 || world.Tick <= 0 || world.Tick % rules.IncenseHealIntervalTicks != 0) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                if (!IncenseAllowed(faction)) continue;
                ref var economy = ref world.Economies[faction - 1];
                if (economy.Incense < 1) continue;
                int best = -1;
                int bestMissing = 0;
                for (int i = 0; i < world.SoldierCount; i++)
                {
                    ref var soldier = ref world.Soldiers[i];
                    if (!soldier.Alive || soldier.Initial.FactionId != faction
                        || soldier.Initial.Kind == UnitKind.Villager || soldier.Initial.Kind == UnitKind.Monk
                        || soldier.Hp >= soldier.Parameters.Hp) continue;
                    int missing = soldier.Parameters.Hp - soldier.Hp;
                    if (best < 0 || missing > bestMissing || missing == bestMissing && soldier.Initial.Id < world.Soldiers[best].Initial.Id)
                    {
                        best = i;
                        bestMissing = missing;
                    }
                }
                if (best < 0) continue;
                ref var target = ref world.Soldiers[best];
                int before = target.Hp;
                target.Hp = Math.Min(target.Parameters.Hp, checked(target.Hp + rules.IncenseHealHp));
                economy.Incense--;
                economy.IncenseConsumed++;
                economy.IncenseHealedHp = checked(economy.IncenseHealedHp + target.Hp - before);
            }
        }
    }
}
