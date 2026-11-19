using System;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private void ApplySoldierRegen()
        {
            if (!SoldierRegenOn) return;
            var rules = world.Config.Economy;
            if (rules.SoldierRegenIntervalTicks <= 0 || world.Tick <= 0 || world.Tick % rules.SoldierRegenIntervalTicks != 0) return;
            for (int i = 0; i < world.SoldierCount; i++)
            {
                ref var soldier = ref world.Soldiers[i];
                if (!soldier.Alive || soldier.Initial.Kind == Rts.Contracts.UnitKind.Villager || soldier.Hp >= soldier.Parameters.Hp) continue;
                long lastCombat = Math.Max(soldier.LastDamagedTick, soldier.LastAttackTick);
                if (lastCombat >= 0 && world.Tick - lastCombat < rules.SoldierRegenDelayTicks) continue;
                int amount = Math.Max(1, (soldier.Parameters.Hp * rules.SoldierRegenPermille + 999) / 1000);
                soldier.Hp = Math.Min(soldier.Parameters.Hp, checked(soldier.Hp + amount));
            }
        }
    }
}
