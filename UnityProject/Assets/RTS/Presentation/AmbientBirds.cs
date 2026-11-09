using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// 11-10 ("small movement makes the map feel alive"): a few small flocks of birds gliding in wide loops over the
    /// map, flapping now and then. Uses the wall clock, never the game; nothing reads them back. Display only.
    /// </summary>
    public sealed class AmbientBirds : MonoBehaviour
    {
        private const int Flocks = 3;
        private const int BirdsPerFlock = 6;

        private Vector3 size;
        private Transform[] birds;
        private Vector3[] slots;
        private Mesh mesh;

        public void Init(Vector3 mapSize)
        {
            size = mapSize;
            mesh = BuildMesh();
            var material = PresentationMaterials.GetUnlit(new Color(0.16f, 0.15f, 0.14f));
            birds = new Transform[Flocks * BirdsPerFlock];
            slots = new Vector3[birds.Length];
            for (int i = 0; i < birds.Length; i++)
            {
                var bird = new GameObject("Bird " + i);
                bird.transform.SetParent(transform, false);
                bird.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = bird.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                birds[i] = bird.transform;
                int place = i % BirdsPerFlock;
                // A loose V behind the leader.
                float side = place == 0 ? 0f : (place % 2 == 0 ? 1f : -1f) * ((place + 1) / 2);
                slots[i] = new Vector3(side * 1.6f, Mathf.Sin(i * 1.7f) * 0.6f, -Mathf.Abs(side) * 1.9f + Mathf.Cos(i * 2.3f) * 0.5f);
            }
        }

        private void LateUpdate()
        {
            if (birds == null) return;
            float time = Time.time;
            for (int f = 0; f < Flocks; f++)
            {
                // Each flock loops round its own part of the map.
                float speed = 0.035f + f * 0.008f;
                float angle = time * speed + f * 2.1f;
                var centre = new Vector3(size.x * (0.25f + 0.25f * f), 0f, size.z * (0.3f + 0.2f * ((f + 1) % 3)));
                float radiusX = size.x * 0.22f, radiusZ = size.z * 0.18f;
                var position = centre + new Vector3(Mathf.Cos(angle) * radiusX, 22f + 3f * Mathf.Sin(time * 0.2f + f), Mathf.Sin(angle * 1.3f) * radiusZ);
                var ahead = centre + new Vector3(Mathf.Cos(angle + 0.01f) * radiusX, 0f, Mathf.Sin((angle + 0.01f) * 1.3f) * radiusZ);
                var heading = ahead - new Vector3(position.x, 0f, position.z);
                var turn = heading.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(heading.normalized, Vector3.up) : Quaternion.identity;
                for (int b = 0; b < BirdsPerFlock; b++)
                {
                    int i = f * BirdsPerFlock + b;
                    var bird = birds[i];
                    bird.localPosition = position + turn * slots[i];
                    bird.localRotation = turn;
                    // Flap for a while, then glide.
                    float flapping = Mathf.Clamp01(Mathf.Sin(time * 0.5f + i * 0.9f) * 2f);
                    float wing = 1f - flapping * 0.45f * (0.5f + 0.5f * Mathf.Sin(time * 11f + i * 1.3f));
                    bird.localScale = new Vector3(wing, 1f, 1f) * 1.6f;
                }
            }
        }

        private static Mesh BuildMesh()
        {
            // A flat V seen from above: two swept wings meeting at the body, both faces drawn.
            var mesh = new Mesh { name = "Bird" };
            var v = new[]
            {
                new Vector3(0f, 0f, 0.25f), new Vector3(-0.75f, 0.06f, -0.2f), new Vector3(-0.05f, 0f, -0.15f),
                new Vector3(0f, 0f, 0.25f), new Vector3(0.05f, 0f, -0.15f), new Vector3(0.75f, 0.06f, -0.2f)
            };
            mesh.vertices = v;
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 1, 3, 4, 5, 3, 5, 4 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
