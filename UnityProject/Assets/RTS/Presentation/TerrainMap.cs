using System.Collections.Generic;

namespace Rts.Presentation
{
    /// <summary>Read-only terrain description for display: which cells are blocked. Cell index is z * WidthCells + x.</summary>
    public sealed class TerrainMap
    {
        public int WidthCells { get; }
        public int HeightCells { get; }
        public float CellSizeMeters { get; }
        private readonly bool[] blocked;

        public TerrainMap(int widthCells, int heightCells, float cellSizeMeters, IReadOnlyList<int> blockedCellIds)
        {
            WidthCells = widthCells;
            HeightCells = heightCells;
            CellSizeMeters = cellSizeMeters;
            blocked = new bool[widthCells * heightCells];
            foreach (int id in blockedCellIds)
                if (id >= 0 && id < blocked.Length) blocked[id] = true;
        }

        public bool IsBlocked(int x, int z) { return blocked[z * WidthCells + x]; }
    }
}
