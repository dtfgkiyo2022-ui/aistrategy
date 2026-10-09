using System.Collections.Generic;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Core.Tests
{
    public sealed class EconomyPresentationToolsTests
    {
        [Test]
        public void Blueprint_RoundTripsVersionedJson()
        {
            var source = new BlueprintDocument { Name = "精錬ライン", Width = 5, Height = 3 };
            source.Buildings.Add(new BlueprintBuilding(BuildingKind.Smelter, 1, 0, 2, 2, Facing.East));
            source.Belts.Add(new BlueprintBelt(0, 2, Facing.North));

            var loaded = BlueprintTools.Deserialize(BlueprintTools.Serialize(source));

            Assert.That(loaded.Version, Is.EqualTo(BlueprintDocument.CurrentVersion));
            Assert.That(loaded.Name, Is.EqualTo(source.Name));
            Assert.That(loaded.Width, Is.EqualTo(5));
            Assert.That(loaded.Height, Is.EqualTo(3));
            Assert.That(loaded.Buildings[0].Kind, Is.EqualTo(BuildingKind.Smelter));
            Assert.That(loaded.Buildings[0].Facing, Is.EqualTo(Facing.East));
            Assert.That(loaded.Belts[0].X, Is.EqualTo(0));
            Assert.That(loaded.Belts[0].Z, Is.EqualTo(2));
        }

        [Test]
        public void Blueprint_V1LoadsAsV2AndNewBeltFieldsKeepTheirValues()
        {
            var old = BlueprintTools.Deserialize("{\"version\":1,\"name\":\"old\",\"width\":2,\"height\":1,\"buildings\":[],\"belts\":[{\"x\":0,\"z\":0,\"facing\":1}]}");
            Assert.That(old.Version, Is.EqualTo(BlueprintDocument.CurrentVersion));
            Assert.That(old.Belts[0].Component, Is.EqualTo(BeltComponentKind.None));

            var source = new BlueprintDocument { Width = 4, Height = 2 };
            source.Belts.Add(new BlueprintBelt(1, 0, Facing.East, BeltComponentKind.Sorter, ResourceKind.Ore, -1, BeltSpeed.Fast));
            var loaded = BlueprintTools.Deserialize(BlueprintTools.Serialize(source));
            Assert.That(loaded.Belts[0].Component, Is.EqualTo(BeltComponentKind.Sorter));
            Assert.That(loaded.Belts[0].SorterKind, Is.EqualTo(ResourceKind.Ore));
            Assert.That(loaded.Belts[0].Speed, Is.EqualTo(BeltSpeed.Fast));
        }

        [Test]
        public void Blueprint_FourRotationsReturnToOriginal()
        {
            var source = new BlueprintDocument { Name = "L", Width = 4, Height = 2 };
            source.Buildings.Add(new BlueprintBuilding(BuildingKind.Mine, 1, 0, 2, 1, Facing.South));
            source.Belts.Add(new BlueprintBelt(0, 1, Facing.West));

            var rotated = BlueprintTools.Rotate(source, 4);

            Assert.That(BlueprintTools.Serialize(rotated), Is.EqualTo(BlueprintTools.Serialize(source)));
        }

        [Test]
        public void Blueprint_SplitsBeltRunsAtMaxRun()
        {
            var belts = new List<BlueprintBelt>();
            for (int i = 0; i < 7; i++) belts.Add(new BlueprintBelt(i, 0, Facing.East));

            var runs = BlueprintTools.SplitBeltRuns(belts, 3);

            Assert.That(runs.Count, Is.EqualTo(3));
            Assert.That(runs[0].Count, Is.EqualTo(3));
            Assert.That(runs[1].Count, Is.EqualTo(3));
            Assert.That(runs[2].Count, Is.EqualTo(1));
        }

        [Test]
        public void FlowMetrics_UsesFrameDifferencesAndRollingWindow()
        {
            var metrics = new EconomyFlowMetrics();
            var first = new[] { new BuildingView(7, 1, BuildingKind.Smelter, default(SimPoint), 2, 1, 1, true, 10, 20, 0, 0) };
            var second = new[] { new BuildingView(7, 1, BuildingKind.Smelter, default(SimPoint), 2, 1, 1, true, 11, 20, 0, 0) };

            metrics.ObserveBuildings(0, first, 1);
            metrics.ObserveBuildings(1, second, 1);
            Assert.That(metrics.BuildingUtilization(7), Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void FlowMetrics_CountsItemsLeavingAnEndCell()
        {
            var metrics = new EconomyFlowMetrics();
            var end = new[] { 12 };
            metrics.ObserveBeltEnds(0, new[] { new BeltView(12, 1, Facing.East, ResourceKind.Ore, 8) }, end, 1);
            metrics.ObserveBeltEnds(1, new BeltView[0], end, 1);

            Assert.That(metrics.BeltDeliveriesPerMinute(12), Is.EqualTo(1));
        }
    }
}
