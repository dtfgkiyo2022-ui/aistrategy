using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class ToolsEditModeTests
    {
        private bool wasJapanese;

        [SetUp] public void UseJapanese() { wasJapanese = UiText.Japanese; UiText.Japanese = true; }
        [TearDown] public void RestoreLanguage() { UiText.Japanese = wasJapanese; }

        [Test]
        public void ToolShopHasJapaneseNameAndFixedContractValues()
        {
            Assert.That((int)ResourceKind.Tools, Is.EqualTo(21));
            Assert.That((int)BuildingKind.ToolShop, Is.EqualTo(43));
            Assert.That((int)ProcessingLineKind.Tools, Is.EqualTo(16));
            Assert.That(EconomyLayer.BuildingName(BuildingKind.ToolShop), Is.EqualTo("工具工房"));
        }

        [Test]
        public void ToolShopUsesTheSelectedWorkshopModel()
        {
            Assert.That(LocalVisualPack.BuildingAssetPath(BuildingKind.ToolShop), Does.EndWith("Workshop.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.ToolShop, 0), Does.EndWith("Workshop_0.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.ToolShop, 1), Does.EndWith("Workshop_1.FBX"));
        }
    }
}
