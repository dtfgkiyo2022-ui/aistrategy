using System;
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
        private bool RepairUnderAttackOn => world.Config.Economy.Enabled && world.Config.Economy.Ages && world.Config.Economy.RepairUnderAttack;

        internal int RepairSlowedTicks(uint faction)
        {
            if (faction < 1 || faction > world.Economies.Length) throw new ArgumentOutOfRangeException(nameof(faction));
            return world.Economies[faction - 1].RepairSlowedTicks;
        }

        /// <summary>A finished building of this faction below this share of its HP is worth sending villagers to.</summary>
        private bool NeedsRepair(BuildingState b) => AgesOn && IsActualBuilding(b) && b.Complete
            && b.Hp < (FortifyOn ? MaxHpOf(b) : HpOf(b.Kind, b.FactionId, b.OriginCell));

        /// <summary>Economy step, before the building work: every villager repairing adds its HP, never past the top.</summary>
        private void RepairBuildings()
        {
            if (!AgesOn) return;
            if (FortifyOn)
            {
                RepairFortifyBuildings();
                return;
            }
            int perTick = world.Config.Economy.RepairHpPerTick;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!NeedsRepair(b)) continue;
                int max = HpOf(b.Kind, b.FactionId, b.OriginCell);
                int workers = 0;
                for (int j = 0; j < world.VillagerCount; j++)
                {
                    var v = world.Villagers[j];
                    if (v.Alive && v.Task == VillagerTask.Building && v.BuildingId == b.Id) workers++;
                }
                if (workers > 0 && RepairUnderAttackActive(b))
                {
                    world.Economies[b.FactionId - 1].RepairSlowedTicks = checked(world.Economies[b.FactionId - 1].RepairSlowedTicks + 1);
                    if (world.Tick % 4 != 0) continue;
                }
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

        private void RepairFortifyBuildings()
        {
            var rules = world.Config.Economy;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var building = ref world.Buildings[i];
                if (!NeedsRepair(building)) continue;
                int max = MaxHpOf(building);
                int workers = 0;
                for (int j = 0; j < world.VillagerCount; j++)
                {
                    var villager = world.Villagers[j];
                    if (villager.Alive && villager.Task == VillagerTask.Building && villager.BuildingId == building.Id) workers++;
                }
                if (workers == 0) continue;
                // The k-th repairer adds first/k; the sum depends only on how many there are.
                int totalMilli = 0;
                for (int rank = 1; rank <= workers; rank++)
                    totalMilli = checked(totalMilli + rules.RepairFirstMilliHp / rank);
                if (RepairUnderAttackActive(building))
                {
                    totalMilli = checked((int)((long)totalMilli * rules.RepairUnderAttackPermille / 1000));
                    world.Economies[building.FactionId - 1].RepairSlowedTicks = checked(world.Economies[building.FactionId - 1].RepairSlowedTicks + 1);
                }
                int remainingMilli = checked((max - building.Hp) * 1000 - building.RepairMilliHp);
                int repairMilli = Math.Min(totalMilli, Math.Max(0, remainingMilli));
                if (repairMilli <= 0) continue;
                int repairCostPermille = rules.RepairCostPermille;
                int woodMilli = CostMilli(building.ConstructionWood, repairMilli, max, repairCostPermille);
                int stoneMilli = CostMilli(building.ConstructionStone, repairMilli, max, repairCostPermille);
                int planksMilli = CostMilli(building.ConstructionPlanks, repairMilli, max, repairCostPermille);
                int bricksMilli = CostMilli(building.ConstructionBricks, repairMilli, max, repairCostPermille);
                int siegePartsMilli = CostMilli(building.ConstructionSiegeParts, repairMilli, max, repairCostPermille);
                int framesMilli = CostMilli(building.ConstructionFrames, repairMilli, max, repairCostPermille);
                int fittingsMilli = CostMilli(building.ConstructionFittings, repairMilli, max, repairCostPermille);
                int ropeMilli = CostMilli(building.ConstructionRope, repairMilli, max, repairCostPermille);
                ref var economy = ref world.Economies[building.FactionId - 1];
                int woodDue = checked((building.RepairWoodCostMilli + woodMilli) / 1000);
                int stoneDue = checked((building.RepairStoneCostMilli + stoneMilli) / 1000);
                int planksDue = checked((building.RepairPlanksCostMilli + planksMilli) / 1000);
                int bricksDue = checked((building.RepairBricksCostMilli + bricksMilli) / 1000);
                int siegePartsDue = checked((building.RepairSiegePartsCostMilli + siegePartsMilli) / 1000);
                int framesDue = checked((building.RepairFramesCostMilli + framesMilli) / 1000);
                int fittingsDue = checked((building.RepairFittingsCostMilli + fittingsMilli) / 1000);
                int ropeDue = checked((building.RepairRopeCostMilli + ropeMilli) / 1000);
                // Review (11-04): a material the building is made of must be in stock (at least 1), even while the
                // fraction owed this tick is still below one whole unit - otherwise an empty stock repaired for free.
                if (Short(economy.Wood, woodDue, building.ConstructionWood) || Short(economy.Stone, stoneDue, building.ConstructionStone)
                    || Short(economy.Planks, planksDue, building.ConstructionPlanks) || Short(economy.Bricks, bricksDue, building.ConstructionBricks)
                    || Short(economy.SiegeParts, siegePartsDue, building.ConstructionSiegeParts) || Short(economy.Frames, framesDue, building.ConstructionFrames)
                    || Short(economy.Fittings, fittingsDue, building.ConstructionFittings) || Short(economy.Rope, ropeDue, building.ConstructionRope)) continue;
                economy.Wood = checked(economy.Wood - woodDue); economy.Stone = checked(economy.Stone - stoneDue);
                economy.Planks = checked(economy.Planks - planksDue); economy.Bricks = checked(economy.Bricks - bricksDue);
                economy.SiegeParts = checked(economy.SiegeParts - siegePartsDue); economy.Frames = checked(economy.Frames - framesDue);
                economy.Fittings = checked(economy.Fittings - fittingsDue); economy.Rope = checked(economy.Rope - ropeDue);
                building.RepairWoodCostMilli = checked((building.RepairWoodCostMilli + woodMilli) % 1000);
                building.RepairStoneCostMilli = checked((building.RepairStoneCostMilli + stoneMilli) % 1000);
                building.RepairPlanksCostMilli = checked((building.RepairPlanksCostMilli + planksMilli) % 1000);
                building.RepairBricksCostMilli = checked((building.RepairBricksCostMilli + bricksMilli) % 1000);
                building.RepairSiegePartsCostMilli = checked((building.RepairSiegePartsCostMilli + siegePartsMilli) % 1000);
                building.RepairFramesCostMilli = checked((building.RepairFramesCostMilli + framesMilli) % 1000);
                building.RepairFittingsCostMilli = checked((building.RepairFittingsCostMilli + fittingsMilli) % 1000);
                building.RepairRopeCostMilli = checked((building.RepairRopeCostMilli + ropeMilli) % 1000);
                int oldHp = building.Hp;
                building.RepairMilliHp = checked(building.RepairMilliHp + repairMilli);
                building.Hp = Math.Min(max, checked(building.Hp + building.RepairMilliHp / 1000));
                building.RepairMilliHp %= 1000;
                if (building.Hp >= max) building.RepairMilliHp = 0;
                economy.FortifyRepairHp = checked(economy.FortifyRepairHp + building.Hp - oldHp);
                economy.FortifyRepairWood = checked(economy.FortifyRepairWood + woodDue);
                economy.FortifyRepairStone = checked(economy.FortifyRepairStone + stoneDue);
                economy.FortifyRepairPlanks = checked(economy.FortifyRepairPlanks + planksDue);
                economy.FortifyRepairBricks = checked(economy.FortifyRepairBricks + bricksDue);
                economy.FortifyRepairSiegeParts = checked(economy.FortifyRepairSiegeParts + siegePartsDue);
                economy.FortifyRepairFrames = checked(economy.FortifyRepairFrames + framesDue);
                economy.FortifyRepairFittings = checked(economy.FortifyRepairFittings + fittingsDue);
                economy.FortifyRepairRope = checked(economy.FortifyRepairRope + ropeDue);
                if (building.Hp >= max)
                    for (int j = 0; j < world.VillagerCount; j++)
                        if (world.Villagers[j].BuildingId == building.Id
                            && (world.Villagers[j].Task == VillagerTask.Building || world.Villagers[j].Task == VillagerTask.ToBuild))
                            world.Villagers[j].Task = VillagerTask.Idle;
            }
        }

        private static bool Short(int stock, int due, int buildCost) => stock < (buildCost > 0 ? Math.Max(due, 1) : due);

        private static int CostMilli(int cost, int repairMilli, int maxHp, int costPermille)
            => cost <= 0 || costPermille <= 0 ? 0 : checked((int)((long)cost * repairMilli * costPermille / ((long)maxHp * 1000)));

        private bool RepairUnderAttackActive(BuildingState building)
            => RepairUnderAttackOn && building.LastDamagedTick != 0
                && world.Tick + 1 - building.LastDamagedTick < world.Config.Economy.RepairUnderAttackTicks;

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
            long share = 1000L * b.Hp / HpOf(b.Kind, b.FactionId, b.OriginCell);
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

        /// <summary>Tollgate stock repairs one finished own target every configured interval, in stable priority order.</summary>
        private void ApplyRepairKits()
        {
            var rules = world.Config.Economy;
            if (!RepairKitsOn || rules.RepairKitIntervalTicks <= 0 || world.Tick == 0 || world.Tick % rules.RepairKitIntervalTicks != 0) return;
            for (uint faction = 1; faction <= world.Factions.Length; faction++)
            {
                if (!TollgateRepairEffectAllowed(faction)) continue;
                ref var economy = ref world.Economies[faction - 1];
                if (economy.RepairKits <= 0) continue;
                int target = -1, targetRank = int.MaxValue;
                uint targetId = uint.MaxValue;
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var b = world.Buildings[i];
                if (!IsActualBuilding(b) || !b.Complete || b.FactionId != faction) continue;
                    int max = HpOf(b.Kind, faction, b.OriginCell);
                    if (b.Hp >= max) continue;
                    int rank = b.Kind == BuildingKind.Tollgate ? 0 : b.Kind == BuildingKind.Tower ? 1 : b.Kind == BuildingKind.Wall ? 2 : 4;
                    if (rank < targetRank || rank == targetRank && b.Id < targetId)
                    {
                        target = i;
                        targetRank = rank;
                        targetId = b.Id;
                    }
                }
                ref var core = ref world.Cores[world.Factions[faction - 1].CoreId - 1];
                if (core.Hp > 0 && core.Hp < core.Definition.Hp
                    && (3 < targetRank || 3 == targetRank && core.Definition.Id < targetId))
                {
                    int repairHp = AgeStrength(faction, CivKind.Tollgate, 4) ? rules.TollgateRepairKitHp : rules.RepairKitHp;
                    int repaired = Math.Min(repairHp, core.Definition.Hp - core.Hp);
                    core.Hp += repaired;
                    economy.RepairKits--;
                    economy.RepairKitsConsumed++;
                    economy.RepairKitsCoreRepairedHp += repaired;
                    continue;
                }
                if (target < 0) continue;
                ref var building = ref world.Buildings[target];
                    int buildingRepairHp = AgeStrength(faction, CivKind.Tollgate, 4) ? rules.TollgateRepairKitHp : rules.RepairKitHp;
                    int amount = Math.Min(buildingRepairHp, HpOf(building.Kind, faction, building.OriginCell) - building.Hp);
                if (amount <= 0) continue;
                building.Hp += amount;
                economy.RepairKits--;
                economy.RepairKitsConsumed++;
                economy.RepairKitsRepairedHp += amount;
            }
        }
    }
}
