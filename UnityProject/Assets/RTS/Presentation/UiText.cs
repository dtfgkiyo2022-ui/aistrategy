namespace Rts.Presentation
{
    /// <summary>
    /// The on-screen language, English or Japanese, until real localisation (string tables) comes later. Every panel asks
    /// for a line in both languages at the place it draws it, so switching takes effect on the next frame. Plain C#: the
    /// host loads and saves the choice.
    /// </summary>
    public static class UiText
    {
        public static bool Japanese;

        public static string T(string english, string japanese) { return Japanese ? japanese : english; }

        public static string DropSiteLabel(int count, int max, int woodCost)
        {
            string countText = max > 0 ? count + "/" + max : "";
            return Japanese ? "集積所 " + countText + "（木" + woodCost + "）"
                : "Drop-off " + countText + " (" + woodCost + " wood)";
        }

        public static string StorageDescription()
            => T("Villagers can unload here; the exit belt sends it to the core",
                "村人が荷を下ろせる（出口からベルトで送る）");
    }
}
