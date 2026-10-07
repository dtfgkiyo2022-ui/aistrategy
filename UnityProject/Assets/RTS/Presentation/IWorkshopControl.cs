namespace Rts.Presentation
{
    /// <summary>Workshop actions exposed to Presentation without a Steamworks dependency.</summary>
    public interface IWorkshopControl
    {
        bool SteamAvailable { get; }
        string WorkshopStatus { get; }
        string[] VisibilityChoices { get; }
        string Visibility { get; set; }
        bool CanPublishTactic(string folderPath);
        void PublishTactic(string folderPath);
        void RefreshWorkshopTactics();
    }
}
