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
        /// <summary>Riders and machines: the pack's clips are for its own (non-Humanoid) skeletons, so these models carry
        /// their own idle/walk/attack clips, like the neutral beasts.</summary>
        private const string AnimatedFolder = "UnitModelsAnimated/";

        private static readonly Dictionary<UnitKind, string> AnimatedTable = new Dictionary<UnitKind, string>
        {
            { UnitKind.Cavalry, "Cavalry" },
            { UnitKind.LightCavalry, "LightCavalry" },
            { UnitKind.HeavyCavalry, "HeavyCavalry" },
            { UnitKind.Cannon, "Cannon" }
        };

        private sealed class Clips { public AnimationClip Idle, Walk, Attack; }
        private static readonly Dictionary<string, Clips> clips = new Dictionary<string, Clips>();

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

        /// <summary>Finished height. The pack's heights fit its own models; the Tripo cannon is low and long, so the
        /// pack's 3.6 m (its catapult) made it about six metres long (11-18).</summary>
        public static float Height(UnitKind kind, float packHeight) => kind == UnitKind.Cannon ? 1.4f : packHeight;

        public static bool HasOwnClips(UnitKind kind) => AnimatedTable.ContainsKey(kind) && Model(kind) != null;

        private static bool TryName(UnitKind kind, out string path)
        {
            path = null;
            if (Table.TryGetValue(kind, out var name)) path = Folder + name;
            else if (AnimatedTable.TryGetValue(kind, out name)) path = AnimatedFolder + name;
            return path != null;
        }

        public static GameObject Model(UnitKind kind)
        {
            if (LocalVisualPack.Disabled || !TryName(kind, out var name)) return null;
            if (models.TryGetValue(name, out var model)) return model;
            model = Resources.Load<GameObject>(name);
            models.Add(name, model);
            return model;
        }

        private const string FemaleVillager = Folder + "VillagerFemale";

        /// <summary>Half the villagers (odd ids) are drawn as women when the model is there (display only, 11-18).</summary>
        public static GameObject FemaleVillagerModel()
        {
            if (LocalVisualPack.Disabled) return null;
            if (models.TryGetValue(FemaleVillager, out var model)) return model;
            model = Resources.Load<GameObject>(FemaleVillager);
            models.Add(FemaleVillager, model);
            return model;
        }

        public static Material FemaleVillagerMaterial(bool own) => MaterialAt(FemaleVillager, own);

        public static Material Material(UnitKind kind, bool own)
        {
            if (!TryName(kind, out var name)) return null;
            return MaterialAt(name, own);
        }

        private static Material MaterialAt(string name, bool own)
        {
            string key = name + (own ? "" : "Red");
            if (materials.TryGetValue(key, out var material)) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var baseColor = Resources.Load<Texture2D>(name + (own ? "_BaseColor" : "_BaseColorRed"));
            if (baseColor == null && !own) baseColor = Resources.Load<Texture2D>(name + "_BaseColor");
            if (shader != null && baseColor != null)
            {
                material = new Material(shader) { name = key + " (unit art)" };
                material.SetTexture("_BaseMap", baseColor);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.15f);
                material.SetFloat("_Metallic", 0f);
                var normal = Resources.Load<Texture2D>(name + "_Normal");
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
            }
            materials.Add(key, material);
            return material;
        }

        /// <summary>The model's own clips for riders and machines; null for the Humanoid soldiers (they use the pack's).</summary>
        public static LocalVisualPack.AnimationHandle CreateOwnAnimation(GameObject instance, UnitKind kind, ulong id)
        {
            if (!HasOwnClips(kind) || !TryName(kind, out var name)) return null;
            if (!clips.TryGetValue(name, out var found))
            {
                found = new Clips();
                foreach (var clip in Resources.LoadAll<AnimationClip>(name))
                {
                    if (clip == null || clip.name.StartsWith("__preview__")) continue;
                    string lower = clip.name.ToLowerInvariant();
                    if (found.Idle == null && lower.Contains("idle")) found.Idle = clip;
                    else if (found.Walk == null && lower.Contains("walk")) found.Walk = clip;
                    else if (found.Attack == null && lower.Contains("attack")) found.Attack = clip;
                }
                clips.Add(name, found);
            }
            return NeutralModelArt.CreateOwnClipAnimation(instance, found.Idle, found.Walk, found.Attack, id);
        }
    }
}
