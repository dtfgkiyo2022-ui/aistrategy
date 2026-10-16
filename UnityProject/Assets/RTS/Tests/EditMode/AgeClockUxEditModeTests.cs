using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class AgeClockUxEditModeTests
    {
        [Test]
        public void AdvanceNowContractAndLabelsAreStable()
        {
            Assert.That((int)EconomyCommandKind.AdvanceNow, Is.EqualTo(27));
            Assert.That(EconomyCommand.AdvanceNow(1, 2, CivKind.Agrarian).Kind, Is.EqualTo(EconomyCommandKind.AdvanceNow));
            Assert.That(AgeClockUxUiText.Reservation("Agrarian", 4800, 0), Does.Contain("4:00"));
            Assert.That(AgeClockUxUiText.AdvanceNow(300, 200), Does.Contain("300"));
            Assert.That(AgeClockUxUiText.IsChoiceWindow(4800, 5400, 5000), Is.True);
        }
    }
}
