using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private byte[] configurationHash;
        /// <summary>Schema 1: ordered named fields, typed LE values. No derived frames or scratch buffers.</summary>
        public DiagnosticState CaptureDiagnostic()
        {
            if(configurationHash==null)
                using(var sha=SHA256.Create()) configurationHash=sha.ComputeHash(ScenarioBinary.Encode(world.Config));
            using(var s=new MemoryStream())
            using(var w=new StateWriter(s))
            {
                w.Write(1U);
                w.Text("Rules.Version",ScenarioBinary.RulesVersion); w.Blob("Config.Hash",configurationHash);
                w.Value("Tick",world.Tick); w.Value("Result.HasEnded",world.Result.HasEnded); w.Value("Result.WinnerFactionId",world.Result.WinnerFactionId);
                w.Value("Result.IsDraw",world.Result.IsDraw); w.Value("Result.IsFault",world.Result.IsFault); w.Value("Result.IsUndecided",world.Result.IsUndecided);
                w.Value("NextSoldierId",world.NextSoldierId); w.Value("NextArmyId",world.NextArmyId); w.Value("NextCoreId",world.NextCoreId); w.Value("NextOutpostId",world.NextOutpostId); w.Value("NextFactionId",world.NextFactionId);
                w.Value("CombatRandom.State",world.CombatRandom.State); w.Value("CombatRandom.CallCount",world.CombatRandom.CallCount);
                w.Value("AiRandom.State",world.AiRandom.State); w.Value("AiRandom.CallCount",world.AiRandom.CallCount);
                w.Value("Soldiers.Count",(uint)world.Soldiers.Length);
                foreach(var p in world.Soldiers)
                {
                    string n="Soldiers["+p.Initial.Id.ToString(CultureInfo.InvariantCulture)+"].";
                    w.Value(n+"Id",p.Initial.Id); w.Value(n+"FactionId",p.Initial.FactionId); w.Value(n+"ArmyId",p.Initial.ArmyId); w.Value(n+"Kind",(byte)p.Initial.Kind);
                    w.Value(n+"Alive",p.Alive); w.Point(n+"Position",p.Position); w.Value(n+"Hp",p.Hp); w.Value(n+"TargetKind",p.TargetKind); w.Value(n+"TargetId",p.TargetId);
                    w.Value(n+"NextAttackTick",p.NextAttackTick); w.Point(n+"MoveGoal",p.MoveGoal); w.Value(n+"StepDistance.Raw",p.StepDistance.Raw);
                    w.Value(n+"IsMoving",p.IsMoving); w.Value(n+"IsAttacking",p.IsAttacking); w.Value(n+"IsRetreating",p.IsRetreating);
                    // Parameters are immutable copies of Config; serialize explicitly to detect accidental divergence too.
                    w.Value(n+"Parameters.Kind",(byte)p.Parameters.Kind); w.Value(n+"Parameters.Hp",p.Parameters.Hp); w.Value(n+"Parameters.Damage",p.Parameters.Damage);
                    w.Value(n+"Parameters.AttackIntervalTicks",p.Parameters.AttackIntervalTicks); w.Value(n+"Parameters.Speed.Raw",p.Parameters.Speed.Raw); w.Value(n+"Parameters.Vision.Raw",p.Parameters.Vision.Raw); w.Value(n+"Parameters.Range.Raw",p.Parameters.Range.Raw);
                }
                w.Value("Armies.Count",(uint)world.Armies.Length);
                foreach(var a in world.Armies) { string n="Armies["+a.Definition.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Id",a.Definition.Id); w.Ids(n+"SoldierIds",a.SoldierIds);
                    w.Value(n+"Path.Count", (uint)a.Path.Length);
                    for (int i = 0; i < a.Path.Length; i++) w.Value(n+"Path["+i.ToString(CultureInfo.InvariantCulture)+"]", a.Path[i]);
                    w.Value(n+"PathCursor",a.PathCursor); w.Value(n+"AutoStage",a.AutoStage);
                    w.Point(n+"PathGoal",a.PathGoal); w.Value(n+"HasPathGoal",a.HasPathGoal); w.Value(n+"PathImpossible",a.PathImpossible);
                }
                // Immutable definitions (including formation slots and roles) are covered by Config.Hash.
                w.Value("Outposts.Count",(uint)world.Outposts.Length);
                foreach(var o in world.Outposts) {
                    string n="Outposts["+o.Definition.Id.ToString(CultureInfo.InvariantCulture)+"].";
                    w.Value(n+"Id",o.Definition.Id); w.Value(n+"OwnerFactionId",o.OwnerFactionId);
                    w.Value(n+"CapturingFaction",o.CapturingFaction); w.Value(n+"CaptureTicks",o.CaptureTicks);
                }
                w.Value("Cores.Count",(uint)world.Cores.Length);
                foreach(var c in world.Cores) { string n="Cores["+c.Definition.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Id",c.Definition.Id); w.Value(n+"Hp",c.Hp); }
                w.Value("Factions.Count",(uint)world.Factions.Length);
                foreach(var f in world.Factions) { string n="Factions["+f.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Id",f.Id); w.Value(n+"CoreId",f.CoreId); w.Value(n+"AliveCount",f.AliveCount); w.Ids(n+"ArmyIds",f.ArmyIds); }
                w.Value("Inputs.Cursor",world.InputCursor);
                WriteCommands(w);
                // Active policies are keyed by army, ordered by ApplyTick then LogIndex then army ID.
                var orders=(ArmyState[])world.Armies.Clone();
                Array.Sort(orders,(a,b)=> { int c=a.ApplyTick.CompareTo(b.ApplyTick); if(c==0)c=a.LogIndex.CompareTo(b.LogIndex); return c==0?a.Definition.Id.CompareTo(b.Definition.Id):c; });
                foreach(var a in orders) { string n="Commands[Army="+a.Definition.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Policy",(byte)a.Policy); w.Goal(n+"Goal",a.Goal); w.Value(n+"CommandId",a.CommandId); w.Value(n+"AcceptedTick",a.AcceptedTick); w.Value(n+"ApplyTick",a.ApplyTick); w.Value(n+"LogIndex",a.LogIndex); }
                foreach(var f in world.Factions) { string n="Observations["+f.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"NextContactId",f.NextContactId); w.Ids(n+"ContactIds",f.ContactIds); }
                w.Value("AiMemory.Count",0U);
                return new DiagnosticState(world.Tick,s.ToArray());
            }
        }
        private sealed class StateWriter : BinaryWriter
        {
            public StateWriter(Stream s):base(s,Encoding.UTF8,true) { }
            private void Name(string n,byte type) { RawText(n); Write(type); }
            private void RawText(string v) { var b=Encoding.UTF8.GetBytes(v); Write((uint)b.Length); Write(b); }
            public void Value(string n,byte v) { Name(n,1); Write(v); }
            public void Value(string n,bool v) { Name(n,2); Write(v); }
            public void Value(string n,int v) { Name(n,3); Write(v); }
            public void Value(string n,uint v) { Name(n,4); Write(v); }
            public void Value(string n,long v) { Name(n,5); Write(v); }
            public void Value(string n,ulong v) { Name(n,6); Write(v); }
            public void Text(string n,string v) { Name(n,7); RawText(v); }
            public void Blob(string n,byte[] v) { Name(n,8); Write((uint)v.Length); Write(v); }
            public void Point(string n,SimPoint p) { Value(n+".X.Raw",p.X.Raw); Value(n+".Z.Raw",p.Z.Raw); }
            public void Goal(string n,PolicyGoal g) { Value(n+".Kind",(byte)g.Kind); Value(n+".Id",g.Id); Point(n+".Point",g.Point); }
            public void Ids(string n,uint[] ids) { Value(n+".Count",(uint)ids.Length); for(int i=0;i<ids.Length;i++)Value(n+"["+i.ToString(CultureInfo.InvariantCulture)+"]",ids[i]); }
        }
    }
}
