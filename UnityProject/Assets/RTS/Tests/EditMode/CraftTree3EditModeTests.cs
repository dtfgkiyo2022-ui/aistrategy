using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class CraftTree3EditModeTests
    {
        [Test]
        public void CraftTree3WireIdsAndNamesAreAppendOnly()
        {
            Assert.That((byte)ResourceKind.SiegeParts, Is.EqualTo(27));
            Assert.That((byte)ResourceKind.Mechanism, Is.EqualTo(28));
            Assert.That((byte)RecipeKind.SiegeParts, Is.EqualTo(4));
            Assert.That((byte)RecipeKind.Mechanism, Is.EqualTo(5));
            Assert.That((byte)BuildingKind.Assembler, Is.EqualTo(46));
            UiText.Japanese = true;
            try
            {
                Assert.That(GoodsMarketUiText.GoodsName(ResourceKind.SiegeParts), Is.EqualTo("攻城部品"));
                Assert.That(GoodsMarketUiText.GoodsName(ResourceKind.Mechanism), Is.EqualTo("精密部品"));
            }
            finally { UiText.Japanese = false; }
        }
    }
}
