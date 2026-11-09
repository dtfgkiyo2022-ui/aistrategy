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
            ApplyLook(level == GraphicsQualityLevel.Custom ? GraphicsQualityRules.PipelineLevel(value) : level);

            // docs/settings.md 2.1: a different model tier rebuilds the buildings and resources already on the map.
            if (ArtDetail.Set(value.ModelDetail))
            {
                var layer = Object.FindFirstObjectByType<EconomyLayer>();
                if (layer != null) layer.RebuildArtForSettings();
            }

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

        private static bool ambientTaken;
        private static bool sunTaken;
        private static float originalSunIntensity, originalSunShadow;
        private static AmbientMode originalAmbientMode;
        private static Color originalAmbient, originalSky, originalEquator, originalGround;

        /// <summary>
        /// 11-09 High and Ultra, 11-10 every level: relief on the ground, light from the sky above and the ground below
        /// instead of one flat colour, for the terrain and nature shaders and for the models; a softer, bluer fog edge.
        /// </summary>
        private static void ApplyLook(GraphicsQualityLevel level)
        {
            if (!ambientTaken)
            {
                ambientTaken = true;
                originalAmbientMode = RenderSettings.ambientMode;
                originalAmbient = RenderSettings.ambientLight;
                originalSky = RenderSettings.ambientSkyColor;
                originalEquator = RenderSettings.ambientEquatorColor;
                originalGround = RenderSettings.ambientGroundColor;
            }
            // 11-10: every level. The look costs no more than the old light (a few texture reads on the ground), and the
            // test play of build1109g found it clearly better. The old light is kept below for a quick way back.
            bool look = true;
            Shader.SetGlobalFloat("_RtsLook", look ? 1f : 0f);
            Shader.SetGlobalFloat("_RtsWear", level == GraphicsQualityLevel.Low ? 0f : 1f);
            // Cloud shadows drifting over the ground, nature and water (11-10, every level; a few noise reads).
            Shader.SetGlobalFloat("_RtsCloud", 0.22f);
            Shader.SetGlobalFloat("_RtsBump", level == GraphicsQualityLevel.Ultra ? 7f : 4.5f);
            Shader.SetGlobalVector("_RtsSky", new Vector4(0.62f, 0.68f, 0.82f, 0f));
            Shader.SetGlobalVector("_RtsGround", new Vector4(0.34f, 0.31f, 0.25f, 0f));
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                if (!sunTaken) { sunTaken = true; originalSunIntensity = sun.intensity; originalSunShadow = sun.shadowStrength; }
                // A brighter sun and deeper shadows give the buildings and trees their shape (11-09).
                sun.intensity = look ? originalSunIntensity * 1.12f : originalSunIntensity;
                sun.shadowStrength = look ? Mathf.Min(0.92f, originalSunShadow + 0.1f) : originalSunShadow;
            }
            if (look)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.46f, 0.53f, 0.70f);
                RenderSettings.ambientEquatorColor = new Color(0.36f, 0.38f, 0.38f);
                RenderSettings.ambientGroundColor = new Color(0.22f, 0.19f, 0.15f);
            }
            else
            {
                RenderSettings.ambientMode = originalAmbientMode;
                RenderSettings.ambientLight = originalAmbient;
                RenderSettings.ambientSkyColor = originalSky;
                RenderSettings.ambientEquatorColor = originalEquator;
                RenderSettings.ambientGroundColor = originalGround;
            }
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
            // Medium and above always: the colour grading (11-10).
            bool wanted = !low || value.ToneMapping || value.Bloom || value.Vignette || gammaPercent != 0;
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
            // High and Ultra (11-09), Medium too (11-10): a calmer green, cool shadows and warm sunlit tops instead of
            // one strong green. Without ACES (Medium) the contrast is a little lower.
            bool graded = !low;
            color.saturation.overrideState = graded;
            color.saturation.value = graded ? -12f : 0f;
            color.contrast.overrideState = graded;
            color.contrast.value = graded ? (value.ToneMapping ? 12f : 8f) : 0f;
            color.postExposure.value = gammaPercent * 0.01f + (graded && value.ToneMapping ? 0.12f : 0f);

            var split = Get<ShadowsMidtonesHighlights>();
            split.active = graded;
            split.shadows.overrideState = graded;
            split.shadows.value = new Vector4(0.90f, 0.96f, 1.10f, 0f);
            split.highlights.overrideState = graded;
            split.highlights.value = new Vector4(1.07f, 1.02f, 0.92f, 0f);

            var white = Get<WhiteBalance>();
            white.active = graded;
            white.temperature.overrideState = graded;
            white.temperature.value = graded ? 6f : 0f;

            // Ultra (11-09, "High and Ultra should be prettier"): the far part of the view, the top of the screen,
            // softens a little, as in a photograph of a model town. TiltShiftFocus moves the focus with the camera.
            bool tilt = value.HighQualitySsao && !low;
            var focus = Get<DepthOfField>();
            focus.active = tilt;
            focus.mode.overrideState = tilt;
            focus.mode.value = tilt ? DepthOfFieldMode.Gaussian : DepthOfFieldMode.Off;
            focus.gaussianMaxRadius.overrideState = tilt;
            focus.gaussianMaxRadius.value = 0.9f;
            focus.highQualitySampling.overrideState = tilt;
            focus.highQualitySampling.value = true;
            focus.gaussianStart.overrideState = tilt;
            focus.gaussianEnd.overrideState = tilt;
            var follower = volumeObject.GetComponent<TiltShiftFocus>();
            if (follower == null) follower = volumeObject.AddComponent<TiltShiftFocus>();
            follower.Bind(tilt ? focus : null);
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
            if (Application.isPlaying) Object.DontDestroyOnLoad(volumeObject);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volumeProfile.name = "RTS Runtime Visual Volume Profile";
            volume.profile = volumeProfile;
        }
    }
}
