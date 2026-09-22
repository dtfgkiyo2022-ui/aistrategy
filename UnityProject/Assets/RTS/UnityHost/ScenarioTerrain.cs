using Rts.Presentation;
using Rts.Simulation;

namespace Rts.UnityHost
{
    /// <summary>Converts a scenario map definition into the display-side terrain type.</summary>
    public static class ScenarioTerrain
    {
        public static TerrainMap From(MapDefinition map)
        {
            return new TerrainMap(map.WidthCells, map.HeightCells, map.CellSizeMeters, map.BlockedCellIds, map.Terrain);
        }
    }
}
