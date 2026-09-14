using System.IO;
using System.Text;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        /// <summary>
        /// Temporary week-one diagnostic schema 1, not the Issue #9 replay format/hash.
        /// Little-endian primitives, byte enums/bools, uint counts, UTF-8 strings.
        /// Includes immutable config, tombstones, cooldowns, policies and contact allocation.
        /// Scratch buffers and derived frames/traversals are deliberately excluded.
        /// </summary>
        public DiagnosticState CaptureDiagnostic()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(1U);
                WriteConfig(writer);
                writer.Write(world.Tick);
                writer.Write(world.Result.HasEnded); writer.Write(world.Result.WinnerFactionId);
                writer.Write(world.Result.IsDraw); writer.Write(world.Result.IsFault); writer.Write(world.Result.IsUndecided);
                writer.Write(world.NextSoldierId); writer.Write(world.NextArmyId); writer.Write(world.NextCoreId);
                writer.Write(world.NextOutpostId); writer.Write(world.NextFactionId); writer.Write(world.InputCursor);
                writer.Write(world.CombatRandom.State); writer.Write(world.CombatRandom.CallCount);
                writer.Write(world.AiRandom.State); writer.Write(world.AiRandom.CallCount);
                writer.Write((uint)world.Soldiers.Length);
                foreach (int i in world.SoldierTraversal)
                {
                    var s = world.Soldiers[i];
                    writer.Write(s.Initial.Id); writer.Write(s.Initial.FactionId); writer.Write(s.Initial.ArmyId);
                    writer.Write((byte)s.Initial.Kind); writer.Write(s.Alive); WritePoint(writer, s.Position);
                    writer.Write(s.Hp); writer.Write(s.TargetKind); writer.Write(s.TargetId); writer.Write(s.NextAttackTick);
                    WritePoint(writer, s.MoveGoal); writer.Write(s.StepDistance.Raw);
                    writer.Write(s.IsMoving); writer.Write(s.IsAttacking); writer.Write(s.IsRetreating);
                }
                writer.Write((uint)world.Armies.Length);
                foreach (int i in world.ArmyTraversal)
                {
                    var a = world.Armies[i];
                    writer.Write(a.Definition.Id); WriteIds(writer, a.SoldierIds);
                    writer.Write((byte)a.Policy); WriteGoal(writer, a.Goal); writer.Write(a.CommandId);
                    writer.Write(a.AcceptedTick); writer.Write(a.ApplyTick);
                }
                writer.Write((uint)world.Cores.Length);
                foreach (var f in world.Factions)
                {
                    var c = world.Cores[f.CoreId - 1];
                    writer.Write(c.Definition.Id); writer.Write(c.Hp);
                }
                writer.Write((uint)world.Factions.Length);
                foreach (var f in world.Factions)
                {
                    writer.Write(f.Id); writer.Write(f.CoreId); writer.Write(f.AliveCount); WriteIds(writer, f.ArmyIds);
                    writer.Write(f.NextContactId);
                    writer.Write((uint)world.Soldiers.Length);
                    foreach (int i in world.SoldierTraversal)
                    { writer.Write(world.Soldiers[i].Initial.Id); writer.Write(f.ContactIds[i]); }
                }
                writer.Flush();
                return new DiagnosticState(world.Tick, stream.ToArray());
            }
        }

        private void WriteConfig(BinaryWriter w)
        {
            var c = world.Config;
            w.Write(c.SchemaVersion); WriteString(w, c.ScenarioId); w.Write(c.Seed);
            w.Write(c.TickRateHz); w.Write(c.VerificationTickLimit);
            var m = c.Map;
            w.Write(m.WidthMeters); w.Write(m.HeightMeters); w.Write(m.CellSizeMeters);
            w.Write(m.WidthCells); w.Write(m.HeightCells); w.Write(m.DefaultPassable);
            w.Write((uint)m.BlockedCellIds.Length);
            foreach (int id in m.BlockedCellIds) w.Write(id);
            var r = c.Rules;
            w.Write(r.FactionCap); w.Write(r.CoreRadius.Raw); w.Write(r.OwnedObjectiveVision.Raw); w.Write(r.CaptureRadius.Raw);
            w.Write(r.CaptureDurationTicks); w.Write(r.CoreReinforcementIntervalTicks); w.Write(r.OutpostReinforcementIntervalTicks);
            w.Write((uint)c.UnitParameters.Length);
            foreach (var p in c.UnitParameters)
            {
                w.Write((byte)p.Kind); w.Write(p.Hp); w.Write(p.Speed.Raw); w.Write(p.Vision.Raw);
                w.Write(p.Range.Raw); w.Write(p.Damage); w.Write(p.AttackIntervalTicks);
            }
            w.Write((uint)c.Factions.Length);
            foreach (var f in c.Factions) { w.Write(f.Id); w.Write(f.CoreId); WriteIds(w, f.ArmyIds); }
            w.Write((uint)c.Armies.Length);
            foreach (int i in world.ArmyTraversal)
            {
                var a = c.Armies[i];
                w.Write(a.Id); w.Write(a.FactionId); WriteString(w, a.Role); w.Write(a.Capacity); WriteGoal(w, a.HomeObjective);
            }
            w.Write((uint)c.Soldiers.Length);
            foreach (int i in world.SoldierTraversal)
            {
                var s = c.Soldiers[i];
                w.Write(s.Id); w.Write(s.FactionId); w.Write(s.ArmyId); w.Write((byte)s.Kind);
                w.Write(s.Alive); WritePoint(w, s.Position); w.Write(s.Hp);
            }
            w.Write((uint)c.Cores.Length);
            foreach (var f in c.Factions)
            {
                var core = c.Cores[f.CoreId - 1];
                w.Write(core.Id); w.Write(core.FactionId); WritePoint(w, core.Position); w.Write(core.Hp);
            }
            w.Write((uint)c.Outposts.Length);
            foreach (var o in c.Outposts) { w.Write(o.Id); WritePoint(w, o.Position); w.Write(o.OwnerFactionId); }
        }

        private static void WritePoint(BinaryWriter w, SimPoint p) { w.Write(p.X.Raw); w.Write(p.Z.Raw); }
        private static void WriteGoal(BinaryWriter w, PolicyGoal g) { w.Write((byte)g.Kind); w.Write(g.Id); WritePoint(w, g.Point); }
        private static void WriteIds(BinaryWriter w, uint[] ids)
        { w.Write((uint)ids.Length); foreach (uint id in ids) w.Write(id); }
        private static void WriteString(BinaryWriter w, string value)
        { byte[] bytes = Encoding.UTF8.GetBytes(value); w.Write((uint)bytes.Length); w.Write(bytes); }
    }
}
