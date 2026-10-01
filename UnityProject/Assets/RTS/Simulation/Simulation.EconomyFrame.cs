using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// Ver.3: the economy part of a faction frame (V3-1 PR5). Own villagers and buildings in full; enemy villagers and
    /// buildings only while the faction sees them, and enemy villagers as bare positions, as enemy soldiers are shown.
    /// Resources are public in V3-1. The frame is display data only and is never read back into the simulation.
    /// </summary>
    public sealed partial class Simulation
    {
        private EconomyView EconomyViewFor(uint faction)
        {
            if (!EconomyOn) return null;
            var rules = world.Config.Economy;
            var economy = world.Economies[faction - 1];
            var villagers = new List<VillagerView>();
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (!v.Alive) continue;
                if (v.FactionId == faction)
                    villagers.Add(new VillagerView(v.Id, true, v.Position, Activity(v.Task), v.CarryKind, v.Carry, v.Hp, v.Held,
                        v.CaravanMarketId, v.CaravanseraiId, v.CaravanOutpostId, v.CaravanStage, v.CaravanWood, v.CaravanGems,
                        CaravanStopReasonFor(v)));
                else if (IsVisibleTo(faction, v.Position))
                    villagers.Add(new VillagerView(0, false, v.Position, VillagerActivity.Idle, 0, 0, 0));
            }
            var buildings = new List<BuildingView>();
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive) continue;
                bool own = b.FactionId == faction;
                if (!own && !BuildingVisibleTo(faction, b)) continue;
                int size = SizeOf(b.Kind);
                CaravanStopReason caravanReason = CaravanStopReason.None;
                if (b.Kind == BuildingKind.Caravanserai && own)
                {
                    if (b.CaravanMarketId == 0 || b.CaravanMarketId > world.BuildingCount
                        || !world.Buildings[b.CaravanMarketId - 1].Alive || !world.Buildings[b.CaravanMarketId - 1].Complete
                        || world.Buildings[b.CaravanMarketId - 1].FactionId != faction
                        || world.Buildings[b.CaravanMarketId - 1].Kind != BuildingKind.Market)
                        caravanReason = CaravanStopReason.MarketLost;
                    else if (b.CaravanOutpostId == 0 || b.CaravanOutpostId > world.Outposts.Length
                        || world.Outposts[b.CaravanOutpostId - 1].OwnerFactionId != faction)
                        caravanReason = CaravanStopReason.OutpostLost;
                    else caravanReason = CaravanStopReason.Normal;
                }
                buildings.Add(new BuildingView(b.Id, b.FactionId, b.Kind, BuildingCenter(b), size * world.Config.Map.CellSizeMeters,
                    own ? b.Hp : 0, own ? HpOf(b.Kind, b.FactionId, b.OriginCell) : 0, b.Complete, own ? b.Progress : 0, WorkOf(b.Kind, b.FactionId),
                    own ? b.Queued : 0, own ? b.TrainRemaining : 0, b.Facing, own ? b.Input : 0, own ? b.Output : 0, own && b.Held,
                    own ? b.Researching : 0, own && b.Researching != 0 ? b.TrainRemaining : 0,
                    own && (ProcessingOn || ForestryOn) ? b.InputSecondary : 0,
                    own && ForestryOn && economy.Age >= 2 ? b.QueuedBowGear : 0,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanMarketId : 0,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanOutpostId : 0,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanDistance : default,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanWoodReward : 0, caravanReason));
            }
            var resources = new List<ResourceView>();
            foreach (var n in world.Nodes)
                if (n.Remaining > 0) resources.Add(new ResourceView(n.Definition.Id, n.Definition.Kind, n.Definition.Position, n.Remaining));
            int population = LivingVillagers(faction) + LivingSoldiers(faction);
            // V3-2: own belts in full, enemy belts on cells the faction sees now (without what they carry).
            var belts = new List<BeltView>();
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                var b = world.Belts[cell];
                if (b.FactionId == 0) continue;
                bool own = b.FactionId == faction;
                if (!own && !world.Factions[faction - 1].VisibleCells[cell]) continue;
                belts.Add(new BeltView(cell, b.FactionId, b.Facing, own ? b.Item : 0, own ? b.Progress : 0, own && b.Held));
            }
            var cavalryMission = CavalryMissionFor(faction);
            return new EconomyView(economy.Food, economy.Wood, population, PopCapFor(faction), economy.Queued, economy.TrainRemaining,
                !economy.AutoOff, rules.BarracksSizeCells, rules.BarracksWoodCost, rules.VillagerFoodCost, InfantryFoodFor(faction),
                InfantryWoodFor(faction), villagers, buildings, resources,
                rules.Industry, economy.Ore, economy.Metal, rules.BeltWoodCost, rules.BeltTicksPerCell, belts,
                InfantryMetalFor(faction), rules.MineWoodCost, rules.SmelterWoodCost, rules.MineSizeCells, rules.SmelterSizeCells, economy.CoreHeld, economy.Policy,
                rules.Ages, economy.Civ, economy.AdvancingTo, economy.AdvanceRemaining, rules.AdvanceFoodCost, rules.AdvanceWoodCost,
                rules.FarmWoodCost, rules.FarmSizeCells, rules.Ages ? rules.ScoutFoodCost : 0, rules.Ages ? rules.HouseWoodCost : 0, rules.Ages ? rules.DropSiteWoodCost : 0,
                 economy.Stone, rules.Ages ? StoneOf(BuildingKind.Wall, faction) : 0, rules.Ages ? WoodOf(BuildingKind.Tower, faction) : 0, rules.Ages ? StoneOf(BuildingKind.Tower, faction) : 0,
                rules.Ages ? rules.BlacksmithWoodCost : 0, economy.Techs, rules.Ages ? rules.TechFood : null, rules.Ages ? rules.TechWood : null, rules.Ages ? rules.TechMetal : null,
                economy.Age, rules.Ages ? rules.Age2FoodCost : 0, rules.Ages ? rules.Age2WoodCost : 0, rules.Ages ? rules.ArcherFood : 0, rules.Ages ? rules.ArcherWood : 0,
                rules.Ages ? rules.CavalryFood : 0, rules.Ages ? rules.CavalryWood : 0, rules.Ages ? rules.CavalryMetal : 0,
                rules.Ages ? rules.MarketWoodCost : 0, rules.Ages ? rules.WorkshopWoodCost : 0, rules.Ages ? rules.TradeLot : 0, rules.Ages ? rules.TradeReturn : 0,
                rules.Ages ? rules.RamFood : 0, rules.Ages ? rules.RamWood : 0,
                rules.Ages ? rules.Age3FoodCost : 0, rules.Ages ? rules.Age3WoodCost : 0,
                rules.Ages ? rules.RangeWoodCost : 0, rules.Ages ? rules.StableWoodCost : 0,
                 rules.Ages ? WoodOf(BuildingKind.Castle, faction) : 0, rules.Ages ? StoneOf(BuildingKind.Castle, faction) : 0,
                rules.Ages ? economy.Gems : 0, rules.Ages ? rules.GemsTradeReturn : 0, rules.Ages ? rules.TechGems : null, rules.Ages ? rules.GemArmorHp : 0,
                rules.Ages ? rules.MercenaryGems : 0, rules.Ages ? rules.MercenaryTicks : 0,
                rules.MonksEnabled, rules.MonksEnabled ? rules.MonkFoodCost : 0, rules.MonksEnabled ? rules.MonkGoldCost : 0,
                rules.MonksEnabled ? rules.MonkTrainTicks : 0,
                rules.ProcessingChain,
                ProcessingOn ? rules.CharcoalKilnWoodCost : 0, ProcessingOn ? rules.SteelworksWoodCost : 0,
                ProcessingOn ? rules.CharcoalKilnSizeCells : 0, ProcessingOn ? rules.SteelworksSizeCells : 0,
                ProcessingOn ? rules.HeavyInfantryFoodCost : 0, ProcessingOn ? rules.HeavyInfantrySteelCost : 0,
                ProcessingOn ? economy.Charcoal : 0, ProcessingOn ? economy.Steel : 0,
                (ProcessingOn || ForestryOn && economy.Age >= 2) ? new List<LineView>(LineViews(faction)) : null,
                rules.GoldEnabled ? economy.Gold : 0,
                ForestryOn ? economy.BowGear : 0, ForestryOn ? rules.FletcherWoodCost : 0, ForestryOn ? rules.FletcherSizeCells : 0,
                ForestryOn ? rules.FletcherTicks : 0, ForestryOn ? rules.SkirmishArcherFoodCost : 0,
                ForestryOn ? rules.SkirmishArcherBowGearCost : 0, ForestryOn ? rules.SkirmishArcherTrainTicks : 0, cavalryMission);
        }

        private CavalryMissionView CavalryMissionFor(uint faction)
        {
            if (!CavalryOn) return default(CavalryMissionView);
            foreach (uint armyId in world.Factions[faction - 1].ArmyIds)
            {
                var army = world.Armies[armyId - 1];
                bool mobile = false;
                foreach (uint soldierId in army.SoldierIds)
                    if (world.Soldiers[soldierId - 1].Alive && world.Soldiers[soldierId - 1].Class == UnitKind.LightCavalry) { mobile = true; break; }
                if (!mobile || army.Decision.Goal.Kind != GoalKind.Point) continue;
                uint id = army.Decision.Goal.Id;
                RaidTargetKind kind = (id & 0x80000000U) != 0 ? RaidTargetKind.IsolatedArmy
                    : (id & 0x40000000U) != 0 ? RaidTargetKind.Carrier : RaidTargetKind.Resource;
                uint first = 0;
                foreach (uint soldierId in army.SoldierIds)
                    if (world.Soldiers[soldierId - 1].Alive) { first = soldierId; break; }
                if (first == 0) continue;
                var lead = world.Soldiers[first - 1];
                var path = world.Map.FindPath(world.Map.Cell(lead.Position), army.Decision.Goal.Point);
                long eta = path.Length == 0 ? -1 : checked((long)path.Length * 20);
                return new CavalryMissionView(kind, id, army.Decision.Goal.Point, eta, army.Decision.Returning);
            }
            return default(CavalryMissionView);
        }

        private static VillagerActivity Activity(VillagerTask task) => task switch
        {
            VillagerTask.ToNode => VillagerActivity.ToResource,
            VillagerTask.Gathering => VillagerActivity.Gathering,
            VillagerTask.ToDropOff => VillagerActivity.Returning,
            VillagerTask.ToBuild => VillagerActivity.ToBuild,
            VillagerTask.Building => VillagerActivity.Building,
            VillagerTask.ToPickup => VillagerActivity.Hauling,
            VillagerTask.ToDeliver => VillagerActivity.Hauling,
            VillagerTask.ToTradeMarket => VillagerActivity.Trading,
            VillagerTask.ToTradeCore => VillagerActivity.Trading,
            VillagerTask.ToCaravanMarket => VillagerActivity.Trading,
            VillagerTask.ToCaravanserai => VillagerActivity.Trading,
            VillagerTask.ToCaravanCore => VillagerActivity.Trading,
            _ => VillagerActivity.Idle
        };

        private CaravanStopReason CaravanStopReasonFor(VillagerState v)
        {
            if (v.CaravanMarketId == 0 && v.CaravanseraiId == 0 && v.CaravanOutpostId == 0) return CaravanStopReason.None;
            if (v.CaravanDangerStopped) return CaravanStopReason.Danger;
            if (v.CaravanMarketId == 0 || v.CaravanMarketId > world.BuildingCount) return CaravanStopReason.MarketLost;
            var market = world.Buildings[v.CaravanMarketId - 1];
            if (!market.Alive || !market.Complete || market.FactionId != v.FactionId || market.Kind != BuildingKind.Market)
                return CaravanStopReason.MarketLost;
            if (v.CaravanseraiId == 0 || v.CaravanseraiId > world.BuildingCount) return CaravanStopReason.HostLost;
            var host = world.Buildings[v.CaravanseraiId - 1];
            if (!host.Alive || !host.Complete || host.FactionId != v.FactionId || host.Kind != BuildingKind.Caravanserai)
                return CaravanStopReason.HostLost;
            if (v.CaravanOutpostId == 0 || v.CaravanOutpostId > world.Outposts.Length
                || world.Outposts[v.CaravanOutpostId - 1].OwnerFactionId != v.FactionId) return CaravanStopReason.OutpostLost;
            return CaravanStopReason.Normal;
        }
    }
}
