using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Tactics
{
    public interface ITacticRuntime
    {
        string Name { get; }
        void Start(string setupJson);
        string Tick(string viewJson);
    }

    /// <summary>Optional diagnostics emitted by a tactic runtime for the current call.</summary>
    public interface ITacticLogSource
    {
        IReadOnlyList<string> TakeConsoleLines();
    }

    /// <summary>Optional receiver for the three whole-faction doctrine shortcuts.</summary>
    public interface ITacticGlobalPolicyPort
    {
        void SetGlobalPolicy(uint factionId, string policy, long observedTick);
    }

    public sealed class TacticFailure
    {
        public long Tick { get; internal set; }
        public string RuntimeName { get; internal set; }
        public string Reason { get; internal set; }
        public int Consecutive { get; internal set; }
    }

    public sealed class TacticHostTickResult
    {
        public long Tick { get; internal set; }
        public bool Called { get; internal set; }
        public string ViewJson { get; internal set; }
        public string CommandJson { get; internal set; }
        public TacticCommandResult Commands { get; internal set; }
        public TacticFailure Failure { get; internal set; }
        public bool Disabled { get; internal set; }
        public int SentPolicies { get; internal set; }
        public int SentEconomy { get; internal set; }
        public IReadOnlyList<string> ConsoleLines { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> ConsoleLog => ConsoleLines;
    }

    /// <summary>Calls one faction's tactic every twenty simulation ticks and sends ordinary logged proposals.</summary>
    public sealed class TacticHost
    {
        public const int DecisionIntervalTicks = 20;
        public const int FailureLimit = 10;
        private readonly uint factionId;
        private readonly IFrameSource frames;
        private readonly ICommandPort commandPort;
        private readonly IEconomyPort economyPort;
        private readonly ITacticRuntime runtime;
        private readonly ITacticGlobalPolicyPort globalPolicyPort;
        private readonly List<TacticFailure> failures = new List<TacticFailure>();
        private readonly List<string> recentConsoleLines = new List<string>();
        private ulong sequence = 1;
        private int consecutiveFailures;
        private bool started;
        private bool disabled;
        private long lastCallTick = -1;
        private int sentCommandCount;
        private int rejectedCommandCount;

        public TacticHost(uint factionId, IFrameSource frames, ICommandPort commandPort, IEconomyPort economyPort, ITacticRuntime runtime, ITacticGlobalPolicyPort globalPolicyPort = null)
        {
            if (factionId < 1 || factionId > 2) throw new ArgumentOutOfRangeException(nameof(factionId));
            this.factionId = factionId; this.frames = frames ?? throw new ArgumentNullException(nameof(frames)); this.commandPort = commandPort ?? throw new ArgumentNullException(nameof(commandPort)); this.economyPort = economyPort; this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); this.globalPolicyPort = globalPolicyPort;
        }

        public string Name => runtime.Name;
        public bool Disabled => disabled;
        public IReadOnlyList<TacticFailure> Failures => failures.AsReadOnly();
        public TacticFailure LastFailure => failures.Count == 0 ? null : failures[failures.Count - 1];
        public long LastCallTick => lastCallTick;
        public int SentCommandCount => sentCommandCount;
        public int RejectedCommandCount => rejectedCommandCount;
        public IReadOnlyList<string> RecentConsoleLines => recentConsoleLines.AsReadOnly();

        public void Start(string setupJson = "{}")
        {
            if (started) throw new InvalidOperationException("TacticHost has already started.");
            started = true;
            try { runtime.Start(setupJson ?? "{}"); }
            catch (Exception e) { RecordFailure(0, e); }
        }

        public TacticHostTickResult Tick()
        {
            var frame = frames.Latest(factionId) ?? throw new InvalidOperationException("Frame source returned null.");
            var result = new TacticHostTickResult { Tick = frame.Tick, Commands = new TacticCommandResult() };
            if (!started) Start("{}");
            CaptureConsoleLines(result);
            if (disabled || frame.Tick % DecisionIntervalTicks != 0) { result.Disabled = disabled; return result; }
            result.Called = true;
            lastCallTick = frame.Tick;
            try
            {
                result.ViewJson = TacticViewWriter.Write(frame);
                result.CommandJson = runtime.Tick(result.ViewJson) ?? throw new InvalidOperationException("戦術がnullの命令JSONを返しました。");
                CaptureConsoleLines(result);
                result.Commands = TacticCommandReader.Read(result.CommandJson, frame);
                if (result.Commands.IsMalformed) throw new FormatException(result.Commands.Error ?? "命令JSONを読めません。");
                rejectedCommandCount += result.Commands.Rejected.Count;
                if (result.Commands.GlobalPolicy != null) globalPolicyPort?.SetGlobalPolicy(factionId, result.Commands.GlobalPolicy, frame.Tick);
                if (result.Commands.Policies.Count != 0)
                {
                    commandPort.Propose(factionId, sequence++, result.Commands.Policies, checked(frame.Tick + 1));
                    result.SentPolicies = result.Commands.Policies.Count;
                }
                if (economyPort != null) foreach (var command in result.Commands.EconomyCommands) { economyPort.SubmitEconomy(command); result.SentEconomy++; }
                sentCommandCount += result.SentPolicies + result.SentEconomy;
                consecutiveFailures = 0;
            }
            catch (Exception e)
            {
                CaptureConsoleLines(result);
                result.Failure = RecordFailure(frame.Tick, e); result.Disabled = disabled; result.Commands = new TacticCommandResult();
            }
            return result;
        }

        public TacticHostTickResult OnTick() => Tick();

        private TacticFailure RecordFailure(long tick, Exception error)
        {
            consecutiveFailures++;
            if (consecutiveFailures >= FailureLimit) disabled = true;
            var failure = new TacticFailure { Tick = tick, RuntimeName = Name, Reason = error.GetType().Name + ": " + error.Message, Consecutive = consecutiveFailures };
            failures.Add(failure); return failure;
        }

        private void CaptureConsoleLines(TacticHostTickResult result)
        {
            if (!(runtime is ITacticLogSource source)) return;
            var lines = source.TakeConsoleLines();
            if (lines != null && lines.Count != 0)
            {
                result.ConsoleLines = lines;
                recentConsoleLines.AddRange(lines);
                while (recentConsoleLines.Count > RecentConsoleLimit) recentConsoleLines.RemoveAt(0);
            }
        }

        // Kept here instead of depending on the JavaScript assembly: native tactics can also expose logs.
        private const int RecentConsoleLimit = 20;
    }
}
