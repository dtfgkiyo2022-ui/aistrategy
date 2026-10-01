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
        // Kept outside TechKind so the existing contract enum numbers and names remain unchanged. The cavalry tail
        // supplies the thirteenth slot only when that optional civilisation is enabled.
        private const TechKind CavalryDrillTech = (TechKind)13;
        private const ulong CavalryDrillBit = 1UL << 12;
        private static readonly TechKind[] AutoResearchOrder = { TechKind.Tools, TechKind.Weapons, TechKind.Armour, TechKind.Carts, TechKind.Irrigation, TechKind.BlastFurnace,
            BridgeTech.Bridgeworks, TechKind.Masonry, TechKind.Siegecraft, TechKind.Banking, TechKind.SteelWeapons, TechKind.SteelArmour, TechKind.GemArmor,
            CavalryDrillTech, BridgeTech.SiegeDeployment };

        private bool HasTech(uint faction, TechKind tech) => AgesOn && (world.Economies[faction - 1].Techs & (1UL << ((int)tech - 1))) != 0;

        private bool HasCavalryDrill(uint faction) => CavalryAllowed(faction) && (world.Economies[faction - 1].Techs & CavalryDrillBit) != 0;

        private int GatherTicksFor(uint faction, uint nodeId)
        {
            var rules = world.Config.Economy;
            int interval = rules.GatherIntervalTicks - (HasTech(faction, TechKind.Tools) ? rules.ToolsGatherTicks : 0);
            if (rules.FishingEnabled && nodeId > 0 && world.Nodes[nodeId - 1].Fishing
                && world.Economies[faction - 1].Civ == CivKind.Agrarian)
                interval = interval * (1000 - rules.FishAgrarianBonusPermille) / 1000;
            return Math.Max(1, interval);
        }

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
            var e = world.Economies[faction - 1];
            if (tech == CavalryDrillTech) return CavalryAllowed(faction) && e.Age >= 2 && !HasCavalryDrill(faction);
            if (e.Civ == CivKind.Primitive || tech < TechKind.Weapons || HasTech(faction, tech)) return false;
            if (tech == BridgeTech.Bridgeworks) return e.Civ == CivKind.Bridge && e.Age >= 2;
            if (tech == BridgeTech.SiegeDeployment) return e.Civ == CivKind.Bridge && e.Age >= 3;
            if (tech > TechKind.GemArmor) return false;
            if (tech == TechKind.Irrigation) return e.Civ == CivKind.Agrarian;
            if (tech == TechKind.BlastFurnace) return e.Civ == CivKind.Metallurgy;
            // V3-5 (32 #14): the steel techs want the second age and the metal to pay for them; the earlier ones must be in first.
            if (tech == TechKind.SteelWeapons) return e.Age >= 2 && HasTech(faction, TechKind.Weapons);
            if (tech == TechKind.SteelArmour) return e.Age >= 2 && HasTech(faction, TechKind.Armour);
            // V3-5 (32 #10): siegecraft, masonry and banking are the third age's, and both civilisations may have them.
            // V3-5 (32 #19): Gems are a separate route from either civilisation's techs, so GemArmor has no prerequisite
            // beyond the third age.
            if (tech >= TechKind.Siegecraft) return e.Age >= 3;
            return true;
        }

        private bool IsBridgeTech(TechKind tech) => tech == BridgeTech.Bridgeworks || tech == BridgeTech.SiegeDeployment;

        private int TechFoodCost(TechKind tech)
            => tech == BridgeTech.Bridgeworks ? world.Config.Economy.BridgeworksFoodCost
                : tech == BridgeTech.SiegeDeployment ? world.Config.Economy.SiegeDeploymentFoodCost
                : world.Config.Economy.TechFood[(int)tech - 1];

        private int TechWoodCost(TechKind tech)
            => tech == BridgeTech.Bridgeworks ? world.Config.Economy.BridgeworksWoodCost
                : tech == BridgeTech.SiegeDeployment ? world.Config.Economy.SiegeDeploymentWoodCost
                : world.Config.Economy.TechWood[(int)tech - 1];

        private int TechMetalCost(TechKind tech)
            => IsBridgeTech(tech) ? 0 : world.Config.Economy.TechMetal[(int)tech - 1];

        private int TechGemsCost(TechKind tech)
            => IsBridgeTech(tech) ? 0 : world.Config.Economy.TechGems[(int)tech - 1];

        private int TechTicks(TechKind tech)
            => tech == BridgeTech.Bridgeworks ? world.Config.Economy.BridgeworksTicks
                : tech == BridgeTech.SiegeDeployment ? world.Config.Economy.SiegeDeploymentTicks
                : world.Config.Economy.TechTicks[(int)tech - 1];

        private bool AcademyTech(TechKind tech) => tech == TechKind.Tools || tech == TechKind.Carts;

        private int AcademyFoodCost(TechKind tech)
            => tech == TechKind.Tools ? world.Config.Economy.AcademyToolsFoodCost : world.Config.Economy.AcademyCartsFoodCost;

        private int AcademyWoodCost(TechKind tech)
            => tech == TechKind.Tools ? world.Config.Economy.AcademyToolsWoodCost : world.Config.Economy.AcademyCartsWoodCost;

        private int AcademyGoldCost(TechKind tech)
            => tech == TechKind.Tools ? world.Config.Economy.AcademyToolsGoldCost : world.Config.Economy.AcademyCartsGoldCost;

        private int AcademyTicks(TechKind tech)
            => tech == TechKind.Tools ? world.Config.Economy.AcademyToolsTicks : world.Config.Economy.AcademyCartsTicks;

        private int BridgeHpFor(uint faction)
        {
            var rules = world.Config.Economy;
            return checked(rules.BridgeHp + (HasTech(faction, BridgeTech.Bridgeworks) ? rules.BridgeworksHpBonus : 0));
        }

        private int BridgeWorkFor(uint faction)
        {
            var rules = world.Config.Economy;
            return Math.Max(1, rules.BridgeWork - (HasTech(faction, BridgeTech.Bridgeworks) ? rules.BridgeworksWorkReduction : 0));
        }

        private int RamTicksFor(uint faction)
        {
            var rules = world.Config.Economy;
            return Math.Max(1, rules.RamTicks - (HasTech(faction, BridgeTech.SiegeDeployment) ? rules.SiegeDeploymentRamTicksReduction : 0));
        }

        private int AutoRamLimitFor(uint faction)
            => AutoRams + (HasTech(faction, BridgeTech.SiegeDeployment) ? world.Config.Economy.SiegeDeploymentRamCapacityBonus : 0);

        private bool BeingResearched(uint faction, TechKind tech)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && (b.Kind == BuildingKind.Blacksmith || b.Kind == BuildingKind.Academy) && b.Researching == tech) return true;
            }
            return false;
        }

        /// <summary>Pays and starts <paramref name="tech"/> at <paramref name="smith"/>; returns false and changes nothing when it cannot.</summary>
        private bool StartResearch(uint faction, ref BuildingState smith, TechKind tech, bool byPlayer)
        {
            bool academy = smith.Kind == BuildingKind.Academy;
            if (!AgesOn || !smith.Complete || smith.Researching != 0 || !TechOpen(faction, tech) || BeingResearched(faction, tech)) return false;
            if (academy ? !AcademyAllowed(faction) || !AcademyTech(tech) : smith.Kind != BuildingKind.Blacksmith) return false;
            ref var economy = ref world.Economies[faction - 1];
            int food = academy ? AcademyFoodCost(tech) : TechFoodCost(tech);
            int wood = academy ? AcademyWoodCost(tech) : TechWoodCost(tech);
            int gold = academy ? AcademyGoldCost(tech) : 0;
            int metal = academy ? 0 : TechMetalCost(tech), gems = academy ? 0 : TechGemsCost(tech);
            if (economy.Food < food || economy.Wood < wood || economy.Gold < gold || economy.Metal < metal || economy.Gems < gems) return false;
            economy.Food = checked(economy.Food - food);
            economy.Wood = checked(economy.Wood - wood);
            economy.Gold = checked(economy.Gold - gold);
            economy.Metal = checked(economy.Metal - metal);
            economy.Gems = checked(economy.Gems - gems);
            smith.Researching = tech;
            smith.TrainRemaining = academy ? AcademyTicks(tech) : TechTicks(tech);
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
                if (!b.Alive || (b.Kind != BuildingKind.Blacksmith && b.Kind != BuildingKind.Academy) || b.Researching == 0) continue;
                if (--b.TrainRemaining > 0) continue;
                var tech = b.Researching;
                b.Researching = 0;
                b.TrainRemaining = 0;
                world.Economies[b.FactionId - 1].Techs |= 1UL << ((int)tech - 1);
                if (tech == BridgeTech.Bridgeworks)
                {
                    var bonus = world.Config.Economy.BridgeworksHpBonus;
                    for (int j = 0; j < world.BuildingCount; j++)
                    {
                        ref var bridge = ref world.Buildings[j];
                        if (bridge.Alive && bridge.FactionId == b.FactionId && bridge.Kind == BuildingKind.Bridge)
                            bridge.Hp = checked(bridge.Hp + bonus);
                    }
                }
                if (tech != TechKind.Weapons && tech != TechKind.Armour && tech != TechKind.SteelWeapons && tech != TechKind.SteelArmour && tech != TechKind.GemArmor) continue;
                foreach (int s in world.SoldierTraversal)
                    if (world.Soldiers[s].Alive && world.Soldiers[s].Initial.FactionId == b.FactionId) ApplyTech(s, tech);
            }
        }

        /// <summary>A newly trained soldier gets every soldier tech its faction has.</summary>
        private void ApplySoldierTechs(uint faction, int index)
        {
            if (HasTech(faction, TechKind.Weapons)) ApplyTech(index, TechKind.Weapons);
            if (HasTech(faction, TechKind.Armour)) ApplyTech(index, TechKind.Armour);
            if (HasTech(faction, TechKind.SteelWeapons)) ApplyTech(index, TechKind.SteelWeapons);
            if (HasTech(faction, TechKind.SteelArmour)) ApplyTech(index, TechKind.SteelArmour);
            if (HasTech(faction, TechKind.GemArmor)) ApplyTech(index, TechKind.GemArmor);
            if (HasCavalryDrill(faction)) ApplyTech(index, CavalryDrillTech);
        }

        private void ApplyTech(int index, TechKind tech)
        {
            var rules = world.Config.Economy;
            ref var s = ref world.Soldiers[index];
            if (tech == CavalryDrillTech)
            {
                if (s.Class == UnitKind.LightCavalry || s.Class == UnitKind.Cavalry)
                {
                    s.Parameters.Speed = Fix64.FromRaw(checked(s.Parameters.Speed.Raw + rules.CavalryDrillSpeed.Raw));
                    s.StepDistance = Fix64.FromRaw(s.Parameters.Speed.Raw / 20);
                }
            }
            else if (tech == TechKind.Weapons) s.Parameters.Damage = checked(s.Parameters.Damage + rules.WeaponsDamage);
            else if (tech == TechKind.SteelWeapons) s.Parameters.Damage = checked(s.Parameters.Damage + rules.SteelWeaponsDamage);
            else
            {
                int hp = tech == TechKind.SteelArmour ? rules.SteelArmourHp : tech == TechKind.GemArmor ? rules.GemArmorHp : rules.ArmourHp;
                s.Parameters.Hp = checked(s.Parameters.Hp + hp);
                s.Hp = checked(s.Hp + hp);
            }
        }

        /// <summary>
        /// AI phase, once in a civilisation and not saving: a blacksmith when there is a finished barracks, then its techs
        /// in a fixed order (tools, weapons, armour, carts, the civilisation's own) as food and wood allow.
        /// </summary>
        private void DecideResearch(uint faction)
        {
            if (!AgesOn || SavingHard(faction)) return;
            if (world.Economies[faction - 1].Civ == CivKind.Academy)
            {
                if (DecideAcademyResearch(faction)) return;
            }
            if (!CivLineStarted(faction)) return;
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
                if (AcademyTech(tech) && world.Economies[faction - 1].Civ == CivKind.Academy) continue;
                else if (TechOpen(faction, tech)) { StartResearch(faction, ref b, tech, false); return; }
        }

        /// <summary>Academy civilisation: build one academy, then research tools before carts.</summary>
        private bool DecideAcademyResearch(uint faction)
        {
            if (!AcademyAllowed(faction)) return false;
            var rules = world.Config.Economy;
            int academy = OwnBuildingIndex(faction, BuildingKind.Academy);
            if (academy < 0)
            {
                if (world.Economies[faction - 1].Wood < rules.AcademyWoodCost) return false;
                int origin = FindSite(faction, rules.AcademySizeCells);
                if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Academy, origin, Facing.North, 0);
                return true;
            }
            ref var building = ref world.Buildings[academy];
            if (!building.Complete || building.Held || building.Researching != 0) return true;
            var order = new[] { TechKind.Tools, TechKind.Carts };
            foreach (var tech in order)
                if (TechOpen(faction, tech))
                {
                    StartResearch(faction, ref building, tech, false);
                    return true;
                }
            return false;
        }
    }
}
