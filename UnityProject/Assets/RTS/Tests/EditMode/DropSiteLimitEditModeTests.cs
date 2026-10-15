using NUnit.Framework;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class DropSiteLimitEditModeTests
    {
        [Test]
        public void JapaneseLabelShowsCurrentCountAndMaximum()
        {
            UiText.Japanese = true;
            Assert.That(UiText.DropSiteLabel(1, 2, 30), Is.EqualTo("集積所 1/2（木30）"));
            UiText.Japanese = false;
        }
    }
}
