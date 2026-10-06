namespace Rts.Presentation
{
    /// <summary>Actions in the match setup that are performed by the Unity host.</summary>
    public interface IPlayerFilesControl
    {
        void OpenTacticsFolder();
        void OpenPacksFolder();
        void RefreshTacticList();
    }
}
