using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Soldier and villager models made for this game (Tripo, 11-18), kept in the repository under
    /// Assets/RTS/Art/Units/Resources/UnitModels. They take priority over the purchased pack, like CustomBuildingArt.
    /// Each model has a Humanoid skeleton (bone names as Mixamo), so the pack's Humanoid clips play on it unchanged.
    /// The own side uses the blue picture and the enemy the red one (_BaseColorRed, hue-shifted from the blue).
    /// </summary>
    public static class CustomUnitArt
    {
        private const string Folder = "UnitModels/";

        private static readonly Dictionary<UnitKind, string> Table = new Dictionary<UnitKind, string>
        {
            { UnitKind.Villager, "Villager" },
            { UnitKind.Infantry, "Infantry" },
            { UnitKind.HeavyInfantry, "HeavyInfantry" },
            { UnitKind.Scout, "Scout" },
            { UnitKind.Archer, "Archer" },
            { UnitKind.SkirmishArcher, "SkirmishArcher" },
            { UnitKind.Mercenary, "Mercenary" },
            { UnitKind.Monk, "Monk" }
        };

        private static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        public static bool Has(UnitKind kind) => Model(kind) != null;

        public static GameObject Model(UnitKind kind)
        {
            if (LocalVisualPack.Disabled || !Table.TryGetValue(kind, out var name)) return null;
            if (models.TryGetValue(name, out var model)) return model;
            model = Resources.Load<GameObject>(Folder + name);
            models.Add(name, model);
            return model;
        }

        public static Material Material(UnitKind kind, bool own)
        {
            if (!Table.TryGetValue(kind, out var name)) return null;
            string key = name + (own ? "" : "Red");
            if (materials.TryGetValue(key, out var material)) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var baseColor = Resources.Load<Texture2D>(Folder + name + (own ? "_BaseColor" : "_BaseColorRed"));
            if (baseColor == null && !own) baseColor = Resources.Load<Texture2D>(Folder + name + "_BaseColor");
            if (shader != null && baseColor != null)
            {
                material = new Material(shader) { name = key + " (unit art)" };
                material.SetTexture("_BaseMap", baseColor);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.15f);
                material.SetFloat("_Metallic", 0f);
                var normal = Resources.Load<Texture2D>(Folder + name + "_Normal");
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
            }
            materials.Add(key, material);
            return material;
        }
    }
}
