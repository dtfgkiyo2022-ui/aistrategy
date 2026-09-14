using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rts.Editor
{
    public static class ProjectSkeletonSetup
    {
        private const string PipelinePath = "Assets/Settings/RtsUniversalRenderPipeline.asset";
        private const string RendererPath = "Assets/Settings/RtsUniversalRenderer.asset";

        // Explicit entry point: never overwrite project settings on editor startup.
        [MenuItem("RTS/Set Up Project Skeleton")]
        public static void Configure()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                AssetDatabase.CreateFolder("Assets", "Settings");

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int originalQuality = QualitySettings.GetQualityLevel();
            try
            {
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = pipeline;
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(originalQuality, false);
            }

            EditorSettings.serializationMode = SerializationMode.ForceText;
            EditorSettings.lineEndingsForNewScripts = LineEndingsMode.Unix;
            UnityEditor.VersionControlSettings.mode = "Visible Meta Files";
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSkeleton] Configured URP for Graphics and all "
                + QualitySettings.names.Length + " quality levels; Force Text; Visible Meta Files.");
        }
    }
}
