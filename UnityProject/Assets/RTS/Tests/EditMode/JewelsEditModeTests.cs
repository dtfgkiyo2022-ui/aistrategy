using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class JewelsEditModeTests
    {
        private bool wasJapanese;

        [SetUp]
        public void UseJapanese()
        {
            wasJapanese = UiText.Japanese;
            UiText.Japanese = true;
        }

        [TearDown]
        public void RestoreLanguage()
        {
            UiText.Japanese = wasJapanese;
        }

        [Test]
        public void JewelsContractsExposeMountainBuildingAndLine()
        {
            Assert.That((int)BuildingKind.Lapidary, Is.EqualTo(38));
            Assert.That((int)ProcessingLineKind.Jewels, Is.EqualTo(11));
            Assert.That(UiText.T("Lapidary", "研磨所"), Is.EqualTo("研磨所"));
        }
    }
}
