namespace Rts.Presentation
{
    /// <summary>Presentation-only sound meanings. These values never enter the simulation contracts.</summary>
    public enum SoundCue
    {
        MeleeHit, ArrowRelease, ArrowHit, StructureHit, UnitDeath, RamHit,
        ConstructionStarted, ConstructionCompleted, VillagerProduced, UnitTrained,
        ResourceDrop, ProcessingOutput, BeltPlaced, BuildingDestroyed,
        AgeAdvancedOwn, AgeAdvancedEnemy, UnderAttackCore, UnderAttackBuilding, UnderAttackVillager,
        CivilizationChosen, ResearchCompleted, ResourceShortage,
        Click, CommandMove, CommandAttack, SelectionChanged, PlacementConfirmed, PlacementRejected
    }
}
