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
        private const string UssResourcePath = "Hud/HudTheme";

        private BattlefieldView view;
        private UIDocument document;
        private PanelSettings panelSettings;
        private FontAsset runtimeFont;
        private VisualElement hudRoot;
        private Label ageLabel;
        private VisualElement resourceRow;
        private Label populationLabel;
        private readonly List<TopBarResourceVisibility.ResourceEntry> resources =
            new List<TopBarResourceVisibility.ResourceEntry>();
        private readonly List<ResourceSlot> slots = new List<ResourceSlot>();
        private string lastAge = "";
        private string lastPopulation = "";
        private bool warnedMissingFont;

        private sealed class ResourceSlot
        {
            public TopBarResourceVisibility.ResourceKind Kind;
            public string Label;
            public Label ValueLabel;
            public int Value;
        }

        /// <summary>PlayerPrefs is intentionally opt-in; the command-line flag is a convenient temporary override.</summary>
        public static bool IsEnabled
        {
            get
            {
                if (PlayerPrefs.GetInt(SettingKey, 0) == 1) return true;
                foreach (var argument in System.Environment.GetCommandLineArgs())
                    if (argument == "-hud-toolkit") return true;
                return false;
            }
        }

        public void Bind(BattlefieldView battlefield)
        {
            view = battlefield;
            enabled = true;
            EnsureDocument();
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
            if (hudRoot == null || ageLabel == null || resourceRow == null || populationLabel == null) return;
            var frame = view.LatestFrame;
            var economy = frame == null ? null : frame.Economy;
            if (economy == null)
            {
                hudRoot.style.display = DisplayStyle.None;
                return;
            }

            hudRoot.style.display = DisplayStyle.Flex;
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
            var theme = Resources.Load<StyleSheet>(UssResourcePath);
            if (theme != null) root.styleSheets.Add(theme);
            else Debug.LogWarning("UI Toolkit HUD USS not found at Resources/" + UssResourcePath + ".");
            tree.CloneTree(root);

            hudRoot = root.Q<VisualElement>("hud-root");
            ageLabel = root.Q<Label>("age-label");
            resourceRow = root.Q<VisualElement>("resource-row");
            populationLabel = root.Q<Label>("population-label");
            if (hudRoot == null || ageLabel == null || resourceRow == null || populationLabel == null)
            {
                Debug.LogWarning("UI Toolkit HUD UXML is missing one of the required top-bar elements.");
                return;
            }
            root.pickingMode = PickingMode.Ignore;
            hudRoot.pickingMode = PickingMode.Ignore;
            runtimeFont = CreateJapaneseFont();
            if (runtimeFont != null) root.style.unityFontDefinition = new StyleFontDefinition(runtimeFont);
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

            int idle = CountIdle(economy);
            string population = TopBarDisplayText.Population(economy.Population, economy.PopulationCap, idle);
            if (population != lastPopulation)
            {
                populationLabel.text = population;
                lastPopulation = population;
            }

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

        private FontAsset CreateJapaneseFont()
        {
            foreach (var family in new[] { "Yu Gothic UI", "Meiryo", "MS Gothic" })
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
            if (!warnedMissingFont)
            {
                warnedMissingFont = true;
                Debug.LogWarning("UI Toolkit HUD could not create a Japanese OS font; using the default UI Toolkit font.");
            }
            return null;
        }

        private void OnDestroy()
        {
            if (runtimeFont != null) Destroy(runtimeFont);
            if (panelSettings != null) Destroy(panelSettings);
        }
    }
}
