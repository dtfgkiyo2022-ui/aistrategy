using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CivRegistryTests
    {
        [Test]
        public void RegistrySelectionMatchesTheOldSevenWayRuleForEverySmallScore()
        {
            for (int food = 0; food <= 6; food++)
                for (int ore = 0; ore <= 3; ore++)
                    for (int forest = 0; forest <= 3; forest++)
                        for (int stone = 0; stone <= 3; stone++)
                            for (int caravan = 0; caravan <= 3; caravan++)
                                for (int cavalry = 0; cavalry <= 3; cavalry++)
                                    for (int bridge = 0; bridge <= 3; bridge++)
                                    {
                                        var candidates = new[]
                                        {
                                            new EconomyDecision.CivScore(CivKind.Agrarian, ExcessFood(food), 0),
                                            new EconomyDecision.CivScore(CivKind.Metallurgy, ore, 1),
                                            new EconomyDecision.CivScore(CivKind.Forestry, forest, 2),
                                            new EconomyDecision.CivScore(CivKind.Masonry, stone, 3),
                                            new EconomyDecision.CivScore(CivKind.Caravan, caravan, 4),
                                            new EconomyDecision.CivScore(CivKind.Cavalry, cavalry, 5),
                                            new EconomyDecision.CivScore(CivKind.Bridge, bridge, 6)
                                        };
                                        CivKind actual = EconomyDecision.ChooseCiv(candidates);
                                        CivKind expected = OldSevenWay(ore, food, forest, stone, caravan, cavalry, bridge);
                                        Assert.That(actual, Is.EqualTo(expected),
                                            "food=" + food + ", ore=" + ore + ", forest=" + forest + ", stone=" + stone
                                            + ", caravan=" + caravan + ", cavalry=" + cavalry + ", bridge=" + bridge);
                                    }
        }

        [Test]
        public void ChooseCivAcrossAllFlagsAndSeedsMatchesThePreRegistryRule()
        {
            var choose = typeof(Battle).GetMethod("ChooseCiv", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(choose, Is.Not.Null);
            foreach (int mask in EnumerateMasks())
                for (ulong seed = 1; seed <= 8; seed++)
                {
                    var scenario = MapGenerator.GenerateTerrain(seed, bridge: (mask & 16) != 0);
                    scenario.Economy.Forestry = (mask & 1) != 0;
                    scenario.Economy.Masonry = (mask & 2) != 0;
                    scenario.Economy.Caravan = (mask & 4) != 0;
                    scenario.Economy.Cavalry = (mask & 8) != 0;
                    scenario.Economy.Bridge = (mask & 16) != 0;
                    var sim = new Battle(scenario);
                    var core = scenario.Cores[0].Position;
                    int ore = 0, food = 0;
                    foreach (var node in scenario.ResourceNodes)
                    {
                        if (node.Kind == ResourceKind.Ore && InRange(node.Position, core, 44)) ore++;
                        else if (node.Kind == ResourceKind.Food && InRange(node.Position, core, 30)) food++;
                    }
                    int forest = (mask & 1) != 0 ? Score(sim, "CountUsableForestWood", core) : 0;
                    int stone = (mask & 2) != 0 ? Score(sim, "CountUsableMasonryStone", core) : 0;
                    int caravan = (mask & 4) != 0 ? Score(sim, "CountUsableCaravanOutposts", core) : 0;
                    int cavalry = (mask & 8) != 0 ? Score(sim, "CountCavalryMobility", core) : 0;
                    int bridge = (mask & 16) != 0 ? Score(sim, "CountUsableBridgeSaving", core) : 0;
                    CivKind expected = OldSevenWay(ore, food, forest, stone, caravan, cavalry, bridge);
                    CivKind actual = (CivKind)choose.Invoke(sim, new object[] { 1u });
                    Assert.That(actual, Is.EqualTo(expected), "mask=" + mask + ", seed=" + seed + ", ore=" + ore + ", food=" + food
                        + ", forest=" + forest + ", stone=" + stone + ", caravan=" + caravan + ", cavalry=" + cavalry + ", bridge=" + bridge);
                }
        }

        private static IEnumerable<int> EnumerateMasks()
        {
            for (int mask = 0; mask < 32; mask++) yield return mask;
        }

        private static int Score(Battle sim, string method, SimPoint core)
            => (int)typeof(Battle).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sim, new object[] { 1u, core });

        private static int ExcessFood(int food) => food > 3 ? food - 3 : 0;

        private static CivKind OldSevenWay(int ore, int food, int forest, int stone, int caravan, int cavalry, int bridge)
        {
            int agrarian = ExcessFood(food);
            if (agrarian >= ore && agrarian >= forest && agrarian >= stone && agrarian >= caravan && agrarian >= cavalry && agrarian >= bridge)
                return CivKind.Agrarian;
            if (ore >= forest && ore >= stone && ore >= caravan && ore >= cavalry && ore >= bridge) return CivKind.Metallurgy;
            if (forest >= stone && forest >= caravan && forest >= cavalry && forest >= bridge) return CivKind.Forestry;
            if (stone >= caravan && stone >= cavalry && stone >= bridge) return CivKind.Masonry;
            if (caravan >= cavalry && caravan >= bridge) return CivKind.Caravan;
            if (cavalry >= bridge) return CivKind.Cavalry;
            return CivKind.Bridge;
        }

        private static bool InRange(SimPoint a, SimPoint b, int radius)
        {
            long dx = a.X.Raw - b.X.Raw;
            long dz = a.Z.Raw - b.Z.Raw;
            long r = (long)Fix64.FromInt(radius).Raw;
            return dx * dx + dz * dz <= r * r;
        }
    }
}
