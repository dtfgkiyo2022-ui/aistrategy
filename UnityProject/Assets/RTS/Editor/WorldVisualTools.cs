using System;
using System.Collections.Generic;
using System.IO;
using Rts.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rts.Editor
{
    /// <summary>Editor-only tools for reproducible visual review. Runtime assemblies never reference this file.</summary>
    public static class WorldVisualTools
    {
        private const string TerrainAssetFolder = "Assets/RTS/Art/Terrain";
        private const string CaptureFolder = "D:/rts-verify/screens";

        [MenuItem("RTS/地面のテクスチャを作り直す")]
        public static void RegenerateTerrainTextures()
        {
            EnsureFolder(TerrainAssetFolder);
            foreach (TerrainTextureKind kind in Enum.GetValues(typeof(TerrainTextureKind)))
            {
                var texture = TerrainTextureGenerator.Create(kind, TerrainTextureGenerator.DefaultSize);
                string file = TerrainAssetFolder + "/" + kind + ".png";
                File.WriteAllBytes(ToProjectPath(file), texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.Refresh();
            Debug.Log("RTS terrain textures regenerated in " + TerrainAssetFolder + ".");
        }

        [MenuItem("RTS/見た目の写真を撮る")]
        public static void CaptureWorldVisuals()
        {
            BattlefieldView view = UnityEngine.Object.FindObjectOfType<BattlefieldView>();
            Camera camera = Camera.main;
            if (view == null || camera == null)
            {
                Debug.LogError("RTS visual capture needs a BattlefieldView and a MainCamera in the open scene.");
                return;
            }
            Directory.CreateDirectory(CaptureFolder);
            TerrainMap map = CreateFixedCaptureMap(41721);
            view.SetTerrain(map);
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldOrthographicSize = camera.orthographicSize;
            bool oldOrthographic = camera.orthographic;
            int width = 1280, height = 720;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var oldTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = renderTexture;
                camera.orthographic = true;
                Vector3 target = new Vector3(map.WidthCells * map.CellSizeMeters * 0.5f, 0f,
                    map.HeightCells * map.CellSizeMeters * 0.5f);
                float extent = Mathf.Max(map.WidthCells, map.HeightCells) * map.CellSizeMeters;
                var shots = new[]
                {
                    new CaptureShot("01_overall", target + new Vector3(extent * 0.25f, extent * 1.15f, -extent * 0.42f), target),
                    new CaptureShot("02_base", target + new Vector3(-extent * 0.15f, extent * 0.80f, -extent * 0.12f), new Vector3(extent * 0.25f, 0f, extent * 0.26f)),
                    new CaptureShot("03_river", target + new Vector3(extent * 0.08f, extent * 0.63f, -extent * 0.16f), new Vector3(extent * 0.63f, 0f, extent * 0.50f)),
                    new CaptureShot("04_forest_edge", target + new Vector3(-extent * 0.20f, extent * 0.72f, extent * 0.20f), new Vector3(extent * 0.70f, 0f, extent * 0.72f))
                };
                camera.orthographicSize = extent * 0.56f;
                foreach (var shot in shots)
                {
                    camera.transform.position = shot.Position;
                    camera.transform.LookAt(shot.Target);
                    RenderShot(camera, renderTexture, width, height, CaptureFolder + "/" + shot.Name + ".png");
                }
                AssetDatabase.Refresh();
                Debug.Log("RTS visual captures written to " + CaptureFolder + " (seed 41721).");
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.transform.position = oldPosition;
                camera.transform.rotation = oldRotation;
                camera.orthographicSize = oldOrthographicSize;
                camera.orthographic = oldOrthographic;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        public static TerrainMap CreateFixedCaptureMap(int seed)
        {
            const int width = 48, height = 32;
            var blocked = new List<int>(); var kinds = new byte[width * height];
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    uint random = TerrainVisualMath.Hash((uint)seed, (uint)x, (uint)z);
                    bool river = Mathf.Abs(x - (width * 0.52f + Mathf.Sin(z * 0.30f) * 3f)) < 1.45f;
                    bool forest = (x > 5 && x < 18 && z > 15) || (x > 28 && z > 5 && z < 20);
                    bool mountain = x < 3 || x > width - 4 || z < 2 || (x > 34 && z > 22);
                    if (river) kinds[z * width + x] = 2;
                    else if (mountain) { kinds[z * width + x] = 3; blocked.Add(z * width + x); }
                    else if (forest) kinds[z * width + x] = 1;
                    else if (TerrainVisualMath.ToUnit(random) < 0.035f) { blocked.Add(z * width + x); }
                }
            return new TerrainMap(width, height, 2f, blocked, kinds);
        }

        private static void RenderShot(Camera camera, RenderTexture target, int width, int height, string file)
        {
            var oldActive = RenderTexture.active;
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(false, false);
            File.WriteAllBytes(file, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = oldActive;
        }

        private static void EnsureFolder(string assetFolder)
        {
            string absolute = ToProjectPath(assetFolder);
            if (!Directory.Exists(absolute)) Directory.CreateDirectory(absolute);
        }

        private static string ToProjectPath(string assetPath)
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(project, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private struct CaptureShot
        {
            public readonly string Name; public readonly Vector3 Position; public readonly Vector3 Target;
            public CaptureShot(string name, Vector3 position, Vector3 target) { Name = name; Position = position; Target = target; }
        }
    }
}
