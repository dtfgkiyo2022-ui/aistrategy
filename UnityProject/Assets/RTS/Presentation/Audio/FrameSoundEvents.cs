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
        // The simulation currently publishes array-backed views, but their traversal order is a
        // presentation detail. These tables are retained and cleared between calls so comparison
        // remains correct if a view is assembled in a different order without per-frame allocations.
        private static readonly Dictionary<uint, RenderUnit> beforeUnits = new Dictionary<uint, RenderUnit>(512);
        private static readonly Dictionary<uint, RenderUnit> afterUnits = new Dictionary<uint, RenderUnit>(512);
        private static readonly Dictionary<uint, KnownObjective> beforeObjectives = new Dictionary<uint, KnownObjective>(64);
        private static readonly Dictionary<uint, KnownObjective> afterObjectives = new Dictionary<uint, KnownObjective>(64);
        private static readonly Dictionary<uint, BuildingView> beforeBuildings = new Dictionary<uint, BuildingView>(128);
        private static readonly Dictionary<uint, BuildingView> afterBuildings = new Dictionary<uint, BuildingView>(128);
        private static readonly Dictionary<uint, VillagerView> beforeVillagers = new Dictionary<uint, VillagerView>(128);
        private static readonly Dictionary<uint, VillagerView> afterVillagers = new Dictionary<uint, VillagerView>(128);

        public static void Compare(FactionFrame previous, FactionFrame current, uint ownFactionId, List<FrameSoundEvent> output)
        {
            output.Clear();
            if (current == null) return;
            if (previous == null) return;
            Index(previous.Units, current.Units, beforeUnits, afterUnits);
            CompareUnits(current.Units, output);
            CompareObjectives(previous.Objectives, current.Objectives, ownFactionId, output);
            if (previous.Economy != null && current.Economy != null)
            {
                if (current.Economy.Age > previous.Economy.Age) output.Add(new FrameSoundEvent(
                    current.FactionId == ownFactionId ? SoundCue.AgeAdvancedOwn : SoundCue.AgeAdvancedEnemy, default(SimPoint)));
                CompareBuildings(previous.Economy.Buildings, current.Economy.Buildings, ownFactionId, output);
                CompareVillagers(previous.Economy.Villagers, current.Economy.Villagers, ownFactionId, output);
                if (current.Economy.Civ != previous.Economy.Civ && current.Economy.Civ != CivKind.Primitive)
                    output.Add(new FrameSoundEvent(SoundCue.CivilizationChosen, default(SimPoint)));
                ulong researched = current.Economy.Techs & ~previous.Economy.Techs;
                if (researched != 0) output.Add(new FrameSoundEvent(SoundCue.ResearchCompleted, default(SimPoint)));
                if (current.Economy.VillagerQueued < previous.Economy.VillagerQueued && current.Economy.Population > previous.Economy.Population)
                    output.Add(new FrameSoundEvent(SoundCue.VillagerProduced, default(SimPoint)));
            }
        }

        private static void CompareObjectives(IReadOnlyList<KnownObjective> before, IReadOnlyList<KnownObjective> after, uint ownFactionId, List<FrameSoundEvent> output)
        {
            beforeObjectives.Clear(); afterObjectives.Clear();
            if (before == null || after == null) return;
            for (int i = 0; i < before.Count; i++) beforeObjectives[before[i].Id] = before[i];
            for (int i = 0; i < after.Count; i++) afterObjectives[after[i].Id] = after[i];
            foreach (var pair in afterObjectives)
            {
                KnownObjective old;
                var item = pair.Value;
                if (beforeObjectives.TryGetValue(item.Id, out old) && old.IsHpKnown && item.IsHpKnown && item.Hp < old.Hp && item.OwnerFactionId == ownFactionId)
                    output.Add(new FrameSoundEvent(item.Kind == GoalKind.Core ? SoundCue.UnderAttackCore : SoundCue.UnderAttackBuilding, item.Position));
            }
        }

        private static void CompareUnits(IReadOnlyList<RenderUnit> after, List<FrameSoundEvent> output)
        {
            foreach (var pair in beforeUnits)
            {
                RenderUnit current;
                if (!afterUnits.TryGetValue(pair.Key, out current))
                    output.Add(new FrameSoundEvent(SoundCue.UnitDeath, pair.Value.Position));
            }
            foreach (var pair in afterUnits)
            {
                RenderUnit old;
                if (!beforeUnits.TryGetValue(pair.Key, out old) || !old.HasHp || !pair.Value.HasHp || pair.Value.Hp >= old.Hp) continue;
                output.Add(new FrameSoundEvent(HitCue(after), pair.Value.Position));
            }
            foreach (var pair in afterUnits)
            {
                RenderUnit old;
                if (!beforeUnits.TryGetValue(pair.Key, out old) || old.IsAttacking || !pair.Value.IsAttacking) continue;
                if (pair.Value.Kind == UnitKind.Archer || pair.Value.Kind == UnitKind.SkirmishArcher)
                    output.Add(new FrameSoundEvent(SoundCue.ArrowRelease, pair.Value.Position));
            }
        }

        private static void CompareBuildings(IReadOnlyList<BuildingView> before, IReadOnlyList<BuildingView> after, uint ownFactionId, List<FrameSoundEvent> output)
        {
            beforeBuildings.Clear(); afterBuildings.Clear();
            if (before == null || after == null) return;
            for (int i = 0; i < before.Count; i++) beforeBuildings[before[i].Id] = before[i];
            for (int i = 0; i < after.Count; i++) afterBuildings[after[i].Id] = after[i];
            foreach (var pair in afterBuildings)
                if (!beforeBuildings.ContainsKey(pair.Key) && pair.Value.FactionId == ownFactionId)
                    output.Add(new FrameSoundEvent(pair.Value.Complete ? SoundCue.ConstructionCompleted : SoundCue.ConstructionStarted, pair.Value.Center));
            foreach (var pair in beforeBuildings)
                if (!afterBuildings.ContainsKey(pair.Key) && pair.Value.FactionId == ownFactionId)
                    output.Add(new FrameSoundEvent(SoundCue.BuildingDestroyed, pair.Value.Center));
            foreach (var pair in afterBuildings)
            {
                BuildingView old;
                var item = pair.Value;
                if (!beforeBuildings.TryGetValue(item.Id, out old)) continue;
                if (!old.Complete && item.Complete) output.Add(new FrameSoundEvent(SoundCue.ConstructionCompleted, item.Center));
                if (old.Hp > item.Hp && item.FactionId == ownFactionId) output.Add(new FrameSoundEvent(SoundCue.UnderAttackBuilding, item.Center));
                if (old.Queued > item.Queued) output.Add(new FrameSoundEvent(SoundCue.UnitTrained, item.Center));
                if (old.Output < item.Output) output.Add(new FrameSoundEvent(SoundCue.ProcessingOutput, item.Center));
            }
        }

        private static void CompareVillagers(IReadOnlyList<VillagerView> before, IReadOnlyList<VillagerView> after, uint ownFactionId, List<FrameSoundEvent> output)
        {
            beforeVillagers.Clear(); afterVillagers.Clear();
            if (before == null || after == null) return;
            for (int i = 0; i < before.Count; i++) if (before[i].IsOwn && before[i].Id != 0) beforeVillagers[before[i].Id] = before[i];
            for (int i = 0; i < after.Count; i++) if (after[i].IsOwn && after[i].Id != 0) afterVillagers[after[i].Id] = after[i];
            foreach (var pair in afterVillagers)
            {
                VillagerView old;
                var item = pair.Value;
                if (!beforeVillagers.TryGetValue(pair.Key, out old)) continue;
                if (item.Hp < old.Hp) output.Add(new FrameSoundEvent(SoundCue.UnderAttackVillager, item.Position));
                if (old.Carry > 0 && item.Carry == 0) output.Add(new FrameSoundEvent(SoundCue.ResourceDrop, item.Position));
            }
        }

        private static SoundCue HitCue(IReadOnlyList<RenderUnit> current)
        {
            for (int i = 0; i < current.Count; i++) if (current[i].IsAttacking)
            {
                switch (current[i].Kind)
                {
                    case UnitKind.Archer: case UnitKind.SkirmishArcher: return SoundCue.ArrowHit;
                    case UnitKind.Ram: return SoundCue.RamHit;
                    case UnitKind.Cavalry: case UnitKind.LightCavalry: case UnitKind.HeavyCavalry: return SoundCue.StructureHit;
                }
            }
            return SoundCue.MeleeHit;
        }

        private static void Index(IReadOnlyList<RenderUnit> before, IReadOnlyList<RenderUnit> after,
            Dictionary<uint, RenderUnit> beforeIndex, Dictionary<uint, RenderUnit> afterIndex)
        {
            beforeIndex.Clear(); afterIndex.Clear();
            if (before == null || after == null) return;
            for (int i = 0; i < before.Count; i++) beforeIndex[before[i].Id] = before[i];
            for (int i = 0; i < after.Count; i++) afterIndex[after[i].Id] = after[i];
        }
    }
}
