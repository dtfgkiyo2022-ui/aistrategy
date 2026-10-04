using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Core.Tests
{
    public sealed class AiSchemaTests
    {
        private static FactionFrame Frame()
        {
            var point = new SimPoint(Fix64.FromInt(0), Fix64.FromInt(10));
            var observation = new FactionObservation(1, 0,
                new[] { new OwnArmyView(7, 1, UnitKind.Infantry, point, 12, default(PolicyGoal)) },
                Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(),
                new[] { new KnownObjective(GoalKind.Outpost, 1, point, true, 1, false, 0, 0) });
            return new FactionFrame(0, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(), Array.Empty<GameEvent>(),
                new FogView(Array.Empty<bool>(), Array.Empty<bool>()), new MatchResult(false, 0, false, false, true));
        }

        private static FactionFrame BudgetFrame()
        {
            var p = new SimPoint(Fix64.FromInt(0), Fix64.FromInt(10));
            var armies = new[]
            {
                new OwnArmyView(1, 1, UnitKind.Infantry, p, 20, default),
                new OwnArmyView(2, 1, UnitKind.Archer, p, 10, default),
                new OwnArmyView(3, 1, UnitKind.Scout, p, 5, default)
            };
            var objectives = new[]
            {
                new KnownObjective(GoalKind.Outpost, 1, p, true, 1, false, 0, 0),
                new KnownObjective(GoalKind.Outpost, 2, p, true, 1, false, 0, 0),
                new KnownObjective(GoalKind.Core, 1, p, true, 1, false, 0, 0),
                new KnownObjective(GoalKind.Core, 2, p, true, 2, false, 0, 0)
            };
            var observation = new FactionObservation(1, 0, armies, Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), objectives);
            var regions = Enumerable.Range(1, 4).Select(i => new RegionView((uint)i, RegionCenterKind.None, 0, p, RegionControl.Ai, PolicyKind.ReturnToAuto, EconomyPolicy.Balanced)).ToArray();
            return new FactionFrame(0, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(), Array.Empty<GameEvent>(),
                new FogView(Array.Empty<bool>(), Array.Empty<bool>()), new MatchResult(false, 0, false, false, true), regions: regions);
        }

        [Test]
        public void DynamicSchemaIsStrictAndHasOperationsWithRequiredNullableFields()
        {
            var summary = AiSituationSummary.From(Frame());
            using (var document = JsonDocument.Parse(AiCommandSchema.Build(summary, AiModelCatalog.Get("gpt-6-luna"))))
            {
                AssertStrict(document.RootElement);
                var root = document.RootElement;
                CollectionAssert.Contains(root.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray(), "operations");
                var scope = root.GetProperty("$defs").GetProperty("command").GetProperty("properties").GetProperty("scope");
                var scopeEnum = scope.GetProperty("anyOf")[0].GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();
                CollectionAssert.Contains(scopeEnum, "全部隊");
                CollectionAssert.DoesNotContain(scopeEnum, "存在しない軍");
                var condition = root.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("properties").GetProperty("when");
                CollectionAssert.Contains(condition.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray(), "And");
            }
        }

        [Test]
        public void SmallModelsHaveOneCommandAndNoOperationsInSchema()
        {
            var summary = AiSituationSummary.From(Frame());
            using (var document = JsonDocument.Parse(AiCommandSchema.Build(summary, AiModelCatalog.Get("local-llm"))))
            {
                var properties = document.RootElement.GetProperty("properties");
                Assert.That(properties.GetProperty("commands").GetProperty("maxItems").GetInt32(), Is.EqualTo(1));
                Assert.That(properties.GetProperty("operations").GetProperty("maxItems").GetInt32(), Is.EqualTo(0));
            }
            Assert.That(AiModelCatalog.Get("jev").AllowsOperations, Is.False);
            Assert.That(AiModelCatalog.Get("gpt-6-luna").MaxCommands, Is.EqualTo(5));
        }

        [Test]
        public void DynamicSituationAndSchemaStayWithinTheTokenBudget()
        {
            var summary = AiSituationSummary.From(BudgetFrame());
            summary.AddAlias("北軍", "第1軍"); summary.AddAlias("南軍", "第2軍"); summary.AddAlias("斥候", "第3軍");
            summary.AddAlias("南の予備", "第3軍"); summary.AddAlias("自分のコア", "自軍コア"); summary.AddAlias("敵のコア", "敵コア");
            summary.AddAlias("支城の区域", "区域2"); summary.AddProducerName("兵舎1", 9, default, "完成、訓練中");
            string changing = AiCommandSchema.Build(summary, AiModelCatalog.Get("gpt-6-luna")) + "\n" + summary.DynamicPrompt("ここを守って");
            Assert.That(AiCostCalculator.EstimateTokens(changing), Is.LessThanOrEqualTo(1500));
            Assert.That(summary.Text, Does.Not.Contain("ID="));
            Assert.That(summary.DynamicPrompt("ここを守って"), Does.Contain("[部隊:"));
            Assert.That(summary.DynamicPrompt("ここを守って"), Does.Contain("[拠点:"));
        }

        [Test]
        public void SelectedBuildingUsesItsOwnNameTableEntry()
        {
            var summary = AiSituationSummary.From(Frame());
            summary.AddProducerName("兵舎1", 7, default, "完成、訓練中");
            string prompt = summary.DynamicPrompt("今の訓練を取り消して", null, "兵舎1");
            Assert.That(prompt, Does.Contain("兵舎1[建物:完成、訓練中]"));
            Assert.That(prompt, Does.Contain("選択中の対象：兵舎1"));
            Assert.That(summary.TryGet("兵舎1", out var producer), Is.True);
            Assert.That(producer.Id, Is.EqualTo(7u));
            Assert.That(producer.HasScope, Is.False);
        }

        private static void AssertStrict(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
            {
                Assert.That(node.GetProperty("additionalProperties").GetBoolean(), Is.False);
                var required = node.GetProperty("required").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x).ToArray();
                var properties = node.GetProperty("properties").EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToArray();
                CollectionAssert.AreEqual(properties, required);
            }
            foreach (var property in node.EnumerateObject())
            {
                Assert.That(property.Name, Is.Not.EqualTo("minimum"));
                Assert.That(property.Name, Is.Not.EqualTo("maximum"));
                AssertStrict(property.Value);
            }
        }
    }
}
