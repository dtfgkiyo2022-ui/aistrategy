using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Low-poly looks for the resource points, built in code so they need no asset pack and ship in a player build:
    /// a clump of trees for wood, boulders for stone, dark rock with rusty or golden lumps for ore and gold, and a
    /// berry bush for food. Every point gets its own arrangement from its ID, and loses pieces as it is gathered.
    /// Display only: nothing here is read back by the simulation.
    /// </summary>
    public static class ResourceModels
    {
        /// <summary>How many steps a point shows between full and nearly empty.</summary>
        public const int Levels = 4;

        private static readonly Color Trunk = new Color(0.36f, 0.25f, 0.15f);
        private static readonly Color LeafDark = new Color(0.13f, 0.40f, 0.17f);
        private static readonly Color LeafLight = new Color(0.25f, 0.55f, 0.22f);
        private static readonly Color LeafWarm = new Color(0.42f, 0.58f, 0.20f);
        private static readonly Color RockLight = new Color(0.60f, 0.60f, 0.57f);
        private static readonly Color RockMid = new Color(0.46f, 0.46f, 0.45f);
        private static readonly Color RockDark = new Color(0.30f, 0.29f, 0.32f);
        private static readonly Color Rust = new Color(0.62f, 0.33f, 0.18f);
        private static readonly Color GoldColor = new Color(0.95f, 0.76f, 0.18f);
        private static readonly Color Bush = new Color(0.20f, 0.47f, 0.20f);
        private static readonly Color Berry = new Color(0.80f, 0.16f, 0.22f);

        private static Mesh cone, blob, cylinder;

        /// <summary>The step (1..Levels) for what is left of a point; a nearly empty point still shows one piece.</summary>
        public static int Level(ResourceKind kind, int remaining)
        {
            float full = kind == ResourceKind.Ore ? 400f : 300f;
            return Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(remaining / full) * Levels), 1, Levels);
        }

        /// <summary>
        /// Rebuilds the meshes under root for one resource point at the given step. Called when the point first
        /// appears and when its step changes, not every frame.
        /// </summary>
        public static void Build(GameObject root, ResourceKind kind, uint id, int level)
        {
            Release(root);
            var parts = new Dictionary<Color, List<CombineInstance>>();
            uint seed = id * 2654435761u + (uint)kind * 97u + 1u;
            switch (kind)
            {
                case ResourceKind.Wood: Trees(parts, ref seed, level); break;
                case ResourceKind.Stone: Rocks(parts, ref seed, level, RockLight, RockMid, null); break;
                case ResourceKind.Ore: Rocks(parts, ref seed, level, RockDark, RockMid, Rust); break;
                case ResourceKind.Gold: Rocks(parts, ref seed, level, RockMid, RockDark, GoldColor); break;
                default: BerryBush(parts, ref seed, level); break;
            }
            // One mesh per color for the whole point, so a map full of points stays a few hundred renderers.
            foreach (var pair in parts)
            {
                var mesh = new Mesh { name = kind + " " + id };
                mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                var part = new GameObject("Part");
                part.transform.SetParent(root.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterial = PresentationMaterials.Get(pair.Key);
            }
        }

        /// <summary>Removes the parts under root and frees their meshes (each point owns its combined meshes).</summary>
        public static void Release(GameObject root)
        {
            if (root == null) return;
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i).gameObject;
                var filter = child.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
                child.transform.SetParent(null, false);
                Object.Destroy(child);
            }
        }

        // Wood: up to five trees in a clump, conifers and round-topped ones mixed; gathered points lose trees.
        private static void Trees(Dictionary<Color, List<CombineInstance>> parts, ref uint seed, int level)
        {
            int count = level + 1;
            for (int i = 0; i < 5; i++)
            {
                float angle = (i * 72f + Range(ref seed, -25f, 25f)) * Mathf.Deg2Rad;
                float radius = i == 0 ? Range(ref seed, 0f, 0.4f) : Range(ref seed, 1.0f, 1.7f);
                float height = Range(ref seed, 3.0f, 4.6f);
                bool conifer = Next(ref seed) < 0.6f;
                float turn = Range(ref seed, 0f, 360f);
                var leaf = Next(ref seed) < 0.5f ? LeafDark : Next(ref seed) < 0.6f ? LeafLight : LeafWarm;
                if (i >= count) continue;
                var at = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Add(parts, Trunk, Cylinder, at, turn, new Vector3(0.32f, height * 0.32f, 0.32f));
                if (conifer)
                {
                    Add(parts, leaf, Cone, at + Vector3.up * height * 0.22f, turn, new Vector3(1.45f, height * 0.5f, 1.45f));
                    Add(parts, leaf, Cone, at + Vector3.up * height * 0.5f, turn + 30f, new Vector3(1.05f, height * 0.5f, 1.05f));
                }
                else
                {
                    Add(parts, leaf, Blob, at + Vector3.up * height * 0.58f, turn, new Vector3(1.6f, height * 0.42f, 1.6f));
                    Add(parts, leaf == LeafDark ? LeafLight : LeafDark, Blob, at + new Vector3(0.45f, height * 0.44f, 0.25f), turn + 50f, new Vector3(1.0f, height * 0.28f, 1.0f));
                }
            }
        }

        // Stone, ore and gold: a few boulders; ore and gold carry small lumps of their color on top.
        private static void Rocks(Dictionary<Color, List<CombineInstance>> parts, ref uint seed, int level, Color main, Color second, Color? lumps)
        {
            int count = level + 1;
            for (int i = 0; i < 5; i++)
            {
                float angle = (i * 72f + Range(ref seed, -30f, 30f)) * Mathf.Deg2Rad;
                float radius = i == 0 ? 0f : Range(ref seed, 1.0f, 1.7f);
                float size = i == 0 ? Range(ref seed, 1.6f, 2.0f) : Range(ref seed, 0.8f, 1.3f);
                float squash = Range(ref seed, 0.55f, 0.85f);
                float turn = Range(ref seed, 0f, 360f);
                float lumpTurn = Range(ref seed, 0f, 360f);
                if (i >= count) continue;
                var at = new Vector3(Mathf.Cos(angle) * radius, size * squash * 0.35f, Mathf.Sin(angle) * radius);
                Add(parts, i % 2 == 0 ? main : second, Blob, at, turn, new Vector3(size, size * squash, size));
                if (!lumps.HasValue) continue;
                for (int k = 0; k < 3; k++)
                {
                    float a = (lumpTurn + k * 120f) * Mathf.Deg2Rad;
                    var on = at + new Vector3(Mathf.Cos(a) * size * 0.32f, size * squash * 0.38f, Mathf.Sin(a) * size * 0.32f);
                    Add(parts, lumps.Value, Blob, on, lumpTurn + k * 40f, Vector3.one * size * 0.3f);
                }
            }
        }

        // Food: a berry bush, green mounds dotted with red; gathered bushes lose mounds.
        private static void BerryBush(Dictionary<Color, List<CombineInstance>> parts, ref uint seed, int level)
        {
            int count = level + 1;
            for (int i = 0; i < 5; i++)
            {
                float angle = (i * 72f + Range(ref seed, -30f, 30f)) * Mathf.Deg2Rad;
                float radius = i == 0 ? 0f : Range(ref seed, 0.8f, 1.3f);
                float size = i == 0 ? Range(ref seed, 1.4f, 1.8f) : Range(ref seed, 0.9f, 1.3f);
                float turn = Range(ref seed, 0f, 360f);
                if (i >= count) continue;
                var at = new Vector3(Mathf.Cos(angle) * radius, size * 0.3f, Mathf.Sin(angle) * radius);
                Add(parts, Bush, Blob, at, turn, new Vector3(size, size * 0.75f, size));
                for (int k = 0; k < 4; k++)
                {
                    float a = (turn + k * 90f) * Mathf.Deg2Rad;
                    var on = at + new Vector3(Mathf.Cos(a) * size * 0.36f, size * (0.12f + 0.12f * (k % 2)), Mathf.Sin(a) * size * 0.36f);
                    Add(parts, Berry, Blob, on, 0f, Vector3.one * 0.28f);
                }
            }
        }

        private static void Add(Dictionary<Color, List<CombineInstance>> parts, Color color, Mesh mesh, Vector3 at, float turnDegrees, Vector3 scale)
        {
            if (!parts.TryGetValue(color, out var list)) parts[color] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(at, Quaternion.Euler(0f, turnDegrees, 0f), scale) });
        }

        // A small repeatable generator: the same point looks the same every time it is drawn.
        private static float Next(ref uint seed)
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFFFF) / 16777216f;
        }

        private static float Range(ref uint seed, float min, float max) { return min + (max - min) * Next(ref seed); }

        // The shapes are unit sized (about 1 m across, base at y = 0 for the cone and cylinder, centred for the blob)
        // and flat shaded: every face has its own corners, so the low-poly facets stay visible.
        private static Mesh Cone { get { return cone != null ? cone : (cone = Faceted("Cone", ConeFaces(6))); } }
        private static Mesh Cylinder { get { return cylinder != null ? cylinder : (cylinder = Faceted("Cylinder", CylinderFaces(6))); } }
        private static Mesh Blob { get { return blob != null ? blob : (blob = Faceted("Blob", IcosahedronFaces())); } }

        private static Mesh Faceted(string name, List<Vector3> triangles)
        {
            var mesh = new Mesh { name = name };
            var indices = new int[triangles.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            mesh.SetVertices(triangles);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<Vector3> ConeFaces(int sides)
        {
            var faces = new List<Vector3>();
            var tip = new Vector3(0f, 1f, 0f);
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = Ring(i, sides, 0.5f, 0f), b = Ring(i + 1, sides, 0.5f, 0f);
                faces.Add(tip); faces.Add(b); faces.Add(a);
                faces.Add(Vector3.zero); faces.Add(a); faces.Add(b);
            }
            return faces;
        }

        private static List<Vector3> CylinderFaces(int sides)
        {
            var faces = new List<Vector3>();
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = Ring(i, sides, 0.5f, 0f), b = Ring(i + 1, sides, 0.5f, 0f);
                Vector3 c = Ring(i, sides, 0.4f, 1f), d = Ring(i + 1, sides, 0.4f, 1f);
                faces.Add(a); faces.Add(c); faces.Add(b);
                faces.Add(b); faces.Add(c); faces.Add(d);
                faces.Add(new Vector3(0f, 1f, 0f)); faces.Add(d); faces.Add(c);
            }
            return faces;
        }

        private static Vector3 Ring(int i, int sides, float radius, float y)
        {
            float angle = i * Mathf.PI * 2f / sides;
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        private static List<Vector3> IcosahedronFaces()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var faces = new List<Vector3>();
            foreach (int index in f) faces.Add(v[index].normalized * 0.5f);
            return faces;
        }
    }
}
