using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class CharmsEditModeTests
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
        public void CharmsHasJapaneseNameAndFixedContractValues()
        {
            Assert.That((int)ResourceKind.Charms, Is.EqualTo(19));
            Assert.That((int)BuildingKind.CharmWorkshop, Is.EqualTo(41));
            Assert.That((int)ProcessingLineKind.Charms, Is.EqualTo(14));
            Assert.That(EconomyLayer.BuildingName(BuildingKind.CharmWorkshop), Is.EqualTo("\u8b77\u7b26\u5de5\u623f"));
        }

        [Test]
        public void CharmsUsesWorkshopFallbackModel()
        {
            Assert.That(LocalVisualPack.BuildingAssetPath(BuildingKind.CharmWorkshop), Does.EndWith("Workshop.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.CharmWorkshop, 0), Does.EndWith("Workshop_0.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.CharmWorkshop, 1), Does.EndWith("Workshop_1.FBX"));
        }
    }
}
