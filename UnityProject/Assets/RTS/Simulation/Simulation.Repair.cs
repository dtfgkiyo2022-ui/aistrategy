using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 repair (technical-design-v3 32 #15). A villager sent to a finished building that has been hurt puts HP back
    /// into it, RepairHpPerTick each tick, and goes idle once it is whole again. Nothing is paid for it: the wood went in
    /// when the building was placed. Maps with ages only, so the older maps keep every tick they had.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>A finished building of this faction below this share of its HP is worth sending villagers to.</summary>
        private bool NeedsRepair(BuildingState b) => AgesOn && b.Alive && b.Complete && b.Kind != BuildingKind.Wall && b.Hp < HpOf(b.Kind);

        /// <summary>Economy step, before the building work: every villager repairing adds its HP, never past the top.</summary>
        private void RepairBuildings()
        {
            if (!AgesOn) return;
            int perTick = world.Config.Economy.RepairHpPerTick;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!NeedsRepair(b)) continue;
                int max = HpOf(b.Kind);
                for (int j = 0; j < world.VillagerCount && b.Hp < max; j++)
                {
                    var v = world.Villagers[j];
                    if (v.Alive && v.Task == VillagerTask.Building && v.BuildingId == b.Id)
                        b.Hp = b.Hp + perTick > max ? max : b.Hp + perTick;
                }
                if (b.Hp < max) continue;
                for (int j = 0; j < world.VillagerCount; j++)
                    if (world.Villagers[j].BuildingId == b.Id
                        && (world.Villagers[j].Task == VillagerTask.Building || world.Villagers[j].Task == VillagerTask.ToBuild))
                        world.Villagers[j].Task = VillagerTask.Idle;
            }
        }

        /// <summary>
        /// AI phase: the worst hurt of its finished buildings, once it is under RepairAtPermille of its HP, gets builders.
        /// One building at a time, so repairing never empties the resource points.
        /// </summary>
        private void DecideRepair(uint faction)
        {
            if (!AgesOn) return;
            int worst = -1;
            long worstShare = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.FactionId != faction || b.Held || !NeedsRepair(b)) continue;
                long share = 1000L * b.Hp / HpOf(b.Kind);
                if (share >= world.Config.Economy.RepairAtPermille) continue;
                if (worst < 0 || share < worstShare) { worst = i; worstShare = share; }
            }
            if (worst < 0) return;
            for (int j = 0; j < world.VillagerCount; j++)
            {
                var v = world.Villagers[j];
                if (v.Alive && v.BuildingId == world.Buildings[worst].Id
                    && (v.Task == VillagerTask.ToBuild || v.Task == VillagerTask.Building)) return; // someone is on it
            }
            AssignBuilders(ref world.Buildings[worst]);
        }
    }
}
