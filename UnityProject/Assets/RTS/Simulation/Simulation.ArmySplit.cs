using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private const int ArmySplitMaxPerFaction = 24;

        private void SplitArmy(uint faction, uint sourceArmyId, IReadOnlyList<uint> selectedIds)
        {
            if (faction == 0 || faction > world.Factions.Length || sourceArmyId == 0 || sourceArmyId > world.Armies.Length
                || selectedIds == null || selectedIds.Count == 0)
                return;
            ref var factionState = ref world.Factions[faction - 1];
            if (!OwnArmy(faction, sourceArmyId)) return;

            var source = world.Armies[sourceArmyId - 1];
            int aliveCount = 0;
            foreach (uint soldierId in source.SoldierIds)
                if (IsLivingOwnSoldierInArmy(soldierId, faction, sourceArmyId)) aliveCount++;
            if (selectedIds.Count >= aliveCount) return;

            uint previous = 0;
            var selected = new HashSet<uint>();
            for (int i = 0; i < selectedIds.Count; i++)
            {
                uint soldierId = selectedIds[i];
                if (soldierId == 0 || (i > 0 && soldierId <= previous) || !selected.Add(soldierId)
                    || !IsLivingOwnSoldierInArmy(soldierId, faction, sourceArmyId)) return;
                previous = soldierId;
            }

            uint newArmyId = 0;
            foreach (uint candidate in factionState.ArmyIds.OrderBy(id => id))
            {
                if (candidate == sourceArmyId) continue;
                int candidateAlive = 0;
                foreach (uint soldierId in world.Armies[candidate - 1].SoldierIds)
                    if (world.Soldiers[soldierId - 1].Alive) candidateAlive++;
                if (candidateAlive == 0) { newArmyId = candidate; break; }
            }
            if (newArmyId == 0)
            {
                if (factionState.ArmyIds.Length >= ArmySplitMaxPerFaction) return;
                newArmyId = CreateSplitArmy(faction, source);
                if (newArmyId == 0) return;
            }
            else
            {
                ResetSplitArmy(newArmyId, source);
            }

            var remaining = new List<uint>(source.SoldierIds.Length);
            var moved = new List<uint>(selectedIds.Count);
            foreach (uint soldierId in source.SoldierIds)
            {
                if (selected.Contains(soldierId)) moved.Add(soldierId);
                else remaining.Add(soldierId);
            }
            world.Armies[sourceArmyId - 1].SoldierIds = remaining.ToArray();
            world.Armies[newArmyId - 1].SoldierIds = moved.ToArray();
            foreach (uint soldierId in moved)
                world.Soldiers[soldierId - 1].Initial.ArmyId = newArmyId;
            RebuildArmyAndSoldierTraversal();
        }

        private void MergeArmies(uint faction, uint targetArmyId, IReadOnlyList<uint> sourceArmyIds)
        {
            if (faction == 0 || faction > world.Factions.Length || targetArmyId == 0 || targetArmyId > world.Armies.Length
                || sourceArmyIds == null || sourceArmyIds.Count == 0 || !OwnArmy(faction, targetArmyId)) return;
            uint previous = 0;
            for (int i = 0; i < sourceArmyIds.Count; i++)
            {
                uint sourceId = sourceArmyIds[i];
                if (sourceId == 0 || (i > 0 && sourceId <= previous) || sourceId == targetArmyId
                    || !OwnArmy(faction, sourceId)) return;
                previous = sourceId;
            }

            var moved = new List<uint>();
            foreach (uint sourceId in sourceArmyIds)
            {
                var dead = new List<uint>();
                var living = new List<uint>();
                foreach (uint soldierId in world.Armies[sourceId - 1].SoldierIds)
                {
                    if (world.Soldiers[soldierId - 1].Alive) living.Add(soldierId);
                    else dead.Add(soldierId);
                }
                living.Sort();
                moved.AddRange(living);
                world.Armies[sourceId - 1].SoldierIds = dead.ToArray();
            }
            moved.Sort();
            if (moved.Count == 0)
            {
                RebuildArmyAndSoldierTraversal();
                return;
            }
            var target = world.Armies[targetArmyId - 1].SoldierIds.ToList();
            target.AddRange(moved);
            world.Armies[targetArmyId - 1].SoldierIds = target.ToArray();
            foreach (uint soldierId in moved)
                world.Soldiers[soldierId - 1].Initial.ArmyId = targetArmyId;
            RebuildArmyAndSoldierTraversal();
        }

        private bool OwnArmy(uint faction, uint armyId)
        {
            return armyId > 0 && armyId <= world.Armies.Length
                && world.Armies[armyId - 1].Definition.FactionId == faction;
        }

        private bool IsLivingOwnSoldierInArmy(uint soldierId, uint faction, uint armyId)
        {
            return soldierId > 0 && soldierId <= world.Soldiers.Length
                && world.Soldiers[soldierId - 1].Alive
                && world.Soldiers[soldierId - 1].Initial.FactionId == faction
                && world.Soldiers[soldierId - 1].Initial.ArmyId == armyId;
        }

        private uint CreateSplitArmy(uint faction, ArmyState source)
        {
            ref var factionState = ref world.Factions[faction - 1];
            uint id = world.NextArmyId;
            world.NextArmyId = checked(id + 1);
            var definition = new ArmyDefinition
            {
                Id = id,
                FactionId = faction,
                Role = source.Definition.Role,
                Capacity = source.Definition.Capacity,
                HomeObjective = source.Definition.HomeObjective
            };
            Array.Resize(ref world.Armies, world.Armies.Length + 1);
            world.Armies[id - 1] = new ArmyState
            {
                Definition = definition,
                Policy = source.Policy,
                Goal = source.Goal,
                CommandId = source.CommandId,
                LogIndex = source.LogIndex,
                AcceptedTick = source.AcceptedTick,
                ApplyTick = source.ApplyTick,
                AutoStartIds = Array.Empty<uint>(),
                SoldierIds = Array.Empty<uint>(),
                Path = Array.Empty<int>()
            };
            var armyIds = new uint[factionState.ArmyIds.Length + 1];
            Array.Copy(factionState.ArmyIds, armyIds, factionState.ArmyIds.Length);
            armyIds[armyIds.Length - 1] = id;
            factionState.ArmyIds = armyIds;
            for (int i = 0; i < world.Factions.Length; i++)
            {
                ref var observer = ref world.Factions[i];
                Array.Resize(ref observer.ArmyContacts, world.Armies.Length);
            }
            return id;
        }

        private void ResetSplitArmy(uint armyId, ArmyState source)
        {
            var definition = new ArmyDefinition
            {
                Id = armyId,
                FactionId = source.Definition.FactionId,
                Role = source.Definition.Role,
                Capacity = source.Definition.Capacity,
                HomeObjective = source.Definition.HomeObjective
            };
            world.Armies[armyId - 1] = new ArmyState
            {
                Definition = definition,
                Policy = source.Policy,
                Goal = source.Goal,
                CommandId = source.CommandId,
                LogIndex = source.LogIndex,
                AcceptedTick = source.AcceptedTick,
                ApplyTick = source.ApplyTick,
                AutoStartIds = Array.Empty<uint>(),
                SoldierIds = Array.Empty<uint>(),
                Path = Array.Empty<int>()
            };
            for (int i = 0; i < world.Factions.Length; i++)
            {
                ref var observer = ref world.Factions[i];
                if (observer.ArmyContacts.Length < world.Armies.Length)
                    Array.Resize(ref observer.ArmyContacts, world.Armies.Length);
                observer.ArmyContacts[armyId - 1] = default;
            }
        }

        private void RebuildArmyAndSoldierTraversal()
        {
            world.ArmyTraversal = new int[world.Armies.Length];
            for (int i = 0; i < world.ArmyTraversal.Length; i++) world.ArmyTraversal[i] = i;
            var soldierTraversal = new List<int>(world.SoldierCount);
            foreach (int armyIndex in world.ArmyTraversal)
                foreach (uint soldierId in world.Armies[armyIndex].SoldierIds)
                    soldierTraversal.Add(checked((int)soldierId - 1));
            world.SoldierTraversal = soldierTraversal.ToArray();
        }
    }
}
