using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Rts.Contracts;

namespace Rts.Replay
{
    public sealed class StateDifference
    {
        public long Tick { get; }
        public string Field { get; }
        public string Left { get; }
        public string Right { get; }
        public StateDifference(long tick,string field,string left,string right) { Tick=tick; Field=field; Left=left; Right=right; }
        public override string ToString()=>"tick="+Tick+" "+Field+": left="+Left+" right="+Right;
    }
    public static class DiagnosticComparison
    {
        public static IReadOnlyList<KeyValuePair<string,string>> Fields(DiagnosticState state)
        {
            return ReplayBinary.Unpack(state.CanonicalState.ToArray(),r=>
            {
                if(r.ReadUInt32()!=1)throw new InvalidDataException("Diagnostic schema mismatch.");
                var fields=new List<KeyValuePair<string,string>>(); var names=new HashSet<string>(StringComparer.Ordinal);
                while(r.BaseStream.Position<r.BaseStream.Length)
                {
                    string name=ReplayBinary.Text(r); if(!names.Add(name))throw new InvalidDataException("Duplicate diagnostic field.");
                    byte type=r.ReadByte(); string value;
                    switch(type)
                    {
                        case 1:value=r.ReadByte().ToString(CultureInfo.InvariantCulture);break;
                        case 2:value=ReplayBinary.Bool(r)?"1":"0";break;
                        case 3:value=r.ReadInt32().ToString(CultureInfo.InvariantCulture);break;
                        case 4:value=r.ReadUInt32().ToString(CultureInfo.InvariantCulture);break;
                        case 5:value=r.ReadInt64().ToString(CultureInfo.InvariantCulture);break;
                        case 6:value=r.ReadUInt64().ToString(CultureInfo.InvariantCulture);break;
                        case 7:value=ReplayBinary.Text(r);break;
                        case 8:value=ReplayBinary.Hex(ReplayBinary.Bytes(r,ReplayBinary.Count(r,ReplayBinary.MaxRecord)));break;
                        default:throw new InvalidDataException("Diagnostic field type.");
                    }
                    fields.Add(new KeyValuePair<string,string>(name,value));
                }
                return fields;
            });
        }
        public static StateDifference First(DiagnosticState left,DiagnosticState right)
        {
            var a=Fields(left); var b=Fields(right);
            for(int i=0;i<Math.Max(a.Count,b.Count);i++)
            {
                if(i>=a.Count)return new StateDifference(right.Tick,b[i].Key,"<missing>",b[i].Value);
                if(i>=b.Count)return new StateDifference(left.Tick,a[i].Key,a[i].Value,"<missing>");
                if(a[i].Key!=b[i].Key)return new StateDifference(left.Tick,"FieldOrder",a[i].Key,b[i].Key);
                if(a[i].Value!=b[i].Value)return new StateDifference(left.Tick,a[i].Key,a[i].Value,b[i].Value);
            }
            if(!left.CanonicalState.SequenceEqual(right.CanonicalState))return new StateDifference(left.Tick,"FieldType","left schema","right schema");
            return null;
        }
        public static byte[] EventHash(FactionFrame frame)=>ReplayBinary.Hash(ReplayBinary.Pack(w=>
        {
            // Week one only emits globally visible terminal events; frame 1 is the internal event sequence.
            w.Write((uint)frame.Events.Count);
            foreach(var e in frame.Events) { w.Write(e.Tick); w.Write(e.Ordinal); w.Write((byte)e.Kind); w.Write(e.AudienceMask); w.Write(e.SubjectId); w.Write(e.CommandId); w.Write(e.Position.X.Raw); w.Write(e.Position.Z.Raw); w.Write(e.Value); w.Write((byte)e.Reason); }
        }));
    }
}
