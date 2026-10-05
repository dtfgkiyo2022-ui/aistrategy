using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        {
            this.readKey = readKey ?? throw new ArgumentNullException(nameof(readKey));
        }

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
            if (transport == null) transport = new HttpJevTransport(readKey);
            string questions = JevQuestions.Build(new JevQuestionContext { InstructionTranslationNeeded = true, IncludeComprehension = false });
            string state = JsonValueWriter.Write(new Dictionary<string, object>
            {
                ["stateVersion"] = "command-g5",
                ["summary"] = request.Summary == null ? "" : request.Summary.Text ?? "",
                ["instruction"] = request.Instruction ?? "",
                ["names"] = (request.Summary == null ? Array.Empty<object>() : request.Summary.NameTable.Select(n => (object)n.Name).ToArray())
            });
            Task<JevAnswers> task = Task.Run(async () => transport is IQuestionAwareJevTransport aware
                ? await aware.AskAsync(state, questions, cancel.Token).ConfigureAwait(false)
                : await transport.AskAsync(state, cancel.Token).ConfigureAwait(false));
            lock (gate) pending.Add(new Pending { Request = request, Task = task });
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

        private static string ToCommandJson(InterpreterRequest request, JevAnswers answers)
        {
            if (answers == null) return "{\"unknown\":true,\"reason\":\"Jevの答えがありません。\",\"commands\":[]}";
            string kind = Choice(answers, "instruction_kind");
            string target = Choice(answers, "instruction_target");
            string goal = Choice(answers, "instruction_goal");
            if (Confidence(answers, "instruction_kind") < 0.6 || kind == JevChoice.Unknown ||
                Confidence(answers, "instruction_target") < 0.6 || target == JevChoice.Unknown)
                return "{\"unknown\":true,\"reason\":\"Jevでは短い定型の指示だけを解釈できます。\",\"commands\":[]}";

            var targetEntry = FindTarget(request.Summary, target);
            var scopeEntry = request.HasFixedTarget ? null : targetEntry;
            if (!request.HasFixedTarget && (scopeEntry == null || !scopeEntry.HasScope || !scopeEntry.IsOwn))
                return "{\"unknown\":true,\"reason\":\"対象を名前表から確定できません。\",\"commands\":[]}";
            if ((kind == "focus" || kind == "defend") && Confidence(answers, "instruction_goal") < 0.6)
                return "{\"unknown\":true,\"reason\":\"目標の確信度が足りません。\",\"commands\":[]}";

            if (kind == "economy")
                return "{\"say\":\"内政を指示します。\",\"commands\":[{\"type\":\"economy\",\"kind\":\"SetEconomyPolicy\",\"policy\":\"Growth\"}]}";
            string policy = kind == "focus" ? "Focus" : kind == "defend" ? "Defend" : kind == "retreat" ? "Retreat" : null;
            if (policy == null) return "{\"unknown\":true,\"reason\":\"対応していない指示です。\",\"commands\":[]}";

            var command = new Dictionary<string, object> { ["type"] = "policy", ["kind"] = policy };
            if (!request.HasFixedTarget) command["scope"] = scopeEntry.Name;
            if (policy == "Focus" || policy == "Defend")
            {
                var goalEntry = targetEntry != null && targetEntry.HasGoal ? targetEntry : FirstGoal(request.Summary);
                if (goalEntry == null || !goalEntry.HasGoal) return "{\"unknown\":true,\"reason\":\"目標を確定できません。\",\"commands\":[]}";
                command["goal"] = goalEntry.Name;
            }
            return JsonValueWriter.Write(new Dictionary<string, object>
            {
                ["say"] = policy + " を実行します。",
                ["commands"] = new List<object> { command }
            });
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
