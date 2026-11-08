using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Resource-point models made for this game (docs/art-direction.md), kept under
    /// Assets/RTS/Art/Resources/Resources/ResourceModels. When a kind has them they replace the code-built shapes of
    /// <see cref="ResourceModels"/>; a gathered point shrinks (rocks, bushes) or loses trees. Display only.
    /// </summary>
    public static class ResourceArt
    {
        private const string Folder = "ResourceModels/";
        private const string HdFolder = "ResourceModelsHD/";
        private static int cacheVersion;
        /// <summary>The child that holds the models; its meshes are shared assets and must not be destroyed.</summary>
        public const string HolderName = "Art";
        /// <summary>Below this share of the screen height the lighter model is drawn.</summary>
        private const float NearScreenHeight = 0.04f;

        private static readonly string[] SingleTrees = { "res_tree_oak", "res_tree_pine", "res_tree_birch" };
        private static readonly Dictionary<ResourceKind, string[]> Clumps = new Dictionary<ResourceKind, string[]>
        {
            { ResourceKind.Stone, new[] { "res_stone_a", "res_stone_b" } },
            { ResourceKind.Ore, new[] { "res_ore_a", "res_ore_b" } },
            { ResourceKind.Gold, new[] { "res_gold_a", "res_gold_b" } },
            { ResourceKind.Food, new[] { "res_berry_a", "res_berry_b" } }
        };

        private static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        /// <summary>Builds the point's look under root from the models; false when this kind has none (keep the code shapes).</summary>
        public static bool TryBuild(GameObject root, ResourceKind kind, uint id, int level)
        {
            uint seed = id * 2654435761u + (uint)kind * 97u + 7u;
            var holder = new GameObject(HolderName);
            holder.transform.SetParent(root.transform, false);
            bool any;
            if (kind == ResourceKind.Wood)
            {
                // A full point is a grove; it thins to two trees and then one smaller tree.
                if (level >= ResourceModels.Levels && Place(holder, "res_tree_group", Vector3.zero, Turn(ref seed), 5.2f, true))
                    any = true;
                else
                {
                    int trees = level >= 3 ? 2 : 1;
                    any = false;
                    for (int i = 0; i < trees; i++)
                    {
                        string name = SingleTrees[Pick(ref seed, SingleTrees.Length)];
                        var at = trees == 1 ? Vector3.zero : new Vector3(i == 0 ? -0.9f : 0.9f, 0f, i == 0 ? 0.4f : -0.4f);
                        any |= Place(holder, name, at, Turn(ref seed), level >= 2 ? 4.4f : 3.6f, true);
                    }
                }
            }
            else if (Clumps.TryGetValue(kind, out var names))
            {
                // Rocks and bushes keep their shape and get smaller as they are gathered.
                float scale = 0.55f + 0.45f * level / ResourceModels.Levels;
                any = Place(holder, names[Pick(ref seed, names.Length)], Vector3.zero, Turn(ref seed), 3.4f * scale, false);
            }
            else any = false;
            if (!any) Discard(holder);
            return any;
        }

        /// <summary>
        /// One model as a mesh, a material and the matrix that puts its feet at the origin, centred, with height
        /// (byHeight) or width of 1 metre. For drawing many copies at once (the forests and mountains, 11-09).
        /// lod1 picks the lighter file. False when the model is missing.
        /// </summary>
        public static bool TryGetUnitMesh(string name, bool lod1, out Mesh mesh, out Material material, out Matrix4x4 unit, bool byHeight)
        {
            mesh = null; material = null; unit = Matrix4x4.identity;
            DropOldTier();
            int tier = ArtDetail.Resolve(HdFolder, name, LoadModel);
            string path = tier == 0 ? Folder + name : HdFolder + name + ArtDetail.Suffix(tier);
            if (lod1 && LoadModel(path + "_LOD1") != null) path += "_LOD1";
            var model = LoadModel(path);
            if (model == null) return false;
            var filter = model.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;
            material = LoadMaterial(name, tier);
            if (material == null) return false;
            mesh = filter.sharedMesh;
            var toRoot = model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var b = mesh.bounds;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = toRoot.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
            var size = max - min;
            float measured = byHeight ? size.y : Mathf.Max(size.x, size.z);
            if (measured <= 0.0001f) return false;
            var centre = (min + max) * 0.5f;
            unit = Matrix4x4.Scale(Vector3.one / measured) * Matrix4x4.Translate(new Vector3(-centre.x, -min.y, -centre.z)) * toRoot;
            return true;
        }

        private static uint Pick(ref uint seed, int count)
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFFFF) % (uint)count;
        }

        private static float Turn(ref uint seed) { return Pick(ref seed, 360); }

        /// <summary>One model, centred on at, its height (trees) or its width (clumps) set to size metres.</summary>
        private static bool Place(GameObject holder, string name, Vector3 at, float turn, float size, bool byHeight)
        {
            DropOldTier();
            int tier = ArtDetail.Resolve(HdFolder, name, LoadModel);
            string path = tier == 0 ? Folder + name : HdFolder + name + ArtDetail.Suffix(tier);
            var near = LoadModel(path);
            if (near == null) return false;
            var far = LoadModel(path + "_LOD1");
            var material = LoadMaterial(name, tier);
            var body = new GameObject(name);
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
            float measured = byHeight ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.z);
            if (measured <= 0.0001f) { Discard(body); return false; }
            body.transform.localScale = Vector3.one * (size / measured);
            body.transform.localRotation = Quaternion.Euler(0f, turn, 0f);
            bounds = Measure(nearInstance);
            var target = holder.transform.TransformPoint(at);
            body.transform.position += new Vector3(target.x - bounds.center.x, target.y - bounds.min.y, target.z - bounds.center.z);
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

        private static Material LoadMaterial(string name, int tier)
        {
            string key = name + ArtDetail.Suffix(tier);
            if (materials.TryGetValue(key, out var material)) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Texture2D baseColor = null, normal = null;
            if (tier > 0)
            {
                if (baseColor == null) baseColor = Resources.Load<Texture2D>(HdFolder + name + "_BaseColor2k");
                if (baseColor != null) normal = Resources.Load<Texture2D>(HdFolder + name + "_Normal2k");
            }
            if (baseColor == null)
            {
                baseColor = Resources.Load<Texture2D>(Folder + name + "_BaseColor");
                normal = Resources.Load<Texture2D>(Folder + name + "_Normal");
            }
            if (shader != null && baseColor != null)
            {
                material = new Material(shader) { name = key + " (art)", enableInstancing = true };
                material.SetTexture("_BaseMap", baseColor);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.1f);
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

        private static void Discard(Object target)
        {
#if UNITY_EDITOR
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
#else
            Object.Destroy(target);
#endif
        }

        private static Bounds Measure(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
                if (i == 0) bounds = renderers[i].bounds; else bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
