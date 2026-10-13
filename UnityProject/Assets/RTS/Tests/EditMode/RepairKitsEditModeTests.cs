using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class RepairKitsEditModeTests
    {
        private bool wasJapanese;

        [SetUp]
        public void UseJapanese() { wasJapanese = UiText.Japanese; UiText.Japanese = true; }
        [TearDown]
        public void RestoreLanguage() { UiText.Japanese = wasJapanese; }

        [Test]
        public void RepairYardHasJapaneseNameAndFixedContractValues()
        {
            Assert.That((int)ResourceKind.RepairKits, Is.EqualTo(20));
            Assert.That((int)BuildingKind.RepairYard, Is.EqualTo(42));
            Assert.That((int)ProcessingLineKind.RepairKits, Is.EqualTo(15));
            Assert.That(UiText.T("Repair yard", "修繕所"), Is.EqualTo("修繕所"));
        }
    }
}
