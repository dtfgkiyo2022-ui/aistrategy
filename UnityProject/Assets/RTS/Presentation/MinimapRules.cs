using System;

namespace Rts.Presentation
{
    /// <summary>Pure map-to-minimap and visibility rules. Coordinates use a bottom-left origin.</summary>
    public static class MinimapRules
    {
        public readonly struct Point { public readonly float X, Y; public Point(float x, float y) { X = x; Y = y; } }
        public static Point ToMinimap(float worldX, float worldZ, float mapWidth, float mapHeight, float left, float bottom, float width, float height)
        {
            float x = mapWidth <= 0f ? left : left + worldX / mapWidth * width;
            float y = mapHeight <= 0f ? bottom : bottom + worldZ / mapHeight * height;
            return new Point(x, y);
        }
        /// <summary>The inverse of ToMinimap: a point on the minimap (bottom-left origin) back to map metres.</summary>
        public static Point ToWorld(float minimapX, float minimapY, float mapWidth, float mapHeight, float left, float bottom, float width, float height)
        {
            float x = width <= 0f ? 0f : (minimapX - left) / width * mapWidth;
            float z = height <= 0f ? 0f : (minimapY - bottom) / height * mapHeight;
            return new Point(Math.Clamp(x, 0f, mapWidth), Math.Clamp(z, 0f, mapHeight));
        }
        public static bool CanDrawEnemy(bool isVisible) { return isVisible; }
        public static bool CanDrawRememberedEnemyBuilding(bool isExplored) { return isExplored; }
        public static bool IsInside(float x, float y, float left, float bottom, float width, float height)
        { return x >= left && x <= left + width && y >= bottom && y <= bottom + height; }
    }
}
