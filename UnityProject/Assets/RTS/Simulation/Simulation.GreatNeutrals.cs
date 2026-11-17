using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private int GreatLordAttackDamage(uint faction, int damage)
        {
            if (!GreatOn || faction < 1 || faction > 2 || world.Factions[faction - 1].GreatLordBlessingUntil <= world.Tick) return damage;
            return checked(damage * 120 / 100);
        }

        private int GreatLordBuildingDamage(uint faction, int damage)
        {
            if (!GreatOn || faction < 1 || faction > 2 || world.Factions[faction - 1].GreatLordBlessingUntil <= world.Tick) return damage;
            return checked(damage * 150 / 100);
        }

        private int GreatLordDefense(uint faction, int defense)
        {
            if (!GreatOn || faction < 1 || faction > 2 || world.Factions[faction - 1].GreatLordBlessingUntil <= world.Tick) return defense;
            return checked(defense + 15);
        }

        private int DragonMovePermille(uint faction)
        {
            if (!GreatOn || faction < 1 || faction > 2 || world.Factions[faction - 1].DragonMarks == null) return 0;
            return world.Factions[faction - 1].DragonMarks[(int)DragonElement.Wind] > 0 ? 40 : 0;
        }

        private int DragonResourcePermille(uint faction)
        {
            if (!GreatOn || faction < 1 || faction > 2 || world.Factions[faction - 1].DragonMarks == null) return 1000;
            return world.Factions[faction - 1].DragonMarks[(int)DragonElement.Water] > 0 ? 1150 : 1000;
        }

        private int GreatSkillCooldown(uint faction, int cooldown)
        {
            if (!GreatOn || faction < 1 || faction > 2 || !world.Factions[faction - 1].DragonSoul) return cooldown;
            return Math.Max(1, checked((cooldown * 90 + 99) / 100));
        }
    }
}
