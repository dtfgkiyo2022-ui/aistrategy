namespace Rts.Presentation
{
    /// <summary>
    /// What the opponent picker may do: see the doctrine names and the one currently running, and switch to another.
    /// Switching restarts the match from tick 0, the same way changing the reply delay or the outside AI does.
    /// </summary>
    public interface IOpponentControl
    {
        /// <summary>Doctrine names in display order (see <c>PolicyPresets.Names</c>).</summary>
        string[] Choices { get; }

        string Current { get; set; }
    }
}
