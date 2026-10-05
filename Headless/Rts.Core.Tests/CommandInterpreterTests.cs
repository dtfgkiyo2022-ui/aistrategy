using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Core.Tests
{
    public sealed class CommandInterpreterTests
    {
        private static readonly SimPoint NorthPoint = new SimPoint(Fix64.FromInt(10), Fix64.FromInt(20));
        private static FactionFrame Frame(bool economy = false, bool autoEconomy = false)
        {
            var observation = new FactionObservation(1, 100,
                new[] { new OwnArmyView(7, 1, UnitKind.Infantry, new SimPoint(Fix64.FromInt(2), Fix64.FromInt(3)), 12, default(PolicyGoal)) },
                Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(),
                new[] { new KnownObjective(GoalKind.Outpost, 1, NorthPoint, true, 1, false, 0, 100) });
            EconomyView view = null;
            if (economy)
                view = new EconomyView(100, 100, 3, 20, 0, 0, autoEconomy, 2, 50, 10, 20, 10,
                    Array.Empty<VillagerView>(), Array.Empty<BuildingView>(), Array.Empty<ResourceView>());
            return new FactionFrame(100, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(),
                Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()), new MatchResult(false, 0, false, false, true), economy: view,
                regions: new[] { new RegionView(3, RegionCenterKind.None, 0, default(SimPoint), RegionControl.Ai, PolicyKind.ReturnToAuto, EconomyPolicy.Balanced) });
        }
        private static AiCommandInterpretationResult Interpret(string json, FactionFrame frame = null, IAiPlacementFinder finder = null)
        {
            frame = frame ?? Frame();
            var summary = AiSituationSummary.From(frame);
            return AiResponseInterpreter.Interpret(json, new AiInterpretationContext { Frame = frame, Summary = summary, StartedTick = 100, DeadlineTick = 340, PlacementFinder = finder });
        }

        [Test]
        public void ValidPolicyAndRegionCommandsBecomeContracts()
        {
            var result = Interpret("{\"commands\":[{\"type\":\"policy\",\"kind\":\"Defend\",\"scope\":\"全部隊\",\"goal\":\"北の拠点\"},{\"type\":\"economy\",\"kind\":\"SetRegionControl\",\"region\":\"区域3\",\"control\":\"Human\"}],\"say\":\"北を守ります\"}", Frame(true, false));
            Assert.That(result.Policies.Count, Is.EqualTo(1));
            Assert.That(result.Policies[0].Kind, Is.EqualTo(PolicyKind.Defend));
            Assert.That(result.Policies[0].Goal.Id, Is.EqualTo(1u));
            Assert.That(result.EconomyCommands.Count, Is.EqualTo(1));
            Assert.That(result.EconomyCommands[0].Kind, Is.EqualTo(EconomyCommandKind.SetRegionControl));
        }

        [Test]
        public void InvalidNamesOwnershipAndRangeAreReportedAndDropped()
        {
            var result = Interpret("{\"commands\":[{\"type\":\"policy\",\"kind\":\"Focus\",\"scope\":\"北の拠点\",\"goal\":\"北の拠点\"},{\"type\":\"policy\",\"kind\":\"Defend\",\"scope\":\"ない軍\",\"goal\":\"北の拠点\"}],\"say\":\"\"}");
            Assert.That(result.Policies, Is.Empty);
            Assert.That(result.Rejected.Count, Is.EqualTo(2));
        }

        [Test]
        public void UnknownAnswerProducesNoCommand()
        {
            var result = Interpret("{\"unknown\":true,\"reason\":\"対象が不明\",\"say\":\"\"}");
            Assert.That(result.Policies, Is.Empty); Assert.That(result.EconomyCommands, Is.Empty);
            Assert.That(result.Reason, Is.EqualTo("対象が不明"));
        }

        [Test]
        public void SmallAnswerIsExpandedAndValidatedAsTheNormalPolicyCommand()
        {
            var summary = AiSituationSummary.From(Frame());
            string scope = summary.NameTable.First(x => x.HasScope && x.Scope.Kind == ScopeKind.All).Name;
            string goal = summary.NameTable.First(x => x.HasGoal).Name;
            var result = AiResponseInterpreter.Interpret(
                "{\"kind\":\"Defend\",\"scope\":\"" + scope + "\",\"goal\":\"" + goal + "\",\"region\":\"\",\"control\":\"\",\"reason\":\"\"}",
                new AiInterpretationContext { Frame = Frame(), Summary = summary, StartedTick = 100, DeadlineTick = 340 });
            Assert.That(result.Unknown, Is.False);
            Assert.That(result.Policies.Count, Is.EqualTo(1));
            Assert.That(result.Policies[0].Kind, Is.EqualTo(PolicyKind.Defend));
            Assert.That(result.Policies[0].Goal.Id, Is.EqualTo(1u));
        }

        [Test]
        public void SmallUnknownAnswerRemainsAnExplicitUnknown()
        {
            var result = Interpret("{\"kind\":\"unknown\",\"scope\":\"\",\"goal\":\"\",\"region\":\"\",\"control\":\"\",\"reason\":\"表にない名前\"}");
            Assert.That(result.Unknown, Is.True);
            Assert.That(result.Policies, Is.Empty);
            Assert.That(result.Reason, Is.EqualTo("表にない名前"));
        }

        [Test]
        public void BrokenJsonAndWrongShapeAreUnreadable()
        {
            Assert.That(Interpret("not-json").Report, Does.StartWith("読めない答え"));
            Assert.That(Interpret("{\"commands\":{}}", Frame()).Report, Does.StartWith("読めない答え"));
        }

        [Test]
        public void LateFakeReplyIsDiscardedByCoordinator()
        {
            var fake = new FakeCommandInterpreter(241, r => "{\"commands\":[],\"say\":\"ok\"}");
            var coordinator = new CommandInterpreterCoordinator(fake);
            coordinator.Request("守れ", Frame(), null, "gpt-6-luna", 100, 240);
            var replies = coordinator.Poll(341);
            Assert.That(replies.Count, Is.EqualTo(1));
            Assert.That(replies[0].Late, Is.True);
            Assert.That(replies[0].Result.Policies, Is.Empty);
        }

        [Test]
        public void SummaryDoesNotInventUnobservedEnemy()
        {
            var summary = AiSituationSummary.From(Frame());
            Assert.That(summary.Text, Does.Contain("現在見えている敵=0"));
            Assert.That(summary.Text, Does.Not.Contain("敵軍団"));
        }

        [Test]
        public void PromptStatesSelectedTargetOrNone()
        {
            var summary = AiSituationSummary.From(Frame());
            Assert.That(summary.Prompt("守れ"), Does.Contain("選択中の対象：なし"));
            Assert.That(summary.Prompt("ここを守って", new ScopeKey(1, ScopeKind.Outpost, 1)), Does.Contain("選択中の対象：北の拠点"));
        }

        [Test]
        public void JapaneseEconomyNamesBecomeGameEnums()
        {
            var result = Interpret("{\"commands\":[{\"type\":\"economy\",\"kind\":\"SetEconomyPolicy\",\"policy\":\"内政\"},{\"type\":\"economy\",\"kind\":\"PlaceBuilding\",\"building\":\"塔\",\"location\":\"お任せ\"},{\"type\":\"economy\",\"kind\":\"Train\",\"unit\":\"歩兵\",\"producer\":\"コア\"}],\"say\":\"\"}", Frame(true, false),
                new DelegateAiPlacementFinder((_, __, building) => Tuple.Create(building == BuildingKind.Tower, 9, "")));
            Assert.That(result.Rejected, Is.Empty);
            Assert.That(result.EconomyCommands.Count, Is.EqualTo(3));
            Assert.That(result.EconomyCommands[0].Policy, Is.EqualTo(EconomyPolicy.Growth));
            Assert.That(result.EconomyCommands[1].Building, Is.EqualTo(BuildingKind.Tower));
            Assert.That(result.EconomyCommands[2].Unit, Is.EqualTo(UnitKind.Infantry));
        }

        [Test]
        public void CostEstimateAndBudgetUseTokenCounts()
        {
            Assert.That(AiCostCalculator.Calculate("gpt-6-luna", 1000, 200), Is.EqualTo(0.03m));
            Assert.That(AiCostCalculator.Calculate("jev", 800, 0), Is.EqualTo(0.00504m));
            var meter = new AiBudgetMeter(3m); meter.Add(0.03m); meter.Add(0.00504m);
            Assert.That(meter.SpentYen, Is.EqualTo(0.03504m)); Assert.That(meter.RemainingYen, Is.EqualTo(2.96496m));
        }

        [Test]
        public void JevTakesOneCommandAndRefusesSeveralWhateverTheWording()
        {
            const string two = "{\"commands\":[{\"type\":\"policy\",\"kind\":\"Defend\",\"scope\":\"全部隊\",\"goal\":\"北の拠点\"},{\"type\":\"economy\",\"kind\":\"SetRegionControl\",\"region\":\"区域3\",\"control\":\"Human\"}],\"say\":\"\"}";
            const string one = "{\"commands\":[{\"type\":\"policy\",\"kind\":\"Defend\",\"scope\":\"全部隊\",\"goal\":\"北の拠点\"}],\"say\":\"\"}";
            var frame = Frame(true, false);

            var jevTwo = new CommandInterpreterCoordinator(new FakeCommandInterpreter(0, r => two));
            jevTwo.Request("北を守って区域3は自分で", frame, null, "jev", 0, 240);
            var refused = jevTwo.Poll(0)[0].Result;
            Assert.That(refused.Unknown, Is.True);
            Assert.That(refused.Reason, Does.Contain("このモデルでは直せません"));
            Assert.That(refused.Policies.Count + refused.EconomyCommands.Count, Is.EqualTo(0));

            // The same answer from a model that takes complex instructions is kept.
            var lunaTwo = new CommandInterpreterCoordinator(new FakeCommandInterpreter(0, r => two));
            lunaTwo.Request("北を守って区域3は自分で", frame, null, "gpt-6-luna", 0, 240);
            var kept = lunaTwo.Poll(0)[0].Result;
            Assert.That(kept.Policies.Count + kept.EconomyCommands.Count, Is.EqualTo(2));

            // A short order is never refused for its wording (this one contains the character "か").
            var jevOne = new CommandInterpreterCoordinator(new FakeCommandInterpreter(0, r => one));
            jevOne.Request("北の拠点に向かって守れ", frame, null, "jev", 0, 240);
            var single = jevOne.Poll(0)[0].Result;
            Assert.That(single.Unknown, Is.False);
            Assert.That(single.Policies.Count, Is.EqualTo(1));
        }

        [Test]
        public void LocalModelMergesIdenticalCommandsBeforeItsLimit()
        {
            const string duplicate = "{\"commands\":[{\"type\":\"policy\",\"kind\":\"Defend\",\"scope\":\"全部隊\",\"goal\":\"北の拠点\"},{\"goal\":\"北の拠点\",\"scope\":\"全部隊\",\"kind\":\"Defend\",\"type\":\"policy\"}],\"say\":\"\"}";
            const string different = "{\"commands\":[{\"type\":\"policy\",\"kind\":\"Defend\",\"scope\":\"全部隊\",\"goal\":\"北の拠点\"},{\"type\":\"policy\",\"kind\":\"Retreat\",\"scope\":\"全部隊\"}],\"say\":\"\"}";
            var frame = Frame();

            var merged = new CommandInterpreterCoordinator(new FakeCommandInterpreter(0, _ => duplicate));
            merged.Request("北を守れ", frame, null, "local-llm", 0, 600);
            var mergedResult = merged.Poll(0)[0].Result;
            Assert.That(mergedResult.Unknown, Is.False);
            Assert.That(mergedResult.Policies, Has.Count.EqualTo(1));

            var refused = new CommandInterpreterCoordinator(new FakeCommandInterpreter(0, _ => different));
            refused.Request("守って戻れ", frame, null, "local-llm", 0, 600);
            Assert.That(refused.Poll(0)[0].Result.Unknown, Is.True);
            Assert.That(AiModelCatalog.Get("local-llm").MaxOutputTokens, Is.EqualTo(2048));
        }
    }
}
