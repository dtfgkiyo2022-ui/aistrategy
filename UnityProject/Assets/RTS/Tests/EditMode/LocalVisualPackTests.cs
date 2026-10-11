using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;
using UnityEngine;

namespace Rts.Tests.EditMode
{
    public sealed class LocalVisualPackTests
    {
        [Test]
        public void EveryUnitKindHasAPackPath()
        {
            foreach (UnitKind kind in Enum.GetValues(typeof(UnitKind)))
            {
                var path = LocalVisualPack.UnitAssetPath(kind);
                Assert.IsNotNull(path, kind + " must have a model or an explicit pack mapping");
                Assert.IsTrue(path.StartsWith("Assets/ThirdParty/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/prefabs/", StringComparison.Ordinal));
                Assert.IsTrue(path.EndsWith(".prefab", StringComparison.Ordinal));
            }
        }

        [Test]
        public void BuildingPathsAreWellFormedAndUnsupportedKindsStayPlaceholder()
        {
            var supported = new HashSet<string>();
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
            {
                var path = LocalVisualPack.BuildingAssetPath(kind);
                if (path == null)
                {
                    Assert.AreEqual(0f, LocalVisualPack.BuildingHeight(kind));
                    continue;
                }
                Assert.IsTrue(path.StartsWith("Assets/ThirdParty/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/models/buildings/", StringComparison.Ordinal));
                Assert.IsTrue(path.EndsWith(".FBX", StringComparison.Ordinal));
                Assert.IsTrue(LocalVisualPack.BuildingHeight(kind) > 0f);
                Assert.Greater(LocalVisualPack.BuildingWidthScale(kind), 0f);
                // Several intentional aliases are part of the design (e.g. Smelter/Steelworks use Blacksmith).
                supported.Add(path);
            }
            Assert.Greater(supported.Count, 0);
        }

        [Test]
        public void EveryBuildingExceptBridgeHasAPackMapping()
        {
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
            {
                if (kind == BuildingKind.Bridge) continue;
                Assert.IsNotNull(LocalVisualPack.BuildingAssetPath(kind), kind + " must have a model mapping");
                Assert.Greater(LocalVisualPack.BuildingHeight(kind), 0f);
            }
            Assert.IsNull(LocalVisualPack.BuildingAssetPath(BuildingKind.Bridge));
        }

        [Test]
        public void MissingPackReportsFalseAndLeavesFallbackToCaller()
        {
            LocalVisualPack.Disabled = true;
            try
            {
                foreach (UnitKind kind in Enum.GetValues(typeof(UnitKind))) Assert.IsFalse(LocalVisualPack.HasUnit(kind));
                foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind))) Assert.IsFalse(LocalVisualPack.HasBuilding(kind));
                Assert.IsFalse(LocalVisualPack.HasCore());
                Assert.IsFalse(LocalVisualPack.HasOutpost());
            }
            finally
            {
                LocalVisualPack.Disabled = false;
            }
        }

        [Test]
        public void EveryUnitKindHasWellFormedAnimationPaths()
        {
            foreach (UnitKind kind in Enum.GetValues(typeof(UnitKind)))
            {
                foreach (LocalVisualPack.UnitMotion motion in Enum.GetValues(typeof(LocalVisualPack.UnitMotion)))
                {
                    var path = LocalVisualPack.AnimationAssetPath(kind, motion);
                    Assert.IsNotNull(path, kind + " must have an animation family");
                    Assert.IsTrue(path.StartsWith("Assets/ThirdParty/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/animation/", StringComparison.Ordinal));
                    Assert.IsTrue(path.EndsWith(".FBX", StringComparison.Ordinal));
                    Assert.IsFalse(path.Contains("_rm", StringComparison.Ordinal));
                }
            }
        }

        [Test]
        public void EveryCivilisationHasThreeInfantryStagesAndAnimations()
        {
            foreach (CivKind civ in Enum.GetValues(typeof(CivKind)))
            {
                for (int stage = 1; stage <= 3; stage++)
                {
                    var model = LocalVisualPack.InfantryAssetPath(civ, stage);
                    Assert.IsNotNull(model, civ + " stage " + stage + " must have an infantry model");
                    Assert.IsTrue(model.StartsWith("Assets/ThirdParty/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/prefabs/", StringComparison.Ordinal));
                    Assert.IsTrue(model.EndsWith(".prefab", StringComparison.Ordinal));
                    foreach (LocalVisualPack.UnitMotion motion in Enum.GetValues(typeof(LocalVisualPack.UnitMotion)))
                    {
                        var animation = LocalVisualPack.InfantryAnimationAssetPath(civ, stage, motion);
                        Assert.IsNotNull(animation, civ + " stage " + stage + " must have motion " + motion);
                        Assert.IsTrue(animation.StartsWith("Assets/ThirdParty/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/animation/", StringComparison.Ordinal));
                        Assert.IsTrue(animation.EndsWith(".FBX", StringComparison.Ordinal));
                    }
                }
            }
        }

        [Test]
        public void EquipmentStageUsesResearchBoundaries()
        {
            Assert.AreEqual(1, LocalVisualPack.EquipmentStage(0UL));
            Assert.AreEqual(2, LocalVisualPack.EquipmentStage(1UL << ((int)TechKind.Weapons - 1)));
            Assert.AreEqual(2, LocalVisualPack.EquipmentStage(1UL << ((int)TechKind.Armour - 1)));
            Assert.AreEqual(3, LocalVisualPack.EquipmentStage(
                (1UL << ((int)TechKind.Weapons - 1)) | (1UL << ((int)TechKind.Armour - 1))));
            Assert.AreEqual(1, LocalVisualPack.EquipmentStage(
                (1UL << ((int)TechKind.Tools - 1)) | (1UL << ((int)TechKind.Carts - 1))));
        }

        [Test]
        public void BannerPathsHaveTwelveColours()
        {
            for (int index = 0; index < 12; index++)
            {
                var path = LocalVisualPack.BannerAssetPath(index);
                Assert.IsNotNull(path);
                Assert.IsTrue(path.StartsWith("Assets/ThirdParty/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/prefabs/banners/", StringComparison.Ordinal));
                Assert.IsTrue(path.EndsWith(".prefab", StringComparison.Ordinal));
            }
            Assert.IsNull(LocalVisualPack.BannerAssetPath(-1));
            Assert.IsNull(LocalVisualPack.BannerAssetPath(12));
        }

        [Test]
        public void MissingPackReportsFalseForTieredModelsAndBanners()
        {
            LocalVisualPack.Disabled = true;
            try
            {
                Assert.IsFalse(LocalVisualPack.HasBanner(0));
                Assert.IsFalse(LocalVisualPack.HasBanner(11));
                var parent = new GameObject("TieredVisualPackTests");
                try
                {
                    GameObject instance;
                    bool tiered;
                    Assert.IsFalse(LocalVisualPack.TryCreateDisplayUnit(UnitKind.Infantry, CivKind.Agrarian, 2,
                        true, parent.transform, out instance, out tiered));
                    Assert.IsFalse(LocalVisualPack.TryCreateBanner(0, parent.transform, out instance));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(parent);
                }
            }
            finally
            {
                LocalVisualPack.Disabled = false;
            }
        }

        [Test]
        public void MissingPackDoesNotCreateAnimationHandle()
        {
            LocalVisualPack.Disabled = true;
            try
            {
                Assert.IsFalse(LocalVisualPack.HasUnit(UnitKind.Infantry));
            }
            finally
            {
                LocalVisualPack.Disabled = false;
            }
        }

        [Test]
        public void MissingManifestFallsBackWithoutThrowing()
        {
            if (Resources.Load<LocalVisualPackManifest>(LocalVisualPack.ManifestResourceName) != null)
                Assert.Ignore("生成済みの素材パック一覧がある環境では、素材ありの経路を確認します。");
            // In the editor the pack is read straight from Assets/ThirdParty, so a PC that has it never falls back.
            if (System.IO.Directory.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath, "ThirdParty", "ToonyTinyPeople")))
                Assert.Ignore("素材パックがある PC では、仮の形に戻る経路は確かめられません。");

            var parent = new GameObject("LocalVisualPackTests");
            try
            {
                GameObject instance;
                float height;
                Assert.DoesNotThrow(() =>
                {
                    Assert.IsFalse(LocalVisualPack.TryCreateUnit(UnitKind.Infantry, true, parent.transform, out instance));
                    Assert.IsFalse(LocalVisualPack.TryCreateBuilding(BuildingKind.Barracks, 1u, parent.transform, 4f, out instance));
                    Assert.IsFalse(LocalVisualPack.TryCreateCore(true, parent.transform, out instance, out height));
                    Assert.IsFalse(LocalVisualPack.TryCreateOutpost(parent.transform, out instance, out height));
                    Assert.IsFalse(LocalVisualPack.HasUnit(UnitKind.Infantry));
                    Assert.IsFalse(LocalVisualPack.HasBuilding(BuildingKind.Barracks));
                    Assert.IsFalse(LocalVisualPack.HasCore());
                    Assert.IsFalse(LocalVisualPack.HasOutpost());
                });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void BuildingConstructionPathsFollowFinishedModelNames()
        {
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
            {
                var finished = LocalVisualPack.BuildingAssetPath(kind);
                if (finished == null) continue;
                for (int stage = 0; stage < 2; stage++)
                {
                    var path = LocalVisualPack.BuildingConstructionAssetPath(kind, stage);
                    Assert.IsNotNull(path);
                    Assert.IsTrue(path.Contains("/models/buildings/construction/", StringComparison.Ordinal));
                    Assert.IsTrue(path.EndsWith("_" + stage + ".FBX", StringComparison.Ordinal));
                }
            }
        }

        [Test]
        public void BuildingConstructionStageUsesHalfwayBoundaries()
        {
            Assert.AreEqual(LocalVisualPack.BuildingConstructionStage.Base,
                LocalVisualPack.GetBuildingConstructionStage(false, 49, 100));
            Assert.AreEqual(LocalVisualPack.BuildingConstructionStage.Frame,
                LocalVisualPack.GetBuildingConstructionStage(false, 50, 100));
            Assert.AreEqual(LocalVisualPack.BuildingConstructionStage.Finished,
                LocalVisualPack.GetBuildingConstructionStage(true, 100, 100));
        }

        [Test]
        public void MissingPackReportsFalseForEffects()
        {
            LocalVisualPack.Disabled = true;
            try
            {
                Assert.IsFalse(LocalVisualPack.HasEffect("FX_Building_burning_small.prefab"));
            }
            finally
            {
                LocalVisualPack.Disabled = false;
            }
        }
    }
}
