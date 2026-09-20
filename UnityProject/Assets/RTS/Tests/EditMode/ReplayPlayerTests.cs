using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class ReplayPlayerTests
    {
        private const long Ticks = 220;

        private static BuildIdentity Build() => new BuildIdentity { Commit = "test", SourceHash = new string('a', 64), Backend = "test" };

        private static ScheduledInput Input(long tick) => new ScheduledInput(1, InputKind.Resolve, tick - 1, tick, 7, 8,
            new[]
            {
                new PolicyOrder(9, 10, CommandSource.Human, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Retreat,
                    new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 50, new LossBudget(300),
                    new EndCondition(EndKind.UntilReplaced, 0), 0, 0, new PolicyVersion[0], 0,
                    new Expiration(long.MaxValue, 240, ExpireFlags.SubjectGone | ExpireFlags.ObservationTooOld))
            });

        private static byte[] Record(long ticks = Ticks)
        {
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, WeekOneScenario.Create(), new[] { Input(3) }, ticks, Build());
                return stream.ToArray();
            }
        }

        /// <summary>Rewrites the recording with one tick hash changed; record hashes stay valid.</summary>
        private static byte[] WithBrokenTickHash(byte[] bytes, long tick)
        {
            using (var input = new MemoryStream(bytes))
            using (var reader = new ReplayReader(input))
            using (var output = new MemoryStream())
            {
                using (var writer = new ReplayWriter(output, reader.Header))
                {
                    ReplayRecord record;
                    while ((record = reader.Read()) != null)
                    {
                        var payload = (byte[])record.Payload.Clone();
                        if (record.Kind == ReplayRecordKind.TickHash && record.Tick == tick) payload[0] ^= 0xFF;
                        writer.Write(new ReplayRecord(record.Kind, record.Tick, record.LogIndex, payload));
                    }
                }
                return output.ToArray();
            }
        }

        [Test]
        public void PlayerLoadsEveryRecordedTickInOrderAndStopsAtTheEnd()
        {
            using (var stream = new MemoryStream(Record()))
            using (var player = new ReplayPlayer(stream, Build()))
            {
                Assert.That(player.Tick, Is.EqualTo(-1));
                long expected = 0;
                while (player.StepOnce())
                {
                    Assert.That(player.Tick, Is.EqualTo(expected));
                    Assert.That(player.Capture(1).Tick, Is.EqualTo(expected));
                    expected++;
                }
                Assert.That(expected, Is.EqualTo(Ticks + 1));
                Assert.That(player.HasEnded, Is.True);
                Assert.That(player.StepOnce(), Is.False);
                Assert.That(player.DisplayMismatchTick, Is.Null);
            }
        }

        [Test]
        public void PlayedTickCountMatchesTheRecordingAndBothFactionsAreCapturable()
        {
            var recorded = new List<long>();
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, WeekOneScenario.Create(), new[] { Input(3) }, Ticks, Build(),
                    (state, hash, events) => recorded.Add(state.Tick));
                stream.Position = 0;
                using (var player = new ReplayPlayer(stream, Build()))
                {
                    int index = 0;
                    while (player.StepOnce())
                    {
                        Assert.That(player.Capture(1).FactionId, Is.EqualTo(1u));
                        Assert.That(player.Capture(2).FactionId, Is.EqualTo(2u));
                        Assert.That(player.Capture(2).Tick, Is.EqualTo(recorded[index]));
                        index++;
                    }
                    Assert.That(index, Is.EqualTo(recorded.Count));
                }
            }
        }

        [Test]
        public void PlayerReportsTheFirstTickWhoseRecordedHashDoesNotMatchAndKeepsPlaying()
        {
            using (var clean = new MemoryStream(Record(40)))
            using (var player = new ReplayPlayer(clean, Build()))
            {
                while (player.StepOnce()) { }
                Assert.That(player.DisplayMismatchTick, Is.Null, "An untouched recording must play back clean.");
            }

            using (var broken = new MemoryStream(WithBrokenTickHash(Record(40), 20)))
            using (var player = new ReplayPlayer(broken, Build()))
            {
                while (player.StepOnce()) { }
                Assert.That(player.DisplayMismatchTick, Is.EqualTo(20));
                Assert.That(player.Tick, Is.EqualTo(40), "A mismatch must not stop playback.");
            }
        }

        [Test]
        public void PlayerRejectsAForeignBuildUnlessTheCallerAllowsIt()
        {
            byte[] bytes = Record(20);
            var other = new BuildIdentity { Commit = "other", SourceHash = new string('b', 64), Backend = "test" };
            using (var stream = new MemoryStream(bytes))
                Assert.Throws<InvalidDataException>(() => new ReplayPlayer(stream, other));
            using (var stream = new MemoryStream(bytes))
            using (var player = new ReplayPlayer(stream, other, true))
            {
                Assert.That(player.StepOnce(), Is.True);
                Assert.That(player.Tick, Is.EqualTo(0));
            }
        }

        [Test]
        public void PlayerAppliesRecordedInputsSoCommandsAppearInTheFrame()
        {
            using (var stream = new MemoryStream(Record()))
            using (var player = new ReplayPlayer(stream, Build()))
            {
                bool sawCommand = false;
                while (player.StepOnce())
                    if (player.Capture(1).Commands.Count > 0) { sawCommand = true; break; }
                Assert.That(sawCommand, Is.True, "The recorded retreat order must reach the played simulation.");
            }
        }
    }
}
