using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rts.Presentation
{
    /// <summary>
    /// The forests and mountains drawn with the tree and rock models made for this game (11-09 test play: Ultra did not
    /// look much better; the code-built cones and pyramids were the plainest things on screen). Medium and above only;
    /// Low keeps the code-built shapes. Copies are grouped in square chunks so a chunk off screen is skipped as a whole,
    /// and a far chunk uses the lighter model. Display only.
    /// </summary>
    public sealed class NatureInstances : MonoBehaviour
    {
        private const float ChunkMeters = 32f;
        /// <summary>Chunks whose centre is further than this from the camera use the lighter model.</summary>
        private const float NearMeters = 140f;
        private const int MaxPerCall = 1023;

        public struct Item
        {
            public int Variant;
            public Vector3 Position;
            public float Turn;
            public float Size;
        }

        private sealed class Variant
        {
            public string Name;
            public bool ByHeight;
            public Mesh NearMesh, FarMesh;
            public Material Material;
            public Matrix4x4 NearUnit, FarUnit;
        }

        private sealed class Chunk
        {
            public Bounds Bounds;
            public Vector3 Centre;
            public List<Matrix4x4>[] Near;
            public List<Matrix4x4>[] Far;
        }

        private readonly List<Variant> variants = new List<Variant>();
        private readonly List<Chunk> chunks = new List<Chunk>();
        private readonly Matrix4x4[] batch = new Matrix4x4[MaxPerCall];

        /// <summary>Adds a model kind; returns its index, or -1 when the model is missing (then the caller keeps its shapes).</summary>
        public int AddVariant(string name, bool byHeight)
        {
            var variant = new Variant { Name = name, ByHeight = byHeight };
            if (!ResourceArt.TryGetUnitMesh(name, false, out variant.NearMesh, out variant.Material, out variant.NearUnit, byHeight))
                return -1;
            if (!ResourceArt.TryGetUnitMesh(name, true, out variant.FarMesh, out _, out variant.FarUnit, byHeight))
            {
                variant.FarMesh = variant.NearMesh;
                variant.FarUnit = variant.NearUnit;
            }
            // A calmer copy for the forests: the resource trees' yellow-greens are too loud in a whole forest (11-09).
            var calm = new Material(variant.Material) { name = variant.Material.name + " (forest)", enableInstancing = true };
            calm.SetColor("_BaseColor", byHeight ? new Color(0.66f, 0.76f, 0.62f) : new Color(0.92f, 0.92f, 0.92f));
            variant.Material = calm;
            variants.Add(variant);
            return variants.Count - 1;
        }

        public void SetItems(List<Item> items)
        {
            chunks.Clear();
            var byKey = new Dictionary<long, Chunk>();
            var order = new List<long>();
            foreach (var item in items)
            {
                if (item.Variant < 0 || item.Variant >= variants.Count) continue;
                int cx = Mathf.FloorToInt(item.Position.x / ChunkMeters), cz = Mathf.FloorToInt(item.Position.z / ChunkMeters);
                long key = ((long)cx << 32) | (uint)cz;
                if (!byKey.TryGetValue(key, out var chunk))
                {
                    chunk = new Chunk { Near = NewLists(), Far = NewLists() };
                    var centre = new Vector3((cx + 0.5f) * ChunkMeters, 0f, (cz + 0.5f) * ChunkMeters);
                    chunk.Centre = centre;
                    chunk.Bounds = new Bounds(centre + Vector3.up * 6f, new Vector3(ChunkMeters + 12f, 16f, ChunkMeters + 12f));
                    byKey.Add(key, chunk);
                    order.Add(key);
                }
                var v = variants[item.Variant];
                var place = Matrix4x4.TRS(item.Position, Quaternion.Euler(0f, item.Turn, 0f), Vector3.one * item.Size);
                chunk.Near[item.Variant].Add(place * v.NearUnit);
                chunk.Far[item.Variant].Add(place * v.FarUnit);
            }
            // The order the chunks were first met in (the caller walks the map in rows), never the dictionary's.
            foreach (var key in order) chunks.Add(byKey[key]);
        }

        private List<Matrix4x4>[] NewLists()
        {
            var lists = new List<Matrix4x4>[variants.Count];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<Matrix4x4>();
            return lists;
        }

        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera == null || chunks.Count == 0) return;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var toWorld = transform.localToWorldMatrix;
            Vector3 eye = camera.transform.position;
            for (int c = 0; c < chunks.Count; c++)
            {
                var chunk = chunks[c];
                var worldBounds = TransformBounds(toWorld, chunk.Bounds);
                if (!GeometryUtility.TestPlanesAABB(planes, worldBounds)) continue;
                bool near = (worldBounds.center - eye).magnitude < NearMeters;
                for (int v = 0; v < variants.Count; v++)
                {
                    var list = near ? chunk.Near[v] : chunk.Far[v];
                    if (list.Count == 0) continue;
                    var variant = variants[v];
                    var mesh = near ? variant.NearMesh : variant.FarMesh;
                    var rp = new RenderParams(variant.Material)
                    {
                        worldBounds = worldBounds,
                        shadowCastingMode = ShadowCastingMode.On,
                        receiveShadows = true,
                        layer = gameObject.layer
                    };
                    for (int start = 0; start < list.Count; start += MaxPerCall)
                    {
                        int count = Mathf.Min(MaxPerCall, list.Count - start);
                        for (int i = 0; i < count; i++) batch[i] = toWorld * list[start + i];
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                            Graphics.RenderMeshInstanced(rp, mesh, sub, batch, count);
                    }
                }
            }
        }

        private static Bounds TransformBounds(Matrix4x4 m, Bounds b)
        {
            var centre = m.MultiplyPoint3x4(b.center);
            var e = b.extents;
            var x = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
            var y = m.MultiplyVector(new Vector3(0f, e.y, 0f));
            var z = m.MultiplyVector(new Vector3(0f, 0f, e.z));
            var extents = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(centre, extents * 2f);
        }
    }
}
