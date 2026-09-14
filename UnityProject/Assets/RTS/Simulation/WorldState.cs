using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    // Private to the simulation assembly: callers only receive copied Contracts snapshots.
    internal struct SoldierState
    {
        internal SoldierDefinition Initial;
        internal SimPoint Position, MoveGoal;
        internal int Hp;
        internal bool Alive, IsMoving, IsAttacking, IsRetreating;
        internal long NextAttackTick;
        internal byte TargetKind; // 0 = none, 1 = soldier, 2 = core
        internal uint TargetId;
        internal UnitParameters Parameters;
        internal Fix64 StepDistance;
    }

    internal struct ArmyState
    {
        internal ArmyDefinition Definition;
        internal uint[] SoldierIds;
        internal PolicyKind Policy;
        internal PolicyGoal Goal;
        internal ulong CommandId;
        internal long AcceptedTick, ApplyTick;
    }

    internal struct CoreState
    {
        internal CoreDefinition Definition;
        internal int Hp;
    }

    internal struct FactionState
    {
        internal uint Id, CoreId;
        internal uint[] ArmyIds;
        internal int AliveCount;
        internal uint NextContactId;
        internal uint[] ContactIds; // soldier ID - 1 -> local contact ID; never exported
    }

    internal sealed class WorldState
    {
        internal readonly ScenarioDefinition Config;
        internal readonly SoldierState[] Soldiers;
        internal readonly ArmyState[] Armies;
        internal readonly CoreState[] Cores;
        internal readonly FactionState[] Factions;
        internal readonly int[] SoldierTraversal;
        internal readonly int[] ArmyTraversal;
        internal readonly SplitMix64 CombatRandom, AiRandom;
        internal readonly uint NextSoldierId, NextArmyId, NextCoreId, NextOutpostId, NextFactionId;
        internal long Tick;
        internal ulong InputCursor;
        internal MatchResult Result;

        internal WorldState(ScenarioDefinition source)
        {
            Config = CopyAndValidate(source);
            CombatRandom = new SplitMix64(Config.Seed);
            AiRandom = new SplitMix64(Config.Seed);
            Soldiers = new SoldierState[Config.Soldiers.Length];
            Armies = new ArmyState[Config.Armies.Length];
            Cores = new CoreState[Config.Cores.Length];
            Factions = new FactionState[2];
            NextSoldierId = checked((uint)Soldiers.Length + 1);
            NextArmyId = checked((uint)Armies.Length + 1);
            NextCoreId = checked((uint)Cores.Length + 1);
            NextOutpostId = checked((uint)Config.Outposts.Length + 1);
            NextFactionId = 3;
            for (int i = 0; i < Cores.Length; i++)
                Cores[i] = new CoreState { Definition = Config.Cores[i], Hp = Config.Cores[i].Hp };
            for (int i = 0; i < Soldiers.Length; i++)
            {
                var d = Config.Soldiers[i];
                var p = Array.Find(Config.UnitParameters, value => value.Kind == d.Kind);
                Soldiers[i] = new SoldierState { Initial = d, Position = d.Position, MoveGoal = d.Position,
                    Alive = d.Alive, Hp = d.Hp, Parameters = p, StepDistance = Fix64.FromRaw(p.Speed.Raw / 20) };
            }
            var soldiers = new System.Collections.Generic.List<int>();
            var armies = new System.Collections.Generic.List<int>();
            for (int f = 0; f < 2; f++)
            {
                var d = Config.Factions[f];
                Factions[f] = new FactionState { Id = d.Id, CoreId = d.CoreId, ArmyIds = d.ArmyIds,
                    ContactIds = new uint[Soldiers.Length], NextContactId = 1 };
                foreach (uint id in d.ArmyIds)
                {
                    var ids = new System.Collections.Generic.List<uint>();
                    for (int i = 0; i < Soldiers.Length; i++)
                        if (Soldiers[i].Initial.ArmyId == id) { ids.Add((uint)i + 1); soldiers.Add(i); }
                    Armies[id - 1] = new ArmyState { Definition = Config.Armies[id - 1], SoldierIds = ids.ToArray() };
                    armies.Add((int)id - 1);
                }
            }
            SoldierTraversal = soldiers.ToArray();
            ArmyTraversal = armies.ToArray();
        }

        private static ScenarioDefinition CopyAndValidate(ScenarioDefinition s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            Require(s.Map != null && s.Rules != null && s.ScenarioId != null, "Missing configuration.");
            Require(s.SchemaVersion == 1 && s.TickRateHz == 20 && s.VerificationTickLimit > 0, "Unsupported scenario version or tick rate.");
            var m = s.Map;
            Require(m.WidthMeters > 0 && m.WidthMeters <= 1024 && m.HeightMeters > 0 && m.HeightMeters <= 1024
                && m.CellSizeMeters > 0 && m.WidthCells > 0 && m.HeightCells > 0
                && (long)m.WidthCells * m.CellSizeMeters == m.WidthMeters
                && (long)m.HeightCells * m.CellSizeMeters == m.HeightMeters, "Invalid map dimensions.");
            Require(m.DefaultPassable && m.BlockedCellIds != null && m.BlockedCellIds.Length == 0, "Week one requires an entirely passable map.");
            var r = s.Rules;
            Require(r.FactionCap > 0 && r.CoreRadius.Raw >= 0 && r.CoreRadius <= Fix64.FromInt(1024)
                && r.OwnedObjectiveVision.Raw >= 0 && r.CaptureRadius.Raw >= 0
                && r.CaptureDurationTicks > 0 && r.CoreReinforcementIntervalTicks > 0
                && r.OutpostReinforcementIntervalTicks > 0, "Invalid rules.");
            var c = new ScenarioDefinition { SchemaVersion = s.SchemaVersion, ScenarioId = s.ScenarioId, Seed = s.Seed,
                TickRateHz = s.TickRateHz, VerificationTickLimit = s.VerificationTickLimit,
                Map = new MapDefinition { WidthMeters = m.WidthMeters, HeightMeters = m.HeightMeters,
                    CellSizeMeters = m.CellSizeMeters, WidthCells = m.WidthCells, HeightCells = m.HeightCells },
                Rules = new RuleDefinition { FactionCap = r.FactionCap, CoreRadius = r.CoreRadius,
                    OwnedObjectiveVision = r.OwnedObjectiveVision, CaptureRadius = r.CaptureRadius,
                    CaptureDurationTicks = r.CaptureDurationTicks, CoreReinforcementIntervalTicks = r.CoreReinforcementIntervalTicks,
                    OutpostReinforcementIntervalTicks = r.OutpostReinforcementIntervalTicks },
                UnitParameters = Copy(s.UnitParameters), Factions = Copy(s.Factions), Cores = Copy(s.Cores),
                Outposts = Copy(s.Outposts), Armies = Copy(s.Armies), Soldiers = Copy(s.Soldiers) };
            // Definitions may arrive in any enumeration order; IDs are explicit and contiguous.
            Array.Sort(c.Factions, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.Cores, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.Outposts, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.Armies, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.Soldiers, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.UnitParameters, (a, b) => a.Kind.CompareTo(b.Kind));
            Require(c.Factions.Length == 2 && c.Cores.Length == 2, "Exactly two factions and cores are required.");
            for (int i = 0; i < c.UnitParameters.Length; i++)
            {
                var p = c.UnitParameters[i];
                Require((p.Kind == UnitKind.Infantry || p.Kind == UnitKind.Scout) && (i == 0 || c.UnitParameters[i - 1].Kind != p.Kind)
                    && p.Hp > 0 && p.Damage >= 0 && p.AttackIntervalTicks > 0 && p.Speed.Raw >= 0 && p.Speed <= Fix64.FromInt(16)
                    && p.Range.Raw >= 0 && p.Range <= Fix64.FromInt(1024) && p.Vision.Raw >= 0, "Invalid unit parameters.");
            }
            var assigned = new bool[c.Armies.Length];
            for (int f = 0; f < 2; f++)
            {
                var d = c.Factions[f];
                Require(d.Id == f + 1 && d.CoreId > 0 && d.CoreId <= 2, "Invalid faction.");
                d.ArmyIds = Copy(d.ArmyIds);
                Array.Sort(d.ArmyIds);
                foreach (uint id in d.ArmyIds)
                {
                    Require(id > 0 && id <= c.Armies.Length && !assigned[id - 1]
                        && c.Armies[id - 1].FactionId == d.Id, "Invalid army membership.");
                    assigned[id - 1] = true;
                }
                c.Factions[f] = d;
                Require(c.Cores[d.CoreId - 1].FactionId == d.Id, "Invalid faction core.");
            }
            for (int i = 0; i < c.Cores.Length; i++)
            {
                var d = c.Cores[i];
                Require(d.Id == i + 1 && d.FactionId >= 1 && d.FactionId <= 2 && d.Hp >= 0, "Invalid core.");
                ValidatePoint(d.Position, c.Map);
            }
            for (int i = 0; i < c.Outposts.Length; i++)
            {
                var d = c.Outposts[i];
                Require(d.Id == i + 1 && d.OwnerFactionId <= 2, "Invalid outpost.");
                ValidatePoint(d.Position, c.Map);
            }
            for (int i = 0; i < c.Armies.Length; i++)
            {
                var d = c.Armies[i];
                Require(d.Id == i + 1 && assigned[i] && d.Role != null && d.Capacity > 0
                    && d.HomeObjective.Kind == GoalKind.Core && d.HomeObjective.Id == c.Factions[d.FactionId - 1].CoreId,
                    "Invalid week-one army.");
            }
            var armyCounts = new int[c.Armies.Length];
            var factionCounts = new int[2];
            for (int i = 0; i < c.Soldiers.Length; i++)
            {
                var d = c.Soldiers[i];
                Require(d.Id == i + 1 && d.ArmyId > 0 && d.ArmyId <= c.Armies.Length
                    && d.FactionId == c.Armies[d.ArmyId - 1].FactionId, "Invalid soldier identity.");
                var p = Array.Find(c.UnitParameters, value => value.Kind == d.Kind);
                Require(p.Hp > 0 && d.Hp >= 0 && d.Hp <= p.Hp && d.Alive == (d.Hp > 0), "Invalid soldier HP or kind.");
                ValidatePoint(d.Position, c.Map);
                if (d.Alive)
                {
                    Require(++armyCounts[d.ArmyId - 1] <= c.Armies[d.ArmyId - 1].Capacity
                        && ++factionCounts[d.FactionId - 1] <= r.FactionCap, "Initial capacity exceeded.");
                }
            }
            return c;
        }

        internal static void ValidatePoint(SimPoint p, MapDefinition map) => Require(
            p.X.Raw >= 0 && p.X <= Fix64.FromInt(map.WidthMeters) && p.Z.Raw >= 0 && p.Z <= Fix64.FromInt(map.HeightMeters),
            "Point is outside the map.");
        private static T[] Copy<T>(T[] a) => a == null ? throw new ArgumentException("Missing scenario array.") : (T[])a.Clone();
        private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
    }
}
