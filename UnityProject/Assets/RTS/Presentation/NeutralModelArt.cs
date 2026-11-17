using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Rts.Presentation
{
    /// <summary>
    /// Models made for this game's neutral beasts (11-17, first the wolf), kept in the repository under
    /// Assets/RTS/Art/Neutrals/Resources/NeutralModels. A kind without a model keeps the placeholder shape.
    /// The model file carries its own clips; a clip is found by name ("idle", "walk" or "run", "attack").
    /// Display only: nothing here is read back into the simulation.
    /// </summary>
    public static class NeutralModelArt
    {
        private const string Folder = "NeutralModels/";

        private sealed class Spec
        {
            public readonly string Name;
            /// <summary>Finished height in metres (shoulder or head, whichever is the top of the model).</summary>
            public readonly float Height;
            public Spec(string name, float height) { Name = name; Height = height; }
        }

        private static readonly Dictionary<NeutralKind, Spec> Table = new Dictionary<NeutralKind, Spec>
        {
            { NeutralKind.WolfPack, new Spec("Wolf", 1.1f) },
            { NeutralKind.RockBeast, new Spec("RockBeast", 2.0f) },
            { NeutralKind.FlameHornBeast, new Spec("FlameHornBeast", 2.4f) },
            { NeutralKind.AzureSerpent, new Spec("AzureSerpent", 2.6f) },
            { NeutralKind.ShellBeast, new Spec("ShellBeast", 1.5f) },
            { NeutralKind.AugmentGuardian, new Spec("AugmentGuardian", 3.2f) }
        };

        private sealed class Loaded
        {
            public GameObject Model;
            public AnimationClip Idle, Walk, Attack;
            public float Scale;
            public float BaseOffset;
            public Material Material;
        }

        private static readonly Dictionary<NeutralKind, Loaded> cache = new Dictionary<NeutralKind, Loaded>();

        public static bool Has(NeutralKind kind) => Load(kind) != null;

        /// <summary>
        /// Creates the model under parent (feet on the parent's origin, facing +Z) and, when the model has the three
        /// clips, an animation handle. Returns false when the kind has no model.
        /// </summary>
        public static bool TryCreate(NeutralKind kind, Transform parent, uint id, out GameObject instance,
            out LocalVisualPack.AnimationHandle animation)
        {
            instance = null; animation = null;
            var loaded = Load(kind);
            if (loaded == null) return false;
            instance = Object.Instantiate(loaded.Model, parent, false);
            instance.name = "NeutralModel";
            instance.transform.localScale = Vector3.one * loaded.Scale;
            instance.transform.localPosition = new Vector3(0f, loaded.BaseOffset, 0f);
            foreach (var collider in instance.GetComponentsInChildren<Collider>()) Object.Destroy(collider);
            if (loaded.Material != null)
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++) slots[i] = loaded.Material;
                    renderer.sharedMaterials = slots;
                }
            animation = CreateAnimation(instance, loaded, id);
            return true;
        }

        private static Loaded Load(NeutralKind kind)
        {
            if (LocalVisualPack.Disabled) return null;
            if (cache.TryGetValue(kind, out var hit)) return hit;
            Loaded loaded = null;
            if (Table.TryGetValue(kind, out var spec))
            {
                var model = Resources.Load<GameObject>(Folder + spec.Name);
                if (model != null)
                {
                    loaded = new Loaded { Model = model, Material = LoadMaterial(spec.Name) };
                    foreach (var clip in Resources.LoadAll<AnimationClip>(Folder + spec.Name))
                    {
                        if (clip == null || clip.name.StartsWith("__preview__")) continue;
                        string name = clip.name.ToLowerInvariant();
                        if (loaded.Idle == null && name.Contains("idle")) loaded.Idle = clip;
                        else if (loaded.Walk == null && (name.Contains("walk") || name.Contains("run"))) loaded.Walk = clip;
                        else if (loaded.Attack == null && (name.Contains("attack") || name.Contains("bite"))) loaded.Attack = clip;
                    }
                    var bounds = MeasureBounds(model);
                    float height = bounds.size.y;
                    loaded.Scale = height > 0.0001f ? spec.Height / height : 1f;
                    loaded.BaseOffset = -bounds.min.y * loaded.Scale;
                }
            }
            cache[kind] = loaded;
            return loaded;
        }

        /// <summary>Same look as the game's own building models (CustomBuildingArt): URP Lit with the baked pictures.</summary>
        private static Material LoadMaterial(string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var baseColor = Resources.Load<Texture2D>(Folder + name + "_BaseColor");
            if (shader == null || baseColor == null) return null;
            var material = new Material(shader) { name = name + " (neutral art)" };
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
            return material;
        }

        private static Bounds MeasureBounds(GameObject model)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh == null) continue;
                var b = TransformBounds(skinned.transform, model.transform, skinned.sharedMesh.bounds);
                if (!any) { bounds = b; any = true; } else bounds.Encapsulate(b);
            }
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var b = TransformBounds(filter.transform, model.transform, filter.sharedMesh.bounds);
                if (!any) { bounds = b; any = true; } else bounds.Encapsulate(b);
            }
            return bounds;
        }

        private static Bounds TransformBounds(Transform from, Transform root, Bounds local)
        {
            // The prefab asset is not in a scene, so its transforms are read relative to the asset root.
            var matrix = root.worldToLocalMatrix * from.localToWorldMatrix;
            var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            var e = local.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                result.Encapsulate(matrix.MultiplyPoint3x4(local.center + new Vector3(e.x * x, e.y * y, e.z * z)));
            return result;
        }

        private static LocalVisualPack.AnimationHandle CreateAnimation(GameObject instance, Loaded loaded, uint id)
        {
            if (loaded.Idle == null || loaded.Walk == null || loaded.Attack == null) return null;
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            var graph = PlayableGraph.Create("RTS Neutral Animation");
            var mixer = AnimationMixerPlayable.Create(graph, 5);
            var idle = AnimationClipPlayable.Create(graph, loaded.Idle);
            var walk = AnimationClipPlayable.Create(graph, loaded.Walk);
            var attack = AnimationClipPlayable.Create(graph, loaded.Attack);
            graph.Connect(idle, 0, mixer, 0);
            graph.Connect(walk, 0, mixer, 1);
            graph.Connect(attack, 0, mixer, 2);
            mixer.SetInputWeight(0, 1f);
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            output.SetSourcePlayable(mixer);
            // A pack of three should not step in unison.
            float offset = (id % 97u) / 97f;
            idle.SetTime(offset * loaded.Idle.length);
            walk.SetTime(offset * loaded.Walk.length);
            attack.SetTime(offset * loaded.Attack.length);
            graph.Play();
            return new LocalVisualPack.AnimationHandle(graph, mixer, idle, walk, attack, default(AnimationClipPlayable),
                default(AnimationClipPlayable), loaded.Attack.length, 0d, false, false);
        }
    }
}
