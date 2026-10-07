using System;
using System.Collections.Generic;

namespace Rts.Providers
{
    /// <summary>Pure decisions shared by the strategist UI and headless tests.</summary>
    public static class StrategistUiRules
    {
        // Below one yen the estimate is too small to deserve a per-send warning in the compact panel.
        public const decimal ExpensiveEstimateThresholdYen = 1m;

        public static bool IsExpensive(decimal estimateYen)
            => estimateYen >= ExpensiveEstimateThresholdYen;

        public static bool ShouldWarnBudget(decimal remainingYen, decimal estimateYen)
            => estimateYen > 0m && remainingYen < estimateYen;

        public static string RestoreModel(string savedModel, IEnumerable<string> availableModels)
        {
            string first = null;
            foreach (string model in availableModels ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(model)) continue;
                first ??= model;
                if (string.Equals(model, savedModel, StringComparison.OrdinalIgnoreCase)) return model;
            }
            return first;
        }
    }
}
