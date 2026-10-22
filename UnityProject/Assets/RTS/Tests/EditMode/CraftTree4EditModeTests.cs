using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class CraftTree4EditModeTests
    {
        [Test]
        public void NewProductsAndCannonHaveJapaneseAndEnglishText()
        {
            bool wasJapanese = UiText.Japanese;
            try
            {
                UiText.Japanese = false;
                Assert.That(UiText.CraftTree4ProductDescription(), Does.Contain("Siege engine"));
                Assert.That(UiText.CannonDescription(), Does.Contain("Cannon"));
                Assert.That(GoodsMarketUiText.GoodsName(ResourceKind.SiegeEngine), Does.Contain("Siege"));
                UiText.Japanese = true;
                Assert.That(UiText.CraftTree4ProductDescription(), Does.Contain("攻城機関"));
                Assert.That(UiText.CannonDescription(), Does.Contain("大砲"));
                Assert.That(GoodsMarketUiText.GoodsName(ResourceKind.EliteKit), Does.Contain("精鋭"));
            }
            finally { UiText.Japanese = wasJapanese; }
        }
    }
}
