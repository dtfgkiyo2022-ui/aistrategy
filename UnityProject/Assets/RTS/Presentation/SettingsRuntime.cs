using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    public static class DisplaySettingsRuntime
    {
        public static HealthBarMode HealthBars = HealthBarMode.Standard;
        /// <summary>Structures show their health only for a while after a hit (the look before the settings screen).</summary>
        public static bool UsesHitRule { get { return HealthBars == HealthBarMode.Standard || HealthBars == HealthBarMode.DamagedOnly; } }
        public static bool DamageNumbers = true;
        public static bool Tooltips = true;
        public static int DefaultGameSpeed = 1;

        public static void Apply(SettingsValues value)
        {
            HealthBars = value.HealthBars;
            DamageNumbers = value.DamageNumbers;
            Tooltips = value.Tooltips;
            DefaultGameSpeed = value.DefaultGameSpeed == 4 || value.DefaultGameSpeed == 2 ? value.DefaultGameSpeed : 1;
        }

        public static bool ShouldShowHealthBar(int hp, int maxHp, bool selected)
        {
            switch (HealthBars)
            {
                case HealthBarMode.DamagedOnly: return hp < maxHp;
                case HealthBarMode.SelectedOnly: return selected;
                default: return true;
            }
        }
    }

    public static class CameraSettingsRuntime
    {
        public static float MoveMultiplier = 1f;
        public static float ZoomMultiplier = 1f;
        public static bool InvertZoom;
        public static bool EdgeScroll = false;
        public static int EdgeWidth = 16;
        public static int DragThreshold = 8;

        public static void Apply(SettingsValues value)
        {
            MoveMultiplier = Mathf.Clamp(value.CameraSpeed, 0.5f, 2f);
            ZoomMultiplier = Mathf.Clamp(value.ZoomSpeed, 0.5f, 2f);
            InvertZoom = value.InvertZoom;
            EdgeScroll = value.EdgeScroll;
            EdgeWidth = Mathf.Clamp(value.EdgeWidth, 4, 64);
            DragThreshold = Mathf.Clamp(value.DragThreshold, 4, 20);
        }
    }

    public static class SettingsStore
    {
        private const string Prefix = "rts.settings.";
        public const string LanguageKey = "rts.language.japanese";

        public static SettingsValues Load()
        {
            var result = new SettingsValues();
            int savedQuality = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "quality", 0), 0, 4);
            result.Quality = savedQuality == (int)GraphicsQualityLevel.Custom
                ? LoadCustomQuality() : GraphicsQualityRules.For((GraphicsQualityLevel)savedQuality);
            result.Resolution = new ResolutionKey(PlayerPrefs.GetInt(Prefix + "width", Screen.width), PlayerPrefs.GetInt(Prefix + "height", Screen.height));
            result.DisplayMode = (DisplayMode)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "display", 0), 0, 2);
            result.VSync = PlayerPrefs.GetInt(Prefix + "vsync", 0) == 1;
            result.FpsLimit = PlayerPrefs.GetInt(Prefix + "fps", 0);
            result.UiScale = PlayerPrefs.GetInt(Prefix + "uiScale", 100);
            result.Gamma = PlayerPrefs.GetInt(Prefix + "gamma", 0);
            result.MasterVolume = PlayerPrefs.GetFloat(GameAudio.MasterKey, 1f);
            result.MusicVolume = PlayerPrefs.GetFloat(GameAudio.MusicKey, 1f);
            result.EffectsVolume = PlayerPrefs.GetFloat(GameAudio.EffectsKey, 1f);
            result.UiVolume = PlayerPrefs.GetFloat(GameAudio.UiKey, 1f);
            result.MasterMute = GameAudio.MasterMuted;
            result.MusicMute = GameAudio.MusicMuted;
            result.EffectsMute = GameAudio.EffectsMuted;
            result.UiMute = GameAudio.UiMuted;
            result.MuteWhenUnfocused = GameAudio.MuteWhenUnfocused;
            result.CameraSpeed = PlayerPrefs.GetFloat(Prefix + "cameraSpeed", 1f);
            result.ZoomSpeed = PlayerPrefs.GetFloat(Prefix + "zoomSpeed", 1f);
            result.InvertZoom = PlayerPrefs.GetInt(Prefix + "invertZoom", 0) == 1;
            result.EdgeScroll = PlayerPrefs.GetInt(Prefix + "edgeScroll", 0) == 1;
            result.EdgeWidth = PlayerPrefs.GetInt(Prefix + "edgeWidth", 16);
            result.DragThreshold = PlayerPrefs.GetInt(Prefix + "dragThreshold", 8);
            result.Japanese = PlayerPrefs.GetInt(LanguageKey, 1) == 1;
            result.HealthBars = (HealthBarMode)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "healthBars", 0), 0, 3);
            result.DamageNumbers = PlayerPrefs.GetInt(Prefix + "damageNumbers", 1) == 1;
            result.Tooltips = PlayerPrefs.GetInt(Prefix + "tooltips", 1) == 1;
            result.DefaultGameSpeed = PlayerPrefs.GetInt(Prefix + "gameSpeed", 1);
            return result;
        }

        public static void Save(SettingsValues value)
        {
            GraphicsQualityLevel level = GraphicsQualityRules.Detect(value.Quality);
            PlayerPrefs.SetInt(Prefix + "quality", (int)level);
            SaveQuality(value.Quality);
            PlayerPrefs.SetInt(Prefix + "width", value.Resolution.Width); PlayerPrefs.SetInt(Prefix + "height", value.Resolution.Height);
            PlayerPrefs.SetInt(Prefix + "display", (int)value.DisplayMode); PlayerPrefs.SetInt(Prefix + "vsync", value.VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "fps", value.FpsLimit); PlayerPrefs.SetInt(Prefix + "uiScale", value.UiScale); PlayerPrefs.SetInt(Prefix + "gamma", value.Gamma);
            GameAudio.MasterVolume = value.MasterVolume; GameAudio.MusicVolume = value.MusicVolume;
            GameAudio.EffectsVolume = value.EffectsVolume; GameAudio.UiVolume = value.UiVolume;
            GameAudio.MasterMuted = value.MasterMute; GameAudio.MusicMuted = value.MusicMute;
            GameAudio.EffectsMuted = value.EffectsMute; GameAudio.UiMuted = value.UiMute;
            GameAudio.MuteWhenUnfocused = value.MuteWhenUnfocused;
            PlayerPrefs.SetFloat(Prefix + "cameraSpeed", value.CameraSpeed); PlayerPrefs.SetFloat(Prefix + "zoomSpeed", value.ZoomSpeed);
            PlayerPrefs.SetInt(Prefix + "invertZoom", value.InvertZoom ? 1 : 0); PlayerPrefs.SetInt(Prefix + "edgeScroll", value.EdgeScroll ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "edgeWidth", value.EdgeWidth); PlayerPrefs.SetInt(Prefix + "dragThreshold", value.DragThreshold);
            PlayerPrefs.SetInt(LanguageKey, value.Japanese ? 1 : 0); PlayerPrefs.SetInt(Prefix + "healthBars", (int)value.HealthBars);
            PlayerPrefs.SetInt(Prefix + "damageNumbers", value.DamageNumbers ? 1 : 0); PlayerPrefs.SetInt(Prefix + "tooltips", value.Tooltips ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "gameSpeed", value.DefaultGameSpeed); PlayerPrefs.Save();
        }

        public static void Apply(SettingsValues value)
        {
            GraphicsQuality.Apply(value.Quality, value.Gamma);
            CameraSettingsRuntime.Apply(value);
            QualitySettings.vSyncCount = value.VSync ? 1 : 0;
            Application.targetFrameRate = value.FpsLimit <= 0 ? -1 : value.FpsLimit;
            FullScreenMode mode = value.DisplayMode == DisplayMode.Exclusive ? FullScreenMode.ExclusiveFullScreen
                : value.DisplayMode == DisplayMode.Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            if (Screen.width != value.Resolution.Width || Screen.height != value.Resolution.Height || Screen.fullScreenMode != mode)
                Screen.SetResolution(value.Resolution.Width, value.Resolution.Height, mode);
            UiText.Japanese = value.Japanese;
            DisplaySettingsRuntime.Apply(value);
            KeyBindings.Load();
            var toolkit = Object.FindFirstObjectByType<HudToolkit>();
            if (toolkit != null) toolkit.SetUiScale(value.UiScale);
        }

        public static List<ResolutionKey> AvailableResolutions()
        {
            var raw = new List<ResolutionKey>();
            foreach (var resolution in Screen.resolutions) raw.Add(new ResolutionKey(resolution.width, resolution.height));
            return ResolutionRules.Deduplicate(raw);
        }

        private static GraphicsQualityValues LoadCustomQuality()
        {
            var value = GraphicsQualityRules.For(GraphicsQualityLevel.Low);
            value.ShadowResolution = PlayerPrefs.GetInt(Prefix + "quality.shadowResolution", value.ShadowResolution);
            value.ShadowDistance = PlayerPrefs.GetFloat(Prefix + "quality.shadowDistance", value.ShadowDistance);
            value.ShadowCascades = PlayerPrefs.GetInt(Prefix + "quality.shadowCascades", value.ShadowCascades);
            value.Msaa = PlayerPrefs.GetInt(Prefix + "quality.msaa", value.Msaa);
            value.RenderScale = PlayerPrefs.GetFloat(Prefix + "quality.renderScale", value.RenderScale);
            value.ToneMapping = PlayerPrefs.GetInt(Prefix + "quality.toneMapping", value.ToneMapping ? 1 : 0) == 1;
            value.Bloom = PlayerPrefs.GetInt(Prefix + "quality.bloom", value.Bloom ? 1 : 0) == 1;
            value.Ssao = PlayerPrefs.GetInt(Prefix + "quality.ssao", value.Ssao ? 1 : 0) == 1;
            value.HighQualitySsao = PlayerPrefs.GetInt(Prefix + "quality.highQualitySsao", value.HighQualitySsao ? 1 : 0) == 1;
            value.Vignette = PlayerPrefs.GetInt(Prefix + "quality.vignette", value.Vignette ? 1 : 0) == 1;
            value.LodBias = PlayerPrefs.GetFloat(Prefix + "quality.lodBias", value.LodBias);
            value.Anisotropic = PlayerPrefs.GetInt(Prefix + "quality.anisotropic", value.Anisotropic ? 1 : 0) == 1;
            value.ForestTrees = PlayerPrefs.GetInt(Prefix + "quality.forestTrees", value.ForestTrees);
            value.MountainRocks = PlayerPrefs.GetInt(Prefix + "quality.mountainRocks", value.MountainRocks);
            value.Decorations = PlayerPrefs.GetInt(Prefix + "quality.decorations", value.Decorations);
            value.ShowFps = PlayerPrefs.GetInt(Prefix + "quality.showFps", value.ShowFps ? 1 : 0) == 1;
            value.ModelDetail = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "quality.modelDetail", value.ModelDetail), 0, 3);
            return GraphicsQualityRules.UpgradeOldNatureCounts(value);
        }

        private static void SaveQuality(GraphicsQualityValues value)
        {
            PlayerPrefs.SetInt(Prefix + "quality.shadowResolution", value.ShadowResolution);
            PlayerPrefs.SetFloat(Prefix + "quality.shadowDistance", value.ShadowDistance);
            PlayerPrefs.SetInt(Prefix + "quality.shadowCascades", value.ShadowCascades);
            PlayerPrefs.SetInt(Prefix + "quality.msaa", value.Msaa);
            PlayerPrefs.SetFloat(Prefix + "quality.renderScale", value.RenderScale);
            PlayerPrefs.SetInt(Prefix + "quality.toneMapping", value.ToneMapping ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "quality.bloom", value.Bloom ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "quality.ssao", value.Ssao ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "quality.highQualitySsao", value.HighQualitySsao ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "quality.vignette", value.Vignette ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "quality.lodBias", value.LodBias);
            PlayerPrefs.SetInt(Prefix + "quality.anisotropic", value.Anisotropic ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "quality.forestTrees", value.ForestTrees);
            PlayerPrefs.SetInt(Prefix + "quality.mountainRocks", value.MountainRocks);
            PlayerPrefs.SetInt(Prefix + "quality.decorations", value.Decorations);
            PlayerPrefs.SetInt(Prefix + "quality.showFps", value.ShowFps ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "quality.modelDetail", value.ModelDetail);
        }
    }
}
