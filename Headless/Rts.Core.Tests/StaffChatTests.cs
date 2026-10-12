using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        private static FactionFrame Frame(bool twoOutposts = false)
        {
            var point = new SimPoint(Fix64.FromInt(0), Fix64.FromInt(10));
            var objectives = twoOutposts
                ? new[]
                {
                    new KnownObjective(GoalKind.Outpost, 1, new SimPoint(Fix64.FromInt(0), Fix64.FromInt(20)), true, 1, false, 0, 20),
                    new KnownObjective(GoalKind.Outpost, 2, new SimPoint(Fix64.FromInt(0), Fix64.FromInt(0)), true, 1, false, 0, 20)
                }
                : new[] { new KnownObjective(GoalKind.Outpost, 1, point, true, 1, false, 0, 20) };
            var observation = new FactionObservation(1, 20,
                new[] { new OwnArmyView(1, 1, UnitKind.Infantry, point, 5, default(PolicyGoal)) },
                Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(),
                objectives);
            return new FactionFrame(20, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(),
                Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()),
                new MatchResult(false, 0, false, false, true));
        }

        private static AiAnswersForTest Answers(string kind, string target, string goal)
        {
            return new AiAnswersForTest(kind, target, goal);
        }

        private sealed class AiAnswersForTest
        {
            internal readonly JevAnswers Value = new JevAnswers();

            internal AiAnswersForTest(string kind, string target, string goal)
            {
                Value.Choices["instruction_kind"] = kind;
                Value.ChoiceConfidences["instruction_kind"] = 0.9;
                Value.Choices["instruction_target"] = target;
                Value.ChoiceConfidences["instruction_target"] = target == JevChoice.Unknown ? 0.8 : 0.9;
                Value.Choices["instruction_goal"] = goal;
                Value.ChoiceConfidences["instruction_goal"] = 0.9;
            }
        }

        private sealed class FakeJevTransport : IJevTransport, IQuestionAwareJevTransport
        {
            internal string State;
            internal string Questions;
            internal readonly JevAnswers Answer;

            internal FakeJevTransport(JevAnswers answer)
            {
                Answer = answer;
            }

            public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel)
            {
                State = stateJson;
                return Task.FromResult(Answer);
            }

            public Task<JevAnswers> AskAsync(string stateJson, string questionsJson, CancellationToken cancel)
            {
                State = stateJson;
                Questions = questionsJson;
                return Task.FromResult(Answer);
            }
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
        public void JevStateNamesThePreviousTurnOnlyWhenThereIsOne()
        {
            var without = AiSituationSummary.From(Frame());
            string unchanged = JevCommandInterpreter.BuildState(new InterpreterRequest
            {
                Instruction = "北の拠点を守って",
                Summary = without
            });
            using (var document = JsonDocument.Parse(unchanged))
            {
                CollectionAssert.AreEquivalent(new[] { "stateVersion", "summary", "instruction", "conversation", "names" },
                    document.RootElement.EnumerateObject().Select(x => x.Name).ToArray());
            }

            var with = AiSituationSummary.From(Frame());
            with.SetConversation(new[] { new AiConversationTurn("攻めて", "どこを攻めますか？", "なし", "聞き返し") });
            string state = JevCommandInterpreter.BuildState(new InterpreterRequest
            {
                Instruction = "北の拠点で",
                Summary = with
            });
            Assert.That(state, Does.Contain("直前の指示: 攻めて"));
            Assert.That(state, Does.Contain("直前の結果: 聞き返し"));
            Assert.That(state, Does.Contain("直前に出した命令の種類・対象・目標: なし"));
            Assert.That(state, Does.Contain("今回の指示: 北の拠点で"));
        }

        [Test]
        public void JevFollowUpAnswerCompletesAQuestionWithThePreviousKind()
        {
            var summary = AiSituationSummary.From(Frame());
            summary.SetConversation(new[] { new AiConversationTurn("攻めて", "どこを攻めますか？", "なし", "聞き返し") });
            var json = JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Instruction = "北の拠点で", Summary = summary },
                Answers("focus", JevChoice.NorthOutpost, "capture").Value);
            var result = AiResponseInterpreter.Interpret(json, new AiInterpretationContext { Frame = Frame(), Summary = summary });
            Assert.That(result.Policies, Has.Count.EqualTo(1));
            Assert.That(result.Policies[0].Kind, Is.EqualTo(PolicyKind.Focus));
            Assert.That(result.Policies[0].Target.Kind, Is.EqualTo(ScopeKind.All));
            Assert.That(result.Policies[0].Goal.Id, Is.EqualTo(1u));
            Assert.That(result.Say, Is.EqualTo("了解です。全軍で北の拠点を攻めます。"));
        }

        [Test]
        public void JevRedoReplacesOnlyTheGoalAndUnrelatedFollowUpDoesNotInheritIt()
        {
            var frame = Frame(true);
            var summary = AiSituationSummary.From(frame);
            summary.SetConversation(new[] { new AiConversationTurn("北の拠点を攻めて", "了解です。", "全軍：攻撃（北の拠点）", "実行") });
            var redo = JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Instruction = "やっぱり南の拠点にして", Summary = summary },
                Answers("focus", JevChoice.SouthOutpost, "capture").Value);
            var redoResult = AiResponseInterpreter.Interpret(redo, new AiInterpretationContext { Frame = frame, Summary = summary });
            Assert.That(redoResult.Policies.Single().Kind, Is.EqualTo(PolicyKind.Focus));
            Assert.That(redoResult.Policies.Single().Goal.Id, Is.EqualTo(2u));
            Assert.That(redoResult.Say, Is.EqualTo("了解です。全軍で南の拠点を攻めます。"));

            var unrelated = JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Instruction = "南の拠点を守れ", Summary = summary },
                Answers("defend", JevChoice.SouthOutpost, "hold").Value);
            var unrelatedResult = AiResponseInterpreter.Interpret(unrelated, new AiInterpretationContext { Frame = frame, Summary = summary });
            Assert.That(unrelatedResult.Policies.Single().Kind, Is.EqualTo(PolicyKind.Defend));
            Assert.That(unrelatedResult.Policies.Single().Goal.Id, Is.EqualTo(2u));
            Assert.That(unrelatedResult.Say, Is.EqualTo("了解です。全軍で南の拠点を守ります。"));
        }

        [Test]
        public void JevAsksForTheMissingTargetInItsReply()
        {
            var summary = AiSituationSummary.From(Frame());
            var answers = Answers("focus", JevChoice.Unknown, "capture").Value;
            string json = JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Instruction = "攻めて", Summary = summary }, answers);
            var result = AiResponseInterpreter.Interpret(json, new AiInterpretationContext { Frame = Frame(), Summary = summary });
            Assert.That(result.Unknown, Is.True);
            Assert.That(result.Say, Is.EqualTo("どこを攻めますか？"));
        }

        [Test]
        public void JevInterpreterUsesTheFakeReplyAndSendsThePreviousTurnAsState()
        {
            var summary = AiSituationSummary.From(Frame());
            summary.SetConversation(new[] { new AiConversationTurn("攻めて", "どこを攻めますか？", "なし", "聞き返し") });
            var fake = new FakeJevTransport(Answers("focus", JevChoice.NorthOutpost, "capture").Value);
            using (var interpreter = new JevCommandInterpreter(() => "not-used", fake))
            {
                interpreter.Request(new InterpreterRequest
                {
                    RequestId = 1,
                    Instruction = "北の拠点で",
                    Summary = summary,
                    Model = "jev"
                });
                var clock = System.Diagnostics.Stopwatch.StartNew();
                IReadOnlyList<InterpreterReply> replies = Array.Empty<InterpreterReply>();
                while (replies.Count == 0 && clock.ElapsedMilliseconds < 2000)
                {
                    replies = interpreter.Poll(0);
                    if (replies.Count == 0) Thread.Sleep(1);
                }

                Assert.That(replies, Has.Count.EqualTo(1));
                Assert.That(fake.State, Does.Contain("直前の指示: 攻めて"));
                Assert.That(fake.State, Does.Contain("直前の結果: 聞き返し"));
                Assert.That(fake.Questions, Does.Contain("今回の指示に書かれていない項目"));
                var result = AiResponseInterpreter.Interpret(replies[0].Json,
                    new AiInterpretationContext { Frame = Frame(), Summary = summary });
                Assert.That(result.Policies.Single().Kind, Is.EqualTo(PolicyKind.Focus));
                Assert.That(result.Policies.Single().Goal.Id, Is.EqualTo(1u));
            }
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
