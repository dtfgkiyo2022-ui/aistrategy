using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Core.Tests
{
    public sealed class BuildingRelocationUiRulesTests
    {
        private static BuildingView Building(uint id, uint faction, bool complete, bool reserved = false)
        {
            return new BuildingView(id, faction, BuildingKind.Barracks, default(SimPoint), 4, 100, 100,
                complete, 100, 100, 0, 0, reserved, reserved ? 1 : 0);
        }

        [Test]
        public void CompletedOwnBuildingShowsMoveAndRemove()
        {
            var state = BuildingRelocationUiRules.Evaluate(true, 1, Building(7, 1, true));

            Assert.That(state.CanMove, Is.True);
            Assert.That(state.CanRemove, Is.True);
            Assert.That(state.CanCancelReservation, Is.False);
        }

        [Test]
        public void FlagOffEnemyAndConstructionDoNotShowActions()
        {
            Assert.That(BuildingRelocationUiRules.Evaluate(false, 1, Building(7, 1, true)).PrimaryAction,
                Is.EqualTo(BuildingRelocationUiAction.None));
            Assert.That(BuildingRelocationUiRules.Evaluate(true, 1, Building(7, 2, true)).PrimaryAction,
                Is.EqualTo(BuildingRelocationUiAction.None));
            Assert.That(BuildingRelocationUiRules.Evaluate(true, 1, Building(7, 1, false)).PrimaryAction,
                Is.EqualTo(BuildingRelocationUiAction.None));
        }

        [Test]
        public void ReservationOnlyShowsCancelReservation()
        {
            var state = BuildingRelocationUiRules.Evaluate(true, 1, Building(7, 1, false, true));

            Assert.That(state.PrimaryAction, Is.EqualTo(BuildingRelocationUiAction.CancelReservation));
            Assert.That(state.CanMove, Is.False);
            Assert.That(state.CanRemove, Is.False);
            Assert.That(state.CanCancelReservation, Is.True);
        }

        [Test]
        public void ImmovableCompletedBuildingStillAllowsRemoval()
        {
            var building = new BuildingView(7, 1, BuildingKind.Castle, default(SimPoint), 8, 100, 100,
                true, 100, 100, 0, 0);
            var state = BuildingRelocationUiRules.Evaluate(true, 1, building);

            Assert.That(state.CanMove, Is.False);
            Assert.That(state.MoveBlockedByKind, Is.True);
            Assert.That(state.CanRemove, Is.True);
        }

        [Test]
        public void ActionsCreateTheExpectedEconomyCommands()
        {
            var move = BuildingRelocationUiRules.CreateCommand(BuildingRelocationUiAction.Move, 2, 3, 7, 44, Facing.West);
            var remove = BuildingRelocationUiRules.CreateCommand(BuildingRelocationUiAction.Remove, 2, 4, 7, 0, Facing.North);
            var cancel = BuildingRelocationUiRules.CreateCommand(BuildingRelocationUiAction.CancelReservation, 2, 5, 7, 0, Facing.North);

            Assert.That(move.Kind, Is.EqualTo(EconomyCommandKind.MoveBuilding));
            Assert.That(move.FactionId, Is.EqualTo(2u));
            Assert.That(move.IssuerSequence, Is.EqualTo(3ul));
            Assert.That(move.ProducerId, Is.EqualTo(7u));
            Assert.That(move.Cell, Is.EqualTo(44));
            Assert.That(remove.Kind, Is.EqualTo(EconomyCommandKind.RemoveBuilding));
            Assert.That(cancel.Kind, Is.EqualTo(EconomyCommandKind.RemoveBuilding));
        }
    }
}
