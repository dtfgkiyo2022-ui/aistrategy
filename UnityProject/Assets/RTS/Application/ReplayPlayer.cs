using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Application
{
    /// <summary>
    /// Steps a recorded match one tick at a time so a viewer can follow it. Verification stays with
    /// ReplayRunner.Replay and the CLI: this player reports a tick hash mismatch but does not stop on it.
    /// </summary>
    public sealed class ReplayPlayer : IDisposable
    {
        private readonly ReplayReader reader;
        private readonly Battle simulation;
        private readonly List<ScheduledInput> batch = new List<ScheduledInput>();
        private readonly HashSet<ulong> indices = new HashSet<ulong>();
        private long nextTick;
        private ulong lastIndex;

        public ScenarioDefinition Scenario { get; }
        public ReplayHeader Header { get { return reader.Header; } }
        /// <summary>Last tick loaded; -1 before the first step.</summary>
        public long Tick { get; private set; } = -1;
        public bool HasEnded { get; private set; }
        public long? MismatchTick { get; private set; }

        public ReplayPlayer(Stream input, BuildIdentity build, bool allowBuildMismatch = false)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            reader = new ReplayReader(input);
            var header = reader.Header;
            if (header.RulesVersion != ScenarioBinary.RulesVersion || header.TickRateHz != 20)
                throw new InvalidDataException("Rules/tick rate mismatch.");
            if (!allowBuildMismatch && header.Build.SourceHash != build.SourceHash)
                throw new InvalidDataException("Source hash mismatch. Pass allowBuildMismatch for an intentional compatibility view.");
            Scenario = ScenarioBinary.Decode(header.Scenario);
            if (Scenario.Seed != header.Seed || Scenario.TickRateHz != header.TickRateHz || header.TickLimit > Scenario.VerificationTickLimit)
                throw new InvalidDataException("Scenario/header mismatch.");
            simulation = new Battle(Scenario);
        }

        public FactionFrame Capture(uint faction) { return simulation.Capture(faction); }

        /// <summary>Loads the next recorded tick. Returns false once the file ends.</summary>
        public bool StepOnce()
        {
            if (HasEnded) return false;
            while (true)
            {
                var record = reader.Read();
                if (record == null) throw new InvalidDataException("Missing End.");
                if (record.Kind == ReplayRecordKind.Input)
                {
                    if (record.Tick != nextTick || nextTick == 0) throw new InvalidDataException("Input record order.");
                    var input = InputBinary.Decode(record.Payload);
                    if (input.AcceptedTick + 1 != record.Tick || input.LogIndex != record.LogIndex || !indices.Add(input.LogIndex)
                        || (batch.Count > 0 && batch[batch.Count - 1].LogIndex >= input.LogIndex))
                        throw new InvalidDataException("Input identity/order.");
                    batch.Add(input); lastIndex = input.LogIndex;
                }
                else if (record.Kind == ReplayRecordKind.TickHash)
                {
                    if (record.Tick != nextTick || record.LogIndex != lastIndex || record.Payload.Length != 64)
                        throw new InvalidDataException("TickHash order/size.");
                    if (nextTick > 0) simulation.Step(nextTick, batch);
                    batch.Clear();
                    var state = simulation.CaptureDiagnostic();
                    byte[] hash = ReplayBinary.Hash(state.CanonicalState);
                    byte[] events = ReplayBinary.Hash(DiagnosticComparison.EventHash(simulation.Capture(1))
                        .Concat(DiagnosticComparison.EventHash(simulation.Capture(2))));
                    if (!record.Payload.SequenceEqual(hash.Concat(events)) && !MismatchTick.HasValue) MismatchTick = nextTick;
                    Tick = nextTick;
                    nextTick++;
                    return true;
                }
                else if (record.Kind == ReplayRecordKind.End)
                {
                    HasEnded = true;
                    return false;
                }
                // CommandResults and DiagnosticCheckpoint are verification records; the viewer skips them.
            }
        }

        public void Dispose() { reader.Dispose(); }
    }
}
