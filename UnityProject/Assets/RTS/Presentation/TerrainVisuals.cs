using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rts.Presentation
{
    /// <summary>Five display-only painted surfaces used by the terrain blend shader.</summary>
    public enum TerrainTextureKind { Grass, Soil, Sand, Rock, Forest }

    public struct TerrainSplatWeights
    {
        public float Grass;
        public float Soil;
        public float Sand;
        public float Rock;
        public float Forest;

        public float Sum { get { return Grass + Soil + Sand + Rock + Forest; } }

        public TerrainSplatWeights Normalized()
        {
            float sum = Sum;
            if (sum <= 0.0001f) return new TerrainSplatWeights { Grass = 1f };
            return new TerrainSplatWeights
            {
                Grass = Grass / sum, Soil = Soil / sum, Sand = Sand / sum,
                Rock = Rock / sum, Forest = Forest / sum
            };
        }
    }

    public enum TerrainDecorationKind { Grass, Pebble, Flower, FallenLog, Mushroom }

    public struct TerrainDecorationInstance
    {
        public TerrainDecorationKind Kind;
        public int X;
        public int Z;
        public Vector3 LocalPosition;
        public float Scale;
        public float Rotation;
    }

    /// <summary>
    /// Deterministic display math. It reads TerrainMap only; it never changes the simulation map.
    /// Keeping this part free of GameObjects also makes the three visual EditMode checks inexpensive.
    /// </summary>
    public static class TerrainVisualMath
    {
        public static TerrainSplatWeights GetSplatWeights(TerrainMap map, int x, int z)
        {
            TerrainSplatWeights result = new TerrainSplatWeights();
            float total = 0f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = Mathf.Clamp(x + dx, 0, map.WidthCells - 1);
                    int nz = Mathf.Clamp(z + dz, 0, map.HeightCells - 1);
                    float distance = Mathf.Abs(dx) + Mathf.Abs(dz);
                    float weight = distance == 0f ? 4f : distance == 1f ? 2f : 1f;
                    AddKind(ref result, map, nx, nz, weight);
                    total += weight;
                }

            if (map.KindAt(x, z) == 2)
            {
                // The water mesh covers the centre. Sand underneath and at the edge keeps the river shore warm.
                result.Sand += 1.5f;
                total += 1.5f;
            }
            else if (!map.IsBlocked(x, z) && (HasNeighbourKind(map, x, z, 2) || HasNeighbourKind(map, x, z, 3)))
            {
                // A narrow soil band makes the grass-to-sand and grass-to-rock transitions feel painted, not cut.
                result.Soil += 0.9f;
                total += 0.9f;
            }
            result.Grass /= total;
            result.Soil /= total;
            result.Sand /= total;
            result.Rock /= total;
            result.Forest /= total;
            return result;
        }

        private static bool HasNeighbourKind(TerrainMap map, int x, int z, byte wanted)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= map.WidthCells || nz >= map.HeightCells) continue;
                    if (map.KindAt(nx, nz) == wanted) return true;
                }
            return false;
        }

        private static void AddKind(ref TerrainSplatWeights value, TerrainMap map, int x, int z, float weight)
        {
            byte kind = map.KindAt(x, z);
            if (kind == 1) value.Forest += weight;
            else if (kind == 2) value.Sand += weight;
            else if (kind == 3 || map.IsBlocked(x, z)) value.Rock += weight;
            else value.Grass += weight;
        }

        public static List<TerrainDecorationInstance> GetDecorations(TerrainMap map, int seed, int maximum)
        {
            var result = new List<TerrainDecorationInstance>(Mathf.Max(0, maximum));
            if (maximum <= 0) return result;
            for (int z = 0; z < map.HeightCells && result.Count < maximum; z++)
                for (int x = 0; x < map.WidthCells && result.Count < maximum; x++)
                {
                    byte kind = map.KindAt(x, z);
                    if (kind == 2 || kind == 3 || map.IsBlocked(x, z)) continue;
                    uint random = Hash((uint)seed, (uint)x, (uint)z);
                    float chance = ToUnit(random);
                    bool forestEdge = kind == 1 && HasNonForestNeighbour(map, x, z);
                    TerrainDecorationKind decoration;
                    if (forestEdge && chance < 0.10f) decoration = TerrainDecorationKind.FallenLog;
                    else if (forestEdge && chance < 0.18f) decoration = TerrainDecorationKind.Mushroom;
                    else if (kind == 1 || chance > 0.72f) continue;
                    else if (chance < 0.13f) decoration = TerrainDecorationKind.Flower;
                    else if (chance < 0.42f) decoration = TerrainDecorationKind.Grass;
                    else decoration = TerrainDecorationKind.Pebble;
                    result.Add(new TerrainDecorationInstance
                    {
                        Kind = decoration, X = x, Z = z,
                        LocalPosition = new Vector3(x + 0.18f + ToUnit(random >> 8) * 0.64f, 0f,
                            z + 0.18f + ToUnit(random >> 16) * 0.64f),
                        Scale = 0.45f + ToUnit(random >> 24) * 0.55f,
                        Rotation = ToUnit(random ^ 0xA341316Cu) * 360f
                    });
                }
            return result;
        }

        private static bool HasNonForestNeighbour(TerrainMap map, int x, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= map.WidthCells || nz >= map.HeightCells) return true;
                    if (map.KindAt(nx, nz) != 1) return true;
                }
            return false;
        }

        public static uint Hash(uint seed, uint x, uint z)
        {
            uint value = seed ^ (x * 0x9E3779B9u) ^ (z * 0x85EBCA6Bu);
            value ^= value >> 16; value *= 0x7FEB352Du;
            value ^= value >> 15; value *= 0x846CA68Bu;
            return value ^ (value >> 16);
        }

        public static float ToUnit(uint value) { return (value & 0x00FFFFFFu) / 16777216f; }
    }

    /// <summary>Procedural, tileable 256px painterly textures. The editor menu calls the same function.</summary>
    public static class TerrainTextureGenerator
    {
        public const int DefaultSize = 256;

        public static Texture2D Create(TerrainTextureKind kind, int size = DefaultSize)
        {
            size = Mathf.Max(2, size);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RTS_Terrain_" + kind,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)(size - 1), v = y / (float)(size - 1);
                    pixels[y * size + x] = Paint(kind, u, v);
                }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        public static Color Paint(TerrainTextureKind kind, float u, float v)
        {
            float n = 0.5f + 0.22f * Mathf.Sin(u * Mathf.PI * 8f) * Mathf.Cos(v * Mathf.PI * 6f)
                + 0.11f * Mathf.Sin((u + v) * Mathf.PI * 18f);
            float fleck = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 42f + v * Mathf.PI * 35f);
            Color dark, light;
            switch (kind)
            {
                case TerrainTextureKind.Soil: dark = new Color(0.34f, 0.20f, 0.12f); light = new Color(0.58f, 0.38f, 0.20f); break;
                case TerrainTextureKind.Sand: dark = new Color(0.72f, 0.58f, 0.34f); light = new Color(0.93f, 0.80f, 0.53f); break;
                case TerrainTextureKind.Rock: dark = new Color(0.25f, 0.27f, 0.29f); light = new Color(0.57f, 0.56f, 0.50f); break;
                case TerrainTextureKind.Forest: dark = new Color(0.10f, 0.25f, 0.14f); light = new Color(0.29f, 0.47f, 0.22f); break;
                default: dark = new Color(0.22f, 0.40f, 0.18f); light = new Color(0.49f, 0.65f, 0.29f); break;
            }
            Color color = Color.Lerp(dark, light, Mathf.Clamp01(n));
            return Color.Lerp(color, Color.Lerp(dark, light, fleck), 0.10f);
        }
    }

    public struct TerrainVisualLimits
    {
        public int ForestTrees;
        public int MountainRocks;
        public int Decorations;
        public int Seed;

        public static TerrainVisualLimits Defaults
        {
            get { return new TerrainVisualLimits { ForestTrees = 600, MountainRocks = 300, Decorations = 360, Seed = 41721 }; }
        }
    }

    /// <summary>Builds one terrain surface, one water mesh and a few combined low-poly meshes.</summary>
    public static class TerrainSurfaceBuilder
    {
        public static GameObject Build(Transform parent, TerrainMap map, TerrainVisualLimits limits)
        {
            var root = new GameObject("Terrain");
            root.transform.SetParent(parent, false);
            float cell = map.CellSizeMeters;
            float width = map.WidthCells * cell, height = map.HeightCells * cell;

            Texture2D grass = TerrainTextureGenerator.Create(TerrainTextureKind.Grass);
            Texture2D soil = TerrainTextureGenerator.Create(TerrainTextureKind.Soil);
            Texture2D sand = TerrainTextureGenerator.Create(TerrainTextureKind.Sand);
            Texture2D rock = TerrainTextureGenerator.Create(TerrainTextureKind.Rock);
            Texture2D forest = TerrainTextureGenerator.Create(TerrainTextureKind.Forest);
            Texture2D splatA = CreateSplat(map, false);
            Texture2D splatB = CreateSplat(map, true);
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "TerrainSurface";
            plane.transform.SetParent(root.transform, false);
            plane.transform.position = new Vector3(width * 0.5f, 0.02f, height * 0.5f);
            plane.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            plane.transform.localScale = new Vector3(width, height, 1f);
            RemoveCollider(plane);
            var terrainMaterial = CreateTerrainMaterial(grass, soil, sand, rock, forest, splatA, splatB);
            plane.GetComponent<Renderer>().sharedMaterial = terrainMaterial;

            BuildWater(root.transform, map, cell);
            BuildNature(root.transform, map, cell, limits);
            return root;
        }

        private static Material CreateTerrainMaterial(Texture2D grass, Texture2D soil, Texture2D sand,
            Texture2D rock, Texture2D forest, Texture2D splatA, Texture2D splatB)
        {
            var shader = Shader.Find("Rts/TerrainBlend");
            var material = shader == null ? PresentationMaterials.NewUnlitTextured(grass) : new Material(shader);
            if (shader == null) return material;
            material.SetTexture("_GrassTex", grass); material.SetTexture("_SoilTex", soil);
            material.SetTexture("_SandTex", sand); material.SetTexture("_RockTex", rock);
            material.SetTexture("_ForestTex", forest); material.SetTexture("_SplatA", splatA);
            material.SetTexture("_SplatB", splatB); material.SetFloat("_TextureScale", 0.55f);
            return material;
        }

        private static Texture2D CreateSplat(TerrainMap map, bool forestOnly)
        {
            var texture = new Texture2D(map.WidthCells, map.HeightCells, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[map.WidthCells * map.HeightCells];
            for (int z = 0; z < map.HeightCells; z++)
                for (int x = 0; x < map.WidthCells; x++)
                {
                    TerrainSplatWeights w = TerrainVisualMath.GetSplatWeights(map, x, z);
                    pixels[z * map.WidthCells + x] = forestOnly
                        ? new Color(w.Forest, 0f, 0f, 0f)
                        : new Color(w.Grass, w.Soil, w.Sand, w.Rock);
                }
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static void BuildWater(Transform parent, TerrainMap map, float cell)
        {
            var mesh = new Mesh { name = "RiverWater" };
            var vertices = new List<Vector3>(); var colors = new List<Color>(); var triangles = new List<int>();
            for (int z = 0; z < map.HeightCells; z++)
                for (int x = 0; x < map.WidthCells; x++)
                    if (map.KindAt(x, z) == 2) AddWaterQuad(vertices, colors, triangles, x * cell, z * cell, cell, IsShallow(map, x, z));
            if (vertices.Count == 0) return;
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            var water = new GameObject("RiverWater"); water.transform.SetParent(parent, false);
            var filter = water.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            var renderer = water.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Rts/TerrainWater");
            renderer.sharedMaterial = shader == null ? PresentationMaterials.GetUnlit(new Color(0.18f, 0.42f, 0.72f)) : new Material(shader);
        }

        private static void AddWaterQuad(List<Vector3> vertices, List<Color> colors, List<int> triangles,
            float x, float z, float cell, bool shallow)
        {
            int start = vertices.Count; Color color = shallow ? new Color(0.38f, 0.72f, 0.86f) : new Color(0.12f, 0.34f, 0.70f);
            vertices.Add(new Vector3(x, 0.075f, z)); vertices.Add(new Vector3(x + cell, 0.075f, z));
            vertices.Add(new Vector3(x + cell, 0.075f, z + cell)); vertices.Add(new Vector3(x, 0.075f, z + cell));
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
        }

        private static bool IsShallow(TerrainMap map, int x, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= map.WidthCells || nz >= map.HeightCells) return true;
                    if (map.KindAt(nx, nz) != 2) return true;
                }
            return false;
        }

        private static void BuildNature(Transform parent, TerrainMap map, float cell, TerrainVisualLimits limits)
        {
            var forest = new MeshParts(); var mountain = new MeshParts(); var decor = new MeshParts();
            int trees = 0, rocks = 0;
            for (int z = 0; z < map.HeightCells; z++)
                for (int x = 0; x < map.WidthCells; x++)
                {
                    uint random = TerrainVisualMath.Hash((uint)limits.Seed, (uint)x, (uint)z);
                    float jitterX = TerrainVisualMath.ToUnit(random) * 0.65f + 0.18f;
                    float jitterZ = TerrainVisualMath.ToUnit(random >> 8) * 0.65f + 0.18f;
                    Vector3 at = new Vector3((x + jitterX) * cell, 0f, (z + jitterZ) * cell);
                    byte kind = map.KindAt(x, z);
                    if (kind == 1 && trees < Mathf.Max(0, limits.ForestTrees))
                    {
                        AddTree(forest, at, 0.75f + TerrainVisualMath.ToUnit(random >> 16) * 0.5f, random);
                        trees++;
                    }
                    if ((kind == 3 || map.IsBlocked(x, z)) && rocks < Mathf.Max(0, limits.MountainRocks))
                    {
                        AddRock(mountain, at, 0.65f + TerrainVisualMath.ToUnit(random >> 16) * 0.7f, random);
                        rocks++;
                    }
                }
            foreach (var item in TerrainVisualMath.GetDecorations(map, limits.Seed, Mathf.Max(0, limits.Decorations)))
            {
                Vector3 at = new Vector3(item.LocalPosition.x * cell, 0.04f, item.LocalPosition.z * cell);
                AddDecoration(decor, item.Kind, at, cell * item.Scale, item.Rotation);
            }
            CreateNatureObject(parent, "ForestTrees", forest, new Color(0.55f, 0.80f, 0.42f), 0.05f);
            CreateNatureObject(parent, "MountainRocks", mountain, Color.white, 0f);
            CreateNatureObject(parent, "TerrainDecorations", decor, Color.white, 0.025f);
        }

        private static void AddTree(MeshParts mesh, Vector3 at, float scale, uint random)
        {
            Color trunk = new Color(0.31f, 0.19f, 0.11f), dark = new Color(0.10f, 0.32f, 0.14f);
            Color light = new Color(0.28f, 0.55f, 0.22f);
            mesh.AddCylinder(at + Vector3.up * (1.0f * scale), 0.22f * scale, 2.0f * scale, trunk);
            mesh.AddCone(at + Vector3.up * (2.1f * scale), 0.95f * scale, 1.35f * scale, dark);
            mesh.AddCone(at + Vector3.up * (2.85f * scale), 0.72f * scale, 1.15f * scale, light);
            mesh.AddCone(at + Vector3.up * (3.45f * scale), 0.46f * scale, 0.85f * scale,
                TerrainVisualMath.ToUnit(random) > 0.5f ? dark : light);
        }

        private static void AddRock(MeshParts mesh, Vector3 at, float scale, uint random)
        {
            Color dark = new Color(0.23f, 0.24f, 0.25f), light = new Color(0.58f, 0.56f, 0.49f);
            mesh.AddRock(at + Vector3.up * (0.42f * scale), scale, TerrainVisualMath.ToUnit(random) > 0.5f ? light : dark);
        }

        private static void AddDecoration(MeshParts mesh, TerrainDecorationKind kind, Vector3 at, float scale, float rotation)
        {
            switch (kind)
            {
                case TerrainDecorationKind.Grass: mesh.AddCone(at + Vector3.up * (scale * 0.32f), scale * 0.10f, scale * 0.7f, new Color(0.30f, 0.58f, 0.22f)); break;
                case TerrainDecorationKind.Flower:
                    mesh.AddCylinder(at + Vector3.up * (scale * 0.22f), scale * 0.035f, scale * 0.45f, new Color(0.23f, 0.46f, 0.18f));
                    mesh.AddRock(at + Vector3.up * (scale * 0.48f), scale * 0.16f, new Color(0.92f, 0.48f, 0.60f)); break;
                case TerrainDecorationKind.Pebble: mesh.AddRock(at + Vector3.up * (scale * 0.16f), scale * 0.28f, new Color(0.45f, 0.43f, 0.39f)); break;
                case TerrainDecorationKind.FallenLog: mesh.AddCylinder(at + Vector3.up * (scale * 0.22f), scale * 0.16f, scale * 1.2f, new Color(0.30f, 0.16f, 0.08f), rotation); break;
                default:
                    mesh.AddCylinder(at + Vector3.up * (scale * 0.20f), scale * 0.12f, scale * 0.35f, new Color(0.86f, 0.76f, 0.64f));
                    mesh.AddCone(at + Vector3.up * (scale * 0.52f), scale * 0.25f, scale * 0.30f, new Color(0.72f, 0.22f, 0.30f)); break;
            }
        }

        private static void CreateNatureObject(Transform parent, string name, MeshParts parts, Color color, float wind)
        {
            if (parts.Vertices.Count == 0) return;
            var mesh = new Mesh { name = name };
            if (parts.Vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(parts.Vertices); mesh.SetColors(parts.Colors); mesh.SetTriangles(parts.Triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Rts/NatureWind");
            var material = shader == null ? PresentationMaterials.Get(color) : new Material(shader);
            if (shader != null) { material.SetColor("_BaseColor", color); material.SetFloat("_WindAmount", wind); }
            renderer.sharedMaterial = material;
        }

        private sealed class MeshParts
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int> Triangles = new List<int>();
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                int index = Vertices.Count; Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
                Colors.Add(color); Colors.Add(color); Colors.Add(color);
                Triangles.Add(index); Triangles.Add(index + 1); Triangles.Add(index + 2);
            }
            public void AddCone(Vector3 at, float radius, float height, Color color) { AddCone(at, radius, height, color, 6); }
            public void AddCone(Vector3 at, float radius, float height, Color color, int sides)
            {
                Vector3 tip = at + Vector3.up * height;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 p0 = at + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                    Vector3 p1 = at + new Vector3(Mathf.Cos(b) * radius, 0f, Mathf.Sin(b) * radius);
                    Triangle(tip, p0, p1, color); Triangle(at, p1, p0, color * 0.72f);
                }
            }
            public void AddCylinder(Vector3 at, float radius, float height, Color color, float rotation = 0f)
            {
                const int sides = 6; float turn = rotation * Mathf.Deg2Rad;
                for (int i = 0; i < sides; i++)
                {
                    float a = turn + i * Mathf.PI * 2f / sides, b = turn + (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 p0 = at + new Vector3(Mathf.Cos(a) * radius, -height * 0.5f, Mathf.Sin(a) * radius);
                    Vector3 p1 = at + new Vector3(Mathf.Cos(b) * radius, -height * 0.5f, Mathf.Sin(b) * radius);
                    Vector3 p2 = p0 + Vector3.up * height, p3 = p1 + Vector3.up * height;
                    Triangle(p0, p2, p1, color * 0.82f); Triangle(p1, p2, p3, color); Triangle(p2, p0, p3, color * 1.08f);
                }
            }
            public void AddRock(Vector3 at, float scale, Color color)
            {
                Vector3[] v = { at + new Vector3(-scale, -scale * 0.45f, -scale * 0.7f), at + new Vector3(scale, -scale * 0.45f, -scale * 0.6f),
                    at + new Vector3(scale * 0.8f, -scale * 0.45f, scale * 0.7f), at + new Vector3(-scale * 0.8f, -scale * 0.45f, scale * 0.65f),
                    at + new Vector3(0f, scale * 0.7f, 0f) };
                Triangle(v[0], v[1], v[4], color * 0.72f); Triangle(v[1], v[2], v[4], color); Triangle(v[2], v[3], v[4], color * 0.84f); Triangle(v[3], v[0], v[4], color * 0.62f);
                Triangle(v[3], v[2], v[1], color * 0.48f); Triangle(v[1], v[0], v[3], color * 0.48f);
            }
        }

        private static void RemoveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
        }
    }
}
