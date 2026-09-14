using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle=Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class ReplayTests
    {
        private static BuildIdentity Build()=>new BuildIdentity { Commit="test",SourceHash=new string('a',64),Backend="test" };
        private static ScheduledInput Input(long tick,PolicyKind kind=PolicyKind.Retreat)=>new ScheduledInput(1,InputKind.Resolve,tick-1,tick,7,8,new[]{new PolicyOrder(9,10,CommandSource.Human,new ScopeKey(1,ScopeKind.Army,1),kind,new PolicyGoal(GoalKind.Point,0,new SimPoint(Fix64.FromInt(80),Fix64.FromInt(64))),50,new LossBudget(300),new EndCondition(EndKind.UntilReplaced,0),200,4,new[]{new PolicyVersion(new ScopeKey(1,ScopeKind.All,0),3)},0,new Expiration(500,240,ExpireFlags.SubjectGone|ExpireFlags.ObservationTooOld))});
        private static byte[] Record(long ticks=200,IEnumerable<ScheduledInput> inputs=null)
        {
            using(var s=new MemoryStream()) { ReplayRunner.Record(s,WeekOneScenario.Create(),inputs??new[]{Input(3)},ticks,Build()); return s.ToArray(); }
        }
        [Test]
        public void RecordReplayMatchesEveryTickIncludingS0AndCheckpoints()
        {
            var expected=new List<byte[]>();
            using(var stream=new MemoryStream())
            {
                ReplayRunner.Record(stream,WeekOneScenario.Create(),new[]{Input(3)},230,Build(),(s,h,e)=>expected.Add(h.Concat(e).ToArray()));
                stream.Position=0; int count=0;
                var result=ReplayRunner.Replay(stream,Build(),(s,h,e)=> { Assert.That(s.Tick,Is.EqualTo(count)); Assert.That(h.Concat(e),Is.EqualTo(expected[count++])); });
                Assert.That(result.FirstMismatchTick,Is.Null); Assert.That(count,Is.EqualTo(231));
                stream.Position=0; using(var reader=new ReplayReader(stream)) { var checkpoints=new List<long>(); ReplayRecord r; while((r=reader.Read())!=null)if(r.Kind==ReplayRecordKind.DiagnosticCheckpoint)checkpoints.Add(r.Tick); Assert.That(checkpoints,Is.EqualTo(new long[]{0,100,200})); }
            }
        }
        [Test]
        public void InputCodecPreservesEveryContractFieldAndControlKinds()
        {
            foreach(InputKind kind in Enum.GetValues(typeof(InputKind)))
            {
                var source=Input(2); var input=new ScheduledInput(source.LogIndex,kind,source.AcceptedTick,source.ApplyTick,source.RequestId,source.IssuerSequence,source.Orders);
                var bytes=InputBinary.Encode(input); var copy=InputBinary.Decode(bytes);
                Assert.That(InputBinary.Encode(copy),Is.EqualTo(bytes)); Assert.That(copy.Kind,Is.EqualTo(kind)); Assert.That(copy.Orders[0].Parents[0].Revision,Is.EqualTo(3)); Assert.That(copy.Orders[0].Expiration.Flags,Is.EqualTo(ExpireFlags.SubjectGone|ExpireFlags.ObservationTooOld));
            }
        }
        [TestCase("NextAttackTick",123L)]
        [TestCase("IsRetreating",true)]
        [TestCase("TargetId",2U)]
        public void SingleMutableFieldChangesHashAndReportsItsPath(string field,object value)
        {
            var a=new Battle(WeekOneScenario.Create()); var b=new Battle(WeekOneScenario.Create());
            MutateSoldier(b,field,value);
            Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState),Is.Not.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)));
            var diff=DiagnosticComparison.First(a.CaptureDiagnostic(),b.CaptureDiagnostic()); Assert.That(diff.Field,Is.EqualTo("Soldiers[1]."+field));
        }
        [Test]
        public void MovementGoalChangesHash()
        {
            var a=new Battle(WeekOneScenario.Create()); var b=new Battle(WeekOneScenario.Create()); MutateSoldier(b,"MoveGoal",new SimPoint(Fix64.FromInt(7),Fix64.FromInt(8)));
            Assert.That(DiagnosticComparison.First(a.CaptureDiagnostic(),b.CaptureDiagnostic()).Field,Is.EqualTo("Soldiers[1].MoveGoal.X.Raw"));
        }
        private static void MutateSoldier(Battle sim,string field,object value)
        {
            var world=typeof(Battle).GetField("world",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(sim);
            var soldiers=(Array)world.GetType().GetField("Soldiers",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(world);
            object soldier=soldiers.GetValue(0); soldier.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(soldier,value); soldiers.SetValue(soldier,0);
        }
        [Test]
        public void FirstDivergentTickAndFieldAreExact()
        {
            var a=new Battle(WeekOneScenario.Create()); var b=new Battle(WeekOneScenario.Create()); StateDifference difference=null;
            for(long tick=0;tick<=9;tick++)
            {
                if(tick>0) { a.Step(tick,Array.Empty<ScheduledInput>()); b.Step(tick,Array.Empty<ScheduledInput>()); }
                if(tick==7)MutateSoldier(b,"NextAttackTick",99L);
                difference=DiagnosticComparison.First(a.CaptureDiagnostic(),b.CaptureDiagnostic()); if(difference!=null)break;
            }
            Assert.That(difference.Tick,Is.EqualTo(7)); Assert.That(difference.Field,Is.EqualTo("Soldiers[1].NextAttackTick")); Assert.That(difference.Left,Is.EqualTo("0")); Assert.That(difference.Right,Is.EqualTo("99"));
        }
        [TestCase(0)] [TestCase(8)] [TestCase(-1)]
        public void RejectsWrongMagicUnknownSchemaAndCorruptPayload(int offset)
        {
            byte[] b=Record(); b[offset<0?b.Length-1:offset]^=0x40;
            Assert.Throws<InvalidDataException>(()=>ReplayRunner.Replay(new MemoryStream(b),Build()));
        }
        [Test]
        public void RejectsTruncationOversizeTrailingBytesAndWrongSource()
        {
            byte[] b=Record(); Assert.Throws<EndOfStreamException>(()=>ReplayRunner.Replay(new MemoryStream(b.Take(b.Length-1).ToArray()),Build()));
            var oversize=(byte[])b.Clone(); for(int i=12;i<16;i++)oversize[i]=255;
            Assert.Throws<InvalidDataException>(()=>ReplayRunner.Replay(new MemoryStream(oversize),Build()));
            Assert.Throws<InvalidDataException>(()=>ReplayRunner.Replay(new MemoryStream(b.Concat(new byte[]{0}).ToArray()),Build()));
            var build=Build();build.SourceHash="other";Assert.Throws<InvalidDataException>(()=>ReplayRunner.Replay(new MemoryStream(b),build));
            Assert.That(ReplayRunner.Replay(new MemoryStream(b),build,null,true).FirstMismatchTick,Is.Null);
        }
        [Test]
        public void ScenarioRoundtripAndPermutedDefinitionsAreCanonical()
        {
            var scenario=WeekOneScenario.Create(); byte[] b=ScenarioBinary.Encode(scenario);
            Array.Reverse(scenario.Soldiers); Array.Reverse(scenario.Armies); Array.Reverse(scenario.Factions);
            Assert.That(ScenarioBinary.Encode(scenario),Is.EqualTo(b)); Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(b)),Is.EqualTo(b));
        }
        [Test]
        public void MissingOrAlteredHashRecordIsDetected()
        {
            var source=Record(10); using(var input=new MemoryStream(source)) using(var reader=new ReplayReader(input)) using(var output=new MemoryStream())
            {
                using(var writer=new ReplayWriter(output,reader.Header)) { ReplayRecord r; while((r=reader.Read())!=null) { if(r.Kind==ReplayRecordKind.TickHash && r.Tick==5)r.Payload[0]^=1; writer.Write(r); } }
                output.Position=0; Assert.That(ReplayRunner.Replay(output,Build()).FirstMismatchTick,Is.EqualTo(5));
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void MissingOrDuplicateTickRecordIsRejected(bool duplicate)
        {
            using(var reader=new ReplayReader(new MemoryStream(Record(10)))) using(var output=new MemoryStream())
            {
                using(var writer=new ReplayWriter(output,reader.Header))
                {
                    ReplayRecord r;
                    while((r=reader.Read())!=null)
                    {
                        if(r.Kind==ReplayRecordKind.TickHash && r.Tick==5)
                        { if(duplicate) { writer.Write(r); writer.Write(r); } }
                        else writer.Write(r);
                    }
                }
                output.Position=0;
                Assert.Throws<InvalidDataException>(()=>ReplayRunner.Replay(output,Build()));
            }
        }
        [Test]
        public void TerminalS0HasOneHashAndEndWithoutInventedTicks()
        {
            var scenario=WeekOneScenario.Create(); scenario.Cores[0].Hp=0;
            using(var s=new MemoryStream())
            {
                var result=ReplayRunner.Record(s,scenario,Array.Empty<ScheduledInput>(),100,Build());
                Assert.That(result.LastTick,Is.EqualTo(0)); s.Position=0; int count=0;
                Assert.That(ReplayRunner.Replay(s,Build(),(d,h,e)=>count++).FirstMismatchTick,Is.Null);
                Assert.That(count,Is.EqualTo(1));
            }
        }
        [Test]
        public void FutureInputsDoNotAffectEarlierHashesAndRebuiltFramesDoNotChangeState()
        {
            var hashes=new List<byte[]>(); using(var s=new MemoryStream())ReplayRunner.Record(s,WeekOneScenario.Create(),Array.Empty<ScheduledInput>(),10,Build(),(d,h,e)=>hashes.Add(h));
            using(var s=new MemoryStream())ReplayRunner.Record(s,WeekOneScenario.Create(),new[]{Input(9)},10,Build(),(d,h,e)=> { if(d.Tick<9)Assert.That(h,Is.EqualTo(hashes[(int)d.Tick])); });
            var battle=new Battle(WeekOneScenario.Create()); var before=battle.CaptureDiagnostic();
            typeof(Battle).GetMethod("PublishFrames",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(battle,null);
            Assert.That(battle.CaptureDiagnostic().CanonicalState,Is.EqualTo(before.CanonicalState));
            var other=new Battle(WeekOneScenario.Create()); battle.Step(1,Array.Empty<ScheduledInput>()); other.Step(1,Array.Empty<ScheduledInput>());
            Assert.That(battle.CaptureDiagnostic().CanonicalState,Is.EqualTo(other.CaptureDiagnostic().CanonicalState));
        }
    }
}
