using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        /// <summary>
        /// The explicit civilisation order is part of the rules: lower Priority wins an equal score. Disabled rows
        /// remain in the table with score zero, so an optional civilisation cannot steal an older zero-score tie.
        /// </summary>
        private readonly struct CivRegistration
        {
            public CivKind Civ { get; }
            public int Priority { get; }
            public bool UsesFoodMarket { get; }
            public Func<Simulation, uint, bool> Enabled { get; }
            public Func<Simulation, uint, SimPoint, int, int, int> Score { get; }
            public Func<Simulation, uint, bool> LineStarted { get; }

            public CivRegistration(CivKind civ, int priority, bool usesFoodMarket,
                Func<Simulation, uint, bool> enabled,
                Func<Simulation, uint, SimPoint, int, int, int> score,
                Func<Simulation, uint, bool> lineStarted)
            {
                Civ = civ;
                Priority = priority;
                UsesFoodMarket = usesFoodMarket;
                Enabled = enabled;
                Score = score;
                LineStarted = lineStarted;
            }
        }

        /// <summary>
        /// One row per selectable civilisation. Score callbacks only point at the existing scoring functions; the
        /// scoring functions themselves stay unchanged. Agrarian and metallurgy are enabled by Ages alone.
        /// </summary>
        private static readonly CivRegistration[] CivRegistrations =
        {
            new CivRegistration(CivKind.Agrarian, 0, false, (s, f) => s.AgesOn, AgrarianScore,
                (s, f) => s.OwnBuildingIndex(f, BuildingKind.Farm) >= 0),
            new CivRegistration(CivKind.Metallurgy, 1, true, (s, f) => s.AgesOn, MetallurgyScore,
                (s, f) => s.OwnBuildingIndex(f, BuildingKind.Mine) >= 0 && s.OwnBuildingIndex(f, BuildingKind.Smelter) >= 0),
            new CivRegistration(CivKind.Forestry, 2, true, (s, f) => s.ForestryOn, ForestryScore,
                (s, f) => s.ForestryAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.LumberCamp) >= 0),
            new CivRegistration(CivKind.Masonry, 3, true, (s, f) => s.MasonryOn, MasonryScore,
                (s, f) => s.MasonryAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Quarry) >= 0),
            new CivRegistration(CivKind.Caravan, 4, true, (s, f) => s.CaravanOn, CaravanScore,
                (s, f) => s.CaravanAllowed(f) && s.OwnFinishedMarketIndex(f) >= 0),
            new CivRegistration(CivKind.Cavalry, 5, true, (s, f) => s.CavalryOn, CavalryScore,
                (s, f) => s.CavalryAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Stable) >= 0),
            new CivRegistration(CivKind.Bridge, 6, true, (s, f) => s.BridgeOn, BridgeScore,
                (s, f) => s.BridgeAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.EngineerCamp) >= 0),
            new CivRegistration(CivKind.Academy, 7, true, (s, f) => s.AcademyOn, AcademyScore,
                (s, f) => s.AcademyAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Academy) >= 0),
            new CivRegistration(CivKind.Cult, 8, true, (s, f) => s.CultOn, CultScore,
                (s, f) => s.CultAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Monastery) >= 0)
        };

        private static int AgrarianScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => food > GuaranteedFoodPoints ? food - GuaranteedFoodPoints : 0;

        private static int MetallurgyScore(Simulation s, uint faction, SimPoint core, int ore, int food) => ore;

        private static int ForestryScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountUsableForestWood(faction, core);

        private static int MasonryScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountUsableMasonryStone(faction, core);

        private static int CaravanScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountUsableCaravanOutposts(faction, core);

        private static int CavalryScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountCavalryMobility(faction, core);

        private static int BridgeScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountUsableBridgeSaving(faction, core);

        private static int AcademyScore(Simulation s, uint faction, SimPoint core, int ore, int food) => 0;

        private static int CultScore(Simulation s, uint faction, SimPoint core, int ore, int food) => 0;

        private bool TryGetCivRegistration(uint faction, out CivRegistration registration)
        {
            CivKind civ = world.Economies[faction - 1].Civ;
            for (int i = 0; i < CivRegistrations.Length; i++)
                if (CivRegistrations[i].Civ == civ)
                {
                    registration = CivRegistrations[i];
                    return true;
                }
            registration = default(CivRegistration);
            return false;
        }

        private bool CivUsesFoodMarket(uint faction)
            => TryGetCivRegistration(faction, out CivRegistration row) && row.UsesFoodMarket;

        private bool CivEnabled(uint faction, CivKind civ)
        {
            for (int i = 0; i < CivRegistrations.Length; i++)
                if (CivRegistrations[i].Civ == civ) return CivRegistrations[i].Enabled(this, faction);
            return false;
        }

        private bool CivLineStartedFromRegistry(uint faction)
            => TryGetCivRegistration(faction, out CivRegistration row) && row.LineStarted(this, faction);
    }
}
