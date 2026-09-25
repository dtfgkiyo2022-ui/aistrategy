using Rts.Contracts;

namespace Rts.Decision
{
    /// <summary>
    /// V3-5 counters (technical-design-v3 32 #13). One triangle among the line units: archers beat infantry, cavalry beats
    /// archers, infantry beats cavalry. Rams and scouts stand outside it, and a soldier of no class counts as infantry,
    /// so a map without ages (where no soldier has a class) keeps every number it had.
    /// </summary>
    public static class CombatMath
    {
        /// <summary>True when <paramref name="attacker"/> has the edge over <paramref name="target"/> in the triangle.</summary>
        public static bool Counters(UnitKind attacker, UnitKind target)
        {
            var a = Line(attacker);
            var t = Line(target);
            if (a == 0 || t == 0) return false;
            return (a == UnitKind.Archer && t == UnitKind.Infantry)
                || (a == UnitKind.Cavalry && t == UnitKind.Archer)
                || (a == UnitKind.Infantry && t == UnitKind.Cavalry);
        }

        /// <summary>
        /// The damage one blow deals: <paramref name="damage"/>, raised by <paramref name="bonusPermille"/> thousandths
        /// when the attacker counters the target. Integer arithmetic only, so every machine gets the same number.
        /// </summary>
        public static int DamageAgainst(int damage, UnitKind attacker, UnitKind target, int bonusPermille)
        {
            if (bonusPermille <= 0 || !Counters(attacker, target)) return damage;
            return (int)((long)damage * (1000 + bonusPermille) / 1000);
        }

        /// <summary>Infantry, archer or cavalry - the three in the triangle. 0 for a scout, ram, mercenary, monk or villager.</summary>
        private static UnitKind Line(UnitKind kind)
            => kind == 0 || kind == UnitKind.Infantry ? UnitKind.Infantry
                : kind == UnitKind.Archer || kind == UnitKind.Cavalry ? kind
                : kind == UnitKind.Scout || kind == UnitKind.Ram || kind == UnitKind.Mercenary || kind == UnitKind.Monk ? (UnitKind)0 : (UnitKind)0;
    }
}
