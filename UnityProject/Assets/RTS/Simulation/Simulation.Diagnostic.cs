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
                w.Value("Result.IsDraw",world.Result.IsDraw); w.Value("Result.IsFault",world.Result.IsFault); w.Value("Result.IsUndecided",world.Result.IsUndecided); w.Value("Result.IsAgeVictory",world.Result.IsAgeVictory);
                w.Value("NextSoldierId",world.NextSoldierId); w.Value("NextArmyId",world.NextArmyId); w.Value("NextCoreId",world.NextCoreId); w.Value("NextOutpostId",world.NextOutpostId); w.Value("NextFactionId",world.NextFactionId);
                w.Value("CombatRandom.State",world.CombatRandom.State); w.Value("CombatRandom.CallCount",world.CombatRandom.CallCount);
                w.Value("AiRandom.State",world.AiRandom.State); w.Value("AiRandom.CallCount",world.AiRandom.CallCount);
                w.Value("Soldiers.Count",(uint)world.SoldierCount);
                for (int i = 0; i < world.SoldierCount; i++)
                {
                    var p = world.Soldiers[i];
                    string n="Soldiers["+p.Initial.Id.ToString(CultureInfo.InvariantCulture)+"].";
                    w.Value(n+"Id",p.Initial.Id); w.Value(n+"FactionId",p.Initial.FactionId); w.Value(n+"ArmyId",p.Initial.ArmyId); w.Value(n+"Kind",(byte)p.Initial.Kind);
                    w.Value(n+"Alive",p.Alive); w.Point(n+"Position",p.Position); w.Value(n+"Hp",p.Hp); w.Value(n+"TargetKind",p.TargetKind); w.Value(n+"TargetId",p.TargetId);
                    w.Value(n+"NextAttackTick",p.NextAttackTick); w.Point(n+"MoveGoal",p.MoveGoal); w.Value(n+"StepDistance.Raw",p.StepDistance.Raw);
                    w.Value(n+"IsMoving",p.IsMoving); w.Value(n+"IsAttacking",p.IsAttacking); w.Value(n+"IsRetreating",p.IsRetreating);
                    w.Value(n+"Joining",p.Joining); w.Value(n+"TacticalRoute",p.TacticalRoute);
                    w.Value(n+"LocalCursor",p.LocalCursor); w.Value(n+"JoinCursor",p.JoinCursor); w.Value(n+"SlotWaitTimedOut",p.SlotWaitTimedOut);
                    w.Value(n+"SlotWaitSinceTick",p.SlotWaitSinceTick); w.Point(n+"SlotWaitTarget",p.SlotWaitTarget); w.Point(n+"LocalGoal",p.LocalGoal);
                    w.Value(n+"LocalPath.Count",(uint)(p.LocalPath?.Length ?? 0));
                    if (p.LocalPath != null) for (int j = 0; j < p.LocalPath.Length; j++) w.Value(n+"LocalPath["+j.ToString(CultureInfo.InvariantCulture)+"]",p.LocalPath[j]);
                    if (CultOn) w.Value(n+"CultTargetContactId", p.CultTargetContactId);
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
                    w.Value(n+"CapturingFaction",o.CapturingFaction); w.Value(n+"CaptureTicks",o.CaptureTicks); w.Value(n+"NextReinforcementTick",o.NextReinforcementTick);
                }
                w.Value("Cores.Count",(uint)world.Cores.Length);
                foreach(var c in world.Cores) { string n="Cores["+c.Definition.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Id",c.Definition.Id); w.Value(n+"Hp",c.Hp); w.Value(n+"NextReinforcementTick",c.NextReinforcementTick); if (CoreDefenceOn) w.Value(n+"DefenceTimer",c.DefenceTimer); }
                w.Value("Factions.Count",(uint)world.Factions.Length);
                foreach(var f in world.Factions) { string n="Factions["+f.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Id",f.Id); w.Value(n+"CoreId",f.CoreId); w.Value(n+"AliveCount",f.AliveCount); w.Ids(n+"ArmyIds",f.ArmyIds);
                    w.Bools(n+"VisibleCells", f.VisibleCells); w.Bools(n+"ExploredCells", f.ExploredCells);
                    for(int i=0;i<f.Objectives.Length;i++) { var m=f.Objectives[i]; string o=n+"Objectives["+i.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(o+"OwnerKnown",m.OwnerKnown); w.Value(o+"Owner",m.OwnerFactionId); w.Value(o+"HpKnown",m.HpKnown); w.Value(o+"Hp",m.Hp); w.Value(o+"LastSeenTick",m.LastSeenTick); }
                }
                w.Value("Inputs.Cursor",world.InputCursor);
                WriteCommands(w);
                // Active policies are keyed by army, ordered by ApplyTick then LogIndex then army ID.
                var orders=(ArmyState[])world.Armies.Clone();
                Array.Sort(orders,(a,b)=> { int c=a.ApplyTick.CompareTo(b.ApplyTick); if(c==0)c=a.LogIndex.CompareTo(b.LogIndex); return c==0?a.Definition.Id.CompareTo(b.Definition.Id):c; });
                foreach(var a in orders) { string n="Commands[Army="+a.Definition.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"Policy",(byte)a.Policy); w.Goal(n+"Goal",a.Goal); w.Value(n+"CommandId",a.CommandId); w.Value(n+"AcceptedTick",a.AcceptedTick); w.Value(n+"ApplyTick",a.ApplyTick); w.Value(n+"LogIndex",a.LogIndex); }
                foreach(var f in world.Factions) { string n="Observations["+f.Id.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(n+"NextArmyContactId",f.NextArmyContactId);
                    for (int i = 0; i < f.ArmyContacts.Length; i++) { var m = f.ArmyContacts[i]; string a = n+"ArmyContacts["+i.ToString(CultureInfo.InvariantCulture)+"].";
                        w.Value(a+"Id",m.Id); w.Point(a+"Position",m.Position); w.Value(a+"LastSeenTick",m.LastSeenTick);
                        w.Value(a+"Min",m.Min); w.Value(a+"Max",m.Max); w.Value(a+"Visible",m.Visible); w.Value(a+"Absent",m.Absent); w.Ids(a+"Covered",m.Covered ?? Array.Empty<uint>()); }
                    w.Value(n+"NextContactId",f.NextContactId); w.Ids(n+"ContactIds",f.ContactIds,world.SoldierCount);
                    for(int i=0;i<world.SoldierCount;i++) { string c=n+"Contacts["+i.ToString(CultureInfo.InvariantCulture)+"]."; w.Point(c+"Position",f.ContactPositions[i]); w.Value(c+"LastSeenTick",f.ContactLastSeenTicks[i]); w.Value(c+"Absent",f.ContactAbsent[i]); }
                    if (CultOn)
                        for (int i=0;i<world.SoldierCount;i++) { string c=n+"CultObservations["+i.ToString(CultureInfo.InvariantCulture)+"]."; w.Value(c+"ContactId",f.CultObservedContactIds[i]); w.Value(c+"Kind",(byte)f.CultObservedKinds[i]); w.Value(c+"LastSeenTick",f.CultObservedLastSeenTicks[i]); w.Value(c+"ValidUntilTick",f.CultObservedValidUntilTicks[i]); } }
                WriteDecision(w);
                // Written only when the economy is on, so Ver.1 scenarios keep their exact canonical bytes.
                if (world.Config.Economy.Enabled) WriteEconomy(w);
                return new DiagnosticState(world.Tick,s.ToArray());
            }
        }
        private void WriteEconomy(StateWriter w)
        {
            for (int f = 0; f < world.Economies.Length; f++)
            {
                var e = world.Economies[f]; string n = "Economy[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Food", e.Food); w.Value(n + "Wood", e.Wood); w.Value(n + "Queued", e.Queued); w.Value(n + "TrainRemaining", e.TrainRemaining); w.Value(n + "AutoOff", e.AutoOff);
            }
            w.Value("ResourceNodes.Count", (uint)world.Nodes.Length);
            foreach (var r in world.Nodes)
            {
                string n = "ResourceNodes[" + r.Definition.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Remaining", r.Remaining);
                if (world.Config.Economy.FishingEnabled) w.Value(n + "Fishing", r.Fishing);
            }
            w.Value("NextBuildingId", world.NextBuildingId);
            w.Value("Buildings.Count", (uint)world.BuildingCount);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i]; string n = "Buildings[" + b.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Id", b.Id); w.Value(n + "FactionId", b.FactionId); w.Value(n + "Kind", (byte)b.Kind);
                w.Value(n + "OriginCell", b.OriginCell); w.Value(n + "WorkCell", b.WorkCell); w.Value(n + "Alive", b.Alive); w.Value(n + "Complete", b.Complete);
                w.Value(n + "Hp", b.Hp); w.Value(n + "Progress", b.Progress); w.Value(n + "Queued", b.Queued); w.Value(n + "TrainRemaining", b.TrainRemaining);
                // V3-2: only with industry, so a V3-1 building keeps its exact canonical bytes.
                if (world.Config.Economy.Industry)
                {
                    w.Value(n + "Facing", (byte)b.Facing); w.Value(n + "Input", b.Input); w.Value(n + "Output", b.Output);
                    w.Value(n + "Timer", b.Timer); w.Value(n + "NodeId", b.NodeId); w.Value(n + "Held", b.Held);
                    if (world.Config.Economy.Ages)
                    {
                        w.Value(n + "QueuedMetal", b.QueuedMetal); w.Value(n + "QueuedGems", b.QueuedGems); w.Value(n + "Interval", b.Interval); w.Value(n + "Shots", b.Shots); w.Value(n + "Researching", (byte)b.Researching);
                        if (world.Config.Economy.GoldEnabled) w.Value(n + "QueuedGold", b.QueuedGold);
                        var kinds = b.QueueKinds ?? Array.Empty<UnitKind>();
                        w.Value(n + "QueueKinds.Count", (uint)kinds.Length);
                        for (int q = 0; q < kinds.Length; q++) w.Value(n + "QueueKinds[" + q.ToString(CultureInfo.InvariantCulture) + "]", (byte)kinds[q]);
                    }
                    if (b.Kind == BuildingKind.Bridge)
                    {
                        w.Value(n + "DestroyedTick", b.DestroyedTick);
                        var bridgeCells = b.BridgeCells ?? Array.Empty<int>();
                        w.Value(n + "BridgeCells.Count", (uint)bridgeCells.Length);
                        for (int j = 0; j < bridgeCells.Length; j++)
                            w.Value(n + "BridgeCells[" + j.ToString(CultureInfo.InvariantCulture) + "]", bridgeCells[j]);
                    }
                    if (world.Config.Economy.ProcessingChain || world.Config.Economy.Forestry && world.Economies[b.FactionId - 1].Age >= 2)
                    {
                        w.Value(n + "InputSecondary", b.InputSecondary); w.Value(n + "QueuedSteel", b.QueuedSteel);
                        if (world.Config.Economy.Forestry && world.Economies[b.FactionId - 1].Age >= 2) w.Value(n + "QueuedBowGear", b.QueuedBowGear);
                    }
                    if (world.Config.Economy.Mountain && b.Kind == BuildingKind.MineShaft)
                        w.Value(n + "MountainOreNext", b.MountainOreNext);
                }
                if (world.Config.Economy.Caravan && b.Kind == BuildingKind.Caravanserai)
                {
                    w.Value(n + "CaravanOutpostId", b.CaravanOutpostId); w.Value(n + "CaravanMarketId", b.CaravanMarketId);
                    w.Value(n + "CaravanDistance.Raw", b.CaravanDistance.Raw); w.Value(n + "CaravanWoodReward", b.CaravanWoodReward);
                }
                if (world.Config.Economy.Sanctuary && b.Kind == BuildingKind.Shrine)
                    w.Value(n + "SanctuaryOutpostId", b.SanctuaryOutpostId);
            }
            w.Value("NextVillagerId", world.NextVillagerId);
            w.Value("Villagers.Count", (uint)world.VillagerCount);
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i]; string n = "Villagers[" + v.Id.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Id", v.Id); w.Value(n + "FactionId", v.FactionId); w.Value(n + "Alive", v.Alive); w.Value(n + "Hp", v.Hp);
                w.Point(n + "Position", v.Position); w.Point(n + "MoveGoal", v.MoveGoal); w.Value(n + "IsMoving", v.IsMoving);
                w.Value(n + "Task", (byte)v.Task); w.Value(n + "NodeId", v.NodeId); w.Value(n + "CarryKind", (byte)v.CarryKind); w.Value(n + "Carry", v.Carry);
                w.Value(n + "NextGatherTick", v.NextGatherTick); w.Point(n + "RouteGoal", v.RouteGoal); w.Value(n + "RouteCursor", v.RouteCursor);
                w.Value(n + "BuildingId", v.BuildingId);
                if (world.Config.Economy.Caravan)
                {
                    w.Value(n + "CaravanMarketId", v.CaravanMarketId); w.Value(n + "CaravanseraiId", v.CaravanseraiId);
                    w.Value(n + "CaravanOutpostId", v.CaravanOutpostId); w.Value(n + "CaravanStage", v.CaravanStage); w.Value(n + "CaravanWood", v.CaravanWood);
                    w.Value(n + "CaravanGems", v.CaravanGems); w.Value(n + "CaravanDangerStopped", v.CaravanDangerStopped); w.Value(n + "CaravanSafeTicks", v.CaravanSafeTicks);
                }
                if (world.Config.Economy.Industry) { w.Value(n + "HaulFrom", v.HaulFrom); w.Value(n + "HaulTo", v.HaulTo); w.Value(n + "Held", v.Held); }
                if (world.Config.Economy.ProcessingChain) w.Value(n + "HaulNodeId", v.HaulNodeId);
                w.Value(n + "Route.Count", (uint)v.Route.Length);
                for (int j = 0; j < v.Route.Length; j++) w.Value(n + "Route[" + j.ToString(CultureInfo.InvariantCulture) + "]", v.Route[j]);
            }
            // V3-5: the class each soldier was trained as, only with ages.
            if (world.Config.Economy.Ages)
                for (int i = 0; i < world.SoldierCount; i++)
                    w.Value("Soldiers[" + world.Soldiers[i].Initial.Id.ToString(CultureInfo.InvariantCulture) + "].Class", (byte)world.Soldiers[i].Class);
            // A reserved civilisation, written only while one is held, so a match without one keeps its exact bytes.
            if (world.Config.Economy.Ages)
                for (int f = 0; f < world.Economies.Length; f++)
                    if (world.Economies[f].ReservedCiv != CivKind.Primitive)
                        w.Value("Economy[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].ReservedCiv", (byte)world.Economies[f].ReservedCiv);
            // V3-2: written only with industry, so a V3-1 economy keeps its exact canonical bytes.
            if (!world.Config.Economy.Industry) return;
            for (int f = 0; f < world.Economies.Length; f++)
            {
                var e = world.Economies[f]; string n = "Economy[" + (f + 1).ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "Ore", e.Ore); w.Value(n + "Metal", e.Metal); w.Value(n + "CoreHeld", e.CoreHeld); w.Value(n + "Policy", (byte)e.Policy);
                if (world.Config.Economy.ProcessingChain) { w.Value(n + "Charcoal", e.Charcoal); w.Value(n + "Steel", e.Steel); }
                if (world.Config.Economy.Forestry && e.Age >= 2) w.Value(n + "BowGear", e.BowGear);
                if (world.Config.Economy.Ages) { w.Value(n + "Stone", e.Stone); w.Value(n + "Gems", e.Gems); w.Value(n + "Techs", e.Techs); w.Value(n + "Age", e.Age); }
                if (world.Config.Economy.GoldEnabled) w.Value(n + "Gold", e.Gold);
                if (world.Config.Economy.Ages) { w.Value(n + "Civ", (byte)e.Civ); w.Value(n + "AdvancingTo", (byte)e.AdvancingTo); w.Value(n + "AdvanceRemaining", e.AdvanceRemaining); w.Value(n + "AgeVictoryProgress", e.AgeVictoryProgress); }
            }
            uint belts = 0;
            foreach (var b in world.Belts) if (b.FactionId != 0) belts++;
            w.Value("Belts.Count", belts);
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                var b = world.Belts[cell];
                if (b.FactionId == 0) continue;
                string n = "Belts[" + cell.ToString(CultureInfo.InvariantCulture) + "].";
                w.Value(n + "FactionId", b.FactionId); w.Value(n + "Facing", (byte)b.Facing); w.Value(n + "Hp", b.Hp);
                w.Value(n + "Item", (byte)b.Item); w.Value(n + "Progress", b.Progress); w.Value(n + "Held", b.Held);
            }
            if (world.Config.Economy.ProcessingChain || world.Config.Economy.Forestry && world.Economies[0].Age >= 2)
            {
                w.Value("ProcessingLines.NextId", world.NextProcessingLineId);
                w.Value("ProcessingLines.Count", (uint)world.ProcessingLines.Length);
                for (int i = 0; i < world.ProcessingLines.Length; i++)
                {
                    var line = world.ProcessingLines[i];
                    string n = "ProcessingLines[" + i.ToString(CultureInfo.InvariantCulture) + "].";
                    w.Value(n + "Id", line.Id); w.Value(n + "FactionId", line.FactionId); w.Value(n + "Kind", (byte)line.Kind);
                    w.Value(n + "Manager", (byte)line.Manager); w.Value(n + "MineId", line.MineId); w.Value(n + "SmelterId", line.SmelterId);
                    w.Value(n + "KilnId", line.KilnId); w.Value(n + "SteelworksId", line.SteelworksId);
                    w.Value(n + "BeltCount", (uint)line.BeltCells.Length);
                    for (int j = 0; j < line.BeltCells.Length; j++)
                    {
                        w.Value(n + "Belts[" + j.ToString(CultureInfo.InvariantCulture) + "].Cell", line.BeltCells[j]);
                        w.Value(n + "Belts[" + j.ToString(CultureInfo.InvariantCulture) + "].Facing", (byte)line.BeltFacings[j]);
                    }
                }
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
            public void Ids(string n,uint[] ids) => Ids(n,ids,ids.Length);
            public void Ids(string n,uint[] ids,int count) { Value(n+".Count",(uint)count); for(int i=0;i<count;i++)Value(n+"["+i.ToString(CultureInfo.InvariantCulture)+"]",ids[i]); }
            public void Bools(string n,bool[] values)
            {
                Value(n+".Count",(uint)values.Length);
                for(int wordIndex=0;wordIndex<(values.Length+63)/64;wordIndex++)
                {
                    ulong word=0;
                    int first=wordIndex*64;
                    int count=Math.Min(64,values.Length-first);
                    for(int bit=0;bit<count;bit++) if(values[first+bit]) word|=1UL<<bit;
                    Value(n+".Words["+wordIndex.ToString(CultureInfo.InvariantCulture)+"]",word);
                }
            }
        }
    }
}
