using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Four shared belt materials; offset is animated once per frame, never once per belt.</summary>
    public static class BeltVisualMaterials
    {
        private static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>();
        private static Texture2D texture;

        public static Material Get(BeltSpeed speed, bool stalled)
        {
            int key = ((int)speed * 2) + (stalled ? 1 : 0);
            if (Cache.TryGetValue(key, out var material))
            {
                if (material != null) return material;
            }
            if (texture == null) texture = Resources.Load<Texture2D>("Textures/BeltChevron");
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            material = new Material(shader);
            material.mainTexture = texture;
            material.SetTexture("_BaseMap", texture);
            material.color = stalled ? new Color(1f, 0.22f, 0.16f) : Color.white;
            material.SetColor("_BaseColor", material.color);
            Cache[key] = material;
            return material;
        }

        public static void Tick(float time)
        {
            float normal = Mathf.Repeat(time * 0.18f, 1f);
            float fast = Mathf.Repeat(time * 0.34f, 1f);
            foreach (var material in Cache)
            {
                bool isFast = material.Key / 2 == (int)BeltSpeed.Fast;
                // The chevrons point along +v (the belt's facing); a falling v offset carries them forward.
                material.Value.mainTextureOffset = new Vector2(0f, 1f - (isFast ? fast : normal));
            }
        }
    }
}
