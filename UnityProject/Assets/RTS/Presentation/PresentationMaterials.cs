using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Temporary solid-color materials for placeholder visuals.</summary>
    public static class PresentationMaterials
    {
        private static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();

        private static readonly Dictionary<Color, Material> UnlitCache = new Dictionary<Color, Material>();

        public static Material Get(Color color) { return Build(color, false, Cache); }

        public static Material GetUnlit(Color color) { return Build(color, true, UnlitCache); }

        public static Material NewUnlitTextured(Texture2D texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader);
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            return material;
        }

        private static Material Build(Color color, bool unlit, Dictionary<Color, Material> cache)
        {
            if (cache.TryGetValue(color, out var material) && material != null) return material;
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            material = new Material(shader) { color = color };
            material.SetColor("_BaseColor", color);
            cache[color] = material;
            return material;
        }
    }
}
