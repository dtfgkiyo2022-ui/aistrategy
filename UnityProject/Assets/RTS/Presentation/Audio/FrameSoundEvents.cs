using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
    public readonly struct FrameSoundEvent
    {
        public readonly SoundCue Cue;
        public readonly SimPoint Position;
        public FrameSoundEvent(SoundCue cue, SimPoint position) { Cue = cue; Position = position; }
    }

    /// <summary>Allocation-free when the caller reuses output. Inputs are contract snapshots, not simulation state.</summary>
    public static class FrameSoundEvents
    {
        public static void Compare(FactionFrame previous, FactionFrame current, uint ownFactionId, List<FrameSoundEvent> output)
        {
            output.Clear();
            if (current == null) return;
            if (previous == null) return;
            CompareUnits(previous.Units, current.Units, output);
            CompareObjectives(previous.Objectives, current.Objectives, ownFactionId, output);
            if (previous.Economy != null && current.Economy != null)
            {
                if (current.Economy.Age > previous.Economy.Age) output.Add(new FrameSoundEvent(
                    current.FactionId == ownFactionId ? SoundCue.AgeAdvancedOwn : SoundCue.AgeAdvancedEnemy, default(SimPoint)));
                CompareBuildings(previous.Economy.Buildings, current.Economy.Buildings, ownFactionId, output);
                if (current.Economy.VillagerQueued < previous.Economy.VillagerQueued && current.Economy.Population > previous.Economy.Population)
                    output.Add(new FrameSoundEvent(SoundCue.VillagerProduced, default(SimPoint)));
            }
        }

        private static void CompareObjectives(IReadOnlyList<KnownObjective> before, IReadOnlyList<KnownObjective> after, uint ownFactionId, List<FrameSoundEvent> output)
        {
            int i = 0, j = 0;
            while (i < before.Count || j < after.Count)
            {
                if (j >= after.Count || (i < before.Count && before[i].Id < after[j].Id)) { i++; continue; }
                if (i >= before.Count || after[j].Id < before[i].Id) { j++; continue; }
                if (before[i].IsHpKnown && after[j].IsHpKnown && after[j].Hp < before[i].Hp && after[j].OwnerFactionId == ownFactionId)
                    output.Add(new FrameSoundEvent(after[j].Kind == GoalKind.Core ? SoundCue.UnderAttackCore : SoundCue.UnderAttackBuilding, after[j].Position));
                i++; j++;
            }
        }

        private static void CompareUnits(IReadOnlyList<RenderUnit> before, IReadOnlyList<RenderUnit> after, List<FrameSoundEvent> output)
        {
            int i = 0, j = 0;
            while (i < before.Count || j < after.Count)
            {
                if (j >= after.Count || (i < before.Count && before[i].Id < after[j].Id))
                { if (before[i].IsOwn) output.Add(new FrameSoundEvent(SoundCue.UnitDeath, before[i].Position)); i++; continue; }
                if (i >= before.Count || after[j].Id < before[i].Id) { j++; continue; }
                if (before[i].HasHp && after[j].HasHp && after[j].Hp < before[i].Hp)
                    output.Add(new FrameSoundEvent(SoundCue.MeleeHit, after[j].Position));
                i++; j++;
            }
        }

        private static void CompareBuildings(IReadOnlyList<BuildingView> before, IReadOnlyList<BuildingView> after, uint ownFactionId, List<FrameSoundEvent> output)
        {
            int i = 0, j = 0;
            while (i < before.Count || j < after.Count)
            {
                if (j >= after.Count || (i < before.Count && before[i].Id < after[j].Id)) { i++; continue; }
                if (i >= before.Count || after[j].Id < before[i].Id) { j++; continue; }
                if (!before[i].Complete && after[j].Complete) output.Add(new FrameSoundEvent(SoundCue.ConstructionCompleted, after[j].Center));
                if (before[i].Hp > after[j].Hp && after[j].FactionId == ownFactionId)
                    output.Add(new FrameSoundEvent(SoundCue.UnderAttackBuilding, after[j].Center));
                if (before[i].Queued > after[j].Queued) output.Add(new FrameSoundEvent(SoundCue.UnitTrained, after[j].Center));
                i++; j++;
            }
        }
    }
}
