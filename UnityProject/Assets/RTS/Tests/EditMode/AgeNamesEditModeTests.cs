using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class AgeNamesEditModeTests
    {
        private static readonly CivKind[] Civilisations =
        {
            CivKind.Agrarian, CivKind.Metallurgy, CivKind.Forestry, CivKind.Masonry, CivKind.Caravan,
            CivKind.Cavalry, CivKind.Bridge, CivKind.Academy, CivKind.Cult, CivKind.Fishing,
            CivKind.Mountain, CivKind.Tollgate, CivKind.Metropolis, CivKind.Sanctuary
        };
        private static readonly string[][] Japanese =
        {
            new[] { "開墾", "都市", "交易", "豊穣" }, new[] { "銅", "鉄", "鋼", "機巧" },
            new[] { "伐採", "木工", "弓匠", "大樹" }, new[] { "石切り", "城壁", "城塞", "不落" },
            new[] { "行商", "隊商", "市場", "通商" }, new[] { "遊牧", "騎兵", "疾風", "大遠征" },
            new[] { "測量", "架橋", "攻城", "大工事" }, new[] { "書写", "学府", "発明", "叡智" },
            new[] { "祈り", "布教", "聖戦", "天啓" }, new[] { "漁村", "港", "船団", "大海" },
            new[] { "坑道", "鉱山", "宝玉", "山の王" }, new[] { "関所", "通行税", "要塞", "鉄壁" },
            new[] { "集落", "町", "都市", "大都市" }, new[] { "巡礼", "聖域", "遺物", "神域" }
        };
        private static readonly string[][] English =
        {
            new[] { "clearing", "city", "trade", "plenty" }, new[] { "copper", "iron", "steel", "mechanism" },
            new[] { "logging", "woodworking", "bowcraft", "great tree" }, new[] { "quarrying", "walls", "fortress", "unbreachable" },
            new[] { "peddling", "caravan", "market", "commerce" }, new[] { "nomadic", "cavalry", "gale", "grand expedition" },
            new[] { "surveying", "bridgebuilding", "siege", "great works" }, new[] { "scribal", "academy", "invention", "wisdom" },
            new[] { "prayer", "preaching", "holy war", "revelation" }, new[] { "fishing village", "harbor", "fleet", "open sea" },
            new[] { "shaft", "mine", "gemstones", "mountain king" }, new[] { "gate", "toll", "fortress", "iron wall" },
            new[] { "settlement", "town", "city", "metropolis" }, new[] { "pilgrimage", "sanctuary", "relic", "holy ground" }
        };

        [Test]
        public void AllCivilisationsHaveTheFourJapaneseAndEnglishAgeNames()
        {
            bool wasJapanese = UiText.Japanese;
            try
            {
                UiText.Japanese = true;
                Assert.That(TopBarDisplayText.AgeName(CivKind.Primitive, 0), Is.EqualTo("原始の時代"));
                for (int i = 0; i < Civilisations.Length; i++)
                    for (int age = 1; age <= 4; age++)
                        Assert.That(TopBarDisplayText.AgeName(Civilisations[i], age), Does.Contain(Japanese[i][age - 1]));

                UiText.Japanese = false;
                Assert.That(TopBarDisplayText.AgeName(CivKind.Primitive, 0), Is.EqualTo("primitive age"));
                for (int i = 0; i < Civilisations.Length; i++)
                    for (int age = 1; age <= 4; age++)
                        Assert.That(TopBarDisplayText.AgeName(Civilisations[i], age), Does.Contain(English[i][age - 1]));
            }
            finally { UiText.Japanese = wasJapanese; }
        }

        [Test]
        public void JapaneseAdvanceMessageContainsIntentAndUnlocks()
        {
            bool wasJapanese = UiText.Japanese;
            try
            {
                UiText.Japanese = true;
                string message = TopBarDisplayText.AgeAdvanceMessage(CivKind.Metallurgy, 2);
                Assert.That(message, Is.EqualTo("時代2に進みました：冶金の文明・鉄の時代 ― 組み合わせる。組立所・武具・2つ目の兵が使えます"));
            }
            finally { UiText.Japanese = wasJapanese; }
        }
    }
}
