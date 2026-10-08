using NUnit.Framework;
using Rts.Providers;

namespace Rts.Core.Tests
{
    public sealed class StrategistUiRulesTests
    {
        [TestCase(0.9999, false)]
        [TestCase(1.0, true)]
        [TestCase(4.5, true)]
        public void ExpensiveEstimateStartsAtOneYen(double estimate, bool expected)
        {
            Assert.That(StrategistUiRules.IsExpensive((decimal)estimate), Is.EqualTo(expected));
        }

        [Test]
        public void BudgetWarningOnlyWhenNextSendDoesNotFit()
        {
            Assert.That(StrategistUiRules.ShouldWarnBudget(4.5m, 4.5m), Is.False);
            Assert.That(StrategistUiRules.ShouldWarnBudget(4.49m, 4.5m), Is.True);
            Assert.That(StrategistUiRules.ShouldWarnBudget(0m, 0m), Is.False);
        }

        [Test]
        public void RestoreModelKeepsSavedUsableModelOrUsesFirstAvailable()
        {
            var models = new[] { "local-llm", "jev" };
            Assert.That(StrategistUiRules.RestoreModel("jev", models), Is.EqualTo("jev"));
            Assert.That(StrategistUiRules.RestoreModel("claude-opus-5-5", models), Is.EqualTo("local-llm"));
            Assert.That(StrategistUiRules.RestoreModel(null, models), Is.EqualTo("local-llm"));
        }

        [Test]
        public void WithoutAUsableSavedModelTheCheapestCapableOneIsChosenNotTheFirstListed()
        {
            // A player build with only an Anthropic key once started on Fable 5.1, the first one listed (10-09).
            var models = new[] { "claude-fable-5-1", "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-haiku-4-5" };
            Assert.That(StrategistUiRules.RestoreModel(null, models), Is.EqualTo("claude-haiku-5-5"));
            Assert.That(StrategistUiRules.RestoreModel("local-llm", models), Is.EqualTo("claude-haiku-5-5"));
            Assert.That(StrategistUiRules.RestoreModel("claude-opus-5-5", models), Is.EqualTo("claude-opus-5-5"));
            Assert.That(StrategistUiRules.RestoreModel(null, new[] { "claude-fable-5-1", "gpt-6-astra" }), Is.EqualTo("claude-fable-5-1"));
            Assert.That(StrategistUiRules.DisplayName("claude-fable-5-1"), Is.EqualTo("Claude Fable 5.1"));
        }
    }
}
