using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class IncenseEditModeTests
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
        public void IncenseHasJapaneseNameAndFixedContractValues()
        {
            Assert.That((int)ResourceKind.Incense, Is.EqualTo(18));
            Assert.That((int)BuildingKind.IncenseHouse, Is.EqualTo(40));
            Assert.That((int)ProcessingLineKind.Incense, Is.EqualTo(13));
            Assert.That(EconomyLayer.BuildingName(BuildingKind.IncenseHouse), Is.EqualTo("香房"));
        }

        [Test]
        public void IncenseUsesWorkshopFallbackModel()
        {
            Assert.That(LocalVisualPack.BuildingAssetPath(BuildingKind.IncenseHouse), Does.EndWith("Workshop.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.IncenseHouse, 0), Does.EndWith("Workshop_0.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.IncenseHouse, 1), Does.EndWith("Workshop_1.FBX"));
        }
    }
}
