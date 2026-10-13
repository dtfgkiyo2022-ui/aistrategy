using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class TradeGoodsEditModeTests
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
        public void WeaverHasJapaneseNameAndFixedContractValues()
        {
            Assert.That((int)ResourceKind.TradeGoods, Is.EqualTo(17));
            Assert.That((int)BuildingKind.Weaver, Is.EqualTo(39));
            Assert.That((int)ProcessingLineKind.TradeGoods, Is.EqualTo(12));
            Assert.That(EconomyLayer.BuildingName(BuildingKind.Weaver), Is.EqualTo("織物工房"));
        }

        [Test]
        public void WeaverUsesWorkshopFallbackModel()
        {
            Assert.That(LocalVisualPack.BuildingAssetPath(BuildingKind.Weaver), Does.EndWith("Workshop.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.Weaver, 0), Does.EndWith("Workshop_0.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.Weaver, 1), Does.EndWith("Workshop_1.FBX"));
        }
    }
}
