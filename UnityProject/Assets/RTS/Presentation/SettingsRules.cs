using System;
using System.Collections.Generic;

namespace Rts.Presentation
{
    public enum GraphicsQualityLevel { Low, Medium, High, Ultra, Custom }

    public struct GraphicsQualityValues : IEquatable<GraphicsQualityValues>
    {
        public int ShadowResolution;
        public float ShadowDistance;
        public int ShadowCascades;
        public int Msaa;
        public float RenderScale;
        public bool ToneMapping;
        public bool Bloom;
        public bool Ssao;
        public bool HighQualitySsao;
        public bool Vignette;
        public float LodBias;
        public bool Anisotropic;
        public int ForestTrees;
        public int MountainRocks;
        public int Decorations;
        public bool ShowFps;
        /// <summary>模型と絵の細かさ（0＝低＝今の模型、1＝中、2＝高、3＝最高）。docs/settings.md の 2.1</summary>
        public int ModelDetail;

        public bool Equals(GraphicsQualityValues other)
        {
            return ShadowResolution == other.ShadowResolution && Math.Abs(ShadowDistance - other.ShadowDistance) < 0.001f
                && ShadowCascades == other.ShadowCascades && Msaa == other.Msaa
                && Math.Abs(RenderScale - other.RenderScale) < 0.001f && ToneMapping == other.ToneMapping
                && Bloom == other.Bloom && Ssao == other.Ssao && HighQualitySsao == other.HighQualitySsao
                && Vignette == other.Vignette && Math.Abs(LodBias - other.LodBias) < 0.001f
                && Anisotropic == other.Anisotropic && ForestTrees == other.ForestTrees
                && MountainRocks == other.MountainRocks && Decorations == other.Decorations && ShowFps == other.ShowFps
                && ModelDetail == other.ModelDetail;
        }

        public override bool Equals(object obj) { return obj is GraphicsQualityValues && Equals((GraphicsQualityValues)obj); }
        public override int GetHashCode() { return ShadowResolution ^ ShadowCascades ^ Msaa ^ ForestTrees ^ MountainRocks ^ Decorations; }
    }

    /// <summary>Unity 非依存の画質段・カスタム判定。低は既存の見た目を表す値のまま。</summary>
    public static class GraphicsQualityRules
    {
        public static GraphicsQualityValues For(GraphicsQualityLevel level)
        {
            switch (level)
            {
                case GraphicsQualityLevel.Medium:
                    return Values(2048, 220f, 4, 2, 1f, false, false, false, false, false, 2.5f, true, 4500, 1800, 16000, false, 1);
                case GraphicsQualityLevel.High:
                    return Values(4096, 280f, 4, 4, 1f, true, true, true, false, true, 3f, true, 6000, 2400, 28000, false, 2);
                case GraphicsQualityLevel.Ultra:
                    return Values(4096, 350f, 4, 8, 1.25f, true, true, true, true, true, 4f, true, 9000, 3600, 48000, false, 3);
                default:
                    // Low = the project's URP asset (2048, 160 m, 4 cascades, no MSAA) and the
                    // Ultra quality level (LOD bias 2, anisotropic forced on). GraphicsQuality restores those, it does not write them.
                    return Values(2048, 160f, 4, 0, 1f, false, false, false, false, false, 2f, true, 3000, 1200, 8000, false, 0);
            }
        }

        public static GraphicsQualityLevel Detect(GraphicsQualityValues value)
        {
            for (int i = 0; i < 4; i++)
            {
                var level = (GraphicsQualityLevel)i;
                if (value.Equals(For(level))) return level;
            }
            return GraphicsQualityLevel.Custom;
        }

        /// <summary>
        /// 11-08 に木・岩・草の数を地図全体に散らす形へ増やした。それより前に保存したカスタム値（旧の 4 段の数）を新しい段の数へ読み替える。
        /// </summary>
        public static GraphicsQualityValues UpgradeOldNatureCounts(GraphicsQualityValues value)
        {
            value.ForestTrees = Upgrade(value.ForestTrees, new[] { 600, 900, 1200, 1800 }, new[] { 3000, 4500, 6000, 9000 });
            value.MountainRocks = Upgrade(value.MountainRocks, new[] { 300, 450, 600, 900 }, new[] { 1200, 1800, 2400, 3600 });
            value.Decorations = Upgrade(value.Decorations, new[] { 360, 540, 720, 1080 }, new[] { 8000, 16000, 28000, 48000 });
            return value;
        }

        private static int Upgrade(int value, int[] oldSteps, int[] newSteps)
        {
            for (int i = 0; i < oldSteps.Length; i++) if (value == oldSteps[i]) return newSteps[i];
            return value;
        }

        public static bool IsCustom(GraphicsQualityValues value) { return Detect(value) == GraphicsQualityLevel.Custom; }

        /// <summary>カスタム値が使う URP の土台。FPS 表示や飾り数だけの変更で中画質へ跳ねない。</summary>
        public static GraphicsQualityLevel PipelineLevel(GraphicsQualityValues value)
        {
            if (value.HighQualitySsao) return GraphicsQualityLevel.Ultra;
            if (value.Ssao || value.Bloom || value.Vignette || value.ToneMapping || value.Msaa >= 4 || value.ShadowResolution >= 4096)
                return GraphicsQualityLevel.High;
            if (value.Msaa >= 2 || value.ShadowDistance > 160f || value.LodBias > 2f)
                return GraphicsQualityLevel.Medium;
            return GraphicsQualityLevel.Low;
        }

        private static GraphicsQualityValues Values(int shadowResolution, float shadowDistance, int cascades, int msaa,
            float renderScale, bool toneMapping, bool bloom, bool ssao, bool highQualitySsao, bool vignette,
            float lodBias, bool anisotropic, int trees, int rocks, int decorations, bool showFps, int modelDetail)
        {
            return new GraphicsQualityValues
            {
                ShadowResolution = shadowResolution, ShadowDistance = shadowDistance, ShadowCascades = cascades,
                Msaa = msaa, RenderScale = renderScale, ToneMapping = toneMapping, Bloom = bloom, Ssao = ssao,
                HighQualitySsao = highQualitySsao, Vignette = vignette, LodBias = lodBias, Anisotropic = anisotropic,
                ForestTrees = trees, MountainRocks = rocks, Decorations = decorations, ShowFps = showFps,
                ModelDetail = modelDetail
            };
        }
    }

    public enum KeyBindingAction
    {
        SelectIdleVillagers, ToggleControlMode, MoveBuilding, RemoveOrCancel, MenuCancel, AddSelection,
        CameraUp, CameraDown, CameraLeft, CameraRight, Rotate, NextInserter, DetailMode
    }

    public struct KeyBindingPair
    {
        public KeyBindingAction Action;
        public int Key;
        public KeyBindingPair(KeyBindingAction action, int key) { Action = action; Key = key; }
    }

    /// <summary>キーの既定値・重なり・入れ替えを Unity なしで検査する小さな規則クラス。</summary>
    public static class KeyBindingRules
    {
        public static KeyBindingPair[] Defaults
        {
            get
            {
                return new[]
                {
                    new KeyBindingPair(KeyBindingAction.SelectIdleVillagers, 104), // H
                    new KeyBindingPair(KeyBindingAction.ToggleControlMode, 109), // M
                    new KeyBindingPair(KeyBindingAction.MoveBuilding, 118), // V
                    new KeyBindingPair(KeyBindingAction.RemoveOrCancel, 127), // Delete
                    new KeyBindingPair(KeyBindingAction.MenuCancel, 27), // Escape
                    new KeyBindingPair(KeyBindingAction.AddSelection, 304), // LeftShift
                    new KeyBindingPair(KeyBindingAction.CameraUp, 119), new KeyBindingPair(KeyBindingAction.CameraDown, 115),
                    new KeyBindingPair(KeyBindingAction.CameraLeft, 97), new KeyBindingPair(KeyBindingAction.CameraRight, 100),
                    new KeyBindingPair(KeyBindingAction.Rotate, 114), new KeyBindingPair(KeyBindingAction.NextInserter, 116),
                    new KeyBindingPair(KeyBindingAction.DetailMode, 308) // LeftAlt
                };
            }
        }

        public static List<KeyBindingAction> FindConflicts(IList<KeyBindingPair> values)
        {
            var result = new List<KeyBindingAction>();
            for (int i = 0; i < values.Count; i++)
                for (int j = i + 1; j < values.Count; j++)
                    if (values[i].Key == values[j].Key)
                    {
                        if (!result.Contains(values[i].Action)) result.Add(values[i].Action);
                        if (!result.Contains(values[j].Action)) result.Add(values[j].Action);
                    }
            return result;
        }

        public static void Swap(IList<KeyBindingPair> values, KeyBindingAction first, int key)
        {
            int index = -1, other = -1, old = key;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Action == first) { index = i; old = values[i].Key; }
                else if (values[i].Key == key) other = i;
            }
            if (index < 0) return;
            values[index] = new KeyBindingPair(first, key);
            if (other >= 0) values[other] = new KeyBindingPair(values[other].Action, old);
        }
    }

    public struct ResolutionKey : IEquatable<ResolutionKey>
    {
        public int Width, Height;
        public ResolutionKey(int width, int height) { Width = width; Height = height; }
        public bool Equals(ResolutionKey other) { return Width == other.Width && Height == other.Height; }
        public override bool Equals(object obj) { return obj is ResolutionKey && Equals((ResolutionKey)obj); }
        public override int GetHashCode() { return Width * 397 ^ Height; }
        public override string ToString() { return Width + " × " + Height; }
    }

    public static class ResolutionRules
    {
        public static List<ResolutionKey> Deduplicate(IEnumerable<ResolutionKey> input)
        {
            var result = new List<ResolutionKey>();
            var seen = new HashSet<ResolutionKey>();
            foreach (var value in input) if (value.Width > 0 && value.Height > 0 && seen.Add(value)) result.Add(value);
            return result;
        }
    }

    public enum DisplayMode { Borderless, Exclusive, Windowed }
    public enum HealthBarMode { Standard, Always, DamagedOnly, SelectedOnly }

    public sealed class SettingsValues
    {
        public GraphicsQualityValues Quality = GraphicsQualityRules.For(GraphicsQualityLevel.Low);
        public ResolutionKey Resolution = new ResolutionKey(1280, 720);
        public DisplayMode DisplayMode = DisplayMode.Borderless;
        public bool VSync;
        public int FpsLimit = 0;
        public int UiScale = 100;
        public int Gamma;
        public bool MasterMute, MusicMute, EffectsMute, UiMute;
        public bool MuteWhenUnfocused;
        public float MasterVolume = 1f, MusicVolume = 1f, EffectsVolume = 1f, UiVolume = 1f;
        public float CameraSpeed = 1f, ZoomSpeed = 1f;
        public bool InvertZoom = false, EdgeScroll = false;
        public int EdgeWidth = 16, DragThreshold = 8;
        public bool Japanese = true, DamageNumbers = true, Tooltips = true;
        public HealthBarMode HealthBars = HealthBarMode.Standard;
        public int DefaultGameSpeed = 1;
        public SettingsValues Clone()
        {
            return (SettingsValues)MemberwiseClone();
        }
    }
}
