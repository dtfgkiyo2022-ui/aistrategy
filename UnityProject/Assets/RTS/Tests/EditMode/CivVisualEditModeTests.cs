using System;
using System.IO;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Presentation;

namespace Rts.Tests.EditMode
{
    public sealed class CivVisualEditModeTests
    {
        [Test]
        public void EveryCivilisationHasPaletteAndGlyph()
        {
            foreach (CivKind civ in CivPalette.Civilizations)
            {
                var definition = CivPalette.Get(civ);
                Assert.IsFalse(string.IsNullOrEmpty(definition.GlyphFileName), civ + " must have a glyph");
                string path = Path.Combine(UnityEngine.Application.dataPath, "RTS", "UI", "Resources", "Icons", "Civ", definition.GlyphFileName);
                Assert.IsTrue(File.Exists(path), path + " must exist");
            }
        }

        [Test]
        public void CivilisationColoursStayApartFromEachOtherAndFactions()
        {
            for (int i = 0; i < CivPalette.Civilizations.Count; i++)
            {
                CivKind a = CivPalette.Civilizations[i];
                Assert.IsTrue(CivPalette.IsDistinctFromFaction(a, 1), a + " is too close to west blue");
                Assert.IsTrue(CivPalette.IsDistinctFromFaction(a, 2), a + " is too close to east red");
                for (int j = i + 1; j < CivPalette.Civilizations.Count; j++)
                    Assert.IsTrue(CivPalette.AreDistinct(a, CivPalette.Civilizations[j]), a + " and " + CivPalette.Civilizations[j] + " are too close");
            }
        }

        [Test]
        public void HealthBarUsesGreenYellowRedBands()
        {
            bool previousLanguage = UiText.Japanese;
            UiText.Japanese = true;
            try
            {
                Assert.AreEqual(new CivPalette.Swatch(74, 177, 92).R, CivPalette.HealthFill(1f).R);
                Assert.AreEqual(new CivPalette.Swatch(225, 170, 54).G, CivPalette.HealthFill(0.5f).G);
                Assert.AreEqual(new CivPalette.Swatch(207, 59, 47).B, CivPalette.HealthFill(0.1f).B);
            }
            finally
            {
                UiText.Japanese = previousLanguage;
            }
        }
    }
}
