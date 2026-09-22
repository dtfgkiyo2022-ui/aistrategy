using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private void Reinforce()
        {
            foreach (var faction in world.Factions)
            {
                ref var core = ref world.Cores[faction.CoreId - 1];
                if (core.Hp > 0 && world.Tick >= core.NextReinforcementTick)
                {
                    core.NextReinforcementTick = checked(world.Tick + world.Config.Rules.CoreReinforcementIntervalTicks);
                    Spawn(faction.Id, GoalKind.Core, core.Definition.Id, core.Definition.Position);
                }
                for (int i = 0; i < world.Outposts.Length; i++)
                {
                    ref var post = ref world.Outposts[i];
                    if (post.OwnerFactionId != faction.Id || world.Tick < post.NextReinforcementTick) continue;
                    post.NextReinforcementTick = checked(world.Tick + world.Config.Rules.OutpostReinforcementIntervalTicks);
                    Spawn(faction.Id, GoalKind.Outpost, post.Definition.Id, post.Definition.Position);
                }
            }
        }

        private bool Spawn(uint faction, GoalKind kind, uint objective, SimPoint origin)
        {
            int alive = 0;
            foreach (int i in world.SoldierTraversal)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == faction) alive++;
            if (alive >= world.Config.Rules.FactionCap) return false;
            uint army = 0;
            for (int priority = 0; priority < 3 && army == 0; priority++)
                foreach (uint id in world.Factions[faction - 1].ArmyIds)
                {
                    var a = world.Armies[id - 1];
                    if (a.Definition.Role == "scout") continue;
                    var home = a.Definition.HomeObjective;
                    int rank = home.Kind == kind && home.Id == objective ? 0 : a.Definition.Role == "reserve" ? 1 : 2;
                    if (rank != priority) continue;
                    int count = 0;
                    foreach (uint soldier in a.SoldierIds) if (world.Soldiers[soldier - 1].Alive) count++;
                    if (count < a.Definition.Capacity) { army = id; break; }
                }
            if (army == 0) return false;
            int cell = -1;
            for (int i = 0; i < world.Config.Map.WidthCells * world.Config.Map.HeightCells; i++)
                if (world.Map.IsPassable(i) && (cell < 0 || DistanceSquared(origin, world.Map.Center(i)) < DistanceSquared(origin, world.Map.Center(cell)))) cell = i;
            if (cell < 0) return false; // No legal spawn position in an empty, impassable scenario.
            uint nextId = checked(world.NextSoldierId + 1);
            int index = world.SoldierCount;
            EnsureSoldierCapacity(index + 1);
            var position = world.Map.Center(cell);
            var parameters = Array.Find(world.Config.UnitParameters, p => p.Kind == UnitKind.Infantry);
            if (parameters.Hp <= 0) throw new ArithmeticException("Missing infantry parameters.");
            world.Soldiers[index] = new SoldierState
            {
                Initial = new SoldierDefinition { Id = world.NextSoldierId, FactionId = faction, ArmyId = army,
                    Kind = UnitKind.Infantry, Alive = true, Hp = parameters.Hp, Position = position },
                Alive = true, Hp = parameters.Hp, Position = position, MoveGoal = position,
                Parameters = parameters, StepDistance = Fix64.FromRaw(parameters.Speed.Raw / 20)
            };
            ref var members = ref world.Armies[army - 1].SoldierIds;
            Array.Resize(ref members, members.Length + 1);
            members[members.Length - 1] = world.NextSoldierId;
            world.NextSoldierId = nextId;
            var traversal = new List<int>(world.SoldierCount);
            foreach (int a in world.ArmyTraversal)
                foreach (uint id in world.Armies[a].SoldierIds) traversal.Add(checked((int)id - 1));
            world.SoldierTraversal = traversal.ToArray();
            // Only observers who can see the spawn receive the event.
            commandEvents.Add(new GameEvent(world.Tick, (uint)commandEvents.Count, EventKind.Reinforcement,
                (byte)((faction == 1 || IsVisibleTo(1, position) ? 1 : 0) | (faction == 2 || IsVisibleTo(2, position) ? 2 : 0)), nextId - 1, 0, origin, 1, ReasonCode.None));
            return true;
        }

        private void EnsureSoldierCapacity(int required)
        {
            int capacity = world.Soldiers.Length;
            while (capacity < required) capacity = capacity == 0 ? 1 : checked(capacity * 2);
            if (capacity != world.Soldiers.Length) Array.Resize(ref world.Soldiers, capacity);
            if (nextPositions.Length < capacity) Array.Resize(ref nextPositions, capacity);
            if (soldierDamage.Length < capacity) Array.Resize(ref soldierDamage, capacity);
            for (int f = 0; f < world.Factions.Length; f++)
            {
                ref var observer = ref world.Factions[f];
                if (observer.ContactIds.Length < capacity) Array.Resize(ref observer.ContactIds, capacity);
                if (observer.ContactPositions.Length < capacity) Array.Resize(ref observer.ContactPositions, capacity);
                if (observer.ContactLastSeenTicks.Length < capacity) Array.Resize(ref observer.ContactLastSeenTicks, capacity);
                if (observer.ContactAbsent.Length < capacity) Array.Resize(ref observer.ContactAbsent, capacity);
            }
        }

        private IReadOnlyList<ReinforcementView> ReinforcementViews(uint faction)
        {
            var result = new List<ReinforcementView>();
            var core = world.Cores[world.Factions[faction - 1].CoreId - 1];
            if (core.Hp > 0) result.Add(new ReinforcementView(GoalKind.Core, core.Definition.Id, core.NextReinforcementTick - world.Tick));
            foreach (var post in world.Outposts)
                if (post.OwnerFactionId == faction) result.Add(new ReinforcementView(GoalKind.Outpost, post.Definition.Id, post.NextReinforcementTick - world.Tick));
            return result;
        }
    }
}
