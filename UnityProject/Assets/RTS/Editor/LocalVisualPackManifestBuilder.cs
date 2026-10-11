using System;
using System.Collections.Generic;
using Rts.Contracts;
using Rts.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rts.Editor
{
    /// <summary>
    /// Creates the runtime manifest from the locally installed visual pack.
    /// </summary>
    public sealed class LocalVisualPackManifestBuilder : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        [MenuItem("RTS/素材パックの一覧を作り直す")]
        public static void RebuildFromMenu()
        {
            RebuildManifest();
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            RebuildManifest();
        }

        private static void RebuildManifest()
        {
            if (!HasAnyPackAsset())
            {
                DeleteManifest();
                Debug.Log("素材パックが見つからないため、LocalVisualPackManifest は作成しません。仮の表示を使います。");
                return;
            }

            EnsureFolder("Assets/RTS/Generated");
            EnsureFolder("Assets/RTS/Generated/Resources");
            DeleteManifest();

            var manifest = ScriptableObject.CreateInstance<LocalVisualPackManifest>();
            var models = new List<LocalVisualPackManifest.ModelEntry>();
            var animations = new List<LocalVisualPackManifest.AnimationEntry>();
            var materials = new List<LocalVisualPackManifest.MaterialEntry>();

            foreach (UnitKind kind in Enum.GetValues(typeof(UnitKind)))
                AddModel(models, kind.ToString(), LocalVisualPack.UnitAssetPath(kind));

            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
                AddModel(models, kind.ToString(), LocalVisualPack.BuildingAssetPath(kind));

            AddModel(models, LocalVisualPackManifest.CoreKey, LocalVisualPack.CoreAssetPath());
            AddModel(models, LocalVisualPackManifest.OutpostKey, LocalVisualPack.OutpostAssetPath());

            foreach (UnitKind kind in Enum.GetValues(typeof(UnitKind)))
            {
                foreach (LocalVisualPack.UnitMotion motion in Enum.GetValues(typeof(LocalVisualPack.UnitMotion)))
                {
                    var clip = LoadAnimationClip(LocalVisualPack.AnimationAssetPath(kind, motion));
                    if (clip != null)
                    {
                        animations.Add(new LocalVisualPackManifest.AnimationEntry
                        {
                            key = LocalVisualPack.AnimationKey(kind, motion),
                            clip = clip
                        });
                    }
                }
            }

            // The staged looks (civilisation x equipment stage) and the army banners are listed by pack file.
            var stagedModels = new List<string>();
            var stagedClips = new List<string>();
            LocalVisualPack.StagedAssetPaths(stagedModels, stagedClips);
            foreach (var path in stagedModels)
                AddModel(models, LocalVisualPackManifest.FileKey(path), path);
            foreach (var path in stagedClips)
            {
                var clip = LoadAnimationClip(path);
                if (clip != null)
                    animations.Add(new LocalVisualPackManifest.AnimationEntry { key = LocalVisualPackManifest.FileKey(path), clip = clip });
            }

            AddMaterial(materials, LocalVisualPackManifest.UnitBlueMaterialKey, LocalVisualPack.UnitMaterialAssetPath(true));
            AddMaterial(materials, LocalVisualPackManifest.UnitRedMaterialKey, LocalVisualPack.UnitMaterialAssetPath(false));
            AddMaterial(materials, LocalVisualPackManifest.BuildingBlueMaterialKey, LocalVisualPack.BuildingMaterialAssetPath(1));
            AddMaterial(materials, LocalVisualPackManifest.BuildingRedMaterialKey, LocalVisualPack.BuildingMaterialAssetPath(2));
            AddMaterial(materials, LocalVisualPackManifest.BuildingWhiteMaterialKey, LocalVisualPack.BuildingMaterialAssetPath(0));

            manifest.Models = models.ToArray();
            manifest.Animations = animations.ToArray();
            manifest.Materials = materials.ToArray();
            AssetDatabase.CreateAsset(manifest, LocalVisualPack.ManifestAssetPath);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("素材パックの一覧を作成しました: " + LocalVisualPack.ManifestAssetPath);
        }

        private static bool HasAnyPackAsset()
        {
            foreach (UnitKind kind in Enum.GetValues(typeof(UnitKind)))
                if (HasAsset(LocalVisualPack.UnitAssetPath(kind))) return true;

            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
                if (HasAsset(LocalVisualPack.BuildingAssetPath(kind))) return true;

            if (HasAsset(LocalVisualPack.CoreAssetPath())) return true;
            if (HasAsset(LocalVisualPack.OutpostAssetPath())) return true;
            if (HasAsset(LocalVisualPack.EffectAssetPath("FX_Building_burning_small.prefab"))) return true;
            if (HasAsset(LocalVisualPack.UnitMaterialAssetPath(true))) return true;
            if (HasAsset(LocalVisualPack.UnitMaterialAssetPath(false))) return true;
            return false;
        }

        private static void AddModel(List<LocalVisualPackManifest.ModelEntry> entries, string key, string path)
        {
            if (path == null) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model != null) entries.Add(new LocalVisualPackManifest.ModelEntry { key = key, model = model });
        }

        private static void AddMaterial(List<LocalVisualPackManifest.MaterialEntry> entries, string key, string path)
        {
            if (path == null) return;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) entries.Add(new LocalVisualPackManifest.MaterialEntry { key = key, material = material });
        }

        private static bool HasAsset(string path)
        {
            return path != null && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null;
        }

        private static AnimationClip LoadAnimationClip(string path)
        {
            if (path == null) return null;
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                var clip = assets[i] as AnimationClip;
                if (clip != null && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return clip;
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void DeleteManifest()
        {
            if (AssetDatabase.LoadAssetAtPath<LocalVisualPackManifest>(LocalVisualPack.ManifestAssetPath) != null)
                AssetDatabase.DeleteAsset(LocalVisualPack.ManifestAssetPath);
        }
    }
}
