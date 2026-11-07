using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Which tier of the made-for-this-game models to draw (docs/settings.md 2.1). 0 = Low = the original files in
    /// BuildingModels/ and ResourceModels/, untouched. 1..3 = Medium/High/Ultra from the *HD folders, named
    /// &lt;name&gt;_M / _H / _U (+ _LOD1), textures &lt;name&gt;_BaseColor2k / _Normal2k (+ _BaseColor4k for Ultra buildings).
    /// A kind without files for the chosen tier uses the next lower tier, and finally Low. Display only.
    /// </summary>
    public static class ArtDetail
    {
        private static readonly string[] Suffixes = { "", "_M", "_H", "_U" };

        public static int Level { get; private set; }

        /// <summary>Changes whenever Level changes, so the model caches know to drop what they loaded.</summary>
        public static int Version { get; private set; }

        /// <summary>Returns true when the tier really changed.</summary>
        public static bool Set(int level)
        {
            level = Mathf.Clamp(level, 0, Suffixes.Length - 1);
            if (level == Level) return false;
            Level = level;
            Version++;
            return true;
        }

        public static string Suffix(int tier) { return Suffixes[Mathf.Clamp(tier, 0, Suffixes.Length - 1)]; }

        /// <summary>The highest tier at or below Level whose near model exists (0 when none of the HD tiers does).</summary>
        public static int Resolve(string hdFolder, string name, System.Func<string, GameObject> load)
        {
            for (int tier = Level; tier >= 1; tier--)
                if (load(hdFolder + name + Suffixes[tier]) != null) return tier;
            return 0;
        }
    }
}
