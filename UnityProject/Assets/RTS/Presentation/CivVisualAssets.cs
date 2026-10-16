using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Loads the small generated civilisation glyphs once and shares their materials.</summary>
    public static class CivVisualAssets
    {
        private const string ResourceRoot = "Icons/Civ/";
        private static readonly Dictionary<CivKind, Texture2D> textures = new Dictionary<CivKind, Texture2D>();
        private static readonly Dictionary<CivKind, Material> materials = new Dictionary<CivKind, Material>();

        public static Texture2D LoadGlyph(CivKind civ)
        {
            if (textures.TryGetValue(civ, out var cached)) return cached;
            string resourceName = ResourceRoot + CivPalette.GlyphFileName(civ).Replace(".png", string.Empty);
            var texture = Resources.Load<Texture2D>(resourceName);
            textures[civ] = texture;
            return texture;
        }

        public static Material GlyphMaterial(CivKind civ)
        {
            if (materials.TryGetValue(civ, out var cached) && cached != null) return cached;
            var texture = LoadGlyph(civ);
            if (texture == null) return null;
            var material = PresentationMaterials.NewUnlitTextured(texture);
            materials[civ] = material;
            return material;
        }
    }
}
