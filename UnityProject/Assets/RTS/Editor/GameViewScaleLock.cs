using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Rts.Editor
{
    /// <summary>
    /// The mouse wheel zooms the camera, and the editor's Game view zooms with the same wheel, so a test play ended up at
    /// "Scale 1.1x" with the HUD cut off at the edges (10-10). While playing, this puts the Game view back to the scale
    /// that fits the view. Editor only; it reads Unity's internal GameView by name and quietly does nothing if those names
    /// change in another Unity version.
    /// </summary>
    [InitializeOnLoad]
    internal static class GameViewScaleLock
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type GameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        private static readonly FieldInfo ZoomArea = GameViewType?.GetField("m_ZoomArea", Flags);
        private static readonly PropertyInfo MinScale = GameViewType?.GetProperty("minScale", Flags);
        private static readonly MethodInfo SnapZoom = GameViewType?.GetMethod("SnapZoom", Flags, null, new[] { typeof(float) }, null);
        private static double nextCheck;
        private static bool broken;

        static GameViewScaleLock()
        {
            if (GameViewType == null || ZoomArea == null || MinScale == null || SnapZoom == null) return;
            EditorApplication.update += Keep;
        }

        private static void Keep()
        {
            if (broken || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 0.25;
            try
            {
                foreach (var window in Resources.FindObjectsOfTypeAll(GameViewType))
                {
                    var area = ZoomArea.GetValue(window);
                    var scaleProperty = area?.GetType().GetProperty("scale", Flags);
                    if (scaleProperty == null) continue;
                    float fit = (float)MinScale.GetValue(window);
                    var scale = (Vector2)scaleProperty.GetValue(area);
                    if (Mathf.Abs(scale.y - fit) > 0.001f) SnapZoom.Invoke(window, new object[] { fit });
                }
            }
            catch (Exception e)
            {
                broken = true;
                Debug.LogWarning("Game view scale lock stopped: " + e.Message);
            }
        }
    }
}
