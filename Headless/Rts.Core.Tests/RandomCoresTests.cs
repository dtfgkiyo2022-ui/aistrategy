using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    [TestFixture]
    public sealed class RandomCoresTests
    {
    [Test]
    public void SeedsOneToFiftyKeepRandomCoreContracts()
    {
        bool sawFactionOneInEastHalf = false;
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var scenario = MapGenerator.GenerateLarge(seed, gold: true, processingChain: true, largeEconomy: true,
                glassworks: true, randomCores: true);
            var a = scenario.Cores[0].Position;
            var b = scenario.Cores[1].Position;
            long dx = (b.X.Raw - a.X.Raw) / 65536, dz = (b.Z.Raw - a.Z.Raw) / 65536;
            Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(300L * 300L), $"seed {seed}");
            Assert.That(a.X.Raw / 65536, Is.InRange(21, 491), $"seed {seed} core 1 x");
            Assert.That(b.X.Raw / 65536, Is.InRange(21, 491), $"seed {seed} core 2 x");
            Assert.That(a.Z.Raw / 65536, Is.InRange(21, 235), $"seed {seed} core 1 z");
            Assert.That(b.Z.Raw / 65536, Is.InRange(21, 235), $"seed {seed} core 2 z");
            Assert.That(IsPassable(scenario, a), Is.True, $"seed {seed} core 1 terrain");
            Assert.That(IsPassable(scenario, b), Is.True, $"seed {seed} core 2 terrain");
            Assert.That(ResourceTotalNear(scenario, a, 60), Is.EqualTo(ResourceTotalNear(scenario, b, 60)), $"seed {seed} resource fairness");
            if (a.X.Raw / 65536 > scenario.Map.WidthMeters / 2) sawFactionOneInEastHalf = true;

            var same = MapGenerator.GenerateLarge(seed, gold: true, processingChain: true, largeEconomy: true,
                glassworks: true, randomCores: true);
            Assert.That(same.Cores[0].Position, Is.EqualTo(a), $"seed {seed} deterministic core 1");
            Assert.That(same.Cores[1].Position, Is.EqualTo(b), $"seed {seed} deterministic core 2");
            Assert.That(same.ResourceNodes.Length, Is.EqualTo(scenario.ResourceNodes.Length), $"seed {seed} node count");
            for (int i = 0; i < scenario.ResourceNodes.Length; i++)
                Assert.That(same.ResourceNodes[i].Position, Is.EqualTo(scenario.ResourceNodes[i].Position), $"seed {seed} node {i}");
        }
        Assert.That(sawFactionOneInEastHalf, Is.True);
    }

    [Test]
    public void FlagOffKeepsHistoricalLargeMapAndExtensionRoundTrips()
    {
        var old = MapGenerator.GenerateLarge(1, gold: true, processingChain: true, largeEconomy: true, glassworks: true);
        var explicitOff = MapGenerator.GenerateLarge(1, gold: true, processingChain: true, largeEconomy: true,
            glassworks: true, randomCores: false);
        Assert.That(ScenarioBinary.Encode(explicitOff), Is.EqualTo(ScenarioBinary.Encode(old)));

        var on = MapGenerator.GenerateLarge(1, gold: true, processingChain: true, largeEconomy: true,
            glassworks: true, randomCores: true);
        var roundTrip = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
        Assert.That(roundTrip.Economy.RandomCores, Is.True);
        Assert.That(roundTrip.Extensions, Has.Some.Matches<ScenarioExtensionData>(e => e.Id == 116 && e.Version == 1));
        on.Economy.RandomCores = false;
        var offRoundTrip = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
        Assert.That(offRoundTrip.Economy.RandomCores, Is.False);
        Assert.That(offRoundTrip.Extensions, Has.None.Matches<ScenarioExtensionData>(e => e.Id == 116));
    }

    private static bool IsPassable(ScenarioDefinition s, SimPoint p)
    {
        int cell = (int)(p.Z.Raw / 65536 / s.Map.CellSizeMeters) * s.Map.WidthCells
            + (int)(p.X.Raw / 65536 / s.Map.CellSizeMeters);
        return cell >= 0 && cell < s.Map.Terrain.Length && s.Map.Terrain[cell] == 0;
    }

    private static long ResourceTotalNear(ScenarioDefinition s, SimPoint p, int radius)
    {
        long total = 0, r2 = (long)radius * radius;
        foreach (var n in s.ResourceNodes)
        {
            long dx = (n.Position.X.Raw - p.X.Raw) / 65536, dz = (n.Position.Z.Raw - p.Z.Raw) / 65536;
            if (dx * dx + dz * dz <= r2) total += n.Amount;
        }
        return total;
    }
    }
}
