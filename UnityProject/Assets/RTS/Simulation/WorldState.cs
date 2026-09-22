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
        internal PursuitMemory Pursuit;
        internal byte TargetKind; // 0 = none, 1 = soldier, 2 = core
        internal uint TargetId;
        internal UnitParameters Parameters;
        internal Fix64 StepDistance;
        internal bool Joining, TacticalRoute;
        internal int[] LocalPath;
        internal int LocalCursor, JoinCursor;
        internal SimPoint LocalGoal;
    }

    internal struct ArmyState
    {
        internal ArmyDefinition Definition;
        internal ArmyDecisionMemory Decision;
        internal uint[] AutoStartIds;
        internal uint[] SoldierIds;
        internal int[] Path;
        internal int PathCursor, AutoStage;
        internal SimPoint PathGoal;
        internal bool HasPathGoal, PathImpossible;
        internal PolicyKind Policy;
        internal PolicyGoal Goal;
        internal ulong CommandId, LogIndex;
        internal long AcceptedTick, ApplyTick;
    }

    internal struct OutpostState
    {
        internal OutpostDefinition Definition;
        internal uint OwnerFactionId, CapturingFaction;
        internal int CaptureTicks;
        internal long NextReinforcementTick;
    }

    internal struct CoreState
    {
        internal CoreDefinition Definition;
        internal int Hp;
        internal long NextReinforcementTick;
    }

    internal struct FactionState
    {
        internal uint Id, CoreId;
        internal uint[] ArmyIds;
        internal int AliveCount;
        internal uint NextContactId;
        internal uint NextArmyContactId;
        internal ArmyContactMemory[] ArmyContacts;
        internal uint[] ContactIds; // soldier ID - 1 -> local contact ID; never exported
        internal SimPoint[] ContactPositions;
        internal long[] ContactLastSeenTicks;
        internal bool[] ContactAbsent;
        internal bool[] VisibleCells, ExploredCells;
        internal ObjectiveMemory[] Objectives;
    }

    internal struct ArmyContactMemory
    {
        internal uint Id;
        internal SimPoint Position;
        internal long LastSeenTick;
        internal int Min, Max;
        internal bool Visible, Absent;
        internal uint[] Covered;
    }

    internal struct ObjectiveMemory
    {
        internal bool OwnerKnown, HpKnown;
        internal uint OwnerFactionId;
        internal int Hp;
        internal long LastSeenTick;
    }

    internal sealed class WorldState
    {
        internal readonly ScenarioDefinition Config;
        internal SoldierState[] Soldiers;
        internal readonly ArmyState[] Armies;
        internal readonly CoreState[] Cores;
        internal readonly OutpostState[] Outposts;
        internal readonly GridMap Map;
        internal readonly FactionState[] Factions;
        internal int[] SoldierTraversal;
        internal readonly int[] ArmyTraversal;
        internal readonly SplitMix64 CombatRandom, AiRandom;
        internal uint NextSoldierId;
        internal int SoldierCount => checked((int)(NextSoldierId - 1));
        internal readonly uint NextArmyId, NextCoreId, NextOutpostId, NextFactionId;
        internal long Tick;
        internal ulong InputCursor;
        internal MatchResult Result;

        internal WorldState(ScenarioDefinition source)
        {
            Config = CopyAndValidate(source);
            Map = new GridMap(Config.Map);
            Outposts = new OutpostState[Config.Outposts.Length];
            for (int i = 0; i < Outposts.Length; i++)
                Outposts[i] = new OutpostState { Definition = Config.Outposts[i], OwnerFactionId = Config.Outposts[i].OwnerFactionId,
                    NextReinforcementTick = Config.Outposts[i].OwnerFactionId == 0 ? 0 : Config.Rules.OutpostReinforcementIntervalTicks };
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
                Cores[i] = new CoreState { Definition = Config.Cores[i], Hp = Config.Cores[i].Hp, NextReinforcementTick = Config.Rules.CoreReinforcementIntervalTicks };
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
                int cellCount = checked(Config.Map.WidthCells * Config.Map.HeightCells);
                Factions[f] = new FactionState { Id = d.Id, CoreId = d.CoreId, ArmyIds = d.ArmyIds,
                    ContactIds = new uint[Soldiers.Length], ContactPositions = new SimPoint[Soldiers.Length],
                    ContactLastSeenTicks = new long[Soldiers.Length], ContactAbsent = new bool[Soldiers.Length],
                    VisibleCells = new bool[cellCount], ExploredCells = new bool[cellCount],
                    Objectives = new ObjectiveMemory[Cores.Length + Outposts.Length], NextContactId = 1, NextArmyContactId = 1, ArmyContacts = new ArmyContactMemory[Armies.Length] };
                foreach (uint id in d.ArmyIds)
                {
                    var ids = new System.Collections.Generic.List<uint>();
                    for (int i = 0; i < Soldiers.Length; i++)
                        if (Soldiers[i].Initial.ArmyId == id) { ids.Add((uint)i + 1); soldiers.Add(i); }
                    Armies[id - 1] = new ArmyState { Definition = Config.Armies[id - 1], SoldierIds = ids.ToArray(), AutoStartIds = ids.FindAll(id => Soldiers[id - 1].Alive).ToArray(), Path = Array.Empty<int>() };
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
            Require(m.BlockedCellIds != null && (long)m.WidthCells * m.HeightCells <= 8192, "Invalid grid.");
            var blocked = Copy(m.BlockedCellIds); Array.Sort(blocked);
            for (int i = 0; i < blocked.Length; i++)
                Require(blocked[i] >= 0 && blocked[i] < m.WidthCells * m.HeightCells && (i == 0 || blocked[i] != blocked[i - 1]), "Invalid blocked cell.");
            var r = s.Rules;
            Require(r.FactionCap > 0 && r.CoreRadius.Raw >= 0 && r.CoreRadius <= Fix64.FromInt(1024)
                && r.OwnedObjectiveVision.Raw >= 0 && r.CaptureRadius.Raw >= 0
                && r.CaptureDurationTicks > 0 && r.CoreReinforcementIntervalTicks > 0
                && r.OutpostReinforcementIntervalTicks > 0 && r.OccupationThreatMemoryTicks >= 0
                && r.DefaultReservePermille <= 1000, "Invalid rules.");
            var c = new ScenarioDefinition { SchemaVersion = s.SchemaVersion, ScenarioId = s.ScenarioId, Seed = s.Seed,
                TickRateHz = s.TickRateHz, VerificationTickLimit = s.VerificationTickLimit,
                Map = new MapDefinition { WidthMeters = m.WidthMeters, HeightMeters = m.HeightMeters,
                    CellSizeMeters = m.CellSizeMeters, WidthCells = m.WidthCells, HeightCells = m.HeightCells, DefaultPassable = m.DefaultPassable, BlockedCellIds = blocked },
                Rules = new RuleDefinition { FactionCap = r.FactionCap, CoreRadius = r.CoreRadius,
                    OwnedObjectiveVision = r.OwnedObjectiveVision, CaptureRadius = r.CaptureRadius,
                    CaptureDurationTicks = r.CaptureDurationTicks, CoreReinforcementIntervalTicks = r.CoreReinforcementIntervalTicks,
                    OutpostReinforcementIntervalTicks = r.OutpostReinforcementIntervalTicks,
                    OccupationThreatMemoryTicks = r.OccupationThreatMemoryTicks,
                    DefaultReservePermille = r.DefaultReservePermille },
                UnitParameters = Copy(s.UnitParameters), Factions = Copy(s.Factions), Cores = Copy(s.Cores),
                Outposts = Copy(s.Outposts), Armies = Copy(s.Armies), Soldiers = Copy(s.Soldiers),
                ResourceNodes = Copy(s.ResourceNodes), Economy = new EconomyRules { Enabled = s.Economy != null && s.Economy.Enabled } };
            Require(s.Economy != null, "Missing economy rules.");
            Require(!c.Economy.Enabled, "The economy is not implemented yet (technical-design-v3 8, PR 2).");
            Array.Sort(c.ResourceNodes, (a, b) => a.Id.CompareTo(b.Id));
            var nodeCells = new System.Collections.Generic.HashSet<long>();
            for (int i = 0; i < c.ResourceNodes.Length; i++)
            {
                var n = c.ResourceNodes[i];
                Require(n.Id == i + 1 && (n.Kind == ResourceKind.Food || n.Kind == ResourceKind.Wood) && n.Amount > 0, "Invalid resource node.");
                ValidatePoint(n.Position, c.Map);
                // One node per cell; the key is the cell, not the point, so two points in one cell are rejected too.
                long cell = (n.Position.Z.Raw / 65536 / m.CellSizeMeters) * m.WidthCells + n.Position.X.Raw / 65536 / m.CellSizeMeters;
                Require(nodeCells.Add(cell), "Two resource nodes share a cell.");
            }
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
                    && ((d.HomeObjective.Kind == GoalKind.Core && d.HomeObjective.Id == c.Factions[d.FactionId - 1].CoreId)
                    || (d.HomeObjective.Kind == GoalKind.Outpost && d.HomeObjective.Id > 0 && d.HomeObjective.Id <= c.Outposts.Length)),
                    "Invalid week-one army.");
            }
            var grid = new GridMap(c.Map);
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
                Require(grid.IsPassable(grid.Cell(d.Position)), "Soldier starts outside passable terrain.");
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
