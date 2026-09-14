using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle=Rts.Simulation.Simulation;

namespace Rts.Application
{
    public sealed class ReplayOutcome
    {
        public long LastTick;
        public long? FirstMismatchTick;
        public bool IsFault;
    }
    public static class ReplayRunner
    {
        public static ReplayOutcome Record(Stream output,ScenarioDefinition scenario,IEnumerable<ScheduledInput> source,long ticks,BuildIdentity build,Action<DiagnosticState,byte[],byte[]> capture=null)
        {
            if(ticks<0 || ticks>10000000 || ticks>scenario.VerificationTickLimit)throw new InvalidDataException("Tick limit outside scenario verification range.");
            // Canonical copy prevents caller mutation; future inputs stay outside Simulation and its hash.
            byte[] bytes=ScenarioBinary.Encode(scenario); var sim=new Battle(ScenarioBinary.Decode(bytes));
            if(source==null)throw new InvalidDataException("Missing input collection.");
            var supplied=source.ToArray();
            if(supplied.Any(v=>v==null || v.Orders.Any(o=>o==null)))throw new InvalidDataException("Null input/order.");
            var inputs=supplied.OrderBy(v=>v.ApplyTick).ThenBy(v=>v.LogIndex).ToArray();
            var indices=new HashSet<ulong>();
            foreach(var i in inputs) if(i.ApplyTick<1 || i.ApplyTick>ticks || !indices.Add(i.LogIndex))throw new InvalidDataException("Input tick or duplicate LogIndex.");
            var header=new ReplayHeader { RulesVersion=ScenarioBinary.RulesVersion,TickRateHz=scenario.TickRateHz,Seed=scenario.Seed,TickLimit=ticks,Scenario=bytes,Build=build };
            using(var writer=new ReplayWriter(output,header))
            {
                int cursor=0; ulong lastIndex=0; var outcome=new ReplayOutcome();
                for(long tick=0;tick<=ticks;tick++)
                {
                    var batch=new List<ScheduledInput>();
                    while(cursor<inputs.Length && inputs[cursor].ApplyTick==tick)
                    {
                        var input=inputs[cursor++]; byte[] payload=InputBinary.Encode(input); InputBinary.Decode(payload);
                        writer.Write(new ReplayRecord(ReplayRecordKind.Input,tick,input.LogIndex,payload)); batch.Add(input); lastIndex=input.LogIndex;
                    }
                    if(tick>0)sim.Step(tick,batch);
                    var state=sim.CaptureDiagnostic(); byte[] hash=ReplayBinary.Hash(state.CanonicalState), events=DiagnosticComparison.EventHash(sim.Capture(1));
                    writer.Write(new ReplayRecord(ReplayRecordKind.TickHash,tick,lastIndex,hash.Concat(events).ToArray()));
                    if(tick%100==0)writer.Write(new ReplayRecord(ReplayRecordKind.DiagnosticCheckpoint,tick,lastIndex,state.CanonicalState.ToArray()));
                    capture?.Invoke(state,hash,events); outcome.LastTick=tick; outcome.IsFault=sim.Capture(1).Result.IsFault;
                    if(sim.Capture(1).Result.HasEnded)break;
                }
                writer.Write(new ReplayRecord(ReplayRecordKind.End,outcome.LastTick,lastIndex,ReplayBinary.Pack(w=>w.Write(outcome.IsFault))));
                return outcome;
            }
        }
        public static ReplayOutcome Replay(Stream input,BuildIdentity build,Action<DiagnosticState,byte[],byte[]> capture=null,bool allowBuildMismatch=false)
        {
            using(var reader=new ReplayReader(input))
            {
                var h=reader.Header;
                if(h.RulesVersion!=ScenarioBinary.RulesVersion || h.TickRateHz!=20)throw new InvalidDataException("Rules/tick rate mismatch.");
                if(!allowBuildMismatch && h.Build.SourceHash!=build.SourceHash)throw new InvalidDataException("Source hash mismatch. Use --allow-build-mismatch for an intentional compatibility test.");
                var scenario=ScenarioBinary.Decode(h.Scenario);
                if(scenario.Seed!=h.Seed || scenario.TickRateHz!=h.TickRateHz || h.TickLimit>scenario.VerificationTickLimit)throw new InvalidDataException("Scenario/header mismatch.");
                var sim=new Battle(scenario); var result=new ReplayOutcome(); var batch=new List<ScheduledInput>(); var indices=new HashSet<ulong>();
                long nextTick=0; ulong lastIndex=0; bool checkpointDue=false; bool terminal=false; DiagnosticState lastState=null;
                while(true)
                {
                    var r=reader.Read();
                    if(r==null)throw new InvalidDataException("Missing End.");
                    if(checkpointDue && r.Kind!=ReplayRecordKind.DiagnosticCheckpoint)throw new InvalidDataException("Missing diagnostic checkpoint.");
                    if(r.Kind==ReplayRecordKind.Input)
                    {
                        if(terminal || r.Tick!=nextTick || nextTick==0)throw new InvalidDataException("Input record order.");
                        var v=InputBinary.Decode(r.Payload);
                        if(v.ApplyTick!=r.Tick || v.LogIndex!=r.LogIndex || !indices.Add(v.LogIndex) || (batch.Count>0 && batch[batch.Count-1].LogIndex>=v.LogIndex))throw new InvalidDataException("Input identity/order.");
                        batch.Add(v); lastIndex=v.LogIndex;
                    }
                    else if(r.Kind==ReplayRecordKind.TickHash)
                    {
                        if(terminal || r.Tick!=nextTick || r.LogIndex!=lastIndex || r.Payload.Length!=64)throw new InvalidDataException("TickHash order/size.");
                        if(nextTick>0)sim.Step(nextTick,batch); batch.Clear();
                        lastState=sim.CaptureDiagnostic(); byte[] hash=ReplayBinary.Hash(lastState.CanonicalState), events=DiagnosticComparison.EventHash(sim.Capture(1));
                        if(!r.Payload.SequenceEqual(hash.Concat(events)) && !result.FirstMismatchTick.HasValue)result.FirstMismatchTick=nextTick;
                        capture?.Invoke(lastState,hash,events); result.LastTick=nextTick; result.IsFault=sim.Capture(1).Result.IsFault;
                        checkpointDue=nextTick%100==0; terminal=sim.Capture(1).Result.HasEnded; nextTick++;
                    }
                    else if(r.Kind==ReplayRecordKind.DiagnosticCheckpoint)
                    {
                        if(!checkpointDue || r.Tick!=nextTick-1 || r.LogIndex!=lastIndex)throw new InvalidDataException("Unexpected checkpoint.");
                        DiagnosticComparison.Fields(new DiagnosticState(r.Tick,r.Payload));
                        if(!r.Payload.SequenceEqual(lastState.CanonicalState) && !result.FirstMismatchTick.HasValue)result.FirstMismatchTick=r.Tick;
                        checkpointDue=false;
                    }
                    else
                    {
                        if(nextTick==0 || batch.Count!=0 || r.Tick!=nextTick-1 || r.LogIndex!=lastIndex || (!terminal && r.Tick!=h.TickLimit))throw new InvalidDataException("Premature or inconsistent End.");
                        bool fault=ReplayBinary.Unpack(r.Payload,ReplayBinary.Bool);
                        if(fault!=result.IsFault && !result.FirstMismatchTick.HasValue)result.FirstMismatchTick=r.Tick;
                        return result;
                    }
                }
            }
        }
    }
}
