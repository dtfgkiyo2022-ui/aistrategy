using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;
using UnityEngine;

namespace Rts.Tests.EditMode
{
    public sealed class SoundEditModeTests
    {
        [Test]
        public void FrameComparisonReportsHpLossAndUnitRemoval()
        {
            var before = Frame(new RenderUnit(1, true, UnitKind.Infantry, default(SimPoint), false, false, false, true, 10));
            var after = Frame(new RenderUnit(1, true, UnitKind.Infantry, default(SimPoint), false, false, false, true, 8));
            var events = new List<FrameSoundEvent>();
            FrameSoundEvents.Compare(before, after, 1, events);
            Assert.AreEqual(SoundCue.MeleeHit, events[0].Cue);

            FrameSoundEvents.Compare(after, Frame(), 1, events);
            Assert.AreEqual(SoundCue.UnitDeath, events[0].Cue);
        }

        [Test]
        public void UnchangedFrameReportsNothing()
        {
            var frame = Frame(new RenderUnit(1, true, UnitKind.Infantry, default(SimPoint), false, false, false, true, 10));
            var events = new List<FrameSoundEvent>();
            FrameSoundEvents.Compare(frame, frame, 1, events);
            Assert.IsEmpty(events);
        }

        [Test]
        public void BuildingCompletionIsReported()
        {
            var beforeBuilding = new BuildingView(4, 1, BuildingKind.House, default(SimPoint), 2, 1, 10, false, 5, 5, 0);
            var afterBuilding = new BuildingView(4, 1, BuildingKind.House, default(SimPoint), 2, 10, 10, true, 10, 10, 0);
            var before = EconomyFrame(beforeBuilding);
            var after = EconomyFrame(afterBuilding);
            var events = new List<FrameSoundEvent>();
            FrameSoundEvents.Compare(before, after, 1, events);
            Assert.IsTrue(events.Exists(item => item.Cue == SoundCue.ConstructionCompleted));
        }

        [Test]
        public void EmptySoundTableIsSafe()
        {
            Assert.DoesNotThrow(() => LocalSoundPack.Load(SoundCue.Click));
        }

        [Test]
        public void CombatUsesVisibleAttackerKindAndEnemyDeathIsAudible()
        {
            var before = Frame(
                new RenderUnit(1, true, UnitKind.Infantry, default(SimPoint), false, false, false, true, 10),
                new RenderUnit(2, false, UnitKind.Archer, default(SimPoint), false, false, false, false, 0),
                new RenderUnit(3, false, UnitKind.Infantry, default(SimPoint), false, false, false, false, 0));
            var after = Frame(
                new RenderUnit(1, true, UnitKind.Infantry, default(SimPoint), false, false, false, true, 8),
                new RenderUnit(2, false, UnitKind.Archer, default(SimPoint), false, true, false, false, 0));
            var events = new List<FrameSoundEvent>();
            FrameSoundEvents.Compare(before, after, 1, events);
            Assert.IsTrue(events.Exists(item => item.Cue == SoundCue.ArrowHit));
            Assert.IsTrue(events.Exists(item => item.Cue == SoundCue.ArrowRelease));
            Assert.IsTrue(events.Exists(item => item.Cue == SoundCue.UnitDeath));
        }

        [Test]
        public void OwnVillagerDamageIsReportedAsAnAttackNotice()
        {
            var beforeVillager = new VillagerView(7, true, default(SimPoint), VillagerActivity.Idle, ResourceKind.Food, 0, 10);
            var afterVillager = new VillagerView(7, true, default(SimPoint), VillagerActivity.Idle, ResourceKind.Food, 0, 9);
            var before = EconomyFrame(null, beforeVillager);
            var after = EconomyFrame(null, afterVillager);
            var events = new List<FrameSoundEvent>();
            FrameSoundEvents.Compare(before, after, 1, events);
            Assert.IsTrue(events.Exists(item => item.Cue == SoundCue.UnderAttackVillager));
        }

        [Test]
        public void ProcessingOutputAndResourceDropAreReported()
        {
            var beforeBuilding = new BuildingView(4, 1, BuildingKind.Smelter, default(SimPoint), 2, 10, 10, true, 10, 10, 0, 0, Facing.North, 0, 1);
            var afterBuilding = new BuildingView(4, 1, BuildingKind.Smelter, default(SimPoint), 2, 10, 10, true, 10, 10, 0, 0, Facing.North, 0, 2);
            var beforeVillager = new VillagerView(7, true, default(SimPoint), VillagerActivity.Returning, ResourceKind.Ore, 3, 10);
            var afterVillager = new VillagerView(7, true, default(SimPoint), VillagerActivity.Idle, ResourceKind.Ore, 0, 10);
            var before = EconomyFrame(beforeBuilding, beforeVillager);
            var after = EconomyFrame(afterBuilding, afterVillager);
            var events = new List<FrameSoundEvent>();
            FrameSoundEvents.Compare(before, after, 1, events);
            Assert.IsTrue(events.Exists(item => item.Cue == SoundCue.ProcessingOutput));
            Assert.IsTrue(events.Exists(item => item.Cue == SoundCue.ResourceDrop));
        }

        [Test]
        public void VolumeSettingsRoundTripThroughPlayerPrefs()
        {
            float old = GameAudio.MasterVolume;
            try { GameAudio.MasterVolume = 0.37f; Assert.AreEqual(0.37f, GameAudio.MasterVolume, 0.0001f); }
            finally { GameAudio.MasterVolume = old; }
        }

        private static FactionFrame Frame(params RenderUnit[] units)
        {
            return new FactionFrame(0, 1, units, null, Array.Empty<CommandView>(), Array.Empty<GameEvent>(), null,
                new MatchResult(false, 0, false, false, true));
        }

        private static FactionFrame EconomyFrame(BuildingView building)
        {
            var economy = new EconomyView(0, 0, 0, 0, 0, 0, false, 2, 0, 0, 0, 0,
                Array.Empty<VillagerView>(), new[] { building }, Array.Empty<ResourceView>());
            return new FactionFrame(0, 1, Array.Empty<RenderUnit>(), null, Array.Empty<CommandView>(), Array.Empty<GameEvent>(), null,
                new MatchResult(false, 0, false, false, true), economy: economy);
        }

        private static FactionFrame EconomyFrame(BuildingView? building, VillagerView villager)
        {
            var buildings = building.HasValue ? new[] { building.Value } : Array.Empty<BuildingView>();
            var economy = new EconomyView(0, 0, 1, 1, 0, 0, false, 2, 0, 0, 0, 0,
                new[] { villager }, buildings, Array.Empty<ResourceView>());
            return new FactionFrame(0, 1, Array.Empty<RenderUnit>(), null, Array.Empty<CommandView>(), Array.Empty<GameEvent>(), null,
                new MatchResult(false, 0, false, false, true), economy: economy);
        }
    }
}
