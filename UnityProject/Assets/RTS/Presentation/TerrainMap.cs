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
        private readonly byte[] kinds;

        public TerrainMap(int widthCells, int heightCells, float cellSizeMeters, IReadOnlyList<int> blockedCellIds)
            : this(widthCells, heightCells, cellSizeMeters, blockedCellIds, null)
        {
        }

        /// <summary>V3-4: <paramref name="terrainKinds"/> is one kind per cell (0 plain, 1 forest, 2 river, 3 mountain), or null.</summary>
        public TerrainMap(int widthCells, int heightCells, float cellSizeMeters, IReadOnlyList<int> blockedCellIds, IReadOnlyList<byte> terrainKinds)
        {
            kinds = new byte[widthCells * heightCells];
            if (terrainKinds != null && terrainKinds.Count == kinds.Length)
                for (int i = 0; i < kinds.Length; i++) kinds[i] = terrainKinds[i];
            WidthCells = widthCells;
            HeightCells = heightCells;
            CellSizeMeters = cellSizeMeters;
            blocked = new bool[widthCells * heightCells];
            foreach (int id in blockedCellIds)
                if (id >= 0 && id < blocked.Length) blocked[id] = true;
        }

        public bool IsBlocked(int x, int z) { return blocked[z * WidthCells + x]; }

        /// <summary>0 plain, 1 forest, 2 river, 3 mountain. A blocked cell of kind 0 is an obstacle of the older maps.</summary>
        public byte KindAt(int x, int z) { return kinds[z * WidthCells + x]; }
    }
}
