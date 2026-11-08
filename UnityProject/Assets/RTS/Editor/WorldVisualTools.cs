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

        /// <summary>Batch entry (-executeMethod): opens the mock battlefield scene and takes the same pictures.</summary>
        public static void CaptureWorldVisualsBatch()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/RTS/Scenes/MockBattlefield.unity");
            // A fresh batch editor compiles shaders in the background and draws a placeholder meanwhile, which made
            // the terrain come out flat white. Compile before drawing instead.
            ShaderUtil.allowAsyncCompilation = false;
            CaptureWorldVisuals();
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
            // No match runs here, so the fog texture is never filled in and would cover the whole map in grey.
            var fog = view.transform.Find("Fog");
            if (fog != null) fog.gameObject.SetActive(false);
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
                CaptureQualityComparisons(view, camera, renderTexture, width, height, shots[0]);
                foreach (var shot in shots)
                {
                    camera.transform.position = shot.Position;
                    camera.transform.LookAt(shot.Target);
                    RenderShot(camera, renderTexture, width, height, CaptureFolder + "/" + shot.Name + ".png");
                }
                CaptureFarms(camera, renderTexture, width, height, map);
                CaptureCatalogue(camera, renderTexture, width, height, map);
                CaptureModelTiers(camera, renderTexture, width, height, map);
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

        private static void CaptureQualityComparisons(BattlefieldView view, Camera camera, RenderTexture renderTexture,
            int width, int height, CaptureShot shot)
        {
            var levels = new[] { GraphicsQualityLevel.Low, GraphicsQualityLevel.Medium, GraphicsQualityLevel.High, GraphicsQualityLevel.Ultra };
            var names = new[] { "low", "medium", "high", "ultra" };
            for (int i = 0; i < levels.Length; i++)
            {
                GraphicsQuality.ApplyLevel(levels[i]);
                var fog = view.transform.Find("Fog");
                if (fog != null) fog.gameObject.SetActive(false);
                camera.transform.position = shot.Position;
                camera.transform.LookAt(shot.Target);
                RenderShot(camera, renderTexture, width, height, CaptureFolder + "/10_quality_" + names[i] + ".png");
            }
            GraphicsQuality.ApplyLevel(GraphicsQualityLevel.Low);
            var oldFog = view.transform.Find("Fog");
            if (oldFog != null) oldFog.gameObject.SetActive(false);
        }

        /// <summary>docs/settings.md 2.1: the same farm, castle and trees close up with each model tier (11_tier_*.png).</summary>
        private static void CaptureModelTiers(Camera camera, RenderTexture renderTexture, int width, int height, TerrainMap map)
        {
            int before = ArtDetail.Level;
            string[] names = { "low", "medium", "high", "ultra" };
            try
            {
                for (int tier = 0; tier < names.Length; tier++)
                {
                    ArtDetail.Set(tier);
                    var root = new GameObject("CaptureTiers");
                    try
                    {
                        float cell = map.CellSizeMeters;
                        if (LocalVisualPack.TryCreateBuilding(Rts.Contracts.BuildingKind.Farm, 1u, root.transform, 2f * cell, out var farm))
                            farm.transform.position = new Vector3(8f * cell, 0f, 8f * cell);
                        if (LocalVisualPack.TryCreateBuilding(Rts.Contracts.BuildingKind.Castle, 1u, root.transform, 4f * cell, out var castle))
                            castle.transform.position = new Vector3(13f * cell, 0f, 9f * cell);
                        var trees = new GameObject("Wood");
                        trees.transform.SetParent(root.transform, false);
                        trees.transform.position = new Vector3(9f * cell, 0f, 12f * cell);
                        ResourceModels.Build(trees, Rts.Contracts.ResourceKind.Wood, 7u, ResourceModels.Levels);
                        var center = new Vector3(10.5f * cell, 0f, 9.5f * cell);
                        camera.orthographicSize = 3.2f * cell;
                        camera.transform.position = center + new Vector3(6f * cell, 8f * cell, -6f * cell);
                        camera.transform.LookAt(center + Vector3.up * cell);
                        RenderShot(camera, renderTexture, width, height, CaptureFolder + "/11_tier_" + names[tier] + ".png");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                    }
                }
            }
            finally
            {
                ArtDetail.Set(before);
            }
        }

        /// <summary>Four finished farms (2 x 2 cells) on open grass, seen close and from the usual play height.</summary>
        private static void CaptureFarms(Camera camera, RenderTexture renderTexture, int width, int height, TerrainMap map)
        {
            var root = new GameObject("CaptureFarms");
            try
            {
                float cell = map.CellSizeMeters, size = 2f * cell;
                int[,] cells = { { 8, 6 }, { 11, 6 }, { 8, 9 }, { 11, 9 } };
                for (int i = 0; i < cells.GetLength(0); i++)
                {
                    if (!LocalVisualPack.TryCreateBuilding(Rts.Contracts.BuildingKind.Farm, 1u, root.transform, size, out var farm))
                    {
                        Debug.LogWarning("RTS visual capture: no farm model.");
                        return;
                    }
                    farm.transform.position = new Vector3((cells[i, 0] + 1f) * cell, 0f, (cells[i, 1] + 1f) * cell);
                }
                var center = new Vector3(11f * cell, 0f, 8.5f * cell);
                camera.orthographicSize = 5f * cell;
                camera.transform.position = center + new Vector3(8f * cell, 11f * cell, -8f * cell);
                camera.transform.LookAt(center);
                RenderShot(camera, renderTexture, width, height, CaptureFolder + "/05_farm_near.png");
                camera.orthographicSize = 14f * cell;
                camera.transform.position = center + new Vector3(16f * cell, 26f * cell, -18f * cell);
                camera.transform.LookAt(center);
                RenderShot(camera, renderTexture, width, height, CaptureFolder + "/06_farm_play.png");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // Footprints in cells of the usual economy (Simulation.SizeOf), for laying the catalogue out at true size.
        private static readonly (Rts.Contracts.BuildingKind kind, int cells)[] CatalogueBuildings =
        {
            (Rts.Contracts.BuildingKind.House, 2), (Rts.Contracts.BuildingKind.Hut, 2), (Rts.Contracts.BuildingKind.GrandHouse, 3),
            (Rts.Contracts.BuildingKind.Town, 4), (Rts.Contracts.BuildingKind.Storage, 2), (Rts.Contracts.BuildingKind.DropSite, 2),
            (Rts.Contracts.BuildingKind.Farm, 2), (Rts.Contracts.BuildingKind.Mill, 2), (Rts.Contracts.BuildingKind.LumberCamp, 2),
            (Rts.Contracts.BuildingKind.Quarry, 2), (Rts.Contracts.BuildingKind.Mine, 2), (Rts.Contracts.BuildingKind.Drill, 2),
            (Rts.Contracts.BuildingKind.MineShaft, 2), (Rts.Contracts.BuildingKind.Smelter, 3), (Rts.Contracts.BuildingKind.BlastFurnace, 3),
            (Rts.Contracts.BuildingKind.Steelworks, 3), (Rts.Contracts.BuildingKind.CharcoalKiln, 2), (Rts.Contracts.BuildingKind.BrickKiln, 2),
            (Rts.Contracts.BuildingKind.Sawmill, 2), (Rts.Contracts.BuildingKind.Joinery, 2), (Rts.Contracts.BuildingKind.Stonecutter, 2),
            (Rts.Contracts.BuildingKind.Assembler, 2), (Rts.Contracts.BuildingKind.GearWorkshop, 2), (Rts.Contracts.BuildingKind.Metalworks, 2),
            (Rts.Contracts.BuildingKind.RopeHut, 2), (Rts.Contracts.BuildingKind.ToolShop, 2), (Rts.Contracts.BuildingKind.RepairYard, 2),
            (Rts.Contracts.BuildingKind.Saddlery, 2), (Rts.Contracts.BuildingKind.Weaver, 2), (Rts.Contracts.BuildingKind.Lapidary, 2),
            (Rts.Contracts.BuildingKind.IncenseHouse, 2), (Rts.Contracts.BuildingKind.CharmWorkshop, 2), (Rts.Contracts.BuildingKind.DryingRack, 2),
            (Rts.Contracts.BuildingKind.Scriptorium, 2), (Rts.Contracts.BuildingKind.Academy, 2), (Rts.Contracts.BuildingKind.Monastery, 2),
            (Rts.Contracts.BuildingKind.Shrine, 2), (Rts.Contracts.BuildingKind.Market, 3), (Rts.Contracts.BuildingKind.Caravanserai, 2),
            (Rts.Contracts.BuildingKind.Harbor, 2), (Rts.Contracts.BuildingKind.Barracks, 3), (Rts.Contracts.BuildingKind.ArcheryRange, 3),
            (Rts.Contracts.BuildingKind.Stable, 3), (Rts.Contracts.BuildingKind.Fletcher, 3), (Rts.Contracts.BuildingKind.Blacksmith, 3),
            (Rts.Contracts.BuildingKind.SiegeWorkshop, 3), (Rts.Contracts.BuildingKind.EngineerCamp, 2), (Rts.Contracts.BuildingKind.Castle, 4),
            (Rts.Contracts.BuildingKind.Tower, 2), (Rts.Contracts.BuildingKind.Wall, 1), (Rts.Contracts.BuildingKind.Gate, 1),
            (Rts.Contracts.BuildingKind.Tollgate, 2)
        };

        /// <summary>
        /// Every building kind at its true footprint in rows of nine, then the resource points full and nearly
        /// gathered, each shot from the usual play height (08_buildings_*.png, 09_resources.png).
        /// </summary>
        private static void CaptureCatalogue(Camera camera, RenderTexture renderTexture, int width, int height, TerrainMap map)
        {
            var root = new GameObject("CaptureCatalogue");
            try
            {
                float cell = map.CellSizeMeters, pitch = 6f * cell;
                const int perRow = 9, rowsPerShot = 2;
                int shot = 0;
                for (int start = 0; start < CatalogueBuildings.Length; start += perRow * rowsPerShot)
                {
                    for (int i = root.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                    for (int i = start; i < Mathf.Min(start + perRow * rowsPerShot, CatalogueBuildings.Length); i++)
                    {
                        int slot = i - start;
                        var entry = CatalogueBuildings[i];
                        var holder = new GameObject(entry.kind.ToString());
                        holder.transform.SetParent(root.transform, false);
                        holder.transform.position = new Vector3((4 + (slot % perRow) * 6) * cell, 0f, (6 + (slot / perRow) * 6) * cell);
                        if (!LocalVisualPack.TryCreateBuilding(entry.kind, 1u, holder.transform, entry.cells * cell, out var _))
                            Debug.LogWarning("RTS visual capture: no model for " + entry.kind);
                    }
                    var center = new Vector3((4 + (perRow - 1) * 3) * cell, 0f, 9f * cell);
                    camera.orthographicSize = 9f * cell;
                    camera.transform.position = center + new Vector3(10f * cell, 22f * cell, -18f * cell);
                    camera.transform.LookAt(center);
                    RenderShot(camera, renderTexture, width, height, CaptureFolder + "/08_buildings_" + (++shot) + ".png");
                }
                for (int i = root.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                var kinds = new[] { Rts.Contracts.ResourceKind.Wood, Rts.Contracts.ResourceKind.Stone, Rts.Contracts.ResourceKind.Ore,
                    Rts.Contracts.ResourceKind.Gold, Rts.Contracts.ResourceKind.Food };
                for (int k = 0; k < kinds.Length; k++)
                    for (int level = ResourceModels.Levels; level >= 1; level--)
                    {
                        var point = new GameObject(kinds[k] + " " + level);
                        point.transform.SetParent(root.transform, false);
                        point.transform.position = new Vector3((4 + (ResourceModels.Levels - level) * 4) * cell, 0f, (5 + k * 4) * cell);
                        ResourceModels.Build(point, kinds[k], (uint)(k * 10 + level), level);
                    }
                var middle = new Vector3(10f * cell, 0f, 13f * cell);
                camera.orthographicSize = 11f * cell;
                camera.transform.position = middle + new Vector3(10f * cell, 22f * cell, -18f * cell);
                camera.transform.LookAt(middle);
                RenderShot(camera, renderTexture, width, height, CaptureFolder + "/09_resources.png");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
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
            string project = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            return Path.Combine(project, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private struct CaptureShot
        {
            public readonly string Name; public readonly Vector3 Position; public readonly Vector3 Target;
            public CaptureShot(string name, Vector3 position, Vector3 target) { Name = name; Position = position; Target = target; }
        }
    }
}
