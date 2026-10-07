using System;
using System.Collections.Generic;

namespace Rts.Presentation
{
    /// <summary>
    /// The staff panel's complete boundary. It deliberately exposes no UnityHost or Providers type: the host owns
    /// target selection, model selection, reservations and costs, while the panel only edits strings and reads views.
    /// </summary>
    public interface IStaffControl
    {
        IReadOnlyList<StaffAiOption> AiChoices { get; }
        string SelectedAiName { get; set; }
        void Speak(string instruction);
        bool CancelLast();
        string TargetLabel { get; }
        decimal EstimateYen(string instruction);
        bool IsExpensiveEstimate(decimal estimateYen);
        decimal SpentYen { get; }
        decimal RemainingBudgetYen { get; }
        bool ShowBudgetWarning(string instruction);
        IReadOnlyList<StaffChatLine> Conversation { get; }
        string LastNotice { get; }
    }

    /// <summary>One model choice as drawn by Presentation.</summary>
    public sealed class StaffAiOption
    {
        public StaffAiOption(string name, string displayName, bool available)
        {
            Name = name ?? "";
            DisplayName = displayName ?? name ?? "";
            Available = available;
        }

        public string Name { get; }
        public string DisplayName { get; }
        public bool Available { get; }
    }

    /// <summary>A single already-formatted conversation line. Tone values are stable CSS-facing strings.</summary>
    public sealed class StaffChatLine
    {
        public const string YouTone = "you";
        public const string StaffTone = "staff";
        public const string DetailTone = "detail";
        public const string BadTone = "bad";

        public StaffChatLine(string time, string speaker, string body, string tone)
        {
            Time = time ?? "";
            Speaker = speaker ?? "";
            Body = body ?? "";
            Tone = tone ?? DetailTone;
        }

        public string Time { get; }
        public string Speaker { get; }
        public string Body { get; }
        public string Tone { get; }
    }

    /// <summary>Unity-free input used by StaffChatText to turn one host history item into display lines.</summary>
    public sealed class StaffInstructionView
    {
        public const string Interpreting = "interpreting";
        public const string Executing = "executing";
        public const string Completed = "completed";
        public const string Cancelled = "cancelled";
        public const string Expired = "expired";
        public const string Unknown = "unknown";

        public StaffInstructionView(string time, string modelDisplayName, string instruction, string state,
            string reply, string reason, IReadOnlyList<string> issued, IReadOnlyList<string> rejectedReasons,
            decimal estimatedCostYen, decimal actualCostYen)
        {
            Time = time ?? "";
            ModelDisplayName = modelDisplayName ?? "";
            Instruction = instruction ?? "";
            State = state ?? Unknown;
            Reply = reply ?? "";
            Reason = reason ?? "";
            Issued = issued ?? Array.Empty<string>();
            RejectedReasons = rejectedReasons ?? Array.Empty<string>();
            EstimatedCostYen = estimatedCostYen;
            ActualCostYen = actualCostYen;
        }

        public string Time { get; }
        public string ModelDisplayName { get; }
        public string Instruction { get; }
        public string State { get; }
        public string Reply { get; }
        public string Reason { get; }
        public IReadOnlyList<string> Issued { get; }
        public IReadOnlyList<string> RejectedReasons { get; }
        public decimal EstimatedCostYen { get; }
        public decimal ActualCostYen { get; }
    }

    /// <summary>Pure text and line decisions shared by IMGUI and UI Toolkit staff displays.</summary>
    public static class StaffChatText
    {
        public static IReadOnlyList<StaffChatLine> Build(IReadOnlyList<StaffInstructionView> history)
        {
            var lines = new List<StaffChatLine>();
            foreach (var item in history ?? Array.Empty<StaffInstructionView>())
            {
                if (item == null) continue;
                lines.Add(new StaffChatLine(item.Time, "あなた", item.Instruction, StaffChatLine.YouTone));

                string state = StateLabel(item.State);
                string reply = !string.IsNullOrEmpty(item.Reply) ? item.Reply : item.Reason;
                string staffBody = string.IsNullOrEmpty(reply) ? state : reply;
                if (!string.IsNullOrEmpty(item.ModelDisplayName)) staffBody = "（" + item.ModelDisplayName + "）" + staffBody;
                lines.Add(new StaffChatLine(item.Time, "参謀", staffBody,
                    item.State == StaffInstructionView.Unknown || item.State == StaffInstructionView.Expired
                        ? StaffChatLine.BadTone : StaffChatLine.StaffTone));

                if (!string.IsNullOrEmpty(item.Reply) && !string.IsNullOrEmpty(item.Reason) && item.Reason != item.Reply)
                    lines.Add(new StaffChatLine(item.Time, "詳しく", "理由：" + item.Reason, StaffChatLine.DetailTone));

                bool carriedOut = item.State == StaffInstructionView.Executing
                    || item.State == StaffInstructionView.Completed
                    || item.State == StaffInstructionView.Cancelled;
                foreach (string issued in item.Issued ?? Array.Empty<string>())
                    lines.Add(new StaffChatLine(item.Time, "詳しく", "→ " + (issued ?? "") + (carriedOut ? "" : "（実行せず）"), StaffChatLine.DetailTone));
                foreach (string rejected in item.RejectedReasons ?? Array.Empty<string>())
                    lines.Add(new StaffChatLine(item.Time, "詳しく", "× 却下：" + (rejected ?? ""), StaffChatLine.BadTone));

                string cost = item.State == StaffInstructionView.Interpreting
                    ? "見積もり " + item.EstimatedCostYen.ToString("0.000")
                    : item.ActualCostYen.ToString("0.000");
                lines.Add(new StaffChatLine(item.Time, "状態", "［" + state + "　" + cost + "円］", StaffChatLine.DetailTone));
            }
            return lines;
        }

        public static string StateLabel(string state)
        {
            switch (state)
            {
                case StaffInstructionView.Interpreting: return "考え中…";
                case StaffInstructionView.Executing: return "実行中";
                case StaffInstructionView.Completed: return "完了";
                case StaffInstructionView.Cancelled: return "取り消し";
                case StaffInstructionView.Expired: return "時間切れ";
                default: return "わからない";
            }
        }
    }
}
