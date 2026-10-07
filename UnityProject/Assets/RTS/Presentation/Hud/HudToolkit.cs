using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Rts.Presentation
{
    /// <summary>
    /// Runtime-owned UI Toolkit shell for the HUD. The scene stays unchanged: this component creates its PanelSettings
    /// and UIDocument when the toolkit switch is on, then reads only the presentation contracts.
    /// </summary>
    public sealed class HudToolkit : MonoBehaviour
    {
        public const string SettingKey = "rts.hud.toolkit";
        private const string UxmlResourcePath = "Hud/TopBar";
        private const string BaseUssResourcePath = "Hud/HudTheme";

        private BattlefieldView view;
        private UIDocument document;
        private PanelSettings panelSettings;
        private StyleSheet baseTheme;
        private StyleSheet activeTheme;
        private VisualElement hudRoot;
        private Label ageLabel;
        private Label ageStageLabel;
        private VisualElement resourceRow;
        private Label populationLabel;
        private Label idleLabel;
        private readonly List<TopBarResourceVisibility.ResourceEntry> resources =
            new List<TopBarResourceVisibility.ResourceEntry>();
        private readonly List<ResourceSlot> slots = new List<ResourceSlot>();
        private readonly ThemeFontSet[] themeFonts = new ThemeFontSet[HudThemeCatalog.Count];
        private string lastAge = "";
        private string lastAgeStage = "";
        private string lastPopulation = "";
        private string lastIdle = "";
        private int activeThemeIndex = -1;
        private bool warnedMissingFont;

        private sealed class ThemeFontSet
        {
            public bool Attempted;
            public FontAsset Heading;
            public FontAsset Body;
        }

        private sealed class ResourceSlot
        {
            public TopBarResourceVisibility.ResourceKind Kind;
            public string Label;
            public Label ValueLabel;
            public int Value;
        }

        private static bool? commandLineFlag;
        private static bool turnedOff;

        /// <summary>
        /// PlayerPrefs is intentionally opt-in; the command-line flag is a convenient temporary override that the
        /// setup panel's switch can still turn off for this run. The flag is read once, not on every GUI event.
        /// </summary>
        public static bool IsEnabled
        {
            get
            {
                if (PlayerPrefs.GetInt(SettingKey, 0) == 1) return true;
                if (!commandLineFlag.HasValue)
                    commandLineFlag = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hud-toolkit") >= 0;
                return commandLineFlag.Value && !turnedOff;
            }
        }

        /// <summary>The setup panel's switch: remembered on this PC, and takes effect on the next frame.</summary>
        public static void SetEnabled(bool on)
        {
            PlayerPrefs.SetInt(SettingKey, on ? 1 : 0);
            PlayerPrefs.Save();
            turnedOff = !on;
        }

        public static int ThemeIndex
        {
            get { return HudThemeCatalog.ClampIndex(PlayerPrefs.GetInt(HudThemeCatalog.PlayerPrefsKey, 0)); }
        }

        /// <summary>The selected theme is applied by the HUD update on the next frame.</summary>
        public static void SetTheme(int index)
        {
            PlayerPrefs.SetInt(HudThemeCatalog.PlayerPrefsKey, HudThemeCatalog.ClampIndex(index));
            PlayerPrefs.Save();
        }

        public void Bind(BattlefieldView battlefield)
        {
            view = battlefield;
            enabled = true;
            if (IsEnabled) EnsureDocument();
        }

        private void OnEnable()
        {
            if (IsEnabled) EnsureDocument();
        }

        private void Update()
        {
            if (!IsEnabled || view == null)
            {
                if (hudRoot != null) hudRoot.style.display = DisplayStyle.None;
                return;
            }

            EnsureDocument();
            if (hudRoot == null || ageLabel == null || ageStageLabel == null || resourceRow == null ||
                populationLabel == null || idleLabel == null) return;
            var frame = view.LatestFrame;
            var economy = frame == null ? null : frame.Economy;
            if (economy == null)
            {
                hudRoot.style.display = DisplayStyle.None;
                return;
            }

            hudRoot.style.display = DisplayStyle.Flex;
            ApplySelectedTheme();
            Refresh(economy);

            // Register the same screen-pixel rectangle used by the IMGUI top bar. The next input frame therefore
            // treats this Toolkit panel as occupied and does not let map selection or orders leak underneath it.
            UiHitAreas.Shared.BeginFrame(Time.frameCount);
            UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).TopLeft);
        }

        private void EnsureDocument()
        {
            if (document != null) return;

            // ScaleWithScreenSize keeps the design coordinates stable while adapting the HUD to different windows;
            // 1920x1080 is the reference used by the existing screen-pixel layout and leaves room for compact screens.
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Runtime HUD PanelSettings";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);

            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var tree = Resources.Load<VisualTreeAsset>(UxmlResourcePath);
            if (tree == null)
            {
                Debug.LogWarning("UI Toolkit HUD UXML not found at Resources/" + UxmlResourcePath + ".");
                return;
            }

            var root = document.rootVisualElement;
            baseTheme = Resources.Load<StyleSheet>(BaseUssResourcePath);
            if (baseTheme != null) root.styleSheets.Add(baseTheme);
            else Debug.LogWarning("UI Toolkit HUD USS not found at Resources/" + BaseUssResourcePath + ".");
            tree.CloneTree(root);

            hudRoot = root.Q<VisualElement>("hud-root");
            ageLabel = root.Q<Label>("age-label");
            ageStageLabel = root.Q<Label>("age-stage-label");
            resourceRow = root.Q<VisualElement>("resource-row");
            populationLabel = root.Q<Label>("population-label");
            idleLabel = root.Q<Label>("idle-label");
            if (hudRoot == null || ageLabel == null || ageStageLabel == null || resourceRow == null ||
                populationLabel == null || idleLabel == null)
            {
                Debug.LogWarning("UI Toolkit HUD UXML is missing one of the required top-bar elements.");
                return;
            }
            root.pickingMode = PickingMode.Ignore;
            hudRoot.pickingMode = PickingMode.Ignore;
            ApplySelectedTheme();
        }

        private void ApplySelectedTheme()
        {
            if (document == null || hudRoot == null) return;
            int selected = ThemeIndex;
            if (selected == activeThemeIndex) return;

            var root = document.rootVisualElement;
            if (activeTheme != null) root.styleSheets.Remove(activeTheme);
            var definition = HudThemeCatalog.Get(selected);
            activeTheme = Resources.Load<StyleSheet>(definition.ResourcePath);
            if (activeTheme != null) root.styleSheets.Add(activeTheme);
            else Debug.LogWarning("UI Toolkit HUD theme USS not found at Resources/" + definition.ResourcePath + ".");
            activeThemeIndex = selected;
            ApplyFonts(root, selected, definition);
        }

        private void ApplyFonts(VisualElement root, int themeIndex, HudThemeCatalog.Definition definition)
        {
            var fonts = themeFonts[themeIndex];
            if (fonts == null)
            {
                fonts = new ThemeFontSet();
                themeFonts[themeIndex] = fonts;
            }
            if (!fonts.Attempted)
            {
                fonts.Attempted = true;
                fonts.Heading = CreateFont(definition.HeadingFontFamilies);
                fonts.Body = CreateFont(definition.BodyFontFamilies);
                if (fonts.Heading == null) fonts.Heading = fonts.Body;
            }

            if (fonts.Body != null) root.style.unityFontDefinition = new StyleFontDefinition(fonts.Body);
            if (fonts.Heading != null)
            {
                root.Query<VisualElement>(className: "hud-heading").ForEach(element =>
                    element.style.unityFontDefinition = new StyleFontDefinition(fonts.Heading));
            }
            else if (!warnedMissingFont)
            {
                warnedMissingFont = true;
                Debug.LogWarning("UI Toolkit HUD could not create a theme font; using the default UI Toolkit font.");
            }
        }

        private void Refresh(EconomyView economy)
        {
            string age = TopBarDisplayText.Age(economy);
            if (age != lastAge)
            {
                // The IMGUI line ends the age with a "  |  " separator; the toolkit bar separates by layout instead.
                ageLabel.text = age.TrimEnd(' ', '|');
                lastAge = age;
            }

            string ageStage = TopBarDisplayText.AgeStage(economy);
            if (ageStage != lastAgeStage)
            {
                ageStageLabel.text = ageStage;
                lastAgeStage = ageStage;
            }

            int idle = CountIdle(economy);
            string population = UiText.T("Pop ", "人口 ") + TopBarDisplayText.FormatCount(economy.Population)
                + "/" + TopBarDisplayText.FormatCount(economy.PopulationCap);
            if (population != lastPopulation)
            {
                populationLabel.text = population;
                lastPopulation = population;
            }
            string idleText = UiText.T("Idle ", "待機 ") + TopBarDisplayText.FormatCount(idle);
            if (idleText != lastIdle)
            {
                idleLabel.text = idleText;
                lastIdle = idleText;
            }
            idleLabel.EnableInClassList("is-warning", idle > 0);

            TopBarResourceVisibility.Fill(economy, resources);
            bool sameLayout = resources.Count == slots.Count;
            if (sameLayout)
            {
                for (int i = 0; i < resources.Count; i++)
                {
                    var entry = resources[i];
                    if (slots[i].Kind != entry.Kind || slots[i].Label != entry.Label)
                    {
                        sameLayout = false;
                        break;
                    }
                }
            }
            if (!sameLayout) RebuildResourceRow();

            for (int i = 0; i < resources.Count; i++)
            {
                var entry = resources[i];
                var slot = slots[i];
                if (slot.Value == entry.Value) continue;
                slot.Value = entry.Value;
                slot.ValueLabel.text = TopBarDisplayText.FormatCount(entry.Value);
            }
        }

        private void RebuildResourceRow()
        {
            resourceRow.Clear();
            slots.Clear();
            foreach (var entry in resources)
            {
                var card = new VisualElement { name = "resource-" + entry.Kind };
                card.AddToClassList("hud-resource");
                var icon = new VisualElement { name = "icon-slot" };
                icon.AddToClassList("hud-resource-icon");
                var text = new VisualElement { name = "resource-text" };
                text.AddToClassList("hud-resource-text");
                var name = new Label(entry.Label);
                name.AddToClassList("hud-resource-name");
                var value = new Label();
                value.AddToClassList("hud-resource-value");
                value.AddToClassList("hud-number");
                text.Add(name);
                text.Add(value);
                card.Add(icon);
                card.Add(text);
                resourceRow.Add(card);
                slots.Add(new ResourceSlot { Kind = entry.Kind, Label = entry.Label, ValueLabel = value, Value = int.MinValue });
            }
        }

        private static int CountIdle(EconomyView economy)
        {
            int count = 0;
            foreach (var villager in economy.Villagers)
                if (villager.IsOwn && villager.Activity == VillagerActivity.Idle) count++;
            return count;
        }

        private static FontAsset CreateFont(string[] families)
        {
            foreach (var family in families)
            {
                try
                {
                    var asset = FontAsset.CreateFontAsset(family, "Regular", 32, 4, GlyphRenderMode.SDFAA);
                    if (asset != null) return asset;
                }
                catch (System.Exception)
                {
                    // The next family is the fallback. Font creation is optional and must not stop the HUD.
                }
            }
            return null;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < themeFonts.Length; i++)
            {
                var fonts = themeFonts[i];
                if (fonts == null) continue;
                if (fonts.Heading != null && fonts.Heading != fonts.Body) Destroy(fonts.Heading);
                if (fonts.Body != null) Destroy(fonts.Body);
            }
            if (panelSettings != null) Destroy(panelSettings);
        }
    }
}
