using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BeltCraftTests
    {
        private static ScenarioDefinition Scenario(bool beltCraft)
        {
            var scenario = MapGenerator.GenerateTerrain(3816UL, false, true);
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.BeltComponents = true;
            scenario.Economy.BeltStack = true;
            scenario.Economy.CraftTree1 = true;
            scenario.Economy.CraftTree2 = true;
            scenario.Economy.CraftTree3 = true;
            scenario.Economy.CraftRequired = true;
            scenario.Economy.BeltCraft = beltCraft;
            return scenario;
        }

        [Test]
        public void WireValuesAndCommandPayloadsAreAppended()
        {
            Assert.That((int)ResourceKind.Gears, Is.EqualTo(35));
            Assert.That((int)BuildingKind.GearWorkshop, Is.EqualTo(51));
            Assert.That((int)ProcessingLineKind.Gears, Is.EqualTo(23));
            Assert.That((int)EconomyCommandKind.SetBeltPriority, Is.EqualTo(29));
            Assert.That((int)BeltComponentKind.Merger, Is.EqualTo(6));
            Assert.That((int)BeltSpeed.VeryFast, Is.EqualTo(2));
            var fast = EconomyCommand.PlaceFastBelt(1, 1, new[] { 10 }, new[] { Facing.East });
            var veryFast = EconomyCommand.PlaceBelt(1, 2, new[] { 10 }, new[] { Facing.East }, BeltSpeed.VeryFast);
            Assert.That(fast.Enabled, Is.True);
            Assert.That(fast.TargetId, Is.EqualTo(0));
            Assert.That(veryFast.Enabled, Is.False);
            Assert.That(veryFast.TargetId, Is.EqualTo((uint)BeltSpeed.VeryFast));
            var merger = EconomyCommand.PlaceMerger(1, 3, 10, Facing.East, BeltPriority.Right);
            Assert.That(merger.IsMerger, Is.True);
            Assert.That(merger.BeltPriority, Is.EqualTo(BeltPriority.Right));
        }

        [Test]
        public void Extension80RoundTripsAndFlagOffOmitsIt()
        {
            var on = Scenario(true);
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 80).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.BeltCraft, Is.True);
            Assert.That(decoded.Economy.MergerGears, Is.EqualTo(2));
            Assert.That(decoded.Economy.VeryFastBeltSteel, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))).Extensions.Any(x => x.Id == 80), Is.False);
        }

        [Test]
        public void EconomyFrameExposesGearsOnlyWhenEnabled()
        {
            var on = new Battle(Scenario(true)).Capture(1).Economy;
            var off = new Battle(Scenario(false)).Capture(1).Economy;
            Assert.That(on.BeltCraftEnabled, Is.True);
            Assert.That(on.Gears, Is.EqualTo(0));
            Assert.That(off.BeltCraftEnabled, Is.False);
            Assert.That(off.Gears, Is.EqualTo(0));
        }
    }
}
