using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class GoodsMarketEditModeTests
    {
        [Test]
        public void GoodsPriceViewCarriesResourceAndQuantity()
        {
            var view = new GoodsPriceView(ResourceKind.Tack, 40);
            Assert.That(view.Goods, Is.EqualTo(ResourceKind.Tack));
            Assert.That(view.Amount, Is.EqualTo(40));
        }

        [Test]
        public void GoodsMarketDoesNotAddAnEnumValue()
        {
            Assert.That((int)ResourceKind.Steel, Is.EqualTo(9));
            Assert.That((int)ResourceKind.Tools, Is.EqualTo(21));
        }

        [Test]
        public void JapaneseGoodsTradeLabelCanBeCreated()
        {
            bool wasJapanese = UiText.Japanese;
            UiText.Japanese = true;
            try { Assert.That(GoodsMarketUiText.GoodsTradeLabel(ResourceKind.Tack, ResourceKind.Wood, 5), Is.EqualTo("馬具5 → 木材")); }
            finally { UiText.Japanese = wasJapanese; }
        }
    }
}
