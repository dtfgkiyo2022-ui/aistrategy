using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.UnityHost
{
    /// <summary>
    /// One immutable publication from the simulation owner. The previous/current pair is kept together so the
    /// presentation side never combines frames from different match generations.
    /// </summary>
    public sealed class PublishedMatchFrame
    {
        public long Generation { get; }
        public long Tick { get; }
        public FactionFrame PreviousFaction1 { get; }
        public FactionFrame PreviousFaction2 { get; }
        public FactionFrame CurrentFaction1 { get; }
        public FactionFrame CurrentFaction2 { get; }
        public IReadOnlyList<GameEvent> Events { get; }
        public SimulationThreadPerf Perf { get; }

        internal PublishedMatchFrame(long generation, long tick,
            FactionFrame previousFaction1, FactionFrame previousFaction2,
            FactionFrame currentFaction1, FactionFrame currentFaction2,
            IReadOnlyList<GameEvent> events, SimulationThreadPerf perf)
        {
            Generation = generation;
            Tick = tick;
            PreviousFaction1 = previousFaction1;
            PreviousFaction2 = previousFaction2;
            CurrentFaction1 = currentFaction1;
            CurrentFaction2 = currentFaction2;
            Events = events ?? Array.Empty<GameEvent>();
            Perf = perf;
        }

        public FactionFrame Current(uint factionId)
        {
            if (factionId == 1) return CurrentFaction1;
            if (factionId == 2) return CurrentFaction2;
            throw new ArgumentOutOfRangeException(nameof(factionId));
        }

        public FactionFrame Previous(uint factionId)
        {
            if (factionId == 1) return PreviousFaction1;
            if (factionId == 2) return PreviousFaction2;
            throw new ArgumentOutOfRangeException(nameof(factionId));
        }

        public static FactionFrame WithEvents(FactionFrame source, IReadOnlyList<GameEvent> events)
        {
            if (source == null) return null;
            return new FactionFrame(source.Tick, source.FactionId, source.Units, source.Observation, source.Commands,
                events ?? Array.Empty<GameEvent>(), source.Fog, source.Result, source.AliveCount, source.FactionCap,
                source.Reinforcements, source.Economy, source.Regions, source.CellRegions);
        }
    }

    public readonly struct SimulationThreadPerf
    {
        public double TickMilliseconds { get; }
        public double MainFrameMilliseconds { get; }

        public SimulationThreadPerf(double tickMilliseconds, double mainFrameMilliseconds)
        {
            TickMilliseconds = tickMilliseconds;
            MainFrameMilliseconds = mainFrameMilliseconds;
        }
    }

    /// <summary>Volatile publication point. Published frame contents are never mutated after Publish.</summary>
    public sealed class PublishedMatchFrameStore
    {
        private PublishedMatchFrame current;

        public PublishedMatchFrame Read() { return Volatile.Read(ref current); }

        public PublishedMatchFrame Publish(long generation, FactionFrame faction1, FactionFrame faction2,
            IReadOnlyList<GameEvent> events, SimulationThreadPerf perf)
        {
            var before = Volatile.Read(ref current);
            bool sameGeneration = before != null && before.Generation == generation;
            var next = new PublishedMatchFrame(generation,
                faction1 == null ? before == null ? 0 : before.Tick : faction1.Tick,
                !sameGeneration ? faction1 : before.CurrentFaction1,
                !sameGeneration ? faction2 : before.CurrentFaction2,
                faction1, faction2, CopyEvents(events), perf);
            Interlocked.Exchange(ref current, next);
            return next;
        }

        public void Clear() { Interlocked.Exchange(ref current, null); }

        private static IReadOnlyList<GameEvent> CopyEvents(IReadOnlyList<GameEvent> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<GameEvent>();
            var copy = new GameEvent[source.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>
    /// Events have delivery semantics unlike positions: the display may skip a frame, but it must drain every event.
    /// This queue is deliberately separate from PublishedMatchFrameStore.
    /// </summary>
    public sealed class SimulationEventQueue
    {
        private readonly ConcurrentQueue<PublishedSimulationEvent> queue = new ConcurrentQueue<PublishedSimulationEvent>();
        private long nextSequence;

        public void Enqueue(long generation, GameEvent value)
        {
            queue.Enqueue(new PublishedSimulationEvent(Interlocked.Increment(ref nextSequence), generation, value));
        }

        public IReadOnlyList<PublishedSimulationEvent> Drain(long generation)
        {
            var result = new List<PublishedSimulationEvent>();
            while (queue.TryDequeue(out var item))
                if (item.Generation == generation) result.Add(item);
            return result.ToArray();
        }

        public void DiscardOtherGenerations(long generation)
        {
            var keep = new List<PublishedSimulationEvent>();
            while (queue.TryDequeue(out var item)) if (item.Generation == generation) keep.Add(item);
            foreach (var item in keep) queue.Enqueue(item);
        }
    }

    public readonly struct PublishedSimulationEvent
    {
        public long Sequence { get; }
        public long Generation { get; }
        public GameEvent Event { get; }

        internal PublishedSimulationEvent(long sequence, long generation, GameEvent value)
        { Sequence = sequence; Generation = generation; Event = value; }
    }

    /// <summary>Worker-side match adapter. All members except the immutable publication are worker-owned.</summary>
    public sealed class SimulationThreadMatch : IDisposable
    {
        public Rts.Simulation.Simulation Simulation { get; }
        public CommandGateway Gateway { get; }
        public Action BeforeStep { get; set; }
        public Action AfterStep { get; set; }
        /// <summary>Optional complete tick owned by the host (tactics, gateway, provider polling, then presets).</summary>
        public Action StepAction { get; set; }
        public Action DisposeAction { get; set; }

        public SimulationThreadMatch(Rts.Simulation.Simulation simulation, CommandGateway gateway)
        {
            Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public long Tick { get { return Simulation.Capture(1).Tick; } }
        public bool HasEnded { get { return Simulation.Capture(1).Result.HasEnded; } }

        public void Step()
        {
            BeforeStep?.Invoke();
            if (StepAction == null) Gateway.Step(); else StepAction();
            AfterStep?.Invoke();
        }

        public void Dispose() { DisposeAction?.Invoke(); }
    }

    /// <summary>
    /// Main-thread command facade. It returns a local receipt immediately; the worker maps it to Gateway's ID in FIFO
    /// order. Cancellation is queued too, so a cancel cannot overtake the request it cancels.
    /// </summary>
    public sealed class SimulationCommandPort : IApplicationCommandPort, ICommandDelayControl
    {
        private interface IOperation { void Apply(SimulationCommandPort owner, CommandGateway gateway); }

        private sealed class SubmitOperation : IOperation
        {
            private readonly ulong receipt; private readonly UserPolicyIntent intent; private readonly bool interpreted;
            internal SubmitOperation(ulong receipt, UserPolicyIntent intent, bool interpreted)
            { this.receipt = receipt; this.intent = intent; this.interpreted = interpreted; }
            public void Apply(SimulationCommandPort owner, CommandGateway gateway)
            {
                ulong gatewayId;
                if (interpreted)
                {
                    owner.interpreting = intent;
                    try { gatewayId = gateway.SubmitInterpreted(intent); }
                    finally { owner.interpreting = null; }
                }
                else gatewayId = gateway.Submit(intent);
                owner.receipts[receipt] = gatewayId;
            }
        }

        private sealed class CancelOperation : IOperation
        {
            private readonly ulong receipt;
            internal CancelOperation(ulong receipt) { this.receipt = receipt; }
            public void Apply(SimulationCommandPort owner, CommandGateway gateway)
            {
                if (!owner.receipts.TryGetValue(receipt, out var gatewayId))
                    throw new InvalidOperationException("The command receipt has not been accepted by the simulation thread.");
                gateway.Cancel(gatewayId);
            }
        }

        private sealed class ProposeOperation : IOperation
        {
            private readonly ulong receipt; private readonly uint faction; private readonly ulong sequence;
            private readonly IReadOnlyList<PolicyOrder> orders; private readonly long applyTick;
            internal ProposeOperation(ulong receipt, uint faction, ulong sequence, IReadOnlyList<PolicyOrder> orders, long applyTick)
            { this.receipt = receipt; this.faction = faction; this.sequence = sequence; this.orders = orders; this.applyTick = applyTick; }
            public void Apply(SimulationCommandPort owner, CommandGateway gateway)
            { owner.receipts[receipt] = gateway.Propose(faction, sequence, orders, applyTick); }
        }

        private sealed class EconomyOperation : IOperation
        {
            private readonly EconomyCommand command;
            internal EconomyOperation(EconomyCommand command) { this.command = command; }
            public void Apply(SimulationCommandPort owner, CommandGateway gateway) { gateway.SubmitEconomy(command); }
        }

        private sealed class BatchOperation : IOperation
        {
            private readonly ulong receipt; private readonly IReadOnlyList<UserPolicyIntent> intents;
            internal BatchOperation(ulong receipt, IReadOnlyList<UserPolicyIntent> intents)
            { this.receipt = receipt; this.intents = intents; }
            public void Apply(SimulationCommandPort owner, CommandGateway gateway)
            { owner.receipts[receipt] = gateway.SubmitBatch(intents); }
        }

        private sealed class BeginInterpretationOperation : IOperation
        {
            private readonly ulong receipt; private readonly uint faction; private readonly ScopeKey? target;
            internal BeginInterpretationOperation(ulong receipt, uint faction, ScopeKey? target)
            { this.receipt = receipt; this.faction = faction; this.target = target; }
            public void Apply(SimulationCommandPort owner, CommandGateway gateway)
            { owner.receipts[receipt] = gateway.BeginInterpretation(faction, target); }
        }

        private sealed class EndInterpretationOperation : IOperation
        {
            private readonly ulong receipt;
            internal EndInterpretationOperation(ulong receipt) { this.receipt = receipt; }
            public void Apply(SimulationCommandPort owner, CommandGateway gateway)
            {
                if (!owner.receipts.TryGetValue(receipt, out var gatewayId)) return;
                gateway.EndInterpretation(gatewayId);
            }
        }

        private readonly ConcurrentQueue<IOperation> operations = new ConcurrentQueue<IOperation>();
        private readonly ConcurrentDictionary<ulong, ulong> receipts = new ConcurrentDictionary<ulong, ulong>();
        private readonly ConcurrentDictionary<ulong, byte> openInterpretations = new ConcurrentDictionary<ulong, byte>();
        private long nextReceipt;
        private UserPolicyIntent? interpreting;
        private readonly int providerDelayTicks;
        private int requestedDelayTicks;

        public SimulationCommandPort(int providerDelayTicks)
        { this.providerDelayTicks = providerDelayTicks; requestedDelayTicks = providerDelayTicks; }

        public int DelayTicks { get { return Volatile.Read(ref requestedDelayTicks); } set { Volatile.Write(ref requestedDelayTicks, value); } }
        public bool RestartRequested { get { return DelayTicks != providerDelayTicks; } }

        public ulong Submit(UserPolicyIntent intent)
        {
            ulong receipt = NextReceipt();
            operations.Enqueue(new SubmitOperation(receipt, intent, providerDelayTicks != 0));
            return receipt;
        }

        public ulong SubmitBatch(IReadOnlyList<UserPolicyIntent> intents)
        {
            ulong receipt = NextReceipt();
            operations.Enqueue(new BatchOperation(receipt, intents));
            return receipt;
        }

        public ulong BeginInterpretation(uint faction, ScopeKey? fixedTarget = null)
        {
            ulong receipt = NextReceipt();
            openInterpretations[receipt] = 0;
            operations.Enqueue(new BeginInterpretationOperation(receipt, faction, fixedTarget));
            return receipt;
        }

        public void EndInterpretation(ulong reservationId)
        {
            openInterpretations.TryRemove(reservationId, out _);
            operations.Enqueue(new EndInterpretationOperation(reservationId));
        }

        public bool IsInterpretationOpen(ulong reservationId) { return openInterpretations.ContainsKey(reservationId); }

        public void Cancel(ulong requestId) { operations.Enqueue(new CancelOperation(requestId)); }

        public ulong Propose(uint faction, ulong sequence, IReadOnlyList<PolicyOrder> orders, long applyTick)
        {
            ulong receipt = NextReceipt();
            operations.Enqueue(new ProposeOperation(receipt, faction, sequence, orders, applyTick));
            return receipt;
        }

        public void SubmitEconomy(EconomyCommand command) { operations.Enqueue(new EconomyOperation(command)); }

        public IReadOnlyList<ulong> Drain(CommandGateway gateway)
        {
            if (gateway == null) throw new ArgumentNullException(nameof(gateway));
            var accepted = new List<ulong>();
            while (operations.TryDequeue(out var operation))
            {
                operation.Apply(this, gateway);
                if (operation is SubmitOperation || operation is ProposeOperation) accepted.Add(1);
            }
            return accepted;
        }

        public bool TryGetGatewayId(ulong receipt, out ulong gatewayId) { return receipts.TryGetValue(receipt, out gatewayId); }

        /// <summary>Used by the worker's DelayedPolicyProvider callback during the queued Submit operation.</summary>
        public IReadOnlyList<PolicyOrder> Interpret(PolicyRequest request)
        {
            if (!interpreting.HasValue) throw new InvalidOperationException("No standard command is being interpreted.");
            var intent = interpreting.Value;
            if (!intent.Target.Equals(request.Scope)) throw new InvalidOperationException("Interpretation target mismatch.");
            return new[] { new PolicyOrder(0, 0, CommandSource.Human, intent.Target, intent.Kind, intent.Goal,
                intent.Priority, intent.AllowedLoss, intent.End, intent.ReservePermille, 0, Array.Empty<PolicyVersion>(),
                request.StartedTick, intent.Expiration) };
        }

        private ulong NextReceipt()
        {
            long next = Interlocked.Increment(ref nextReceipt);
            return checked((ulong)next);
        }
    }

    /// <summary>
    /// Owns one simulation thread. Stop is a request followed by a join; callers never dispose the match while it runs.
    /// </summary>
    public sealed class SimulationThreadWorker : IDisposable
    {
        private readonly Func<SimulationThreadMatch> factory;
        private readonly SimulationCommandPort commands;
        private readonly PublishedMatchFrameStore frames;
        private readonly SimulationEventQueue events;
        private readonly long generation;
        private readonly TimeSpan tickInterval;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly ConcurrentQueue<Action> controls = new ConcurrentQueue<Action>();
        private Thread thread;
        private int stopRequested;
        private int paused;
        private int speed = 1;
        // 10-23: the managed heap once grew to ~45 GB and took other programs on the PC down with it. Past this size the
        // worker stops stepping (the match stays as it is) instead of letting the whole machine run out of memory.
        public const long MemoryGuardBytes = 6L * 1024 * 1024 * 1024;
        private int memoryGuardTripped;
        private int stepsSinceMemoryCheck;
        private Exception failure;
        private SimulationThreadMatch match;
        private byte[] finalCanonicalState;

        public SimulationThreadWorker(Func<SimulationThreadMatch> factory, SimulationCommandPort commands,
            PublishedMatchFrameStore frames, SimulationEventQueue events, long generation, TimeSpan tickInterval)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.frames = frames ?? throw new ArgumentNullException(nameof(frames));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            this.generation = generation;
            this.tickInterval = tickInterval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(50) : tickInterval;
        }

        public long Generation { get { return generation; } }
        public bool IsRunning { get { return thread != null && thread.IsAlive; } }
        public Exception Failure { get { return Volatile.Read(ref failure); } }
        public bool MemoryGuardTripped { get { return Volatile.Read(ref memoryGuardTripped) != 0; } }
        public IReadOnlyList<byte> FinalCanonicalState { get { return Volatile.Read(ref finalCanonicalState); } }
        public Action<long, IReadOnlyList<byte>> TickObserver { get; set; }
        [ThreadStatic] private static SimulationThreadWorker current;
        public bool IsCurrentThread { get { return ReferenceEquals(current, this); } }

        public void Start()
        {
            if (thread != null) throw new InvalidOperationException("Simulation thread already started.");
            thread = new Thread(Run) { IsBackground = true, Name = "RTS Simulation" };
            thread.Start();
        }

        public void SetPaused(bool value) { Volatile.Write(ref paused, value ? 1 : 0); wake.Set(); }
        public void SetSpeed(int value) { Volatile.Write(ref speed, value == 2 || value == 4 ? value : 1); wake.Set(); }
        public void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            controls.Enqueue(action); wake.Set();
        }

        public bool StopAndJoin(TimeSpan timeout)
        {
            Interlocked.Exchange(ref stopRequested, 1);
            wake.Set();
            var current = thread;
            return current == null || !current.IsAlive || current.Join(timeout);
        }

        private void Run()
        {
            try
            {
                current = this;
                events.DiscardOtherGenerations(generation);
                match = factory();
                Publish();
                var next = Stopwatch.GetTimestamp();
                while (Volatile.Read(ref stopRequested) == 0)
                {
                    while (controls.TryDequeue(out var control)) control();
                    commands.Drain(match.Gateway);
                    if (Volatile.Read(ref paused) != 0 || match.HasEnded || Volatile.Read(ref memoryGuardTripped) != 0)
                    {
                        wake.WaitOne(20);
                        continue;
                    }
                    long started = Stopwatch.GetTimestamp();
                    match.Step();
                    double milliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                    Publish(new SimulationThreadPerf(milliseconds, 0));
                    if (++stepsSinceMemoryCheck >= 100)
                    {
                        stepsSinceMemoryCheck = 0;
                        if (GC.GetTotalMemory(false) > MemoryGuardBytes) Volatile.Write(ref memoryGuardTripped, 1);
                    }
                    long interval = (long)(tickInterval.TotalSeconds * Stopwatch.Frequency / Math.Max(1, Volatile.Read(ref speed)));
                    next += interval;
                    long remaining = next - Stopwatch.GetTimestamp();
                    if (remaining <= 0) next = Stopwatch.GetTimestamp();
                    // Wait for the whole tick interval, in short sleeps so pause, speed and stop still answer quickly.
                    // (Sleeping only once for at most 20 ms let a light tick run at about twice the chosen speed.)
                    while (remaining > 0 && Volatile.Read(ref stopRequested) == 0 && Volatile.Read(ref paused) == 0)
                    {
                        Thread.Sleep((int)Math.Max(1, Math.Min(20, remaining * 1000L / Stopwatch.Frequency)));
                        remaining = next - Stopwatch.GetTimestamp();
                    }
                }
            }
            catch (Exception e) { Volatile.Write(ref failure, e); }
            finally
            {
                if (match != null)
                {
                    var state = match.Simulation.CaptureDiagnostic().CanonicalState;
                    var copy = new byte[state.Count];
                    for (int i = 0; i < copy.Length; i++) copy[i] = state[i];
                    Volatile.Write(ref finalCanonicalState, copy);
                }
                try { match?.Dispose(); } catch (Exception e) { Volatile.Write(ref failure, e); }
                match = null;
                current = null;
            }
        }

        private void Publish(SimulationThreadPerf perf = default(SimulationThreadPerf))
        {
            if (match == null) return;
            var first = match.Simulation.Capture(1);
            var second = match.Simulation.Capture(2);
            var allEvents = new List<GameEvent>();
            AddEvents(allEvents, first); AddEvents(allEvents, second);
            var publication = frames.Publish(generation, first, second, allEvents, perf);
            foreach (var value in publication.Events) events.Enqueue(generation, value);
            // The canonical state is serialised only for a test observer: building it every tick cost more than the tick
            // itself on the 2600-unit map and kept the 4x speed from keeping up (test play 10-20).
            if (TickObserver == null) return;
            var state = match.Simulation.CaptureDiagnostic().CanonicalState;
            var copy = new byte[state.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = state[i];
            TickObserver?.Invoke(match.Tick, copy);
        }

        private static void AddEvents(List<GameEvent> destination, FactionFrame frame)
        {
            if (frame == null || frame.Events == null) return;
            foreach (var value in frame.Events) destination.Add(value);
        }

        public void Dispose()
        {
            StopAndJoin(TimeSpan.FromSeconds(5));
            wake.Dispose();
            if (thread != null && thread.IsAlive) throw new InvalidOperationException("Simulation thread did not stop.");
        }
    }
}
