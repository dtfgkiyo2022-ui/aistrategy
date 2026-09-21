using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// Measurement helper for stage 5: repeats every soldier of a scenario <c>factor</c> times and raises the faction and
    /// army caps to match. Integer meters only, so it stays inside the deterministic rules. It is a test fixture, not a
    /// game rule: nothing in a normal match calls it.
    /// </summary>
    public static class ScenarioScale
    {
        // Copies are spread along z so that no two start on the same spot.
        private const int CopySpacingMeters = 2;

        public static ScenarioDefinition Multiply(ScenarioDefinition s, int factor)
        {
            if (factor < 1) throw new System.ArgumentOutOfRangeException(nameof(factor));
            if (factor == 1) return s;
            s.ScenarioId = s.ScenarioId + "-x" + factor;
            s.Rules.FactionCap *= factor;
            for (int i = 0; i < s.Armies.Length; i++) s.Armies[i].Capacity *= factor;
            var scaled = new SoldierDefinition[s.Soldiers.Length * factor];
            uint id = 1;
            // Army by army, so that ids stay grouped the way the base scenario groups them.
            for (int a = 0; a < s.Armies.Length; a++)
            {
                uint armyId = s.Armies[a].Id;
                for (int copy = 0; copy < factor; copy++)
                {
                    int shift = (copy - (factor - 1) / 2) * CopySpacingMeters;
                    foreach (var d in s.Soldiers)
                    {
                        if (d.ArmyId != armyId) continue;
                        scaled[id - 1] = new SoldierDefinition
                        {
                            Id = id, FactionId = d.FactionId, ArmyId = d.ArmyId, Kind = d.Kind, Alive = d.Alive, Hp = d.Hp,
                            Position = new SimPoint(d.Position.X, d.Position.Z + Fix64.FromInt(shift))
                        };
                        id++;
                    }
                }
            }
            s.Soldiers = scaled;
            return s;
        }
    }
}
