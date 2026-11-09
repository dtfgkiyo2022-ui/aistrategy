using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        // Runtime-only cache for chapter 44. It is a pure function of the current world state: it is rebuilt
        // whenever it is read on a tick other than the one it was built on, so a world loaded or cloned
        // mid-round sees exactly the same positions. It is never serialized or consulted by hashing.
        private readonly SimPoint[][] aiAvoidEnemyPositions =
            { Array.Empty<SimPoint>(), Array.Empty<SimPoint>() };
        private readonly long[] aiAvoidEnemyPositionsTick = { -1, -1 };
        private readonly int[] aiAvoidEnemyBlocked = new int[2];
        private readonly int[] aiAvoidEnemySkippedSites = new int[2];
        private readonly int[] aiAvoidEnemyUnfinishedLost = new int[2];
        private readonly int[] aiAvoidEnemyUnfinishedLostWood = new int[2];
        private readonly HashSet<uint> aiAvoidAutomaticBuildings = new HashSet<uint>();
        private bool advancingAutomaticReservation;

        private bool AiAvoidEnemyOn => world.Config.Economy.Enabled && world.Config.Economy.AiAvoidEnemy;

        private void PrepareAiAvoidEnemySnapshot(uint faction)
        {
            {
                var visible = world.Factions[faction - 1].VisibleCells;
                var points = new List<SimPoint>();
                // Soldier array indices are stable soldier numbers; do not use collection enumeration here.
                for (int i = 0; i < world.Soldiers.Length; i++)
                {
                    var soldier = world.Soldiers[i];
                    if (!soldier.Alive || soldier.Initial.FactionId == faction) continue;
                    int cell = world.Map.Cell(soldier.Position);
                    if (cell >= 0 && cell < visible.Length && visible[cell]) points.Add(soldier.Position);
                }
                aiAvoidEnemyPositions[faction - 1] = points.Count == 0 ? Array.Empty<SimPoint>() : points.ToArray();
                aiAvoidEnemyPositionsTick[faction - 1] = world.Tick;
            }
        }

        private bool AiAvoidEnemyNear(uint faction, SimPoint centre)
        {
            if (!AiAvoidEnemyOn) return false;
            if (aiAvoidEnemyPositionsTick[faction - 1] != world.Tick) PrepareAiAvoidEnemySnapshot(faction);
            var enemies = aiAvoidEnemyPositions[faction - 1];
            if (enemies.Length == 0) return false;
            long radius = Fix64.FromInt(world.Config.Economy.AiAvoidEnemyMeters).Raw;
            long limit = checked(radius * radius);
            foreach (var enemy in enemies)
            {
                long dx = checked(enemy.X.Raw - centre.X.Raw), dz = checked(enemy.Z.Raw - centre.Z.Raw);
                if (checked(dx * dx + dz * dz) <= limit) return true;
            }
            return false;
        }

        // An automatic reservation is recognised from saved building state only (38.20: an AI reservation
        // carries its placement reason and is not Held), never from a runtime-only set.
        private bool AiAvoidAutomaticReservation(BuildingState reservation)
            => AiReasonsOn && reservation.AiReason != AiReason.None && !reservation.Held;

        private void RecordAiAvoidEnemyBlocked(uint faction)
        {
            if (AiAvoidEnemyOn) aiAvoidEnemyBlocked[faction - 1] = checked(aiAvoidEnemyBlocked[faction - 1] + 1);
        }

        internal int AiAvoidEnemyBlocked(uint faction) => aiAvoidEnemyBlocked[faction - 1];
        internal int AiAvoidEnemySkippedSites(uint faction) => aiAvoidEnemySkippedSites[faction - 1];
        internal int AiAvoidEnemyUnfinishedLost(uint faction) => aiAvoidEnemyUnfinishedLost[faction - 1];
        internal int AiAvoidEnemyUnfinishedLostWood(uint faction) => aiAvoidEnemyUnfinishedLostWood[faction - 1];

        private void RecordAiAvoidEnemyUnfinishedLoss(BuildingState building)
        {
            if (!AiAvoidEnemyOn || building.Complete || !aiAvoidAutomaticBuildings.Remove(building.Id)) return;
            int index = checked((int)building.FactionId - 1);
            aiAvoidEnemyUnfinishedLost[index] = checked(aiAvoidEnemyUnfinishedLost[index] + 1);
            aiAvoidEnemyUnfinishedLostWood[index] = checked(aiAvoidEnemyUnfinishedLostWood[index] + building.ConstructionWood);
        }
    }
}
