using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class TackEditModeTests
    {
        [Test]
        public void HeavyCavalryUsesNamedModelAnimationsAndSaddleryModel()
        {
            Assert.AreEqual("重騎兵", BattlefieldView.UnitName(UnitKind.HeavyCavalry));
            Assert.That(LocalVisualPack.UnitAssetPath(UnitKind.HeavyCavalry), Does.EndWith("TT_Mounted_Paladin.prefab"));
            Assert.That(LocalVisualPack.BuildingAssetPath(BuildingKind.Saddlery), Does.EndWith("Workshop.FBX"));
            for (int stage = 1; stage <= 3; stage++)
            {
                Assert.That(LocalVisualPack.TieredUnitAssetPath(UnitKind.HeavyCavalry, stage), Does.EndWith(stage == 3 ? "TT_Mounted_Paladin.prefab" : stage == 2 ? "TT_Mounted_Knight.prefab" : "TT_Heavy_Cavalry.prefab"));
                Assert.IsNotNull(LocalVisualPack.TieredUnitAnimationAssetPath(UnitKind.HeavyCavalry, stage, LocalVisualPack.UnitMotion.Idle));
                Assert.IsNotNull(LocalVisualPack.TieredUnitAnimationAssetPath(UnitKind.HeavyCavalry, stage, LocalVisualPack.UnitMotion.Walk));
                Assert.IsNotNull(LocalVisualPack.TieredUnitAnimationAssetPath(UnitKind.HeavyCavalry, stage, LocalVisualPack.UnitMotion.Attack));
            }
        }

        [Test]
        public void CavalryTopBarShowsTackAndSelectionNamesRemainJapanese()
        {
            var scenario = MapGenerator.GenerateTerrain(4321UL, false, true);
            scenario.Economy.Enabled = true;
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.Tack = true;
            scenario.Economy.Cavalry = true;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            var simulation = new Rts.Simulation.Simulation(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Cavalry));
            gateway.Step();
            gateway.Step();

            var economy = simulation.Capture(1).Economy;
            Assert.IsTrue(economy.TackEnabled);
            Assert.IsTrue(TopBarResourceVisibility.ShowTack(economy));
            var entries = new List<TopBarResourceVisibility.ResourceEntry>();
            TopBarResourceVisibility.Fill(economy, entries);
            Assert.IsTrue(entries.Exists(entry => entry.Kind == TopBarResourceVisibility.ResourceKind.Tack && entry.Label == "馬具"));
            Assert.AreEqual("重騎兵", BattlefieldView.UnitName(UnitKind.HeavyCavalry));
        }
    }
}
