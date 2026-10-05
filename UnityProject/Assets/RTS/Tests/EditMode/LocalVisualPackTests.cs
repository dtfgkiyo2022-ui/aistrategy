using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

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
    }
}
