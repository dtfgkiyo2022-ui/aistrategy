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

        /// <summary>
        /// Up to <paramref name="maximum"/> decorations spread evenly over the whole map (not the first rows), with
        /// several slots per cell when the count is larger than the open ground. Seed-stable; display only.
        /// </summary>
        public static List<TerrainDecorationInstance> GetDecorations(TerrainMap map, int seed, int maximum)
        {
            var result = new List<TerrainDecorationInstance>(Mathf.Max(0, maximum));
            if (maximum <= 0) return result;
            int open = 0;
            for (int z = 0; z < map.HeightCells; z++)
                for (int x = 0; x < map.WidthCells; x++)
                    if (IsDecorationGround(map, x, z)) open++;
            if (open == 0) return result;
            int slots = Mathf.Clamp(Mathf.CeilToInt(maximum / (open * 0.6f)), 1, 12);
            int candidates = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                float keep = pass == 0 ? 1f : Mathf.Min(1f, maximum / (float)Mathf.Max(1, candidates));
                for (int z = 0; z < map.HeightCells; z++)
                    for (int x = 0; x < map.WidthCells; x++)
                    {
                        if (!IsDecorationGround(map, x, z)) continue;
                        for (int slot = 0; slot < slots; slot++)
                        {
                            uint random = Hash((uint)seed + (uint)slot * 7919u, (uint)x, (uint)z);
                            TerrainDecorationKind decoration;
                            if (!PickDecoration(map, x, z, random, out decoration)) continue;
                            if (pass == 0) { candidates++; continue; }
                            if (result.Count >= maximum || Unit(random, 1u) >= keep) continue;
                            result.Add(new TerrainDecorationInstance
                            {
                                Kind = decoration, X = x, Z = z,
                                LocalPosition = new Vector3(x + 0.05f + Unit(random, 2u) * 0.90f, 0f,
                                    z + 0.05f + Unit(random, 3u) * 0.90f),
                                Scale = 0.45f + Unit(random, 4u) * 0.55f,
                                Rotation = Unit(random, 5u) * 360f
                            });
                        }
                    }
            }
            return result;
        }

        private static bool IsDecorationGround(TerrainMap map, int x, int z)
        {
            byte kind = map.KindAt(x, z);
            return kind != 2 && kind != 3 && !map.IsBlocked(x, z);
        }

        private static bool PickDecoration(TerrainMap map, int x, int z, uint random, out TerrainDecorationKind decoration)
        {
            decoration = TerrainDecorationKind.Grass;
            float chance = ToUnit(random);
            byte kind = map.KindAt(x, z);
            bool forestEdge = kind == 1 && HasNonForestNeighbour(map, x, z);
            if (forestEdge && chance < 0.06f) decoration = TerrainDecorationKind.FallenLog;
            else if (forestEdge && chance < 0.12f) decoration = TerrainDecorationKind.Mushroom;
            else if (kind == 1) return chance < 0.40f; // tufts of grass under the trees
            else if (chance > 0.80f) return false;
            else if (chance < 0.05f) decoration = TerrainDecorationKind.Flower;
            else if (chance < 0.075f) decoration = TerrainDecorationKind.Pebble;
            return true;
        }

        /// <summary>Picks about <paramref name="limit"/> of the cells that match, spread over the whole map.</summary>
        public static float SpreadChance(int limit, int matching)
        {
            if (limit <= 0 || matching <= 0) return 0f;
            return Mathf.Min(1f, limit / (float)matching);
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

        /// <summary>
        /// An independent 0..1 value per salt. Shifting one hash (random &gt;&gt; 16 and so on) left too few bits:
        /// every decoration sat on the same line of its cell and had the same size.
        /// </summary>
        public static float Unit(uint random, uint salt) { return ToUnit(Hash(random, salt, salt * 0x68E31DA4u)); }
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
        /// <summary>Metres covered by one repeat of a painted ground tile (docs/art-direction.md).</summary>
        private const float PaintedTileMeters = 8f;

        /// <summary>The painted ground tile from Art/Terrain/Resources/TerrainTextures, or null when it is absent.</summary>
        private static Texture2D PaintedTexture(TerrainTextureKind kind)
        {
            // High and Ultra: the 1024 px tiles (11-09); Low and Medium keep the 512 px ones.
            Texture2D texture = null;
            if (Application.isPlaying && UseHdGround()) texture = Resources.Load<Texture2D>("TerrainTexturesHD/" + kind);
            if (texture == null) texture = Resources.Load<Texture2D>("TerrainTextures/" + kind);
            if (texture != null) texture.wrapMode = TextureWrapMode.Repeat;
            return texture;
        }

        private static Texture2D SurfaceTexture(TerrainTextureKind kind)
            => PaintedTexture(kind) ?? TerrainTextureGenerator.Create(kind);

        public static GameObject Build(Transform parent, TerrainMap map, TerrainVisualLimits limits)
        {
            var root = new GameObject("Terrain");
            root.transform.SetParent(parent, false);
            float cell = map.CellSizeMeters;
            float width = map.WidthCells * cell, height = map.HeightCells * cell;

            bool painted = PaintedTexture(TerrainTextureKind.Grass) != null;
            Texture2D grass = SurfaceTexture(TerrainTextureKind.Grass);
            Texture2D soil = SurfaceTexture(TerrainTextureKind.Soil);
            Texture2D sand = SurfaceTexture(TerrainTextureKind.Sand);
            Texture2D rock = SurfaceTexture(TerrainTextureKind.Rock);
            Texture2D forest = SurfaceTexture(TerrainTextureKind.Forest);
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
            // The shader repeats the tile (_TextureScale * 24) times across the map. A painted tile shows grass blades
            // and pebbles, so it is repeated every PaintedTileMeters instead of being stretched over the whole map.
            if (painted) terrainMaterial.SetFloat("_TextureScale", width / (PaintedTileMeters * 24f));
            // One blend-map pixel per cell: the shader wobbles the edges by about a cell, in UV units.
            terrainMaterial.SetVector("_SplatTexel", new Vector4(1f / map.WidthCells, 1f / map.HeightCells, 0f, 0f));
            terrainMaterial.SetVector("_MapMeters", new Vector4(width, height, 0f, 0f));
            terrainMaterial.SetFloat("_ReliefTexel", 1.5f / Mathf.Max(64, grass.width));
            plane.GetComponent<Renderer>().sharedMaterial = terrainMaterial;
            // Packed earth round buildings and walked paths (Medium and above, 11-09).
            if (Application.isPlaying) root.AddComponent<GroundWear>().Init(map, terrainMaterial);

            BuildOutland(root.transform, grass, width, height);
            BuildWater(root.transform, map, cell);
            BuildNature(root.transform, map, cell, limits);
            // Dense grass near the camera on Medium and above (11-09). It decides each frame whether to draw anything.
            if (Application.isPlaying) root.AddComponent<NearGrassField>().Init(map);
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

        /// <summary>
        /// 11-10 (3rd round): ground beyond the map edge, a frame of four quads 600 m wide round the map, so the edge
        /// is not a straight line into the empty camera colour. Display only.
        /// </summary>
        private static void BuildOutland(Transform parent, Texture2D grass, float width, float height)
        {
            var shader = Shader.Find("Rts/Outland");
            if (shader == null) return;
            const float margin = 600f, y = 0f;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            void Rect(float x0, float z0, float x1, float z1)
            {
                int start = vertices.Count;
                vertices.Add(new Vector3(x0, y, z0)); vertices.Add(new Vector3(x1, y, z0));
                vertices.Add(new Vector3(x1, y, z1)); vertices.Add(new Vector3(x0, y, z1));
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
            }
            Rect(-margin, -margin, width + margin, 0f);
            Rect(-margin, height, width + margin, height + margin);
            Rect(-margin, 0f, 0f, height);
            Rect(width, 0f, width + margin, height);
            var mesh = new Mesh { name = "Outland" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            var outland = new GameObject("Outland"); outland.transform.SetParent(parent, false);
            outland.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(shader);
            material.SetTexture("_GrassTex", grass);
            material.SetFloat("_TileMeters", PaintedTileMeters);
            material.SetVector("_MapMeters", new Vector4(width, height, 0f, 0f));
            var renderer = outland.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void BuildWater(Transform parent, TerrainMap map, float cell)
        {
            // 11-10 (3rd round): the quads join the cell centres instead of following the cell edges. Each corner says
            // whether its cell is water (alpha) and how far it is from the bank (red); the shader cuts the surface where
            // the blended value crosses one half, so the bank runs as a rounded, slightly ragged line instead of 2 m
            // steps, and draws a band of wet earth just outside it. Outside the map a cell copies the nearest one, so a
            // river still runs off the edge.
            int w = map.WidthCells, h = map.HeightCells;
            var near = new bool[(w + 1) * (h + 1)];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    if (map.KindAt(x, z) != 2) continue;
                    // The four quads that have this cell centre as a corner (quad q joins centres q and q + 1).
                    for (int dz = -1; dz <= 0; dz++)
                        for (int dx = -1; dx <= 0; dx++)
                            near[(z + dz + 1) * (w + 1) + (x + dx + 1)] = true;
                }
            var mesh = new Mesh { name = "RiverWater" };
            var vertices = new List<Vector3>(); var colors = new List<Color>(); var triangles = new List<int>();
            var cornerCache = new Dictionary<int, Color>();
            for (int qz = -1; qz < h; qz++)
                for (int qx = -1; qx < w; qx++)
                    if (near[(qz + 1) * (w + 1) + (qx + 1)])
                        AddWaterQuad(vertices, colors, triangles, map, qx, qz, cell, cornerCache);
            if (vertices.Count == 0) return;
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            var water = new GameObject("RiverWater"); water.transform.SetParent(parent, false);
            var filter = water.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            var renderer = water.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Rts/TerrainWater");
            renderer.sharedMaterial = shader == null ? PresentationMaterials.GetUnlit(new Color(0.18f, 0.42f, 0.72f)) : new Material(shader);
        }

        /// <summary>One quad between the centres of cells (qx, qz) and (qx + 1, qz + 1).</summary>
        private static void AddWaterQuad(List<Vector3> vertices, List<Color> colors, List<int> triangles,
            TerrainMap map, int qx, int qz, float cell, Dictionary<int, Color> cornerCache)
        {
            int start = vertices.Count;
            float x = (qx + 0.5f) * cell, z = (qz + 0.5f) * cell;
            vertices.Add(new Vector3(x, 0.075f, z)); vertices.Add(new Vector3(x + cell, 0.075f, z));
            vertices.Add(new Vector3(x + cell, 0.075f, z + cell)); vertices.Add(new Vector3(x, 0.075f, z + cell));
            colors.Add(WaterCorner(map, qx, qz, cornerCache)); colors.Add(WaterCorner(map, qx + 1, qz, cornerCache));
            colors.Add(WaterCorner(map, qx + 1, qz + 1, cornerCache)); colors.Add(WaterCorner(map, qx, qz + 1, cornerCache));
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
        }

        private static bool IsWaterCell(TerrainMap map, int x, int z)
            => map.KindAt(Mathf.Clamp(x, 0, map.WidthCells - 1), Mathf.Clamp(z, 0, map.HeightCells - 1)) == 2;

        /// <summary>
        /// The corner at the centre of cell (cx, cz): alpha 1 for water and 0 for land; red is the depth, 0 next to the
        /// bank, then 0.3, 0.6 and 1 two, three and four cells in (land within that ring).
        /// </summary>
        private static Color WaterCorner(TerrainMap map, int cx, int cz, Dictionary<int, Color> cache)
        {
            int key = (cz + 1) * (map.WidthCells + 2) + (cx + 1);
            if (cache.TryGetValue(key, out var known)) return known;
            var color = new Color(0f, 0f, 0f, 0f);
            if (IsWaterCell(map, cx, cz))
            {
                float depth = 1f;
                for (int ring = 1; ring <= 3 && depth == 1f; ring++)
                    for (int dz = -ring; dz <= ring && depth == 1f; dz++)
                        for (int dx = -ring; dx <= ring; dx++)
                            if (!IsWaterCell(map, cx + dx, cz + dz)) { depth = ring == 1 ? 0f : ring == 2 ? 0.3f : 0.6f; break; }
                color = new Color(depth, 0f, 0f, 1f);
            }
            cache[key] = color;
            return color;
        }

        private static void BuildNature(Transform parent, TerrainMap map, float cell, TerrainVisualLimits limits)
        {
            var forest = new MeshParts(); var mountain = new MeshParts(); var decor = new MeshParts();
            int forestCells = 0, rockCells = 0;
            for (int z = 0; z < map.HeightCells; z++)
                for (int x = 0; x < map.WidthCells; x++)
                {
                    byte kind = map.KindAt(x, z);
                    if (kind == 1) forestCells++;
                    if (kind != 2 && (kind == 3 || map.IsBlocked(x, z))) rockCells++;
                }
            // Spread the tree and rock budget over every forest and mountain, not just the first rows of the map.
            int treesPerCell = Mathf.Clamp(Mathf.CeilToInt(limits.ForestTrees / (float)Mathf.Max(1, forestCells)), 1, 3);
            float treeChance = TerrainVisualMath.SpreadChance(limits.ForestTrees, forestCells * treesPerCell);
            float rockChance = TerrainVisualMath.SpreadChance(limits.MountainRocks, rockCells);
            int trees = 0, rocks = 0;
            // Medium and above: the tree and rock models made for this game instead of the code-built shapes (11-09).
            NatureInstances models = null;
            var modelItems = new List<NatureInstances.Item>();
            int[] treeVariants = null, rockVariants = null;
            if (Application.isPlaying && UseNatureModels())
            {
                var holder = new GameObject("NatureModels");
                holder.transform.SetParent(parent, false);
                models = holder.AddComponent<NatureInstances>();
                treeVariants = Variants(models, true, "res_tree_pine", "res_tree_oak", "res_tree_pine", "res_tree_birch", "res_tree_pine");
                rockVariants = Variants(models, false, "res_stone_a", "res_stone_b");
            }
            for (int z = 0; z < map.HeightCells; z++)
                for (int x = 0; x < map.WidthCells; x++)
                {
                    byte kind = map.KindAt(x, z);
                    for (int slot = 0; slot < treesPerCell; slot++)
                    {
                        uint random = TerrainVisualMath.Hash((uint)limits.Seed + (uint)slot * 104729u, (uint)x, (uint)z);
                        float jitterX = TerrainVisualMath.Unit(random, 2u) * 0.65f + 0.18f;
                        float jitterZ = TerrainVisualMath.Unit(random, 3u) * 0.65f + 0.18f;
                        Vector3 at = new Vector3((x + jitterX) * cell, 0f, (z + jitterZ) * cell);
                        float pick = TerrainVisualMath.Unit(random, 1u);
                        if (kind == 1 && trees < limits.ForestTrees && pick < treeChance)
                        {
                            float scale = 0.70f + TerrainVisualMath.Unit(random, 4u) * 0.65f;
                            // 11-10: the forest's edge steps down in young trees instead of ending in a wall.
                            int openX = 0, openZ = 0;
                            bool edge = treeVariants != null && ForestEdge(map, x, z, out openX, out openZ);
                            if (edge) scale *= 0.78f;
                            if (treeVariants != null)
                            {
                                modelItems.Add(new NatureInstances.Item
                                {
                                    Variant = treeVariants[(int)(TerrainVisualMath.Unit(random, 6u) * treeVariants.Length) % treeVariants.Length],
                                    Position = at, Turn = TerrainVisualMath.Unit(random, 7u) * 360f, Size = 5.2f * scale
                                });
                                if (edge && slot == 0 && TerrainVisualMath.Unit(random, 8u) < 0.7f)
                                {
                                    float young = 0.32f + TerrainVisualMath.Unit(random, 9u) * 0.22f;
                                    var outside = at + new Vector3(openX, 0f, openZ) * (cell * (0.45f + TerrainVisualMath.Unit(random, 10u) * 0.35f));
                                    modelItems.Add(new NatureInstances.Item
                                    {
                                        Variant = treeVariants[(int)(TerrainVisualMath.Unit(random, 11u) * treeVariants.Length) % treeVariants.Length],
                                        Position = outside, Turn = TerrainVisualMath.Unit(random, 12u) * 360f, Size = 5.2f * young
                                    });
                                }
                            }
                            else AddTree(forest, at, scale, random);
                            trees++;
                        }
                        if (slot == 0 && kind != 2 && (kind == 3 || map.IsBlocked(x, z)) && rocks < limits.MountainRocks && pick < rockChance)
                        {
                            float scale = 0.65f + TerrainVisualMath.Unit(random, 4u) * 0.7f;
                            if (rockVariants != null)
                                modelItems.Add(new NatureInstances.Item
                                {
                                    Variant = rockVariants[(int)(TerrainVisualMath.Unit(random, 6u) * rockVariants.Length) % rockVariants.Length],
                                    Position = at, Turn = TerrainVisualMath.Unit(random, 7u) * 360f, Size = 2.6f * scale
                                });
                            else AddRock(mountain, at, scale, random);
                            rocks++;
                        }
                    }
                }
            foreach (var item in TerrainVisualMath.GetDecorations(map, limits.Seed, Mathf.Max(0, limits.Decorations)))
            {
                Vector3 at = new Vector3(item.LocalPosition.x * cell, 0.04f, item.LocalPosition.z * cell);
                AddDecoration(decor, item.Kind, at, cell * item.Scale, item.Rotation);
            }
            if (models != null) models.SetItems(modelItems);
            if (Application.isPlaying)
            {
                // A few flocks of birds over the map (11-10).
                var flock = new GameObject("AmbientBirds");
                flock.transform.SetParent(parent, false);
                flock.AddComponent<AmbientBirds>().Init(new Vector3(map.WidthCells * cell, 0f, map.HeightCells * cell));
            }
            CreateNatureObject(parent, "ForestTrees", forest, new Color(0.55f, 0.80f, 0.42f), 0.05f);
            CreateNatureObject(parent, "MountainRocks", mountain, Color.white, 0f);
            CreateNatureObject(parent, "TerrainDecorations", decor, Color.white, 0.025f);
        }

        /// <summary>A forest cell next to open ground (not water or mountain); out: the direction to that ground.</summary>
        private static bool ForestEdge(TerrainMap map, int x, int z, out int openX, out int openZ)
        {
            openX = 0; openZ = 0;
            for (int i = 0; i < 4; i++)
            {
                int dx = i == 0 ? 1 : i == 1 ? -1 : 0, dz = i == 2 ? 1 : i == 3 ? -1 : 0;
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= map.WidthCells || nz >= map.HeightCells) continue;
                byte kind = map.KindAt(nx, nz);
                if (kind == 0 && !map.IsBlocked(nx, nz)) { openX = dx; openZ = dz; return true; }
            }
            return false;
        }

        private static bool UseHdGround()
        {
            var value = GraphicsQuality.Current;
            var level = GraphicsQualityRules.Detect(value);
            if (level == GraphicsQualityLevel.Custom) level = GraphicsQualityRules.PipelineLevel(value);
            return level == GraphicsQualityLevel.High || level == GraphicsQualityLevel.Ultra;
        }

        private static bool UseNatureModels()
        {
            var value = GraphicsQuality.Current;
            var level = GraphicsQualityRules.Detect(value);
            if (level == GraphicsQualityLevel.Custom) level = GraphicsQualityRules.PipelineLevel(value);
            return level != GraphicsQualityLevel.Low;
        }

        /// <summary>The variant indices of the models that exist; null when none does (keep the code-built shapes).</summary>
        private static int[] Variants(NatureInstances models, bool byHeight, params string[] names)
        {
            var found = new List<int>();
            var seen = new Dictionary<string, int>();
            foreach (var name in names)
            {
                if (!seen.TryGetValue(name, out int index)) { index = models.AddVariant(name, byHeight); seen.Add(name, index); }
                if (index >= 0) found.Add(index);
            }
            return found.Count == 0 ? null : found.ToArray();
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
                case TerrainDecorationKind.Grass: mesh.AddTuft(at, scale, rotation); break;
                case TerrainDecorationKind.Flower:
                    mesh.AddCylinder(at + Vector3.up * (scale * 0.22f), scale * 0.035f, scale * 0.45f, new Color(0.23f, 0.46f, 0.18f));
                    mesh.AddRock(at + Vector3.up * (scale * 0.48f), scale * 0.16f, new Color(0.92f, 0.48f, 0.60f)); break;
                case TerrainDecorationKind.Pebble: mesh.AddRock(at + Vector3.up * (scale * 0.07f), scale * 0.13f, new Color(0.50f, 0.48f, 0.43f)); break;
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

        internal sealed class MeshParts
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
            /// <summary>A tuft of thin, leaning blades (single triangles; the nature shader draws both sides).</summary>
            public void AddTuft(Vector3 at, float scale, float rotation)
            {
                const int blades = 7;
                float turn = rotation * Mathf.Deg2Rad;
                for (int i = 0; i < blades; i++)
                {
                    float a = turn + i * 2.39996f; // golden angle: blades fan out without lining up
                    float spread = scale * (0.05f + 0.10f * ((i * 37) % 7) / 6f);
                    Vector3 root = at + new Vector3(Mathf.Cos(a) * spread, 0f, Mathf.Sin(a) * spread);
                    float height = scale * (0.30f + 0.28f * ((i * 53) % 5) / 4f);
                    float width = scale * 0.045f;
                    Vector3 side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * width;
                    Vector3 lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * height * 0.35f;
                    float shade = 0.85f + 0.3f * ((i * 29) % 4) / 3f;
                    Color baseColor = new Color(0.16f, 0.34f, 0.12f) * shade;
                    Color tipColor = new Color(0.52f, 0.72f, 0.30f) * shade;
                    int index = Vertices.Count;
                    Vertices.Add(root - side); Vertices.Add(root + side); Vertices.Add(root + lean + Vector3.up * height);
                    Colors.Add(baseColor); Colors.Add(baseColor); Colors.Add(tipColor);
                    Triangles.Add(index); Triangles.Add(index + 2); Triangles.Add(index + 1);
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
