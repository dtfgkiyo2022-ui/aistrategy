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
        /// <summary>Below this share of the screen height the lighter model is drawn.</summary>
        private const float NearScreenHeight = 0.10f;

        private sealed class Spec
        {
            public readonly string Name;
            /// <summary>Finished height in metres at the building's usual footprint.</summary>
            public readonly float Height;
            public Spec(string name, float height) { Name = name; Height = height; }
        }

        // Farm: 2 x 2 cells (4 m); the model is 0.73 as tall as it is wide.
        private static readonly Dictionary<BuildingKind, Spec> Table = new Dictionary<BuildingKind, Spec>
        {
            { BuildingKind.Farm, new Spec("Farm", 2.9f) }
        };

        private static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        public static bool Has(BuildingKind kind) { return Table.ContainsKey(kind) && LoadModel(Table[kind].Name) != null; }

        public static float Height(BuildingKind kind) { return Table.TryGetValue(kind, out var s) ? s.Height : 0f; }

        /// <summary>Creates the model under parent: footprint width x width, the table's height, feet on the parent's y.</summary>
        public static bool TryCreate(BuildingKind kind, Transform parent, float width, out GameObject instance)
        {
            instance = null;
            if (parent == null || !Table.TryGetValue(kind, out var spec)) return false;
            var near = LoadModel(spec.Name);
            if (near == null) return false;
            var far = LoadModel(spec.Name + "_LOD1");
            var material = LoadMaterial(spec.Name);
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
            body.transform.localScale = new Vector3(width / span, spec.Height / bounds.size.y, width / span);
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
            instance = holder;
            return true;
        }

        private static GameObject LoadModel(string name)
        {
            if (models.TryGetValue(name, out var model)) return model;
            model = Resources.Load<GameObject>(Folder + name);
            models.Add(name, model);
            return model;
        }

        private static Material LoadMaterial(string name)
        {
            if (materials.TryGetValue(name, out var material)) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var baseColor = Resources.Load<Texture2D>(Folder + name + "_BaseColor");
            if (shader != null && baseColor != null)
            {
                material = new Material(shader) { name = name + " (art)" };
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
            materials.Add(name, material);
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
