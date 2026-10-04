using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>作戦を作った主体。人の指示から作られた作戦は Human、参謀の自律提案は Ai とする。</summary>
    public enum OperationSource : byte
    {
        Human = 1,
        Ai = 2
    }

    public enum OperationConditionKind : byte
    {
        OutpostOwnerChangedToEnemy = 1,
        OutpostOwnerChangedToSelf = 2,
        EnemyNearObjective = 3,
        OwnArmyBelowPercent = 4,
        MatchTimeAfter = 5,
        JudgementTrue = 6,
        All = 7
    }

    public enum OperationState : byte
    {
        Waiting = 1,
        Fired = 2,
        Cancelled = 3
    }

    /// <summary>
    /// 固定語彙の条件。自由な文章や式は保持せず、条件の値は作戦を登録した時点で名前表から解決する。
    /// </summary>
    public sealed class OperationCondition
    {
        public OperationConditionKind Kind { get; private set; }
        public string ObjectiveName { get; private set; }
        public uint ObjectiveId { get; private set; }
        public int EnemyCount { get; private set; }
        public int NearMeters { get; private set; }
        public int OwnArmyPercent { get; private set; }
        public long TimeTick { get; private set; }
        public string JudgementQuestion { get; private set; }
        public IReadOnlyList<OperationCondition> Children { get; private set; }

        private OperationCondition() { }

        public static OperationCondition OwnerChangedToEnemy(string objectiveName, uint objectiveId) =>
            new OperationCondition { Kind = OperationConditionKind.OutpostOwnerChangedToEnemy, ObjectiveName = objectiveName, ObjectiveId = objectiveId, Children = Empty() };

        public static OperationCondition OwnerChangedToSelf(string objectiveName, uint objectiveId) =>
            new OperationCondition { Kind = OperationConditionKind.OutpostOwnerChangedToSelf, ObjectiveName = objectiveName, ObjectiveId = objectiveId, Children = Empty() };

        public static OperationCondition EnemyNear(string objectiveName, uint objectiveId, int count, int nearMeters = 40)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            if (nearMeters < 0) throw new ArgumentOutOfRangeException(nameof(nearMeters));
            return new OperationCondition { Kind = OperationConditionKind.EnemyNearObjective, ObjectiveName = objectiveName, ObjectiveId = objectiveId,
                EnemyCount = count, NearMeters = nearMeters, Children = Empty() };
        }

        public static OperationCondition OwnArmyBelowPercent(int percent)
        {
            if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
            return new OperationCondition { Kind = OperationConditionKind.OwnArmyBelowPercent, OwnArmyPercent = percent, Children = Empty() };
        }

        public static OperationCondition TimeAfterSeconds(long seconds)
        {
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            return new OperationCondition { Kind = OperationConditionKind.MatchTimeAfter, TimeTick = checked(seconds * 20), Children = Empty() };
        }

        public static OperationCondition TimeAfterTick(long tick)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            return new OperationCondition { Kind = OperationConditionKind.MatchTimeAfter, TimeTick = tick, Children = Empty() };
        }

        public static OperationCondition Judgement(string question = "operation_north_broken")
        {
            if (string.IsNullOrEmpty(question)) throw new ArgumentException("A judgement question is required.", nameof(question));
            return new OperationCondition { Kind = OperationConditionKind.JudgementTrue, JudgementQuestion = question, Children = Empty() };
        }

        public static OperationCondition AllOf(params OperationCondition[] conditions)
        {
            if (conditions == null || conditions.Length < 2 || conditions.Length > 2 || conditions.Any(c => c == null))
                throw new ArgumentException("An operation AND must contain one or two conditions.", nameof(conditions));
            return new OperationCondition { Kind = OperationConditionKind.All, Children = Array.AsReadOnly(conditions.ToArray()) };
        }

        public string Describe()
        {
            switch (Kind)
            {
                case OperationConditionKind.OutpostOwnerChangedToEnemy: return (ObjectiveName ?? "拠点") + "の所有者が敵に変わった";
                case OperationConditionKind.OutpostOwnerChangedToSelf: return (ObjectiveName ?? "拠点") + "の所有者が自分に変わった";
                case OperationConditionKind.EnemyNearObjective: return (ObjectiveName ?? "拠点") + "の近くに見えている敵が" + EnemyCount.ToString(CultureInfo.InvariantCulture) + "以上";
                case OperationConditionKind.OwnArmyBelowPercent: return "自分の兵が作戦を作ったときの" + OwnArmyPercent.ToString(CultureInfo.InvariantCulture) + "%以下";
                case OperationConditionKind.MatchTimeAfter: return "試合の時間が" + (TimeTick / 20).ToString(CultureInfo.InvariantCulture) + "秒を過ぎた";
                case OperationConditionKind.JudgementTrue: return "細かい判断「" + (JudgementQuestion ?? "") + "である」が真";
                case OperationConditionKind.All: return string.Join(" AND ", Children.Select(c => c.Describe()).ToArray());
                default: return "不明な条件";
            }
        }

        private static IReadOnlyList<OperationCondition> Empty() => Array.Empty<OperationCondition>();
    }

    public sealed class OperationAction
    {
        public bool IsPolicy { get; private set; }
        public UserPolicyIntent Policy { get; private set; }
        public EconomyCommand Economy { get; private set; }

        public static OperationAction FromPolicy(UserPolicyIntent policy) => new OperationAction { IsPolicy = true, Policy = policy };
        public static OperationAction FromEconomy(EconomyCommand economy) => economy == null ? throw new ArgumentNullException(nameof(economy)) : new OperationAction { Economy = economy };
    }

    /// <summary>参謀の答えから検証済みの G-1 命令だけを作戦の then に保持する。</summary>
    public sealed class OperationDefinition
    {
        public OperationCondition When { get; private set; }
        public IReadOnlyList<OperationAction> Then { get; private set; }
        public bool Once { get; private set; }
        public OperationSource Source { get; private set; }

        public OperationDefinition(OperationCondition when, IReadOnlyList<OperationAction> then, bool once, OperationSource source)
        {
            if (when == null) throw new ArgumentNullException(nameof(when));
            if (then == null || then.Count == 0 || then.Any(a => a == null)) throw new ArgumentException("An operation needs a command.", nameof(then));
            When = when; Then = Array.AsReadOnly(then.ToArray()); Once = once; Source = source;
        }
    }

    public sealed class OperationAddResult
    {
        public bool Accepted { get; internal set; }
        public ulong Id { get; internal set; }
        public string Reason { get; internal set; }
    }

    public sealed class OperationView
    {
        public ulong Id { get; internal set; }
        public string ConditionDescription { get; internal set; }
        public OperationState State { get; internal set; }
        public string StateText => State == OperationState.Waiting ? "待機中" : State == OperationState.Fired ? "発火済み" : "取り消し";
        public OperationSource Source { get; internal set; }
    }

    public sealed class FiredOperation
    {
        public ulong Id { get; internal set; }
        public OperationSource Source { get; internal set; }
        public string ConditionDescription { get; internal set; }
        public IReadOnlyList<OperationAction> Actions { get; internal set; }
    }

    public sealed class OperationEvaluation
    {
        public IReadOnlyList<FiredOperation> Fired { get; internal set; } = Array.Empty<FiredOperation>();
        public bool JudgementWasUsed { get; internal set; }
        public string JudgementFailure { get; internal set; }
    }

    /// <summary>
    /// 陣営ごとの作戦表。観測は FactionFrame だけで読み、判定した命令を返す。Gateway や Simulation は参照しない。
    /// </summary>
    public sealed class OperationTable
    {
        public const int MaxOperations = 10;

        private sealed class Entry
        {
            internal ulong Id;
            internal OperationDefinition Definition;
            internal OperationState State;
            internal bool Armed = true;
            internal bool PreviousSatisfied;
            internal bool HasPrevious;
        }

        private readonly uint factionId;
        private readonly int minJudgementPermille;
        private readonly List<Entry> entries = new List<Entry>();
        private ulong nextId = 1;
        private FactionFrame previousFrame;

        public OperationTable(uint factionId, int minJudgementPermille = 600)
        {
            if (factionId == 0) throw new ArgumentOutOfRangeException(nameof(factionId));
            if (minJudgementPermille < 0 || minJudgementPermille > 1000) throw new ArgumentOutOfRangeException(nameof(minJudgementPermille));
            this.factionId = factionId; this.minJudgementPermille = minJudgementPermille;
        }

        public int Count => entries.Count(e => e.State != OperationState.Cancelled);
        public IReadOnlyList<OperationView> Operations => entries.Select(View).ToArray();

        public OperationAddResult Add(OperationDefinition definition, FactionFrame createdFrame)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (createdFrame == null) throw new ArgumentNullException(nameof(createdFrame));
            ValidateFrame(createdFrame);
            if (definition.Then.Any(a => a.IsPolicy ? a.Policy.Target.FactionId != factionId : a.Economy.FactionId != factionId))
                return new OperationAddResult { Accepted = false, Reason = "作戦の命令がこの陣営のものではないため、作戦を断った" };
            if (Count >= MaxOperations)
                return new OperationAddResult { Accepted = false, Reason = "作戦の上限10件に達しているため、新しい作戦を断った" };
            var entry = new Entry { Id = nextId++, Definition = definition, State = OperationState.Waiting };
            entries.Add(entry);
            baselineArmy[entry.Id] = createdFrame.Observation.OwnArmies.Sum(a => Math.Max(0, a.AliveCount));
            previousFrame = createdFrame;
            return new OperationAddResult { Accepted = true, Id = entry.Id, Reason = definition.Source == OperationSource.Human ? "人の指示から作戦を登録した" : "参謀の自律提案から作戦を登録した" };
        }

        public bool Cancel(ulong id)
        {
            var entry = entries.FirstOrDefault(e => e.Id == id);
            if (entry == null || entry.State == OperationState.Cancelled) return false;
            entry.State = OperationState.Cancelled; entry.Armed = false; return true;
        }

        public bool TryCancel(ulong id, out string reason)
        {
            bool cancelled = Cancel(id);
            reason = cancelled ? "作戦を取り消した" : "作戦IDが見つからないか、すでに取り消されている";
            return cancelled;
        }

        /// <summary>ルール条件だけを判定する入口。Jev 条件がある場合はその条件を偽として扱う。</summary>
        public OperationEvaluation Evaluate(FactionFrame frame, JevAnswers judgement = null)
        {
            ValidateFrame(frame);
            var result = EvaluateCore(frame, judgement, null);
            previousFrame = frame;
            return result;
        }

        /// <summary>
        /// 作戦に Jev 条件がある時だけ、G-3 q9 の作戦設問を一度送る。失敗・低確信度は偽として扱う。
        /// </summary>
        public async Task<OperationEvaluation> EvaluateAsync(FactionFrame frame, IJudgement judgement, CancellationToken cancel = default(CancellationToken))
        {
            ValidateFrame(frame);
            bool needsJudgement = entries.Any(e => e.State == OperationState.Waiting && ContainsJudgement(e.Definition.When));
            JevAnswers answers = null; string failure = null;
            if (needsJudgement)
            {
                if (judgement == null) failure = "Jev 条件の判定口がない";
                else
                {
                    try
                    {
                        string state = JevState.Build(frame.Observation);
                        string questions = JevQuestions.Build(new JevQuestionContext { OperationConditionNeeded = true, IncludeComprehension = false });
                        var aware = judgement as IQuestionAwareJevTransport;
                        answers = aware == null
                            ? await judgement.AskAsync(state, cancel).ConfigureAwait(false)
                            : await aware.AskAsync(state, questions, cancel).ConfigureAwait(false);
                        if (answers == null) failure = "Jev が答えを返さなかった";
                        // A transport that supports q9 can receive the selected question set through the overload.
                        // The call above is kept for the small IJudgement contract; the state and q9 are still fixed here.
                        _ = questions;
                    }
                    catch (Exception e) when (e is InvalidOperationException || e is FormatException || e is OperationCanceledException || e is System.Net.Http.HttpRequestException)
                    { failure = "Jev の判定に失敗した"; }
                }
            }
            var result = EvaluateCore(frame, answers, failure);
            previousFrame = frame;
            return result;
        }

        private OperationEvaluation EvaluateCore(FactionFrame frame, JevAnswers judgement, string failure)
        {
            var fired = new List<FiredOperation>();
            foreach (var entry in entries.OrderBy(e => e.Id))
            {
                if (entry.State == OperationState.Cancelled || (entry.Definition.Once && entry.State == OperationState.Fired)) continue;
                bool satisfied = EvaluateCondition(entry.Definition.When, frame, previousFrame, entry, judgement);
                bool rising = satisfied && entry.Armed;
                entry.HasPrevious = true; entry.PreviousSatisfied = satisfied;
                if (rising)
                {
                    entry.Armed = false; entry.State = OperationState.Fired;
                    fired.Add(new FiredOperation { Id = entry.Id, Source = entry.Definition.Source,
                        ConditionDescription = entry.Definition.When.Describe(), Actions = entry.Definition.Then });
                }
                else if (!satisfied)
                {
                    entry.Armed = true;
                    if (!entry.Definition.Once) entry.State = OperationState.Waiting;
                }
            }
            return new OperationEvaluation { Fired = fired.AsReadOnly(), JudgementWasUsed = judgement != null,
                JudgementFailure = failure };
        }

        private bool EvaluateCondition(OperationCondition condition, FactionFrame current, FactionFrame previous, Entry entry, JevAnswers judgement)
        {
            switch (condition.Kind)
            {
                case OperationConditionKind.OutpostOwnerChangedToEnemy: return OwnerChanged(condition, current, previous, false);
                case OperationConditionKind.OutpostOwnerChangedToSelf: return OwnerChanged(condition, current, previous, true);
                case OperationConditionKind.EnemyNearObjective:
                {
                    var objective = FindObjective(current, condition); if (!objective.HasValue) return false;
                    int count = current.Observation.VisibleEnemies.Count(e => DistanceMeters(e.Position, objective.Value.Position) <= condition.NearMeters);
                    return count >= condition.EnemyCount;
                }
                case OperationConditionKind.OwnArmyBelowPercent:
                    int baseline = BaselineArmyCount(entry, current);
                    return baseline > 0 && current.Observation.OwnArmies.Sum(a => Math.Max(0, a.AliveCount)) * 100 <= baseline * condition.OwnArmyPercent;
                case OperationConditionKind.MatchTimeAfter: return current.Tick > condition.TimeTick;
                case OperationConditionKind.JudgementTrue:
                    double probability;
                    return judgement != null && judgement.Noul.TryGetValue(condition.JudgementQuestion, out probability)
                        && probability * 1000 >= minJudgementPermille;
                case OperationConditionKind.All: return condition.Children.All(c => EvaluateCondition(c, current, previous, entry, judgement));
                default: return false;
            }
        }

        // Stored at registration without introducing a world reference or a second source of truth.
        private readonly Dictionary<ulong, int> baselineArmy = new Dictionary<ulong, int>();
        private int BaselineArmyCount(Entry entry, FactionFrame current)
        {
            if (!baselineArmy.TryGetValue(entry.Id, out var value))
            {
                value = current.Observation.OwnArmies.Sum(a => Math.Max(0, a.AliveCount));
                baselineArmy[entry.Id] = value;
            }
            return value;
        }

        private static bool OwnerChanged(OperationCondition condition, FactionFrame current, FactionFrame previous, bool toSelf)
        {
            if (previous == null) return false;
            var before = FindObjective(previous, condition); var after = FindObjective(current, condition);
            if (!before.HasValue || !after.HasValue || !before.Value.IsOwnerKnown || !after.Value.IsOwnerKnown) return false;
            bool wasSelf = before.Value.OwnerFactionId == current.FactionId;
            bool isSelf = after.Value.OwnerFactionId == current.FactionId;
            return toSelf ? !wasSelf && isSelf : wasSelf && !isSelf && after.Value.OwnerFactionId != 0;
        }

        private static KnownObjective? FindObjective(FactionFrame frame, OperationCondition condition)
        {
            return frame.Observation.Objectives.Where(o => o.Kind == GoalKind.Outpost &&
                (condition.ObjectiveId != 0 ? o.Id == condition.ObjectiveId : o.Id == 0)).Select(o => (KnownObjective?)o).FirstOrDefault();
        }

        private static long DistanceMeters(SimPoint a, SimPoint b)
        {
            long dx = (a.X.Raw - b.X.Raw) / 65536;
            long dz = (a.Z.Raw - b.Z.Raw) / 65536;
            return (long)Math.Sqrt(dx * dx + dz * dz);
        }

        private static bool ContainsJudgement(OperationCondition c) => c.Kind == OperationConditionKind.JudgementTrue ||
            (c.Kind == OperationConditionKind.All && c.Children.Any(ContainsJudgement));

        private static OperationView View(Entry e) => new OperationView { Id = e.Id, ConditionDescription = e.Definition.When.Describe(), State = e.State, Source = e.Definition.Source };

        private void ValidateFrame(FactionFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (frame.FactionId != factionId || frame.Observation == null || frame.Observation.FactionId != factionId)
                throw new ArgumentException("作戦表は自陣営の FactionFrame だけを受け取ります.", nameof(frame));
        }
    }
}
