using System;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Core.Tests
{
    public sealed class AdjacentBuildingsEditModeTests
    {
        private const int Width = 12;
        private const int Height = 12;

        [Test]
        public void OverlapIsRejectedAndAdjacentFootprintIsAllowedWhenEnabled()
        {
            var economy = CreateEconomy(true, 1);
            int existing = 5 * Width + 5;

            Assert.That(Can(economy, BuildingKind.Barracks, existing, 2, 1), Is.False);
            Assert.That(Can(economy, BuildingKind.Barracks, 5 * Width + 7, 2, 1), Is.True);
            Assert.That(Can(economy, BuildingKind.Barracks, 5 * Width + 8, 2, 1), Is.True);
        }

        [Test]
        public void AdjacentFootprintIsRejectedWhenFeatureIsDisabled()
        {
            var economy = CreateEconomy(false, 2);
            Assert.That(Can(economy, BuildingKind.Barracks, 5 * Width + 7, 2, 1), Is.False);
        }

        [Test]
        public void WoodShortageIsRejectedBeforeGeometry()
        {
            var economy = CreateEconomy(true, 1, wood: 0);
            Assert.That(Can(economy, BuildingKind.Barracks, 1 * Width + 1, 2, 1), Is.False);
        }

        [Test]
        public void CoreClearanceIsRejected()
        {
            var economy = CreateEconomy(true, 1, includeBuilding: false);
            var core = new KnownObjective(GoalKind.Core, 1,
                new SimPoint(Fix64.FromInt(1), Fix64.FromInt(1)), true, 1, true, 100, 0);

            Assert.That(AdjacentBuildingPlacementRules.CanPlace(economy, new[] { core }, 1,
                BuildingKind.Barracks, 1 * Width + 1, 2, Facing.North, 1, 0), Is.False);
        }

        [Test]
        public void BuildingsWithTheirOwnSiteRuleOnlyCheckTheCost()
        {
            // A mine goes on its ore node, which the general rule keeps clear; it must not be painted red there.
            var economy = CreateEconomy(true, 1);
            Assert.That(Can(economy, BuildingKind.Mine, 5 * Width + 5, 2, 1), Is.True);
            Assert.That(Can(CreateEconomy(true, 1, wood: 0), BuildingKind.Mine, 5 * Width + 5, 2, 1), Is.False);
        }

        private static EconomyView CreateEconomy(bool enabled, int clearance, int wood = 100,
            bool includeBuilding = true)
        {
            var buildings = includeBuilding
                ? new[] { new BuildingView(1, 1, BuildingKind.Barracks,
                    new SimPoint(Fix64.FromInt(12), Fix64.FromInt(12)), 4, 1, 1,
                    true, 1, 1, 0, 0, Facing.North, 0, 0) }
                : Array.Empty<BuildingView>();
            var passable = new bool[Width * Height];
            var river = new bool[Width * Height];
            for (int i = 0; i < passable.Length; i++) passable[i] = true;
            return new EconomyView(wood, 100, enabled, clearance, 4, 2, Width, Height, 2,
                buildings, Array.Empty<ResourceView>(), Array.Empty<BeltView>(), passable, river);
        }

        private static bool Can(EconomyView economy, BuildingKind kind, int origin, int size, int wood)
            => AdjacentBuildingPlacementRules.CanPlace(economy, Array.Empty<KnownObjective>(), 1,
                kind, origin, size, Facing.North, wood, 0);
    }
}
