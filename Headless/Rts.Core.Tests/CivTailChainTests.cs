using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// The civilisation tails are written one after another (forestry, masonry, caravan, cavalry). Every combination
    /// must decode to the same flags and values, and a later tail's marker must never be read as an earlier tail's value.
    /// </summary>
    public sealed class CivTailChainTests
    {
        [Test]
        public void EveryCombinationOfCivilisationTailsRoundTrips()
        {
            for (int mask = 0; mask < 32; mask++)
            {
                var s = MapGenerator.GenerateTerrain(1);
                var e = s.Economy;
                e.Forestry = (mask & 1) != 0;
                e.Masonry = (mask & 2) != 0;
                e.Caravan = (mask & 4) != 0;
                e.Cavalry = (mask & 8) != 0;
                e.ProcessingChain = (mask & 16) != 0;
                // Non-default values show that each tail's payload lands in its own fields.
                e.MasonryDefenceCostPermille = 701; e.MasonryDefenceWorkPermille = 702;
                e.CaravanAutoVillagers = 7;
                e.CavalryDrillFood = 123; e.CavalryDrillTicks = 456;
                var bytes = ScenarioBinary.Encode(s);
                var d = ScenarioBinary.Decode(bytes).Economy;
                Assert.That((d.Forestry, d.Masonry, d.Caravan, d.Cavalry, d.ProcessingChain),
                    Is.EqualTo((e.Forestry, e.Masonry, e.Caravan, e.Cavalry, e.ProcessingChain)), $"mask {mask}");
                if (e.Masonry) Assert.That((d.MasonryDefenceCostPermille, d.MasonryDefenceWorkPermille), Is.EqualTo((701, 702)), $"mask {mask}");
                if (e.Caravan) Assert.That(d.CaravanAutoVillagers, Is.EqualTo(7), $"mask {mask}");
                if (e.Cavalry) Assert.That((d.CavalryDrillFood, d.CavalryDrillTicks), Is.EqualTo((123, 456)), $"mask {mask}");
                Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(bytes)), Is.EqualTo(bytes), $"mask {mask}");
            }
        }

        [TestCase(0, 3, 0, 0, 0, 0, CivKind.Agrarian)]
        [TestCase(2, 3, 0, 0, 0, 2, CivKind.Metallurgy)]
        [TestCase(0, 3, 0, 0, 2, 2, CivKind.Caravan)]
        [TestCase(0, 3, 0, 0, 1, 2, CivKind.Cavalry)]
        [TestCase(0, 3, 0, 2, 0, 2, CivKind.Masonry)]
        [TestCase(0, 3, 2, 0, 0, 2, CivKind.Forestry)]
        public void SixWayChoiceKeepsTheOlderTieOrder(int ore, int food, int forest, int stone, int caravan, int cavalry, CivKind expected)
        {
            Assert.That(EconomyDecision.ChooseCiv(ore, food, forest, stone, caravan, cavalry, 3), Is.EqualTo(expected));
        }

        [Test]
        public void AZeroCavalryScoreGivesTheFiveWayAnswer()
        {
            for (int ore = 0; ore < 3; ore++)
            for (int food = 2; food < 6; food++)
            for (int forest = 0; forest < 3; forest++)
            for (int stone = 0; stone < 3; stone++)
            for (int caravan = 0; caravan < 3; caravan++)
                Assert.That(EconomyDecision.ChooseCiv(ore, food, forest, stone, caravan, 0, 3),
                    Is.EqualTo(EconomyDecision.ChooseCiv(ore, food, forest, stone, caravan, 3)));
        }
    }
}
