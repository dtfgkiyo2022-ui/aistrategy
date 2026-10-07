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
    }
}
