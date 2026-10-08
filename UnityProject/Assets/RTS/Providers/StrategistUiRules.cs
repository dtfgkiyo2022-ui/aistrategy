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

        /// <summary>
        /// When nothing usable was remembered, the cheapest capable AI is chosen first: a new player must not start on
        /// the most expensive model just because its key is present (seen in a player build, 10-09).
        /// </summary>
        public static readonly string[] FirstChoiceOrder =
            { "claude-haiku-5-5", "local-llm", "gpt-6-luna", "jev", "claude-haiku-4-5", "claude-sonnet-5-5" };

        public static string RestoreModel(string savedModel, IEnumerable<string> availableModels)
        {
            var available = new List<string>();
            foreach (string model in availableModels ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(model)) available.Add(model);
            foreach (string model in available)
                if (string.Equals(model, savedModel, StringComparison.OrdinalIgnoreCase)) return model;
            foreach (string preferred in FirstChoiceOrder)
                foreach (string model in available)
                    if (string.Equals(model, preferred, StringComparison.OrdinalIgnoreCase)) return model;
            return available.Count == 0 ? null : available[0];
        }

        /// <summary>The name a player reads in the AI list; unknown models show their id.</summary>
        public static string DisplayName(string model)
        {
            switch ((model ?? "").ToLowerInvariant())
            {
                case "local-llm": return "ローカルLLM（無料）";
                case "jev": return "Jev（命令1つ）";
                case "claude-haiku-5-5": return "Claude Haiku 5.5";
                case "claude-haiku-4-5": return "Claude Haiku 4.5";
                case "claude-sonnet-5-5": return "Claude Sonnet 5.5";
                case "claude-opus-5-5": return "Claude Opus 5.5";
                case "claude-fable-5-1": return "Claude Fable 5.1";
                case "gpt-6-luna": return "ChatGPT Luna";
                case "gpt-6.1-sol": return "ChatGPT Sol";
                case "gpt-6-astra": return "ChatGPT Astra";
                default: return model ?? "";
            }
        }
    }
}
