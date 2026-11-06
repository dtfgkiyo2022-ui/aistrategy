using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rts.Presentation
{
    /// <summary>画質の実行時反映を一か所に集める。設定を変更した瞬間ではなく「適用」時だけ呼ぶ。</summary>
    public static class GraphicsQuality
    {
        private static RenderPipelineAsset lowPipeline;
        private static GameObject volumeObject;
        private static VolumeProfile volumeProfile;
        private static GraphicsQualityValues current;

        public static GraphicsQualityValues Current { get { return current; } }

        // The state before the settings screen touched anything. Low puts exactly this back instead of writing its own
        // numbers, so Low stays the old look even if the project's quality level or URP asset is changed later.
        private static bool snapshotTaken;
        private static int originalAntiAliasing;
        private static float originalShadowDistance;
        private static int originalShadowCascades;
        private static float originalLodBias;
        private static AnisotropicFiltering originalAnisotropic;
        private static bool originalCameraPostProcessing;
        private static bool hasTerrainLimits;
        private static GraphicsQualityValues terrainLimits;
        private static RenderPipelineAsset customPipeline;
        private static GraphicsQualityLevel customPipelineBase = GraphicsQualityLevel.Custom;

        public static void Apply(GraphicsQualityValues value, int gammaPercent)
        {
            TakeSnapshot();
            current = value;
            GraphicsQualityLevel level = GraphicsQualityRules.Detect(value);
            bool low = level == GraphicsQualityLevel.Low;
            if (low)
            {
                QualitySettings.renderPipeline = lowPipeline;
                QualitySettings.antiAliasing = originalAntiAliasing;
                QualitySettings.shadowDistance = originalShadowDistance;
                QualitySettings.shadowCascades = originalShadowCascades;
                QualitySettings.lodBias = originalLodBias;
                QualitySettings.anisotropicFiltering = originalAnisotropic;
            }
            else
            {
                QualitySettings.antiAliasing = value.Msaa;
                QualitySettings.shadowDistance = value.ShadowDistance;
                QualitySettings.shadowCascades = value.ShadowCascades;
                QualitySettings.lodBias = value.LodBias;
                QualitySettings.anisotropicFiltering = value.Anisotropic
                    ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Disable;
                var pipeline = PipelineFor(value, level);
                if (pipeline != null) QualitySettings.renderPipeline = pipeline;
            }
            ApplyVolume(value, gammaPercent, low);
            ApplyCameraPostProcessing(value, gammaPercent, low);

            // Rebuilding the terrain decoration is slow; only when the counts really change (not on the first Low).
            var limits = GraphicsQualityRules.For(GraphicsQualityLevel.Low);
            var previous = hasTerrainLimits ? terrainLimits : limits;
            hasTerrainLimits = true;
            terrainLimits = value;
            if (previous.ForestTrees == value.ForestTrees && previous.MountainRocks == value.MountainRocks
                && previous.Decorations == value.Decorations) return;
            var view = Object.FindFirstObjectByType<BattlefieldView>();
            if (view != null) view.RebuildTerrainForSettings(value);
        }

        private static void TakeSnapshot()
        {
            if (snapshotTaken) return;
            snapshotTaken = true;
            lowPipeline = QualitySettings.renderPipeline;
            originalAntiAliasing = QualitySettings.antiAliasing;
            originalShadowDistance = QualitySettings.shadowDistance;
            originalShadowCascades = QualitySettings.shadowCascades;
            originalLodBias = QualitySettings.lodBias;
            originalAnisotropic = QualitySettings.anisotropicFiltering;
            var camera = Camera.main;
            var data = camera != null ? camera.GetComponent<UniversalAdditionalCameraData>() : null;
            originalCameraPostProcessing = data != null && data.renderPostProcessing;
        }

        /// <summary>A preset uses its generated asset as it is. Custom values change a copy, never an asset on disk.</summary>
        private static RenderPipelineAsset PipelineFor(GraphicsQualityValues value, GraphicsQualityLevel level)
        {
            if (level != GraphicsQualityLevel.Custom) return Resources.Load<RenderPipelineAsset>(PipelineResource(value));
            GraphicsQualityLevel baseLevel = GraphicsQualityRules.PipelineLevel(value);
            RenderPipelineAsset source = baseLevel == GraphicsQualityLevel.Low ? lowPipeline
                : Resources.Load<RenderPipelineAsset>(PipelineResource(GraphicsQualityRules.For(baseLevel)));
            if (source == null) return null;
            if (customPipeline == null || customPipelineBase != baseLevel)
            {
                if (customPipeline != null) Object.Destroy(customPipeline);
                customPipeline = Object.Instantiate(source);
                customPipeline.name = source.name + " (custom)";
                customPipelineBase = baseLevel;
            }
            var universal = customPipeline as UniversalRenderPipelineAsset;
            if (universal != null)
            {
                universal.msaaSampleCount = value.Msaa <= 0 ? 1 : value.Msaa;
                universal.renderScale = value.RenderScale;
                universal.shadowDistance = value.ShadowDistance;
                universal.shadowCascadeCount = value.ShadowCascades;
            }
            return customPipeline;
        }

        public static void ApplyLevel(GraphicsQualityLevel level, int gammaPercent = 0)
        {
            Apply(GraphicsQualityRules.For(level), gammaPercent);
        }

        private static string PipelineResource(GraphicsQualityValues value)
        {
            GraphicsQualityLevel level = GraphicsQualityRules.Detect(value);
            if (level == GraphicsQualityLevel.Custom) level = GraphicsQualityRules.PipelineLevel(value);
            switch (level)
            {
                case GraphicsQualityLevel.Medium: return "Graphics/RtsURP_Medium";
                case GraphicsQualityLevel.High: return "Graphics/RtsURP_High";
                case GraphicsQualityLevel.Ultra: return "Graphics/RtsURP_Ultra";
                default: return "";
            }
        }

        private static void ApplyCameraPostProcessing(GraphicsQualityValues value, int gammaPercent, bool low)
        {
            var camera = Camera.main;
            if (camera == null) return;
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) return;
            bool wanted = value.ToneMapping || value.Bloom || value.Vignette || gammaPercent != 0;
            data.renderPostProcessing = low && gammaPercent == 0 ? originalCameraPostProcessing : wanted || originalCameraPostProcessing;
        }

        private static void ApplyVolume(GraphicsQualityValues value, int gammaPercent, bool low)
        {
            // Low with no brightness change: no volume of ours at all, as before the settings screen.
            if (low && gammaPercent == 0)
            {
                if (volumeObject != null) volumeObject.SetActive(false);
                return;
            }
            EnsureVolume();
            volumeObject.SetActive(true);
            var tone = Get<Tonemapping>();
            tone.active = value.ToneMapping;
            tone.mode.overrideState = value.ToneMapping;
            tone.mode.value = TonemappingMode.ACES;

            var bloom = Get<Bloom>();
            bloom.active = value.Bloom;
            bloom.intensity.overrideState = value.Bloom;
            bloom.intensity.value = value.Bloom ? (value.HighQualitySsao ? 0.35f : 0.18f) : 0f;
            bloom.threshold.overrideState = value.Bloom;
            bloom.threshold.value = 1.1f;

            var vignette = Get<Vignette>();
            vignette.active = value.Vignette;
            vignette.intensity.overrideState = value.Vignette;
            vignette.intensity.value = value.Vignette ? 0.16f : 0f;

            var color = Get<ColorAdjustments>();
            color.active = true;
            color.postExposure.overrideState = true;
            color.postExposure.value = gammaPercent * 0.01f;
            color.saturation.overrideState = value.ToneMapping;
            color.saturation.value = value.ToneMapping ? 8f : 0f;
            color.contrast.overrideState = value.ToneMapping;
            color.contrast.value = value.ToneMapping ? 5f : 0f;
        }

        private static T Get<T>() where T : VolumeComponent
        {
            T component;
            if (!volumeProfile.TryGet(out component)) component = volumeProfile.Add<T>(true);
            return component;
        }

        private static void EnsureVolume()
        {
            if (volumeObject != null && volumeProfile != null) return;
            volumeObject = new GameObject("RTS Global Visual Volume");
            Object.DontDestroyOnLoad(volumeObject);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volumeProfile.name = "RTS Runtime Visual Volume Profile";
            volume.profile = volumeProfile;
        }
    }
}
