using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Core.Tests
{
    public sealed class MatchTimelineTests
    {
        [Test]
        public void JapaneseTimelineNamesDoctrineAndStaffOrdersSeparately()
        {
            bool previous = UiText.Japanese;
            UiText.Japanese = true;
            try
            {
                var target = new ScopeKey(1, ScopeKind.Army, 1);
                var commands = new[]
                {
                    new CommandView(1, target, PolicyKind.Defend, new PolicyGoal(GoalKind.Outpost, 1, default),
                        CommandStatus.Pending, 1, 2, ReasonCode.None, CommandSource.Doctrine),
                    new CommandView(2, target, PolicyKind.Retreat, default, CommandStatus.Pending, 1, 2,
                        ReasonCode.None, CommandSource.Ai)
                };
                var frame = new FactionFrame(1, 1, Array.Empty<RenderUnit>(),
                    new FactionObservation(1, 1, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                        Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>()), commands,
                    Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()), default);

                var timeline = new MatchTimeline();
                timeline.Ingest(frame);
                var text = timeline.Entries.Select(entry => entry.Text).ToArray();
                Assert.That(text.Any(value => value.Contains("お任せの方針の命令 #1")), Is.True);
                Assert.That(text.Any(value => value.Contains("参謀の命令 #2")), Is.True);
            }
            finally
            {
                UiText.Japanese = previous;
            }
        }
    }
}
