using System;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Providers;
using Rts.Simulation;
using Rts.UnityHost;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class StaffChatTests
    {
        private static FactionFrame Frame()
        {
            var point = new SimPoint(Fix64.FromInt(0), Fix64.FromInt(10));
            var observation = new FactionObservation(1, 20,
                new[] { new OwnArmyView(1, 1, UnitKind.Infantry, point, 5, default(PolicyGoal)) },
                Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(),
                new[] { new KnownObjective(GoalKind.Outpost, 1, point, true, 1, false, 0, 20) });
            return new FactionFrame(20, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(),
                Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()),
                new MatchResult(false, 0, false, false, true));
        }

        [Test]
        public void ConversationKeepsThreeLatestTurnsAndStaysWithinBothLimits()
        {
            var summary = AiSituationSummary.From(Frame());
            summary.SetConversation(Enumerable.Range(1, 4).Select(i => new AiConversationTurn(
                "古い指示" + i + new string('人', 200), "返事" + i, "命令" + i, "実行")));
            string prompt = summary.DynamicPrompt("もっと強く");
            Assert.That(summary.Conversation, Does.Not.Contain("古い指示1"));
            Assert.That(summary.Conversation, Does.Contain("古い指示2"));
            Assert.That(summary.Conversation, Does.Contain("古い指示4"));
            Assert.That(summary.Conversation.Length, Is.LessThanOrEqualTo(AiConversationLimits.MaxCharacters));
            Assert.That(summary.Conversation.Split('\n').All(x => x.Length <= AiConversationLimits.MaxTurnCharacters + 3), Is.True);
            Assert.That(prompt, Does.Contain("直前のやり取り（参考。最大3往復）"));
        }

        [Test]
        public void RedoingTheConversationStartsEmptyAndAnswerToQuestionCanBecomeAnOrder()
        {
            var summary = AiSituationSummary.From(Frame());
            summary.SetConversation(new[] { new AiConversationTurn("どこを攻めますか？", "北か南を選んでください。", "なし", "聞き返し") });
            var first = AiResponseInterpreter.Interpret(
                "{\"commands\":[{\"type\":\"policy\",\"kind\":\"Focus\",\"scope\":\"全部隊\",\"goal\":\"北の拠点\"}],\"say\":\"了解です。北の拠点へ向かいます。\"}",
                new AiInterpretationContext { Frame = Frame(), Summary = summary });
            Assert.That(first.Policies, Has.Count.EqualTo(1));
            var reset = AiSituationSummary.From(Frame());
            Assert.That(reset.Conversation, Is.Empty);
        }

        [Test]
        public void ReplyIsFilledAndTrimmedButItsWordingIsNotSecondGuessed()
        {
            string longReply = new string('返', 100);
            Assert.That(AiResponseInterpreter.NormalizeReply("", "対象が不明です。", false, true), Is.EqualTo("対象が不明です。"));
            Assert.That(AiResponseInterpreter.NormalizeReply(longReply, "", true, false).Length, Is.EqualTo(80));
            Assert.That(AiResponseInterpreter.NormalizeReply("", "", true, false), Is.EqualTo("了解です。指示を実行します。"));
            var result = AiResponseInterpreter.Interpret(
                "{\"commands\":[{\"type\":\"policy\",\"kind\":\"Focus\",\"scope\":\"全部隊\",\"goal\":\"北の拠点\"}],\"say\":\"了解です。撤退します。\"}",
                new AiInterpretationContext { Frame = Frame(), Summary = AiSituationSummary.From(Frame()) });
            // The wording is the model's. Matching words such as 攻め or 守 against the orders replaced correct
            // replies (a doctrine switch has no Focus order), so the interpreter does not second-guess it.
            Assert.That(result.Say, Is.EqualTo("了解です。撤退します。"));
        }

        [Test]
        public void SmallSchemaRequiresTheSameNaturalReplyField()
        {
            using (var document = JsonDocument.Parse(AiCommandSchema.BuildSmall(AiSituationSummary.From(Frame()))))
            {
                var properties = document.RootElement.GetProperty("properties").EnumerateObject().Select(x => x.Name).ToArray();
                CollectionAssert.Contains(properties, "say");
                CollectionAssert.Contains(document.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray(), "say");
            }
        }

        [Test]
        public void OneExchangeHasOneHumanAndOneStaffBubbleWithSupplement()
        {
            var views = new[]
            {
                new StaffInstructionView("12:00", "luna", "北を守って", StaffInstructionView.Completed,
                    "了解です。北を守ります。", "敵の接近に備えます。", new[] { "全軍：防衛（北の拠点）" },
                    new[] { "対象外の命令" }, 0.1m, 0.2m)
            };
            var lines = StaffChatText.Build(views);
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0].Speaker, Is.EqualTo("あなた"));
            Assert.That(lines[1].Speaker, Is.EqualTo("参謀"));
            Assert.That(lines[1].Body, Does.Not.Contain("命令："), "the bubble's text is the reply only");
            Assert.That(lines[1].Detail, Does.Contain("命令："));
            Assert.That(lines[1].Detail, Does.Contain("却下："));
            Assert.That(lines[1].Detail, Does.Contain("費用："));

            var thinking = StaffChatText.Build(new[]
            {
                new StaffInstructionView("12:01", "luna", "北を守って", StaffInstructionView.Interpreting,
                    "", "", Array.Empty<string>(), Array.Empty<string>(), 0.3m, 0m)
            });
            var answered = StaffChatText.Build(new[]
            {
                new StaffInstructionView("12:01", "luna", "北を守って", StaffInstructionView.Completed,
                    "了解です。北を守ります。", "", new[] { "防衛" }, Array.Empty<string>(), 0.3m, 0.4m)
            });
            Assert.That(thinking, Has.Count.EqualTo(2));
            Assert.That(answered, Has.Count.EqualTo(2));
            Assert.That(thinking[1].Body, Does.Contain("考え中…"));
            Assert.That(answered[1].Body, Does.Contain("了解です。北を守ります。"));
        }
    }
}
