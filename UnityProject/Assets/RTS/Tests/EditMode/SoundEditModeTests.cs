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
    }
}
