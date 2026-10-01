using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// The civilisation tails are written one after another (forestry, masonry, caravan, cavalry, bridge). Every combination
    /// must decode to the same flags and values, and a later tail's marker must never be read as an earlier tail's value.
    /// </summary>
    public sealed class CivTailChainTests
    {
        [Test]
        public void EveryCombinationOfCivilisationTailsRoundTrips()
        {
            for (int mask = 0; mask < 64; mask++)
            {
                var s = MapGenerator.GenerateTerrain(1);
                var e = s.Economy;
                e.Forestry = (mask & 1) != 0;
                e.Masonry = (mask & 2) != 0;
                e.Caravan = (mask & 4) != 0;
                e.Cavalry = (mask & 8) != 0;
                e.ProcessingChain = (mask & 16) != 0;
                e.Bridge = (mask & 32) != 0;
                // Non-default values show that each tail's payload lands in its own fields.
                e.MasonryDefenceCostPermille = 701; e.MasonryDefenceWorkPermille = 702;
                e.CaravanAutoVillagers = 7;
                e.CavalryDrillFood = 123; e.CavalryDrillTicks = 456;
                e.BridgeworksTicks = 789; e.SiegeDeploymentRamCapacityBonus = 2;
                var bytes = ScenarioBinary.Encode(s);
                var d = ScenarioBinary.Decode(bytes).Economy;
                Assert.That((d.Forestry, d.Masonry, d.Caravan, d.Cavalry, d.ProcessingChain, d.Bridge),
                    Is.EqualTo((e.Forestry, e.Masonry, e.Caravan, e.Cavalry, e.ProcessingChain, e.Bridge)), $"mask {mask}");
                if (e.Masonry) Assert.That((d.MasonryDefenceCostPermille, d.MasonryDefenceWorkPermille), Is.EqualTo((701, 702)), $"mask {mask}");
                if (e.Caravan) Assert.That(d.CaravanAutoVillagers, Is.EqualTo(7), $"mask {mask}");
                if (e.Cavalry) Assert.That((d.CavalryDrillFood, d.CavalryDrillTicks), Is.EqualTo((123, 456)), $"mask {mask}");
                if (e.Bridge) Assert.That((d.BridgeworksTicks, d.SiegeDeploymentRamCapacityBonus), Is.EqualTo((789, 2)), $"mask {mask}");
                Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(bytes)), Is.EqualTo(bytes), $"mask {mask}");
            }
        }

        /// <summary>
        /// The "all seven civilisations" switch of the live match turns the five later flags on together on a plain
        /// random map (no extra resources). Such a match must run, pick civilisations and replay tick for tick.
        /// </summary>
        [TestCase(1UL)]
        [TestCase(27UL)]
        public void AllSevenCivilisationsOnAPlainMapRunAndReplay(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Forestry = true; s.Economy.Masonry = true; s.Economy.Caravan = true;
            s.Economy.Cavalry = true; s.Economy.Bridge = true;
            var sim = new Rts.Simulation.Simulation(s);
            var gateway = new Rts.Application.CommandGateway(sim);
            for (int i = 0; i < 12000 && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "fault at tick " + sim.Capture(1).Tick);
            }
            TestContext.WriteLine("seed " + seed + ": civs=" + sim.Capture(1).Economy.Civ + "/" + sim.Capture(2).Economy.Civ
                + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age + ", tick=" + sim.Capture(1).Tick);
            using (var stream = new System.IO.MemoryStream())
            {
                var identity = new Rts.Replay.BuildIdentity();
                Rts.Application.ReplayRunner.Record(stream, s, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = Rts.Application.ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
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

        [TestCase(0, 3, 0, 0, 0, 2, 2, CivKind.Cavalry)]
        [TestCase(0, 3, 0, 0, 0, 1, 2, CivKind.Bridge)]
        [TestCase(0, 3, 0, 0, 2, 0, 2, CivKind.Caravan)]
        public void SevenWayChoiceKeepsTheOlderTieOrder(int ore, int food, int forest, int stone, int caravan, int cavalry, int bridge, CivKind expected)
        {
            Assert.That(EconomyDecision.ChooseCiv(ore, food, forest, stone, caravan, cavalry, bridge, 3), Is.EqualTo(expected));
        }

        [Test]
        public void AZeroBridgeScoreGivesTheSixWayAnswer()
        {
            for (int ore = 0; ore < 3; ore++)
            for (int food = 2; food < 6; food++)
            for (int stone = 0; stone < 3; stone++)
            for (int caravan = 0; caravan < 3; caravan++)
            for (int cavalry = 0; cavalry < 3; cavalry++)
                Assert.That(EconomyDecision.ChooseCiv(ore, food, 0, stone, caravan, cavalry, 0, 3),
                    Is.EqualTo(EconomyDecision.ChooseCiv(ore, food, 0, stone, caravan, cavalry, 3)));
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
