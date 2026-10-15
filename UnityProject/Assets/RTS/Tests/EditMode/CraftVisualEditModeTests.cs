using NUnit.Framework;
using System.Collections.Generic;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class CraftVisualEditModeTests
    {
        [Test]
        public void GoodsIconsReturnAnIconForEveryResourceKind()
        {
            foreach (ResourceKind kind in System.Enum.GetValues(typeof(ResourceKind)))
                Assert.That(GoodsIcons.Get(kind), Is.Not.Null, kind + ".png がありません");
        }

        [Test]
        public void CargoVisualTableCoversEveryResourceKind()
        {
            Assert.That(GoodsCargoVisuals.HasAllKinds, Is.True);
            foreach (ResourceKind kind in System.Enum.GetValues(typeof(ResourceKind)))
                Assert.That(GoodsCargoVisuals.Get(kind).Color.a, Is.GreaterThan(0f), kind + " の荷の色がありません");
        }

        [Test]
        public void CraftBuildingStatesUseThePublishedBuildingFields()
        {
            var constructing = Building(complete: false, input: 0, output: 0);
            var waiting = Building(complete: true, input: 0, output: 0);
            var working = Building(complete: true, input: 1, output: 0);
            var blocked = Building(complete: true, input: 1, output: CraftVisualRules.OutputBufferLimit);
            Assert.That(CraftVisualRules.Evaluate(constructing), Is.EqualTo(CraftBuildingState.Constructing));
            Assert.That(CraftVisualRules.Evaluate(waiting), Is.EqualTo(CraftBuildingState.WaitingForMaterial));
            Assert.That(CraftVisualRules.Evaluate(working), Is.EqualTo(CraftBuildingState.Working));
            Assert.That(CraftVisualRules.Evaluate(blocked), Is.EqualTo(CraftBuildingState.OutputBlocked));
            Assert.That(CraftVisualRules.IsStockFull(10, 10), Is.True);
            Assert.That(CraftVisualRules.IsStockFull(9, 10), Is.False);
        }

        [Test]
        public void BeltTraversalFollowsSplitterSorterAndUndergroundPair()
        {
            var belts = new[]
            {
                new BeltView(10, 1, Facing.East, ResourceKind.Ore, 0),
                new BeltView(11, 1, Facing.East, ResourceKind.Ore, 0, false, BeltComponentKind.Splitter, 0, -1, BeltSpeed.Normal),
                new BeltView(12, 1, Facing.East, ResourceKind.Ore, 0, false, BeltComponentKind.Sorter, ResourceKind.Ore, -1, BeltSpeed.Normal),
                new BeltView(13, 1, Facing.East, ResourceKind.Ore, 0),
                new BeltView(139, 1, Facing.North, ResourceKind.Ore, 0, false, BeltComponentKind.UndergroundEntrance, 0, 267, BeltSpeed.Normal),
                new BeltView(267, 1, Facing.North, ResourceKind.Ore, 0, false, BeltComponentKind.UndergroundExit, 0, 139, BeltSpeed.Normal)
            };
            var cells = new HashSet<int>();
            CraftLineTraversal.CollectConnectedBeltCells(belts, 10, 128, 64, cells);
            Assert.That(cells, Does.Contain(10));
            Assert.That(cells, Does.Contain(12), "仕分け前の直線");
            Assert.That(cells, Does.Contain(13), "仕分けの通常出口");
            Assert.That(cells, Does.Contain(139), "分岐出口");
            Assert.That(cells, Does.Contain(267), "地下ベルトの対向口");
        }

        [Test]
        public void FlowMetricsSummarizeProducedAndConsumedFromBufferDifferences()
        {
            var metrics = new EconomyFlowMetrics();
            var first = Building(complete: true, input: 3, output: 0);
            var second = Building(complete: true, input: 1, output: 2);
            metrics.ObserveBuildings(0, new[] { first }, 1);
            metrics.ObserveBuildings(1, new[] { second }, 1);
            metrics.ObserveBuildings(EconomyFlowMetrics.WindowTicks, new[] { Building(complete: true, input: 0, output: 3) }, 1);
            var metal = metrics.GetCurrentMinute(ResourceKind.Metal);
            var ore = metrics.GetCurrentMinute(ResourceKind.Ore);
            Assert.That(metal.Produced, Is.EqualTo(1));
            Assert.That(ore.Consumed, Is.EqualTo(1));
            var history = new List<FlowMinuteSample>();
            metrics.GetHistory(ResourceKind.Metal, history);
            Assert.That(history.Count, Is.EqualTo(10));
            Assert.That(history[8].Produced, Is.EqualTo(2));
            Assert.That(history[9].Produced, Is.EqualTo(1));
        }

        [Test]
        public void DetailModeToggleChangesOnlyThePassedMatchState()
        {
            bool off = false;
            bool on = CraftDisplayMode.Toggle(off);
            Assert.That(on, Is.True);
            Assert.That(CraftDisplayMode.Toggle(on), Is.False);
            Assert.That(CraftDisplayMode.Toggle(false), Is.True, "PlayerPrefs を使わず試合状態だけを反転");
        }

        private static BuildingView Building(bool complete, int input, int output)
        {
            return new BuildingView(1, 1, BuildingKind.Smelter, default(SimPoint), 2, 100, 100,
                complete, 0, 0, 0, 0, Facing.North, input, output);
        }
    }
}
