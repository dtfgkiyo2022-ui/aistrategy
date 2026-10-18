using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class BuildingReservationEditModeTests
    {
        [Test]
        public void BuildingViewCarriesReservationOrder()
        {
            var building = new BuildingView(1, 1, BuildingKind.Barracks, new SimPoint(), 6, 0, 0,
                false, 0, 400, 0, 0, true, 2);
            Assert.That(building.Reserved, Is.True);
            Assert.That(building.ReservationOrder, Is.EqualTo(2));
        }

        [Test]
        public void SelectionTextExplainsTheWaitingReservationInJapanese()
        {
            var building = new BuildingView(1, 1, BuildingKind.Barracks, new SimPoint(), 6, 0, 0,
                false, 0, 400, 0, 0, true, 1);
            var economy = new EconomyView(0, 0, false, 2, 4, 2, 12, 12, 2,
                new[] { building }, System.Array.Empty<ResourceView>(), System.Array.Empty<BeltView>(),
                new bool[144], new bool[144]);
            bool japanese = UiText.Japanese;
            UiText.Japanese = true;
            try
            {
                StringAssert.Contains("予約中", SelectionInfoText.BuildingReservationStatus(building, economy));
            }
            finally
            {
                UiText.Japanese = japanese;
            }
        }

        [Test]
        public void WaitingReservationUsesTheFullBuildingHeight()
        {
            Assert.That(UiInfoRules.BuildingDisplayHeight(3f, false, true, 0, 400), Is.EqualTo(3f));
            Assert.That(UiInfoRules.BuildingDisplayHeight(3f, false, false, 0, 400), Is.EqualTo(0.6f));
        }
    }
}
