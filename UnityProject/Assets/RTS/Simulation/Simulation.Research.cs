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

        // Academy prices are deliberately fixed per technology. They are not scenario knobs: keeping them here leaves
        // the V3-12 #1 extension payload unchanged while making the academy's discount explicit and deterministic.
        private const int AcademyWeaponsFoodCost = 75, AcademyWeaponsWoodCost = 50, AcademyWeaponsGoldCost = 50, AcademyWeaponsTicks = 300;
        private const int AcademyArmourFoodCost = 75, AcademyArmourWoodCost = 50, AcademyArmourGoldCost = 50, AcademyArmourTicks = 300;
        private const int AcademySiegecraftFoodCost = 100, AcademySiegecraftWoodCost = 75, AcademySiegecraftGoldCost = 75, AcademySiegecraftTicks = 350;

        private static readonly TechKind[] AcademyResearchOrder = { TechKind.Tools, TechKind.Carts, TechKind.Weapons, TechKind.Armour, TechKind.Siegecraft };
        private static readonly TechKind[] MountainResearchOrder = { MountainTech.DeepShaft, MountainTech.MountainFort };
        private static readonly TechKind[] FishingResearchOrder = { FishingTech.FishingNet, FishingTech.DriedFish };

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

        private int CarryFor(uint faction, bool fishing = false)
        {
            int carry = world.Config.Economy.CarryCapacity + (HasTech(faction, TechKind.Carts) ? world.Config.Economy.CartsCarry : 0);
            return fishing && HasTech(faction, FishingTech.FishingNet)
                ? checked(carry * (1000 + world.Config.Economy.FishingNetCarryBonusPermille) / 1000) : carry;
        }

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
            if (tech == MountainTech.DeepShaft) return MountainAllowed(faction) && e.Age >= 2 && !HasTech(faction, tech);
            if (tech == MountainTech.MountainFort) return MountainAllowed(faction) && e.Age >= 3 && !HasTech(faction, tech);
            if (tech == FishingTech.FishingNet) return FishingAllowed(faction) && e.Age >= 2 && !HasTech(faction, tech);
            if (tech == FishingTech.DriedFish) return FishingAllowed(faction) && e.Age >= 3 && !HasTech(faction, tech);
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

        private bool IsFishingTech(TechKind tech)
            => tech == FishingTech.FishingNet || tech == FishingTech.DriedFish;

        private int FishingFoodCost(TechKind tech)
            => tech == FishingTech.FishingNet ? world.Config.Economy.FishingNetFoodCost : world.Config.Economy.DriedFishFoodCost;

        private int FishingWoodCost(TechKind tech)
            => tech == FishingTech.FishingNet ? world.Config.Economy.FishingNetWoodCost : world.Config.Economy.DriedFishWoodCost;

        private int FishingTicks(TechKind tech)
            => tech == FishingTech.FishingNet ? world.Config.Economy.FishingNetTicks : world.Config.Economy.DriedFishTicks;

        private int TechFoodCost(TechKind tech)
            => IsFishingTech(tech) ? FishingFoodCost(tech)
                : tech == BridgeTech.Bridgeworks ? world.Config.Economy.BridgeworksFoodCost
                : tech == BridgeTech.SiegeDeployment ? world.Config.Economy.SiegeDeploymentFoodCost
                : world.Config.Economy.TechFood[(int)tech - 1];

        private int TechWoodCost(TechKind tech)
            => IsFishingTech(tech) ? FishingWoodCost(tech)
                : tech == BridgeTech.Bridgeworks ? world.Config.Economy.BridgeworksWoodCost
                : tech == BridgeTech.SiegeDeployment ? world.Config.Economy.SiegeDeploymentWoodCost
                : world.Config.Economy.TechWood[(int)tech - 1];

        private int TechMetalCost(TechKind tech)
            => IsBridgeTech(tech) || IsFishingTech(tech) ? 0 : world.Config.Economy.TechMetal[(int)tech - 1];

        private int TechGemsCost(TechKind tech)
            => IsBridgeTech(tech) || IsFishingTech(tech) ? 0 : world.Config.Economy.TechGems[(int)tech - 1];

        private int TechTicks(TechKind tech)
            => IsFishingTech(tech) ? FishingTicks(tech)
                : tech == BridgeTech.Bridgeworks ? world.Config.Economy.BridgeworksTicks
                : tech == BridgeTech.SiegeDeployment ? world.Config.Economy.SiegeDeploymentTicks
                : world.Config.Economy.TechTicks[(int)tech - 1];

        private bool IsMountainTech(TechKind tech)
            => tech == MountainTech.DeepShaft || tech == MountainTech.MountainFort;

        private int MountainFoodCost(TechKind tech)
            => tech == MountainTech.DeepShaft ? world.Config.Economy.MountainDeepShaftFoodCost : world.Config.Economy.MountainFortFoodCost;

        private int MountainWoodCost(TechKind tech)
            => tech == MountainTech.DeepShaft ? world.Config.Economy.MountainDeepShaftWoodCost : world.Config.Economy.MountainFortWoodCost;

        private int MountainTicks(TechKind tech)
            => tech == MountainTech.DeepShaft ? world.Config.Economy.MountainDeepShaftTicks : world.Config.Economy.MountainFortTicks;

        private bool AcademyTech(TechKind tech)
            => tech == TechKind.Tools || tech == TechKind.Carts || tech == TechKind.Weapons || tech == TechKind.Armour || tech == TechKind.Siegecraft;

        private bool AcademyTechOpen(uint faction, TechKind tech)
        {
            if (!AcademyTech(tech) || !TechOpen(faction, tech)) return false;
            byte age = world.Economies[faction - 1].Age;
            if ((tech == TechKind.Weapons || tech == TechKind.Armour) && age < 2) return false;
            if (tech == TechKind.Siegecraft && age < 3) return false;
            return true;
        }

        private int AcademyFoodCost(TechKind tech)
            => tech == TechKind.Tools ? world.Config.Economy.AcademyToolsFoodCost
                : tech == TechKind.Carts ? world.Config.Economy.AcademyCartsFoodCost
                : tech == TechKind.Weapons ? AcademyWeaponsFoodCost
                : tech == TechKind.Armour ? AcademyArmourFoodCost : AcademySiegecraftFoodCost;

        private int AcademyWoodCost(TechKind tech)
            => tech == TechKind.Tools ? world.Config.Economy.AcademyToolsWoodCost
                : tech == TechKind.Carts ? world.Config.Economy.AcademyCartsWoodCost
                : tech == TechKind.Weapons ? AcademyWeaponsWoodCost
                : tech == TechKind.Armour ? AcademyArmourWoodCost : AcademySiegecraftWoodCost;

        private int AcademyGoldCost(TechKind tech)
            => tech == TechKind.Tools ? world.Config.Economy.AcademyToolsGoldCost
                : tech == TechKind.Carts ? world.Config.Economy.AcademyCartsGoldCost
                : tech == TechKind.Weapons ? AcademyWeaponsGoldCost
                : tech == TechKind.Armour ? AcademyArmourGoldCost : AcademySiegecraftGoldCost;

        private int AcademyTicks(TechKind tech)
            => tech == TechKind.Tools ? world.Config.Economy.AcademyToolsTicks
                : tech == TechKind.Carts ? world.Config.Economy.AcademyCartsTicks
                : tech == TechKind.Weapons ? AcademyWeaponsTicks
                : tech == TechKind.Armour ? AcademyArmourTicks : AcademySiegecraftTicks;

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
                if (b.Alive && b.FactionId == faction && (b.Kind == BuildingKind.Blacksmith || b.Kind == BuildingKind.Academy || b.Kind == BuildingKind.MineShaft || b.Kind == BuildingKind.Harbor) && b.Researching == tech) return true;
            }
            return false;
        }

        /// <summary>Pays and starts <paramref name="tech"/> at <paramref name="smith"/>; returns false and changes nothing when it cannot.</summary>
        private bool StartResearch(uint faction, ref BuildingState smith, TechKind tech, bool byPlayer)
        {
            bool academy = smith.Kind == BuildingKind.Academy;
            bool mountain = smith.Kind == BuildingKind.MineShaft;
            bool fishing = smith.Kind == BuildingKind.Harbor;
            if (!AgesOn || !smith.Complete || smith.Researching != 0 || BeingResearched(faction, tech)) return false;
            if (academy)
            {
                if (!AcademyAllowed(faction) || !AcademyTechOpen(faction, tech) || !AcademyGoldAvailable(faction, tech)
                    || !AcademyBudgetAllows(faction, tech)) return false;
            }
            else if (mountain)
            {
                if (!IsMountainTech(tech) || !TechOpen(faction, tech)) return false;
            }
            else if (fishing)
            {
                if (!IsFishingTech(tech) || !TechOpen(faction, tech)) return false;
            }
            else if (smith.Kind != BuildingKind.Blacksmith || IsMountainTech(tech) || !TechOpen(faction, tech)) return false;
            ref var economy = ref world.Economies[faction - 1];
            int food = academy ? AcademyFoodCost(tech) : mountain ? MountainFoodCost(tech) : fishing ? FishingFoodCost(tech) : TechFoodCost(tech);
            int wood = academy ? AcademyWoodCost(tech) : mountain ? MountainWoodCost(tech) : fishing ? FishingWoodCost(tech) : TechWoodCost(tech);
            int gold = academy ? AcademyGoldCost(tech) : 0;
            int normalMetal = academy || mountain || fishing ? 0 : TechMetalCost(tech);
            int metal = MountainAllowed(faction) ? 0 : normalMetal;
            int oreForMetal = MountainAllowed(faction) ? normalMetal : 0;
            int gems = academy || mountain || fishing ? 0 : TechGemsCost(tech);
            if (economy.Food < food || economy.Wood < wood || economy.Gold < gold || economy.Metal < metal
                || economy.Ore < oreForMetal || economy.Gems < gems) return false;
            economy.Food = checked(economy.Food - food);
            economy.Wood = checked(economy.Wood - wood);
            economy.Gold = checked(economy.Gold - gold);
            economy.Metal = checked(economy.Metal - metal);
            economy.Ore = checked(economy.Ore - oreForMetal);
            economy.Gems = checked(economy.Gems - gems);
            smith.Researching = tech;
            smith.TrainRemaining = academy ? AcademyTicks(tech) : mountain ? MountainTicks(tech) : fishing ? FishingTicks(tech) : TechTicks(tech);
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
                if (!b.Alive || (b.Kind != BuildingKind.Blacksmith && b.Kind != BuildingKind.Academy && b.Kind != BuildingKind.MineShaft && b.Kind != BuildingKind.Harbor) || b.Researching == 0) continue;
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
                if (tech == MountainTech.MountainFort)
                {
                    for (int j = 0; j < world.BuildingCount; j++)
                    {
                        ref var defence = ref world.Buildings[j];
                        if (!defence.Alive || defence.FactionId != b.FactionId
                            || (defence.Kind != BuildingKind.Wall && defence.Kind != BuildingKind.Tower)) continue;
                        defence.Hp = MountainFortHp(defence.Hp, defence.FactionId, defence.OriginCell, SizeOf(defence.Kind));
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
            if (world.Economies[faction - 1].Civ == CivKind.Fishing && DecideFishingResearch(faction)) return;
            if (world.Economies[faction - 1].Civ == CivKind.Mountain && DecideMountainResearch(faction)) return;
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
                if (AcademyTech(tech) && world.Economies[faction - 1].Civ == CivKind.Academy && AcademyResearchOperational(faction)) continue;
                else if (TechOpen(faction, tech)) { StartResearch(faction, ref b, tech, false); return; }
        }

        private bool DecideFishingResearch(uint faction)
        {
            if (!FishingAllowed(faction)) return false;
            int harbor = OwnBuildingIndex(faction, BuildingKind.Harbor);
            if (harbor < 0) return false;
            ref var building = ref world.Buildings[harbor];
            if (!building.Complete || building.Held || building.Researching != 0) return true;
            foreach (var tech in FishingResearchOrder)
                if (TechOpen(faction, tech)) return StartResearch(faction, ref building, tech, false);
            return false;
        }

        private bool DecideMountainResearch(uint faction)
        {
            if (!MountainAllowed(faction)) return false;
            int shaft = OwnBuildingIndex(faction, BuildingKind.MineShaft);
            if (shaft < 0) return false;
            ref var building = ref world.Buildings[shaft];
            if (!building.Complete || building.Held || building.Researching != 0) return true;
            foreach (var tech in MountainResearchOrder)
                if (TechOpen(faction, tech)) return StartResearch(faction, ref building, tech, false);
            return false;
        }

        /// <summary>Academy civilisation: build one academy, then fund the next useful fixed academy research.</summary>
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
            foreach (var tech in AcademyResearchOrderFor(faction))
                if (AcademyTechOpen(faction, tech))
                {
                    if (StartResearch(faction, ref building, tech, false)) return true;
                    // A failed start means that this research is not affordable within the protected budget. Do not
                    // spend the reserve on a later item in the same allocation tick.
                    return false;
                }
            return false;
        }

        private TechKind[] AcademyResearchOrderFor(uint faction)
        {
            var result = new System.Collections.Generic.List<TechKind>(AcademyResearchOrder.Length);
            bool infantry = LivingClass(faction, UnitKind.Infantry) + QueuedOf(faction, UnitKind.Infantry) > 0;
            bool siege = LivingClass(faction, UnitKind.Ram) + QueuedOf(faction, UnitKind.Ram) > 0;
            if (infantry && world.Economies[faction - 1].Age >= 2)
            {
                result.Add(TechKind.Weapons); result.Add(TechKind.Armour);
            }
            if (siege && world.Economies[faction - 1].Age >= 3) result.Add(TechKind.Siegecraft);
            foreach (var tech in AcademyResearchOrder)
                if (!result.Contains(tech)) result.Add(tech);
            return result.ToArray();
        }

        private bool AcademyResearchOperational(uint faction)
        {
            if (!AcademyAllowed(faction)) return false;
            int academy = OwnBuildingIndex(faction, BuildingKind.Academy);
            if (academy < 0 || !world.Buildings[academy].Complete) return false;
            var next = AcademyNextResearch(faction);
            return next != 0 && AcademyGoldAvailable(faction, next);
        }

        private bool AcademyGoldAvailable(uint faction, TechKind tech)
        {
            var e = world.Economies[faction - 1];
            return HasUsableGoldSource(faction) && e.Gold >= AcademyGoldCost(tech);
        }

        private bool AcademyBudgetAllows(uint faction, TechKind tech)
        {
            var e = world.Economies[faction - 1];
            var rules = world.Config.Economy;
            var (ageFood, ageWood, ageGold, _) = AdvancePrice(faction, e);
            int foodReserve = ageFood + (e.Civ == CivKind.Primitive ? 0 : rules.MarketFoodFloor);
            int woodReserve = ageWood;
            int goldReserve = ageGold;
            if (LivingVillagers(faction) + e.Queued < AutoVillagerTargetFor(faction)) foodReserve = checked(foodReserve + VillagerFoodCostFor(faction));
            if (CompleteBarracks(faction) && LivingSoldiers(faction) == 0)
            {
                foodReserve = checked(foodReserve + InfantryFoodFor(faction));
                woodReserve = checked(woodReserve + InfantryWoodFor(faction));
            }
            return e.Food >= checked(AcademyFoodCost(tech) + foodReserve)
                && e.Wood >= checked(AcademyWoodCost(tech) + woodReserve)
                && e.Gold >= checked(AcademyGoldCost(tech) + goldReserve);
        }

        private bool AcademyOffenseReady(uint faction)
        {
            var e = world.Economies[faction - 1];
            return e.Civ == CivKind.Academy && ((HasTech(faction, TechKind.Weapons) && HasTech(faction, TechKind.Armour))
                || HasTech(faction, TechKind.Siegecraft));
        }
    }
}
