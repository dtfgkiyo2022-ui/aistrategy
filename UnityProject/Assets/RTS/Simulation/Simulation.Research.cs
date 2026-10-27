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
        private const byte AgeFourAiResearchFailurePrerequisite = 1;
        private const byte AgeFourAiResearchFailureBuilding = 2;
        private const byte AgeFourAiResearchFailureResearching = 3;
        private const byte AgeFourAiResearchFailureResource = 4;

        // Kept outside TechKind so the existing contract enum numbers and names remain unchanged. The cavalry tail
        // supplies the thirteenth slot only when that optional civilisation is enabled.
        private const TechKind CavalryDrillTech = (TechKind)13;
        private const ulong CavalryDrillBit = 1UL << 12;
        private static readonly TechKind[] AutoResearchOrder = { TechKind.Tools, TechKind.Weapons, TechKind.Armour, TechKind.Carts, TechKind.Irrigation, TechKind.BlastFurnace,
            BridgeTech.Bridgeworks, TechKind.Masonry, TechKind.Siegecraft, TechKind.Banking, TechKind.SteelWeapons, TechKind.SteelArmour, TechKind.GemArmor,
            CavalryDrillTech, BridgeTech.SiegeDeployment, TollgateTech.GateDefence, TollgateTech.GateNetwork, TechKind.TotalWar, TechKind.GrandWorkshop };
        private static readonly TechKind[] AgeFourAiResearchOrder = { TechKind.TotalWar, TechKind.GrandWorkshop };

        // Academy prices are deliberately fixed per technology. They are not scenario knobs: keeping them here leaves
        // the V3-12 #1 extension payload unchanged while making the academy's discount explicit and deterministic.
        private const int AcademyWeaponsFoodCost = 75, AcademyWeaponsWoodCost = 50, AcademyWeaponsGoldCost = 50, AcademyWeaponsTicks = 300;
        private const int AcademyArmourFoodCost = 75, AcademyArmourWoodCost = 50, AcademyArmourGoldCost = 50, AcademyArmourTicks = 300;
        private const int AcademySiegecraftFoodCost = 100, AcademySiegecraftWoodCost = 75, AcademySiegecraftGoldCost = 75, AcademySiegecraftTicks = 350;

        private static readonly TechKind[] AcademyResearchOrder = { TechKind.Tools, TechKind.Carts, TechKind.Weapons, TechKind.Armour, TechKind.Siegecraft };
        private static readonly TechKind[] MountainResearchOrder = { MountainTech.DeepShaft, MountainTech.MountainFort };
        private static readonly TechKind[] FishingResearchOrder = { FishingTech.FishingNet, FishingTech.DriedFish };
        private static readonly TechKind[] CultResearchOrder = { CultTech.Sermon, CultTech.MartyrBlessing };
        private static readonly TechKind[] TollgateResearchOrder = { TollgateTech.GateDefence, TollgateTech.GateNetwork };
        private static readonly TechKind[] MetropolisResearchOrder = { MetropolisTech.MarketFestivity, MetropolisTech.CitizenMilitia };

        private bool HasTech(uint faction, TechKind tech) => AgesOn && (world.Economies[faction - 1].Techs & (1UL << ((int)tech - 1))) != 0;

        private bool HasCavalryDrill(uint faction) => CavalryAllowed(faction) && (world.Economies[faction - 1].Techs & CavalryDrillBit) != 0;

        private int GatherTicksFor(uint faction, uint nodeId)
        {
            var rules = world.Config.Economy;
            int interval = (EarlyArmsOn ? rules.EarlyGatherIntervalTicks : rules.GatherIntervalTicks)
                - (HasTech(faction, TechKind.Tools) ? rules.ToolsGatherTicks : 0);
            if (rules.FishingEnabled && nodeId > 0 && world.Nodes[nodeId - 1].Fishing
                && world.Economies[faction - 1].Civ == CivKind.Agrarian)
                interval = interval * (1000 - rules.FishAgrarianBonusPermille) / 1000;
            if (nodeId > 0 && world.Nodes[nodeId - 1].Fishing && AgeStrength(faction, CivKind.Fishing, 2))
                interval = Math.Max(1, checked((interval * rules.FishingGatherTimePermille + 999) / 1000));
            if (nodeId > 0 && world.Nodes[nodeId - 1].Definition.Kind == ResourceKind.Ore && AgeStrength(faction, CivKind.Mountain, 2))
                interval = Math.Max(1, checked((interval * rules.MountainMiningTimePermille + 999) / 1000));
            if (PrimitiveCraftOn && vHasStoneTools(faction))
                interval = Math.Max(1, checked((interval * rules.StoneToolsGatherIntervalPermille + 999) / 1000));
            return Math.Max(1, interval);
        }

        private bool vHasStoneTools(uint faction)
        {
            for (int i = 0; i < world.VillagerCount; i++)
                if (world.Villagers[i].Alive && world.Villagers[i].FactionId == faction && world.Villagers[i].StoneToolsUsed) return true;
            return false;
        }

        private int CarryFor(uint faction, bool fishing = false)
        {
            int carry = world.Config.Economy.CarryCapacity + (HasTech(faction, TechKind.Carts) ? world.Config.Economy.CartsCarry : 0);
            if (fishing && AgeStrength(faction, CivKind.Fishing, 4)) carry = checked(carry + world.Config.Economy.FishingFoodCarryBonus);
            return fishing && HasTech(faction, FishingTech.FishingNet)
                ? checked(carry * (1000 + world.Config.Economy.FishingNetCarryBonusPermille) / 1000) : carry;
        }

        private int SmeltTicksFor(uint faction)
        {
            int ticks = world.Config.Economy.SmeltTicks - (HasTech(faction, TechKind.BlastFurnace) ? world.Config.Economy.BlastFurnaceTicks : 0);
            return AgeStrength(faction, CivKind.Metallurgy, 2)
                ? Math.Max(1, checked((ticks * world.Config.Economy.MetallurgySmeltSpeedPermille + 999) / 1000)) : ticks;
        }

        /// <summary>A farm's ticks per food: its ground's pace, quicker with irrigation, never under 10.</summary>
        private int FarmTicksFor(BuildingState farm)
        {
            int ticks = farm.Interval - (HasTech(farm.FactionId, TechKind.Irrigation) ? world.Config.Economy.IrrigationTicks : 0);
            return EarlyArmsOn ? Math.Max(10, ticks / 2) : Math.Max(10, ticks);
        }

        /// <summary>Whether this faction may research <paramref name="tech"/> at all (the civilisation techs are for their own civilisation).</summary>
        private bool TechOpen(uint faction, TechKind tech)
        {
            var e = world.Economies[faction - 1];
            if (tech == CavalryDrillTech) return CavalryAllowed(faction) && e.Age >= 2 && !HasCavalryDrill(faction);
            if (tech == MountainTech.DeepShaft) return MountainAllowed(faction) && e.Age >= 2 && !HasTech(faction, tech);
            if (tech == MountainTech.MountainFort) return MountainAllowed(faction) && e.Age >= 3 && !HasTech(faction, tech);
            if (tech == FishingTech.FishingNet) return FishingAllowed(faction) && e.Age >= 2 && !HasTech(faction, tech);
            if (tech == FishingTech.DriedFish) return FishingAllowed(faction) && e.Age >= 3 && !HasTech(faction, tech);
            if (tech == CultTech.Sermon) return CultAllowed(faction) && e.Age >= 2 && !HasTech(faction, tech);
            if (tech == CultTech.MartyrBlessing) return CultAllowed(faction) && e.Age >= 3 && !HasTech(faction, tech);
            if (tech == TollgateTech.GateDefence) return TollgateAllowed(faction) && e.Age >= 2 && !HasTech(faction, tech);
            if (tech == TollgateTech.GateNetwork) return TollgateAllowed(faction) && e.Age >= 3 && !HasTech(faction, tech);
            if (tech == MetropolisTech.MarketFestivity) return MetropolisAllowed(faction) && e.Age >= 2 && !HasTech(faction, tech);
            if (tech == MetropolisTech.CitizenMilitia) return MetropolisAllowed(faction) && e.Age >= 3 && !HasTech(faction, tech);
            if (tech == SanctuaryTech.Pilgrimage) return SanctuaryAllowed(faction) && e.Age >= 2 && !HasTech(faction, tech);
            if (tech == SanctuaryTech.HolyRelic) return SanctuaryAllowed(faction) && e.Age >= 3 && !HasTech(faction, tech);
            if (e.Civ == CivKind.Primitive || tech < TechKind.Weapons || HasTech(faction, tech)) return false;
            if (tech == TechKind.TotalWar || tech == TechKind.GrandWorkshop)
                return AgeFourOn && e.Age >= 4;
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

        private bool IsCultTech(TechKind tech)
            => tech == CultTech.Sermon || tech == CultTech.MartyrBlessing;

        private bool IsTollgateTech(TechKind tech)
            => tech == TollgateTech.GateDefence || tech == TollgateTech.GateNetwork;

        private bool IsMetropolisTech(TechKind tech)
            => tech == MetropolisTech.MarketFestivity || tech == MetropolisTech.CitizenMilitia;

        private int FishingFoodCost(TechKind tech)
            => tech == FishingTech.FishingNet ? world.Config.Economy.FishingNetFoodCost : world.Config.Economy.DriedFishFoodCost;

        private int FishingWoodCost(TechKind tech)
            => tech == FishingTech.FishingNet ? world.Config.Economy.FishingNetWoodCost : world.Config.Economy.DriedFishWoodCost;

        private int FishingTicks(TechKind tech)
            => tech == FishingTech.FishingNet ? world.Config.Economy.FishingNetTicks : world.Config.Economy.DriedFishTicks;

        private int CultFoodCost(TechKind tech)
            => tech == CultTech.Sermon ? world.Config.Economy.SermonFoodCost : world.Config.Economy.MartyrBlessingFoodCost;

        private int CultWoodCost(TechKind tech)
            => tech == CultTech.Sermon ? world.Config.Economy.SermonWoodCost : world.Config.Economy.MartyrBlessingWoodCost;

        private int CultTicks(TechKind tech)
            => tech == CultTech.Sermon ? world.Config.Economy.SermonTicks : world.Config.Economy.MartyrBlessingTicks;

        private int TollgateFoodCost(TechKind tech)
            => tech == TollgateTech.GateDefence ? world.Config.Economy.TollgateGateDefenceFoodCost : world.Config.Economy.TollgateGateNetworkFoodCost;

        private int TollgateWoodCost(TechKind tech)
            => tech == TollgateTech.GateDefence ? world.Config.Economy.TollgateGateDefenceWoodCost : world.Config.Economy.TollgateGateNetworkWoodCost;

        private int TollgateTicks(TechKind tech)
            => tech == TollgateTech.GateDefence ? world.Config.Economy.TollgateGateDefenceTicks : world.Config.Economy.TollgateGateNetworkTicks;

        private int MetropolisFoodCost(TechKind tech)
            => tech == MetropolisTech.MarketFestivity ? world.Config.Economy.MetropolisMarketFoodCost : world.Config.Economy.MetropolisMilitiaFoodCost;

        private int MetropolisWoodCost(TechKind tech)
            => tech == MetropolisTech.MarketFestivity ? world.Config.Economy.MetropolisMarketWoodCost : world.Config.Economy.MetropolisMilitiaWoodCost;

        private int MetropolisTicks(TechKind tech)
            => tech == MetropolisTech.MarketFestivity ? world.Config.Economy.MetropolisMarketTicks : world.Config.Economy.MetropolisMilitiaTicks;

        private bool IsSanctuaryTech(TechKind tech)
            => tech == SanctuaryTech.Pilgrimage || tech == SanctuaryTech.HolyRelic;

        private int SanctuaryFoodCost(TechKind tech)
            => tech == SanctuaryTech.Pilgrimage ? world.Config.Economy.SanctuaryPilgrimageFoodCost : world.Config.Economy.SanctuaryRelicFoodCost;

        private int SanctuaryWoodCost(TechKind tech)
            => tech == SanctuaryTech.Pilgrimage ? world.Config.Economy.SanctuaryPilgrimageWoodCost : world.Config.Economy.SanctuaryRelicWoodCost;

        private int SanctuaryTicks(TechKind tech)
            => tech == SanctuaryTech.Pilgrimage ? world.Config.Economy.SanctuaryPilgrimageTicks : world.Config.Economy.SanctuaryRelicTicks;

        private int TechFoodCost(TechKind tech)
            => IsSanctuaryTech(tech) ? SanctuaryFoodCost(tech)
                : IsTollgateTech(tech) ? TollgateFoodCost(tech)
                : IsMetropolisTech(tech) ? MetropolisFoodCost(tech)
                : IsCultTech(tech) ? CultFoodCost(tech)
                : IsFishingTech(tech) ? FishingFoodCost(tech)
                : tech == BridgeTech.Bridgeworks ? world.Config.Economy.BridgeworksFoodCost
                : tech == BridgeTech.SiegeDeployment ? world.Config.Economy.SiegeDeploymentFoodCost
                : world.Config.Economy.TechFood[(int)tech - 1];

        private int TechWoodCost(TechKind tech)
            => IsSanctuaryTech(tech) ? SanctuaryWoodCost(tech)
                : IsTollgateTech(tech) ? TollgateWoodCost(tech)
                : IsMetropolisTech(tech) ? MetropolisWoodCost(tech)
                : IsCultTech(tech) ? CultWoodCost(tech)
                : IsFishingTech(tech) ? FishingWoodCost(tech)
                : tech == BridgeTech.Bridgeworks ? world.Config.Economy.BridgeworksWoodCost
                : tech == BridgeTech.SiegeDeployment ? world.Config.Economy.SiegeDeploymentWoodCost
                : world.Config.Economy.TechWood[(int)tech - 1];

        private int TechMetalCost(TechKind tech)
            => IsBridgeTech(tech) || IsFishingTech(tech) || IsCultTech(tech) || IsTollgateTech(tech) || IsMetropolisTech(tech) || IsSanctuaryTech(tech) ? 0 : world.Config.Economy.TechMetal[(int)tech - 1];

        private int TechGemsCost(TechKind tech)
            => IsBridgeTech(tech) || IsFishingTech(tech) || IsCultTech(tech) || IsTollgateTech(tech) || IsMetropolisTech(tech) || IsSanctuaryTech(tech) ? 0 : world.Config.Economy.TechGems[(int)tech - 1];

        private int TechTicks(TechKind tech)
            => IsSanctuaryTech(tech) ? SanctuaryTicks(tech)
                : IsTollgateTech(tech) ? TollgateTicks(tech)
                : IsMetropolisTech(tech) ? MetropolisTicks(tech)
                : IsCultTech(tech) ? CultTicks(tech)
                : IsFishingTech(tech) ? FishingTicks(tech)
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
        {
            int bonus = HasTech(faction, BridgeTech.SiegeDeployment) ? world.Config.Economy.SiegeDeploymentRamCapacityBonus : 0;
            if (PlanksAllowed(faction) && world.Economies[faction - 1].Planks >= world.Config.Economy.PlanksPerRam
                && OwnBuildingIndex(faction, BuildingKind.SiegeWorkshop) >= 0)
                bonus += world.Config.Economy.PlankExtraRams;
            if (CraftTree3On)
                // Stored siege parts are a reservation for additional rams.  This makes the
                // stock visible to the same numeric cap that gates automatic training.
                bonus += world.Economies[faction - 1].SiegeParts;
            return AutoRams + bonus;
        }

        private bool BeingResearched(uint faction, TechKind tech)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.FactionId == faction && (b.Kind == BuildingKind.Blacksmith || b.Kind == BuildingKind.Academy || b.Kind == BuildingKind.MineShaft || b.Kind == BuildingKind.Harbor || b.Kind == BuildingKind.Monastery || b.Kind == BuildingKind.Tollgate || b.Kind == BuildingKind.GrandHouse || b.Kind == BuildingKind.Shrine) && b.Researching == tech) return true;
            }
            return false;
        }

        private bool FailAgeFourAiResearch(uint faction, TechKind tech, byte reason)
        {
            if (AgeFourAiOn)
            {
                ref var economy = ref world.Economies[faction - 1];
                economy.AgeFourAiResearchFailureTech = tech;
                economy.AgeFourAiResearchFailureReason = reason;
                economy.AgeFourAiResearchFailureTick = world.Tick;
            }
            return false;
        }

        /// <summary>Pays and starts <paramref name="tech"/> at <paramref name="smith"/>; returns false and changes nothing when it cannot.</summary>
        private bool StartResearch(uint faction, ref BuildingState smith, TechKind tech, bool byPlayer)
        {
            bool academy = smith.Kind == BuildingKind.Academy;
            bool mountain = smith.Kind == BuildingKind.MineShaft;
            bool fishing = smith.Kind == BuildingKind.Harbor;
            bool cult = smith.Kind == BuildingKind.Monastery;
            bool tollgate = smith.Kind == BuildingKind.Tollgate;
            bool metropolis = smith.Kind == BuildingKind.GrandHouse;
            bool sanctuary = smith.Kind == BuildingKind.Shrine;
            if (!AgesOn) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailurePrerequisite);
            if (!smith.Complete || BuildingPaused(smith)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailureBuilding);
            if (smith.Researching != 0 || BeingResearched(faction, tech)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailureResearching);
            if (academy)
            {
                if (!AcademyAllowed(faction) || !AcademyTechOpen(faction, tech) || !AcademyGoldAvailable(faction, tech)
                    || !AcademyBudgetAllows(faction, tech)) return FailAgeFourAiResearch(faction, tech,
                        AcademyAllowed(faction) && AcademyTechOpen(faction, tech) ? AgeFourAiResearchFailureResource : AgeFourAiResearchFailurePrerequisite);
            }
            else if (mountain)
            {
                if (!IsMountainTech(tech) || !TechOpen(faction, tech)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailurePrerequisite);
            }
            else if (fishing)
            {
                if (!IsFishingTech(tech) || !TechOpen(faction, tech)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailurePrerequisite);
            }
            else if (cult)
            {
                if (!IsCultTech(tech) || !TechOpen(faction, tech)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailurePrerequisite);
            }
            else if (tollgate)
            {
                if (!IsTollgateTech(tech) || !TechOpen(faction, tech)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailurePrerequisite);
            }
            else if (metropolis)
            {
                if (!IsMetropolisTech(tech) || !TechOpen(faction, tech)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailurePrerequisite);
            }
            else if (sanctuary)
            {
                if (!IsSanctuaryTech(tech) || !TechOpen(faction, tech)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailurePrerequisite);
            }
            else if (smith.Kind != BuildingKind.Blacksmith || IsMountainTech(tech) || IsTollgateTech(tech) || IsMetropolisTech(tech) || IsSanctuaryTech(tech) || !TechOpen(faction, tech))
                return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailurePrerequisite);
            ref var economy = ref world.Economies[faction - 1];
            if (CraftTree3On && economy.Age >= 3 && IsThirdAgeResearch(tech)
                && economy.Mechanism < 1 && economy.Scholarly >= world.Config.Economy.ScholarlyPerResearch
                && OwnBuildingIndex(faction, BuildingKind.Assembler) >= 0)
                // Let the single assembler turn Scholarly into Mechanism first.  Without this
                // one-cycle reservation, DecideResearch runs before DecideIndustry and consumes
                // the intermediate directly, so Mechanism can never reach a third-age research.
                return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailureResource);
            int food = academy ? AcademyFoodCost(tech) : mountain ? MountainFoodCost(tech) : fishing ? FishingFoodCost(tech) : tollgate ? TollgateFoodCost(tech) : metropolis ? MetropolisFoodCost(tech) : sanctuary ? SanctuaryFoodCost(tech) : TechFoodCost(tech);
            int wood = academy ? AcademyWoodCost(tech) : mountain ? MountainWoodCost(tech) : fishing ? FishingWoodCost(tech) : tollgate ? TollgateWoodCost(tech) : metropolis ? MetropolisWoodCost(tech) : sanctuary ? SanctuaryWoodCost(tech) : TechWoodCost(tech);
            int gold = academy ? AcademyGoldCost(tech) : 0;
            int normalMetal = academy || mountain || fishing ? 0 : TechMetalCost(tech);
            int metal = MountainAllowed(faction) ? 0 : normalMetal;
            int oreForMetal = MountainAllowed(faction) ? normalMetal : 0;
            int gems = academy || mountain || fishing || metropolis ? 0 : TechGemsCost(tech);
            // Books belong to the Academy civilisation, not to one particular research building.  Keep the
            // effect at the common start point so both the player's EconomyCommand.Research route and the
            // autonomous route receive the same deterministic discount/shortening at a blacksmith or academy.
            // Books belong to the Academy civilisation, not to one particular research building.  Keep the
            // effect at the common start point so both the player's EconomyCommand.Research route and the
            // autonomous route receive the same deterministic discount/shortening at a blacksmith or academy.
            bool bookResearch = BooksAllowed(faction) && economy.Books >= world.Config.Economy.BooksPerResearch;
            bool mechanismResearch = CraftTree3On && economy.Age >= 3 && IsThirdAgeResearch(tech) && economy.Mechanism >= 1;
            bool scholarlyResearch = CraftTree2On && !bookResearch && !mechanismResearch && economy.Scholarly >= world.Config.Economy.ScholarlyPerResearch;
            if (bookResearch)
            {
                food = BookDiscount(food, world.Config.Economy.BookResearchCostPermille);
                wood = BookDiscount(wood, world.Config.Economy.BookResearchCostPermille);
                metal = BookDiscount(metal, world.Config.Economy.BookResearchCostPermille);
                gems = BookDiscount(gems, world.Config.Economy.BookResearchCostPermille);
            }
            if (scholarlyResearch)
            {
                food = ScholarlyDiscount(food, world.Config.Economy.ScholarlyCostPermille);
                wood = ScholarlyDiscount(wood, world.Config.Economy.ScholarlyCostPermille);
                metal = ScholarlyDiscount(metal, world.Config.Economy.ScholarlyCostPermille);
                oreForMetal = ScholarlyDiscount(oreForMetal, world.Config.Economy.ScholarlyCostPermille);
                gems = ScholarlyDiscount(gems, world.Config.Economy.ScholarlyCostPermille);
                gold = ScholarlyDiscount(gold, world.Config.Economy.ScholarlyCostPermille);
            }
            if (mechanismResearch)
            {
                food = ScholarlyDiscount(food, world.Config.Economy.MechanismResearchCostPermille);
                wood = ScholarlyDiscount(wood, world.Config.Economy.MechanismResearchCostPermille);
                metal = ScholarlyDiscount(metal, world.Config.Economy.MechanismResearchCostPermille);
                oreForMetal = ScholarlyDiscount(oreForMetal, world.Config.Economy.MechanismResearchCostPermille);
                gems = ScholarlyDiscount(gems, world.Config.Economy.MechanismResearchCostPermille);
                gold = ScholarlyDiscount(gold, world.Config.Economy.MechanismResearchCostPermille);
            }
            if (scholarlyResearch && AgeStrength(faction, CivKind.Academy, 3))
            {
                food = ScholarlyDiscount(food, world.Config.Economy.AcademyScholarlyCostPermille);
                wood = ScholarlyDiscount(wood, world.Config.Economy.AcademyScholarlyCostPermille);
                metal = ScholarlyDiscount(metal, world.Config.Economy.AcademyScholarlyCostPermille);
                gold = ScholarlyDiscount(gold, world.Config.Economy.AcademyScholarlyCostPermille);
            }
            if (AgeStrength(faction, CivKind.Academy, 4) && (tech == TechKind.TotalWar || tech == TechKind.GrandWorkshop))
            {
                food = ScholarlyDiscount(food, world.Config.Economy.AcademyAge4ResearchCostPermille);
                wood = ScholarlyDiscount(wood, world.Config.Economy.AcademyAge4ResearchCostPermille);
                metal = ScholarlyDiscount(metal, world.Config.Economy.AcademyAge4ResearchCostPermille);
                gold = ScholarlyDiscount(gold, world.Config.Economy.AcademyAge4ResearchCostPermille);
            }
            // Research must not consume the metal reserved for the first recovery infantry.
            if (ArmyRecoveryWouldConsumeReserve(faction, ResourceKind.Metal, metal)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailureResource);
            int heldWood = !byPlayer && BuildingReservationsOn ? ReservedConstructionWood(faction) : 0;
            heldWood = checked(heldWood + (!byPlayer ? AgeFourAiWoodReserve(faction) : 0));
            if (!byPlayer && automaticEconomyAction && AiExpandTownPending(faction)
                && economy.Wood - wood < TownWoodCostFor(faction))
                return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailureResource);
            if (economy.Food < food || economy.Wood - heldWood < wood || economy.Gold < gold || economy.Metal < metal
                || economy.Ore < oreForMetal || economy.Gems < gems) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailureResource);
            if (cult && !CultBudgetAllows(faction, food, wood)) return FailAgeFourAiResearch(faction, tech, AgeFourAiResearchFailureResource);
            economy.Food = checked(economy.Food - food);
            economy.Wood = checked(economy.Wood - wood);
            RecordLargeUse(faction, ResourceKind.Wood, LargeEconomyUseKind.Research, wood);
            economy.Gold = checked(economy.Gold - gold);
            economy.Metal = checked(economy.Metal - metal);
            if (AiFactoryOn) economy.MetalConsumed = checked(economy.MetalConsumed + metal);
            economy.Ore = checked(economy.Ore - oreForMetal);
            economy.Gems = checked(economy.Gems - gems);
            if (bookResearch)
            {
                economy.Books = checked(economy.Books - world.Config.Economy.BooksPerResearch);
                economy.BooksConsumed = checked(economy.BooksConsumed + world.Config.Economy.BooksPerResearch);
                economy.BooksUsedResearch++;
            }
            if (scholarlyResearch)
            {
                economy.Scholarly = checked(economy.Scholarly - world.Config.Economy.ScholarlyPerResearch);
                economy.ScholarlyConsumed = checked(economy.ScholarlyConsumed + world.Config.Economy.ScholarlyPerResearch);
                economy.ScholarlyUsedResearch++;
            }
            if (mechanismResearch)
            {
                economy.Mechanism = checked(economy.Mechanism - 1);
                economy.MechanismConsumed = checked(economy.MechanismConsumed + 1);
                economy.MechanismUsedResearch++;
            }
            smith.Researching = tech;
            int ticks = academy ? AcademyTicks(tech) : mountain ? MountainTicks(tech) : fishing ? FishingTicks(tech) : tollgate ? TollgateTicks(tech) : metropolis ? MetropolisTicks(tech) : sanctuary ? SanctuaryTicks(tech) : TechTicks(tech);
            smith.TrainRemaining = bookResearch ? Math.Max(1, checked(ticks * world.Config.Economy.BookResearchPermille / 1000))
                : mechanismResearch ? Math.Max(1, checked((ticks * world.Config.Economy.MechanismResearchTimePermille + 999) / 1000))
                : scholarlyResearch ? Math.Max(1, checked((ticks * world.Config.Economy.ScholarlyTimePermille + 999) / 1000)) : ticks;
            if (AgeStrength(faction, CivKind.Academy, 2))
                smith.TrainRemaining = Math.Max(1, checked((smith.TrainRemaining * world.Config.Economy.AcademyResearchTimePermille + 999) / 1000));
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
                if (!IsActualBuilding(b) || BuildingPaused(b) || (b.Kind != BuildingKind.Blacksmith && b.Kind != BuildingKind.Academy && b.Kind != BuildingKind.MineShaft && b.Kind != BuildingKind.Harbor && b.Kind != BuildingKind.Monastery && b.Kind != BuildingKind.Tollgate && b.Kind != BuildingKind.GrandHouse && b.Kind != BuildingKind.Shrine) || b.Researching == 0) continue;
                if (--b.TrainRemaining > 0) continue;
                var tech = b.Researching;
                b.Researching = 0;
                b.TrainRemaining = 0;
                world.Economies[b.FactionId - 1].Techs |= 1UL << ((int)tech - 1);
                if (tech == TollgateTech.GateDefence)
                {
                    for (int j = 0; j < world.BuildingCount; j++)
                    {
                        ref var gate = ref world.Buildings[j];
                    if (IsActualBuilding(gate) && gate.FactionId == b.FactionId && gate.Kind == BuildingKind.Tollgate)
                            gate.Hp = TollgateHpFor(gate.Hp, gate.FactionId);
                    }
                }
                if (tech == BridgeTech.Bridgeworks)
                {
                    var bonus = world.Config.Economy.BridgeworksHpBonus;
                    for (int j = 0; j < world.BuildingCount; j++)
                    {
                        ref var bridge = ref world.Buildings[j];
                    if (IsActualBuilding(bridge) && bridge.FactionId == b.FactionId && bridge.Kind == BuildingKind.Bridge)
                            bridge.Hp = checked(bridge.Hp + bonus);
                    }
                }
                if (tech == MountainTech.MountainFort)
                {
                    for (int j = 0; j < world.BuildingCount; j++)
                    {
                        ref var defence = ref world.Buildings[j];
                    if (!IsActualBuilding(defence) || defence.FactionId != b.FactionId
                            || (defence.Kind != BuildingKind.Wall && defence.Kind != BuildingKind.Tower)) continue;
                        defence.Hp = MountainFortHp(defence.Hp, defence.FactionId, defence.OriginCell, SizeOf(defence.Kind));
                    }
                }
                if (tech == CultTech.MartyrBlessing)
                {
                    foreach (int soldier in world.SoldierTraversal)
                        if (world.Soldiers[soldier].Alive && world.Soldiers[soldier].Initial.FactionId == b.FactionId
                            && world.Soldiers[soldier].Initial.Kind == UnitKind.Monk)
                            ApplyTech(soldier, tech);
                    continue;
                }
                if (tech != TechKind.Weapons && tech != TechKind.Armour && tech != TechKind.SteelWeapons && tech != TechKind.SteelArmour
                    && tech != TechKind.GemArmor && tech != TechKind.TotalWar) continue;
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
            if (HasTech(faction, TechKind.TotalWar)) ApplyTech(index, TechKind.TotalWar);
            if (HasCavalryDrill(faction)) ApplyTech(index, CavalryDrillTech);
            if (HasTech(faction, CultTech.MartyrBlessing) && world.Soldiers[index].Initial.Kind == UnitKind.Monk)
                ApplyTech(index, CultTech.MartyrBlessing);
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
            else if (tech == TechKind.TotalWar)
            {
                s.Parameters.Hp = Math.Max(1, checked(s.Parameters.Hp * 1100 / 1000));
                s.Hp = Math.Max(1, checked(s.Hp * 1100 / 1000));
            }
            else if (tech == TechKind.Weapons) s.Parameters.Damage = checked(s.Parameters.Damage + rules.WeaponsDamage);
            else if (tech == TechKind.SteelWeapons) s.Parameters.Damage = checked(s.Parameters.Damage + rules.SteelWeaponsDamage);
            else if (tech == CultTech.MartyrBlessing && s.Initial.Kind == UnitKind.Monk)
            {
                int hp = checked(s.Parameters.Hp * rules.MartyrBlessingHpPermille / 1000);
                int delta = checked(hp - s.Parameters.Hp);
                s.Parameters.Hp = hp;
                s.Hp = checked(s.Hp + delta);
            }
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
            if (AgeFourAiOn && world.Economies[faction - 1].Age >= 4 && TryAgeFourAiResearchBeforeNormalDecision(faction)) return;
            if (world.Economies[faction - 1].Civ == CivKind.Fishing && DecideFishingResearch(faction)) return;
            if (world.Economies[faction - 1].Civ == CivKind.Mountain && DecideMountainResearch(faction)) return;
            if (world.Economies[faction - 1].Civ == CivKind.Cult)
            {
                if (DecideCultResearch(faction)) return;
            }
            if (world.Economies[faction - 1].Civ == CivKind.Tollgate)
            {
                if (DecideTollgateResearch(faction)) return;
            }
            if (world.Economies[faction - 1].Civ == CivKind.Metropolis)
            {
                if (DecideMetropolisResearch(faction)) return;
            }
            if (world.Economies[faction - 1].Civ == CivKind.Sanctuary)
            {
                if (DecideSanctuaryResearch(faction)) return;
            }
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
                else if (!IsMetropolisTech(tech) && TechOpen(faction, tech)) { StartResearch(faction, ref b, tech, false); return; }
        }

        private bool TryAgeFourAiResearchBeforeNormalDecision(uint faction)
        {
            int smithIndex = OwnBuildingIndex(faction, BuildingKind.Blacksmith);
            if (smithIndex < 0) return false;
            ref var smith = ref world.Buildings[smithIndex];
            if (!smith.Complete || smith.Held || smith.Researching != 0) return false;
            foreach (var tech in AgeFourAiResearchOrder)
            {
                if (!TechOpen(faction, tech)) continue;
                if (StartResearch(faction, ref smith, tech, false)) return true;
            }
            return false;
        }

        private bool DecideMetropolisResearch(uint faction)
        {
            if (!MetropolisAllowed(faction)) return false;
            int grandHouse = OwnBuildingIndex(faction, BuildingKind.GrandHouse);
            if (grandHouse < 0) return false;
            ref var building = ref world.Buildings[grandHouse];
            if (!building.Complete || building.Held || building.Researching != 0) return true;
            foreach (var tech in MetropolisResearchOrder)
                if (TechOpen(faction, tech))
                {
                    if (StartResearch(faction, ref building, tech, false)) return true;
                    return false;
                }
            return false;
        }

        private bool DecideCultResearch(uint faction)
        {
            if (!CultAllowed(faction)) return false;
            int monastery = OwnBuildingIndex(faction, BuildingKind.Monastery);
            if (monastery < 0) return false;
            ref var building = ref world.Buildings[monastery];
            if (!building.Complete || building.Held || building.Researching != 0) return true;
            foreach (var tech in CultResearchOrder)
                if (TechOpen(faction, tech))
                {
                    if (StartResearch(faction, ref building, tech, false)) return true;
                    return false;
                }
            return false;
        }

        private bool DecideTollgateResearch(uint faction)
        {
            if (!TollgateAllowed(faction)) return false;
            int gate = OwnBuildingIndex(faction, BuildingKind.Tollgate);
            if (gate < 0) return false;
            ref var building = ref world.Buildings[gate];
            if (!building.Complete || building.Held || building.Researching != 0) return true;
            foreach (var tech in TollgateResearchOrder)
                if (TechOpen(faction, tech)) return StartResearch(faction, ref building, tech, false);
            return false;
        }

        private bool CultBudgetAllows(uint faction, int foodCost, int woodCost)
        {
            var e = world.Economies[faction - 1];
            var rules = world.Config.Economy;
            var (ageFood, ageWood, ageGold, _) = AdvancePrice(faction, e);
            int foodReserve = ageFood + (e.Civ == CivKind.Primitive ? 0 : rules.MarketFoodFloor);
            int woodReserve = ageWood;
            if (LivingVillagers(faction) + e.Queued < AutoVillagerTargetFor(faction))
                foodReserve = checked(foodReserve + VillagerFoodCostFor(faction));
            if (CompleteBarracks(faction) && LivingSoldiers(faction) == 0)
            {
                foodReserve = checked(foodReserve + InfantryFoodFor(faction));
                woodReserve = checked(woodReserve + InfantryWoodFor(faction));
            }
            return e.Food >= checked(foodCost + foodReserve) && e.Wood >= checked(woodCost + woodReserve)
                && e.Gold >= ageGold;
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
            if (BooksAllowed(faction) && DecideBooks(faction)) return true;
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
            int foodCost = AcademyFoodCost(tech), woodCost = AcademyWoodCost(tech);
            if (e.Books >= rules.BooksPerResearch && BooksAllowed(faction))
            {
                foodCost = BookDiscount(foodCost, rules.BookResearchCostPermille);
                woodCost = BookDiscount(woodCost, rules.BookResearchCostPermille);
            }
            return e.Food >= checked(foodCost + foodReserve)
                && e.Wood >= checked(woodCost + woodReserve)
                && e.Gold >= checked(AcademyGoldCost(tech) + goldReserve);
        }

        private int BookDiscount(int cost, int permille)
            => cost <= 0 ? 0 : checked((cost * permille + 999) / 1000);

        private int ScholarlyDiscount(int cost, int permille)
            => cost <= 0 ? 0 : checked((cost * permille + 999) / 1000);

        private bool IsThirdAgeResearch(TechKind tech)
            => tech == TechKind.Siegecraft || tech == TechKind.Masonry || tech == TechKind.Banking || tech == TechKind.GemArmor
                || tech == BridgeTech.SiegeDeployment || tech == MountainTech.MountainFort || tech == FishingTech.DriedFish
                || tech == CultTech.MartyrBlessing || tech == TollgateTech.GateNetwork || tech == MetropolisTech.CitizenMilitia
                || tech == SanctuaryTech.HolyRelic;

        private bool AcademyOffenseReady(uint faction)
        {
            var e = world.Economies[faction - 1];
            return e.Civ == CivKind.Academy && ((HasTech(faction, TechKind.Weapons) && HasTech(faction, TechKind.Armour))
                || HasTech(faction, TechKind.Siegecraft));
        }
    }
}
