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
        private (int food, int wood, int metal, int gems, int ticks) CostOf(uint faction, UnitKind kind)
        {
            var e = world.Config.Economy;
            if (kind == UnitKind.Scout) return (e.ScoutFoodCost, e.ScoutWoodCost, 0, 0, e.ScoutTrainTicks);
            if (kind == UnitKind.Archer) return (e.ArcherFood, e.ArcherWood, 0, 0, e.ArcherTicks);
            if (kind == UnitKind.Cavalry) return (e.CavalryFood, e.CavalryWood, e.CavalryMetal, 0, e.CavalryTicks);
            if (kind == UnitKind.Ram) return (e.RamFood, e.RamWood, 0, 0, e.RamTicks);
            if (kind == UnitKind.Mercenary) return (0, 0, 0, e.MercenaryGems, e.MercenaryTicks);
            if (kind == UnitKind.Monk) return (e.MonkFoodCost, 0, e.MonkGoldCost, 0, e.MonkTrainTicks);
            return (InfantryFoodFor(faction), InfantryWoodFor(faction), InfantryMetalFor(faction), 0, InfantryTicksFor(faction));
        }

        /// <summary>What a barracks can train: infantry always, scouts on a map with ages.</summary>
        private bool Trains(BuildingState b, UnitKind kind)
        {
            // V3-5 (32 #9): the siege workshop trains rams, and only rams.
            if (b.Kind == BuildingKind.SiegeWorkshop) return kind == UnitKind.Ram && AgesOn && world.Economies[b.FactionId - 1].Age >= 2;
            // V3-5 (32 #12): the range and the stable train their unit in either civilisation, from the second age.
            if (b.Kind == BuildingKind.ArcheryRange) return kind == UnitKind.Archer && AgesOn && world.Economies[b.FactionId - 1].Age >= 2;
            if (b.Kind == BuildingKind.Stable) return kind == UnitKind.Cavalry && AgesOn && world.Economies[b.FactionId - 1].Age >= 2;
            // V3-5 (32 #17): a castle trains any of the three line units, whatever the civilisation.
            if (b.Kind == BuildingKind.Castle)
                return AgesOn && world.Economies[b.FactionId - 1].Age >= 3
                    && (kind == UnitKind.Infantry || kind == UnitKind.Archer || kind == UnitKind.Cavalry || kind == UnitKind.Mercenary);
            if (b.Kind != BuildingKind.Barracks) return false;
            if (kind == UnitKind.Infantry) return true;
            if (kind == UnitKind.Monk) return world.Config.Economy.MonksEnabled;
            if (!AgesOn) return false;
            var e = world.Economies[b.FactionId - 1];
            return kind == UnitKind.Scout
                || (kind == UnitKind.Archer && e.Civ == CivKind.Agrarian && e.Age >= 2)
                || (kind == UnitKind.Cavalry && e.Civ == CivKind.Metallurgy && e.Age >= 2);
        }

        /// <summary>The civilisation's own unit once it is in its second age (archers or cavalry), else 0.</summary>
        private UnitKind SpecialUnit(uint faction)
        {
            var e = world.Economies[faction - 1];
            if (!AgesOn || e.Age < 2) return 0;
            return e.Civ == CivKind.Agrarian ? UnitKind.Archer : e.Civ == CivKind.Metallurgy ? UnitKind.Cavalry : 0;
        }

        /// <summary>Archers and cavalry fight as infantry with their own numbers (32 #8); it is what they were trained as.</summary>
        private void ApplyClass(int index, UnitKind unit)
        {
            if (unit == UnitKind.Ram)
            {
                var r = world.Config.Economy;
                ref var ram = ref world.Soldiers[index];
                ram.Class = unit;
                ram.Parameters.Hp = r.RamHp; ram.Parameters.Damage = r.RamDamage; ram.Parameters.AttackIntervalTicks = r.RamInterval;
                ram.Parameters.Range = r.RamRange; ram.Parameters.Speed = r.RamSpeed; ram.Parameters.Vision = r.RamVision;
                ram.StepDistance = Fix64.FromRaw(ram.Parameters.Speed.Raw / 20);
                ram.Hp = r.RamHp; ram.Initial.Hp = r.RamHp;
                return;
            }
            if (unit == UnitKind.Mercenary)
            {
                var r = world.Config.Economy;
                ref var mercenary = ref world.Soldiers[index];
                mercenary.Class = unit;
                mercenary.Parameters.Hp = r.MercenaryHp; mercenary.Parameters.Damage = r.MercenaryDamage;
                mercenary.Parameters.AttackIntervalTicks = r.MercenaryInterval;
                mercenary.Hp = r.MercenaryHp; mercenary.Initial.Hp = r.MercenaryHp;
                return;
            }
            if (unit != UnitKind.Archer && unit != UnitKind.Cavalry) return;
            var e = world.Config.Economy;
            ref var s = ref world.Soldiers[index];
            bool archer = unit == UnitKind.Archer;
            s.Class = unit;
            s.Parameters.Hp = archer ? e.ArcherHp : e.CavalryHp;
            s.Parameters.Damage = archer ? e.ArcherDamage : e.CavalryDamage;
            s.Parameters.AttackIntervalTicks = archer ? e.ArcherInterval : e.CavalryInterval;
            s.Parameters.Range = archer ? e.ArcherRange : e.CavalryRange;
            s.Parameters.Speed = archer ? e.ArcherSpeed : e.CavalrySpeed;
            s.Parameters.Vision = archer ? e.ArcherVision : e.CavalryVision;
            s.StepDistance = Fix64.FromRaw(s.Parameters.Speed.Raw / 20);
            s.Hp = s.Parameters.Hp;
            s.Initial.Hp = s.Parameters.Hp;
        }

        private static UnitKind QueueAt(BuildingState b, int i) => b.QueueKinds == null || i >= b.QueueKinds.Length ? UnitKind.Infantry : b.QueueKinds[i];

        private bool CanPay(uint faction, UnitKind kind)
        {
            if (kind == UnitKind.Monk && !world.Config.Economy.MonksEnabled) return false;
            var e = world.Economies[faction - 1];
            var c = CostOf(faction, kind);
            return e.Food >= c.food && e.Wood >= c.wood && e.Metal >= c.metal && e.Gems >= c.gems;
        }

        /// <summary>Pays and puts one unit at the back of the queue. The caller checked room, cost and the queue limit.</summary>
        private void Enqueue(uint faction, ref BuildingState b, UnitKind kind)
        {
            var c = CostOf(faction, kind);
            ref var e = ref world.Economies[faction - 1];
            e.Food = checked(e.Food - c.food);
            e.Wood = checked(e.Wood - c.wood);
            e.Metal = checked(e.Metal - c.metal);
            e.Gems = checked(e.Gems - c.gems);
            b.QueuedMetal = checked(b.QueuedMetal + c.metal);
            b.QueuedGems = checked(b.QueuedGems + c.gems);
            if (b.Queued == 0) b.TrainRemaining = c.ticks;
            b.Queued++;
            if (AgesOn || kind == UnitKind.Monk)
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
            int gemsBack = Math.Min(c.gems, b.QueuedGems);
            b.QueuedGems -= gemsBack;
            e.Gems = checked(e.Gems + gemsBack);
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
            int metal = CostOf(faction, done).metal;
            int gems = CostOf(faction, done).gems;
            if (metal > 0 || done == UnitKind.Infantry) b.QueuedMetal = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedMetal - metal);
            if (gems > 0) b.QueuedGems = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedGems - gems);
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
            // Archers and cavalry join the infantry armies, so they share the infantry room.
            int queued = scout ? QueuedOf(faction, UnitKind.Scout)
                : QueuedOf(faction, UnitKind.Infantry) + QueuedOf(faction, UnitKind.Archer) + QueuedOf(faction, UnitKind.Cavalry)
                    + QueuedOf(faction, UnitKind.Ram) + QueuedOf(faction, UnitKind.Monk);
            int free = 0;
            foreach (uint id in world.Factions[faction - 1].ArmyIds)
            {
                var a = world.Armies[id - 1];
                if ((a.Definition.Role == "scout") != scout) continue;
                int count = 0;
                foreach (uint soldier in a.SoldierIds) if (world.Soldiers[soldier - 1].Alive) count++;
                free += a.Definition.Capacity - count;
            }
            return free > queued;
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
