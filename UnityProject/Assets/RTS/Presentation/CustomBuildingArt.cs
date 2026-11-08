using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Building models made for this game (docs/art-direction.md), kept in the repository under
    /// Assets/RTS/Art/Buildings/Resources/BuildingModels. They take priority over the purchased pack and work in a
    /// player build without the pack manifest. Each model carries its own textures, so the owner's colour is not applied.
    /// </summary>
    public static class CustomBuildingArt
    {
        private const string Folder = "BuildingModels/";
        private const string HdFolder = "BuildingModelsHD/";
        private static int cacheVersion;
        /// <summary>Below this share of the screen height the lighter model is drawn.</summary>
        private const float NearScreenHeight = 0.10f;

        private sealed class Spec
        {
            public readonly string Name;
            /// <summary>Finished height in metres at the building's usual footprint.</summary>
            public readonly float Height;
            public Spec(string name, float height) { Name = name; Height = height; }
        }

        // Heights are the model's own proportion at the usual footprint (cells x 2 m), e.g. the farm is 2 x 2 cells (4 m)
        // and 0.73 as tall as it is wide. The model is scaled evenly, so a larger footprint draws it taller too.
        private static readonly Dictionary<BuildingKind, Spec> Table = new Dictionary<BuildingKind, Spec>
        {
            { BuildingKind.Farm, new Spec("Farm", 2.9f) },
            { BuildingKind.Academy, new Spec("Academy", 2.9f) },
            { BuildingKind.ArcheryRange, new Spec("ArcheryRange", 4.0f) },
            { BuildingKind.Assembler, new Spec("Assembler", 3.5f) },
            { BuildingKind.Barracks, new Spec("Barracks", 4.0f) },
            { BuildingKind.Blacksmith, new Spec("Blacksmith", 5.4f) },
            { BuildingKind.BlastFurnace, new Spec("BlastFurnace", 5.4f) },
            { BuildingKind.BrickKiln, new Spec("BrickKiln", 2.5f) },
            { BuildingKind.Caravanserai, new Spec("Caravanserai", 1.6f) },
            { BuildingKind.Castle, new Spec("Castle", 6.1f) },
            { BuildingKind.CharcoalKiln, new Spec("CharcoalKiln", 3.5f) },
            { BuildingKind.CharmWorkshop, new Spec("CharmWorkshop", 3.2f) },
            { BuildingKind.Drill, new Spec("Drill", 4.6f) },
            { BuildingKind.DropSite, new Spec("DropSite", 3.7f) },
            { BuildingKind.DryingRack, new Spec("DryingRack", 3.4f) },
            { BuildingKind.EngineerCamp, new Spec("EngineerCamp", 1.5f) },
            { BuildingKind.Fletcher, new Spec("Fletcher", 5.5f) },
            { BuildingKind.Gate, new Spec("Gate", 1.8f) },
            { BuildingKind.GearWorkshop, new Spec("GearWorkshop", 3.8f) },
            { BuildingKind.GlassWorks, new Spec("GlassWorks", 3.6f) },
            { BuildingKind.GrandHouse, new Spec("GrandHouse", 7.5f) },
            { BuildingKind.Harbor, new Spec("Harbor", 2.9f) },
            { BuildingKind.House, new Spec("House", 4.9f) },
            { BuildingKind.Hut, new Spec("Hut", 3.2f) },
            { BuildingKind.IncenseHouse, new Spec("IncenseHouse", 2.9f) },
            { BuildingKind.Joinery, new Spec("Joinery", 3.7f) },
            { BuildingKind.Lapidary, new Spec("Lapidary", 3.5f) },
            { BuildingKind.LumberCamp, new Spec("LumberCamp", 3.1f) },
            { BuildingKind.Market, new Spec("Market", 2.8f) },
            { BuildingKind.Metalworks, new Spec("Metalworks", 3.6f) },
            { BuildingKind.Mill, new Spec("Mill", 5.1f) },
            { BuildingKind.Mine, new Spec("Mine", 3.0f) },
            { BuildingKind.MineShaft, new Spec("MineShaft", 4.8f) },
            { BuildingKind.Monastery, new Spec("Monastery", 3.0f) },
            { BuildingKind.Quarry, new Spec("Quarry", 3.1f) },
            { BuildingKind.RepairYard, new Spec("RepairYard", 2.9f) },
            { BuildingKind.RopeHut, new Spec("RopeHut", 3.0f) },
            { BuildingKind.Saddlery, new Spec("Saddlery", 3.2f) },
            { BuildingKind.Sawmill, new Spec("Sawmill", 3.6f) },
            { BuildingKind.Scriptorium, new Spec("Scriptorium", 3.6f) },
            { BuildingKind.Shrine, new Spec("Shrine", 3.8f) },
            { BuildingKind.SiegeWorkshop, new Spec("SiegeWorkshop", 4.5f) },
            { BuildingKind.Smelter, new Spec("Smelter", 5.2f) },
            { BuildingKind.Stable, new Spec("Stable", 5.1f) },
            { BuildingKind.Steelworks, new Spec("Steelworks", 4.9f) },
            { BuildingKind.Stonecutter, new Spec("Stonecutter", 2.9f) },
            { BuildingKind.Storage, new Spec("Storage", 3.6f) },
            { BuildingKind.Tollgate, new Spec("Tollgate", 2.2f) },
            { BuildingKind.ToolShop, new Spec("ToolShop", 3.5f) },
            { BuildingKind.Tower, new Spec("Tower", 6.9f) },
            { BuildingKind.Town, new Spec("Town", 7.3f) },
            { BuildingKind.Wall, new Spec("Wall", 1.5f) },
            { BuildingKind.Weaver, new Spec("Weaver", 2.9f) }
        };

        private static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        public static bool Has(BuildingKind kind) { return Table.ContainsKey(kind) && LoadModel(Folder + Table[kind].Name) != null; }

        /// <summary>True when the model made for this game exists under this name (Core, Outpost).</summary>
        public static bool HasNamed(string name)
        {
            DropOldTier();
            return !string.IsNullOrEmpty(name) && LoadModel(Folder + name) != null;
        }

        public static float Height(BuildingKind kind) { return Table.TryGetValue(kind, out var s) ? s.Height : 0f; }

        /// <summary>Creates the model under parent: footprint width x width, its own proportions, feet on the parent's y.</summary>
        public static bool TryCreate(BuildingKind kind, Transform parent, float width, out GameObject instance)
        {
            instance = null;
            if (parent == null || !Table.TryGetValue(kind, out var spec)) return false;
            return TryCreateNamed(spec.Name, spec.Name, parent, width, out instance, out _);
        }

        /// <summary>
        /// A model that is not a BuildingKind (the core, the outpost). colourName picks another base colour texture for
        /// the same model (CoreRed = the enemy's core with red roofs and flags); the normal map stays the model's own.
        /// height is the finished height in metres, for the health bar.
        /// </summary>
        public static bool TryCreateNamed(string name, string colourName, Transform parent, float width,
            out GameObject instance, out float height)
        {
            instance = null;
            height = 0f;
            if (parent == null || string.IsNullOrEmpty(name)) return false;
            DropOldTier();
            int tier = ArtDetail.Resolve(HdFolder, name, LoadModel);
            string path = tier == 0 ? Folder + name : HdFolder + name + ArtDetail.Suffix(tier);
            var near = LoadModel(path);
            if (near == null) return false;
            var far = LoadModel(path + "_LOD1");
            var material = LoadMaterial(name, string.IsNullOrEmpty(colourName) ? name : colourName, tier);
            var holder = new GameObject("Art");
            holder.transform.SetParent(parent, false);
            var body = new GameObject("Body");
            body.transform.SetParent(holder.transform, false);
            var nearInstance = Object.Instantiate(near, body.transform);
            Retexture(nearInstance, material);
            GameObject farInstance = null;
            if (far != null)
            {
                farInstance = Object.Instantiate(far, body.transform);
                Retexture(farInstance, material);
            }
            var bounds = Measure(nearInstance);
            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            if (span <= 0.0001f || bounds.size.y <= 0.0001f) { Discard(holder); return false; }
            body.transform.localScale = Vector3.one * (width / span);
            bounds = Measure(nearInstance);
            body.transform.position += new Vector3(parent.position.x - bounds.center.x, parent.position.y - bounds.min.y,
                parent.position.z - bounds.center.z);
            if (farInstance != null)
            {
                var group = body.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(NearScreenHeight, nearInstance.GetComponentsInChildren<Renderer>()),
                    new LOD(0.002f, farInstance.GetComponentsInChildren<Renderer>())
                });
                group.RecalculateBounds();
            }
            height = bounds.size.y;
            instance = holder;
            return true;
        }

        /// <summary>A tier change drops what was loaded, so only the chosen tier stays referenced (the settings screen
        /// then calls Resources.UnloadUnusedAssets once the old instances are gone).</summary>
        private static void DropOldTier()
        {
            if (cacheVersion == ArtDetail.Version) return;
            cacheVersion = ArtDetail.Version;
            models.Clear();
            materials.Clear();
        }

        /// <summary>path is relative to Resources/, e.g. Folder + name or HdFolder + name + "_M".</summary>
        private static GameObject LoadModel(string path)
        {
            if (models.TryGetValue(path, out var model)) return model;
            model = Resources.Load<GameObject>(path);
            models.Add(path, model);
            return model;
        }

        private static Material LoadMaterial(string name, string colourName, int tier)
        {
            string key = colourName + ArtDetail.Suffix(tier);
            if (materials.TryGetValue(key, out var material)) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Texture2D baseColor = null, normal = null;
            if (tier > 0)
            {
                if (tier >= 3) baseColor = Resources.Load<Texture2D>(HdFolder + colourName + "_BaseColor4k");
                if (baseColor == null) baseColor = Resources.Load<Texture2D>(HdFolder + colourName + "_BaseColor2k");
                if (baseColor != null) normal = Resources.Load<Texture2D>(HdFolder + name + "_Normal2k");
            }
            if (baseColor == null)
            {
                baseColor = Resources.Load<Texture2D>(Folder + colourName + "_BaseColor");
                normal = Resources.Load<Texture2D>(Folder + name + "_Normal");
            }
            if (shader != null && baseColor != null)
            {
                material = new Material(shader) { name = key + " (art)" };
                material.SetTexture("_BaseMap", baseColor);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.15f);
                material.SetFloat("_Metallic", 0f);
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
            }
            materials.Add(key, material);
            return material;
        }

        private static void Retexture(GameObject instance, Material material)
        {
            if (material == null) return;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
        }

        private static Bounds Measure(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
                if (i == 0) bounds = renderers[i].bounds; else bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void Discard(Object target)
        {
#if UNITY_EDITOR
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
#else
            Object.Destroy(target);
#endif
        }
    }
}
