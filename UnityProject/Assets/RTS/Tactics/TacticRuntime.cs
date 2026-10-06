using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    /// <summary>Optional limits for runtimes whose failures require an earlier fallback.</summary>
    public interface ITacticFailurePolicy
    {
        int ConsecutiveFailureLimit { get; }
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
        public IReadOnlyList<TacticParamChange> ParamChanges { get; internal set; } = Array.Empty<TacticParamChange>();
    }

    /// <summary>Calls one faction's tactic every twenty simulation ticks and sends ordinary logged proposals.</summary>
    public sealed class TacticHost : IDisposable
    {
        public const int DecisionIntervalTicks = 20;
        public const int FailureLimit = 10;
        private readonly uint factionId;
        private readonly IFrameSource frames;
        private readonly ICommandPort commandPort;
        private readonly IEconomyPort economyPort;
        private readonly ITacticRuntime runtime;
        private readonly ITacticGlobalPolicyPort globalPolicyPort;
        private readonly Func<ScopeKey, IReadOnlyList<PolicyVersion>> versions;
        private readonly IReadOnlyList<TacticParamDefinition> parameters;
        private readonly Dictionary<string, object> parameterValues = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly List<TacticParamChange> parameterChanges = new List<TacticParamChange>();
        private readonly List<TacticParamChange> pendingParameterChanges = new List<TacticParamChange>();
        private readonly List<TacticFailure> failures = new List<TacticFailure>();
        private readonly List<string> recentConsoleLines = new List<string>();
        private readonly List<double> callMilliseconds = new List<double>();
        private ulong sequence = 1;
        private int consecutiveFailures;
        private bool started;
        private bool disabled;
        private long lastCallTick = -1;
        private int sentCommandCount;
        private int rejectedCommandCount;

        public TacticHost(uint factionId, IFrameSource frames, ICommandPort commandPort, IEconomyPort economyPort, ITacticRuntime runtime, ITacticGlobalPolicyPort globalPolicyPort = null,
            Func<ScopeKey, IReadOnlyList<PolicyVersion>> versions = null)
        {
            if (factionId < 1 || factionId > 2) throw new ArgumentOutOfRangeException(nameof(factionId));
            this.factionId = factionId; this.frames = frames ?? throw new ArgumentNullException(nameof(frames)); this.commandPort = commandPort ?? throw new ArgumentNullException(nameof(commandPort)); this.economyPort = economyPort; this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); this.globalPolicyPort = globalPolicyPort; this.versions = versions;
            parameters = runtime is ITacticParameterRuntime parameterRuntime ? parameterRuntime.Parameters ?? Array.Empty<TacticParamDefinition>() : Array.Empty<TacticParamDefinition>();
            foreach (var parameter in parameters) parameterValues[parameter.Name] = parameter.DefaultValue;
        }

        public string Name => runtime.Name;
        public bool Disabled => disabled;
        public IReadOnlyList<TacticFailure> Failures => failures.AsReadOnly();
        public TacticFailure LastFailure => failures.Count == 0 ? null : failures[failures.Count - 1];
        public long LastCallTick => lastCallTick;
        public IReadOnlyList<double> CallMilliseconds => callMilliseconds.AsReadOnly();
        public int SentCommandCount => sentCommandCount;
        public int RejectedCommandCount => rejectedCommandCount;
        public IReadOnlyList<string> RecentConsoleLines => recentConsoleLines.AsReadOnly();
        public IReadOnlyList<TacticParamDefinition> Parameters => parameters;
        public IReadOnlyDictionary<string, object> ParamValues => parameterValues;
        public IReadOnlyList<TacticParamChange> ParamChanges => parameterChanges.AsReadOnly();
        public int ParamChangeCount => parameterChanges.Count;
        public TacticParamChange LastParamChange => parameterChanges.Count == 0 ? null : parameterChanges[parameterChanges.Count - 1];

        /// <summary>Changes one declared knob without clamping; invalid values are rejected.</summary>
        public bool SetParam(string name, object value)
        {
            return TrySetParam(name, value, out _);
        }

        /// <summary>
        /// Applies values carried from the previous definition before the replacement runtime starts. This deliberately
        /// does not create a user parameter-change record: reloading is a definition change, not a knob edit.
        /// </summary>
        public void ApplyInitialParameterValues(IReadOnlyDictionary<string, object> values)
        {
            if (started) throw new InvalidOperationException("Initial tactic parameters must be applied before Start().");
            if (values == null) return;
            foreach (var parameter in parameters)
            {
                if (!values.TryGetValue(parameter.Name, out var value)) continue;
                if (parameter.TryNormalize(value, out var normalized, out _)) parameterValues[parameter.Name] = normalized;
            }
            if (runtime is ITacticParameterRuntime parameterRuntime) parameterRuntime.SetParameters(parameterValues);
        }

        public bool TrySetParam(string name, object value, out string reason)
        {
            reason = null;
            TacticParamDefinition definition = null;
            foreach (var candidate in parameters) if (candidate.Name == name) { definition = candidate; break; }
            if (definition == null) { reason = "つまみが見つかりません: " + (name ?? ""); return false; }
            if (!definition.TryNormalize(value, out var normalized, out reason)) return false;
            var old = parameterValues[name];
            if (Equals(old, normalized)) return true;
            long tick = lastCallTick;
            try { var frame = frames.Latest(factionId); if (frame != null) tick = frame.Tick; } catch (Exception) { }
            var change = new TacticParamChange { Tick = tick, Name = name, From = old, To = normalized };
            parameterValues[name] = normalized;
            parameterChanges.Add(change);
            pendingParameterChanges.Add(change);
            if (runtime is ITacticParameterRuntime parameterRuntime) parameterRuntime.SetParameters(parameterValues);
            return true;
        }

        public void Start(string setupJson = "{}")
        {
            if (started) throw new InvalidOperationException("TacticHost has already started.");
            started = true;
            try { runtime.Start(TacticParameterJson.AddParams(setupJson ?? "{}", parameterValues)); }
            catch (Exception e) { RecordFailure(0, e); }
        }

        public TacticHostTickResult Tick()
        {
            var frame = frames.Latest(factionId) ?? throw new InvalidOperationException("Frame source returned null.");
            var result = new TacticHostTickResult { Tick = frame.Tick, Commands = new TacticCommandResult() };
            if (pendingParameterChanges.Count != 0)
            {
                result.ParamChanges = pendingParameterChanges.ToArray();
                pendingParameterChanges.Clear();
            }
            if (!started) Start("{}");
            CaptureConsoleLines(result);
            if (disabled || frame.Tick % DecisionIntervalTicks != 0) { result.Disabled = disabled; return result; }
            result.Called = true;
            lastCallTick = frame.Tick;
            try
            {
                result.ViewJson = TacticViewWriter.Write(frame, parameterValues);
                var watch = Stopwatch.StartNew();
                result.CommandJson = runtime.Tick(result.ViewJson) ?? throw new InvalidOperationException("戦術がnullの命令JSONを返しました。");
                watch.Stop();
                callMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                CaptureConsoleLines(result);
                result.Commands = TacticCommandReader.Read(result.CommandJson, frame);
                if (result.Commands.IsMalformed) throw new FormatException(result.Commands.Error ?? "命令JSONを読めません。");
                rejectedCommandCount += result.Commands.Rejected.Count;
                if (result.Commands.GlobalPolicy != null) globalPolicyPort?.SetGlobalPolicy(factionId, result.Commands.GlobalPolicy, frame.Tick);
                if (result.Commands.Policies.Count != 0)
                {
                    commandPort.Propose(factionId, sequence++, WithCurrentVersions(result.Commands.Policies), checked(frame.Tick + 1));
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
            int limit = runtime is ITacticFailurePolicy policy ? policy.ConsecutiveFailureLimit : FailureLimit;
            if (consecutiveFailures >= limit) Dispose();
            var failure = new TacticFailure { Tick = tick, RuntimeName = Name, Reason = error.GetType().Name + ": " + error.Message, Consecutive = consecutiveFailures };
            failures.Add(failure); return failure;
        }

        private IReadOnlyList<PolicyOrder> WithCurrentVersions(IReadOnlyList<PolicyOrder> orders)
            => versions == null ? orders : TacticOrderVersions.Stamp(orders, versions);

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

        public void Dispose()
        {
            if (disabled) return;
            disabled = true;
            (runtime as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Stamps tactic orders with the policy versions the faction can see now, the same way the doctrine presets do.
    /// An order carrying revision 0 is dropped by the simulation as StaleVersion once its target has been revised,
    /// so without this a tactic stops having any effect after the first policy change (found 2026-10-06).
    /// </summary>
    public static class TacticOrderVersions
    {
        public static IReadOnlyList<PolicyOrder> Stamp(IReadOnlyList<PolicyOrder> orders, Func<ScopeKey, IReadOnlyList<PolicyVersion>> versions)
        {
            if (orders == null) throw new ArgumentNullException(nameof(orders));
            if (versions == null) throw new ArgumentNullException(nameof(versions));
            var stamped = new PolicyOrder[orders.Count];
            for (int i = 0; i < orders.Count; i++)
            {
                var o = orders[i];
                var snapshot = versions(o.Target) ?? Array.Empty<PolicyVersion>();
                ulong revision = 0;
                var parents = new List<PolicyVersion>();
                foreach (var v in snapshot)
                {
                    if (v.Scope.Equals(o.Target)) revision = v.Revision;
                    else parents.Add(v);
                }
                stamped[i] = new PolicyOrder(o.CommandId, o.BatchId, o.Source, o.Target, o.Kind, o.Goal, o.Priority,
                    o.AllowedLoss, o.End, o.ReservePermille, revision, parents.ToArray(), o.ObservedTick, o.Expiration);
            }
            return stamped;
        }
    }
}
