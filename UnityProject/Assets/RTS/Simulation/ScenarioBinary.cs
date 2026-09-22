using System;
using System.IO;
using System.Text;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>Explicit week-one scenario binary schema. Also canonicalizes authoring enumeration order.</summary>
    public static class ScenarioBinary
    {
        public const string RulesVersion = "week3-reinforcements-1";
        public static byte[] Encode(ScenarioDefinition source)
        {
            var c = new WorldState(source).Config;
            using (var s = new MemoryStream())
            using (var w = new BinaryWriter(s))
            {
                // Preserve the v1 bytes (and Config.Hash) for historical default rules.
                // Binary v2 adds tuning values; the authoring JSON remains schema 1.
                bool tuned = c.Rules.OccupationThreatMemoryTicks != 200 || c.Rules.DefaultReservePermille != 100;
                // Binary v3 adds the Ver.3 resources and economy rules. Written only when a scenario has them, so every
                // Ver.1 scenario keeps its v1/v2 bytes and therefore its Config.Hash.
                bool economy = c.ResourceNodes.Length > 0 || c.Economy.Enabled;
                // Binary v4 adds the V3-2 industry rules and the starting belts; a V3-1 economy keeps its v3 bytes.
                // Binary v5 adds the V3-4 terrain kinds; a map without terrain keeps its v4 bytes.
                int schema = c.Map.Terrain.Length != 0 ? 5 : c.Economy.Industry ? 4 : economy ? 3 : tuned ? 2 : 1;
                w.Write(schema); Text(w, c.ScenarioId); w.Write(c.Seed); w.Write(c.TickRateHz); w.Write(c.VerificationTickLimit);
                var m = c.Map;
                w.Write(m.WidthMeters); w.Write(m.HeightMeters); w.Write(m.CellSizeMeters); w.Write(m.WidthCells); w.Write(m.HeightCells); w.Write(m.DefaultPassable);
                w.Write((uint)m.BlockedCellIds.Length); foreach (var id in m.BlockedCellIds) w.Write(id);
                var r = c.Rules;
                w.Write(r.FactionCap); w.Write(r.CoreRadius.Raw); w.Write(r.OwnedObjectiveVision.Raw); w.Write(r.CaptureRadius.Raw);
                w.Write(r.CaptureDurationTicks); w.Write(r.CoreReinforcementIntervalTicks); w.Write(r.OutpostReinforcementIntervalTicks);
                if (schema >= 2) { w.Write(r.OccupationThreatMemoryTicks); w.Write(r.DefaultReservePermille); }
                w.Write((uint)c.UnitParameters.Length);
                foreach (var p in c.UnitParameters) { w.Write((byte)p.Kind); w.Write(p.Hp); w.Write(p.Speed.Raw); w.Write(p.Vision.Raw); w.Write(p.Range.Raw); w.Write(p.Damage); w.Write(p.AttackIntervalTicks); }
                w.Write((uint)c.Factions.Length); foreach (var f in c.Factions) { w.Write(f.Id); w.Write(f.CoreId); w.Write((uint)f.ArmyIds.Length); foreach (var id in f.ArmyIds) w.Write(id); }
                w.Write((uint)c.Armies.Length); foreach (var a in c.Armies) { w.Write(a.Id); w.Write(a.FactionId); Text(w,a.Role); w.Write(a.Capacity); Goal(w,a.HomeObjective); }
                w.Write((uint)c.Soldiers.Length); foreach (var p in c.Soldiers) { w.Write(p.Id); w.Write(p.FactionId); w.Write(p.ArmyId); w.Write((byte)p.Kind); w.Write(p.Alive); Point(w,p.Position); w.Write(p.Hp); }
                w.Write((uint)c.Cores.Length); foreach (var p in c.Cores) { w.Write(p.Id); w.Write(p.FactionId); Point(w,p.Position); w.Write(p.Hp); }
                w.Write((uint)c.Outposts.Length); foreach (var p in c.Outposts) { w.Write(p.Id); Point(w,p.Position); w.Write(p.OwnerFactionId); }
                if (schema >= 3)
                {
                    w.Write((uint)c.ResourceNodes.Length); foreach (var n in c.ResourceNodes) { w.Write(n.Id); w.Write((byte)n.Kind); Point(w,n.Position); w.Write(n.Amount); }
                    var e = c.Economy;
                    w.Write(e.Enabled);
                    if (e.Enabled)
                    {
                        w.Write(e.StartFood); w.Write(e.StartWood); w.Write(e.PopulationCap); w.Write(e.VillagerHp); w.Write(e.VillagerSpeed.Raw);
                        w.Write(e.CarryCapacity); w.Write(e.GatherIntervalTicks); w.Write(e.VillagerFoodCost); w.Write(e.VillagerTrainTicks);
                        w.Write(e.QueueLimit); w.Write(e.AutoVillagerTarget); w.Write(e.DropOffMargin.Raw);
                        w.Write(e.BarracksSizeCells); w.Write(e.BarracksWoodCost); w.Write(e.BarracksWork); w.Write(e.BarracksHp); w.Write(e.Builders);
                        w.Write(e.InfantryFoodCost); w.Write(e.InfantryWoodCost); w.Write(e.InfantryTrainTicks); w.Write(e.AutoInfantryQueue);
                        w.Write((uint)c.Villagers.Length); foreach (var v in c.Villagers) { w.Write(v.Id); w.Write(v.FactionId); Point(w,v.Position); }
                    }
                }
                if (schema >= 4)
                {
                    var e = c.Economy;
                    w.Write(e.BeltWoodCost); w.Write(e.BeltTicksPerCell); w.Write(e.BeltHp); w.Write(e.BeltLimit);
                    w.Write(e.MineSizeCells); w.Write(e.MineWoodCost); w.Write(e.MineWork); w.Write(e.MineHp); w.Write(e.MineIntervalTicks);
                    w.Write(e.SmelterSizeCells); w.Write(e.SmelterWoodCost); w.Write(e.SmelterWork); w.Write(e.SmelterHp); w.Write(e.SmeltTicks); w.Write(e.OrePerMetal);
                    w.Write(e.BufferLimit); w.Write(e.InfantryMetalCost);
                    w.Write((uint)c.Belts.Length); foreach (var b in c.Belts) { w.Write(b.Cell); w.Write(b.FactionId); w.Write((byte)b.Facing); w.Write((byte)b.Item); }
                }
                if (schema >= 5)
                {
                    w.Write((uint)m.Terrain.Length); w.Write(m.Terrain);
                    var e = c.Economy;
                    w.Write(e.Ages); w.Write(e.AdvanceFoodCost); w.Write(e.AdvanceWoodCost); w.Write(e.AdvanceTicks);
                    w.Write(e.AgrarianInfantryFood); w.Write(e.AgrarianInfantryWood); w.Write(e.AgrarianInfantryTicks); w.Write(e.ForgedInfantryHp); w.Write(e.ForgedInfantryDamage);
                    w.Write(e.FarmSizeCells); w.Write(e.FarmWoodCost); w.Write(e.FarmWork); w.Write(e.FarmHp);
                    w.Write(e.FarmBaseTicks); w.Write(e.FarmStepTicks); w.Write(e.FarmMinTicks); w.Write(e.FarmFoodReach); w.Write(e.FarmRiverReach);
                }
                return s.ToArray();
            }
        }
        public static ScenarioDefinition Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Scenario size.");
            using (var s = new MemoryStream(bytes, false))
            using (var r = new BinaryReader(s))
            {
                int schema = r.ReadInt32();
                if (schema < 1 || schema > 5) throw new InvalidDataException("Unknown scenario binary schema.");
                var c = new ScenarioDefinition { ScenarioId=Text(r), Seed=r.ReadUInt64(), TickRateHz=r.ReadInt32(), VerificationTickLimit=r.ReadInt64() };
                c.Map = new MapDefinition { WidthMeters=r.ReadInt32(), HeightMeters=r.ReadInt32(), CellSizeMeters=r.ReadInt32(), WidthCells=r.ReadInt32(), HeightCells=r.ReadInt32(), DefaultPassable=Bool(r), BlockedCellIds=new int[Count(r)] };
                for(int i=0;i<c.Map.BlockedCellIds.Length;i++) c.Map.BlockedCellIds[i]=r.ReadInt32();
                c.Rules = new RuleDefinition { FactionCap=r.ReadInt32(), CoreRadius=Fix(r), OwnedObjectiveVision=Fix(r), CaptureRadius=Fix(r), CaptureDurationTicks=r.ReadInt32(), CoreReinforcementIntervalTicks=r.ReadInt32(), OutpostReinforcementIntervalTicks=r.ReadInt32() };
                if (schema >= 2) { c.Rules.OccupationThreatMemoryTicks = r.ReadInt32(); c.Rules.DefaultReservePermille = r.ReadUInt16(); }
                c.UnitParameters=new UnitParameters[Count(r)]; for(int i=0;i<c.UnitParameters.Length;i++) c.UnitParameters[i]=new UnitParameters { Kind=(UnitKind)r.ReadByte(), Hp=r.ReadInt32(), Speed=Fix(r), Vision=Fix(r), Range=Fix(r), Damage=r.ReadInt32(), AttackIntervalTicks=r.ReadInt32() };
                c.Factions=new FactionDefinition[Count(r)]; for(int i=0;i<c.Factions.Length;i++) { var f=new FactionDefinition { Id=r.ReadUInt32(), CoreId=r.ReadUInt32(), ArmyIds=new uint[Count(r)] }; for(int j=0;j<f.ArmyIds.Length;j++) f.ArmyIds[j]=r.ReadUInt32(); c.Factions[i]=f; }
                c.Armies=new ArmyDefinition[Count(r)]; for(int i=0;i<c.Armies.Length;i++) c.Armies[i]=new ArmyDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), Role=Text(r), Capacity=r.ReadInt32(), HomeObjective=Goal(r) };
                c.Soldiers=new SoldierDefinition[Count(r)]; for(int i=0;i<c.Soldiers.Length;i++) c.Soldiers[i]=new SoldierDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), ArmyId=r.ReadUInt32(), Kind=(UnitKind)r.ReadByte(), Alive=Bool(r), Position=Point(r), Hp=r.ReadInt32() };
                c.Cores=new CoreDefinition[Count(r)]; for(int i=0;i<c.Cores.Length;i++) c.Cores[i]=new CoreDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), Position=Point(r), Hp=r.ReadInt32() };
                c.Outposts=new OutpostDefinition[Count(r)]; for(int i=0;i<c.Outposts.Length;i++) c.Outposts[i]=new OutpostDefinition { Id=r.ReadUInt32(), Position=Point(r), OwnerFactionId=r.ReadUInt32() };
                if (schema >= 3)
                {
                    c.ResourceNodes=new ResourceNodeDefinition[Count(r)]; for(int i=0;i<c.ResourceNodes.Length;i++) c.ResourceNodes[i]=new ResourceNodeDefinition { Id=r.ReadUInt32(), Kind=(ResourceKind)r.ReadByte(), Position=Point(r), Amount=r.ReadInt32() };
                    c.Economy=new EconomyRules { Enabled=Bool(r) };
                    if (c.Economy.Enabled)
                    {
                        var e=c.Economy;
                        e.StartFood=r.ReadInt32(); e.StartWood=r.ReadInt32(); e.PopulationCap=r.ReadInt32(); e.VillagerHp=r.ReadInt32(); e.VillagerSpeed=Fix(r);
                        e.CarryCapacity=r.ReadInt32(); e.GatherIntervalTicks=r.ReadInt32(); e.VillagerFoodCost=r.ReadInt32(); e.VillagerTrainTicks=r.ReadInt32();
                        e.QueueLimit=r.ReadInt32(); e.AutoVillagerTarget=r.ReadInt32(); e.DropOffMargin=Fix(r);
                        e.BarracksSizeCells=r.ReadInt32(); e.BarracksWoodCost=r.ReadInt32(); e.BarracksWork=r.ReadInt32(); e.BarracksHp=r.ReadInt32(); e.Builders=r.ReadInt32();
                        e.InfantryFoodCost=r.ReadInt32(); e.InfantryWoodCost=r.ReadInt32(); e.InfantryTrainTicks=r.ReadInt32(); e.AutoInfantryQueue=r.ReadInt32();
                        c.Villagers=new VillagerDefinition[Count(r)]; for(int i=0;i<c.Villagers.Length;i++) c.Villagers[i]=new VillagerDefinition { Id=r.ReadUInt32(), FactionId=r.ReadUInt32(), Position=Point(r) };
                    }
                }
                if (schema >= 4)
                {
                    // v4 is written only for industry, which needs the economy the v3 block just read.
                    if (!c.Economy.Enabled) throw new InvalidDataException("Industry without an economy.");
                    var e=c.Economy;
                    e.Industry=true; e.BeltWoodCost=r.ReadInt32(); e.BeltTicksPerCell=r.ReadInt32(); e.BeltHp=r.ReadInt32(); e.BeltLimit=r.ReadInt32();
                    e.MineSizeCells=r.ReadInt32(); e.MineWoodCost=r.ReadInt32(); e.MineWork=r.ReadInt32(); e.MineHp=r.ReadInt32(); e.MineIntervalTicks=r.ReadInt32();
                    e.SmelterSizeCells=r.ReadInt32(); e.SmelterWoodCost=r.ReadInt32(); e.SmelterWork=r.ReadInt32(); e.SmelterHp=r.ReadInt32(); e.SmeltTicks=r.ReadInt32(); e.OrePerMetal=r.ReadInt32();
                    e.BufferLimit=r.ReadInt32(); e.InfantryMetalCost=r.ReadInt32();
                    c.Belts=new BeltDefinition[Count(r)]; for(int i=0;i<c.Belts.Length;i++) c.Belts[i]=new BeltDefinition { Cell=r.ReadInt32(), FactionId=r.ReadUInt32(), Facing=(Facing)r.ReadByte(), Item=(ResourceKind)r.ReadByte() };
                }
                if (schema >= 5)
                {
                    int n=Count(r); var terrain=r.ReadBytes(n); if(terrain.Length!=n) throw new EndOfStreamException();
                    c.Map.Terrain=terrain;
                    var e=c.Economy;
                    e.Ages=Bool(r); e.AdvanceFoodCost=r.ReadInt32(); e.AdvanceWoodCost=r.ReadInt32(); e.AdvanceTicks=r.ReadInt32();
                    e.AgrarianInfantryFood=r.ReadInt32(); e.AgrarianInfantryWood=r.ReadInt32(); e.AgrarianInfantryTicks=r.ReadInt32(); e.ForgedInfantryHp=r.ReadInt32(); e.ForgedInfantryDamage=r.ReadInt32();
                    e.FarmSizeCells=r.ReadInt32(); e.FarmWoodCost=r.ReadInt32(); e.FarmWork=r.ReadInt32(); e.FarmHp=r.ReadInt32();
                    e.FarmBaseTicks=r.ReadInt32(); e.FarmStepTicks=r.ReadInt32(); e.FarmMinTicks=r.ReadInt32(); e.FarmFoodReach=r.ReadInt32(); e.FarmRiverReach=r.ReadInt32();
                }
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
