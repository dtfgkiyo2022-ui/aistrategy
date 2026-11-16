using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    public sealed partial class BattlefieldView
    {
        private sealed class WardVisual
        {
            public GameObject Root;
            public Renderer Pole;
            public Renderer Ball;
            public LineRenderer Vision;
            public bool Own;
            public WardKind Kind;
            public bool Detected;
        }

        private readonly Dictionary<uint, WardVisual> wardVisuals = new Dictionary<uint, WardVisual>();

        private void SyncWards(FactionFrame frame)
        {
            var live = new HashSet<uint>();
            foreach (var ward in frame.Wards)
            {
                live.Add(ward.Id);
                if (!wardVisuals.TryGetValue(ward.Id, out var visual))
                {
                    var root = new GameObject("Ward_" + ward.Id);
                    root.transform.SetParent(transform, false);
                    var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pole.name = "WardPole"; pole.transform.SetParent(root.transform, false);
                    pole.transform.localPosition = new Vector3(0f, 1.1f, 0f); pole.transform.localScale = new Vector3(0.12f, 1.1f, 0.12f);
                    Discard(pole.GetComponent<Collider>());
                    var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    ball.name = "WardGlow"; ball.transform.SetParent(root.transform, false);
                    ball.transform.localPosition = new Vector3(0f, 2.35f, 0f); ball.transform.localScale = Vector3.one * 0.42f;
                    Discard(ball.GetComponent<Collider>());
                    var circle = new GameObject("WardVision"); circle.transform.SetParent(root.transform, false);
                    var line = circle.AddComponent<LineRenderer>(); line.loop = true; line.positionCount = 48; line.widthMultiplier = 0.045f;
                    line.sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.2f, 0.55f, 1f, 0.35f));
                    for (int i = 0; i < line.positionCount; i++)
                    { float a = i / (float)line.positionCount * Mathf.PI * 2f; line.SetPosition(i, new Vector3(Mathf.Cos(a) * 12f, 0.04f, Mathf.Sin(a) * 12f)); }
                    visual = new WardVisual { Root = root, Pole = pole.GetComponent<Renderer>(), Ball = ball.GetComponent<Renderer>(), Vision = line };
                    wardVisuals.Add(ward.Id, visual);
                }
                visual.Own = ward.FactionId == frame.FactionId; visual.Kind = ward.Kind; visual.Detected = ward.IsDetected;
                visual.Root.transform.position = ToWorld(ward.Position, 0f);
                visual.Root.SetActive(true); visual.Vision.gameObject.SetActive(visual.Own);
                UpdateWardMaterial(visual, ward);
            }
            var remove = new List<uint>();
            foreach (var pair in wardVisuals) if (!live.Contains(pair.Key)) remove.Add(pair.Key);
            foreach (uint id in remove) { Discard(wardVisuals[id].Root); wardVisuals.Remove(id); }
        }

        private static void UpdateWardMaterial(WardVisual visual, WardView ward)
        {
            Color color = visual.Own ? new Color(0.1f, 0.35f, 1f) : new Color(0.95f, 0.12f, 0.1f);
            float alpha = visual.Own || ward.Kind == WardKind.Revealing || ward.IsDetected ? 1f : 0.35f;
            if (!visual.Own && ward.Kind == WardKind.Hidden && ward.IsDetected) alpha = Mathf.Lerp(0.25f, 1f, (Mathf.Sin(Time.time * 8f) + 1f) * 0.5f);
            color.a = alpha;
            visual.Pole.sharedMaterial = PresentationMaterials.GetUnlit(color);
            visual.Ball.sharedMaterial = PresentationMaterials.GetUnlit(new Color(color.r, color.g, color.b, Mathf.Min(1f, alpha + 0.15f)));
        }

        private void UpdateWardVisuals()
        {
            if (latestFrame == null) return;
            foreach (var ward in latestFrame.Wards)
                if (wardVisuals.TryGetValue(ward.Id, out var visual) && !visual.Own && ward.Kind == WardKind.Hidden && ward.IsDetected)
                    UpdateWardMaterial(visual, ward);
        }

        private void ClearWardVisuals()
        {
            foreach (var visual in wardVisuals.Values) Discard(visual.Root);
            wardVisuals.Clear();
        }
    }
}
