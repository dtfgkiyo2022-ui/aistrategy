using UnityEngine;
using UnityEngine.Rendering;

namespace Rts.Presentation
{
    /// <summary>Shared two-line selection ring: brass edge with a faction-colour centre line.</summary>
    public static class SelectionRingVisuals
    {
        private const int PointCount = 49;
        private static readonly Color Brass = new Color(0.86f, 0.64f, 0.25f, 0.95f);

        public static GameObject Create(Transform parent, string name, float radius, Color factionColor)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            CreateLine(root.transform, "Brass", radius, 0.18f, Brass);
            CreateLine(root.transform, "Faction", radius, 0.09f, factionColor);
            return root;
        }

        public static void SetRadius(GameObject root, float radius)
        {
            if (root == null) return;
            foreach (var line in root.GetComponentsInChildren<LineRenderer>())
                SetCircle(line, radius);
        }

        public static void SetFactionColor(GameObject root, Color factionColor)
        {
            if (root == null) return;
            var line = root.transform.Find("Faction");
            if (line != null) line.GetComponent<LineRenderer>().sharedMaterial = PresentationMaterials.GetUnlit(factionColor);
        }

        private static void CreateLine(Transform parent, string name, float radius, float width, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = PointCount;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = PresentationMaterials.GetUnlit(color);
            SetCircle(line, radius);
        }

        private static void SetCircle(LineRenderer line, float radius)
        {
            for (int i = 0; i < PointCount; i++)
            {
                float angle = i * Mathf.PI * 2f / (PointCount - 1);
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.04f, Mathf.Sin(angle) * radius));
            }
        }
    }
}
