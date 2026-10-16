using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>One shared world-space health-bar construction for units, buildings, cores and capture gauges.</summary>
    public static class WorldHealthBarVisuals
    {
        public sealed class Handle
        {
            public GameObject Root { get; }
            public Transform Fill { get; }
            public float Width { get; }

            internal Handle(GameObject root, Transform fill, float width)
            {
                Root = root; Fill = fill; Width = width;
            }
        }

        public static Handle Create(Transform parent, string name, float width, float height = 0.16f)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Back";
            back.transform.SetParent(root.transform, false);
            back.transform.localScale = new Vector3(width + 0.16f, height + 0.10f, 1f);
            BattlefieldViewDiscard(back.GetComponent<Collider>());
            back.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.07f, 0.065f, 0.055f, 0.94f));

            var edge = GameObject.CreatePrimitive(PrimitiveType.Quad);
            edge.name = "BrassEdge";
            edge.transform.SetParent(root.transform, false);
            edge.transform.localScale = new Vector3(width + 0.06f, height + 0.04f, 1f);
            BattlefieldViewDiscard(edge.GetComponent<Collider>());
            edge.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.86f, 0.64f, 0.25f, 0.92f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            fill.transform.SetParent(root.transform, false);
            BattlefieldViewDiscard(fill.GetComponent<Collider>());
            fill.GetComponent<Renderer>().sharedMaterial = HealthMaterial(1f);
            return new Handle(root, fill.transform, width);
        }

        public static void Set(Handle bar, int hp, int maxHp, Vector3 position, Quaternion rotation)
        {
            if (bar == null || bar.Fill == null) return;
            float ratio = maxHp <= 0 ? 0f : Mathf.Clamp01(hp / (float)maxHp);
            bar.Root.transform.position = position;
            bar.Root.transform.rotation = rotation;
            bar.Root.SetActive(maxHp > 0);
            bar.Fill.localScale = new Vector3(bar.Width * ratio, 0.16f, 1f);
            bar.Fill.localPosition = new Vector3(-bar.Width * (1f - ratio) / 2f, 0f, -0.01f);
            bar.Fill.GetComponent<Renderer>().sharedMaterial = HealthMaterial(ratio);
        }

        public static void SetConstruction(Handle bar, float progress, Vector3 position, Quaternion rotation)
        {
            if (bar == null || bar.Fill == null) return;
            float ratio = Mathf.Clamp01(progress);
            bar.Root.transform.position = position;
            bar.Root.transform.rotation = rotation;
            bar.Root.SetActive(true);
            bar.Fill.localScale = new Vector3(bar.Width * ratio, 0.16f, 1f);
            bar.Fill.localPosition = new Vector3(-bar.Width * (1f - ratio) / 2f, 0f, -0.01f);
            bar.Fill.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.96f, 0.72f, 0.2f, 1f));
        }

        public static Color HealthColor(float ratio)
        {
            var color = CivPalette.HealthFill(ratio);
            return new Color(color.Red, color.Green, color.Blue, 1f);
        }

        public static int UnitMaxHp(UnitKind kind, int equipmentStage)
        {
            int baseHp;
            switch (kind)
            {
                case UnitKind.Scout: baseHp = 40; break;
                case UnitKind.Villager: baseHp = 40; break;
                case UnitKind.Monk: baseHp = 100; break;
                case UnitKind.Ram: baseHp = 220; break;
                case UnitKind.HeavyInfantry: baseHp = 140; break;
                case UnitKind.HeavyCavalry: baseHp = 160; break;
                case UnitKind.Cavalry: baseHp = 120; break;
                case UnitKind.LightCavalry: baseHp = 90; break;
                default: baseHp = 100; break;
            }
            return baseHp + Mathf.Max(0, equipmentStage - 1) * (baseHp / 10);
        }

        private static Material HealthMaterial(float ratio)
        {
            return PresentationMaterials.GetUnlit(HealthColor(ratio));
        }

        private static void BattlefieldViewDiscard(Object target)
        {
            if (target == null) return;
            if (UnityEngine.Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
        }
    }
}
