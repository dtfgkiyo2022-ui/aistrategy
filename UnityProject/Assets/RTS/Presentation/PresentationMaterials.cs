using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Temporary solid-color materials for placeholder visuals.</summary>
    public static class PresentationMaterials
    {
        private static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();

        private static readonly Dictionary<Color, Material> UnlitCache = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> NatureCache = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> GlowCache = new Dictionary<Color, Material>();

        public static Material Get(Color color) { return Build(color, false, Cache); }

        public static Material GetUnlit(Color color) { return Build(color, true, UnlitCache); }

        public static Material GetNature(Color color) { return BuildNature(color, NatureCache, false); }

        public static Material GetGlow(Color color) { return BuildNature(color, GlowCache, true); }

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

        private static Material BuildNature(Color color, Dictionary<Color, Material> cache, bool glow)
        {
            if (cache.TryGetValue(color, out var material) && material != null) return material;
            var shader = glow ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Rts/NatureWind");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            if (glow)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.8f);
            }
            cache[color] = material;
            return material;
        }
    }
}
