namespace Rts.Presentation
{
    /// <summary>Per-match display mode state. It intentionally has no PlayerPrefs backing.</summary>
    public static class CraftDisplayMode
    {
        public static bool Toggle(bool current) { return !current; }
    }
}
