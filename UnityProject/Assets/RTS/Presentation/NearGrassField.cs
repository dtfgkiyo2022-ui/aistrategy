using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rts.Presentation
{
    /// <summary>
    /// Dense grass only around where the camera looks (11-09 test play: "up close the grass is sparse"). The whole-map
    /// decoration budget cannot be dense everywhere, so square chunks are built near the camera's ground point and
    /// dropped when the camera moves away. Positions come from a hash of the chunk, so a chunk looks the same each time
    /// it is rebuilt. Medium and above only; Low keeps its look (docs/settings.md). Display only.
    /// </summary>
    public sealed class NearGrassField : MonoBehaviour
    {
        private const float ChunkMeters = 16f;
        private const int ChunksBuiltPerFrame = 3;
        private const float RefreshSeconds = 0.15f;
        /// <summary>The grass shrinks away over this many metres of camera height below the level's ceiling.</summary>
        private const float HeightFadeMeters = 25f;

        private TerrainMap map;
        private readonly Dictionary<long, GameObject> chunks = new Dictionary<long, GameObject>();
        private readonly List<long> dropList = new List<long>();
        private readonly List<Vector2Int> wanted = new List<Vector2Int>();
        private readonly Dictionary<long, int> chunkYards = new Dictionary<long, int>();
        private readonly List<long> staleList = new List<long>();
        private bool cards;
        private int cardPlanes = 2;
        /// <summary>High and Ultra: meadows in patches with bare clearings instead of an even carpet (11-09).</summary>
        private bool patchy;
        private Material material;
        private float density;
        private float nextRefresh;
        private int radius;

        public void Init(TerrainMap terrain)
        {
            map = terrain;
        }

        /// <summary>Tufts per square metre for a quality level (0 = none).</summary>
        public static float DensityFor(GraphicsQualityLevel level)
        {
            switch (level)
            {
                case GraphicsQualityLevel.Medium: return 0.7f;
                case GraphicsQualityLevel.High: return 1.2f;
                case GraphicsQualityLevel.Ultra: return 1.8f;
                default: return 0f;
            }
        }

        /// <summary>The widest field, in chunks out from the centre chunk.</summary>
        private static int MaxRadiusFor(GraphicsQualityLevel level)
        {
            return level == GraphicsQualityLevel.Ultra ? 6 : level == GraphicsQualityLevel.High ? 5 : 4;
        }

        /// <summary>Above this camera height nothing is drawn (the blades are a pixel or two).</summary>
        private static float MaxHeightFor(GraphicsQualityLevel level)
        {
            return level == GraphicsQualityLevel.Ultra ? 115f : level == GraphicsQualityLevel.High ? 100f : 85f;
        }

        private static GraphicsQualityLevel CurrentLevel()
        {
            var value = GraphicsQuality.Current;
            var level = GraphicsQualityRules.Detect(value);
            return level == GraphicsQualityLevel.Custom ? GraphicsQualityRules.PipelineLevel(value) : level;
        }

        private void LateUpdate()
        {
            if (map == null) return;
            var camera = Camera.main;
            var level = CurrentLevel();
            float height = camera != null ? camera.transform.position.y : float.MaxValue;
            float ceiling = MaxHeightFor(level);
            Vector3 point = Vector3.zero;
            bool look = camera != null && LookPoint(camera, out point);

            // Every frame: the blades shrink into the ground before the edge of the built chunks, and the whole field
            // shrinks away as the camera rises, so neither the square edge nor the switch-off can be seen.
            if (material != null && look)
            {
                float reach = radius * ChunkMeters;
                material.SetVector("_FadeCentre", new Vector4(point.x, point.y, point.z, 0f));
                material.SetVector("_FadeRange", new Vector4(reach * 0.55f, reach - 2f, 1f,
                    Mathf.Clamp01((ceiling - height) / HeightFadeMeters)));
            }

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            float want = DensityFor(level);
            // Meadow patches on every level with grass (11-10; about half the tufts are skipped, so the 1.5x count costs the same).
            bool wantPatchy = true;
            if (!Mathf.Approximately(want, density) || wantPatchy != patchy) { DropAll(); density = want; patchy = wantPatchy; cardPlanes = level == GraphicsQualityLevel.Ultra ? 3 : 2; }
            if (density <= 0f || camera == null || height > ceiling || !look) { DropAll(); return; }
            var local = transform.InverseTransformPoint(point);
            int cx = Mathf.FloorToInt(local.x / ChunkMeters), cz = Mathf.FloorToInt(local.z / ChunkMeters);
            // About as far out as the camera can see at this height, within the level's widest field.
            radius = Mathf.Clamp(Mathf.CeilToInt(height * 1.15f / ChunkMeters), 2, MaxRadiusFor(level));

            dropList.Clear();
            foreach (var pair in chunks)
            {
                int x = (int)(pair.Key >> 32), z = (int)(pair.Key & 0xffffffffL);
                if (Mathf.Abs(x - cx) > radius + 1 || Mathf.Abs(z - cz) > radius + 1) dropList.Add(pair.Key);
            }
            foreach (var key in dropList) { DestroyChunk(chunks[key]); chunks.Remove(key); chunkYards.Remove(key); }

            // Nearest first, so the chunk under the camera appears before the ones at the edge.
            wanted.Clear();
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                    if (!chunks.ContainsKey(Key(cx + dx, cz + dz))) wanted.Add(new Vector2Int(cx + dx, cz + dz));
            wanted.Sort((a, b) => (Mathf.Abs(a.x - cx) + Mathf.Abs(a.y - cz)).CompareTo(Mathf.Abs(b.x - cx) + Mathf.Abs(b.y - cz)));
            int built = 0;
            for (int i = 0; i < wanted.Count && built < ChunksBuiltPerFrame; i++, built++)
            {
                long key = Key(wanted[i].x, wanted[i].y);
                chunks.Add(key, BuildChunk(wanted[i].x, wanted[i].y));
                chunkYards[key] = YardVersion();
            }
            // A new or removed building changes the bare yards: rebuild the chunks one by one, replacing each in place.
            int version = YardVersion();
            if (built < ChunksBuiltPerFrame)
            {
                staleList.Clear();
                foreach (var pair in chunkYards) if (pair.Value != version) staleList.Add(pair.Key);
                staleList.Sort((a, b) => Distance(a, cx, cz).CompareTo(Distance(b, cx, cz)));
                for (int i = 0; i < staleList.Count && built < ChunksBuiltPerFrame; i++, built++)
                {
                    long key = staleList[i];
                    if (!chunks.TryGetValue(key, out var stale)) { chunkYards.Remove(key); continue; }
                    DestroyChunk(stale);
                    chunks[key] = BuildChunk((int)(key >> 32), (int)(key & 0xffffffffL));
                    chunkYards[key] = version;
                }
            }
        }

        private static int Distance(long key, int cx, int cz)
        {
            return Mathf.Abs((int)(key >> 32) - cx) + Mathf.Abs((int)(key & 0xffffffffL) - cz);
        }

        private static int YardVersion() { return GroundWear.Active != null ? GroundWear.Active.YardVersion : 0; }

        private static bool LookPoint(Camera camera, out Vector3 point)
        {
            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            point = Vector3.zero;
            if (ray.direction.y > -0.01f) return false;
            float t = -ray.origin.y / ray.direction.y;
            point = ray.origin + ray.direction * t;
            return true;
        }

        /// <summary>Smooth value noise in [0, 1] (display only).</summary>
        private static float Noise(float x, float z, uint salt)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fz = z - iz;
            fx = fx * fx * (3f - 2f * fx); fz = fz * fz * (3f - 2f * fz);
            float a = Corner(ix, iz, salt), b = Corner(ix + 1, iz, salt), c = Corner(ix, iz + 1, salt), d = Corner(ix + 1, iz + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        private static float Corner(int x, int z, uint salt) { return TerrainVisualMath.Unit(TerrainVisualMath.Hash(salt, (uint)x, (uint)z), 1u); }

        private static long Key(int x, int z) { return ((long)x << 32) | (uint)z; }

        private void DropAll()
        {
            foreach (var pair in chunks) DestroyChunk(pair.Value);
            chunks.Clear();
            chunkYards.Clear();
        }

        private static void DestroyChunk(GameObject chunk)
        {
            if (chunk == null) return;
            var filter = chunk.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
            Destroy(chunk);
        }

        private GameObject BuildChunk(int cx, int cz)
        {
            var go = new GameObject("NearGrass_" + cx + "_" + cz);
            go.transform.SetParent(transform, false);
            float cell = map.CellSizeMeters;
            float mapWidth = map.WidthCells * cell, mapHeight = map.HeightCells * cell;
            float x0 = cx * ChunkMeters, z0 = cz * ChunkMeters;
            if (x0 >= mapWidth || z0 >= mapHeight || x0 + ChunkMeters <= 0f || z0 + ChunkMeters <= 0f) return go;
            var grassMaterial = GrassMaterial();
            var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var colors = new List<Color>(); var triangles = new List<int>();
            var parts = cards ? null : new TerrainSurfaceBuilder.MeshParts();
            var wear = GroundWear.Active;
            bool wearOn = wear != null && GroundWear.Enabled;
            // Patchy meadows keep about half the tufts, so the patches themselves are filled a little denser.
            int count = Mathf.RoundToInt(density * ChunkMeters * ChunkMeters * (patchy ? 1.5f : 1f));
            uint seed = TerrainVisualMath.Hash(90731u, (uint)cx, (uint)cz);
            for (int i = 0; i < count; i++)
            {
                uint random = TerrainVisualMath.Hash(seed, (uint)i, 7u);
                float x = x0 + TerrainVisualMath.Unit(random, 1u) * ChunkMeters;
                float z = z0 + TerrainVisualMath.Unit(random, 2u) * ChunkMeters;
                if (x < 0f || z < 0f || x >= mapWidth || z >= mapHeight) continue;
                int gx = (int)(x / cell), gz = (int)(z / cell);
                byte kind = map.KindAt(gx, gz);
                if (kind == 2 || kind == 3 || map.IsBlocked(gx, gz)) continue;
                // Thinner under the trees, where the forest floor shows through.
                if (kind == 1 && TerrainVisualMath.Unit(random, 3u) > 0.45f) continue;
                // Bare round buildings and on walked paths, thinning towards their edges.
                if (wearOn && wear.WearAt(x, z) > 0.18f + TerrainVisualMath.Unit(random, 8u) * 0.25f) continue;
                float scale = 0.85f + TerrainVisualMath.Unit(random, 4u) * 0.75f;
                float lush = -1f;
                if (patchy)
                {
                    // Meadow patches of about 12 m broken up by 3 m clumps; between them the ground shows. The
                    // grass is tallest and greenest in the middle of a patch and thins and dries towards its edge.
                    float n = Noise(x * 0.085f, z * 0.085f, 31u) * 0.65f + Noise(x * 0.31f, z * 0.31f, 47u) * 0.35f;
                    lush = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.66f, n));
                    if (TerrainVisualMath.Unit(random, 11u) > lush) continue;
                    scale = (0.62f + TerrainVisualMath.Unit(random, 4u) * 0.55f) * (0.70f + 0.40f * lush);
                }
                float turn = TerrainVisualMath.Unit(random, 5u) * 360f;
                if (cards) AddCard(vertices, uvs, colors, triangles, new Vector3(x, 0.02f, z), scale, turn, random, lush);
                else parts.AddTuft(new Vector3(x, 0.03f, z), scale, turn);
            }
            if (!cards) { vertices = parts.Vertices; colors = parts.Colors; triangles = parts.Triangles; uvs = null; }
            if (vertices.Count == 0) return go;
            var mesh = new Mesh { name = go.name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetColors(colors);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = grassMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A tuft as crossed upright cards with the painted blades, tinted a little per tuft.</summary>
        private void AddCard(List<Vector3> vertices, List<Vector2> uvs, List<Color> colors, List<int> triangles,
            Vector3 at, float scale, float turn, uint random, float lush)
        {
            // Seen from above (the RTS camera), an upright card shows only its edge. The cards lie back at about
            // 55 degrees, each towards its own side, so their painted blades face the camera.
            float width = 0.85f * scale, height = 0.62f * scale;
            float tint = TerrainVisualMath.Unit(random, 9u);
            // From a deep green to a sun-dried yellow-green, mostly in between.
            var color = Color.Lerp(new Color(0.70f, 0.80f, 0.62f), new Color(0.88f, 0.86f, 0.62f), tint * tint);
            // Patchy meadows: a deep green in the middle of a patch, a lighter, drier green at its edge.
            if (lush >= 0f)
                color = Color.Lerp(Color.Lerp(new Color(0.92f, 0.90f, 0.66f), new Color(0.98f, 0.94f, 0.70f), tint),
                    Color.Lerp(new Color(0.62f, 0.78f, 0.56f), new Color(0.74f, 0.84f, 0.60f), tint), lush);
            for (int plane = 0; plane < cardPlanes; plane++)
            {
                float a = (turn + plane * 180f / cardPlanes) * Mathf.Deg2Rad;
                var side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (width * 0.5f);
                // A slight lean away from the card's plane, so the crossed cards do not read as a flat star.
                var lean = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * (height * 0.82f * (plane % 2 == 0 ? 1f : -1f));
                int index = vertices.Count;
                vertices.Add(at - side); vertices.Add(at + side);
                vertices.Add(at + side + lean + Vector3.up * (height * 0.57f)); vertices.Add(at - side + lean + Vector3.up * (height * 0.57f));
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
                triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 1);
                triangles.Add(index); triangles.Add(index + 3); triangles.Add(index + 2);
            }
        }

        private Material GrassMaterial()
        {
            if (material != null) return material;
            // The painted cards (11-09) when their shader and picture are in the build; the plain blades otherwise.
            var cardShader = Shader.Find("Rts/GrassCard");
            var blades = Resources.Load<Texture2D>("Grass/GrassCard");
            if (cardShader != null && blades != null)
            {
                cards = true;
                material = new Material(cardShader);
                material.SetTexture("_BaseMap", blades);
                material.SetFloat("_WindAmount", 0.07f);
                return material;
            }
            cards = false;
            var shader = Shader.Find("Rts/NatureWind");
            material = shader == null ? PresentationMaterials.Get(Color.white) : new Material(shader);
            if (shader != null) { material.SetColor("_BaseColor", Color.white); material.SetFloat("_WindAmount", 0.03f); }
            return material;
        }

        private void OnDestroy()
        {
            DropAll();
            if (material != null) Destroy(material);
        }
    }
}
