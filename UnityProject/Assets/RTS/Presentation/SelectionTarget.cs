namespace Rts.Presentation
{
    public enum SelectionKind : byte { None = 0, Army = 1, Core = 2, Outpost = 3 }

    /// <summary>A selection by frame ID, never by display position.</summary>
    public readonly struct SelectionTarget
    {
        public SelectionKind Kind { get; }
        public uint Id { get; }
        public SelectionTarget(SelectionKind kind, uint id) { Kind = kind; Id = id; }
        public static SelectionTarget None => new SelectionTarget(SelectionKind.None, 0);
    }
}
