using System;
using System.IO;
using System.Text;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>Explicit week-one scenario binary schema. Also canonicalizes authoring enumeration order.</summary>
    public static class ScenarioBinary
    {
        public const string RulesVersion = "week2-policy-3";
        public static byte[] Encode(ScenarioDefinition source)
        {
            var c = new WorldState(source).Config;
            using (var s = new MemoryStream())
            using (var w = new BinaryWriter(s))
            {
                w.Write(c.SchemaVersion); Text(w, c.ScenarioId); w.Write(c.Seed); w.Write(c.TickRateHz); w.Write(c.VerificationTickLimit);
                var m = c.Map;
                w.Write(m.WidthMeters); w.Write(m.HeightMeters); w.Write(m.CellSizeMeters); w.Write(m.WidthCells); w.Write(m.HeightCells); w.Write(m.DefaultPassable);
                w.Write((uint)m.BlockedCellIds.Length); foreach (var id in m.BlockedCellIds) w.Write(id);
                var r = c.Rules;
                w.Write(r.FactionCap); w.Write(r.CoreRadius.Raw); w.Write(r.OwnedObjectiveVision.Raw); w.Write(r.CaptureRadius.Raw);
                w.Write(r.CaptureDurationTicks); w.Write(r.CoreReinforcementIntervalTicks); w.Write(r.OutpostReinforcementIntervalTicks);
                w.Write((uint)c.UnitParameters.Length);
                foreach (var p in c.UnitParameters) { w.Write((byte)p.Kind); w.Write(p.Hp); w.Write(p.Speed.Raw); w.Write(p.Vision.Raw); w.Write(p.Range.Raw); w.Write(p.Damage); w.Write(p.AttackIntervalTicks); }
                w.Write((uint)c.Factions.Length); foreach (var f in c.Factions) { w.Write(f.Id); w.Write(f.CoreId); w.Write((uint)f.ArmyIds.Length); foreach (var id in f.ArmyIds) w.Write(id); }
                w.Write((uint)c.Armies.Length); foreach (var a in c.Armies) { w.Write(a.Id); w.Write(a.FactionId); Text(w,a.Role); w.Write(a.Capacity); Goal(w,a.HomeObjective); }
                w.Write((uint)c.Soldiers.Length); foreach (var p in c.Soldiers) { w.Write(p.Id); w.Write(p.FactionId); w.Write(p.ArmyId); w.Write((byte)p.Kind); w.Write(p.Alive); Point(w,p.Position); w.Write(p.Hp); }
                w.Write((uint)c.Cores.Length); foreach (var p in c.Cores) { w.Write(p.Id); w.Write(p.FactionId); Point(w,p.Position); w.Write(p.Hp); }
                w.Write((uint)c.Outposts.Length); foreach (var p in c.Outposts) { w.Write(p.Id); Point(w,p.Position); w.Write(p.OwnerFactionId); }
                return s.ToArray();
            }
        }
        public static ScenarioDefinition Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Scenario size.");
            using (var s = new MemoryStream(bytes, false))
            using (var r = new BinaryReader(s))
            {
                var c = new ScenarioDefinition { SchemaVersion=r.ReadInt32(), ScenarioId=Text(r), Seed=r.ReadUInt64(), TickRateHz=r.ReadInt32(), VerificationTickLimit=r.ReadInt64() };
                c.Map = new MapDefinition { WidthMeters=r.ReadInt32(), HeightMeters=r.ReadInt32(), CellSizeMeters=r.ReadInt32(), WidthCells=r.ReadInt32(), HeightCells=r.ReadInt32(), DefaultPassable=Bool(r), BlockedCellIds=new int[Count(r)] };
                for(int i=0;i<c.Map.BlockedCellIds.Length;i++) c.Map.BlockedCellIds[i]=r.ReadInt32();
                c.Rules = new RuleDefinition { FactionCap=r.ReadInt32(), CoreRadius=Fix(r), OwnedObjectiveVision=Fix(r), CaptureRadius=Fix(r), CaptureDurationTicks=r.ReadInt32(), CoreReinforcementIntervalTicks=r.ReadInt32(), OutpostReinforcementIntervalTicks=r.ReadInt32() };
                c.UnitParameters=new UnitParameters[Count(r)]; for(int i=0;i<c.UnitParameters.Length;i++) c.UnitParameters[i]=new UnitParameters { Kind=(UnitKind)r.ReadByte(), Hp=r.ReadInt32(), Speed=Fix(r), Vision=Fix(r), Range=Fix(r), Damage=r.ReadInt32(), AttackIntervalTicks=r.ReadInt32() };
                c.Factions=new FactionDefinition[Count(r)]; for(int i=0;i<c.Factions.Length;i++) { var f=new FactionDefinition { Id=r.ReadUInt32(), CoreId=r.ReadUInt32(), ArmyIds=new uint[Count(r)] }; for(int j=0;j<f.ArmyIds.Length;j++) f.ArmyIds[j]=r.ReadUInt32(); c.Factions[i]=f; }
                c.Armies=new ArmyDefinition[Count(r)]; for(int i=0;i<c.Armies.Length;i++) c.Armies[i]=new ArmyDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), Role=Text(r), Capacity=r.ReadInt32(), HomeObjective=Goal(r) };
                c.Soldiers=new SoldierDefinition[Count(r)]; for(int i=0;i<c.Soldiers.Length;i++) c.Soldiers[i]=new SoldierDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), ArmyId=r.ReadUInt32(), Kind=(UnitKind)r.ReadByte(), Alive=Bool(r), Position=Point(r), Hp=r.ReadInt32() };
                c.Cores=new CoreDefinition[Count(r)]; for(int i=0;i<c.Cores.Length;i++) c.Cores[i]=new CoreDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), Position=Point(r), Hp=r.ReadInt32() };
                c.Outposts=new OutpostDefinition[Count(r)]; for(int i=0;i<c.Outposts.Length;i++) c.Outposts[i]=new OutpostDefinition { Id=r.ReadUInt32(), Position=Point(r), OwnerFactionId=r.ReadUInt32() };
                if(s.Position!=s.Length) throw new InvalidDataException("Trailing scenario data.");
                return new WorldState(c).Config;
            }
        }
        private static int Count(BinaryReader r) { uint n=r.ReadUInt32(); if(n>100000 || n>r.BaseStream.Length-r.BaseStream.Position) throw new InvalidDataException("Scenario count."); return (int)n; }
        private static bool Bool(BinaryReader r) { byte b=r.ReadByte(); if(b>1) throw new InvalidDataException("Boolean."); return b==1; }
        private static Fix64 Fix(BinaryReader r)=>Fix64.FromRaw(r.ReadInt64());
        private static SimPoint Point(BinaryReader r)=>new SimPoint(Fix(r),Fix(r));
        private static PolicyGoal Goal(BinaryReader r)=>new PolicyGoal((GoalKind)r.ReadByte(),r.ReadUInt32(),Point(r));
        private static void Point(BinaryWriter w,SimPoint p) { w.Write(p.X.Raw); w.Write(p.Z.Raw); }
        private static void Goal(BinaryWriter w,PolicyGoal g) { w.Write((byte)g.Kind); w.Write(g.Id); Point(w,g.Point); }
        private static void Text(BinaryWriter w,string v) { var b=Encoding.UTF8.GetBytes(v); if(b.Length>1048576) throw new InvalidDataException("String size."); w.Write((uint)b.Length); w.Write(b); }
        private static string Text(BinaryReader r) { uint n=r.ReadUInt32(); if(n>1048576) throw new InvalidDataException("String size."); var b=r.ReadBytes((int)n); if(b.Length!=n) throw new EndOfStreamException(); return new UTF8Encoding(false,true).GetString(b); }
    }
}
