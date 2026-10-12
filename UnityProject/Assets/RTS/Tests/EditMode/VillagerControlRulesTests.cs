using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class VillagerControlRulesTests
    {
        [Test]
        public void BoxWithOwnSoldierDoesNotSelectVillagers()
        {
            var points = new[]
            {
                new VillagerControlRules.ScreenPoint(10, true, false, 4, 4),
                new VillagerControlRules.ScreenPoint(20, true, true, 8, 8),
                new VillagerControlRules.ScreenPoint(30, false, true, 6, 6)
            };

            var selected = VillagerControlRules.SelectBox(points, new VillagerControlRules.ScreenRect(0, 0, 10, 10));

            Assert.That(selected, Is.Empty);
        }

        [Test]
        public void BoxWithoutOwnSoldierSelectsOwnVillagersOnly()
        {
            var points = new[]
            {
                new VillagerControlRules.ScreenPoint(30, false, false, 4, 4),
                new VillagerControlRules.ScreenPoint(20, true, false, 8, 8),
                new VillagerControlRules.ScreenPoint(10, true, false, 2, 2),
                new VillagerControlRules.ScreenPoint(40, true, true, 20, 20)
            };

            var selected = VillagerControlRules.SelectBox(points, new VillagerControlRules.ScreenRect(0, 0, 10, 10));

            Assert.That(selected, Is.EqualTo(new[] { 10u, 20u }));
        }

        [Test]
        public void ShiftClickAddsAndRemoves()
        {
            var added = VillagerControlRules.ApplyClick(new[] { 1u, 2u }, 3, true);
            var removed = VillagerControlRules.ApplyClick(added, 2, true);

            Assert.That(added, Is.EqualTo(new[] { 1u, 2u, 3u }));
            Assert.That(removed, Is.EqualTo(new[] { 1u, 3u }));
        }

        [TestCase(VillagerControlRules.TargetKind.ResourceNode, VillagerControlRules.OrderKind.Gather)]
        [TestCase(VillagerControlRules.TargetKind.Construction, VillagerControlRules.OrderKind.Build)]
        [TestCase(VillagerControlRules.TargetKind.HaulSource, VillagerControlRules.OrderKind.Haul)]
        [TestCase(VillagerControlRules.TargetKind.Market, VillagerControlRules.OrderKind.TradeRoute)]
        [TestCase(VillagerControlRules.TargetKind.Caravanserai, VillagerControlRules.OrderKind.CaravanRoute)]
        [TestCase(VillagerControlRules.TargetKind.Ground, VillagerControlRules.OrderKind.None)]
        public void TargetMapsToExpectedOrder(VillagerControlRules.TargetKind target, VillagerControlRules.OrderKind order)
        {
            Assert.That(VillagerControlRules.ResolveOrder(new VillagerControlRules.Target(target, 1)), Is.EqualTo(order));
        }

        [TestCase(VillagerControlRules.TargetKind.ResourceNode, EconomyCommandKind.AssignVillagers)]
        [TestCase(VillagerControlRules.TargetKind.Construction, EconomyCommandKind.AssignVillagers)]
        [TestCase(VillagerControlRules.TargetKind.HaulSource, EconomyCommandKind.AssignVillagers)]
        [TestCase(VillagerControlRules.TargetKind.Market, EconomyCommandKind.TradeRoute)]
        [TestCase(VillagerControlRules.TargetKind.Caravanserai, EconomyCommandKind.CaravanRoute)]
        [TestCase(VillagerControlRules.TargetKind.Ground, (EconomyCommandKind)0)]
        public void TargetMapsToExpectedContractCommand(VillagerControlRules.TargetKind target, EconomyCommandKind command)
        {
            Assert.That(VillagerControlRules.ResolveCommandKind(new VillagerControlRules.Target(target, 1)), Is.EqualTo(command));
        }

        [Test]
        public void MissingVillagerIsNotKeptByClickSelection()
        {
            var selected = VillagerControlRules.PruneMissing(new[] { 7u, 9u }, new[] { 9u });

            Assert.That(selected, Is.EqualTo(new[] { 9u }));
        }

        [Test]
        public void HaulSourcesMatchTheExistingAssignRule()
        {
            Assert.That(VillagerControlRules.IsHaulSource(BuildingKind.Mine), Is.True);
            Assert.That(VillagerControlRules.IsHaulSource(BuildingKind.Smelter), Is.True);
            Assert.That(VillagerControlRules.IsHaulSource(BuildingKind.Market), Is.False);
        }

        [Test]
        public void CaravanNeedsFinishedLinkedHost()
        {
            Assert.That(VillagerControlRules.CanUseCaravanserai(false, 1, 1), Is.False);
            Assert.That(VillagerControlRules.CanUseCaravanserai(true, 0, 1), Is.False);
            Assert.That(VillagerControlRules.CanUseCaravanserai(true, 1, 1), Is.True);
        }
    }
}
