using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Rts.Contracts;

namespace Rts.Replay
{
    public static class ReplayBinary
    {
        public const int MaxRecord = 64 * 1024 * 1024, MaxString = 1024 * 1024;
        public static byte[] Hash(IEnumerable<byte> bytes) { using(var h=SHA256.Create()) return h.ComputeHash(bytes.ToArray()); }
        public static string Hex(IEnumerable<byte> b)=>BitConverter.ToString(b.ToArray()).Replace("-","").ToLowerInvariant();
        public static byte[] Pack(Action<BinaryWriter> write) { using(var s=new MemoryStream()) using(var w=new BinaryWriter(s)) { write(w); return s.ToArray(); } }
        public static T Unpack<T>(byte[] bytes,Func<BinaryReader,T> read) { using(var s=new MemoryStream(bytes,false)) using(var r=new BinaryReader(s)) { var v=read(r); if(s.Position!=s.Length)throw new InvalidDataException("Trailing payload bytes."); return v; } }
        public static byte[] Bytes(BinaryReader r,int n) { if(n<0 || n>MaxRecord)throw new InvalidDataException("Payload size."); var b=r.ReadBytes(n); if(b.Length!=n)throw new EndOfStreamException(); return b; }
        public static int Count(BinaryReader r,int max=100000) { uint n=r.ReadUInt32(); if(n>max || n>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Length/count limit."); return (int)n; }
        public static string Text(BinaryReader r)=>new UTF8Encoding(false,true).GetString(Bytes(r,Count(r,MaxString)));
        public static void Text(BinaryWriter w,string s) { if(s==null)throw new InvalidDataException("Null string."); var b=Encoding.UTF8.GetBytes(s); if(b.Length>MaxString)throw new InvalidDataException("String size."); w.Write((uint)b.Length); w.Write(b); }
        public static bool Bool(BinaryReader r) { byte b=r.ReadByte(); if(b>1)throw new InvalidDataException("Boolean must be 0 or 1."); return b==1; }
        public static T Enum<T>(BinaryReader r) where T:struct { byte b=r.ReadByte(); var v=(T)System.Enum.ToObject(typeof(T),b); if(!System.Enum.IsDefined(typeof(T),v))throw new InvalidDataException("Unknown "+typeof(T).Name); return v; }
    }
    public sealed class BuildIdentity
    {
        public string Commit="", SourceHash="", EditorVersion="", Backend="", PackageLockHash="";
        public bool Dirty;
        internal void Write(BinaryWriter w) { ReplayBinary.Text(w,Commit); w.Write(Dirty); ReplayBinary.Text(w,SourceHash); ReplayBinary.Text(w,EditorVersion); ReplayBinary.Text(w,Backend); ReplayBinary.Text(w,PackageLockHash); }
        internal static BuildIdentity Read(BinaryReader r)=>new BuildIdentity { Commit=ReplayBinary.Text(r), Dirty=ReplayBinary.Bool(r), SourceHash=ReplayBinary.Text(r), EditorVersion=ReplayBinary.Text(r), Backend=ReplayBinary.Text(r), PackageLockHash=ReplayBinary.Text(r) };
        public override string ToString()=>Commit+" dirty="+Dirty+" source="+SourceHash+" backend="+Backend;
    }
    public sealed class ReplayHeader
    {
        public string RulesVersion="";
        public int TickRateHz;
        public ulong Seed;
        public long TickLimit;
        public byte[] Scenario=Array.Empty<byte>();
        public BuildIdentity Build=new BuildIdentity();
        public string WestPreset="none", EastPreset="none";
        public int AiDelayTicks = -1; // -1: no provider (including schemas 2/3)
        public string AiProfile = "default";
        internal byte[] Encode()=>ReplayBinary.Pack(w=> { ReplayBinary.Text(w,RulesVersion); w.Write(TickRateHz); w.Write(Seed); w.Write(TickLimit); w.Write((uint)Scenario.Length); w.Write(Scenario); Build.Write(w); ReplayBinary.Text(w,WestPreset); ReplayBinary.Text(w,EastPreset); w.Write(AiDelayTicks); ReplayBinary.Text(w,AiProfile); });
        internal static ReplayHeader Decode(byte[] b,uint schema)=>ReplayBinary.Unpack(b,r=>new ReplayHeader { RulesVersion=ReplayBinary.Text(r),TickRateHz=r.ReadInt32(),Seed=r.ReadUInt64(),TickLimit=r.ReadInt64(),Scenario=ReplayBinary.Bytes(r,ReplayBinary.Count(r,ReplayBinary.MaxRecord)),Build=BuildIdentity.Read(r),WestPreset=schema>=3?ReplayBinary.Text(r):"none",EastPreset=schema>=3?ReplayBinary.Text(r):"none",AiDelayTicks=schema>=4?r.ReadInt32():-1,AiProfile=schema>=4?ReplayBinary.Text(r):"default" });
    }
    public enum ReplayRecordKind:byte { Input=1, TickHash=2, DiagnosticCheckpoint=3, End=4, CommandResults=5 }
    public sealed class ReplayRecord
    {
        public ReplayRecordKind Kind { get; }
        public long Tick { get; }
        public ulong LogIndex { get; }
        public byte[] Payload { get; }
        public ReplayRecord(ReplayRecordKind kind,long tick,ulong index,byte[] payload) { Kind=kind; Tick=tick; LogIndex=index; Payload=payload; }
    }
    public sealed class ReplayWriter:IDisposable
    {
        private readonly BinaryWriter writer;
        private bool ended;
        public ReplayWriter(Stream stream,ReplayHeader header)
        {
            writer=new BinaryWriter(stream,Encoding.UTF8,true);
            byte[] b=header.Encode(); if(b.Length>ReplayBinary.MaxRecord)throw new InvalidDataException("Header size.");
            writer.Write(Encoding.ASCII.GetBytes("RTSRPL01")); writer.Write(4U); writer.Write((uint)b.Length); writer.Write(b);
        }
        public void Write(ReplayRecord record)
        {
            if(ended || record.Tick<0 || !System.Enum.IsDefined(typeof(ReplayRecordKind),record.Kind) || record.Payload==null || record.Payload.Length>ReplayBinary.MaxRecord-53)throw new InvalidDataException("Invalid record.");
            writer.Write((uint)(53+record.Payload.Length)); writer.Write((byte)record.Kind); writer.Write(record.Tick); writer.Write(record.LogIndex);
            writer.Write((uint)record.Payload.Length); writer.Write(ReplayBinary.Hash(record.Payload)); writer.Write(record.Payload);
            ended=record.Kind==ReplayRecordKind.End;
        }
        public void Dispose() { writer.Flush(); writer.Dispose(); }
    }
    /// <summary>Streaming bounded reader. Missing End is rejected, never reported as a successful replay.</summary>
    public sealed class ReplayReader:IDisposable
    {
        private readonly BinaryReader reader;
        private bool ended;
        public ReplayHeader Header { get; }
        public ReplayReader(Stream stream)
        {
            reader=new BinaryReader(stream,Encoding.UTF8,true);
            if(!ReplayBinary.Bytes(reader,8).SequenceEqual(Encoding.ASCII.GetBytes("RTSRPL01")))throw new InvalidDataException("Replay Magic mismatch.");
            uint schema=reader.ReadUInt32(); if(schema!=2 && schema!=3 && schema!=4)throw new InvalidDataException("Unknown replay schemaVersion.");
            Header=ReplayHeader.Decode(ReplayBinary.Bytes(reader,ReplayBinary.Count(reader,ReplayBinary.MaxRecord)),schema);
            if ((Header.AiDelayTicks != -1 && Header.AiDelayTicks != 0 && Header.AiDelayTicks != 60 && Header.AiDelayTicks != 200 && Header.AiDelayTicks != 400) ||
                (Header.AiProfile != "default" && Header.AiProfile != "long")) throw new InvalidDataException("AI timing header.");
            if(Header.TickRateHz<=0 || Header.TickLimit<0 || Header.TickLimit>10000000)throw new InvalidDataException("Header tick limits.");
        }
        public ReplayRecord Read()
        {
            if(ended)return null;
            uint length=reader.ReadUInt32(); if(length<53 || length>ReplayBinary.MaxRecord)throw new InvalidDataException("Record length.");
            var kind=ReplayBinary.Enum<ReplayRecordKind>(reader); long tick=reader.ReadInt64(); ulong index=reader.ReadUInt64(); uint payloadLength=reader.ReadUInt32();
            if(tick<0 || tick>Header.TickLimit || payloadLength!=length-53)throw new InvalidDataException("Record tick/length.");
            var hash=ReplayBinary.Bytes(reader,32); var payload=ReplayBinary.Bytes(reader,(int)payloadLength);
            if(!hash.SequenceEqual(ReplayBinary.Hash(payload)))throw new InvalidDataException("Payload SHA-256 mismatch at tick "+tick+", LogIndex "+index);
            ended=kind==ReplayRecordKind.End;
            if(ended && reader.BaseStream.ReadByte()!=-1)throw new InvalidDataException("Trailing data after End.");
            return new ReplayRecord(kind,tick,index,payload);
        }
        public void Dispose()=>reader.Dispose();
    }
    public static class InputBinary
    {
        public static byte[] Encode(ScheduledInput v)=>ReplayBinary.Pack(w=>
        {
            w.Write(v.LogIndex); w.Write((byte)v.Kind); w.Write(v.AcceptedTick); w.Write(v.ApplyTick); w.Write(v.RequestId); w.Write(v.IssuerSequence); w.Write(v.DeadlineTick); w.Write((byte)v.ResolutionReason); w.Write((uint)v.Orders.Count);
            foreach(var o in v.Orders)
            {
                w.Write(o.CommandId); w.Write(o.BatchId); w.Write((byte)o.Source); Scope(w,o.Target); w.Write((byte)o.Kind); Goal(w,o.Goal); w.Write(o.Priority);
                w.Write(o.AllowedLoss.Permille); w.Write((byte)o.End.Kind); w.Write(o.End.Tick); w.Write(o.ReservePermille); w.Write(o.TargetRevision);
                w.Write((uint)o.Parents.Count); foreach(var p in o.Parents) { Scope(w,p.Scope); w.Write(p.Revision); }
                w.Write(o.ObservedTick); w.Write(o.Expiration.ValidUntilTick); w.Write(o.Expiration.MaxObservationAgeTicks); w.Write((byte)o.Expiration.Flags);
            }
            // Only economy inputs carry this, so every Ver.1 input keeps its exact bytes.
            if(v.Kind==InputKind.Economy)
            {
                var e=v.Economy;
                w.Write(e.FactionId); w.Write(e.IssuerSequence); w.Write((byte)e.Kind); w.Write((byte)e.Building); w.Write(e.Cell); w.Write(e.ProducerId); w.Write((byte)e.Unit);
                w.Write((uint)e.VillagerIds.Count); foreach(var id in e.VillagerIds) w.Write(id);
                w.Write((byte)e.TargetKind); w.Write(e.TargetId); w.Write(e.Enabled);
                // V3-2: only a belt run carries cells, so every V3-1 economy input keeps its exact bytes.
                if(e.Kind==EconomyCommandKind.PlaceBelt)
                {
                    w.Write((uint)e.Cells.Count); for(int i=0;i<e.Cells.Count;i++) { w.Write(e.Cells[i]); w.Write((byte)e.Facings[i]); }
                }
                // V3-2: a mine or smelter carries the side of its output; a barracks keeps its V3-1 bytes.
                if(e.Kind==EconomyCommandKind.PlaceBuilding && e.Building!=BuildingKind.Barracks) w.Write((byte)e.Facing);
                // V3-3: only a policy change carries the policy.
                if(e.Kind==EconomyCommandKind.SetEconomyPolicy) w.Write((byte)e.Policy);
                // V3-4: only advancing carries the civilisation.
                if(e.Kind==EconomyCommandKind.AdvanceAge) w.Write((byte)e.Civ);
            }
        });
        public static ScheduledInput Decode(byte[] b)=>ReplayBinary.Unpack(b,r=>
        {
            ulong index=r.ReadUInt64(); var kind=ReplayBinary.Enum<InputKind>(r); long accepted=r.ReadInt64(), apply=r.ReadInt64(); ulong request=r.ReadUInt64(), sequence=r.ReadUInt64();
            if(accepted<0 || apply<=0 || accepted>apply)throw new InvalidDataException("Input ticks.");
            long deadline=r.ReadInt64(); var resolution=ReplayBinary.Enum<ReasonCode>(r);
            var orders=new PolicyOrder[ReplayBinary.Count(r)];
            for(int i=0;i<orders.Length;i++)
            {
                ulong command=r.ReadUInt64(),batch=r.ReadUInt64(); var source=ReplayBinary.Enum<CommandSource>(r); var target=Scope(r); var policy=ReplayBinary.Enum<PolicyKind>(r); var goal=Goal(r); byte priority=r.ReadByte();
                ushort loss=r.ReadUInt16(); var end=new EndCondition(ReplayBinary.Enum<EndKind>(r),r.ReadInt64()); ushort reserve=r.ReadUInt16(); ulong revision=r.ReadUInt64();
                var parents=new PolicyVersion[ReplayBinary.Count(r)]; for(int j=0;j<parents.Length;j++)parents[j]=new PolicyVersion(Scope(r),r.ReadUInt64());
                long observed=r.ReadInt64(),valid=r.ReadInt64(); int age=r.ReadInt32(); byte flags=r.ReadByte();
                if(loss>1000 || reserve>1000 || flags>7)throw new InvalidDataException("Order range.");
                orders[i]=new PolicyOrder(command,batch,source,target,policy,goal,priority,new LossBudget(loss),end,reserve,revision,parents,observed,new Expiration(valid,age,(ExpireFlags)flags));
            }
            if(kind==InputKind.Economy)
            {
                if(orders.Length!=0)throw new InvalidDataException("Economy input with orders.");
                uint faction=r.ReadUInt32(); ulong issuer=r.ReadUInt64(); var ek=ReplayBinary.Enum<EconomyCommandKind>(r); var building=(BuildingKind)r.ReadByte(); int cell=r.ReadInt32(); uint producer=r.ReadUInt32(); var unit=(UnitKind)r.ReadByte();
                var villagers=new uint[ReplayBinary.Count(r)]; for(int i=0;i<villagers.Length;i++)villagers[i]=r.ReadUInt32();
                var target=ReplayBinary.Enum<EconomyTargetKind>(r); uint targetId=r.ReadUInt32(); bool enabled=ReplayBinary.Bool(r);
                int[] cells=null; Facing[] facings=null;
                if(ek==EconomyCommandKind.PlaceBelt)
                {
                    int n=ReplayBinary.Count(r); if(n>EconomyCommand.MaxBeltRun)throw new InvalidDataException("Belt run length.");
                    cells=new int[n]; facings=new Facing[n];
                    for(int i=0;i<n;i++) { cells[i]=r.ReadInt32(); facings[i]=ReplayBinary.Enum<Facing>(r); }
                }
                var facing=ek==EconomyCommandKind.PlaceBuilding && building!=BuildingKind.Barracks ? ReplayBinary.Enum<Facing>(r) : Facing.North;
                var policy=ek==EconomyCommandKind.SetEconomyPolicy ? ReplayBinary.Enum<EconomyPolicy>(r) : EconomyPolicy.Balanced;
                var civ=ek==EconomyCommandKind.AdvanceAge ? ReplayBinary.Enum<CivKind>(r) : CivKind.Primitive;
                return new ScheduledInput(index,accepted,apply,new EconomyCommand(faction,issuer,ek,building,cell,producer,unit,villagers,target,targetId,enabled,cells,facings,facing,policy,civ));
            }
            return new ScheduledInput(index,kind,accepted,apply,request,sequence,orders,deadline,resolution);
        });
        private static void Scope(BinaryWriter w,ScopeKey s) { w.Write(s.FactionId); w.Write((byte)s.Kind); w.Write(s.Id); }
        private static ScopeKey Scope(BinaryReader r)=>new ScopeKey(r.ReadUInt32(),ReplayBinary.Enum<ScopeKind>(r),r.ReadUInt32());
        private static void Goal(BinaryWriter w,PolicyGoal g) { w.Write((byte)g.Kind); w.Write(g.Id); w.Write(g.Point.X.Raw); w.Write(g.Point.Z.Raw); }
        private static PolicyGoal Goal(BinaryReader r)=>new PolicyGoal(ReplayBinary.Enum<GoalKind>(r),r.ReadUInt32(),new SimPoint(Fix64.FromRaw(r.ReadInt64()),Fix64.FromRaw(r.ReadInt64())));
    }
}
