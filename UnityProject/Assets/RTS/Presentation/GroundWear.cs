using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Worn ground (11-09, owner: "draw the roads and the bare earth"): packed earth round every building and paths
    /// where villagers keep walking. A map of 0..1 at two texels per cell, read by Rts/TerrainBlend (_WearTex) and by
    /// the near grass, which leaves those places bare (no blades poking through a floor any more). Medium and above.
    /// Display only: it reads the economy frame and never touches the simulation.
    /// </summary>
    public sealed class GroundWear : MonoBehaviour
    {
        public static GroundWear Active { get; private set; }

        private const int TexelsPerCell = 2;
        private const float ObserveSeconds = 1f;
        private const float StepPerVisit = 0.035f;
        /// <summary>Every ObserveSeconds the paths lose this share, so a path left alone fades over some minutes.</summary>
        private const float Decay = 0.9985f;
        private const float YardMeters = 2.2f;

        private int width, height;
        private float metersPerTexel;
        private float[] traffic;
        private float[] yards;
        private byte[] bytes;
        private Texture2D texture;
        private Material terrainMaterial;
        private float nextObserve;
        private long buildingSignature = long.MinValue;

        /// <summary>Changes whenever the buildings' yards change, so the near grass knows to rebuild.</summary>
        public int YardVersion { get; private set; }

        public void Init(TerrainMap map, Material material)
        {
            width = map.WidthCells * TexelsPerCell;
            height = map.HeightCells * TexelsPerCell;
            metersPerTexel = map.CellSizeMeters / TexelsPerCell;
            traffic = new float[width * height];
            yards = new float[width * height];
            bytes = new byte[width * height];
            texture = new Texture2D(width, height, TextureFormat.R8, false)
            {
                name = "RTS_GroundWear", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(bytes);
            texture.Apply(false, false);
            terrainMaterial = material;
            if (terrainMaterial != null) terrainMaterial.SetTexture("_WearTex", texture);
            Active = this;
        }

        public static bool Enabled
        {
            get
            {
                var value = GraphicsQuality.Current;
                var level = GraphicsQualityRules.Detect(value);
                if (level == GraphicsQualityLevel.Custom) level = GraphicsQualityRules.PipelineLevel(value);
                return level != GraphicsQualityLevel.Low;
            }
        }

        /// <summary>0..1 at a point in this object's space (metres from the map's corner).</summary>
        public float WearAt(float x, float z)
        {
            int tx = (int)(x / metersPerTexel), tz = (int)(z / metersPerTexel);
            if (tx < 0 || tz < 0 || tx >= width || tz >= height) return 0f;
            int i = tz * width + tx;
            return Mathf.Max(traffic[i], yards[i]);
        }

        /// <summary>Called by EconomyLayer each frame; does its work once a second.</summary>
        public void Observe(EconomyView economy)
        {
            if (economy == null || traffic == null || Time.unscaledTime < nextObserve) return;
            nextObserve = Time.unscaledTime + ObserveSeconds;
            if (!Enabled) return;

            // Buildings: rebuild the yards only when the set of buildings changes (order-independent signature).
            long signature = 17;
            foreach (var b in economy.Buildings)
                signature += (long)(b.Id * 2654435761u) ^ ((long)b.SizeMeters << 40) ^ (b.Complete ? 1L << 50 : 0L);
            if (signature != buildingSignature)
            {
                buildingSignature = signature;
                System.Array.Clear(yards, 0, yards.Length);
                foreach (var b in economy.Buildings) AddYard(EconomyLayer.ToWorld(b.Center), b.SizeMeters);
                YardVersion++;
            }

            for (int i = 0; i < traffic.Length; i++) traffic[i] *= Decay;
            foreach (var v in economy.Villagers)
            {
                var p = transform.InverseTransformPoint(EconomyLayer.ToWorld(v.Position));
                int tx = (int)(p.x / metersPerTexel), tz = (int)(p.z / metersPerTexel);
                Visit(tx, tz, StepPerVisit);
                Visit(tx + 1, tz, StepPerVisit * 0.5f); Visit(tx - 1, tz, StepPerVisit * 0.5f);
                Visit(tx, tz + 1, StepPerVisit * 0.5f); Visit(tx, tz - 1, StepPerVisit * 0.5f);
            }

            for (int i = 0; i < bytes.Length; i++)
            {
                // A path shows only once it has been walked a few times (the square keeps single trips invisible).
                float t = traffic[i];
                float wear = Mathf.Max(yards[i], t * t * 1.6f);
                bytes[i] = (byte)(Mathf.Clamp01(wear) * 255f);
            }
            texture.LoadRawTextureData(bytes);
            texture.Apply(false, false);
        }

        private void Visit(int tx, int tz, float amount)
        {
            if (tx < 0 || tz < 0 || tx >= width || tz >= height) return;
            int i = tz * width + tx;
            traffic[i] = Mathf.Min(0.8f, traffic[i] + amount);
        }

        private void AddYard(Vector3 world, int sizeMeters)
        {
            var p = transform.InverseTransformPoint(world);
            float half = sizeMeters * 0.5f;
            float reach = half + YardMeters;
            int x0 = Mathf.FloorToInt((p.x - reach) / metersPerTexel), x1 = Mathf.CeilToInt((p.x + reach) / metersPerTexel);
            int z0 = Mathf.FloorToInt((p.z - reach) / metersPerTexel), z1 = Mathf.CeilToInt((p.z + reach) / metersPerTexel);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(height - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(width - 1, x1); x++)
                {
                    float cx = (x + 0.5f) * metersPerTexel - p.x, cz = (z + 0.5f) * metersPerTexel - p.z;
                    // Distance outside the footprint square: 0 inside, YardMeters at the yard's edge.
                    float outside = Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) - half;
                    float value = outside <= 0f ? 1f : Mathf.Lerp(0.75f, 0f, outside / YardMeters);
                    int i = z * width + x;
                    if (value > yards[i]) yards[i] = value;
                }
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            if (texture != null) Destroy(texture);
        }
    }
}
