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
    /// The graphics setting picks the model detail like the buildings (ArtDetail): Low = the files above,
    /// Medium/High/Ultra = UnitModelsHD/&lt;name&gt;_M/_H/_U with 2048 pictures (_BaseColor2k, _BaseColorRed2k, _Normal2k).
    /// A tier change applies to models created after it; the soldiers already on the field keep theirs.
    /// </summary>
    public static class CustomUnitArt
    {
        private const string Folder = "UnitModels/";
        /// <summary>Riders and machines: the pack's clips are for its own (non-Humanoid) skeletons, so these models carry
        /// their own idle/walk/attack clips, like the neutral beasts.</summary>
        private const string AnimatedFolder = "UnitModelsAnimated/";
        private const string HdFolder = "UnitModelsHD/";

        private static readonly Dictionary<UnitKind, string> AnimatedTable = new Dictionary<UnitKind, string>
        {
            { UnitKind.Cavalry, "Cavalry" },
            { UnitKind.LightCavalry, "LightCavalry" },
            { UnitKind.HeavyCavalry, "HeavyCavalry" },
            { UnitKind.Cannon, "Cannon" },
            { UnitKind.Ram, "Ram" }
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
        private static int cacheVersion = -1;

        public static bool Has(UnitKind kind) => Model(kind) != null;

        /// <summary>Finished height. The pack's heights fit its own models; the Tripo cannon is low and long, so the
        /// pack's 3.6 m (its catapult) made it about six metres long (11-18).</summary>
        public static float Height(UnitKind kind, float packHeight) => kind == UnitKind.Cannon ? 1.4f : kind == UnitKind.Ram ? 2.6f : packHeight;

        public static bool HasOwnClips(UnitKind kind) => AnimatedTable.ContainsKey(kind) && Model(kind) != null;

        private static bool TryName(UnitKind kind, out string name, out string lowFolder)
        {
            lowFolder = null;
            if (Table.TryGetValue(kind, out name)) lowFolder = Folder;
            else if (AnimatedTable.TryGetValue(kind, out name)) lowFolder = AnimatedFolder;
            return lowFolder != null;
        }

        /// <summary>A tier change drops what was loaded, so the next model created uses the new tier.</summary>
        private static void DropOldTier()
        {
            if (cacheVersion == ArtDetail.Version) return;
            cacheVersion = ArtDetail.Version;
            models.Clear();
            materials.Clear();
            clips.Clear();
        }

        private static GameObject LoadModel(string path)
        {
            if (models.TryGetValue(path, out var model)) return model;
            model = Resources.Load<GameObject>(path);
            models.Add(path, model);
            return model;
        }

        /// <summary>The Resources path of the model for the current tier, and that tier (0 = Low).</summary>
        private static string Path(string name, string lowFolder, out int tier)
        {
            DropOldTier();
            tier = ArtDetail.Resolve(HdFolder, name, LoadModel);
            return tier == 0 ? lowFolder + name : HdFolder + name + ArtDetail.Suffix(tier);
        }

        public static GameObject Model(UnitKind kind)
        {
            if (LocalVisualPack.Disabled || !TryName(kind, out var name, out var lowFolder)) return null;
            return LoadModel(Path(name, lowFolder, out _));
        }

        private const string FemaleVillager = "VillagerFemale";

        /// <summary>Half the villagers (odd ids) are drawn as women when the model is there (display only, 11-18).</summary>
        public static GameObject FemaleVillagerModel()
        {
            if (LocalVisualPack.Disabled) return null;
            return LoadModel(Path(FemaleVillager, Folder, out _));
        }

        public static Material FemaleVillagerMaterial(bool own) => MaterialAt(FemaleVillager, Folder, own);

        public static Material Material(UnitKind kind, bool own)
        {
            if (!TryName(kind, out var name, out var lowFolder)) return null;
            return MaterialAt(name, lowFolder, own);
        }

        private static Material MaterialAt(string name, string lowFolder, bool own)
        {
            Path(name, lowFolder, out int tier);
            string key = name + ArtDetail.Suffix(tier) + (own ? "" : "Red");
            if (materials.TryGetValue(key, out var material)) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            string picture = tier == 0 ? lowFolder + name : HdFolder + name;
            string size = tier == 0 ? "" : "2k";
            var baseColor = Resources.Load<Texture2D>(picture + (own ? "_BaseColor" : "_BaseColorRed") + size);
            if (baseColor == null && !own) baseColor = Resources.Load<Texture2D>(picture + "_BaseColor" + size);
            if (shader != null && baseColor != null)
            {
                material = new Material(shader) { name = key + " (unit art)" };
                material.SetTexture("_BaseMap", baseColor);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.15f);
                material.SetFloat("_Metallic", 0f);
                var normal = Resources.Load<Texture2D>(picture + "_Normal" + size);
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
            if (!HasOwnClips(kind) || !TryName(kind, out var name, out var lowFolder)) return null;
            string path = Path(name, lowFolder, out _);
            if (!clips.TryGetValue(path, out var found))
            {
                found = new Clips();
                foreach (var clip in Resources.LoadAll<AnimationClip>(path))
                {
                    if (clip == null || clip.name.StartsWith("__preview__")) continue;
                    string lower = clip.name.ToLowerInvariant();
                    if (found.Idle == null && lower.Contains("idle")) found.Idle = clip;
                    else if (found.Walk == null && lower.Contains("walk")) found.Walk = clip;
                    else if (found.Attack == null && lower.Contains("attack")) found.Attack = clip;
                }
                clips.Add(path, found);
            }
            return NeutralModelArt.CreateOwnClipAnimation(instance, found.Idle, found.Walk, found.Attack, id);
        }
    }
}
