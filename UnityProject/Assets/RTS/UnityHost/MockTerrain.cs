using Rts.Presentation;
using Rts.Simulation;

namespace Rts.UnityHost
{
    /// <summary>Terrain of the week-2 two-route scenario, converted to the display-side type.</summary>
    public static class MockTerrain
    {
        public static TerrainMap Create()
        {
            var map = WeekTwoScenario.Create().Map;
            return new TerrainMap(map.WidthCells, map.HeightCells, map.CellSizeMeters, map.BlockedCellIds);
        }
    }
}
