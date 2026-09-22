using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 training queues (technical-design-v3 32). A barracks queue holds unit kinds in order; without ages the queue
    /// has no kinds and every entry is infantry, exactly as in V3-1 to V3-4. Costs, times and room are asked per kind
    /// here, so a new unit kind is one more case in these methods.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Food, wood and metal a unit of <paramref name="kind"/> costs this faction now, and its training ticks.</summary>
        private (int food, int wood, int metal, int ticks) CostOf(uint faction, UnitKind kind)
        {
            var e = world.Config.Economy;
            if (kind == UnitKind.Scout) return (e.ScoutFoodCost, e.ScoutWoodCost, 0, e.ScoutTrainTicks);
            return (InfantryFoodFor(faction), InfantryWoodFor(faction), InfantryMetalFor(faction), InfantryTicksFor(faction));
        }

        /// <summary>What a barracks can train: infantry always, scouts on a map with ages.</summary>
        private bool Trains(BuildingState b, UnitKind kind)
            => b.Kind == BuildingKind.Barracks && (kind == UnitKind.Infantry || (kind == UnitKind.Scout && AgesOn));

        private static UnitKind QueueAt(BuildingState b, int i) => b.QueueKinds == null || i >= b.QueueKinds.Length ? UnitKind.Infantry : b.QueueKinds[i];

        private bool CanPay(uint faction, UnitKind kind)
        {
            var e = world.Economies[faction - 1];
            var c = CostOf(faction, kind);
            return e.Food >= c.food && e.Wood >= c.wood && e.Metal >= c.metal;
        }

        /// <summary>Pays and puts one unit at the back of the queue. The caller checked room, cost and the queue limit.</summary>
        private void Enqueue(uint faction, ref BuildingState b, UnitKind kind)
        {
            var c = CostOf(faction, kind);
            ref var e = ref world.Economies[faction - 1];
            e.Food = checked(e.Food - c.food);
            e.Wood = checked(e.Wood - c.wood);
            e.Metal = checked(e.Metal - c.metal);
            b.QueuedMetal = checked(b.QueuedMetal + c.metal);
            if (b.Queued == 0) b.TrainRemaining = c.ticks;
            b.Queued++;
            if (AgesOn)
            {
                var kinds = b.QueueKinds ?? Array.Empty<UnitKind>();
                Array.Resize(ref kinds, kinds.Length + 1);
                kinds[kinds.Length - 1] = kind;
                b.QueueKinds = kinds;
            }
        }

        /// <summary>
        /// Takes the last unit off the queue and returns today's price for it - metal only up to what the queue paid, so a
        /// cancel after advancing never gives metal that was not paid (26.1).
        /// </summary>
        private void CancelLast(uint faction, ref BuildingState b)
        {
            var kind = QueueAt(b, b.Queued - 1);
            var c = CostOf(faction, kind);
            ref var e = ref world.Economies[faction - 1];
            b.Queued--;
            if (b.QueueKinds != null && b.QueueKinds.Length > 0) Array.Resize(ref b.QueueKinds, b.QueueKinds.Length - 1);
            e.Food = checked(e.Food + c.food);
            e.Wood = checked(e.Wood + c.wood);
            int metalBack = Math.Min(c.metal, b.QueuedMetal);
            b.QueuedMetal -= metalBack;
            e.Metal = checked(e.Metal + metalBack);
            if (b.Queued == 0) b.TrainRemaining = 0;
        }

        /// <summary>The front unit left the building: the next one starts its clock.</summary>
        private void Dequeue(uint faction, ref BuildingState b, UnitKind done)
        {
            b.Queued--;
            if (b.QueueKinds != null && b.QueueKinds.Length > 0)
            {
                var rest = new UnitKind[b.QueueKinds.Length - 1];
                Array.Copy(b.QueueKinds, 1, rest, 0, rest.Length);
                b.QueueKinds = rest;
            }
            if (done == UnitKind.Infantry) b.QueuedMetal = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedMetal - InfantryMetalFor(faction));
            b.TrainRemaining = b.Queued > 0 ? CostOf(faction, QueueAt(b, 0)).ticks : 0;
        }

        /// <summary>Units of <paramref name="kind"/> queued in all the faction's buildings.</summary>
        private int QueuedOf(uint faction, UnitKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.FactionId != faction) continue;
                for (int q = 0; q < b.Queued; q++) if (QueueAt(b, q) == kind) count++;
            }
            return count;
        }

        /// <summary>Room for one more of <paramref name="kind"/> in the armies it would join, counting the queues.</summary>
        private bool HasRoomFor(uint faction, UnitKind kind)
        {
            bool scout = kind == UnitKind.Scout;
            int free = 0;
            foreach (uint id in world.Factions[faction - 1].ArmyIds)
            {
                var a = world.Armies[id - 1];
                if ((a.Definition.Role == "scout") != scout) continue;
                int count = 0;
                foreach (uint soldier in a.SoldierIds) if (world.Soldiers[soldier - 1].Alive) count++;
                free += a.Definition.Capacity - count;
            }
            return free > QueuedOf(faction, kind);
        }

        /// <summary>
        /// The automatic economy keeps the scout armies full: one scout at a time when one is missing, never while saving
        /// to advance. Returns true when it queued one (one action per cycle).
        /// </summary>
        private bool DecideScout(uint faction, ref BuildingState barracks)
        {
            if (!AgesOn || SavingToAdvance(faction) || barracks.Queued >= world.Config.Economy.QueueLimit) return false;
            if (QueuedOf(faction, UnitKind.Scout) > 0 || !HasRoomFor(faction, UnitKind.Scout) || !CanPay(faction, UnitKind.Scout)) return false;
            Enqueue(faction, ref barracks, UnitKind.Scout);
            return true;
        }
    }
}
