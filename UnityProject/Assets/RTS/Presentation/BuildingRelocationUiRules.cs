using System;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Actions exposed for the selected building by the EasyRebuild presentation.</summary>
    public enum BuildingRelocationUiAction
    {
        None,
        Move,
        Remove,
        CancelReservation
    }

    /// <summary>Display-only result of the selected-building action rules.</summary>
    public readonly struct BuildingRelocationUiState
    {
        public BuildingRelocationUiAction PrimaryAction { get; }
        public bool CanMove { get; }
        public bool CanRemove { get; }
        public bool MoveBlockedByKind { get; }
        public bool CanCancelReservation { get { return PrimaryAction == BuildingRelocationUiAction.CancelReservation; } }

        public BuildingRelocationUiState(BuildingRelocationUiAction primaryAction)
            : this(primaryAction, primaryAction == BuildingRelocationUiAction.Move,
                primaryAction == BuildingRelocationUiAction.Move, false)
        {
        }

        public BuildingRelocationUiState(BuildingRelocationUiAction primaryAction, bool canMove, bool canRemove,
            bool moveBlockedByKind)
        {
            PrimaryAction = primaryAction;
            CanMove = canMove;
            CanRemove = canRemove;
            MoveBlockedByKind = moveBlockedByKind;
        }
    }

    /// <summary>
    /// Unity-independent policy for the buttons and hotkeys of a selected building.
    /// Reservations are intentionally a separate action: they may be cancelled, but never moved or removed as a built building.
    /// </summary>
    public static class BuildingRelocationUiRules
    {
        public static BuildingRelocationUiState Evaluate(bool easyRebuild, uint ownFaction, BuildingView building)
        {
            if (!easyRebuild || building.Id == 0 || building.FactionId != ownFaction)
                return new BuildingRelocationUiState(BuildingRelocationUiAction.None);
            if (building.Reserved)
                return new BuildingRelocationUiState(BuildingRelocationUiAction.CancelReservation);
            if (!building.Complete)
                return new BuildingRelocationUiState(BuildingRelocationUiAction.None);
            if (!IsMovableKind(building.Kind))
                return new BuildingRelocationUiState(BuildingRelocationUiAction.Remove, false, true, true);
            return new BuildingRelocationUiState(BuildingRelocationUiAction.Move);
        }

        private static bool IsMovableKind(BuildingKind kind)
        {
            return kind != BuildingKind.Farm && kind != BuildingKind.Bridge && kind != BuildingKind.Tollgate
                && kind != BuildingKind.Town && kind != BuildingKind.Castle && kind != BuildingKind.Shrine
                && kind != BuildingKind.Caravanserai && kind != BuildingKind.MineShaft && kind != BuildingKind.GrandHouse
                && kind != BuildingKind.EngineerCamp && kind != BuildingKind.Academy && kind != BuildingKind.Monastery
                && kind != BuildingKind.Harbor;
        }

        public static EconomyCommand CreateCommand(BuildingRelocationUiAction action, uint faction, ulong sequence,
            uint buildingId, int cell, Facing facing)
        {
            switch (action)
            {
                case BuildingRelocationUiAction.Move:
                    return EconomyCommand.MoveBuilding(faction, sequence, buildingId, cell, facing);
                case BuildingRelocationUiAction.Remove:
                case BuildingRelocationUiAction.CancelReservation:
                    return EconomyCommand.RemoveBuilding(faction, sequence, buildingId);
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "No economy command for this action.");
            }
        }
    }
}
