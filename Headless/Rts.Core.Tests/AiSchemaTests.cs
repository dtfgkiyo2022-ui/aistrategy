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
        public void DynamicSchemaIsStrictAndHasOperationsWithRequiredFields()
        {
            var summary = AiSituationSummary.From(Frame());
            using (var document = JsonDocument.Parse(AiCommandSchema.Build(summary, AiModelCatalog.Get("gpt-6-luna"))))
            {
                AssertStrict(document.RootElement);
                var root = document.RootElement;
                CollectionAssert.Contains(root.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray(), "operations");
                var scope = root.GetProperty("properties").GetProperty("commands").GetProperty("items").GetProperty("properties").GetProperty("scope");
                var scopeEnum = scope.GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();
                CollectionAssert.Contains(scopeEnum, "全部隊");
                CollectionAssert.Contains(scopeEnum, "", "an empty string is how the schema says 'not given'");
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
                // The command limit is enforced by the interpreter: Claude rejects maxItems in strict schemas (HTTP 400, 2026-10-05).
                AssertStrict(document.RootElement);
            }
            Assert.That(AiModelCatalog.Get("local-llm").MaxCommands, Is.EqualTo(1));
            Assert.That(AiModelCatalog.Get("local-llm").AllowsOperations, Is.False);
            Assert.That(AiModelCatalog.Get("jev").AllowsOperations, Is.False);
            Assert.That(AiModelCatalog.Get("gpt-6-luna").MaxCommands, Is.EqualTo(5));
        }

        /// <summary>Walks a JSON Schema node by node (only schema positions: "properties" values and "items").</summary>
        private static void AssertStrict(JsonElement node)
        {
            Assert.That(node.ValueKind, Is.EqualTo(JsonValueKind.Object));
            // Keywords the providers' strict subsets rejected on 2026-10-05: numeric ranges, array lengths, and union types
            // (Claude allows at most 16 parameters with type arrays or anyOf; the schema uses "" / 0 / false for "none").
            foreach (var keyword in new[] { "minimum", "maximum", "maxItems", "minItems", "anyOf", "oneOf" })
                Assert.That(node.TryGetProperty(keyword, out _), Is.False, keyword);
            Assert.That(node.GetProperty("type").ValueKind, Is.EqualTo(JsonValueKind.String), "no type arrays");
            string type = node.GetProperty("type").GetString();
            if (type == "object")
            {
                Assert.That(node.GetProperty("additionalProperties").GetBoolean(), Is.False);
                var required = node.GetProperty("required").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x).ToArray();
                var properties = node.GetProperty("properties").EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToArray();
                CollectionAssert.AreEqual(properties, required);
                foreach (var property in node.GetProperty("properties").EnumerateObject()) AssertStrict(property.Value);
            }
            else if (type == "array") AssertStrict(node.GetProperty("items"));
        }
    }
}
