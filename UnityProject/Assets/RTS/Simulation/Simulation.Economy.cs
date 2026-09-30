using System;
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

        /// <summary>V3-5: how far ahead one stock must be before idle villagers go to the other.</summary>
        private const int StockGap = 300, WoodFloor = 150, MinimumFoodGatherers = 3, MinimumWoodGatherers = 2;

        /// <summary>AI phase, on the allocation cycle (5.4 step 1): the automatic economy trains villagers.</summary>
        private void DecideEconomy()
        {
            if (!EconomyOn || world.Tick % 20 != 0) return;
            var rules = world.Config.Economy;
            for (int f = 0; f < 2; f++)
            {
                uint faction = (uint)f + 1;
                if (world.Cores[world.Factions[f].CoreId - 1].Hp <= 0 || world.Economies[f].AutoOff) continue;
                ref var economy = ref world.Economies[f];
                int villagers = LivingVillagers(faction);
                var plan = PlanOf(faction);
                DecideAdvance(faction);
                if (!economy.CoreHeld && economy.AdvanceRemaining == 0 && EconomyDecision.ShouldTrainVillager(villagers, economy.Queued, plan.VillagerTarget, economy.Food,
                    rules.VillagerFoodCost, villagers + LivingSoldiers(faction) + QueuedInfantry(faction), PopCapFor(faction), rules.QueueLimit))
                {
                    economy.Food = checked(economy.Food - rules.VillagerFoodCost);
                    if (economy.Queued == 0) economy.TrainRemaining = rules.VillagerTrainTicks;
                    economy.Queued++;
                }
                ResumeUnbuilt(faction);
                DecideHouse(faction);
                DecideDropSite(faction);
                DecideTower(faction);
                DecideResearch(faction);
                DecideMarket(faction);
                DecideCaravanserai(faction);
                DecideCaravanRoute(faction);
                DecideTradeRoute(faction);
                DecideSiege(faction);
                DecideCrossUnit(faction);
                DecideCastle(faction);
                DecideCaravanMercenary(faction);
                DecideRepair(faction);
                DecideBuildings(faction);
                DecideIndustry(faction);
            }
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
                    else { v.Task = VillagerTask.Idle; v.NodeId = 0; v.Route = Array.Empty<int>(); v.RouteCursor = 0; }
                }
                if (IsTradeRouteTask(v.Task) && !TradeRouteActive(v)) { StopTradeRoute(ref v); continue; }
                if (IsCaravanTask(v.Task))
                {
                    if (!CaravanMarketValid(v)) { EndCaravanAtCore(ref v); }
                    else if (v.Task == VillagerTask.ToCaravanserai && !CaravanPairValid(v))
                    {
                        v.CaravanStage = 3; v.Task = VillagerTask.ToCaravanMarket; v.Route = Array.Empty<int>(); v.RouteCursor = 0; v.RouteGoal = v.Position;
                    }
                    else
                    {
                        StopCaravanOutboundForDanger(ref v);
                        if (HoldCaravanAtMarket(ref v)) continue;
                    }
                }
                if (v.Task == VillagerTask.Idle && !v.Held && !world.Economies[v.FactionId - 1].AutoOff) AssignWork(ref v);
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
                var next = world.Map.ClipMove(v.Position, FixMath.MoveTowards(v.Position, v.MoveGoal, world.VillagerStep));
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
            RegrowFishing();
            AdvanceBelts();
            AdvanceIndustry();
            AdvanceAges();
            AdvanceResearch();
            int count = world.VillagerCount; // villagers trained below start next tick
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
                        v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle; continue;
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
                        v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle; continue;
                    }
                    if (world.Tick < v.NextGatherTick) continue;
                    node.Remaining--;
                    v.CarryKind = node.Definition.Kind;
                    v.Carry++;
                    v.NextGatherTick = checked(world.Tick + GatherTicksFor(v.FactionId, v.NodeId));
                    if (v.Carry >= CarryFor(v.FactionId) || node.Remaining == 0) v.Task = VillagerTask.ToDropOff;
                }
                else if (v.Task == VillagerTask.ToDropOff)
                {
                    var drop = DropOff(v);
                    if (!InRange(v.Position, drop.point, drop.reach)) continue;
                    if (ProcessingKilnTarget(v))
                    {
                        if (!TryDeliverWoodToKiln(ref v)) AddStock(v.FactionId, v.CarryKind, v.Carry);
                        if (v.Carry > 0) continue; // a full kiln is a deliberate hand-haul wait state
                        if (v.HaulTo != 0 && AssignKilnWood(ref v, v.HaulTo)) continue;
                        v.HaulTo = 0; v.Task = VillagerTask.Idle;
                        continue;
                    }
                    AddStock(v.FactionId, v.CarryKind, v.Carry);
                    v.Carry = 0;
                    if (v.CaravanMarketId != 0 && v.CaravanseraiId != 0 && v.CaravanStage == 1)
                    {
                        v.Task = VillagerTask.ToCaravanMarket;
                        v.NodeId = 0; v.Route = Array.Empty<int>(); v.RouteCursor = 0; v.RouteGoal = v.Position;
                        continue;
                    }
                    if (v.HaulFrom != 0) { v.Task = VillagerTask.ToPickup; continue; }
                    if (AgesOn && !v.Held && !world.Economies[v.FactionId - 1].AutoOff && v.NodeId != 0
                        && world.Nodes[v.NodeId - 1].Definition.Kind != ResourceKind.Stone)
                    {
                        var currentKind = world.Nodes[v.NodeId - 1].Definition.Kind;
                        var kind = WorkKindFor(v);
                        // Only between food and wood: who goes to stone stays with StoneWanted, as for the idle.
                        if (kind != currentKind && kind != ResourceKind.Stone)
                        {
                            int index = NearestWorkNode(v.Position, kind);
                            if (index >= 0)
                            {
                                SetWorkNode(ref v, index);
                                continue;
                            }
                        }
                    }
                    v.Task = v.NodeId != 0 && world.Nodes[v.NodeId - 1].Remaining > 0 ? VillagerTask.ToNode : VillagerTask.Idle;
                }
                else if (v.Task == VillagerTask.ToPickup || v.Task == VillagerTask.ToDeliver) Haul(ref v);
            }
            AdvanceBuildings();
            for (int f = 0; f < 2; f++)
            {
                ref var economy = ref world.Economies[f];
                if (economy.Queued == 0) continue;
                if (economy.TrainRemaining > 0) economy.TrainRemaining--;
                if (economy.TrainRemaining > 0) continue;
                uint faction = (uint)f + 1;
                // A full population holds the finished villager at the door until there is room.
                if (LivingVillagers(faction) + LivingSoldiers(faction) >= PopCapFor(faction)) continue;
                SpawnVillager(faction);
                economy.Queued--;
                economy.TrainRemaining = economy.Queued > 0 ? rules.VillagerTrainTicks : 0;
            }
        }

        /// <summary>Fishing is finite: at each configured multiple, one unit returns up to the original amount.</summary>
        private void RegrowFishing()
        {
            var rules = world.Config.Economy;
            if (!rules.FishingEnabled || world.Tick % rules.FishRegrowTicks != 0) return;
            foreach (uint id in world.FishingNodeIds)
            {
                ref var node = ref world.Nodes[id - 1];
                if (node.Remaining < node.Definition.Amount) node.Remaining++;
            }
        }

        private void AssignWork(ref VillagerState v)
        {
            var kind = WorkKindFor(v);
            int index = NearestWorkNode(v.Position, kind);
            if (index < 0) index = NearestWorkNode(v.Position, kind == ResourceKind.Food ? ResourceKind.Wood : ResourceKind.Food);
            if (index < 0) return; // nothing left anywhere: stays idle
            SetWorkNode(ref v, index);
        }

        private bool ProcessingKilnTarget(VillagerState v)
            => ProcessingOn && (v.Carry == 0 || v.CarryKind == ResourceKind.Wood) && v.HaulTo > 0 && v.HaulTo <= world.BuildingCount
               && world.Buildings[v.HaulTo - 1].Kind == BuildingKind.CharcoalKiln;

        private bool AssignKilnWood(ref VillagerState v, uint kilnId)
        {
            if (!ProcessingOn || kilnId == 0 || kilnId > world.BuildingCount) return false;
            var kiln = world.Buildings[kilnId - 1];
            if (!kiln.Alive || !kiln.Complete || kiln.FactionId != v.FactionId) return false;
            int index = NearestWorkNode(v.Position, ResourceKind.Wood);
            if (index < 0) return false;
            v.HaulFrom = 0; v.HaulTo = kilnId; v.HaulNodeId = 0;
            v.NodeId = world.Nodes[index].Definition.Id;
            v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.ToNode;
            return true;
        }

        private bool TryDeliverWoodToKiln(ref VillagerState v)
        {
            if (!ProcessingKilnTarget(v)) return false;
            ref var kiln = ref world.Buildings[v.HaulTo - 1];
            if (!kiln.Alive || !kiln.Complete)
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
            int food = 0, wood = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var other = world.Villagers[i];
                if (!other.Alive || other.Id == v.Id || other.FactionId != v.FactionId || other.Task == VillagerTask.Idle || other.NodeId == 0
                    || other.Task == VillagerTask.ToBuild || other.Task == VillagerTask.Building) continue;
                var otherKind = world.Nodes[other.NodeId - 1].Definition.Kind;
                if (otherKind == ResourceKind.Food) food++; else if (otherKind == ResourceKind.Wood) wood++;
            }
            ResourceKind current = v.NodeId == 0 ? 0 : world.Nodes[v.NodeId - 1].Definition.Kind;
            if (GoldNeeded(v.FactionId) > 0 && GoldGathererRoom(v, food, wood)) return ResourceKind.Gold;
            var kind = StoneWanted(v.FactionId) ? ResourceKind.Stone : EconomyDecision.KindToGather(food, wood, PlanOf(v.FactionId).FoodPerWood);
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

        private int NearestWorkNode(SimPoint position, ResourceKind kind)
        {
            if (kind == ResourceKind.Gold) return NearestGoldNode(position);
            int n = world.Nodes.Length;
            var positions = new SimPoint[n];
            var kinds = new ResourceKind[n];
            var remaining = new int[n];
            for (int i = 0; i < n; i++) { positions[i] = world.Nodes[i].Definition.Position; kinds[i] = world.Nodes[i].Definition.Kind; remaining[i] = world.Nodes[i].Remaining; }
            return EconomyDecision.NearestNode(position, positions, kinds, remaining, kind);
        }

        private int NearestGoldNode(SimPoint position)
        {
            int best = -1; long bestDistance = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (node.Definition.Kind != ResourceKind.Gold || node.Remaining <= 0) continue;
                if (world.Map.SharedRoute(world.Map.Cell(position), node.Definition.Position).Length == 0) continue;
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
        {
            var node = world.Nodes[v.NodeId - 1];
            if (node.Remaining <= 0 || world.Map.SharedRoute(world.Map.Cell(v.Position), node.Definition.Position).Length == 0) return false;
            int danger = world.Config.Economy.GoldDangerMeters;
            long limit = checked(Fix64.FromInt(danger).Raw * Fix64.FromInt(danger).Raw);
            var faction = world.Factions[v.FactionId - 1];
            foreach (int i in world.SoldierTraversal)
            {
                var enemy = world.Soldiers[i];
                if (!enemy.Alive || enemy.Initial.FactionId == v.FactionId) continue;
                int cell = world.Map.Cell(enemy.Position);
                if (cell < 0 || !faction.VisibleCells[cell]) continue;
                long dx = enemy.Position.X.Raw - node.Definition.Position.X.Raw, dz = enemy.Position.Z.Raw - node.Definition.Position.Z.Raw;
                if (checked(dx * dx + dz * dz) <= limit) return false;
            }
            return true;
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
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && IsGoldWork(v)) demand -= v.Carry;
            }
            return Math.Max(0, demand - e.Gold);
        }

        private bool MonkPlanned(uint faction)
            => world.Config.Economy.MonksEnabled && CompleteBarracks(faction) && QueuedOf(faction, UnitKind.Monk) == 0 && LivingClass(faction, UnitKind.Monk) == 0;

        private bool CompleteBarracks(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Barracks) return true;
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
            v.NodeId = world.Nodes[index].Definition.Id;
            v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.ToNode;
        }

        /// <summary>The same centre-to-centre walk the soldiers' local routes use, on the shared route cache.</summary>
        private SimPoint VillagerRouteTarget(ref VillagerState v, SimPoint goal)
        {
            if (v.Route.Length == 0 || !SamePoint(v.RouteGoal, goal))
            {
                v.RouteGoal = goal;
                v.Route = world.Map.SharedRoute(world.Map.Cell(v.Position), goal);
                v.RouteCursor = 0;
                if (v.Route.Length > 1 && Clear(v.Position, world.Map.Center(v.Route[1]))) v.RouteCursor = 1;
            }
            var path = v.Route;
            if (path.Length == 0) return v.Position;
            while (v.RouteCursor + 1 < path.Length && SamePoint(v.Position, world.Map.Center(path[v.RouteCursor]))) v.RouteCursor++;
            var target = world.Map.Center(path[v.RouteCursor]);
            if (v.RouteCursor == path.Length - 1 && Clear(v.Position, goal)) target = goal;
            return Clear(v.Position, target) ? target : world.Map.Center(world.Map.Cell(v.Position));
        }

        private void SpawnVillager(uint faction)
        {
            var origin = OwnCore(faction).Definition.Position;
            int cell = -1;
            for (int i = 0; i < world.Config.Map.WidthCells * world.Config.Map.HeightCells; i++)
                if (world.Map.IsPassable(i) && (cell < 0 || DistanceSquared(origin, world.Map.Center(i)) < DistanceSquared(origin, world.Map.Center(cell)))) cell = i;
            if (cell < 0) return;
            int index = world.VillagerCount;
            if (index == world.Villagers.Length)
                Array.Resize(ref world.Villagers, world.Villagers.Length == 0 ? 4 : checked(world.Villagers.Length * 2));
            var position = world.Map.Center(cell);
            world.Villagers[index] = new VillagerState { Id = world.NextVillagerId, FactionId = faction, Alive = true,
                Hp = world.Config.Economy.VillagerHp, Position = position, MoveGoal = position, Route = Array.Empty<int>() };
            world.NextVillagerId = checked(world.NextVillagerId + 1);
        }

        /// <summary>V3-3: the numbers of the faction's economy policy; always the scenario's own numbers without industry.</summary>
        private EconomyDecision.Plan PlanOf(uint faction)
        {
            var rules = world.Config.Economy;
            return EconomyDecision.PlanFor(IndustryOn ? world.Economies[faction - 1].Policy : EconomyPolicy.Balanced,
                rules.AutoVillagerTarget, rules.AutoInfantryQueue);
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
