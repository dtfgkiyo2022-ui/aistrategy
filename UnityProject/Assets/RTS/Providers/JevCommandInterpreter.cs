using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>
    /// Jev adapter for the G-5 spoken-command port. Jev is a choice model, so only one short policy is produced;
    /// complex or low-confidence answers become an explicit unknown result. Network work stays in Providers.
    /// </summary>
    public sealed class JevCommandInterpreter : ICommandInterpreter, IDisposable
    {
        private sealed class Pending
        {
            internal InterpreterRequest Request;
            internal Task<JevAnswers> Task;
            internal string PreflightJson;
        }

        private readonly Func<string> readKey;
        private readonly List<Pending> pending = new List<Pending>();
        private readonly object gate = new object();
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private HttpJevTransport transport;
        private bool disposed;

        public JevCommandInterpreter(Func<string> readKey)
            : this(readKey, null)
        {
        }

        /// <summary>テストでは偽物のtransportを渡し、本物のJevへ接続せずにstateから返答まで確認できる。</summary>
        public JevCommandInterpreter(Func<string> readKey, IJevTransport transport)
        {
            this.readKey = readKey ?? throw new ArgumentNullException(nameof(readKey));
            this.transport = transport as HttpJevTransport;
            suppliedTransport = transport;
        }

        private readonly IJevTransport suppliedTransport;

        public void Request(InterpreterRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (disposed) throw new ObjectDisposedException(nameof(JevCommandInterpreter));
            string unknownName = request.Summary == null ? null : request.Summary.FindUnknownInstructionName(request.Instruction);
            if (!string.IsNullOrEmpty(unknownName))
            {
                lock (gate) pending.Add(new Pending { Request = request, PreflightJson = JsonValueWriter.Write(new Dictionary<string, object>
                {
                    ["unknown"] = true, ["reason"] = "表にない名前（" + unknownName + "）", ["commands"] = Array.Empty<object>()
                }) });
                return;
            }
            if (transport == null && suppliedTransport == null) transport = new HttpJevTransport(readKey);
            var signals = request.Summary?.TacticInfo?.Signals ?? Array.Empty<AiTacticSignalInfo>();
            var regions = (request.Summary?.NameTable ?? Array.Empty<AiNameTableEntry>())
                .Where(e => e != null && e.HasScope && e.Scope.Kind == Rts.Contracts.ScopeKind.Region)
                .Select(e => e.Name).Distinct(StringComparer.Ordinal).ToArray();
            bool hasPrevious = request.Summary?.LatestConversationTurn != null;
            string questions = JevQuestions.Build(new JevQuestionContext
            {
                InstructionTranslationNeeded = true,
                IncludeComprehension = false,
                SignalDefinitions = signals,
                AvailableLineNames = request.Summary?.AvailableLineNames,
                RegionNames = regions,
                PreviousConversationAvailable = hasPrevious
            });
            string state = BuildState(request);
            Task<JevAnswers> task = Task.Run(async () => suppliedTransport is IQuestionAwareJevTransport suppliedAware
                ? await suppliedAware.AskAsync(state, questions, cancel.Token).ConfigureAwait(false)
                : suppliedTransport != null
                    ? await suppliedTransport.AskAsync(state, cancel.Token).ConfigureAwait(false)
                    : transport is IQuestionAwareJevTransport aware
                        ? await aware.AskAsync(state, questions, cancel.Token).ConfigureAwait(false)
                        : await transport.AskAsync(state, cancel.Token).ConfigureAwait(false));
            lock (gate) pending.Add(new Pending { Request = request, Task = task });
        }

        /// <summary>Jevへ渡すstateを組み立てる。会話がないときは従来の5項目だけを出す。</summary>
        public static string BuildState(InterpreterRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var state = new Dictionary<string, object>
            {
                ["stateVersion"] = "command-g5",
                ["summary"] = request.Summary == null ? "" : request.Summary.Text ?? "",
                ["instruction"] = request.Instruction ?? "",
                ["conversation"] = request.Summary == null ? "" : request.Summary.Conversation ?? "",
                ["names"] = (request.Summary == null ? Array.Empty<object>() : request.Summary.NameTable.Select(n => (object)n.Name).ToArray())
            };
            var previous = request.Summary?.LatestConversationTurn;
            if (previous != null)
            {
                state["stateVersion"] = "command-g6";
                state["previous"] = new Dictionary<string, object>
                {
                    ["instruction"] = "直前の指示: " + previous.Instruction,
                    ["reply"] = "直前の参謀の返事: " + previous.Reply,
                    ["command"] = "直前に出した命令の種類・対象・目標: " + previous.Commands,
                    ["result"] = "直前の結果: " + previous.Result
                };
                state["instructionContext"] = "今回の指示: " + (request.Instruction ?? "") +
                    "。今回の指示に書かれていない種類・対象・目標は、直前の指示から引き継ぐ。" +
                    "直前の指示と無関係なら、今回の指示だけで決める。";
            }
            return JsonValueWriter.Write(state);
        }

        public IReadOnlyList<InterpreterReply> Poll(long tick)
        {
            var ready = new List<InterpreterReply>();
            lock (gate)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var p = pending[i];
                    if (p.PreflightJson == null && !p.Task.IsCompleted) continue;
                    pending.RemoveAt(i);
                    try
                    {
                        var answers = p.PreflightJson == null ? p.Task.GetAwaiter().GetResult() : null;
                        ready.Add(new InterpreterReply
                        {
                            RequestId = p.Request.RequestId,
                            ReturnedTick = tick,
                            Json = p.PreflightJson ?? ToCommandJson(p.Request, answers),
                            Usage = new AiTokenUsage((int)Math.Max(0, answers?.Usage?.InputTokens ?? 0),
                                (int)Math.Max(0, answers?.Usage?.OutputTokens ?? 0)),
                            Model = p.Request.Model
                        });
                    }
                    catch (Exception)
                    {
                        ready.Add(new InterpreterReply { RequestId = p.Request.RequestId, ReturnedTick = tick,
                            Model = p.Request.Model, Usage = new AiTokenUsage(0, 0), FailureReason = "Jevの答えを読めませんでした。" });
                    }
                }
            }
            ready.Sort((a, b) => a.RequestId.CompareTo(b.RequestId));
            return ready;
        }

        /// <summary>The name-table entry for the whole own force (AiSituationSummary adds it to every summary).</summary>
        private const string AllArmiesScope = "全部隊";

        internal static string ToCommandJson(InterpreterRequest request, JevAnswers answers)
        {
            if (answers == null) return "{\"unknown\":true,\"reason\":\"Jevの答えがありません。\",\"commands\":[]}";
            string kind = Choice(answers, "instruction_kind");
            string requestedLine = Choice(answers, "instruction_line");
            bool confidentLine = requestedLine != null && requestedLine != JevChoice.Unknown &&
                Confidence(answers, "instruction_line") >= 0.6;
            // The real Jev hardly ever answers "signal" for the kind: "総攻撃して" comes back as focus and "いったん下がって"
            // as retreat, each with the target unknown, while the signal question is answered at 0.99-1.0 (10-08,
            // signalprompt set). So an attack, defence or retreat with no target is a signal too when one is picked
            // with confidence - the same rule the LLM prompt gives (rule 14). A named target keeps the policy.
            string pickedSignal = Choice(answers, "instruction_signal");
            string answeredTarget = Choice(answers, "instruction_target");
            bool untargetedSignal = (kind == "focus" || kind == JevChoice.Defend || kind == JevChoice.Retreat)
                && (answeredTarget == null || answeredTarget == JevChoice.Unknown)
                && pickedSignal != null && pickedSignal != JevChoice.Unknown
                && Confidence(answers, "instruction_signal") >= 0.6;
            if (kind == "signal" || untargetedSignal)
            {
                string signalName = pickedSignal;
                if (Confidence(answers, "instruction_kind") < 0.6 ||
                    Confidence(answers, "instruction_signal") < 0.6 ||
                    signalName == JevChoice.Unknown)
                    return UnknownJson("戦術の合図を確定できません。", "どの合図を送りますか？");

                var signal = (request.Summary?.TacticInfo?.Signals ?? Array.Empty<AiTacticSignalInfo>())
                    .FirstOrDefault(s => s != null && s.Name == signalName);
                if (signal == null)
                    return UnknownJson("戦術の合図を確定できません。", "どの合図を送りますか？");
                if (signal.NeedsPoint && !request.HasFixedTarget)
                    return UnknownJson("地点を選んでから話しかけてください。", "どの地点に合図を送りますか？");

                var result = new Dictionary<string, object>
                {
                    ["kind"] = "SendTacticSignal",
                    ["tacticSignal"] = signal.Name,
                    ["tacticSignalX"] = 0d,
                    ["tacticSignalZ"] = 0d,
                    ["reason"] = ""
                };
                if (signal.NeedsPoint)
                {
                    var selected = (request.Summary?.NameTable ?? Array.Empty<AiNameTableEntry>())
                        .FirstOrDefault(e => e != null && e.HasScope && e.Scope.Equals(request.FixedTarget) && e.HasGoal);
                    if (selected == null)
                        return UnknownJson("地点を選んでから話しかけてください。", "どの地点に合図を送りますか？");
                    result["tacticSignalX"] = selected.Point.X.Raw / 65536d;
                    result["tacticSignalZ"] = selected.Point.Z.Raw / 65536d;
                }
                result["say"] = "了解です。" + (signal.Label ?? signal.Name) + "の合図を送ります。";
                return JsonValueWriter.Write(result);
            }
            string target = Choice(answers, "instruction_target");
            string goal = Choice(answers, "instruction_goal");
            if (Confidence(answers, "instruction_kind") < 0.6 || kind == JevChoice.Unknown)
                return UnknownJson("Jevでは短い定型の指示だけを解釈できます。", "攻める、守る、引くなどの短い指示で教えてください。");

            if (kind == "doctrine")
            {
                string doctrine = Choice(answers, "instruction_doctrine");
                if (Confidence(answers, "instruction_doctrine") < 0.6 ||
                    (doctrine != "none" && doctrine != "maintain" && doctrine != "concentrate"))
                    return UnknownJson("全体方針を確定できません。", "攻め気味、守り気味のどちらにしますか？");
                string doctrineSay = doctrine == "concentrate" ? "攻め気味" : doctrine == "maintain" ? "守り気味" : "自分で担当";
                return JsonValueWriter.Write(new Dictionary<string, object>
                {
                    ["kind"] = "SetDoctrine", ["scope"] = "", ["goal"] = "", ["region"] = "", ["control"] = "",
                    ["doctrine"] = doctrine, ["reason"] = "", ["say"] = "了解です。" + doctrineSay + "の全体方針にします。"
                });
            }

            // "内政" is the older broad category. If Jev also selected a line with confidence,
            // preserve the specific request instead of falling back to Growth.
            if ((kind == "line" || kind == JevChoice.Economy && confidentLine) && confidentLine)
            {
                var available = request.Summary?.AvailableLineNames ?? Array.Empty<string>();
                if (!available.Contains(requestedLine, StringComparer.Ordinal))
                    return UnknownJson("作れるラインを確定できません。", "どの加工ラインを作りますか？");
                string region = Choice(answers, "instruction_region");
                bool hasRegions = (request.Summary?.NameTable ?? Array.Empty<AiNameTableEntry>())
                    .Any(e => e != null && e.HasScope && e.Scope.Kind == Rts.Contracts.ScopeKind.Region);
                if (hasRegions && (region == null || region == JevChoice.Unknown || Confidence(answers, "instruction_region") < 0.6))
                    return UnknownJson("ラインを置く区域を確定できません。", "どの区域にラインを置きますか？");
                var lineCommand = new Dictionary<string, object>
                {
                    ["type"] = "economy", ["kind"] = "RequestLine", ["line"] = requestedLine,
                    ["region"] = hasRegions ? region : "", ["location"] = hasRegions ? "" : "お任せ"
                };
                return JsonValueWriter.Write(new Dictionary<string, object>
                {
                    ["say"] = "了解です。" + LineLabel(requestedLine) + "の加工ラインを作ります。",
                    ["commands"] = new List<object> { lineCommand }
                });
            }

            if (Confidence(answers, "instruction_target") < 0.6 || target == JevChoice.Unknown)
                return UnknownJson("対象を確定できません。", MissingTargetSay(kind));

            var targetEntry = FindTarget(request.Summary, target);
            // The answered target is where to attack or defend, not who goes: "北の拠点を守れ" with nothing selected is
            // every army defending that outpost, and "敵コアを攻めろ" must not be refused for naming an enemy (10-08,
            // signalprompt set). A retreat names who pulls back, so an own scope stays the scope; otherwise everyone.
            string scopeName = null;
            if (!request.HasFixedTarget)
            {
                bool ownScope = targetEntry != null && targetEntry.HasScope && targetEntry.IsOwn;
                scopeName = kind == JevChoice.Retreat && ownScope ? targetEntry.Name : AllArmiesScope;
            }
            if ((kind == "focus" || kind == "defend") && Confidence(answers, "instruction_goal") < 0.6)
                return UnknownJson("目標の確信度が足りません。", kind == "focus" ? "どの目標を攻めますか？" : "どの目標を守りますか？");

            if (kind == "economy")
                return JsonValueWriter.Write(new Dictionary<string, object>
                {
                    ["say"] = "了解です。内政を成長重視にします。",
                    ["commands"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "economy", ["kind"] = "SetEconomyPolicy", ["policy"] = "Growth"
                        }
                    }
                });
            string policy = kind == "focus" ? "Focus" : kind == "defend" ? "Defend" : kind == "retreat" ? "Retreat" : null;
            if (policy == null) return UnknownJson("対応していない指示です。", "どのような命令にしますか？");

            var command = new Dictionary<string, object> { ["type"] = "policy", ["kind"] = policy };
            if (scopeName != null) command["scope"] = scopeName;
            if (policy == "Focus" || policy == "Defend")
            {
                var goalEntry = targetEntry != null && targetEntry.HasGoal ? targetEntry : FirstGoal(request.Summary);
                if (goalEntry == null || !goalEntry.HasGoal)
                    return UnknownJson("目標を確定できません。", MissingTargetSay(kind));
                command["goal"] = goalEntry.Name;
                return JsonValueWriter.Write(new Dictionary<string, object>
                {
                    ["say"] = PolicySay(policy, scopeName, goalEntry.Name),
                    ["commands"] = new List<object> { command }
                });
            }
            return JsonValueWriter.Write(new Dictionary<string, object>
            {
                ["say"] = PolicySay(policy, scopeName, null),
                ["commands"] = new List<object> { command }
            });
        }

        private static string UnknownJson(string reason, string say)
        {
            return JsonValueWriter.Write(new Dictionary<string, object>
            {
                ["unknown"] = true, ["reason"] = reason ?? "", ["say"] = say ?? "", ["commands"] = Array.Empty<object>()
            });
        }

        private static string MissingTargetSay(string kind)
        {
            if (kind == "focus") return "どこを攻めますか？";
            if (kind == "defend") return "どこを守りますか？";
            if (kind == "retreat") return "どの部隊を引きますか？";
            return "対象を指定してください。";
        }

        private static string PolicySay(string policy, string scope, string goal)
        {
            string subject = scope == AllArmiesScope || string.IsNullOrEmpty(scope) ? "全軍" : scope;
            if (policy == "Focus") return "了解です。" + subject + "で" + goal + "を攻めます。";
            if (policy == "Defend") return "了解です。" + subject + "で" + goal + "を守ります。";
            return "了解です。" + subject + "を引きます。";
        }

        private static string LineLabel(string line)
        {
            if (line == nameof(ProcessingLineKind.CoreMetal)) return "金属";
            if (line == nameof(ProcessingLineKind.Steel)) return "鋼";
            if (line == nameof(ProcessingLineKind.CoreWood)) return "木材";
            if (line == nameof(ProcessingLineKind.BowGear)) return "弓具";
            if (line == nameof(ProcessingLineKind.Rations)) return "糧食";
            return line ?? "加工";
        }

        private static string Choice(JevAnswers answers, string name) =>
            answers.Choices != null && answers.Choices.TryGetValue(name, out var value) ? value : null;

        private static double Confidence(JevAnswers answers, string name) =>
            answers.ChoiceConfidences != null && answers.ChoiceConfidences.TryGetValue(name, out var value) ? value : 0;

        private static AiNameTableEntry FindTarget(InterpreterRequest request, string choice)
        {
            return FindTarget(request == null ? null : request.Summary, choice);
        }

        private static AiNameTableEntry FindTarget(AiSituationSummary summary, string choice)
        {
            if (summary == null) return null;
            var entries = summary.NameTable ?? Array.Empty<AiNameTableEntry>();
            if (choice == JevChoice.MyCore) return entries.FirstOrDefault(e => e.HasGoal && e.Name == "自軍コア");
            if (choice == JevChoice.EnemyCore) return entries.FirstOrDefault(e => e.HasGoal && e.Name == "敵コア");
            var posts = entries.Where(e => e.HasGoal && e.Goal.Kind == Rts.Contracts.GoalKind.Outpost).OrderByDescending(e => e.Point.Z.Raw).ToArray();
            if (choice == JevChoice.NorthOutpost) return posts.FirstOrDefault();
            if (choice == JevChoice.SouthOutpost) return posts.Skip(1).FirstOrDefault() ?? posts.FirstOrDefault();
            return null;
        }

        private static AiNameTableEntry FirstGoal(AiSituationSummary summary) =>
            (summary?.NameTable ?? Array.Empty<AiNameTableEntry>()).FirstOrDefault(e => e.HasGoal && e.IsOwn);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cancel.Cancel();
            cancel.Dispose();
        }
    }
}
