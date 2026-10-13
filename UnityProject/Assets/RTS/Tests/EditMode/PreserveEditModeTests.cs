using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class PreserveEditModeTests
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
        public void DryingRackUsesJapaneseNameAndUnusedPackModel()
        {
            Assert.AreEqual("干し場", EconomyLayer.BuildingName(BuildingKind.DryingRack));
            Assert.That(LocalVisualPack.BuildingAssetPath(BuildingKind.DryingRack), Does.EndWith("Tower_A.FBX"));
        }
    }
}
