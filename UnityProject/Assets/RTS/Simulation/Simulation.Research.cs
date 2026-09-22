using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 research (technical-design-v3 32 #6). A finished blacksmith researches one tech at a time; a faction researches
    /// each tech once, and one blacksmith at a time works on it. Weapons and armour change every own soldier - those alive
    /// when it completes and every one trained after; tools, carts, irrigation and the blast furnace change the numbers
    /// the economy reads. Maps with ages only.
    /// </summary>
    public sealed partial class Simulation
    {
        private static readonly TechKind[] AutoResearchOrder = { TechKind.Tools, TechKind.Weapons, TechKind.Armour, TechKind.Carts, TechKind.Irrigation, TechKind.BlastFurnace };

        private bool HasTech(uint faction, TechKind tech) => AgesOn && (world.Economies[faction - 1].Techs & (1UL << ((int)tech - 1))) != 0;

        private int GatherTicksFor(uint faction)
            => world.Config.Economy.GatherIntervalTicks - (HasTech(faction, TechKind.Tools) ? world.Config.Economy.ToolsGatherTicks : 0);

        private int CarryFor(uint faction)
            => world.Config.Economy.CarryCapacity + (HasTech(faction, TechKind.Carts) ? world.Config.Economy.CartsCarry : 0);

        private int SmeltTicksFor(uint faction)
            => world.Config.Economy.SmeltTicks - (HasTech(faction, TechKind.BlastFurnace) ? world.Config.Economy.BlastFurnaceTicks : 0);

        /// <summary>A farm's ticks per food: its ground's pace, quicker with irrigation, never under 10.</summary>
        private int FarmTicksFor(BuildingState farm)
            => Math.Max(10, farm.Interval - (HasTech(farm.FactionId, TechKind.Irrigation) ? world.Config.Economy.IrrigationTicks : 0));

        /// <summary>Whether this faction may research <paramref name="tech"/> at all (the civilisation techs are for their own civilisation).</summary>
        private bool TechOpen(uint faction, TechKind tech)
        {
            var civ = world.Economies[faction - 1].Civ;
            if (civ == CivKind.Primitive || tech < TechKind.Weapons || tech > TechKind.BlastFurnace || HasTech(faction, tech)) return false;
            if (tech == TechKind.Irrigation) return civ == CivKind.Agrarian;
            if (tech == TechKind.BlastFurnace) return civ == CivKind.Metallurgy;
            return true;
        }

        private bool BeingResearched(uint faction, TechKind tech)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && b.Kind == BuildingKind.Blacksmith && b.Researching == tech) return true;
            }
            return false;
        }

        /// <summary>Pays and starts <paramref name="tech"/> at <paramref name="smith"/>; returns false and changes nothing when it cannot.</summary>
        private bool StartResearch(uint faction, ref BuildingState smith, TechKind tech, bool byPlayer)
        {
            if (!AgesOn || smith.Kind != BuildingKind.Blacksmith || !smith.Complete || smith.Researching != 0 || !TechOpen(faction, tech) || BeingResearched(faction, tech)) return false;
            var rules = world.Config.Economy;
            int t = (int)tech - 1;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Food < rules.TechFood[t] || economy.Wood < rules.TechWood[t]) return false;
            economy.Food = checked(economy.Food - rules.TechFood[t]);
            economy.Wood = checked(economy.Wood - rules.TechWood[t]);
            smith.Researching = tech;
            smith.TrainRemaining = rules.TechTicks[t];
            if (byPlayer && IndustryOn) smith.Held = true;
            return true;
        }

        /// <summary>Economy step: research clocks; a finished tech takes effect at once.</summary>
        private void AdvanceResearch()
        {
            if (!AgesOn) return;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || b.Kind != BuildingKind.Blacksmith || b.Researching == 0) continue;
                if (--b.TrainRemaining > 0) continue;
                var tech = b.Researching;
                b.Researching = 0;
                b.TrainRemaining = 0;
                world.Economies[b.FactionId - 1].Techs |= 1UL << ((int)tech - 1);
                if (tech != TechKind.Weapons && tech != TechKind.Armour) continue;
                foreach (int s in world.SoldierTraversal)
                    if (world.Soldiers[s].Alive && world.Soldiers[s].Initial.FactionId == b.FactionId) ApplyTech(s, tech);
            }
        }

        /// <summary>A newly trained soldier gets every soldier tech its faction has.</summary>
        private void ApplySoldierTechs(uint faction, int index)
        {
            if (HasTech(faction, TechKind.Weapons)) ApplyTech(index, TechKind.Weapons);
            if (HasTech(faction, TechKind.Armour)) ApplyTech(index, TechKind.Armour);
        }

        private void ApplyTech(int index, TechKind tech)
        {
            var rules = world.Config.Economy;
            ref var s = ref world.Soldiers[index];
            if (tech == TechKind.Weapons) s.Parameters.Damage = checked(s.Parameters.Damage + rules.WeaponsDamage);
            else
            {
                s.Parameters.Hp = checked(s.Parameters.Hp + rules.ArmourHp);
                s.Hp = checked(s.Hp + rules.ArmourHp);
            }
        }

        /// <summary>
        /// AI phase, once in a civilisation and not saving: a blacksmith when there is a finished barracks, then its techs
        /// in a fixed order (tools, weapons, armour, carts, the civilisation's own) as food and wood allow.
        /// </summary>
        private void DecideResearch(uint faction)
        {
            if (!AgesOn || !CivLineStarted(faction) || SavingHard(faction)) return;
            var rules = world.Config.Economy;
            int smith = OwnBuildingIndex(faction, BuildingKind.Blacksmith);
            if (smith < 0)
            {
                if (OwnBuildingIndex(faction, BuildingKind.Barracks) < 0 || world.Economies[faction - 1].Wood < rules.BlacksmithWoodCost) return;
                int origin = FindSite(faction, rules.BlacksmithSizeCells);
                if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Blacksmith, origin, Facing.North, 0);
                return;
            }
            ref var b = ref world.Buildings[smith];
            if (!b.Complete || b.Held || b.Researching != 0) return;
            foreach (var tech in AutoResearchOrder)
                if (TechOpen(faction, tech)) { StartResearch(faction, ref b, tech, false); return; }
        }
    }
}
