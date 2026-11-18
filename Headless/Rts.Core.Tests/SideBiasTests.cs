using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class SideBiasTests
    {
        [Test]
        public void SideFairRotationAndSourceChecksRemainEquivalentThrough3000Ticks()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.AiHuntUntilCore = false; // Keep this legacy rotation check independent of huntcore's point-goal paths.
            var normalScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var rotatedScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Rotate(rotatedScenario);
            normalScenario.Economy.SideFair = true;
            rotatedScenario.Economy.SideFair = true;
            var normal = new Battle(normalScenario);
            var rotated = new Battle(rotatedScenario);

            Assert.That(normalScenario.Economy.SideFair, Is.True);
            Assert.That(FirstDifference(normal, rotated, rotatedScenario), Is.Null, "source: initial state");
            for (long tick = 1; tick <= 3000; tick++)
            {
                normal.Step(tick, Array.Empty<ScheduledInput>());
                rotated.Step(tick, Array.Empty<ScheduledInput>());
                string difference = FirstDifference(normal, rotated, rotatedScenario);
                if (tick == 20) Assert.That(difference, Is.Null, "source: formation/navigation");
                if (tick == 100) Assert.That(difference, Is.Null, "source: drop-site/farm placement");
                if (tick == 240) Assert.That(difference, Is.Null, "source: exploration/path tie breaks");
                if (tick == 818) Assert.That(difference, Is.Null, "source: belt route/drop-off tie breaks");
                if (tick == 2300) Assert.That(difference, Is.Null, "source: town placement");
                Assert.That(difference, Is.Null, "tick " + tick);
            }
        }

        [Test]
        public void SideFairDefaultsOff()
        {
            Assert.That(new EconomyRules().SideFair, Is.False);
        }

        private static string FirstDifference(Battle normal, Battle rotated, ScenarioDefinition scenario)
        {
            var a = normal.Capture(1);
            var b = rotated.Capture(1);
            if (a.AliveCount != b.AliveCount || a.Units.Count != b.Units.Count) return "units/count";
            var au = a.Units.OrderBy(x => x.Id).ToArray();
            var bu = b.Units.OrderBy(x => x.Id).ToArray();
            for (int i = 0; i < au.Length; i++)
            {
                if (!Same(au[i].Position, Mirror(bu[i].Position, scenario))) return "Units[" + au[i].Id + "].Position";
                if (au[i].IsMoving != bu[i].IsMoving || au[i].IsAttacking != bu[i].IsAttacking
                    || au[i].IsRetreating != bu[i].IsRetreating || au[i].Hp != bu[i].Hp)
                    return "Units[" + au[i].Id + "].state";
            }
            if (a.Economy == null || b.Economy == null) return a.Economy == b.Economy ? null : "Economy.null";
            foreach (string name in new[] { "Food", "Wood", "Population", "PopulationCap", "VillagerQueued", "VillagerTrainRemaining", "Ore", "Metal", "Stone", "Gold", "Gems", "Charcoal", "Steel", "Slag" })
            {
                object left = typeof(EconomyView).GetProperty(name).GetValue(a.Economy);
                object right = typeof(EconomyView).GetProperty(name).GetValue(b.Economy);
                if (!Equals(left, right)) return "Economy." + name;
            }
            var av = a.Economy.Villagers.OrderBy(x => x.Id).ToArray();
            var bv = b.Economy.Villagers.OrderBy(x => x.Id).ToArray();
            if (av.Length != bv.Length) return "Villagers.Count";
            for (int i = 0; i < av.Length; i++)
            {
                if (!Same(av[i].Position, Mirror(bv[i].Position, scenario)) || av[i].Activity != bv[i].Activity
                    || av[i].CarryKind != bv[i].CarryKind || av[i].Carry != bv[i].Carry)
                    return "Villagers[" + av[i].Id + "]";
            }
            var ab = a.Economy.Buildings.OrderBy(x => x.Id).ToArray();
            var bb = b.Economy.Buildings.OrderBy(x => x.Id).ToArray();
            if (ab.Length != bb.Length) return "Buildings.Count";
            for (int i = 0; i < ab.Length; i++)
            {
                if (ab[i].Kind != bb[i].Kind || !Same(ab[i].Center, Mirror(bb[i].Center, scenario)))
                    return "Buildings[" + ab[i].Id + "].Center";
                if (ab[i].Complete != bb[i].Complete || ab[i].Progress != bb[i].Progress || ab[i].Work != bb[i].Work)
                    return "Buildings[" + ab[i].Id + "].Progress";
            }
            return a.Commands.Count == b.Commands.Count ? null : "Commands.Count";
        }

        private static SimPoint Mirror(SimPoint point, ScenarioDefinition scenario)
            => new SimPoint(Fix64.FromInt(scenario.Map.WidthCells * scenario.Map.CellSizeMeters) - point.X,
                Fix64.FromInt(scenario.Map.HeightCells * scenario.Map.CellSizeMeters) - point.Z);

        private static void Rotate(ScenarioDefinition scenario)
        {
            int cells = scenario.Map.WidthCells * scenario.Map.HeightCells;
            var width = Fix64.FromInt(scenario.Map.WidthCells * scenario.Map.CellSizeMeters);
            var height = Fix64.FromInt(scenario.Map.HeightCells * scenario.Map.CellSizeMeters);
            SimPoint R(SimPoint p) => new SimPoint(width - p.X, height - p.Z);
            if (scenario.Map.Terrain != null && scenario.Map.Terrain.Length == cells)
            {
                var terrain = new byte[cells];
                for (int c = 0; c < cells; c++) terrain[cells - 1 - c] = scenario.Map.Terrain[c];
                scenario.Map.Terrain = terrain;
            }
            if (scenario.Map.BlockedCellIds != null)
            {
                var blocked = scenario.Map.BlockedCellIds.Select(c => cells - 1 - c).ToArray();
                Array.Sort(blocked);
                scenario.Map.BlockedCellIds = blocked;
            }
            for (int i = 0; i < scenario.Cores.Length; i++) { var core = scenario.Cores[i]; core.Position = R(core.Position); scenario.Cores[i] = core; }
            for (int i = 0; i < scenario.Outposts.Length; i++) { var outpost = scenario.Outposts[i]; outpost.Position = R(outpost.Position); scenario.Outposts[i] = outpost; }
            for (int i = 0; i < scenario.Soldiers.Length; i++) scenario.Soldiers[i].Position = R(scenario.Soldiers[i].Position);
            for (int i = 0; i < scenario.Villagers.Length; i++) scenario.Villagers[i].Position = R(scenario.Villagers[i].Position);
            for (int i = 0; i < scenario.ResourceNodes.Length; i++) scenario.ResourceNodes[i].Position = R(scenario.ResourceNodes[i].Position);
            // C9 (Neutrals): the camps are placed in the scenario too and turn with the map.
            for (int i = 0; i < scenario.NeutralRules.Camps.Length; i++) scenario.NeutralRules.Camps[i].Position = R(scenario.NeutralRules.Camps[i].Position);
        }

        private static bool Same(SimPoint a, SimPoint b) => a.X.Raw == b.X.Raw && a.Z.Raw == b.Z.Raw;

    }
}
