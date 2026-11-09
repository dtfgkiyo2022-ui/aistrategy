using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// 11-10 test play: villagers at the forest edge vanished under the tree tops. Adds one extra material to a
    /// villager's or soldier's model that shows it as a see-through shape in its faction colour wherever a tree,
    /// building or hill stands in front of it (shader Rts/UnitSilhouette). All quality levels. Display only.
    /// </summary>
    public static class UnitSilhouette
    {
        private static Material own, enemy;
        private static bool looked;

        public static void Attach(GameObject unit, bool isOwn)
        {
            if (unit == null) return;
            var material = Get(isOwn);
            if (material == null) return;
            foreach (var renderer in unit.GetComponentsInChildren<Renderer>(true))
            {
                // Only the body: the civ trim cubes, glyphs, rings and carried goods would add noise.
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                string name = renderer.gameObject.name;
                if (name.StartsWith("Civ") || name.Contains("Ring") || name.Contains("Carry")) continue;
                var slots = renderer.sharedMaterials;
                bool already = false;
                foreach (var slot in slots) if (slot == material) { already = true; break; }
                if (already) continue;
                // A material past the last sub-mesh draws that last sub-mesh again; for the one-mesh unit models
                // that is the whole body.
                var grown = new Material[slots.Length + 1];
                slots.CopyTo(grown, 0);
                grown[slots.Length] = material;
                renderer.sharedMaterials = grown;
            }
        }

        private static Material Get(bool isOwn)
        {
            if (!looked)
            {
                looked = true;
                var shader = Shader.Find("Rts/UnitSilhouette");
                if (shader == null) return null;
                own = Make(shader, new Color(0.30f, 0.62f, 1f, 0.55f), "Unit silhouette (own)");
                enemy = Make(shader, new Color(1f, 0.36f, 0.30f, 0.55f), "Unit silhouette (enemy)");
            }
            return isOwn ? own : enemy;
        }

        private static Material Make(Shader shader, Color color, string name)
        {
            var material = new Material(shader) { name = name };
            material.SetColor("_Color", color);
            return material;
        }
    }
}
