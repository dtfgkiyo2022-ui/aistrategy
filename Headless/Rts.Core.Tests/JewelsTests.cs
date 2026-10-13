using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class JewelsTests
    {
        [Test]
        public void MountainAiBuildsLapidaryAndReturnsGemsToCore()
        {
            var scenario = MapGenerator.GenerateTerrain(701);
            scenario.Economy.Enabled = true;
            scenario.Economy.Industry = true;
            scenario.Economy.Ages = true;
            scenario.Economy.Mountain = true;
            scenario.Economy.Jewels = true;
            scenario.Economy.StartFood = 50000;
            scenario.Economy.StartWood = 50000;
            scenario.Economy.StartStone = 50000;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.GatherIntervalTicks = 1000000;
            scenario.Economy.MountainWork = 1;
            scenario.Economy.MountainBaseIntervalTicks = 4;
            scenario.Economy.MountainIntervalStepTicks = 0;
            scenario.Economy.MountainMinIntervalTicks = 4;

            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Mountain));
            for (int i = 0; i < 12000; i++) gateway.Step();

            var economy = simulation.Capture(1).Economy;
            Assert.That(economy.Civ, Is.EqualTo(CivKind.Mountain));
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.MineShaft && b.Complete), Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Lapidary && b.Complete), Is.True);
            var snapshot = simulation.CaptureMatchPackSnapshot().Factions[0];
            var lapidary = economy.Buildings.Single(b => b.Kind == BuildingKind.Lapidary);
            Assert.That(economy.Gems, Is.GreaterThan(0), "ore=" + economy.Ore + ", stone=" + economy.Stone + ", produced=" + snapshot.JewelsProduced + ", consumed=" + snapshot.JewelsConsumed + ", output=" + lapidary.Output + ", belts=" + economy.Belts.Count);
        }

        [Test]
        public void JewelsExtensionRoundTripsAllTuningValues()
        {
            var scenario = MapGenerator.GenerateTerrain(702);
            scenario.Economy.Enabled = true;
            scenario.Economy.Industry = true;
            scenario.Economy.Ages = true;
            scenario.Economy.Mountain = true;
            scenario.Economy.Jewels = true;
            scenario.Economy.LapidarySizeCells = 3;
            scenario.Economy.LapidaryWoodCost = 91;
            scenario.Economy.LapidaryWork = 211;
            scenario.Economy.LapidaryHp = 411;
            scenario.Economy.JewelTicks = 121;
            scenario.Economy.JewelStoneInput = 4;
            scenario.Economy.JewelOreInput = 2;
            scenario.Economy.JewelMaxLapidaries = 2;
            scenario.Economy.JewelKeepStone = 41;
            scenario.Economy.JewelWoodReserve = 21;
            scenario.Economy.JewelMercenaryGems = 13;
            scenario.Economy.JewelMercenaryFood = 41;
            scenario.Economy.JewelMercenaryInfantryRatio = 4;

            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario));
            Assert.That(decoded.Extensions.Any(e => e.Id == 27), Is.True);
            Assert.That(decoded.Economy.Jewels, Is.True);
            Assert.That(decoded.Economy.LapidarySizeCells, Is.EqualTo(3));
            Assert.That(decoded.Economy.LapidaryWoodCost, Is.EqualTo(91));
            Assert.That(decoded.Economy.LapidaryWork, Is.EqualTo(211));
            Assert.That(decoded.Economy.LapidaryHp, Is.EqualTo(411));
            Assert.That(decoded.Economy.JewelTicks, Is.EqualTo(121));
            Assert.That(decoded.Economy.JewelStoneInput, Is.EqualTo(4));
            Assert.That(decoded.Economy.JewelOreInput, Is.EqualTo(2));
            Assert.That(decoded.Economy.JewelMaxLapidaries, Is.EqualTo(2));
            Assert.That(decoded.Economy.JewelKeepStone, Is.EqualTo(41));
            Assert.That(decoded.Economy.JewelWoodReserve, Is.EqualTo(21));
            Assert.That(decoded.Economy.JewelMercenaryGems, Is.EqualTo(13));
            Assert.That(decoded.Economy.JewelMercenaryFood, Is.EqualTo(41));
            Assert.That(decoded.Economy.JewelMercenaryInfantryRatio, Is.EqualTo(4));
        }
    }
}
