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

        /// <summary>AI phase, on the allocation cycle (5.4 step 1): the automatic economy trains villagers.</summary>
        private void DecideEconomy()
        {
            if (!EconomyOn || world.Tick % 20 != 0) return;
            var rules = world.Config.Economy;
            for (int f = 0; f < 2; f++)
            {
                uint faction = (uint)f + 1;
                if (world.Cores[world.Factions[f].CoreId - 1].Hp <= 0) continue;
                ref var economy = ref world.Economies[f];
                int villagers = LivingVillagers(faction);
                if (EconomyDecision.ShouldTrainVillager(villagers, economy.Queued, rules.AutoVillagerTarget, economy.Food,
                    rules.VillagerFoodCost, villagers + LivingSoldiers(faction) + QueuedInfantry(faction), rules.PopulationCap, rules.QueueLimit))
                {
                    economy.Food = checked(economy.Food - rules.VillagerFoodCost);
                    if (economy.Queued == 0) economy.TrainRemaining = rules.VillagerTrainTicks;
                    economy.Queued++;
                }
                DecideBuildings(faction);
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
                if (v.Task == VillagerTask.Idle) AssignWork(ref v);
                SimPoint goal;
                if (v.Task == VillagerTask.ToNode) goal = world.Nodes[v.NodeId - 1].Definition.Position;
                else if (v.Task == VillagerTask.ToDropOff) goal = OwnCore(v.FactionId).Definition.Position;
                else if (v.Task == VillagerTask.ToBuild) goal = world.Map.Center(world.Buildings[v.BuildingId - 1].WorkCell);
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
            int count = world.VillagerCount; // villagers trained below start next tick
            for (int i = 0; i < count; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive) continue;
                if (v.Task == VillagerTask.ToNode)
                {
                    var node = world.Nodes[v.NodeId - 1];
                    if (node.Remaining <= 0) { v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle; continue; }
                    if (InRange(v.Position, node.Definition.Position, GatherReach))
                    {
                        v.Task = VillagerTask.Gathering;
                        v.NextGatherTick = checked(world.Tick + rules.GatherIntervalTicks);
                    }
                }
                else if (v.Task == VillagerTask.Gathering)
                {
                    ref var node = ref world.Nodes[v.NodeId - 1];
                    if (node.Remaining <= 0) { v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle; continue; }
                    if (world.Tick < v.NextGatherTick) continue;
                    node.Remaining--;
                    v.CarryKind = node.Definition.Kind;
                    v.Carry++;
                    v.NextGatherTick = checked(world.Tick + rules.GatherIntervalTicks);
                    if (v.Carry >= rules.CarryCapacity || node.Remaining == 0) v.Task = VillagerTask.ToDropOff;
                }
                else if (v.Task == VillagerTask.ToDropOff)
                {
                    var core = OwnCore(v.FactionId);
                    if (!InRange(v.Position, core.Definition.Position, world.Config.Rules.CoreRadius + rules.DropOffMargin)) continue;
                    ref var economy = ref world.Economies[v.FactionId - 1];
                    if (v.CarryKind == ResourceKind.Food) economy.Food = checked(economy.Food + v.Carry);
                    else economy.Wood = checked(economy.Wood + v.Carry);
                    v.Carry = 0;
                    v.Task = v.NodeId != 0 && world.Nodes[v.NodeId - 1].Remaining > 0 ? VillagerTask.ToNode : VillagerTask.Idle;
                }
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
                if (LivingVillagers(faction) + LivingSoldiers(faction) >= rules.PopulationCap) continue;
                SpawnVillager(faction);
                economy.Queued--;
                economy.TrainRemaining = economy.Queued > 0 ? rules.VillagerTrainTicks : 0;
            }
        }

        private void AssignWork(ref VillagerState v)
        {
            int food = 0, wood = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var other = world.Villagers[i];
                if (!other.Alive || other.FactionId != v.FactionId || other.Task == VillagerTask.Idle || other.NodeId == 0
                    || other.Task == VillagerTask.ToBuild || other.Task == VillagerTask.Building) continue;
                if (world.Nodes[other.NodeId - 1].Definition.Kind == ResourceKind.Food) food++; else wood++;
            }
            int n = world.Nodes.Length;
            var positions = new SimPoint[n];
            var kinds = new ResourceKind[n];
            var remaining = new int[n];
            for (int i = 0; i < n; i++) { positions[i] = world.Nodes[i].Definition.Position; kinds[i] = world.Nodes[i].Definition.Kind; remaining[i] = world.Nodes[i].Remaining; }
            var kind = EconomyDecision.KindToGather(food, wood);
            int index = EconomyDecision.NearestNode(v.Position, positions, kinds, remaining, kind);
            if (index < 0) index = EconomyDecision.NearestNode(v.Position, positions, kinds, remaining, kind == ResourceKind.Food ? ResourceKind.Wood : ResourceKind.Food);
            if (index < 0) return; // nothing left anywhere: stays idle
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
