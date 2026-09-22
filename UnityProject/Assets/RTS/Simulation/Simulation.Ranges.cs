using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 the archery range and the stable (technical-design-v3 32 #12). From the second age either civilisation can put
    /// one up and field the unit its own civilisation never trains - farming gets cavalry, metallurgy gets archers. They
    /// fight as infantry with their own numbers, exactly as the ones a barracks trains (32 #8). Maps with ages only.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>One of these for every AutoPerCross line soldiers, so the mix stays a garnish rather than the army.</summary>
        private const int AutoPerCross = 8;

        /// <summary>The unit a faction's own civilisation does not train: cavalry for farming, archers for metallurgy.</summary>
        private UnitKind CrossUnit(uint faction)
        {
            var civ = world.Economies[faction - 1].Civ;
            return civ == CivKind.Agrarian ? UnitKind.Cavalry : civ == CivKind.Metallurgy ? UnitKind.Archer : 0;
        }

        private static BuildingKind HouseOf(UnitKind unit) => unit == UnitKind.Cavalry ? BuildingKind.Stable : BuildingKind.ArcheryRange;

        /// <summary>
        /// AI phase, in the second age and not saving: the building for the unit the civilisation lacks, then one of that
        /// unit for every AutoPerCross line soldiers.
        /// </summary>
        private void DecideCrossUnit(uint faction)
        {
            if (!AgesOn || world.Economies[faction - 1].Age < 2 || SavingToAdvance(faction)) return;
            var unit = CrossUnit(faction);
            if (unit == 0) return;
            var kind = HouseOf(unit);
            var rules = world.Config.Economy;
            int index = OwnBuildingIndex(faction, kind);
            if (index < 0)
            {
                if (world.Economies[faction - 1].Wood < WoodOf(kind)) return;
                int origin = FindSite(faction, SizeOf(kind));
                if (origin >= 0) PlaceBuildingAt(faction, kind, origin, Facing.North, 0);
                return;
            }
            ref var b = ref world.Buildings[index];
            if (!b.Complete || b.Held || b.Queued > 0) return;
            if (AutoPerCross * CountClass(faction, unit) >= CountClass(faction, UnitKind.Infantry)) return;
            if (HasRoomFor(faction, unit) && CanPay(faction, unit)) Enqueue(faction, ref b, unit);
        }
    }
}
