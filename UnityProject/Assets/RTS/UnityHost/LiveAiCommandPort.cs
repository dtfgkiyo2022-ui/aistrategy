using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Application;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.UnityHost
{
    public enum AiInstructionState : byte
    {
        Interpreting = 1, Executing = 2, Completed = 3, Cancelled = 4, Expired = 5, Unknown = 6
    }

    /// <summary>Read-only model choice information for the presentation owner.</summary>
    public sealed class LiveAiModelOption
    {
        public string Model { get; }
        public bool Available { get; }
        public int DeadlineTicks { get; }
        public bool SupportsComplexInstructions { get; }
        public decimal InputUsdPerMillion { get; }
        public decimal OutputUsdPerMillion { get; }

        internal LiveAiModelOption(AiModelPrice price, bool available)
        {
            Model = price.Model;
            Available = available;
            DeadlineTicks = price.DeadlineTicks;
            SupportsComplexInstructions = price.SupportsComplexInstructions;
            InputUsdPerMillion = price.InputUsdPerMillion;
            OutputUsdPerMillion = price.OutputUsdPerMillion;
        }
    }

    /// <summary>Read-only status of one spoken instruction. Raw provider text is never placed in replay inputs.</summary>
    public sealed class LiveAiInstruction
    {
        public ulong RequestId { get; internal set; }
        public ulong ReservationId { get; internal set; }
        public ulong HumanRequestId { get; internal set; }
        public uint FactionId { get; internal set; }
        public ScopeKey? FixedTarget { get; internal set; }
        public string Instruction { get; internal set; }
        public string Model { get; internal set; }
        public long StartedTick { get; internal set; }
        public long DeadlineTick { get; internal set; }
        public AiInstructionState State { get; internal set; }
        public string Say { get; internal set; }
        public string Reason { get; internal set; }
        public IReadOnlyList<string> RejectedReasons { get; internal set; }
        public decimal EstimatedCostYen { get; internal set; }
        public decimal ActualCostYen { get; internal set; }

        internal IReadOnlyList<UserPolicyIntent> Policies { get; set; } = Array.Empty<UserPolicyIntent>();
    }

    /// <summary>G-4 will populate this view; the G-5 branch deliberately exposes an empty read-only list.</summary>
    public sealed class LiveOperationView
    {
        public string Name { get; internal set; }
        public string Status { get; internal set; }
    }

    /// <summary>
    /// Presentation-facing, non-deterministic command adapter. It opens the Application reservation before asking a
    /// model, turns a completed answer into ordinary human gateway inputs, and owns the local cost/status meter.
    /// </summary>
    public sealed class LiveAiCommandPort : IDisposable
    {
        private readonly CommandGateway gateway;
        private readonly Func<FactionFrame> frame;
        private readonly CommandInterpreterCoordinator coordinator;
        private readonly IDisposable ownedInterpreter;
        private readonly Dictionary<ulong, LiveAiInstruction> instructions = new Dictionary<ulong, LiveAiInstruction>();
        private readonly List<LiveAiInstruction> history = new List<LiveAiInstruction>();
        private readonly AiBudgetMeter budget;

        public LiveAiCommandPort(CommandGateway gateway, Func<FactionFrame> frame = null, decimal budgetYen = 3m)
        {
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.frame = frame;
            var runtime = new RuntimeCommandInterpreterRouter();
            ownedInterpreter = runtime;
            coordinator = new CommandInterpreterCoordinator(runtime);
            budget = new AiBudgetMeter(budgetYen);
        }

        /// <summary>Test/development constructor; the supplied interpreter prevents any real network access.</summary>
        public LiveAiCommandPort(CommandGateway gateway, ICommandInterpreter interpreter, Func<FactionFrame> frame = null,
            decimal budgetYen = 3m)
        {
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.frame = frame;
            coordinator = new CommandInterpreterCoordinator(interpreter ?? throw new ArgumentNullException(nameof(interpreter)));
            budget = new AiBudgetMeter(budgetYen);
        }

        public IReadOnlyList<LiveAiModelOption> Models => AiModelCatalog.All
            .Select(p => new LiveAiModelOption(p, AiModelAvailability.IsConfigured(p.Model))).ToArray();
        public IReadOnlyList<LiveAiInstruction> Instructions => Array.AsReadOnly(history.ToArray());
        public IReadOnlyList<LiveOperationView> Operations => Array.Empty<LiveOperationView>();
        public decimal MatchCostYen => budget.SpentYen;
        public decimal BudgetYen => budget.BudgetYen;
        public decimal RemainingBudgetYen => budget.RemainingYen;
        // The free allowance is intentionally zero until the Ver.4 account/service work exists.
        public decimal RemainingFreeYen => 0m;

        public decimal Estimate(string instruction, string model, ScopeKey? fixedTarget = null)
        {
            var f = frame == null ? null : frame();
            if (f == null) throw new InvalidOperationException("試合が開始されていません。");
            var selected = AiModelCatalog.Get(model ?? "gpt-6-luna");
            var summary = AiSituationSummary.From(f);
            return AiCostCalculator.EstimateYen(selected.Model, summary.Prompt(instruction ?? "",
                fixedTarget), 200);
        }

        public ulong BeginInterpretation(string instruction, ScopeKey? fixedTarget, string model)
        {
            var f = frame == null ? null : frame();
            if (f == null) throw new InvalidOperationException("試合が開始されていません。");
            var selected = AiModelCatalog.Get(model ?? "gpt-6-luna");
            if (!AiModelAvailability.IsConfigured(selected.Model) && ownedInterpreter != null)
                throw new InvalidOperationException("選択したAIはこのPCで使えません。");
            if (fixedTarget.HasValue && fixedTarget.Value.FactionId != f.FactionId)
                throw new ArgumentException("選択対象は自陣営のものにしてください。", nameof(fixedTarget));

            ulong reservation = gateway.BeginInterpretation(f.FactionId, fixedTarget);
            try
            {
                ulong request = coordinator.Request(instruction ?? "", f, fixedTarget, selected.Model, f.Tick,
                    selected.DeadlineTicks);
                var item = new LiveAiInstruction
                {
                    RequestId = request,
                    ReservationId = reservation,
                    FactionId = f.FactionId,
                    FixedTarget = fixedTarget,
                    Instruction = instruction ?? "",
                    Model = selected.Model,
                    StartedTick = f.Tick,
                    DeadlineTick = checked(f.Tick + selected.DeadlineTicks),
                    State = AiInstructionState.Interpreting,
                    Say = "",
                    Reason = "",
                    RejectedReasons = Array.Empty<string>(),
                    EstimatedCostYen = Estimate(instruction, selected.Model, fixedTarget)
                };
                instructions.Add(request, item);
                history.Add(item);
                return request;
            }
            catch
            {
                gateway.EndInterpretation(reservation);
                throw;
            }
        }

        /// <summary>Polls the provider without waiting; call once per Unity/game tick.</summary>
        public void Poll(long tick)
        {
            // HTTP providers may still be waiting on a task after the game-side deadline. The reservation belongs to
            // the game clock, so it must close here even when no provider reply has arrived yet.
            foreach (var item in history.Where(i => i.State == AiInstructionState.Interpreting && tick > i.DeadlineTick).ToArray())
            {
                item.State = AiInstructionState.Expired;
                item.Reason = "締め切りを過ぎた答え";
                if (gateway.IsInterpretationOpen(item.ReservationId)) gateway.EndInterpretation(item.ReservationId);
            }
            foreach (var reply in coordinator.Poll(tick))
            {
                if (!instructions.TryGetValue(reply.RequestId, out var item)) continue;
                if (item.State != AiInstructionState.Interpreting) continue;
                item.ActualCostYen = reply.CostYen;
                budget.Add(reply.CostYen);
                var result = reply.Result ?? new AiCommandInterpretationResult { Unknown = true, Reason = "答えがありません。" };
                item.Say = result.Say ?? "";
                item.Reason = result.Reason ?? "";
                item.RejectedReasons = result.Rejected == null ? Array.Empty<string>() :
                    result.Rejected.Select(r => r.Reason ?? "命令を読めませんでした。").ToArray();
                item.Policies = result.Policies ?? Array.Empty<UserPolicyIntent>();

                if (reply.Late || result.Unknown || (result.Policies.Count == 0 && result.EconomyCommands.Count == 0))
                {
                    item.State = reply.Late ? AiInstructionState.Expired : AiInstructionState.Unknown;
                    if (reply.Late && string.IsNullOrEmpty(item.Reason)) item.Reason = "締め切りを過ぎた答え";
                    if (string.IsNullOrEmpty(item.Reason) && item.RejectedReasons.Count != 0)
                        item.Reason = item.RejectedReasons[0];
                    gateway.EndInterpretation(item.ReservationId);
                    continue;
                }

                try
                {
                    if (result.Policies.Count != 0) item.HumanRequestId = gateway.SubmitBatch(result.Policies);
                    foreach (var command in result.EconomyCommands) gateway.SubmitEconomy(command);
                    item.State = AiInstructionState.Executing;
                }
                catch (Exception e)
                {
                    item.State = AiInstructionState.Unknown;
                    item.Reason = e.Message;
                }
                finally
                {
                    gateway.EndInterpretation(item.ReservationId);
                }
            }
            Refresh(frame == null ? null : frame());
        }

        /// <summary>Updates executing/completed/expired display states from the latest simulation frame.</summary>
        public void Refresh(FactionFrame latest)
        {
            if (latest == null) return;
            foreach (var item in history.Where(i => i.State == AiInstructionState.Executing && i.Policies.Count != 0))
            {
                var views = latest.Commands.Where(c => c.Source == CommandSource.Human && c.AcceptedTick >= item.StartedTick &&
                    item.Policies.Any(p => p.Target.Equals(c.Target) && p.Kind == c.Kind)).ToArray();
                if (views.Length == 0) continue;
                if (views.Any(v => v.Status == CommandStatus.Cancelled)) item.State = AiInstructionState.Cancelled;
                else if (views.Any(v => v.Status == CommandStatus.Expired || v.Status == CommandStatus.Impossible)) item.State = AiInstructionState.Expired;
                else if (views.All(v => v.Status == CommandStatus.Completed)) item.State = AiInstructionState.Completed;
            }
        }

        /// <summary>Cancel does not ask a model. It closes the reservation and cancels a queued human request.</summary>
        public bool CancelLast()
        {
            var item = history.LastOrDefault(i => i.State == AiInstructionState.Interpreting || i.State == AiInstructionState.Executing);
            if (item == null) return false;
            if (item.HumanRequestId != 0) gateway.Cancel(item.HumanRequestId);
            if (gateway.IsInterpretationOpen(item.ReservationId)) gateway.EndInterpretation(item.ReservationId);
            item.State = AiInstructionState.Cancelled;
            item.Reason = "人が取り消しました。";
            return true;
        }

        public void Dispose() { ownedInterpreter?.Dispose(); }
    }

    /// <summary>Routes concurrent requests to one provider instance per model and returns replies canonically.</summary>
    internal sealed class RuntimeCommandInterpreterRouter : ICommandInterpreter, IDisposable
    {
        private readonly Dictionary<string, ICommandInterpreter> children = new Dictionary<string, ICommandInterpreter>(StringComparer.OrdinalIgnoreCase);

        public void Request(InterpreterRequest request)
        {
            Get(request.Model).Request(request);
        }

        public IReadOnlyList<InterpreterReply> Poll(long tick)
        {
            var all = new List<InterpreterReply>();
            foreach (var child in children.Values) all.AddRange(child.Poll(tick));
            return all.OrderBy(r => r.RequestId).ToArray();
        }

        private ICommandInterpreter Get(string model)
        {
            if (children.TryGetValue(model ?? "", out var existing)) return existing;
            ICommandInterpreter created;
            if ((model ?? "").StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
                created = new ClaudeCommandInterpreter(() => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
            else if ((model ?? "").StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
                created = new OpenAiCommandInterpreter(() => Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
            else if (string.Equals(model, "local-llm", StringComparison.OrdinalIgnoreCase))
                created = new LocalLlmCommandInterpreter(Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL") ?? "qwen3.5:4b",
                    url: Environment.GetEnvironmentVariable("LOCAL_LLM_URL") ?? LocalLlmCommandInterpreter.DefaultUrl);
            else if (string.Equals(model, "jev", StringComparison.OrdinalIgnoreCase))
                created = new JevCommandInterpreter(() => Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));
            else throw new ArgumentException("未知のモデルです。", nameof(model));
            children.Add(model ?? "", created);
            return created;
        }

        public void Dispose()
        {
            foreach (var child in children.Values) (child as IDisposable)?.Dispose();
            children.Clear();
        }
    }
}
