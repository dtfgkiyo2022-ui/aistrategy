using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    public enum ScopeKind : byte
    {
        All = 1, Army = 2, Outpost = 3
    }

    public enum PolicyKind : byte
    {
        Focus = 1, AllowAbandon = 2, Retreat = 3, MaintainReserve = 4,
    Defend = 5, Scout = 6, ReturnToAuto = 7
    }

    public enum CommandSource : byte
    {
        Human = 1, Doctrine = 2, Ai = 3
    }

    public enum CommandStatus : byte
    {
        Interpreting = 1, Pending = 2, Executing = 3, Completed = 4,
    Cancelled = 5, Expired = 6, Impossible = 7
    }

    public enum GoalKind : byte
    {
        None = 0, Point = 1, Outpost = 2, Core = 3
    }

    /// <summary>Observation-only kind of a deliberate cavalry raid target.</summary>
    public enum RaidTargetKind : byte
    {
        Resource = 1, Carrier = 2, IsolatedArmy = 3
    }

    public enum EndKind : byte
    {
        UntilReplaced = 1, Arrived = 2, ObjectiveOwned = 3, AtTick = 4, LossReached = 5
    }

    [Flags]
    public enum ExpireFlags : byte
    {
        None = 0, SubjectGone = 1, OwnershipChanged = 2, ObservationTooOld = 4
    }

    public enum InputKind : byte
    {
        Reserve = 1, Resolve = 2, Cancel = 3, Proposal = 4,
        /// <summary>Ver.3 direct economy operation; carries <see cref="ScheduledInput.Economy"/> and no orders.</summary>
        Economy = 5
    }

    // UnitKind has no assigned numbers in chapter 5; these are the initial contract values.
    // 3 is left for the sentry of the held PR #62. Villager is Ver.3 (technical-design-v3 3.3).
    // Archer and Cavalry (V3-5, technical-design-v3 32 #8) are what a barracks trains; LightCavalry is the V3-10 early
    // stable unit. They carry their own parameters and class; the counter line maps LightCavalry to Cavalry.
    public enum UnitKind : byte { Infantry = 1, Scout = 2, Villager = 4, Archer = 5, Cavalry = 6, Ram = 7, Mercenary = 8, Monk = 9, HeavyInfantry = 10, SkirmishArcher = 11, LightCavalry = 12 }

    /// <summary>Ver.3 resources (technical-design-v3 2.3). Ore and Metal are V3-2 (12.1); Metal is made, never found.</summary>
    /// <remarks>Gold (7) is the V3-5 resource B; Charcoal and Steel are V3-6.</remarks>
    public enum ResourceKind : byte { Food = 1, Wood = 2, Ore = 3, Metal = 4, Stone = 5, Gems = 6, Gold = 7, Charcoal = 8, Steel = 9, BowGear = 10 }

    /// <summary>
    /// V3-4 civilisation (technical-design-v3 26-27). Everyone starts Primitive and picks one of the two when advancing.
    /// More civilisations and later ages are added after the first ones are tried (the value is not an age number).
    /// </summary>
    public enum CivKind : byte { Primitive = 0, Agrarian = 1, Metallurgy = 2, Forestry = 3, Masonry = 4, Caravan = 5, Cavalry = 6, Bridge = 7, Academy = 8, Cult = 9, Fishing = 10, Mountain = 11 }

    /// <summary>
    /// V3-5 research at a blacksmith (technical-design-v3 32 #6). A faction's researched techs are a bit set of
    /// 1 &lt;&lt; (value - 1). Irrigation is for farming only, BlastFurnace for metallurgy only.
    /// </summary>
    /// <summary>Siegecraft, Masonry and Banking are the third age's techs (V3-5, 32 #10); the rest come with a civilisation.</summary>
    public enum TechKind : byte { Weapons = 1, Armour = 2, Tools = 3, Carts = 4, Irrigation = 5, BlastFurnace = 6, Siegecraft = 7, Masonry = 8, Banking = 9,
        SteelWeapons = 10, SteelArmour = 11, GemArmor = 12 }

    /// <summary>
    /// V3-11 #3: bridge civilisation research values live outside <see cref="TechKind"/> so the
    /// established tech numbers, names and meanings remain unchanged. They still use the existing
    /// research command and faction tech bitset; the values are intentionally after the last slot.
    /// </summary>
    public static class BridgeTech
    {
        public const TechKind Bridgeworks = (TechKind)14;
        public const TechKind SiegeDeployment = (TechKind)15;
    }

    /// <summary>V3-3 economy policy (technical-design-v3 20): what the automatic economy aims for.</summary>
    public enum EconomyPolicy : byte { Balanced = 0, Military = 1, Growth = 2 }

    /// <summary>V3-9 #3: the display-facing reason a caravan route is stopped.</summary>
    public enum CaravanStopReason : byte { None = 0, Normal = 1, Danger = 2, OutpostLost = 3, MarketLost = 4, HostLost = 5 }

    /// <summary>Ver.3 direction of a belt or a building's output (technical-design-v3 11.2). North is +z, east is +x.</summary>
    public enum Facing : byte { North = 0, East = 1, South = 2, West = 3 }

    /// <summary>Ver.3 buildings (technical-design-v3 3.3, 12.2). The core is not a building. Mine and Smelter are V3-2.</summary>
    public enum BuildingKind : byte { Barracks = 1, Mine = 2, Smelter = 3, Farm = 4, House = 5, DropSite = 6, Wall = 7, Tower = 8, Blacksmith = 9, Market = 10, SiegeWorkshop = 11,
        ArcheryRange = 12, Stable = 13, Castle = 14, CharcoalKiln = 15, Steelworks = 16, LumberCamp = 17, Fletcher = 18, Quarry = 19, Caravanserai = 20,
        EngineerCamp = 21, Bridge = 22, Academy = 23, Monastery = 24, Harbor = 25, MineShaft = 26 }

    public enum EventKind : byte
    {
        CommandChanged = 1, MoveStarted = 2, Attack = 3, Death = 4,
        Capture = 5, Reinforcement = 6, ContactChanged = 7, MatchEnded = 8, Fault = 9, AiReport = 10
    }

    public enum ReasonCode : byte
    {
        None = 0, Superseded = 1, UserCancelled = 2, Deadline = 3,
        StaleVersion = 4, InvalidPayload = 5, SubjectGone = 6, OwnershipChanged = 7,
        NoPath = 8, EmptyArmy = 9, ObservationTooOld = 10, LossLimit = 11, ReserveShortfall = 12
    }

}
