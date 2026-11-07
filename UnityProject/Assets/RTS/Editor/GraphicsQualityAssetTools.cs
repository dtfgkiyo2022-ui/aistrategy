using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rts.Editor
{
    /// <summary>既存の低画質アセットを複製して、実行時 Resources から読める画質段を生成する。</summary>
    public static class GraphicsQualityAssetTools
    {
        private const string SourcePipeline = "Assets/Settings/RtsUniversalRenderPipeline.asset";
        private const string SourceRenderer = "Assets/Settings/RtsUniversalRenderer.asset";
        private const string Folder = "Assets/Settings/Resources/Graphics";

        [MenuItem("RTS/画質の設定を作る")]
        public static void CreateQualityAssets()
        {
            EnsureFolder("Assets/Settings/Resources"); EnsureFolder(Folder);
            var hqRenderer = CreateHighQualityRenderer();
            CreatePipeline("RtsURP_Medium", 2, 1f, 80f, 2048, null);
            CreatePipeline("RtsURP_High", 4, 1f, 120f, 4096, hqRenderer);
            CreatePipeline("RtsURP_Ultra", 8, 1.25f, 180f, 4096, hqRenderer);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("RTS quality assets created in " + Folder + ".");
        }

        private static void CreatePipeline(string name, int msaa, float scale, float shadowDistance, int shadowResolution,
            ScriptableRendererData renderer)
        {
            string target = Folder + "/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(target);
            if (asset == null && !AssetDatabase.CopyAsset(SourcePipeline, target))
            {
                var source = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SourcePipeline);
                if (source == null) { Debug.LogError("RTS source URP asset not found: " + SourcePipeline); return; }
                var copy = Object.Instantiate(source); copy.name = name; AssetDatabase.CreateAsset(copy, target);
            }
            asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(target);
            if (asset == null) return;
            asset.name = name; asset.msaaSampleCount = msaa; asset.renderScale = scale; asset.shadowDistance = shadowDistance;
            asset.shadowCascadeCount = 4; // Soft shadows come from the copied Low asset (already on).
            var serialized = new SerializedObject(asset);
            var shadowMap = serialized.FindProperty("m_MainLightShadowmapResolution");
            if (shadowMap != null) shadowMap.intValue = shadowResolution;
            if (renderer != null)
            {
                var rendererList = serialized.FindProperty("m_RendererDataList");
                if (rendererList != null && rendererList.arraySize > 0)
                    rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static ScriptableRendererData CreateHighQualityRenderer()
        {
            string target = Folder + "/RtsUniversalRenderer_HQ.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(target);
            if (renderer == null)
            {
                if (!AssetDatabase.CopyAsset(SourceRenderer, target))
                {
                    Debug.LogError("RTS source renderer asset not found: " + SourceRenderer);
                    return null;
                }
                renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(target);
            }
            if (renderer == null) return null;

            var rendererObject = new SerializedObject(renderer);
            var features = rendererObject.FindProperty("m_RendererFeatures");
            if (features != null)
            {
                ScreenSpaceAmbientOcclusion ssao = null;
                for (int i = 0; i < features.arraySize; i++)
                {
                    var existing = features.GetArrayElementAtIndex(i).objectReferenceValue as ScreenSpaceAmbientOcclusion;
                    if (existing != null) { ssao = existing; break; }
                }
                if (ssao == null)
                {
                    ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                    ssao.name = "RTS Screen Space Ambient Occlusion";
                    AssetDatabase.AddObjectToAsset(ssao, renderer);
                    features.arraySize++;
                    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
                }
                ssao.SetActive(true);
                rendererObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(ssao);
            }
            EditorUtility.SetDirty(renderer);
            return renderer;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
