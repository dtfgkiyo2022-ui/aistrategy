using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rts.Presentation
{
    /// <summary>Display-only lighting and post effects. Quality is a saved presentation preference.</summary>
    public sealed class WorldVisualEffects : MonoBehaviour
    {
        public const string QualityKey = "rts.world.visual.effects.high";
        private Volume volume;
        private Bloom bloom;
        private Vignette vignette;
        private ColorAdjustments colorAdjustments;
        private bool built;

        public static bool IsHighQuality { get { return PlayerPrefs.GetInt(QualityKey, 1) != 0; } }

        public static WorldVisualEffects Ensure(GameObject owner)
        {
            var effects = owner.GetComponent<WorldVisualEffects>();
            if (effects == null) effects = owner.AddComponent<WorldVisualEffects>();
            effects.BuildIfNeeded();
            effects.ApplyQuality(IsHighQuality);
            return effects;
        }

        public static void SetQuality(bool high)
        {
            PlayerPrefs.SetInt(QualityKey, high ? 1 : 0);
            PlayerPrefs.Save();
            var all = FindObjectsOfType<WorldVisualEffects>();
            foreach (var effect in all) effect.ApplyQuality(high);
        }

        private void Awake() { BuildIfNeeded(); ApplyQuality(IsHighQuality); }

        private void BuildIfNeeded()
        {
            if (built) return;
            built = true;
            ConfigureSun();
            var volumeObject = new GameObject("WorldGlobalVolume");
            volumeObject.transform.SetParent(transform, false);
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = -1f;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile.name = "Runtime World Visual Effects";
            bloom = volume.profile.Add<Bloom>(true);
            bloom.threshold.value = 1.05f; bloom.intensity.value = 0.28f; bloom.scatter.value = 0.65f;
            vignette = volume.profile.Add<Vignette>(true);
            vignette.intensity.value = 0.11f; vignette.smoothness.value = 0.72f; vignette.rounded.value = true;
            colorAdjustments = volume.profile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.value = 0.05f; colorAdjustments.contrast.value = 8f; colorAdjustments.saturation.value = 10f;
            var tone = volume.profile.Add<Tonemapping>(true); tone.mode.value = TonemappingMode.ACES;
        }

        private static void ConfigureSun()
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                var go = new GameObject("RTS Evening Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
                RenderSettings.sun = sun;
            }
            sun.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            sun.color = new Color(1.0f, 0.78f, 0.60f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.78f;
            sun.shadowBias = 0.045f;
            sun.shadowNormalBias = 0.35f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.20f, 0.25f, 0.38f);
        }

        private void ApplyQuality(bool high)
        {
            BuildIfNeeded();
            bloom.active = high;
            vignette.active = true;
            colorAdjustments.active = true;
            // A renderer feature can opt into this shared switch. The default renderer has no SSAO feature,
            // so the low-poly scene remains portable; an installed feature can read the same global value.
            Shader.SetGlobalFloat("_RTS_SSAO_STRENGTH", high ? 0.16f : 0f);
        }

        private void OnDestroy()
        {
            if (volume != null && volume.profile != null) Destroy(volume.profile);
        }
    }
}
