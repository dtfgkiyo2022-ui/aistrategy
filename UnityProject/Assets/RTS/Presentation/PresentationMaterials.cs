using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Temporary solid-color materials for placeholder visuals.</summary>
    public static class PresentationMaterials
    {
        private static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();

        public static Material Get(Color color)
        {
            if (Cache.TryGetValue(color, out var material) && material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            material = new Material(shader) { color = color };
            material.SetColor("_BaseColor", color);
            Cache[color] = material;
            return material;
        }
    }
}
