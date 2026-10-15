using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>
    /// Display-only civilisation identity. The table deliberately uses bytes and no Unity types so that its
    /// separation rules can be checked without loading a scene or creating a Unity object.
    /// </summary>
    public static class CivPalette
    {
        public const int CivilizationCount = 14;
        public const float MinimumDistinctDistanceSquared = 0.02f;

        public readonly struct Swatch
        {
            public byte R { get; }
            public byte G { get; }
            public byte B { get; }

            public Swatch(byte r, byte g, byte b)
            {
                R = r; G = g; B = b;
            }

            public float Red { get { return R / 255f; } }
            public float Green { get { return G / 255f; } }
            public float Blue { get { return B / 255f; } }
        }

        public readonly struct Definition
        {
            public Swatch Primary { get; }
            public Swatch Accent { get; }
            public string GlyphFileName { get; }
            public string EnglishName { get; }
            public string JapaneseName { get; }

            public Definition(Swatch primary, Swatch accent, string glyphFileName, string englishName, string japaneseName)
            {
                Primary = primary;
                Accent = accent;
                GlyphFileName = glyphFileName;
                EnglishName = englishName;
                JapaneseName = japaneseName;
            }
        }

        private static readonly CivKind[] civilizations =
        {
            CivKind.Agrarian, CivKind.Metallurgy, CivKind.Forestry, CivKind.Masonry,
            CivKind.Caravan, CivKind.Cavalry, CivKind.Bridge, CivKind.Academy,
            CivKind.Cult, CivKind.Fishing, CivKind.Mountain, CivKind.Tollgate,
            CivKind.Metropolis, CivKind.Sanctuary
        };

        public static IReadOnlyList<CivKind> Civilizations { get { return civilizations; } }

        public static Definition Get(CivKind civ)
        {
            switch (civ)
            {
                case CivKind.Agrarian: return new Definition(new Swatch(214, 184, 76), new Swatch(91, 116, 44), "civ_agrarian.png", "Agrarian", "農耕の文明");
                case CivKind.Metallurgy: return new Definition(new Swatch(85, 153, 184), new Swatch(210, 218, 224), "civ_metallurgy.png", "Metallurgy", "冶金の文明");
                case CivKind.Forestry: return new Definition(new Swatch(95, 173, 104), new Swatch(218, 151, 71), "civ_forestry.png", "Forestry", "森林・木工の文明");
                case CivKind.Masonry: return new Definition(new Swatch(168, 110, 90), new Swatch(224, 190, 135), "civ_masonry.png", "Masonry", "石工・城塞の文明");
                case CivKind.Caravan: return new Definition(new Swatch(216, 120, 174), new Swatch(245, 211, 96), "civ_caravan.png", "Caravan", "隊商・交易の文明");
                case CivKind.Cavalry: return new Definition(new Swatch(229, 142, 53), new Swatch(103, 60, 34), "civ_cavalry.png", "Cavalry", "騎馬・機動の文明");
                case CivKind.Bridge: return new Definition(new Swatch(58, 182, 166), new Swatch(225, 229, 207), "civ_bridge.png", "Engineering", "工兵・架橋の文明");
                case CivKind.Academy: return new Definition(new Swatch(117, 87, 217), new Swatch(239, 220, 112), "civ_academy.png", "Academy", "学府・技術の文明");
                case CivKind.Cult: return new Definition(new Swatch(199, 63, 124), new Swatch(245, 205, 224), "civ_cult.png", "Cult", "教団・改宗の文明");
                case CivKind.Fishing: return new Definition(new Swatch(58, 167, 217), new Swatch(239, 229, 154), "civ_fishing.png", "Fishing", "漁労・港湾の文明");
                case CivKind.Mountain: return new Definition(new Swatch(122, 124, 130), new Swatch(218, 225, 232), "civ_mountain.png", "Mountain", "山岳・鉱夫の文明");
                case CivKind.Tollgate: return new Definition(new Swatch(168, 90, 55), new Swatch(231, 193, 91), "civ_tollgate.png", "Tollgate", "関所・要塞の文明");
                case CivKind.Metropolis: return new Definition(new Swatch(168, 184, 59), new Swatch(79, 104, 57), "civ_metropolis.png", "Metropolis", "都市・人口の文明");
                case CivKind.Sanctuary: return new Definition(new Swatch(225, 214, 176), new Swatch(132, 105, 180), "civ_sanctuary.png", "Sanctuary", "聖地・遺物の文明");
                default: return new Definition(new Swatch(76, 86, 98), new Swatch(196, 171, 115), "civ_primitive.png", "Primitive age", "原始時代");
            }
        }

        public static Swatch Primary(CivKind civ) { return Get(civ).Primary; }
        public static Swatch Accent(CivKind civ) { return Get(civ).Accent; }
        public static string GlyphFileName(CivKind civ) { return Get(civ).GlyphFileName; }

        public static float ColorDistanceSquared(Swatch a, Swatch b)
        {
            float r = a.Red - b.Red, g = a.Green - b.Green, blue = a.Blue - b.Blue;
            return r * r + g * g + blue * blue;
        }

        public static Swatch FactionSwatch(uint faction)
        {
            if (faction == 1) return new Swatch(51, 115, 242);
            if (faction == 2) return new Swatch(230, 64, 51);
            return new Swatch(166, 166, 166);
        }

        public static bool AreDistinct(CivKind a, CivKind b)
        {
            return ColorDistanceSquared(Primary(a), Primary(b)) >= MinimumDistinctDistanceSquared;
        }

        public static bool IsDistinctFromFaction(CivKind civ, uint faction)
        {
            return ColorDistanceSquared(Primary(civ), FactionSwatch(faction)) >= MinimumDistinctDistanceSquared;
        }

        /// <summary>Three stable bands keep HP materials shared even when hundreds of units are visible.</summary>
        public static Swatch HealthFill(float ratio)
        {
            if (ratio <= 0.25f) return new Swatch(207, 59, 47);
            if (ratio <= 0.50f) return new Swatch(225, 170, 54);
            return new Swatch(74, 177, 92);
        }
    }
}
