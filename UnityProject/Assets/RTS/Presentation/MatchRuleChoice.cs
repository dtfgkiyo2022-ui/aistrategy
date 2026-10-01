namespace Rts.Presentation
{
    public interface IMatchRuleChoice
    {
        bool Monks { get; set; }
        bool AgeVictory { get; set; }
        /// <summary>Opens the five later civilisations (forestry, masonry, caravan, cavalry, bridge) beside the first two.</summary>
        bool AllCivilisations { get; set; }
    }
}
