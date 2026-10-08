namespace Rts.Presentation
{
    /// <summary>Unity-free correspondence between the HUD theme choices and their assets/fonts.</summary>
    public static class HudThemeCatalog
    {
        public const string PlayerPrefsKey = "rts.hud.theme";

        public sealed class Definition
        {
            public string EnglishName { get; }
            public string JapaneseName { get; }
            public string ResourcePath { get; }
            public string[] HeadingFontFamilies { get; }
            public string[] BodyFontFamilies { get; }
            public bool UseBundledHeadingFont { get; }
            public bool UseBundledBodyFont { get; }

            public Definition(string englishName, string japaneseName, string resourcePath,
                string[] headingFontFamilies, string[] bodyFontFamilies,
                bool useBundledHeadingFont, bool useBundledBodyFont)
            {
                EnglishName = englishName;
                JapaneseName = japaneseName;
                ResourcePath = resourcePath;
                HeadingFontFamilies = headingFontFamilies;
                BodyFontFamilies = bodyFontFamilies;
                UseBundledHeadingFont = useBundledHeadingFont;
                UseBundledBodyFont = useBundledBodyFont;
            }
        }

        private static readonly Definition[] definitions =
        {
            new Definition("A Stone & Brass", "A 石と真鍮", "Hud/Themes/ThemeStone",
                new[] { "Yu Mincho", "MS Mincho" },
                new[] { "Yu Gothic UI", "Meiryo", "MS Gothic" },
                false, true),
            new Definition("B Operations Table", "B 作戦卓", "Hud/Themes/ThemeTable",
                new[] { "BIZ UDGothic", "MS Gothic" },
                new[] { "BIZ UDGothic", "MS Gothic" },
                true, true),
            new Definition("C Parchment War Map", "C 羊皮紙の軍図", "Hud/Themes/ThemeParchment",
                new[] { "Yu Mincho", "MS Mincho" },
                new[] { "Meiryo", "Yu Gothic UI", "MS Gothic" },
                true, true),
            // Between A and C (owner, 10-08): darker than parchment, lighter than stone, with A's leather buttons.
            new Definition("D Leather & Vellum", "D 革と羊皮紙", "Hud/Themes/ThemeLeather",
                new[] { "Yu Mincho", "MS Mincho" },
                new[] { "Yu Gothic UI", "Meiryo", "MS Gothic" },
                false, true)
        };

        public static int Count { get { return definitions.Length; } }

        public static Definition Get(int index)
        {
            return definitions[ClampIndex(index)];
        }

        public static int ClampIndex(int index)
        {
            if (index < 0) return 0;
            if (index >= definitions.Length) return definitions.Length - 1;
            return index;
        }
    }
}
