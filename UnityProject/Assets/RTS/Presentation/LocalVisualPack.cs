using Rts.Contracts;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rts.Presentation
{
    /// <summary>
    /// Optional visuals from a purchased asset pack (Toony Tiny RTS Set). The pack lives under Assets/ThirdParty/, which
    /// is git-ignored (an Extension Asset needs one license per person holding the files), so this code refers to it
    /// only by path and works without it: every method reports false when the pack is missing, and the caller falls
    /// back to the placeholder models. Nothing here is referenced from a scene or prefab.
    /// </summary>
    public static class LocalVisualPack
    {
        private const string Root = "Assets/ThirdParty/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/";
        private const float InfantryHeight = 2.4f, ScoutHeight = 2.4f, CoreWidth = 9f, OutpostWidth = 6f;
        private const string OutpostModel = "models/buildings/Tower_A.FBX";

        public static bool HasUnit(UnitKind kind) { return Exists(UnitPath(kind)); }

        public static bool HasCore() { return Exists(Root + "models/buildings/Castle.FBX"); }

        public static bool HasOutpost() { return Exists(Root + OutpostModel); }

        /// <summary>Creates an outpost tower under parent, white (neutral) until SetOutpostOwner colors it.</summary>
        public static bool TryCreateOutpost(Transform parent, out GameObject instance, out float height)
        {
            return TryCreate(Root + OutpostModel, BuildingMaterial(0), parent, OutpostWidth, true, out instance, out height);
        }

        /// <summary>Colors an outpost tower by its owner: 1 west blue, 2 east red, anything else neutral white.</summary>
        public static void SetOutpostOwner(GameObject instance, uint owner)
        {
            Recolor(instance, BuildingMaterial(owner));
        }

        private static string BuildingMaterial(uint owner)
        {
            return Root + "models/materials/color/Buildings/TT_RTS_buildings_" + (owner == 1 ? "blue" : owner == 2 ? "red" : "white") + ".mat";
        }

        /// <summary>Creates a unit of the pack under parent, feet on the parent's y, team blue (own) or red (enemy).</summary>
        public static bool TryCreateUnit(UnitKind kind, bool own, Transform parent, out GameObject instance)
        {
            return TryCreate(UnitPath(kind), Root + "models/materials/color/Units/TT_RTS_Units_" + (own ? "blue" : "red") + ".mat",
                parent, kind == UnitKind.Scout ? ScoutHeight : InfantryHeight, false, out instance, out _);
        }

        /// <summary>Creates the core building under parent; height is the top of the model, for placing the HP bar.</summary>
        public static bool TryCreateCore(bool west, Transform parent, out GameObject instance, out float height)
        {
            return TryCreate(Root + "models/buildings/Castle.FBX", Root + "models/materials/color/Buildings/TT_RTS_buildings_" + (west ? "blue" : "red") + ".mat",
                parent, CoreWidth, true, out instance, out height);
        }

        private static string UnitPath(UnitKind kind)
        {
            return Root + "prefabs/" + (kind == UnitKind.Scout ? "TT_Scout.prefab" : "TT_Light_Infantry.prefab");
        }

#if UNITY_EDITOR
        private static bool Exists(string path) { return AssetDatabase.LoadAssetAtPath<GameObject>(path) != null; }

        private static void Recolor(GameObject instance, string materialPath)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null || instance == null) return;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }

        private static bool TryCreate(string modelPath, string materialPath, Transform parent, float size, bool bySpan,
            out GameObject instance, out float height)
        {
            instance = null; height = 0f;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) return false;
            var holder = new GameObject("Pack");
            holder.transform.SetParent(parent, false);
            var body = Object.Instantiate(model, holder.transform);
            body.transform.localPosition = Vector3.zero;
            Recolor(body, materialPath);
            var bounds = Measure(body);
            float measured = bySpan ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y;
            if (measured <= 0.0001f) { Discard(holder); return false; }
            holder.transform.localScale = Vector3.one * (size / measured);
            bounds = Measure(body);
            // Feet on the parent's ground level, whatever the model's own pivot is.
            holder.transform.position += new Vector3(0f, parent.position.y - bounds.min.y, 0f);
            height = bounds.size.y;
            instance = holder;
            return true;
        }

        private static Bounds Measure(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            bool first = true;
            foreach (var renderer in renderers)
            {
                if (first) { bounds = renderer.bounds; first = false; } else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private static void Discard(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
        }
#else
        // A player build cannot load the pack by path, so it always uses the placeholder models.
        private static bool Exists(string path) { return false; }

        private static void Recolor(GameObject instance, string materialPath) { }

        private static bool TryCreate(string modelPath, string materialPath, Transform parent, float size, bool bySpan,
            out GameObject instance, out float height)
        {
            instance = null; height = 0f;
            return false;
        }
#endif
    }
}
