using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class JewelsVisualEditModeTests
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
        public void LapidaryUsesTheRegisteredWorkshopModelAndConstructionModels()
        {
            Assert.That(LocalVisualPack.BuildingAssetPath(BuildingKind.Lapidary), Does.EndWith("Workshop.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.Lapidary, 0), Does.EndWith("Workshop_0.FBX"));
            Assert.That(LocalVisualPack.BuildingConstructionAssetPath(BuildingKind.Lapidary, 1), Does.EndWith("Workshop_1.FBX"));
        }
    }
}
