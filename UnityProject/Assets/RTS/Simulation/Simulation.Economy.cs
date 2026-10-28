using System;
using System.Numerics;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    /// <summary>
    /// Ver.3 economy (technical-design-v3 5): gathering, dropping off, and training villagers at the core. Every method
    /// returns at once when the scenario's economy is disabled, so Ver.1 scenarios run exactly as before.
    /// V3-1 PR2 limits: villagers are not seen or attacked by the enemy yet, and there are no buildings.
    /// </summary>
    public sealed partial class Simulation
    {
        private static readonly Fix64 GatherReach = Fix64.FromInt(1);

        private bool EconomyOn => world.Config.Economy.Enabled;
        private bool EasyRebuildOn => EconomyOn && world.Config.Economy.EasyRebuild;
        // Zero is the canonical "never moved" sentinel; a >= comparison would pause every building on tick zero.
        private bool BuildingPaused(BuildingState building) => EasyRebuildOn && building.MovePauseUntilTick > world.Tick;
        private bool BuildingReservationsOn => EconomyOn && world.Config.Economy.BuildingReservations;
        private bool NoNegativeStockOn => EconomyOn && world.Config.Economy.NoNegativeStock;
        private bool EconomyScaleOn => EconomyOn && world.Config.Economy.EconomyScale;
        private bool LargeEconomyOn => EconomyOn && world.Config.Economy.LargeEconomy;

        /// <summary>V3-5: how far ahead one stock must be before idle villagers go to the other.</summary>
        private const int StockGap = 300, WoodFloor = 150, MinimumFoodGatherers = 3, MinimumWoodGatherers = 2;
        private const int ArmyRecoveryMinimumInfantry = 3;

        /// <summary>AI phase, on the allocation cycle (5.4 step 1): the automatic economy trains villagers.</summary>
        private void DecideEconomy()
        {
            AdvanceReserved(); // every tick, not only on the allocation cycle
            if (AiFactoryOn) UpdateAiFactoryBarracksIdle();
            if (!EconomyOn || world.Tick % 20 != 0) return;
            var rules = world.Config.Economy;
            for (int f = 0; f < 2; f++)
            {
                uint faction = (uint)f + 1;
                if (world.Cores[world.Factions[f].CoreId - 1].Hp <= 0 || world.Economies[f].AutoOff) continue;
                automaticEconomyAction = true;
                try
                {
                    ref var economy = ref world.Economies[f];
                    int villagers = LivingVillagers(faction);
                    var plan = PlanOf(faction, world.Cores[world.Factions[f].CoreId - 1].Definition.Position);
                    MeasureSubphase("AI.DecideEconomy.DecideAdvance", DecideAdvance, faction);
                    bool tool = CanUseToolForVillager(faction);
                    bool shouldTrainVillager = LargeEconomyOn
                        ? ShouldTrainLargeVillagers(faction, villagers, plan.VillagerTarget)
                        : EconomyDecision.ShouldTrainVillager(villagers, economy.Queued, plan.VillagerTarget, economy.Food,
                            VillagerFoodCostFor(faction, tool), villagers + LivingSoldiers(faction) + QueuedInfantry(faction) + QueuedTownVillagers(faction), PopCapFor(faction), rules.QueueLimit);
                    if (!economy.CoreHeld && !HumanRegionAt(faction, world.Cores[world.Factions[f].CoreId - 1].Definition.Position) && economy.AdvanceRemaining == 0 && shouldTrainVillager)
                    {
                        if (LargeEconomyOn) EnqueueCoreVillagerBatch(faction);
                        else EnqueueCoreVillager(faction);
                    }
                    MeasureSubphase("AI.DecideEconomy.ResumeUnbuilt", ResumeUnbuilt, faction);
                    if (EarlyStartOn && OpenFarmsOn && economy.Age == 0 && !AgrarianFarmingAllowed(faction) && !HasFarm(faction))
                    {
                        // Reserve the first primitive construction decision for the farm. This also keeps a
                        // not-yet-affordable farm ahead of barracks, houses, and other primitive buildings.
                        MeasureSubphase("AI.DecideEconomy.EarlyStart.Farm", DecideFarms, faction);
                        // Only while saving the farm's wood: with the wood in hand and still no farm, there was no site,
                        // and the rest of the economy must not wait on it.
                        if (!HasFarm(faction) && economy.Wood < rules.FarmWoodCost) continue;
                    }
                    MeasureSubphase("AI.DecideEconomy.DecideHouse", DecideHouse, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideDropSite", DecideDropSite, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideTower", DecideTower, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideResearch", DecideResearch, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideMarket", DecideMarket, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideGoodsMarket", DecideGoodsMarket, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideCaravanserai", DecideCaravanserai, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideCaravanRoute", DecideCaravanRoute, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideTradeRoute", DecideTradeRoute, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideSiege", DecideSiege, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideCrossUnit", DecideCrossUnit, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideCastle", DecideCastle, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideCaravanMercenary", DecideCaravanMercenary, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideRepair", DecideRepair, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideBuildings", DecideBuildings, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideTownVillagers", DecideTownVillagers, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideIndustry", DecideIndustry, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideInserters", DecideInserters, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideTollgate", DecideTollgate, faction);
                    MeasureSubphase("AI.DecideEconomy.DecideCavalryStable", DecideCavalryStable, faction);
                }
                finally { automaticEconomyAction = false; }
            }
        }

        private bool ShouldTrainLargeVillagers(uint faction, int villagers, int target)
        {
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Queued != 0 || villagers >= target || economy.Food < VillagerFoodCostFor(faction, CanUseToolForVillager(faction))) return false;
            int batch = Math.Min(rules.LargeVillagerBatch, target - villagers);
            int population = villagers + LivingSoldiers(faction) + QueuedInfantry(faction) + QueuedTownVillagers(faction);
            int room = PopCapFor(faction) - population;
            return batch > 0 && batch <= rules.QueueLimit && batch <= room;
        }

        /// <summary>Movement phase, after the soldiers: idle villagers are given work, then every villager steps.</summary>
        private void MoveVillagers()
        {
            if (!EconomyOn) return;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                v.IsMoving = false;
                if (!v.Alive) continue;
                if (IsGoldWork(v) && (v.Task == VillagerTask.ToNode || v.Task == VillagerTask.Gathering) && !GoldTargetUsable(v))
                {
                    if (v.Carry > 0) v.Task = VillagerTask.ToDropOff;
                    else { v.Task = VillagerTask.Idle; v.NodeId = 0; ReleaseVillagerRoute(ref v); v.RouteCursor = 0; }
                }
                if (IsTradeRouteTask(v.Task) && !TradeRouteActive(v)) { StopTradeRoute(ref v); continue; }
                if (IsCaravanTask(v.Task))
                {
                    if (!CaravanMarketValid(v)) { EndCaravanAtCore(ref v); }
                    else if (v.Task == VillagerTask.ToCaravanserai && !CaravanPairValid(v))
                    {
                        v.CaravanStage = 3; v.Task = VillagerTask.ToCaravanMarket; ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position;
                    }
                    else
                    {
                        StopCaravanOutboundForDanger(ref v);
                        if (HoldCaravanAtMarket(ref v)) continue;
                    }
                }
                if (AiExpandWorkRouteTimedOut(ref v)) continue;
                if (v.Task == VillagerTask.Idle && !v.Held && !world.Economies[v.FactionId - 1].AutoOff && CanAiAssign(v)) AssignWork(ref v);
                if (AiArmyRecoveryOn && v.Task == VillagerTask.ToNode && !v.Held && !world.Economies[v.FactionId - 1].AutoOff)
                {
                    var recoveryKind = RecoveryWorkKindFor(v.FactionId);
                    var currentKind = world.Nodes[v.NodeId - 1].Definition.Kind;
                    if (recoveryKind != 0 && recoveryKind != currentKind)
                    {
                        int recoveryNode = NearestWorkNode(v.Position, recoveryKind, v.FactionId);
                        if (recoveryNode >= 0) SetWorkNode(ref v, recoveryNode);
                    }
                }
                if (v.Task == VillagerTask.ToDropOff && v.Carry > 0 && BeltDropOffHolds(ref v)) continue;
                SimPoint goal;
                if (v.Task == VillagerTask.ToNode) goal = world.Nodes[v.NodeId - 1].Definition.Position;
                else if (v.Task == VillagerTask.ToDropOff) goal = DropOff(v).point;
                else if (v.Task == VillagerTask.ToBuild) goal = world.Map.Center(world.Buildings[v.BuildingId - 1].WorkCell);
                else if (v.Task == VillagerTask.ToPickup) goal = world.Map.Center(world.Buildings[v.HaulFrom - 1].WorkCell);
                else if (v.Task == VillagerTask.ToDeliver) goal = world.Map.Center(world.Buildings[v.HaulTo - 1].WorkCell);
                else if (v.Task == VillagerTask.ToTradeMarket) goal = world.Map.Center(world.Buildings[v.BuildingId - 1].WorkCell);
                else if (v.Task == VillagerTask.ToTradeCore) goal = OwnCore(v.FactionId).Definition.Position;
                else if (v.Task == VillagerTask.ToCaravanMarket) goal = world.Map.Center(world.Buildings[v.CaravanMarketId - 1].WorkCell);
                else if (v.Task == VillagerTask.ToCaravanserai) goal = world.Map.Center(world.Buildings[v.CaravanseraiId - 1].WorkCell);
                else if (v.Task == VillagerTask.ToCaravanCore) goal = OwnCore(v.FactionId).Definition.Position;
                else { v.MoveGoal = v.Position; continue; }
                v.MoveGoal = VillagerRouteTarget(ref v, goal);
                var next = world.Map.ClipMove(v.Position, FixMath.MoveTowards(v.Position, v.MoveGoal, world.VillagerStep), v.FactionId);
                v.IsMoving = !SamePoint(v.Position, next);
                v.Position = next;
            }
        }

        /// <summary>
        /// Between deaths and captures (5.1 step 5.5): gather, drop off, and finish training. A new villager does not
        /// move on the tick it appears, like a reinforcement.
        /// </summary>
        private void EconomyStep()
        {
            if (!EconomyOn) return;
            var rules = world.Config.Economy;
            RefreshBeltDropOffDestinationSignature();
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceBuildingReservations", AdvanceBuildingReservations);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.RegrowFishing", RegrowFishing);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceBelts", AdvanceBelts);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceIndustry", AdvanceIndustry);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceInserters", AdvanceInserters);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.ApplyBrickUpgrades", ApplyBrickUpgrades);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.RepairMasonryCoresWithBricks", RepairMasonryCoresWithBricks);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.ApplyRationsHealing", ApplyRationsHealing);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.ApplyIncenseHealing", ApplyIncenseHealing);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.ApplyRepairKits", ApplyRepairKits);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceMountain", AdvanceMountain);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceByAgeClock", AdvanceByAgeClock);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceAges", AdvanceAges);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceResearch", AdvanceResearch);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceTollgateFees", AdvanceTollgateFees);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceSanctuaryPilgrimage", AdvanceSanctuaryPilgrimage);
            int count = world.VillagerCount; // villagers trained below start next tick
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.Villagers", AdvanceEconomyVillagers, (uint)count);
            MeasureSubphase("ObjectivesReinforcements.EconomyStep.AdvanceBuildings", AdvanceBuildings);
            for (int f = 0; f < 2; f++)
            {
                ref var economy = ref world.Economies[f];
                if (economy.Queued == 0) continue;
                if (economy.TrainRemaining > 0) economy.TrainRemaining--;
                if (economy.TrainRemaining > 0) continue;
                uint faction = (uint)f + 1;
                int productionBatch = LargeEconomyOn ? world.Config.Economy.LargeVillagerBatch : 1;
                for (int produced = 0; produced < productionBatch && economy.Queued > 0; produced++)
                {
                    // A full population holds the finished villager at the door until there is room.
                    if (LivingVillagers(faction) + LivingSoldiers(faction) >= PopCapFor(faction)) break;
                    bool stoneTool = QueueStoneToolsAt(economy, 0);
                    SpawnVillager(faction, OwnCore(faction).Definition.Position, stoneTool);
                    economy.Queued--;
                    if (economy.QueueTools != null && economy.QueueTools.Length > 0)
                    {
                        var rest = new bool[economy.QueueTools.Length - 1];
                        Array.Copy(economy.QueueTools, 1, rest, 0, rest.Length); economy.QueueTools = rest;
                    }
                    if (economy.QueueStoneTools != null && economy.QueueStoneTools.Length > 0)
                    {
                        var rest = new bool[economy.QueueStoneTools.Length - 1];
                        Array.Copy(economy.QueueStoneTools, 1, rest, 0, rest.Length); economy.QueueStoneTools = rest;
                    }
                    economy.TrainRemaining = economy.Queued > 0 ? VillagerTrainTicksFor(faction, QueueToolsAt(economy, 0)) : 0;
                }
            }
        }

        private void AdvanceEconomyVillagers(uint count)
        {
            for (int i = 0; i < count; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive) continue;
                if (IsTradeRouteTask(v.Task)) AdvanceTradeRoute(ref v);
                else if (IsCaravanTask(v.Task)) AdvanceCaravanRoute(ref v);
                else if (v.Task == VillagerTask.ToNode)
                {
                    var node = world.Nodes[v.NodeId - 1];
                    if (node.Remaining <= 0)
                    {
                        if (v.Carry == 0 && ProcessingKilnTarget(v) && AssignKilnWood(ref v, v.HaulTo)) continue;
                        if (v.Carry == 0 && ProcessingKilnTarget(v)) v.HaulTo = 0;
                        v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
                        v.BeltWaitUntilTick = 0;
                        continue;
                    }
                    if (InRange(v.Position, node.Definition.Position, GatherReach))
                    {
                        v.Task = VillagerTask.Gathering;
                        v.NextGatherTick = checked(world.Tick + GatherTicksFor(v.FactionId, v.NodeId));
                    }
                }
                else if (v.Task == VillagerTask.Gathering)
                {
                    ref var node = ref world.Nodes[v.NodeId - 1];
                    if (node.Remaining <= 0)
                    {
                        if (v.Carry == 0 && ProcessingKilnTarget(v) && AssignKilnWood(ref v, v.HaulTo)) continue;
                        if (v.Carry == 0 && ProcessingKilnTarget(v)) v.HaulTo = 0;
                        v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
                        if (v.Task == VillagerTask.ToDropOff) v.BeltWaitUntilTick = 0;
                        continue;
                    }
                    // While the villager is waiting for its next gathering tick, move at most one already-carried
                    // item onto an open nearby belt. The gathering timer itself is unchanged.
                    if (v.Carry > 0 && BeltDropOffOn && world.Tick < v.NextGatherTick)
                        TryPlaceCarriedOnBelt(ref v);
                    if (world.Tick < v.NextGatherTick) continue;
                    node.Remaining--;
                    v.CarryKind = node.Definition.Kind;
                    if (!TryPlaceGatheredOnBelt(ref v, v.CarryKind)) v.Carry++;
                    v.NextGatherTick = checked(world.Tick + GatherTicksFor(v.FactionId, v.NodeId));
                    if (v.Carry >= CarryFor(v.FactionId, node.Fishing) || node.Remaining == 0)
                    {
                        v.Task = VillagerTask.ToDropOff;
                        v.BeltWaitUntilTick = 0;
                    }
                }
                else if (v.Task == VillagerTask.ToDropOff)
                {
                    if (BeltDropOffOn && v.Carry <= 0)
                    {
                        FinishDropOff(ref v);
                        continue;
                    }
                    if (v.Carry > 0 && BeltDropOffOn && BeltDropOffHolds(ref v))
                    {
                        int beltCell = FindBeltDropOffCell(v);
                        if (beltCell >= 0)
                        {
                            ref var belt = ref world.Belts[beltCell];
                            if (belt.Item == 0)
                            {
                                belt.Item = v.CarryKind;
                                belt.Progress = 0;
                                v.Carry--;
                                RecordLargeBeltDrop(v.FactionId, v.CarryKind, 1);
                                v.BeltWaitUntilTick = 0;
                                if (v.Carry > 0) continue;
                                FinishDropOff(ref v);
                                continue;
                            }
                        }
                        continue;
                    }
                    var drop = DropOff(v);
                    if (!InRange(v.Position, drop.point, drop.reach)) continue;
                    if (ProcessingKilnTarget(v))
                    {
                        int delivered = v.Carry;
                        if (!TryDeliverWoodToKiln(ref v)) AddStock(v.FactionId, v.CarryKind, v.Carry);
                        RecordLargeDelivery(v, v.CarryKind, delivered - v.Carry);
                        if (v.Carry > 0) continue; // a full kiln is a deliberate hand-haul wait state
                        if (v.HaulTo != 0 && AssignKilnWood(ref v, v.HaulTo)) continue;
                        v.HaulTo = 0; v.Task = VillagerTask.Idle;
                        continue;
                    }
                    if (drop.storageId != 0)
                    {
                        if (drop.storageId > world.BuildingCount) continue;
                        ref var storage = ref world.Buildings[drop.storageId - 1];
                        int stored = PutIntoStorage(ref storage, v.CarryKind, v.Carry);
                        RecordLargeDelivery(v, v.CarryKind, stored);
                        v.Carry -= stored;
                        if (v.Carry > 0) continue;
                    }
                    else
                    {
                        int delivered = v.Carry;
                        AddStock(v.FactionId, v.CarryKind, v.Carry);
                        RecordLargeDelivery(v, v.CarryKind, delivered);
                        v.Carry = 0;
                    }
                    FinishDropOff(ref v);
                }
                else if (v.Task == VillagerTask.ToPickup || v.Task == VillagerTask.ToDeliver) Haul(ref v);
            }
        }

        /// <summary>Fishing is finite: at each configured multiple, one unit returns up to the original amount.</summary>
        private void RegrowFishing()
        {
            var rules = world.Config.Economy;
            if (!rules.FishingEnabled) return;
            foreach (uint id in world.FishingNodeIds)
            {
                ref var node = ref world.Nodes[id - 1];
                int interval = rules.FishRegrowTicks;
                for (uint faction = 1; faction <= 2; faction++)
                    if (FishingAllowed(faction) && HarborCoversFish(faction, node.Definition.Position))
                    {
                        int coveredInterval = HasTech(faction, FishingTech.DriedFish)
                            ? Math.Max(1, checked(rules.FishRegrowTicks * rules.DriedFishRegrowIntervalPermille / 1000))
                            : Math.Max(1, rules.FishRegrowTicks / 2);
                        interval = Math.Min(interval, coveredInterval);
                    }
                if (world.Tick % interval != 0) continue;
                if (node.Remaining < node.Definition.Amount) node.Remaining++;
            }
        }

        private void AssignWork(ref VillagerState v)
        {
            if (!CanAiAssign(v)) return;
            int fishing = FishingWorkNode(v.Position, v.FactionId);
            if (fishing >= 0) { SetWorkNode(ref v, fishing); return; }
            var kind = WorkKindFor(v);
            int index = NearestWorkNode(v.Position, kind, v.FactionId);
            if (index < 0) index = NearestWorkNode(v.Position, kind == ResourceKind.Food ? ResourceKind.Wood : ResourceKind.Food, v.FactionId);
            if (index < 0) return; // nothing left anywhere: stays idle
            SetWorkNode(ref v, index);
        }

        private bool TryPlaceGatheredOnBelt(ref VillagerState villager, ResourceKind kind)
        {
            if (!BeltDropOffOn) return false;
            villager.CarryKind = kind;
            int cell = FindBeltDropOffCell(villager);
            if (cell < 0 || world.Belts[cell].Item != 0) return false;
            world.Belts[cell].Item = kind;
            world.Belts[cell].Progress = 0;
            RecordLargeBeltDrop(villager.FactionId, kind, 1);
            return true;
        }

        private bool TryPlaceCarriedOnBelt(ref VillagerState villager)
        {
            if (!BeltDropOffOn || villager.Carry <= 0) return false;
            int cell = FindBeltDropOffCell(villager);
            if (cell < 0 || world.Belts[cell].Item != 0) return false;
            ref var belt = ref world.Belts[cell];
            belt.Item = villager.CarryKind;
            belt.Progress = 0;
            villager.Carry--;
            RecordLargeBeltDrop(villager.FactionId, villager.CarryKind, 1);
            return true;
        }

        private bool BeltDropOffHolds(ref VillagerState villager)
        {
            if (!BeltDropOffOn || villager.Carry <= 0 || villager.BeltWaitUntilTick < 0) return false;
            int cell = FindBeltDropOffCell(villager);
            if (cell < 0) { villager.BeltWaitUntilTick = 0; return false; }
            if (world.Belts[cell].Item == 0) return true;
            if (villager.BeltWaitUntilTick == 0)
                villager.BeltWaitUntilTick = checked(world.Tick + world.Config.Economy.BeltDropOffWaitTicks);
            if (world.Tick < villager.BeltWaitUntilTick) return true;
            villager.BeltWaitUntilTick = -1;
            return false;
        }

        private void FinishDropOff(ref VillagerState v)
        {
            v.BeltWaitUntilTick = 0;
            if (v.CaravanMarketId != 0 && v.CaravanseraiId != 0 && v.CaravanStage == 1)
            {
                v.Task = VillagerTask.ToCaravanMarket;
                v.NodeId = 0; ReleaseVillagerRoute(ref v); v.RouteCursor = 0; v.RouteGoal = v.Position;
                return;
            }
            if (v.HaulFrom != 0) { v.Task = VillagerTask.ToPickup; return; }
            if (AgesOn && !v.Held && !world.Economies[v.FactionId - 1].AutoOff && v.NodeId != 0
                && world.Nodes[v.NodeId - 1].Definition.Kind != ResourceKind.Stone)
            {
                var currentKind = world.Nodes[v.NodeId - 1].Definition.Kind;
                int fishing = FishingWorkNode(v.Position, v.FactionId);
                if (fishing >= 0) { SetWorkNode(ref v, fishing); return; }
                var kind = WorkKindFor(v);
                // Only between food and wood: who goes to stone stays with StoneWanted, as for the idle.
                if (kind != currentKind && kind != ResourceKind.Stone)
                {
                    int index = NearestWorkNode(v.Position, kind, v.FactionId);
                    if (index >= 0)
                    {
                        SetWorkNode(ref v, index);
                        return;
                    }
                }
            }
            v.Task = v.NodeId != 0 && world.Nodes[v.NodeId - 1].Remaining > 0 ? VillagerTask.ToNode : VillagerTask.Idle;
        }

        private bool ProcessingKilnTarget(VillagerState v)
            => ProcessingOn && (v.Carry == 0 || v.CarryKind == ResourceKind.Wood) && v.HaulTo > 0 && v.HaulTo <= world.BuildingCount
               && IsActualBuilding(world.Buildings[v.HaulTo - 1]) && world.Buildings[v.HaulTo - 1].Kind == BuildingKind.CharcoalKiln;

        private bool AssignKilnWood(ref VillagerState v, uint kilnId)
        {
            if (!ProcessingOn || kilnId == 0 || kilnId > world.BuildingCount) return false;
            var kiln = world.Buildings[kilnId - 1];
            if (!IsActualBuilding(kiln) || !kiln.Complete || kiln.FactionId != v.FactionId) return false;
            int index = NearestWorkNode(v.Position, ResourceKind.Wood, v.FactionId);
            if (index < 0) return false;
            v.HaulFrom = 0; v.HaulTo = kilnId; v.HaulNodeId = 0;
            v.NodeId = world.Nodes[index].Definition.Id;
            if (AiExpandOn) v.AiExpandWorkStartTick = world.Tick;
            v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.ToNode;
            return true;
        }

        private bool TryDeliverWoodToKiln(ref VillagerState v)
        {
            if (!ProcessingKilnTarget(v)) return false;
            ref var kiln = ref world.Buildings[v.HaulTo - 1];
            if (!IsActualBuilding(kiln) || !kiln.Complete || BuildingPaused(kiln))
            {
                v.HaulTo = 0;
                AddStock(v.FactionId, v.CarryKind, v.Carry);
                v.Carry = 0;
                return true;
            }
            int put = Math.Min(v.Carry, world.Config.Economy.BufferLimit - kiln.Input);
            kiln.Input += put;
            v.Carry -= put;
            return true;
        }

        private ResourceKind WorkKindFor(VillagerState v)
        {
            if (LargeEconomyOn) return LargeWorkKindFor(v);
            int food = 0, wood = 0;
            int stone = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var other = world.Villagers[i];
                if (!other.Alive || other.Id == v.Id || other.FactionId != v.FactionId || other.Task == VillagerTask.Idle || other.NodeId == 0
                    || other.Task == VillagerTask.ToBuild || other.Task == VillagerTask.Building) continue;
                var otherKind = world.Nodes[other.NodeId - 1].Definition.Kind;
                if (otherKind == ResourceKind.Food) food++; else if (otherKind == ResourceKind.Wood) wood++; else if (otherKind == ResourceKind.Stone) stone++;
            }
            if (PrimitiveCraftOn && world.Economies[v.FactionId - 1].Age == 0)
            {
                var stock = world.Economies[v.FactionId - 1];
                var rules = world.Config.Economy;
                if (wood == 0 || stock.Wood < rules.HutWoodCost + rules.SawmillWoodCost) return ResourceKind.Wood;
                if (stone == 0 || stock.Stone < rules.StoneToolsStoneInput) return ResourceKind.Stone;
            }
            var recoveryKind = RecoveryWorkKindFor(v.FactionId);
            if (recoveryKind != 0) return recoveryKind;
            ResourceKind current = v.NodeId == 0 ? 0 : world.Nodes[v.NodeId - 1].Definition.Kind;
            if (GoldNeeded(v.FactionId) > 0 && GoldGathererRoom(v, food, wood)) return ResourceKind.Gold;
            var plan = PlanOf(v.FactionId, v.Position);
            int foodPerWood = EconomyDecision.FoodPerWood(EconomyScaleOn, plan.FoodPerWood);
            if (CraftTimingOn && CraftTimingNeedsWood(v.FactionId))
                foodPerWood = world.Config.Economy.CraftTimingFoodPerWood;
            var kind = StoneWanted(v.FactionId) ? ResourceKind.Stone : EconomyDecision.KindToGather(food, wood, foodPerWood);
            // V3-5 (32.7): on a map with ages the stock speaks too - far more of one than the other sends the idle to the other.
            if (AgesOn && kind != ResourceKind.Stone)
            {
                var stock = world.Economies[v.FactionId - 1];
                if (stock.Food >= stock.Wood + StockGap) kind = ResourceKind.Wood;
                else if (stock.Wood >= stock.Food + StockGap) kind = ResourceKind.Food;
                // V3-5 (32.11): wood runs out long before food, and everything new is priced in wood, so an empty wood
                // store sends the idle to the trees even when the two stocks are close.
                else if (stock.Wood < WoodFloor && stock.Wood < stock.Food) kind = ResourceKind.Wood;
            }
            return kind;
        }

        private ResourceKind LargeWorkKindFor(VillagerState v)
        {
            RefreshLargeWorkCounts();
            int factionIndex = checked((int)v.FactionId - 1);
            ref var economy = ref world.Economies[factionIndex];
            int food = largeWorkCounts[factionIndex * 6 + 0];
            int wood = largeWorkCounts[factionIndex * 6 + 1];
            int stone = largeWorkCounts[factionIndex * 6 + 2];
            int villagers = largeVillagerCounts[factionIndex];
            if (AiExpandOn && villagers >= 5 && economy.Age == 0
                && food * 1000 < villagers * world.Config.Economy.AiExpandPrimitiveFoodWorkerPermille)
                return ResourceKind.Food;
            if (PrimitiveCraftOn && economy.Age == 0)
            {
                if (wood == 0 || economy.Wood < world.Config.Economy.HutWoodCost + world.Config.Economy.SawmillWoodCost) return ResourceKind.Wood;
                if (stone == 0 || economy.Stone < world.Config.Economy.StoneToolsStoneInput) return ResourceKind.Stone;
            }
            if (AiExpandOn && economy.Age >= 1 && AiExpandTownNeedsFunding(v.FactionId)
                && economy.Stone < TownStoneCostFor(v.FactionId)
                && stone * 1000 < villagers * world.Config.Economy.AiExpandTownStoneWorkerPermille)
                return ResourceKind.Stone;
            if (AiExpandOn && villagers >= world.Config.Economy.AiExpandShareMinVillagers)
            {
                if (food * 1000 < villagers * world.Config.Economy.AiExpandFoodWorkerPermille) return ResourceKind.Food;
                if (CraftRequiredOn && economy.Age >= 2
                    && wood * 1000 < villagers * (world.Config.Economy.AiExpandWoodWorkerPermille
                        + world.Config.Economy.CraftRequiredAiWoodWorkerPermille)) return ResourceKind.Wood;
                if (wood * 1000 < villagers * world.Config.Economy.AiExpandWoodWorkerPermille) return ResourceKind.Wood;
                if (economy.Age >= 1 && stone * 1000 < villagers * world.Config.Economy.AiExpandStoneWorkerPermille) return ResourceKind.Stone;
            }
            int townVillagers = world.Config.Economy.LargeTownVillagersPerTown;
            int desiredTowns = villagers < townVillagers ? 0
                : Math.Min(world.Config.Economy.TownMaxBuildings, Math.Max(1, villagers / townVillagers));
            int townVillagerThreshold = world.Config.Economy.LargeTownVillagersPerTown;
            // A town is ahead of optional construction, but not ahead of the food floor that keeps the
            // large-economy villager line alive.
            if (economy.Food < world.Config.Economy.VillagerFoodCost * 8) return ResourceKind.Food;
            if (AiExpandTownNeedsFunding(v.FactionId))
            {
                if (economy.Wood < TownWoodCostFor(v.FactionId)) return ResourceKind.Wood;
                if (economy.Stone < TownStoneCostFor(v.FactionId)) return ResourceKind.Stone;
            }
            bool townNeedsStone = largeTownCounts[factionIndex] < desiredTowns
                && economy.Stone < TownStoneCostFor(v.FactionId);
            int barracksVillagers = world.Config.Economy.LargeBarracksVillagersPerBuilding;
            bool barracksNeedsWood = largeBarracksCounts[factionIndex] < Math.Min(world.Config.Economy.LargeBarracksMaxBuildings,
                Math.Max(1, villagers / barracksVillagers)) && economy.Wood < world.Config.Economy.BarracksWoodCost;
            // Keep the next ordinary-cost building payable before balancing the long-run food line. Food has priority
            // when the next villager batch is not covered; the reserve is a scheduling threshold only, and the
            // authoritative prices remain in EconomyRules' ordinary fields.
            if (economy.Food < world.Config.Economy.VillagerFoodCost * 8) return ResourceKind.Food;
            // A remote stone cluster is intentionally far away. Start that line once a small food floor is staffed;
            // waiting for the global food buffer to be full leaves no worker able to fund the first town.
            int stoneTarget = villagers < townVillagerThreshold
                ? Math.Max(1, world.Config.Economy.LargeTownStoneGatherers / 3)
                : world.Config.Economy.LargeTownStoneGatherers;
            if (townNeedsStone && food >= stoneTarget * 2 && stone < stoneTarget) return ResourceKind.Stone;
            // After the 300-villager line is filled, the next useful income is the infantry line. Keep a bounded
            // reserve for several ordinary-cost batches and name the missing input explicitly, including ore for
            // metal. This is a scheduling choice only; unit prices and training work remain unchanged.
            if (villagers >= AutoVillagerTargetFor(v.FactionId))
            {
                var infantryInput = LargeInfantryInputKindFor(v.FactionId);
                if (infantryInput != 0) return infantryInput;
            }
            if (economy.Wood < world.Config.Economy.LargeConstructionReserveWood || barracksNeedsWood) return ResourceKind.Wood;
            if (economy.Food < world.Config.Economy.VillagerFoodCost * 4) return ResourceKind.Food;
            return EconomyDecision.KindToGather(food, wood, world.Config.Economy.LargeFoodPerWood);
        }

        private ResourceKind LargeInfantryInputKindFor(uint faction)
        {
            if (!CompleteBarracks(faction)) return 0;
            ref var economy = ref world.Economies[faction - 1];
            int reserve = world.Config.Economy.LargeInfantryResourceReserveUnits;
            int foodNeed = checked(InfantryFoodFor(faction) * reserve);
            int woodNeed = checked(InfantryWoodFor(faction) * reserve);
            int metalNeed = checked(InfantryMetalFor(faction) * reserve);
            if (metalNeed > 0 && economy.Metal < metalNeed) return ResourceKind.Ore;
            if (foodNeed > 0 && economy.Food < foodNeed) return ResourceKind.Food;
            if (woodNeed > 0 && economy.Wood < woodNeed) return ResourceKind.Wood;
            return 0;
        }

        private void RefreshLargeWorkCounts()
        {
            if (!LargeEconomyOn || largeWorkCountsTick == world.Tick) return;
            Array.Clear(largeWorkCounts, 0, largeWorkCounts.Length);
            Array.Clear(largeVillagerCounts, 0, largeVillagerCounts.Length);
            Array.Clear(largeTownCounts, 0, largeTownCounts.Length);
            Array.Clear(largeBarracksCounts, 0, largeBarracksCounts.Length);
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (!v.Alive || v.FactionId < 1 || v.FactionId > 2) continue;
                int factionIndex = (int)v.FactionId - 1;
                largeVillagerCounts[factionIndex]++;
                if (v.NodeId == 0 || v.NodeId > world.Nodes.Length
                    || (v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering && v.Task != VillagerTask.ToDropOff)) continue;
                int resource = LargeWorkResourceIndex(world.Nodes[v.NodeId - 1].Definition.Kind);
                if (resource >= 0) largeWorkCounts[factionIndex * 6 + resource]++;
            }
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId < 1 || b.FactionId > 2) continue;
                int factionIndex = (int)b.FactionId - 1;
                if (b.Kind == BuildingKind.Town) largeTownCounts[factionIndex]++;
                else if (b.Kind == BuildingKind.Barracks) largeBarracksCounts[factionIndex]++;
            }
            largeWorkCountsTick = world.Tick;
        }

        private static int LargeWorkResourceIndex(ResourceKind kind)
            => kind == ResourceKind.Food ? 0 : kind == ResourceKind.Wood ? 1 : kind == ResourceKind.Stone ? 2
                : kind == ResourceKind.Ore ? 3 : kind == ResourceKind.Gold ? 4 : kind == ResourceKind.Metal ? 5 : -1;

        private bool AiArmyRecoveryOn => EconomyOn && world.Config.Economy.AiArmyRecovery;
        private bool LineBuildingCheckOn => EconomyOn && world.Config.Economy.LineBuildingCheck;

        private ResourceKind RecoveryWorkKindFor(uint faction)
        {
            if (!ArmyRecoveryNeedsInfantry(faction)) return 0;
            ref var economy = ref world.Economies[faction - 1];
            int foodNeed = Math.Max(0, InfantryFoodFor(faction) - economy.Food);
            int woodNeed = Math.Max(0, InfantryWoodFor(faction) - economy.Wood);
            int metalNeed = Math.Max(0, InfantryMetalFor(faction) - economy.Metal);
            if (metalNeed > 0 && OwnSmelter(faction) != 0) return ResourceKind.Ore;
            if (foodNeed == 0 && woodNeed == 0) return 0;
            if (woodNeed == 0) return ResourceKind.Food;
            if (foodNeed == 0) return ResourceKind.Wood;
            long foodPriority = checked((long)foodNeed * InfantryWoodFor(faction));
            long woodPriority = checked((long)woodNeed * InfantryFoodFor(faction));
            return foodPriority >= woodPriority ? ResourceKind.Food : ResourceKind.Wood;
        }

        private bool ArmyRecoveryNeedsInfantry(uint faction)
            => AiArmyRecoveryOn && LivingClass(faction, UnitKind.Infantry) + QueuedInfantry(faction) < ArmyRecoveryMinimumInfantry
                && world.Economies[faction - 1].Age >= 3 && CompleteBarracks(faction)
                && HasInfantryRoom(faction) && !SavingToAdvance(faction);

        private bool ArmyRecoveryWouldConsumeReserve(uint faction, ResourceKind kind, int amount)
        {
            if (!ArmyRecoveryNeedsInfantry(faction)) return false;
            ref var economy = ref world.Economies[faction - 1];
            int reserve = kind == ResourceKind.Food ? InfantryFoodFor(faction) : kind == ResourceKind.Wood ? InfantryWoodFor(faction)
                : kind == ResourceKind.Metal ? InfantryMetalFor(faction) : 0;
            int stock = kind == ResourceKind.Food ? economy.Food : kind == ResourceKind.Wood ? economy.Wood
                : kind == ResourceKind.Metal ? economy.Metal : int.MaxValue;
            return reserve > 0 && checked(stock - amount) < reserve;
        }

        private int NearestWorkNode(SimPoint position, ResourceKind kind, uint faction)
        {
            if (kind == ResourceKind.Gold && !LargeEconomyOn) return NearestGoldNode(position, faction);
            if (LargeEconomyOn) return NearestLargeWorkNode(position, kind, faction);
            if (ResourceFogOn)
            {
                int best = -1;
                long bestDistance = 0;
                for (int i = 0; i < world.Nodes.Length; i++)
                {
                    var node = world.Nodes[i];
                    if (!ResourceKnownToFaction(faction, i) || node.Definition.Kind != kind || node.Remaining <= 0) continue;
                    long dx = node.Definition.Position.X.Raw - position.X.Raw;
                    long dz = node.Definition.Position.Z.Raw - position.Z.Raw;
                    long distance = checked(dx * dx + dz * dz);
                    if (best < 0 || distance < bestDistance || distance == bestDistance && node.Definition.Id < world.Nodes[best].Definition.Id)
                    { best = i; bestDistance = distance; }
                }
                return best;
            }
            int n = world.Nodes.Length;
            var positions = new SimPoint[n];
            var kinds = new ResourceKind[n];
            var remaining = new int[n];
            for (int i = 0; i < n; i++) { positions[i] = world.Nodes[i].Definition.Position; kinds[i] = world.Nodes[i].Definition.Kind; remaining[i] = world.Nodes[i].Remaining; }
            return EconomyDecision.NearestNode(position, positions, kinds, remaining, kind);
        }

        private void EnsureLargeResourceClusters()
        {
            if (!LargeEconomyOn || largeNodeClusters != null) return;
            int count = world.Nodes.Length;
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            var radius = Fix64.FromInt(world.Config.Economy.LargeResourceClusterRadiusMeters);
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    if (world.Nodes[i].Definition.Kind != world.Nodes[j].Definition.Kind
                        || !InRange(world.Nodes[i].Definition.Position, world.Nodes[j].Definition.Position, radius)) continue;
                    int left = FindClusterRoot(parent, i), right = FindClusterRoot(parent, j);
                    if (left != right) parent[Math.Max(left, right)] = Math.Min(left, right);
                }
            var rootToCluster = new int[count]; Array.Fill(rootToCluster, -1);
            var sizes = new int[count]; int clusterCount = 0;
            largeNodeClusters = new int[count];
            for (int i = 0; i < count; i++)
            {
                int root = FindClusterRoot(parent, i);
                int cluster = rootToCluster[root];
                if (cluster < 0) { cluster = clusterCount++; rootToCluster[root] = cluster; }
                largeNodeClusters[i] = cluster;
                sizes[cluster]++;
            }
            largeClusterNodes = new int[clusterCount][];
            largeClusterKinds = new ResourceKind[clusterCount];
            largeClusterAnchors = new int[clusterCount];
            for (int cluster = 0; cluster < clusterCount; cluster++) largeClusterNodes[cluster] = new int[sizes[cluster]];
            Array.Clear(sizes, 0, sizes.Length);
            for (int i = 0; i < count; i++)
            {
                int cluster = largeNodeClusters[i];
                largeClusterNodes[cluster][sizes[cluster]++] = i;
                largeClusterKinds[cluster] = world.Nodes[i].Definition.Kind;
                if (largeClusterNodes[cluster][0] == i) largeClusterAnchors[cluster] = i;
            }
        }

        private static int FindClusterRoot(int[] parent, int value)
        {
            int root = value;
            while (parent[root] != root) root = parent[root];
            while (parent[value] != value)
            {
                int next = parent[value]; parent[value] = root; value = next;
            }
            return root;
        }

        private int NearestLargeWorkNode(SimPoint position, ResourceKind kind, uint faction)
        {
            EnsureLargeResourceClusters();
            int bestCluster = -1;
            BigInteger bestDropDistance = default;
            uint bestAnchorId = 0;
            for (int cluster = 0; cluster < largeClusterNodes.Length; cluster++)
            {
                if (largeClusterKinds[cluster] != kind) continue;
                int knownNode = FirstKnownRemainingNode(faction, cluster);
                if (knownNode < 0) continue;
                var anchor = world.Nodes[knownNode].Definition.Position;
                var probe = new VillagerState { FactionId = faction, Position = anchor, CarryKind = kind, Task = VillagerTask.ToDropOff };
                var drop = DropOff(probe).point;
                if (AiExpandOn && (!AiExpandWorkRouteAvailable(position, anchor, faction)
                    || !AiExpandWorkRouteAvailable(anchor, drop, faction))) continue;
                var distance = DistanceSquared(anchor, drop);
                uint anchorId = world.Nodes[knownNode].Definition.Id;
                if (bestCluster < 0 || distance < bestDropDistance || distance == bestDropDistance && anchorId < bestAnchorId)
                { bestCluster = cluster; bestDropDistance = distance; bestAnchorId = anchorId; }
            }
            if (bestCluster < 0) return -1;
            int best = -1; BigInteger bestDistance = default; uint bestId = 0;
            foreach (int nodeIndex in largeClusterNodes[bestCluster])
            {
                var node = world.Nodes[nodeIndex];
                if (!ResourceKnownToFaction(faction, nodeIndex) || node.Remaining <= 0) continue;
                if (AiExpandOn)
                {
                    if (!AiExpandWorkRouteAvailable(position, node.Definition.Position, faction)) continue;
                }
                var distance = DistanceSquared(position, node.Definition.Position);
                if (best < 0 || distance < bestDistance || distance == bestDistance && node.Definition.Id < bestId)
                { best = nodeIndex; bestDistance = distance; bestId = node.Definition.Id; }
            }
            return best;
        }

        private bool AiExpandWorkRouteAvailable(SimPoint from, SimPoint to, uint faction)
        {
            if (!AiExpandOn) return true;
            if (Clear(from, to, faction)) return true;
            return AiExpandWorkRouteAvailable(world.Map.Cell(from), world.Map.Cell(to), faction);
        }

        private bool AiExpandWorkRouteAvailable(int originCell, int resourceCell, uint faction)
        {
            if (!AiExpandOn) return true;
            if (originCell < 0 || resourceCell < 0) return false;
            int version = world.Map.PassableVersion;
            if (aiExpandWorkRouteCacheVersion != version)
            {
                aiExpandWorkRouteCache.Clear();
                aiExpandWorkRouteCacheOrder.Clear();
                aiExpandWorkRouteCacheVersion = version;
            }
            var key = (Faction: faction, Origin: originCell, Resource: resourceCell);
            if (aiExpandWorkRouteCache.TryGetValue(key, out bool available)) return available;
            available = originCell == resourceCell
                || world.Map.SharedRoute(originCell, world.Map.Center(resourceCell), faction).Length > 1;
            if (aiExpandWorkRouteCache.Count == AiExpandWorkRouteCacheLimit)
            {
                var oldest = aiExpandWorkRouteCacheOrder.Dequeue();
                aiExpandWorkRouteCache.Remove(oldest);
            }
            aiExpandWorkRouteCache.Add(key, available);
            aiExpandWorkRouteCacheOrder.Enqueue(key);
            return available;
        }

        /// <summary>Test-only route-cache probe; the shipped AI calls the same cell-keyed path.</summary>
        internal bool ProbeAiExpandWorkRouteForTest(uint faction, int originCell, int resourceCell)
            => AiExpandWorkRouteAvailable(originCell, resourceCell, faction);

        /// <summary>Test-only terrain mutation used to verify PassableVersion invalidation.</summary>
        internal void SetPassableForTest(int cell, bool value)
        {
            world.Map.SetPassable(cell, value);
            TerrainChanged();
        }

        private bool AiExpandWorkRouteTimedOut(ref VillagerState villager)
        {
            if (!AiExpandOn || villager.Task != VillagerTask.ToNode || villager.NodeId == 0 || villager.NodeId > world.Nodes.Length
                || villager.AiExpandWorkStartTick < 0 || world.Tick - villager.AiExpandWorkStartTick < world.Config.Economy.AiExpandUnreachableWorkTimeoutTicks) return false;
            var node = world.Nodes[villager.NodeId - 1];
            if (AiExpandWorkRouteAvailable(villager.Position, node.Definition.Position, villager.FactionId)) return false;
            villager.Task = VillagerTask.Idle; villager.NodeId = 0; ReleaseVillagerRoute(ref villager); villager.RouteCursor = 0;
            villager.RouteGoal = villager.Position; villager.AiExpandWorkStartTick = -1;
            return true;
        }

        private int ClusterRemaining(int cluster)
        {
            int remaining = 0;
            foreach (int nodeIndex in largeClusterNodes[cluster])
                remaining = checked(remaining + Math.Max(0, world.Nodes[nodeIndex].Remaining));
            return remaining;
        }

        private int FirstKnownRemainingNode(uint faction, int cluster)
        {
            foreach (int nodeIndex in largeClusterNodes[cluster])
                if (ResourceKnownToFaction(faction, nodeIndex) && world.Nodes[nodeIndex].Remaining > 0) return nodeIndex;
            return -1;
        }

        private int NearestGoldNode(SimPoint position, uint faction)
        {
            int best = -1; long bestDistance = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (!ResourceKnownToFaction(faction, i) || node.Definition.Kind != ResourceKind.Gold || node.Remaining <= 0) continue;
                if (world.Map.SharedRoute(world.Map.Cell(position), node.Definition.Position, faction).Length == 0) continue;
                long dx = node.Definition.Position.X.Raw - position.X.Raw, dz = node.Definition.Position.Z.Raw - position.Z.Raw;
                long distance = checked(dx * dx + dz * dz);
                if (best < 0 || distance < bestDistance || distance == bestDistance && node.Definition.Id < world.Nodes[best].Definition.Id)
                { best = i; bestDistance = distance; }
            }
            return best;
        }

        private bool IsGoldWork(VillagerState v) => world.Config.Economy.GoldEnabled && v.NodeId > 0
            && v.NodeId <= world.Nodes.Length && world.Nodes[v.NodeId - 1].Definition.Kind == ResourceKind.Gold;

        private bool GoldTargetUsable(VillagerState v)
            => GoldTargetUsable(v.FactionId, v.NodeId, world.Map.Cell(v.Position));

        private bool GoldTargetUsable(uint factionId, uint nodeId, int routeStartCell)
        {
            if (nodeId == 0 || nodeId > world.Nodes.Length) return false;
            var node = world.Nodes[nodeId - 1];
            if (node.Remaining <= 0 || world.Map.SharedRoute(routeStartCell, node.Definition.Position, factionId).Length == 0) return false;
            int danger = world.Config.Economy.GoldDangerMeters;
            long limit = checked(Fix64.FromInt(danger).Raw * Fix64.FromInt(danger).Raw);
            var faction = world.Factions[factionId - 1];
            foreach (int i in world.SoldierTraversal)
            {
                var enemy = world.Soldiers[i];
                if (!enemy.Alive || enemy.Initial.FactionId == factionId) continue;
                int cell = world.Map.Cell(enemy.Position);
                if (cell < 0 || !faction.VisibleCells[cell]) continue;
                long dx = enemy.Position.X.Raw - node.Definition.Position.X.Raw, dz = enemy.Position.Z.Raw - node.Definition.Position.Z.Raw;
                if (checked(dx * dx + dz * dz) <= limit) return false;
            }
            return true;
        }

        /// <summary>
        /// Academy only: is there a gold point with a route from the core and no observed enemy near it? Used to stop
        /// the academy's gold research when the gold line is lost; the shared gold-gathering rules do not use it.
        /// </summary>
        private bool HasUsableGoldSource(uint faction, uint nodeId = 0)
        {
            var core = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
            int danger = world.Config.Economy.GoldDangerMeters;
            long limit = checked(Fix64.FromInt(danger).Raw * Fix64.FromInt(danger).Raw);
            for (int n = 0; n < world.Nodes.Length; n++)
            {
                var node = world.Nodes[n];
                if (!ResourceKnownToFaction(faction, n) || node.Definition.Kind != ResourceKind.Gold || node.Remaining <= 0 || nodeId != 0 && node.Definition.Id != nodeId) continue;
                if (world.Map.SharedRoute(world.Map.Cell(core), node.Definition.Position, faction).Length == 0) continue;
                bool dangerFound = false;
                foreach (int i in world.SoldierTraversal)
                {
                    var enemy = world.Soldiers[i];
                    if (!enemy.Alive || enemy.Initial.FactionId == faction) continue;
                    int cell = world.Map.Cell(enemy.Position);
                    if (cell < 0 || !world.Factions[faction - 1].VisibleCells[cell]) continue;
                    long dx = enemy.Position.X.Raw - node.Definition.Position.X.Raw, dz = enemy.Position.Z.Raw - node.Definition.Position.Z.Raw;
                    if (checked(dx * dx + dz * dz) <= limit) { dangerFound = true; break; }
                }
                if (!dangerFound) return true;
            }
            return false;
        }

        private int GoldNeeded(uint faction)
        {
            var rules = world.Config.Economy;
            if (!rules.GoldEnabled) return 0;
            var e = world.Economies[faction - 1];
            int demand = 0;
            if (e.Civ != CivKind.Primitive && e.Age == 2 && SavingToAdvance(faction))
                demand = e.Civ == CivKind.Metallurgy ? rules.Age3GoldCostMetallurgy : rules.Age3GoldCostAgrarian;
            else if (!SavingToAdvance(faction) && MonkPlanned(faction)) demand = rules.MonkGoldCost;
            if (e.Civ == CivKind.Academy && AcademyAllowed(faction))
            {
                // A lost or unreachable gold line ends the academy's gold demand; nobody is sent to a dead line.
                if (!HasUsableGoldSource(faction)) return 0;
                var next = AcademyNextResearch(faction);
                if (next != 0) demand += AcademyGoldCost(next);
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && IsGoldWork(v)) demand -= v.Carry;
            }
            return Math.Max(0, demand - e.Gold);
        }

        private TechKind AcademyNextResearch(uint faction)
        {
            if (!AcademyAllowed(faction)) return 0;
            foreach (var tech in AcademyResearchOrderFor(faction))
                if (AcademyTechOpen(faction, tech) && !BeingResearched(faction, tech)) return tech;
            return 0;
        }

        private bool MonkPlanned(uint faction)
            => world.Config.Economy.MonksEnabled && !CultAllowed(faction) && CompleteBarracks(faction)
                && QueuedOf(faction, UnitKind.Monk) == 0 && LivingClass(faction, UnitKind.Monk) == 0;

        private bool CompleteBarracks(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
            if (IsActualBuilding(b) && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Barracks) return true;
            }
            return false;
        }

        private int LivingClass(uint faction, UnitKind kind)
        {
            int count = 0;
            foreach (int i in world.SoldierTraversal)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction
                    && (world.Soldiers[i].Class == kind || world.Soldiers[i].Initial.Kind == kind)) count++;
            return count;
        }

        private bool GoldGathererRoom(VillagerState v, int food, int wood)
        {
            int gold = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var other = world.Villagers[i];
                if (other.Alive && other.FactionId == v.FactionId && IsGoldWork(other)) gold++;
            }
            if (gold >= world.Config.Economy.GoldGatherers) return false;
            return food >= MinimumFoodGatherers && wood >= MinimumWoodGatherers;
        }

        private void SetWorkNode(ref VillagerState v, int index)
        {
            if (LargeEconomyOn && largeWorkCountsTick == world.Tick)
            {
                int oldResource = v.NodeId > 0 && v.NodeId <= world.Nodes.Length
                    && (v.Task == VillagerTask.ToNode || v.Task == VillagerTask.Gathering || v.Task == VillagerTask.ToDropOff)
                    ? LargeWorkResourceIndex(world.Nodes[v.NodeId - 1].Definition.Kind) : -1;
                if (oldResource >= 0) largeWorkCounts[(int)(v.FactionId - 1) * 6 + oldResource]--;
                int newResource = LargeWorkResourceIndex(world.Nodes[index].Definition.Kind);
                if (newResource >= 0) largeWorkCounts[(int)(v.FactionId - 1) * 6 + newResource]++;
            }
            v.NodeId = world.Nodes[index].Definition.Id;
            v.BeltWaitUntilTick = 0;
            if (AiExpandOn) v.AiExpandWorkStartTick = world.Tick;
            v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.ToNode;
        }

        private int FishingWorkNode(SimPoint position, uint faction)
        {
            if (!FishingAllowed(faction)) return -1;
            // Fish outside a completed harbour's reach is deliberately not a fishing-civ fallback. Once the
            // covered fish is exhausted, the normal WorkKindFor path must resume so the faction can use ordinary
            // food (and the market opened by FishingNeedsFoodMarket) instead of silently chasing another fish.
            return NearestFishingNode(position, faction, true);
        }

        private int NearestFishingNode(SimPoint position, uint faction, bool harborOnly)
        {
            int best = -1;
            long bestDistance = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if ((LargeEconomyOn && !ResourceKnownToFaction(faction, i)) || !node.Fishing || node.Remaining <= 0) continue;
                if (harborOnly && !HarborCoversFish(faction, node.Definition.Position)) continue;
                long dx = node.Definition.Position.X.Raw - position.X.Raw;
                long dz = node.Definition.Position.Z.Raw - position.Z.Raw;
                long distance = checked(dx * dx + dz * dz);
                if (best < 0 || distance < bestDistance || distance == bestDistance && node.Definition.Id < world.Nodes[best].Definition.Id)
                { best = i; bestDistance = distance; }
            }
            return best;
        }

        private bool HarborCoversFish(uint faction, SimPoint fish)
        {
            var reach = Fix64.FromInt(world.Config.Economy.FishReach);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || !b.Complete || b.FactionId != faction || b.Kind != BuildingKind.Harbor) continue;
                if (InRange(BuildingCenter(b), fish, reach)) return true;
            }
            return false;
        }

        /// <summary>The same centre-to-centre walk the soldiers' local routes use, on the shared route cache.</summary>
        private SimPoint VillagerRouteTarget(ref VillagerState v, SimPoint goal)
        {
            if (v.Route.Length == 0 || !SamePoint(v.RouteGoal, goal))
            {
                v.RouteGoal = goal;
                ReleaseVillagerRoute(ref v);
                v.Route = world.Map.SharedRoute(world.Map.Cell(v.Position), goal, v.FactionId);
                v.RouteCursor = 0;
                if (v.Route.Length > 1 && Clear(v.Position, world.Map.Center(v.Route[1]), v.FactionId)) v.RouteCursor = 1;
            }
            var path = v.Route;
            if (path.Length == 0) return v.Position;
            while (v.RouteCursor + 1 < path.Length && SamePoint(v.Position, world.Map.Center(path[v.RouteCursor]))) v.RouteCursor++;
            var target = world.Map.Center(path[v.RouteCursor]);
            if (v.RouteCursor == path.Length - 1 && Clear(v.Position, goal, v.FactionId)) target = goal;
            return Clear(v.Position, target, v.FactionId) ? target : world.Map.Center(world.Map.Cell(v.Position));
        }

        private void ReleaseVillagerRoute(ref VillagerState v)
        {
            world.Map.ReleaseSharedRoute(v.Route);
            v.Route = Array.Empty<int>();
        }

        private void SpawnVillager(uint faction)
            => SpawnVillager(faction, OwnCore(faction).Definition.Position, false);

        private void SpawnVillager(uint faction, SimPoint origin)
            => SpawnVillager(faction, origin, false);

        private void SpawnVillager(uint faction, SimPoint origin, bool stoneTools)
        {
            int cell = -1;
            for (int i = 0; i < world.Config.Map.WidthCells * world.Config.Map.HeightCells; i++)
                if (world.Map.IsPassable(i) && (cell < 0 || DistanceSquared(origin, world.Map.Center(i)) < DistanceSquared(origin, world.Map.Center(cell)))) cell = i;
            if (cell < 0) return;
            int index = world.VillagerCount;
            if (index == world.Villagers.Length)
                Array.Resize(ref world.Villagers, world.Villagers.Length == 0 ? 4 : checked(world.Villagers.Length * 2));
            var position = world.Map.Center(cell);
            world.Villagers[index] = new VillagerState { Id = world.NextVillagerId, FactionId = faction, Alive = true,
                Hp = world.Config.Economy.VillagerHp, Position = position, MoveGoal = position, Route = Array.Empty<int>(),
                StoneToolsUsed = stoneTools, AiExpandWorkStartTick = AiExpandOn ? -1 : 0 };
            world.NextVillagerId = checked(world.NextVillagerId + 1);
        }

        /// <summary>V3-3: the numbers of the faction's economy policy; always the scenario's own numbers without industry.</summary>
        private EconomyDecision.Plan PlanOf(uint faction)
        {
            var rules = world.Config.Economy;
            return EconomyDecision.PlanFor(IndustryOn ? world.Economies[faction - 1].Policy : EconomyPolicy.Balanced,
                AutoVillagerTargetFor(faction), rules.AutoInfantryQueue);
        }

        private EconomyDecision.Plan PlanOf(uint faction, SimPoint point)
        {
            if (!IndustryOn || !RegionsOn) return PlanOf(faction);
            uint region = RegionForPoint(point);
            var policy = region > 0 && region <= world.Regions.Length
                ? world.Factions[faction - 1].RegionEconomyPolicies[region - 1]
                : world.Economies[faction - 1].Policy;
            return EconomyDecision.PlanFor(policy, AutoVillagerTargetFor(faction), world.Config.Economy.AutoInfantryQueue);
        }

        private CoreState OwnCore(uint faction) => world.Cores[world.Factions[faction - 1].CoreId - 1];

        private int LivingVillagers(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.VillagerCount; i++) if (world.Villagers[i].Alive && world.Villagers[i].FactionId == faction) count++;
            return count;
        }

        private int LivingSoldiers(uint faction)
        {
            int count = 0;
            foreach (int i in world.SoldierTraversal) if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction) count++;
            return count;
        }
    }
}
