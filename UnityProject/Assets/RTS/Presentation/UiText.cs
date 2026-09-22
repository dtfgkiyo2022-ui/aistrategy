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
    }
}
