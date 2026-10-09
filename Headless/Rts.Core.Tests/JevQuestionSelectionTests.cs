using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Tests.Headless
{
    public sealed class JevQuestionSelectionTests
    {
        private sealed class CapturingTransport : IJevTransport, IQuestionAwareJevTransport
        {
            internal readonly List<string> Questions = new List<string>();

            public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel) =>
                Task.FromResult(new JevAnswers());

            public Task<JevAnswers> AskAsync(string stateJson, string questionsJson, CancellationToken cancel)
            {
                lock (Questions) Questions.Add(questionsJson);
                return Task.FromResult(new JevAnswers());
            }
        }

        private static PolicyRequest Request(ulong id, long tick) =>
            new PolicyRequest(id, 1, new ScopeKey(1, ScopeKind.All, 0), tick,
                new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                    Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>()),
                new[] { new PolicyVersion(new ScopeKey(1, ScopeKind.All, 0), 0) }, tick + 240,
                PolicyKind.Focus, default(PolicyGoal));

        private static string[] Names(JevQuestionContext context) =>
            JevQuestions.Names(JevQuestions.Build(context)).ToArray();

        [Test]
        public void TheRegularSetHasOnlyTheThreeRecurringJudgements()
        {
            Assert.That(Names(new JevQuestionContext { IncludeComprehension = false }),
                Is.EqualTo(new[] { "decisive_point", "dangerous_outpost", "retreat_north" }));
            Assert.That(Names(new JevQuestionContext { IncludeComprehension = true }),
                Is.EqualTo(new[] { "decisive_point", "dangerous_outpost", "retreat_north",
                    "outnumbering", "enemy_near_my_core", "outpost_held_by_enemy" }));
        }

        [Test]
        public void OperationAndInstructionQuestionsAreAddedOnlyByTheirContext()
        {
            var operation = Names(new JevQuestionContext { OperationConditionNeeded = true, IncludeComprehension = false });
            Assert.That(operation, Does.Contain("operation_north_broken"));
            Assert.That(operation, Does.Not.Contain("instruction_kind"));

            var instruction = Names(new JevQuestionContext { InstructionTranslationNeeded = true, IncludeComprehension = false });
            Assert.That(instruction, Does.Contain("instruction_kind"));
            Assert.That(instruction, Does.Contain("instruction_target"));
            Assert.That(instruction, Does.Contain("instruction_goal"));
            Assert.That(instruction, Does.Not.Contain("operation_north_broken"));
        }

        [Test]
        public void SignalQuestionsAreAddedOnlyWhenOwnTacticHasSignals()
        {
            var without = JevQuestions.Build(new JevQuestionContext { InstructionTranslationNeeded = true, IncludeComprehension = false });
            Assert.That(without, Does.Not.Contain("instruction_signal"));
            Assert.That(without, Does.Not.Contain("\"signal\":\"戦術の合図を送る\""));

            var with = JevQuestions.Build(new JevQuestionContext
            {
                InstructionTranslationNeeded = true,
                IncludeComprehension = false,
                SignalDefinitions = new[]
                {
                    new AiTacticSignalInfo("allIn", "総攻撃", false),
                    new AiTacticSignalInfo("holdHere", "ここを守れ", true)
                }
            });
            Assert.That(with, Does.Contain("instruction_signal"));
            Assert.That(with, Does.Contain("\"allIn\":\"総攻撃\""));
            Assert.That(with, Does.Contain("\"holdHere\":\"ここを守れ\""));
            Assert.That(with, Does.Contain("\"unknown\":\"わからない\""));
        }

        [Test]
        public void LineQuestionsUseOnlyAvailableLinesAndRegions()
        {
            var questions = JevQuestions.Build(new JevQuestionContext
            {
                InstructionTranslationNeeded = true,
                IncludeComprehension = false,
                AvailableLineNames = new[] { "Steel", "CoreWood" },
                RegionNames = new[] { "区域3", "区域7" }
            });
            Assert.That(questions, Does.Contain("\"line\":\"加工のラインを作る\""));
            Assert.That(questions, Does.Contain("\"instruction_line\""));
            Assert.That(questions, Does.Contain("\"Steel\":\"鋼\""));
            Assert.That(questions, Does.Contain("\"区域3\":\"区域3\""));
            Assert.That(questions, Does.Contain("\"instruction_region\""));
        }

        [Test]
        public void NoAvailableLineLeavesInstructionQuestionsUnchanged()
        {
            var withoutLines = JevQuestions.Build(new JevQuestionContext { InstructionTranslationNeeded = true, IncludeComprehension = false });
            Assert.That(withoutLines, Does.Not.Contain("instruction_line"));
            Assert.That(withoutLines, Does.Not.Contain("instruction_region"));
            Assert.That(withoutLines, Does.Not.Contain("\"line\":\"加工のラインを作る\""));
        }

        [Test]
        public void ConfidentSteelLineAnswerBecomesRequestLine()
        {
            var summary = new AiSituationSummary
            {
                AvailableLineNames = new[] { "Steel" }
            };
            var answers = new JevAnswers();
            answers.Choices["instruction_kind"] = "line";
            answers.ChoiceConfidences["instruction_kind"] = 0.9;
            answers.Choices["instruction_line"] = "Steel";
            answers.ChoiceConfidences["instruction_line"] = 0.9;
            string json = JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Summary = summary }, answers);
            Assert.That(json, Does.Contain("\"kind\":\"RequestLine\""));
            Assert.That(json, Does.Contain("\"line\":\"Steel\""));
            Assert.That(json, Does.Contain("\"location\":\"お任せ\""));
        }

        [Test]
        public void LowConfidenceLineAnswerBecomesUnknown()
        {
            var summary = new AiSituationSummary { AvailableLineNames = new[] { "Steel" } };
            var answers = new JevAnswers();
            answers.Choices["instruction_kind"] = "line";
            answers.ChoiceConfidences["instruction_kind"] = 0.9;
            answers.Choices["instruction_line"] = "Steel";
            answers.ChoiceConfidences["instruction_line"] = 0.5;
            Assert.That(JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Summary = summary }, answers), Does.Contain("\"unknown\":true"));
        }

        [Test]
        public void RealShapedAnswerReadsLineChoice()
        {
            var transport = new HttpJevTransport(() => "k");
            var answers = transport.Read("{\"answers\":{\"instruction_kind\":{\"type\":\"choice\",\"choice\":\"line\",\"confidence\":0.9},\"instruction_line\":{\"type\":\"choice\",\"choice\":\"Steel\",\"confidence\":0.95}}}");
            Assert.That(answers.Choices["instruction_kind"], Is.EqualTo("line"));
            Assert.That(answers.Choices["instruction_line"], Is.EqualTo("Steel"));
            Assert.That(answers.ChoiceConfidences["instruction_line"], Is.EqualTo(0.95).Within(1e-9));
        }

        [Test]
        public void ConfidentSignalAnswerBecomesSendTacticSignal()
        {
            var summary = new AiSituationSummary();
            summary.SetTacticInfo("rush", new[] { "rush" }, Array.Empty<AiTacticParameterInfo>(),
                new[] { new AiTacticSignalInfo("allIn", "総攻撃", false) });
            var answers = new JevAnswers();
            answers.Choices["instruction_kind"] = "signal";
            answers.ChoiceConfidences["instruction_kind"] = 0.9;
            answers.Choices["instruction_signal"] = "allIn";
            answers.ChoiceConfidences["instruction_signal"] = 0.9;

            string json = JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Summary = summary }, answers);
            Assert.That(json, Does.Contain("\"kind\":\"SendTacticSignal\""));
            Assert.That(json, Does.Contain("\"tacticSignal\":\"allIn\""));
        }

        [Test]
        public void RealAnswerKindsSignalAndDoctrineAreRead()
        {
            // Read once mapped every unlisted choice to "no choice", so a real "signal" or "doctrine" answer was lost.
            var transport = new HttpJevTransport(() => "k");
            var answers = transport.Read("{\"answers\":{"
                + "\"instruction_kind\":{\"type\":\"choice\",\"choice\":\"signal\",\"confidence\":0.9},"
                + "\"instruction_doctrine\":{\"type\":\"choice\",\"choice\":\"maintain\",\"confidence\":0.9},"
                + "\"instruction_signal\":{\"type\":\"choice\",\"choice\":\"allIn\",\"confidence\":1.0}}}");
            Assert.That(answers.Choices["instruction_kind"], Is.EqualTo("signal"));
            Assert.That(answers.Choices["instruction_doctrine"], Is.EqualTo("maintain"));
            Assert.That(answers.Choices["instruction_signal"], Is.EqualTo("allIn"));
        }

        [Test]
        public void AnAttackWithNoTargetIsTheSignalButANamedTargetKeepsThePolicy()
        {
            // The real Jev answers "総攻撃して" as focus with the target unknown and allIn at 1.0 (10-08).
            var summary = new AiSituationSummary();
            summary.SetTacticInfo("rush", new[] { "rush" }, Array.Empty<AiTacticParameterInfo>(),
                new[] { new AiTacticSignalInfo("allIn", "総攻撃", false) });
            var untargeted = new JevAnswers();
            untargeted.Choices["instruction_kind"] = "focus";
            untargeted.ChoiceConfidences["instruction_kind"] = 0.92;
            untargeted.Choices["instruction_signal"] = "allIn";
            untargeted.ChoiceConfidences["instruction_signal"] = 1.0;
            untargeted.Choices["instruction_target"] = JevChoice.Unknown;
            untargeted.ChoiceConfidences["instruction_target"] = 0.74;
            Assert.That(JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Summary = summary }, untargeted),
                Does.Contain("\"tacticSignal\":\"allIn\""));

            var named = new JevAnswers();
            named.Choices["instruction_kind"] = "focus";
            named.ChoiceConfidences["instruction_kind"] = 0.92;
            named.Choices["instruction_signal"] = "allIn";
            named.ChoiceConfidences["instruction_signal"] = 1.0;
            named.Choices["instruction_target"] = JevChoice.EnemyCore;
            named.ChoiceConfidences["instruction_target"] = 0.9;
            Assert.That(JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Summary = summary }, named),
                Does.Not.Contain("SendTacticSignal"));
        }

        [Test]
        public void LowConfidenceAndMissingPointSignalAnswersBecomeUnknown()
        {
            var summary = new AiSituationSummary();
            summary.SetTacticInfo("rush", new[] { "rush" }, Array.Empty<AiTacticParameterInfo>(),
                new[] { new AiTacticSignalInfo("holdHere", "ここを守れ", true) });
            var low = new JevAnswers();
            low.Choices["instruction_kind"] = "signal";
            low.ChoiceConfidences["instruction_kind"] = 0.9;
            low.Choices["instruction_signal"] = "holdHere";
            low.ChoiceConfidences["instruction_signal"] = 0.5;
            Assert.That(JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Summary = summary }, low), Does.Contain("\"unknown\":true"));

            var noTarget = new JevAnswers();
            noTarget.Choices["instruction_kind"] = "signal";
            noTarget.ChoiceConfidences["instruction_kind"] = 0.9;
            noTarget.Choices["instruction_signal"] = "holdHere";
            noTarget.ChoiceConfidences["instruction_signal"] = 0.9;
            Assert.That(JevCommandInterpreter.ToCommandJson(new InterpreterRequest { Summary = summary }, noTarget), Does.Contain("地点を選んでから話しかけてください"));
        }

        [Test]
        public void ComprehensionIsFirstAndThenEveryConfiguredNumberOfCalls()
        {
            var transport = new CapturingTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { ComprehensionIntervalCalls = 3 }))
            {
                for (ulong i = 1; i <= 4; i++)
                {
                    provider.Request(Request(i, (long)i * 20));
                    var clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < 5000)
                    {
                        if (provider.Poll((long)i * 20).Count != 0) break;
                        Thread.Sleep(1);
                    }
                }
            }
            var sent = transport.Questions.SelectMany(q => new[] { JevQuestions.Names(q) }).ToArray();
            Assert.That(sent.Length, Is.EqualTo(4));
            Assert.That(sent[0], Does.Contain("outnumbering"));
            Assert.That(sent[1], Does.Not.Contain("outnumbering"));
            Assert.That(sent[2], Does.Not.Contain("outnumbering"));
            Assert.That(sent[3], Does.Contain("outnumbering"));
        }

        [Test]
        public void TheFixedGroupedStateAndRegularQuestionsStayNearEightHundredTokens()
        {
            var armies = Enumerable.Range(0, 100).Select(i => new OwnArmyView((uint)i + 1, 1,
                UnitKind.Infantry, new SimPoint(Fix64.FromInt(20 + i % 10), Fix64.FromInt(30 + i % 70)), 8,
                new PolicyGoal(GoalKind.Outpost, i % 2 == 0 ? 1u : 2u, default(SimPoint)))).ToArray();
            var contacts = Enumerable.Range(0, 100).Select(i => new EnemyContact((uint)i + 1,
                new SimPoint(Fix64.FromInt(150 + i % 8), Fix64.FromInt(30 + i % 70)), 1200, 1, 2, true)).ToArray();
            var observation = new FactionObservation(1, 1200, armies, Array.Empty<VisibleEnemy>(), contacts,
                new[]
                {
                    new KnownObjective(GoalKind.Outpost, 1, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(96)), true, 1, false, 0, 0),
                    new KnownObjective(GoalKind.Outpost, 2, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(32)), true, 1, false, 0, 0)
                });
            string state = JevState.Build(observation);
            string questions = JevQuestions.Build(new JevQuestionContext { IncludeComprehension = true });
            string body = "{\"model\":\"jev-latest\",\"state\":" + state + ",\"questions\":" + questions + "}";
            string regularQuestions = JevQuestions.Build(new JevQuestionContext { IncludeComprehension = false });
            string regularBody = "{\"model\":\"jev-latest\",\"state\":" + state + ",\"questions\":" + regularQuestions + "}";
            TestContext.Out.WriteLine("regular input: " + body.Length + " chars, approximately " + body.Length / 4 + " tokens");
            TestContext.Out.WriteLine("regular input without comprehension: " + regularBody.Length + " chars, approximately " + regularBody.Length / 4 + " tokens");
            Assert.That(body.Length / 4, Is.LessThanOrEqualTo(800), body.Length.ToString());
        }
    }
}
