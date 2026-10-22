using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private void ApplyRationsHealing()
        {
            if (!RationsOn) return;
            var rules = world.Config.Economy;
            if (rules.RationsHealIntervalTicks <= 0 || world.Tick <= 0 || world.Tick % rules.RationsHealIntervalTicks != 0) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                if (!RationsAllowed(faction)) continue;
                ref var economy = ref world.Economies[faction - 1];
                int healed = 0;
                for (int i = 0; i < world.SoldierCount && healed < rules.RationsHealMaxPerInterval; i++)
                {
                    ref var soldier = ref world.Soldiers[i];
                    if (!soldier.Alive || soldier.Initial.FactionId != faction
                        || soldier.Initial.Kind == UnitKind.Villager || soldier.Initial.Kind == UnitKind.Scout
                        || soldier.Hp >= soldier.Parameters.Hp
                        || world.Tick - soldier.LastCombatTick < rules.RationsHealQuietTicks
                        || economy.Rations < 1) continue;
                    economy.Rations = checked(economy.Rations - 1);
                    economy.RationsConsumed++;
                    int before = soldier.Hp;
                    int heal = AgeStrength(faction, CivKind.Agrarian, 3) ? rules.AgrarianRationsHealHp : rules.RationsHealHp;
                    soldier.Hp = Math.Min(soldier.Parameters.Hp, checked(soldier.Hp + heal));
                    economy.RationsHealedHp = checked(economy.RationsHealedHp + soldier.Hp - before);
                    healed++;
                }
            }
        }
    }
}
