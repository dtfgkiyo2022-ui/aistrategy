using NUnit.Framework;
using Rts.Contracts;

namespace Rts.Tests.EditMode
{
    public sealed class CraftTree1EditModeTests
    {
        [Test]
        public void CraftTree1WireIdsAreAppendOnly()
        {
            Assert.That((byte)ResourceKind.CutStone, Is.EqualTo(22));
            Assert.That((byte)ResourceKind.Frames, Is.EqualTo(23));
            Assert.That((byte)BuildingKind.Stonecutter, Is.EqualTo(44));
            Assert.That((byte)BuildingKind.Joinery, Is.EqualTo(45));
            Assert.That((byte)ProcessingLineKind.CutStone, Is.EqualTo(17));
            Assert.That((byte)ProcessingLineKind.Frames, Is.EqualTo(18));
            Assert.That((byte)EconomyCommandKind.UpgradeBuilding, Is.EqualTo(25));
        }
    }
}
