using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>
    /// Chapter 11 UI timeline: first contact, report shown, command accepted, interpreted, applied,
    /// departure, arrival, loss and capture change. Built from captured frames only; no wall clock.
    /// </summary>
    public sealed class MatchTimeline
    {
        public readonly struct Entry
        {
            public long Tick { get; }
            public string Text { get; }
            public Entry(long tick, string text) { Tick = tick; Text = text; }
        }

        private const int Capacity = 400;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<ulong, CommandStatus> commandStatus = new Dictionary<ulong, CommandStatus>();
        private readonly HashSet<uint> knownContacts = new HashSet<uint>();
        private readonly Dictionary<uint, uint> outpostOwners = new Dictionary<uint, uint>();
        private long lastTick = -1;
        private uint factionId;

        public IReadOnlyList<Entry> Entries { get { return entries; } }

        public void Clear()
        {
            entries.Clear();
            commandStatus.Clear();
            knownContacts.Clear();
            outpostOwners.Clear();
            lastTick = -1;
        }

        public void Ingest(FactionFrame frame)
        {
            if (frame == null) return;
            if (frame.FactionId != factionId) { factionId = frame.FactionId; Clear(); }
            // A tick that goes backwards means the match was restarted: forget the old match's entries.
            if (frame.Tick < lastTick) Clear();
            else if (frame.Tick == lastTick) return;
            lastTick = frame.Tick;

            // New contacts of one tick make one line: a column coming into view used to fill the timeline line by line.
            int sighted = 0;
            foreach (var contact in frame.Observation.Contacts)
            {
                if (contact.IsArmyContact || !knownContacts.Add(contact.ContactId)) continue;
                sighted++;
            }
            if (sighted > 0)
                Add(frame.Tick, UiText.T("Enemy sighted (", "敵を発見（") + sighted + UiText.T(")", "体）"));

            foreach (var objective in frame.Objectives)
            {
                if (objective.Kind != GoalKind.Outpost || !objective.IsOwnerKnown) continue;
                if (outpostOwners.TryGetValue(objective.Id, out var previous))
                {
                    if (previous == objective.OwnerFactionId) continue;
                    Add(frame.Tick, UiText.T("Outpost ", "拠点") + objective.Id + UiText.T(" changed hands: ", " の持ち主：") + Owner(previous) + " → " + Owner(objective.OwnerFactionId));
                }
                outpostOwners[objective.Id] = objective.OwnerFactionId;
            }

            foreach (var command in frame.Commands)
            {
                bool known = commandStatus.TryGetValue(command.CommandId, out var previous);
                if (!known)
                {
                    Add(command.AcceptedTick, Source(command.Source) + UiText.T(" order #", "の命令 #") + command.CommandId + "：" + Target(command.Target)
                        + UiText.T(" ", "に") + Policy(command.Kind));
                }
                if (known && previous == command.Status) continue;
                commandStatus[command.CommandId] = command.Status;
                switch (command.Status)
                {
                    case CommandStatus.Pending:
                        Add(frame.Tick, UiText.T("Order #", "命令 #") + command.CommandId + UiText.T(" understood, starts at ", " 了解、開始 ") + MatchOutcome.Clock(command.ApplyTick));
                        break;
                    case CommandStatus.Executing:
                        Add(frame.Tick, UiText.T("Order #", "命令 #") + command.CommandId + UiText.T(" under way", " 実行中"));
                        break;
                    case CommandStatus.Completed:
                        Add(frame.Tick, UiText.T("Order #", "命令 #") + command.CommandId + UiText.T(" done", " 完了"));
                        break;
                    case CommandStatus.Cancelled:
                    case CommandStatus.Expired:
                    case CommandStatus.Impossible:
                        Add(frame.Tick, UiText.T("Order #", "命令 #") + command.CommandId + " " + Ended(command.Status)
                            + (command.Reason == ReasonCode.None ? "" : UiText.T(" (", "（") + Reason(command.Reason) + UiText.T(")", "）")));
                        break;
                }
            }

            foreach (var e in frame.Events)
            {
                switch (e.Kind)
                {
                    case EventKind.MoveStarted:
                        Add(e.Tick, UiText.T("Soldier ", "兵") + e.SubjectId + UiText.T(" set off", " が出発"));
                        break;
                    case EventKind.Death:
                        Add(e.Tick, UiText.T("Soldier ", "兵") + e.SubjectId + UiText.T(" lost", " を失った"));
                        break;
                    case EventKind.Capture:
                        Add(e.Tick, UiText.T("Outpost ", "拠点") + e.SubjectId + UiText.T(" taken by ", " を") + Owner((uint)e.Value) + UiText.T("", "が占領"));
                        break;
                    case EventKind.Reinforcement:
                        Add(e.Tick, UiText.T("Reinforcements +", "増援 +") + e.Value + UiText.T(" at point ", "（地点") + e.SubjectId + UiText.T("", "）"));
                        break;
                    case EventKind.MatchEnded:
                        var outcome = MatchOutcome.Describe(frame.Result, frame.FactionId, e.Tick);
                        Add(e.Tick, outcome.HasValue ? UiText.T("match ended: ", "試合終了：") + outcome.Value.Headline : UiText.T("match ended", "試合終了"));
                        break;
                }
            }
        }

        // Player-facing words for the contract values; the raw enum names stay in logs and replays.
        private string Owner(uint faction)
        {
            if (faction == 0) return UiText.T("nobody", "中立");
            return faction == factionId ? UiText.T("us", "自軍") : UiText.T("the enemy", "敵");
        }

        private static string Source(CommandSource source)
        {
            switch (source)
            {
                case CommandSource.Human: return UiText.T("Your", "あなた");
                case CommandSource.Doctrine: return UiText.T("Entrusted policy", "お任せの方針");
                case CommandSource.Ai: return UiText.T("Staff officer's", "参謀");
                default: return UiText.T("Auto", "お任せ");
            }
        }

        private static string Target(ScopeKey target)
        {
            switch (target.Kind)
            {
                case ScopeKind.All: return UiText.T("all forces", "全軍");
                case ScopeKind.Army: return UiText.T("army ", "軍団") + target.Id;
                case ScopeKind.Outpost: return UiText.T("outpost ", "拠点") + target.Id;
                case ScopeKind.Region: return UiText.T("region ", "区域") + target.Id;
                default: return target.Kind.ToString() + target.Id;
            }
        }

        private static string Policy(PolicyKind kind)
        {
            switch (kind)
            {
                case PolicyKind.Focus: return UiText.T("attack", "攻撃");
                case PolicyKind.AllowAbandon: return UiText.T("may abandon", "放棄を許可");
                case PolicyKind.Retreat: return UiText.T("retreat", "撤退");
                case PolicyKind.MaintainReserve: return UiText.T("keep a reserve", "予備を保つ");
                case PolicyKind.Defend: return UiText.T("defend", "防衛");
                case PolicyKind.Scout: return UiText.T("scout", "偵察");
                case PolicyKind.ReturnToAuto: return UiText.T("back to auto", "お任せに戻す");
                default: return kind.ToString();
            }
        }

        private static string Ended(CommandStatus status)
        {
            switch (status)
            {
                case CommandStatus.Cancelled: return UiText.T("cancelled", "取り消し");
                case CommandStatus.Expired: return UiText.T("timed out", "時間切れ");
                default: return UiText.T("not possible", "実行できない");
            }
        }

        private static string Reason(ReasonCode reason)
        {
            switch (reason)
            {
                case ReasonCode.Superseded: return UiText.T("replaced by a newer order", "新しい命令に置き換え");
                case ReasonCode.UserCancelled: return UiText.T("cancelled by you", "あなたが取り消し");
                case ReasonCode.Deadline: return UiText.T("reply too late", "返答が間に合わない");
                case ReasonCode.SubjectGone: return UiText.T("the unit is gone", "対象がもういない");
                case ReasonCode.OwnershipChanged: return UiText.T("owner changed", "持ち主が変わった");
                case ReasonCode.NoPath: return UiText.T("no way there", "道がない");
                case ReasonCode.EmptyArmy: return UiText.T("army is empty", "軍団に兵がいない");
                case ReasonCode.LossLimit: return UiText.T("loss limit reached", "損害の上限");
                case ReasonCode.ReserveShortfall: return UiText.T("not enough reserve", "予備が足りない");
                default: return reason.ToString();
            }
        }

        private void Add(long tick, string text)
        {
            entries.Add(new Entry(tick, text));
            if (entries.Count > Capacity) entries.RemoveRange(0, entries.Count - Capacity);
        }
    }
}
