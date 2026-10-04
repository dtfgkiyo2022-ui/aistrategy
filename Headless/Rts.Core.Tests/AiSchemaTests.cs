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

        [Test]
        public void DynamicSchemaIsStrictAndHasOperationsWithRequiredNullableFields()
        {
            var summary = AiSituationSummary.From(Frame());
            using (var document = JsonDocument.Parse(AiCommandSchema.Build(summary, AiModelCatalog.Get("gpt-6-luna"))))
            {
                AssertStrict(document.RootElement);
                var root = document.RootElement;
                CollectionAssert.Contains(root.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray(), "operations");
                var scope = root.GetProperty("properties").GetProperty("commands").GetProperty("items").GetProperty("properties").GetProperty("scope");
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
