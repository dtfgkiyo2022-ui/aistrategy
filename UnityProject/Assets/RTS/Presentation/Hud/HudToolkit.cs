using System;
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
        private const string RuntimeThemeResourcePath = "Hud/HudRuntimeTheme";
        private const string StaffUxmlResourcePath = "Hud/Staff";
        private const string CommandsUxmlResourcePath = "Hud/Commands";
        private const string EconomyUxmlResourcePath = "Hud/Economy";
        private const string SupplyUxmlResourcePath = "Hud/Supply";
        private const string TimelineUxmlResourcePath = "Hud/Timeline";
        private const string ClockUxmlResourcePath = "Hud/Clock";
        private const string SetupUxmlResourcePath = "Hud/Setup";
        private const string LogUxmlResourcePath = "Hud/Log";
        private const string ResultUxmlResourcePath = "Hud/Result";
        private const string BaseUssResourcePath = "Hud/HudTheme";
        private const string RegularFontResourcePath = "Hud/Fonts/NotoSansJP-Regular";
        private const string BoldFontResourcePath = "Hud/Fonts/NotoSansJP-Bold";

        private static readonly TopBarResourceVisibility.ResourceKind[] IconKinds =
        {
            TopBarResourceVisibility.ResourceKind.Food,
            TopBarResourceVisibility.ResourceKind.Wood,
            TopBarResourceVisibility.ResourceKind.Ore,
            TopBarResourceVisibility.ResourceKind.Metal,
            TopBarResourceVisibility.ResourceKind.Stone,
            TopBarResourceVisibility.ResourceKind.Gems,
            TopBarResourceVisibility.ResourceKind.Gold,
            TopBarResourceVisibility.ResourceKind.Charcoal,
            TopBarResourceVisibility.ResourceKind.Steel,
            TopBarResourceVisibility.ResourceKind.BowGear
        };

        private BattlefieldView view;
        private UIDocument document;
        private PanelSettings panelSettings;
        private StyleSheet baseTheme;
        private StyleSheet activeTheme;
        private VisualElement hudRoot;
        private VisualElement topFrame;
        private Label ageLabel;
        private Label ageStageLabel;
        private VisualElement resourceRow;
        private VisualElement populationIcon;
        private Label populationLabel;
        private Label idleLabel;
        private VisualElement staffFrame;
        private VisualElement staffPanel;
        private ScrollView staffScroll;
        private VisualElement staffOptions;
        private Label staffTargetLabel;
        private Label staffEstimateLabel;
        private Label staffCostLabel;
        private Label staffNoticeLabel;
        private Label staffEmptyLabel;
        private Button staffAiButton;
        private Button staffSendButton;
        private Button staffCancelButton;
        private TextField staffInput;
        private IStaffControl staff;
        private CommandPanel commandPanel;
        private VisualElement commandsFrame;
        private VisualElement commandsPanel;
        private Label commandsTitle;
        private Label commandsHelp;
        private Label commandsSelectionTitle;
        private Label commandsSelectionDetail;
        private Label commandsSelectionHint;
        private Button commandAttack;
        private Button commandRetreat;
        private Button commandDefend;
        private Button commandAbandon;
        private Button commandReserve;
        private Button commandAuto;
        private Button commandCancel;
        private EconomyPanel economyPanel;
        private VisualElement economyFrame;
        private VisualElement economyActions;
        private Label economyTitle;
        private Label economyHeaderStatus;
        private Label economyHint;
        private Label economyNotice;
        private Button economyTabBuild;
        private Button economyTabMake;
        private Button economyTabResearch;
        private Button economyTabPolicy;
        private CommandPanel supplySource;
        private VisualElement supplyFrame;
        private Label supplyTitle;
        private Label supplyUnits;
        private Label supplyLine0;
        private Label supplyLine1;
        private TimelinePanel timelineSource;
        private VisualElement timelineFrame;
        private Label timelineTitle;
        private ScrollView timelineScroll;
        private VisualElement clockFrame;
        private Label clockInfo;
        private Button clockPause;
        private Button clockStep;
        private Button clockSpeed1;
        private Button clockSpeed2;
        private Button clockSpeed4;
        private Button clockFaction;
        private VisualElement setupControlsFrame;
        private Button setupToggle;
        private Button languageToggle;
        private VisualElement setupFrame;
        private Label setupTitle;
        private ScrollView setupScroll;
        private VisualElement setupContent;
        private VisualElement logToggleFrame;
        private Button logToggle;
        private VisualElement logStatusFrame;
        private Label logStatusTitle;
        private ScrollView logStatusScroll;
        private VisualElement logEntriesFrame;
        private Label logEntriesTitle;
        private ScrollView logEntriesScroll;
        private VisualElement resultFrame;
        private Label resultHeadline;
        private Label resultDetail;
        private Label resultPack;
        private Button resultRestart;
        private bool staffInputFocused;
        private bool staffAiListOpen;
        private string staffAiSignature = "";
        private int renderedStaffLineCount;
        private readonly List<StaffLineSlot> staffLineSlots = new List<StaffLineSlot>();
        private readonly List<EconomyActionSlot> economyActionSlots = new List<EconomyActionSlot>();
        private readonly List<TimelineLineSlot> timelineLineSlots = new List<TimelineLineSlot>();
        private readonly List<TopBarResourceVisibility.ResourceEntry> resources =
            new List<TopBarResourceVisibility.ResourceEntry>();
        private readonly List<ResourceSlot> slots = new List<ResourceSlot>();
        private readonly ThemeFontSet[] themeFonts = new ThemeFontSet[HudThemeCatalog.Count];
        private readonly Dictionary<TopBarResourceVisibility.ResourceKind, Texture2D> resourceIcons =
            new Dictionary<TopBarResourceVisibility.ResourceKind, Texture2D>();
        private readonly List<FontAsset> createdFontAssets = new List<FontAsset>();
        private FontAsset bundledRegularFont;
        private FontAsset bundledBoldFont;
        private Texture2D populationIconTexture;
        private bool hudAssetsLoaded;
        private string lastAge = "";
        private string lastAgeStage = "";
        private string lastPopulation = "";
        private string lastIdle = "";
        private int activeThemeIndex = -1;
        private bool warnedMissingFont;
        private string economyActionSignature = "";
        private int renderedTimelineLineCount;
        private long renderedTimelineLastTick = long.MinValue;
        private string renderedTimelineLastText = "";

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
            public VisualElement Icon;
            public Label ValueLabel;
            public int Value;
        }

        private sealed class StaffLineSlot
        {
            public VisualElement Row;
            public VisualElement Bubble;
            public Label Label;
        }

        private sealed class EconomyActionSlot
        {
            public VisualElement Row;
            public Button Button;
            public Label Reason;
        }

        private sealed class TimelineLineSlot
        {
            public Label Label;
        }


        /// <summary>
        /// The new screen is the default (owner, 10-09). The setup panel's switch can still turn it off on this PC, and
        /// the start argument -hud-legacy starts with the old screen for this run. Arguments are read once.
        /// </summary>
        public static bool IsEnabled
        {
            get
            {
                if (!legacyFlag.HasValue)
                    legacyFlag = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hud-legacy") >= 0;
                if (legacyFlag.Value && !switchedThisRun) return false;
                return PlayerPrefs.GetInt(SettingKey, 1) == 1;
            }
        }

        private static bool? legacyFlag;
        private static bool switchedThisRun;

        /// <summary>The setup panel's switch: remembered on this PC, and takes effect on the next frame.</summary>
        public static void SetEnabled(bool on)
        {
            PlayerPrefs.SetInt(SettingKey, on ? 1 : 0);
            PlayerPrefs.Save();
            switchedThisRun = true;
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
            Bind(battlefield, null);
        }

        public void Bind(BattlefieldView battlefield, IStaffControl staffControl)
        {
            Bind(battlefield, null, staffControl, null);
        }

        public void Bind(BattlefieldView battlefield, IStaffControl staffControl, CommandPanel commandControl)
        {
            Bind(battlefield, null, staffControl, commandControl, null, null);
        }

        public void Bind(BattlefieldView battlefield, EconomyPanel economyControl, IStaffControl staffControl, CommandPanel commandControl)
        {
            Bind(battlefield, economyControl, staffControl, commandControl, commandControl, null);
        }

        public void Bind(BattlefieldView battlefield, EconomyPanel economyControl, IStaffControl staffControl,
            CommandPanel commandControl, CommandPanel supplyControl, TimelinePanel timelineControl)
        {
            view = battlefield;
            economyPanel = economyControl;
            staff = staffControl;
            commandPanel = commandControl;
            supplySource = supplyControl;
            timelineSource = timelineControl;
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
                if (economyFrame != null) economyFrame.style.display = DisplayStyle.None;
                if (staffFrame != null) staffFrame.style.display = DisplayStyle.None;
                if (commandsFrame != null) commandsFrame.style.display = DisplayStyle.None;
                if (supplyFrame != null) supplyFrame.style.display = DisplayStyle.None;
                if (timelineFrame != null) timelineFrame.style.display = DisplayStyle.None;
                if (clockFrame != null) clockFrame.style.display = DisplayStyle.None;
                HideCommandOverlays();
                SetStaffInputFocus(false);
                return;
            }

            EnsureDocument();
            if (hudRoot == null || ageLabel == null || ageStageLabel == null || resourceRow == null ||
                populationLabel == null || idleLabel == null) return;
            var frame = view.LatestFrame;
            var economy = frame == null ? null : frame.Economy;
            if (economy == null && commandPanel == null && staff == null)
            {
                hudRoot.style.display = DisplayStyle.None;
                if (economyFrame != null) economyFrame.style.display = DisplayStyle.None;
                if (staffFrame != null) staffFrame.style.display = DisplayStyle.None;
                if (commandsFrame != null) commandsFrame.style.display = DisplayStyle.None;
                if (supplyFrame != null) supplyFrame.style.display = DisplayStyle.None;
                if (timelineFrame != null) timelineFrame.style.display = DisplayStyle.None;
                if (clockFrame != null) clockFrame.style.display = DisplayStyle.None;
                HideCommandOverlays();
                SetStaffInputFocus(false);
                return;
            }

            hudRoot.style.display = DisplayStyle.Flex;
            LogLayoutOnce();
            ApplySelectedTheme();
            if (topFrame != null) topFrame.style.display = economy == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (economy != null) Refresh(economy);
            RefreshEconomy(economy);
            RefreshStaff();
            RefreshCommands();
            RefreshSupply();
            RefreshTimeline();
            RefreshClock();
            RefreshSetup();
            RefreshLog();
            RefreshResult();

            // Register the same screen-pixel rectangle used by the IMGUI top bar. The next input frame therefore
            // treats this Toolkit panel as occupied and does not let map selection or orders leak underneath it.
            UiHitAreas.Shared.BeginFrame(Time.frameCount);
            UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).TopLeft);
            if (staffFrame != null && staffFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).Strategist);
            if (commandsFrame != null && commandsFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).Commands);
            if (economyFrame != null && economyFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).Economy);
            if (supplyFrame != null && supplyFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).Supply);
            if (timelineFrame != null && timelineFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).Timeline);
            if (clockFrame != null && clockFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).TopCenter);
            if (setupControlsFrame != null && setupControlsFrame.resolvedStyle.display != DisplayStyle.None)
            {
                var topRight = UiLayout.Calculate(Screen.width, Screen.height).TopRight;
                UiHitAreas.Shared.Register(new Rect(topRight.x, topRight.y, topRight.width * 0.64f, 28f));
                UiHitAreas.Shared.Register(new Rect(topRight.x + topRight.width * 0.66f, topRight.y, topRight.width * 0.34f, 28f));
            }
            if (setupFrame != null && setupFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).Setup);
            if (logToggleFrame != null && logToggleFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).LogToggle);
            if (logStatusFrame != null && logStatusFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).LogStatus);
            if (logEntriesFrame != null && logEntriesFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).LogEntries);
            if (resultFrame != null && resultFrame.resolvedStyle.display != DisplayStyle.None)
                UiHitAreas.Shared.Register(UiLayout.Calculate(Screen.width, Screen.height).Result);
        }

        private float layoutLogAt = -1f;

        /// <summary>
        /// Writes the laid-out size of the root and the HUD panels once, a second after the HUD first shows, so a
        /// collapsed layout can be read from Editor.log without a screenshot (the panels collapsed once, 10-08).
        /// </summary>
        private void LogLayoutOnce()
        {
            if (layoutLogAt == float.MaxValue) return;
            if (layoutLogAt < 0f) { layoutLogAt = Time.unscaledTime + 1f; return; }
            if (Time.unscaledTime < layoutLogAt) return;
            layoutLogAt = float.MaxValue;
            var root = document.rootVisualElement;
            Debug.Log("RTS HUD layout: root=" + root.worldBound
                + " top=" + Bound(root.Q<VisualElement>("top-bar"))
                 + " staff=" + Bound(root.Q<VisualElement>(className: "hud-staff-frame"))
                 + " commands=" + Bound(root.Q<VisualElement>(className: "hud-commands-frame"))
                 + " economy=" + Bound(root.Q<VisualElement>(className: "hud-economy-frame"))
                + " font=" + (root.resolvedStyle.unityFontDefinition.fontAsset != null ? root.resolvedStyle.unityFontDefinition.fontAsset.name : "none"));
        }

        private static string Bound(VisualElement element)
        {
            return element == null ? "missing" : element.worldBound.ToString();
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
            // Without a theme the document root gets no size, so percentage widths and the bars collapse to thin lines
            // (seen 10-08: "No Theme Style Sheet set to PanelSettings"). The .tss only imports Unity's default theme.
            var runtimeTheme = Resources.Load<ThemeStyleSheet>(RuntimeThemeResourcePath);
            if (runtimeTheme != null) panelSettings.themeStyleSheet = runtimeTheme;
            else Debug.LogWarning("UI Toolkit HUD theme not found at Resources/" + RuntimeThemeResourcePath + ".");

            LoadHudAssets();

            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var tree = Resources.Load<VisualTreeAsset>(UxmlResourcePath);
            if (tree == null)
            {
                Debug.LogWarning("UI Toolkit HUD UXML not found at Resources/" + UxmlResourcePath + ".");
                return;
            }

            var root = document.rootVisualElement;
            // The theme sheets define their variables on this class, not :root (see Themes/*.uss).
            root.AddToClassList("hud-document");
            root.style.flexGrow = 1f;
            baseTheme = Resources.Load<StyleSheet>(BaseUssResourcePath);
            if (baseTheme != null) root.styleSheets.Add(baseTheme);
            else Debug.LogWarning("UI Toolkit HUD USS not found at Resources/" + BaseUssResourcePath + ".");
            tree.CloneTree(root);
            var staffTree = Resources.Load<VisualTreeAsset>(StaffUxmlResourcePath);
            if (staffTree != null) staffTree.CloneTree(root);
            else Debug.LogWarning("UI Toolkit staff UXML not found at Resources/" + StaffUxmlResourcePath + ".");
            var commandsTree = Resources.Load<VisualTreeAsset>(CommandsUxmlResourcePath);
            if (commandsTree != null) commandsTree.CloneTree(root);
            else Debug.LogWarning("UI Toolkit commands UXML not found at Resources/" + CommandsUxmlResourcePath + ".");
            var economyTree = Resources.Load<VisualTreeAsset>(EconomyUxmlResourcePath);
            if (economyTree != null) economyTree.CloneTree(root);
            else Debug.LogWarning("UI Toolkit economy UXML not found at Resources/" + EconomyUxmlResourcePath + ".");
            var supplyTree = Resources.Load<VisualTreeAsset>(SupplyUxmlResourcePath);
            if (supplyTree != null) supplyTree.CloneTree(root);
            else Debug.LogWarning("UI Toolkit supply UXML not found at Resources/" + SupplyUxmlResourcePath + ".");
            var timelineTree = Resources.Load<VisualTreeAsset>(TimelineUxmlResourcePath);
            if (timelineTree != null) timelineTree.CloneTree(root);
            else Debug.LogWarning("UI Toolkit timeline UXML not found at Resources/" + TimelineUxmlResourcePath + ".");
            var clockTree = Resources.Load<VisualTreeAsset>(ClockUxmlResourcePath);
            if (clockTree != null) clockTree.CloneTree(root);
            else Debug.LogWarning("UI Toolkit clock UXML not found at Resources/" + ClockUxmlResourcePath + ".");
            var setupTree = Resources.Load<VisualTreeAsset>(SetupUxmlResourcePath);
            if (setupTree != null) setupTree.CloneTree(root);
            else Debug.LogWarning("UI Toolkit setup UXML not found at Resources/" + SetupUxmlResourcePath + ".");
            var logTree = Resources.Load<VisualTreeAsset>(LogUxmlResourcePath);
            if (logTree != null) logTree.CloneTree(root);
            else Debug.LogWarning("UI Toolkit log UXML not found at Resources/" + LogUxmlResourcePath + ".");
            var resultTree = Resources.Load<VisualTreeAsset>(ResultUxmlResourcePath);
            if (resultTree != null) resultTree.CloneTree(root);
            else Debug.LogWarning("UI Toolkit result UXML not found at Resources/" + ResultUxmlResourcePath + ".");

            hudRoot = root.Q<VisualElement>("hud-root");
            topFrame = root.Q<VisualElement>("top-frame");
            ageLabel = root.Q<Label>("age-label");
            ageStageLabel = root.Q<Label>("age-stage-label");
            resourceRow = root.Q<VisualElement>("resource-row");
            populationIcon = root.Q<VisualElement>("population-icon");
            if (populationIcon != null && populationIconTexture != null)
                populationIcon.style.backgroundImage = new StyleBackground(populationIconTexture);
            populationLabel = root.Q<Label>("population-label");
            idleLabel = root.Q<Label>("idle-label");
            staffFrame = root.Q<VisualElement>("staff-frame");
            staffPanel = root.Q<VisualElement>("staff-panel");
            staffScroll = root.Q<ScrollView>("staff-scroll");
            staffOptions = root.Q<VisualElement>("staff-ai-options");
            staffTargetLabel = root.Q<Label>("staff-target-label");
            staffEstimateLabel = root.Q<Label>("staff-estimate-label");
            staffCostLabel = root.Q<Label>("staff-cost-label");
            staffNoticeLabel = root.Q<Label>("staff-notice-label");
            staffEmptyLabel = root.Q<Label>("staff-empty-label");
            staffAiButton = root.Q<Button>("staff-ai-button");
            staffSendButton = root.Q<Button>("staff-send-button");
            staffCancelButton = root.Q<Button>("staff-cancel-button");
            staffInput = root.Q<TextField>("staff-input");
            commandsFrame = root.Q<VisualElement>("commands-frame");
            commandsPanel = root.Q<VisualElement>("commands-panel");
            commandsTitle = root.Q<Label>("commands-title");
            commandsHelp = root.Q<Label>("commands-help");
            commandsSelectionTitle = root.Q<Label>("commands-selection-title");
            commandsSelectionDetail = root.Q<Label>("commands-selection-detail");
            commandsSelectionHint = root.Q<Label>("commands-selection-hint");
            commandAttack = root.Q<Button>("command-attack");
            commandRetreat = root.Q<Button>("command-retreat");
            commandDefend = root.Q<Button>("command-defend");
            commandAbandon = root.Q<Button>("command-abandon");
            commandReserve = root.Q<Button>("command-reserve");
            commandAuto = root.Q<Button>("command-auto");
            commandCancel = root.Q<Button>("command-cancel");
            economyFrame = root.Q<VisualElement>("economy-frame");
            economyActions = root.Q<VisualElement>("economy-actions");
            economyTitle = root.Q<Label>("economy-title");
            economyHeaderStatus = root.Q<Label>("economy-header-status");
            economyHint = root.Q<Label>("economy-hint");
            economyNotice = root.Q<Label>("economy-notice");
            economyTabBuild = root.Q<Button>("economy-tab-build");
            economyTabMake = root.Q<Button>("economy-tab-make");
            economyTabResearch = root.Q<Button>("economy-tab-research");
            economyTabPolicy = root.Q<Button>("economy-tab-policy");
            supplyFrame = root.Q<VisualElement>("supply-frame");
            supplyTitle = root.Q<Label>("supply-title");
            supplyUnits = root.Q<Label>("supply-units");
            supplyLine0 = root.Q<Label>("supply-line-0");
            supplyLine1 = root.Q<Label>("supply-line-1");
            timelineFrame = root.Q<VisualElement>("timeline-frame");
            timelineTitle = root.Q<Label>("timeline-title");
            timelineScroll = root.Q<ScrollView>("timeline-scroll");
            clockFrame = root.Q<VisualElement>("clock-frame");
            clockInfo = root.Q<Label>("clock-info");
            clockPause = root.Q<Button>("clock-pause");
            clockStep = root.Q<Button>("clock-step");
            clockSpeed1 = root.Q<Button>("clock-speed-1");
            clockSpeed2 = root.Q<Button>("clock-speed-2");
            clockSpeed4 = root.Q<Button>("clock-speed-4");
            clockFaction = root.Q<Button>("clock-faction");
            setupControlsFrame = root.Q<VisualElement>("setup-controls-frame");
            setupToggle = root.Q<Button>("setup-toggle");
            languageToggle = root.Q<Button>("language-toggle");
            setupFrame = root.Q<VisualElement>("setup-frame");
            setupTitle = root.Q<Label>("setup-title");
            setupScroll = root.Q<ScrollView>("setup-scroll");
            setupContent = root.Q<VisualElement>("setup-content");
            logToggleFrame = root.Q<VisualElement>("log-toggle-frame");
            logToggle = root.Q<Button>("log-toggle");
            logStatusFrame = root.Q<VisualElement>("log-status-frame");
            logStatusTitle = root.Q<Label>("log-status-title");
            logStatusScroll = root.Q<ScrollView>("log-status-scroll");
            logEntriesFrame = root.Q<VisualElement>("log-entries-frame");
            logEntriesTitle = root.Q<Label>("log-entries-title");
            logEntriesScroll = root.Q<ScrollView>("log-entries-scroll");
            resultFrame = root.Q<VisualElement>("result-frame");
            resultHeadline = root.Q<Label>("result-headline");
            resultDetail = root.Q<Label>("result-detail");
            resultPack = root.Q<Label>("result-pack");
            resultRestart = root.Q<Button>("result-restart");
            if (hudRoot == null || ageLabel == null || ageStageLabel == null || resourceRow == null ||
                populationLabel == null || idleLabel == null)
            {
                Debug.LogWarning("UI Toolkit HUD UXML is missing one of the required top-bar elements.");
                return;
            }
            root.pickingMode = PickingMode.Ignore;
            hudRoot.pickingMode = PickingMode.Ignore;
            if (staffFrame != null) staffFrame.pickingMode = PickingMode.Position;
            if (staffPanel != null) staffPanel.pickingMode = PickingMode.Position;
            if (commandsFrame != null) commandsFrame.pickingMode = PickingMode.Position;
            if (commandsPanel != null) commandsPanel.pickingMode = PickingMode.Position;
            if (economyFrame != null) economyFrame.pickingMode = PickingMode.Position;
            if (supplyFrame != null) supplyFrame.pickingMode = PickingMode.Position;
            if (timelineFrame != null) timelineFrame.pickingMode = PickingMode.Position;
            if (clockFrame != null) clockFrame.pickingMode = PickingMode.Position;
            if (setupControlsFrame != null) setupControlsFrame.pickingMode = PickingMode.Position;
            if (setupFrame != null) setupFrame.pickingMode = PickingMode.Position;
            if (logToggleFrame != null) logToggleFrame.pickingMode = PickingMode.Position;
            if (logStatusFrame != null) logStatusFrame.pickingMode = PickingMode.Position;
            if (logEntriesFrame != null) logEntriesFrame.pickingMode = PickingMode.Position;
            if (resultFrame != null) resultFrame.pickingMode = PickingMode.Position;
            BindStaffEvents();
            BindCommandEvents();
            BindEconomyEvents();
            BindClockEvents();
            BindSetupEvents();
            BindLogEvents();
            BindResultEvents();
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
                if (definition.UseBundledBodyFont)
                {
                    fonts.Body = bundledRegularFont;
                    if (fonts.Body == null) fonts.Body = CreateOsFont(definition.BodyFontFamilies);
                }
                else
                {
                    fonts.Body = CreateOsFont(definition.BodyFontFamilies);
                }

                if (definition.UseBundledHeadingFont)
                {
                    fonts.Heading = bundledBoldFont;
                    if (fonts.Heading == null) fonts.Heading = CreateOsFont(definition.HeadingFontFamilies);
                }
                else
                {
                    fonts.Heading = CreateOsFont(definition.HeadingFontFamilies);
                    if (fonts.Heading == null) fonts.Heading = bundledBoldFont;
                }
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

        private void BindStaffEvents()
        {
            if (staffAiButton == null || staffSendButton == null || staffCancelButton == null || staffInput == null) return;
            staffAiButton.clicked += ToggleStaffAiList;
            staffSendButton.clicked += SendStaff;
            staffCancelButton.clicked += CancelStaff;
            staffInput.RegisterCallback<FocusInEvent>(_ => SetStaffInputFocus(true));
            staffInput.RegisterCallback<FocusOutEvent>(_ => SetStaffInputFocus(false));
            staffInput.RegisterCallback<KeyDownEvent>(OnStaffKeyDown);
        }

        private void BindCommandEvents()
        {
            if (commandAttack == null || commandRetreat == null || commandDefend == null || commandAbandon == null ||
                commandReserve == null || commandAuto == null || commandCancel == null) return;
            commandAttack.clicked += () => { if (commandPanel != null) commandPanel.BeginAttackPick(); };
            commandRetreat.clicked += () => { if (commandPanel != null) commandPanel.IssueRetreat(); };
            commandDefend.clicked += () => { if (commandPanel != null) commandPanel.IssueDefendOwnCore(); };
            commandAbandon.clicked += () => { if (commandPanel != null) commandPanel.IssueAllowAbandon(); };
            commandReserve.clicked += () => { if (commandPanel != null) commandPanel.IssueMaintainReserve(); };
            commandAuto.clicked += () => { if (commandPanel != null) commandPanel.IssueReturnToAuto(); };
            commandCancel.clicked += () => { if (commandPanel != null) commandPanel.CancelGroundPick(); };
        }

        private void BindEconomyEvents()
        {
            if (economyTabBuild == null || economyTabMake == null || economyTabResearch == null || economyTabPolicy == null) return;
            economyTabBuild.clicked += () => SelectEconomyTab(EconomyPanel.Tab.Build);
            economyTabMake.clicked += () => SelectEconomyTab(EconomyPanel.Tab.Make);
            economyTabResearch.clicked += () => SelectEconomyTab(EconomyPanel.Tab.Research);
            economyTabPolicy.clicked += () => SelectEconomyTab(EconomyPanel.Tab.Policy);
        }

        private void BindClockEvents()
        {
            if (clockPause != null) clockPause.clicked += () => { if (timelineSource != null) timelineSource.TogglePause(); };
            if (clockStep != null) clockStep.clicked += () => { if (timelineSource != null) timelineSource.StepOneTick(); };
            if (clockSpeed1 != null) clockSpeed1.clicked += () => { if (timelineSource != null) timelineSource.SetSpeed(1); };
            if (clockSpeed2 != null) clockSpeed2.clicked += () => { if (timelineSource != null) timelineSource.SetSpeed(2); };
            if (clockSpeed4 != null) clockSpeed4.clicked += () => { if (timelineSource != null) timelineSource.SetSpeed(4); };
            if (clockFaction != null) clockFaction.clicked += () => { if (timelineSource != null) timelineSource.ToggleViewFaction(); };
        }

        private void SelectEconomyTab(EconomyPanel.Tab tab)
        {
            if (economyPanel != null) economyPanel.SelectToolkitTab(tab);
        }

        private static void SetFrameRect(VisualElement frame, Rect rect, float width, float height)
        {
            if (frame == null) return;
            frame.style.left = Length.Percent(width <= 0f ? 0f : rect.x / width * 100f);
            frame.style.top = Length.Percent(height <= 0f ? 0f : rect.y / height * 100f);
            frame.style.width = Length.Percent(width <= 0f ? 0f : rect.width / width * 100f);
            frame.style.height = Length.Percent(height <= 0f ? 0f : rect.height / height * 100f);
        }

        private void HideCommandOverlays()
        {
            if (setupControlsFrame != null) setupControlsFrame.style.display = DisplayStyle.None;
            if (setupFrame != null) setupFrame.style.display = DisplayStyle.None;
            if (logToggleFrame != null) logToggleFrame.style.display = DisplayStyle.None;
            if (logStatusFrame != null) logStatusFrame.style.display = DisplayStyle.None;
            if (logEntriesFrame != null) logEntriesFrame.style.display = DisplayStyle.None;
            if (resultFrame != null) resultFrame.style.display = DisplayStyle.None;
        }

        private void BindSetupEvents()
        {
            if (setupToggle != null) setupToggle.clicked += () => { if (commandPanel != null) commandPanel.ToggleSetup(); };
            if (languageToggle != null) languageToggle.clicked += () => { if (commandPanel != null) commandPanel.ToggleLanguage(); };
        }

        private void BindLogEvents()
        {
            if (logToggle != null) logToggle.clicked += () => { if (commandPanel != null) commandPanel.ToggleLog(); };
        }

        private void BindResultEvents()
        {
            if (resultRestart != null)
                resultRestart.clicked += () => { if (commandPanel != null && commandPanel.MatchRestart != null) commandPanel.MatchRestart.RestartMatch(); };
        }

        private void RefreshSetup()
        {
            if (setupControlsFrame == null || setupFrame == null || commandPanel == null)
            {
                if (setupControlsFrame != null) setupControlsFrame.style.display = DisplayStyle.None;
                if (setupFrame != null) setupFrame.style.display = DisplayStyle.None;
                return;
            }

            var layout = UiLayout.Calculate(Screen.width, Screen.height);
            SetFrameRect(setupControlsFrame, layout.TopRight, Screen.width, Screen.height);
            setupControlsFrame.style.display = DisplayStyle.Flex;
            setupToggle.text = commandPanel.IsSetupOpen
                ? UiText.T("Match setup ▲", "試合の設定 ▲")
                : UiText.T("Match setup ▼", "試合の設定 ▼");
            languageToggle.text = UiText.Japanese ? "English" : "日本語";

            SetFrameRect(setupFrame, layout.Setup, Screen.width, Screen.height);
            setupFrame.style.display = commandPanel.IsSetupOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!commandPanel.IsSetupOpen || setupContent == null) return;
            if (setupTitle != null) setupTitle.text = UiText.T("Match setup", "試合の設定");

            // Settings are intentionally rebuilt only while the panel is open. Each control still calls the same
            // presentation interfaces as the legacy rows, while the scroll view handles the long list.
            setupContent.Clear();
            BuildSetupContent();
        }

        private VisualElement AddSetupRow(string title)
        {
            var row = new VisualElement();
            row.AddToClassList("setup-row");
            var label = new Label(title);
            label.AddToClassList("setup-label");
            row.Add(label);
            var content = new VisualElement();
            content.AddToClassList("setup-control");
            row.Add(content);
            setupContent.Add(row);
            return content;
        }

        private static Button AddSetupButton(VisualElement parent, string text, bool selected, Action action)
        {
            var button = new Button(action);
            button.text = text;
            button.AddToClassList("setup-button");
            button.EnableInClassList("is-selected", selected);
            parent.Add(button);
            return button;
        }

        private static void AddSetupText(VisualElement parent, string text, string className)
        {
            var label = new Label(text ?? "");
            label.AddToClassList(className);
            parent.Add(label);
        }

        private void BuildSetupContent()
        {
            var toolkit = new Toggle();
            toolkit.text = HudToolkit.IsEnabled
                ? UiText.T("On (trial)", "使う（試作）")
                : UiText.T("Off", "使わない");
            toolkit.value = HudToolkit.IsEnabled;
            toolkit.AddToClassList("setup-switch");
            toolkit.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != HudToolkit.IsEnabled) HudToolkit.SetEnabled(evt.newValue);
            });
            AddSetupRowWithControl(UiText.T("New screen", "新しい画面"), toolkit);

            var themes = AddSetupRow(UiText.T("HUD theme", "見た目の案"));
            for (int i = 0; i < HudThemeCatalog.Count; i++)
            {
                int themeIndex = i;
                var theme = HudThemeCatalog.Get(themeIndex);
                AddSetupButton(themes, UiText.T(theme.EnglishName, theme.JapaneseName), HudToolkit.ThemeIndex == themeIndex,
                    () => HudToolkit.SetTheme(themeIndex));
            }

            var delay = commandPanel.DelayControl;
            var delayContent = AddSetupRow(UiText.T("Reply delay", "返答の遅延"));
            int[] delayOptions = { 0, 60, 200, 400 };
            string[] delayNames = { "0s", "3s", "10s", "20s" };
            for (int i = 0; i < delayOptions.Length; i++)
            {
                int ticks = delayOptions[i];
                AddSetupButton(delayContent, delayNames[i], delay != null && delay.DelayTicks == ticks,
                    () => { if (delay != null) delay.DelayTicks = ticks; });
            }

            AddDoctrineChoices(UiText.T("Opponent", "相手の方針"), commandPanel.Opponent);
            AddDoctrineChoices(UiText.T("Own side", "自軍の方針"), commandPanel.OwnDoctrine);
            AddTacticSection(UiText.T("Own tactic", "自軍の戦術"), commandPanel.OwnTactic, true);
            AddWorkshopSection();
            AddTacticSection(UiText.T("Opponent tactic", "相手の戦術"), commandPanel.OpponentTactic, false);
            AddFileSection();
            AddMapSection();
            AddRuleSection();
            AddExternalAiSection();

            AddSetupText(setupContent,
                UiText.T("Asset credits: Noto Sans JP (OFL); game-icons.net by Lorc, Delapouite, Faithtoken (CC BY 3.0).",
                    "素材の出典：Noto Sans JP（OFL）、game-icons.net（Lorc・Delapouite・Faithtoken、CC BY 3.0）"),
                "setup-credits");
        }

        private void AddSetupRowWithControl(string title, VisualElement control)
        {
            var content = AddSetupRow(title);
            content.Add(control);
        }

        private void AddDoctrineChoices(string title, IOpponentControl control)
        {
            if (control == null) return;
            var content = AddSetupRow(title);
            var choices = control.Choices ?? Array.Empty<string>();
            for (int i = 0; i < choices.Length; i++)
            {
                string choice = choices[i];
                AddSetupButton(content, CommandPanel.PresetLabel(choice), control.Current == choice,
                    () => control.Current = choice);
            }
        }

        private void AddTacticSection(string title, ITacticControl control, bool editable)
        {
            if (control == null) return;
            var content = AddSetupRow(title);
            AddTacticChoices(content, control);
            string hint = CommandPanel.TacticHintText(control);
            if (!string.IsNullOrEmpty(hint)) AddSetupText(content, hint, "setup-hint");
            string status = CommandPanel.TacticStatusText(control,
                editable ? UiText.T("Own tactic status", "自軍の戦術の状態") : UiText.T("Opponent tactic status", "相手の戦術の状態"));
            if (!string.IsNullOrEmpty(status))
            {
                AddSetupText(content, status, "setup-status");
                AddSetupText(content, CommandPanel.TacticConsoleText(control), "setup-console");
                var reloadRow = new VisualElement();
                reloadRow.AddToClassList("setup-inline-row");
                var auto = new Toggle();
                auto.text = control.AutoReload
                    ? UiText.T("Auto reload: on", "自動で読み直す：入")
                    : UiText.T("Auto reload: off", "自動で読み直す：切");
                auto.value = control.AutoReload;
                auto.AddToClassList("setup-switch");
                auto.RegisterValueChangedCallback(evt =>
                {
                    control.AutoReload = evt.newValue;
                });
                reloadRow.Add(auto);
                AddSetupButton(reloadRow, UiText.T("Reload", "読み直す"), false, () => control.Reload());
                content.Add(reloadRow);
                if (!string.IsNullOrEmpty(control.ReloadMessage)) AddSetupText(content, control.ReloadMessage, "setup-status");
            }
            AddTacticParameters(content, control, editable);
            AddTacticSignals(content, control, editable);
        }

        private static void AddTacticChoices(VisualElement parent, ITacticControl control)
        {
            var grid = new VisualElement();
            grid.AddToClassList("setup-choice-grid");
            var views = control.ChoiceViews;
            if (views != null && views.Count != 0)
            {
                for (int i = 0; i < views.Count; i++)
                {
                    var choice = views[i];
                    AddSetupButton(grid, CommandPanel.TacticLabel(choice), control.Current == choice.Selection,
                        () => control.Current = choice.Selection);
                }
            }
            else
            {
                var choices = control.Choices ?? Array.Empty<string>();
                for (int i = 0; i < choices.Length; i++)
                {
                    string choice = choices[i];
                    AddSetupButton(grid, CommandPanel.TacticLabel(choice), control.Current == choice,
                        () => control.Current = choice);
                }
            }
            parent.Add(grid);
        }

        private void AddTacticParameters(VisualElement parent, ITacticControl control, bool editable)
        {
            var parameters = control.Parameters;
            if (parameters == null || parameters.Count == 0) return;
            AddSetupText(parent, UiText.T("Tactic knobs", "戦術のつまみ"), "setup-subtitle");
            for (int i = 0; i < parameters.Count; i++) AddTacticParameter(parent, control, parameters[i], editable);
        }

        private void AddTacticParameter(VisualElement parent, ITacticControl control, TacticParamView parameter, bool editable)
        {
            object raw = CommandPanel.TacticParameterValue(control, parameter);
            string label = CommandPanel.TacticParameterLabel(parameter);
            if (parameter.Type == "bool")
            {
                var toggle = new Toggle();
                bool value = raw is bool && (bool)raw;
                toggle.text = label + ": " + (value ? "on" : "off");
                toggle.value = value;
                toggle.SetEnabled(editable);
                toggle.AddToClassList("setup-switch");
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (editable) control.SetParam(parameter.Name, evt.newValue);
                });
                parent.Add(toggle);
                return;
            }

            if (parameter.Type == "choice")
            {
                AddSetupText(parent, label + ": " + (raw ?? ""), "setup-param-label");
                var choices = parameter.Choices ?? Array.Empty<string>();
                var grid = new VisualElement();
                grid.AddToClassList("setup-choice-grid");
                for (int i = 0; i < choices.Count; i++)
                {
                    string choice = choices[i];
                    AddSetupButton(grid, choice, Equals(raw, choice),
                        () => { if (editable) control.SetParam(parameter.Name, choice); });
                }
                grid.SetEnabled(editable);
                parent.Add(grid);
                return;
            }

            decimal current = raw is decimal ? (decimal)raw : Convert.ToDecimal(raw, System.Globalization.CultureInfo.InvariantCulture);
            AddSetupText(parent, label + ": " + current.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "setup-param-label");
            if (!editable || !parameter.Min.HasValue || !parameter.Max.HasValue) return;
            decimal minValue = parameter.Min.Value;
            decimal maxValue = parameter.Max.Value;
            if (parameter.Type == "int")
            {
                var slider = new SliderInt((int)minValue, (int)maxValue);
                slider.value = (int)Math.Round(current, 0, MidpointRounding.AwayFromZero);
                slider.AddToClassList("setup-slider");
                slider.RegisterValueChangedCallback(evt => commandPanel.SetTacticParameter(control, parameter, evt.newValue));
                parent.Add(slider);
            }
            else
            {
                var slider = new Slider((float)minValue, (float)maxValue);
                slider.value = (float)current;
                slider.AddToClassList("setup-slider");
                slider.RegisterValueChangedCallback(evt => commandPanel.SetTacticParameter(control, parameter, (decimal)evt.newValue));
                parent.Add(slider);
            }
            var step = parameter.Step ?? (parameter.Type == "int" ? 1m : (maxValue - minValue) / 20m);
            var nudge = new VisualElement();
            nudge.AddToClassList("setup-nudge-row");
            AddSetupButton(nudge, "−", false, () => commandPanel.SetTacticParameter(control, parameter, current - step));
            AddSetupButton(nudge, "+", false, () => commandPanel.SetTacticParameter(control, parameter, current + step));
            parent.Add(nudge);
        }

        private void AddTacticSignals(VisualElement parent, ITacticControl control, bool editable)
        {
            var signals = control.Signals;
            if (!editable || signals == null || signals.Count == 0) return;
            AddSetupText(parent, UiText.T("Tactic signals", "戦術の合図"), "setup-subtitle");
            var grid = new VisualElement();
            grid.AddToClassList("setup-choice-grid");
            for (int i = 0; i < signals.Count; i++)
            {
                var signal = signals[i];
                string text = signal.Label + (signal.NeedsPoint ? UiText.T(" (pick point)", "（地点を選ぶ）") : "");
                AddSetupButton(grid, text, false, () => commandPanel.BeginTacticSignal(control, signal));
            }
            parent.Add(grid);
        }

        private void AddWorkshopSection()
        {
            var workshop = commandPanel.Workshop;
            var ownTactic = commandPanel.OwnTactic;
            if (workshop == null) return;
            var content = AddSetupRow(UiText.T("Workshop", "Workshop"));
            bool canPublish = ownTactic != null && workshop.CanPublishTactic(ownTactic.Current);
            var row = new VisualElement();
            row.AddToClassList("setup-inline-row");
            var publish = AddSetupButton(row, UiText.T("Publish tactic", "戦術を公開"), false,
                () => { if (ownTactic != null) workshop.PublishTactic(ownTactic.Current); });
            publish.SetEnabled(canPublish && workshop.SteamAvailable);
            var visibility = workshop.VisibilityChoices ?? Array.Empty<string>();
            for (int i = 0; i < visibility.Length; i++)
            {
                string choice = visibility[i];
                AddSetupButton(row, choice, workshop.Visibility == choice, () => workshop.Visibility = choice);
            }
            content.Add(row);
            AddSetupButton(content, UiText.T("Refresh Workshop", "Workshopを読み直す"), false, workshop.RefreshWorkshopTactics);
            AddSetupText(content, workshop.WorkshopStatus ?? "", "setup-status");
        }

        private void AddFileSection()
        {
            var files = commandPanel.PlayerFiles;
            if (files == null) return;
            var content = AddSetupRow(UiText.T("Folders", "フォルダ"));
            AddSetupButton(content, UiText.T("Open tactics folder", "戦術のフォルダを開く"), false, files.OpenTacticsFolder);
            AddSetupButton(content, UiText.T("Open packs folder", "記録パックのフォルダを開く"), false, files.OpenPacksFolder);
            AddSetupButton(content, UiText.T("Refresh list", "一覧を更新"), false, files.RefreshTacticList);
            AddSetupButton(content, UiText.T("Export rulebook", "ルールブックを書き出す"), false, files.ExportRulebook);
            AddSetupText(content, files.RulebookStatus ?? "", "setup-status");
        }

        private void AddMapSection()
        {
            var map = commandPanel.MapChoice;
            if (map == null) return;
            var content = AddSetupRow(UiText.T("Map", "マップ"));
            bool economy = map.EconomyMap;
            AddSetupButton(content, economy
                ? UiText.T("Random #", "ランダム #") + map.Seed + UiText.T(" (economy, lines, terrain)", "（内政・ライン・地形）")
                : UiText.T("Classic two roads", "旧来の二本道"), economy, () => map.EconomyMap = true);
            var newMap = AddSetupButton(content, UiText.T("New random map", "新しいランダムマップ"), false, map.NewMap);
            newMap.SetEnabled(economy);
            AddSetupButton(content, UiText.T("Classic two roads", "旧来の二本道"), !economy, () => map.EconomyMap = false);
        }

        private void AddRuleSection()
        {
            var rules = commandPanel.MatchRuleChoice;
            if (rules == null) return;
            bool economy = commandPanel.MapChoice != null && commandPanel.MapChoice.EconomyMap;
            var extra = AddSetupRow(UiText.T("Extra rules", "追加ルール"));
            var monks = AddSetupButton(extra, rules.Monks ? UiText.T("Monks: on", "僧侶：入") : UiText.T("Monks: off", "僧侶：切"), rules.Monks, () => rules.Monks = !rules.Monks);
            monks.SetEnabled(economy);
            var ageVictory = AddSetupButton(extra, rules.AgeVictory ? UiText.T("Age victory: on", "時代到達勝利：入") : UiText.T("Age victory: off", "時代到達勝利：切"), rules.AgeVictory, () => rules.AgeVictory = !rules.AgeVictory);
            ageVictory.SetEnabled(economy);
            var civ = AddSetupRow(UiText.T("Civilisations", "文明"));
            var civilisations = AddSetupButton(civ, rules.AllCivilisations
                ? UiText.T("All 14 (12 more civilisations; gold, monks and fish on)", "14個全部（森林〜聖地の12文明を追加、金・僧侶・漁あり）")
                : UiText.T("First 2 only (agrarian, metallurgy)", "最初の2つだけ（農耕・冶金）"), rules.AllCivilisations,
                () => rules.AllCivilisations = !rules.AllCivilisations);
            civilisations.SetEnabled(economy);
        }

        private void AddExternalAiSection()
        {
            var ai = commandPanel.ExternalAi;
            var content = AddSetupRow(UiText.T("Outside AI", "外部AI"));
            if (ai == null || !ai.KeyAvailable)
            {
                AddSetupText(content, UiText.T("Off. No key is set on this PC.", "切。このPCにはキーが設定されていません。"), "setup-status");
                return;
            }
            var toggle = new Toggle();
            toggle.text = ai.Enabled ? UiText.T("On - asking an outside AI", "入 - 外部AIに聞いています") : UiText.T("Off - ask an outside AI", "切 - 外部AIに聞く");
            toggle.value = ai.Enabled;
            toggle.AddToClassList("setup-switch");
            toggle.RegisterValueChangedCallback(evt => ai.Enabled = evt.newValue);
            content.Add(toggle);
            AddSetupText(content, ai.Enabled ? ai.Status.Replace("\n", "   ")
                : UiText.T("Turning it on sends what your side can see (positions, counts, outposts) to an outside service.", "入れると、自陣営に見えている情報（位置・人数・拠点）を外部のサービスに送ります。"), "setup-status");
        }

        private void RefreshLog()
        {
            if (logToggleFrame == null || commandPanel == null) return;
            var layout = UiLayout.Calculate(Screen.width, Screen.height);
            SetFrameRect(logToggleFrame, layout.LogToggle, Screen.width, Screen.height);
            logToggleFrame.style.display = DisplayStyle.Flex;
            logToggle.text = commandPanel.IsLogOpen
                ? UiText.T("Fold the command log ^", "命令の記録と状態を畳む ▲")
                : UiText.T("Command log and status v", "命令の記録と状態を開く ▼");

            bool open = commandPanel.IsLogOpen;
            if (logStatusFrame != null)
            {
                SetFrameRect(logStatusFrame, layout.LogStatus, Screen.width, Screen.height);
                logStatusFrame.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (logEntriesFrame != null)
            {
                SetFrameRect(logEntriesFrame, layout.LogEntries, Screen.width, Screen.height);
                logEntriesFrame.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!open) return;
            if (logStatusTitle != null) logStatusTitle.text = UiText.T("Command status (7 states)", "命令の状態（7段階）");
            if (logEntriesTitle != null) logEntriesTitle.text = UiText.T("Command log", "命令の記録");
            if (logStatusScroll != null)
            {
                logStatusScroll.Clear();
                for (int i = 0; i < commandPanel.CommandStatusCount; i++)
                {
                    var line = new Label(commandPanel.CommandStatusText(i));
                    line.AddToClassList("log-line");
                    logStatusScroll.Add(line);
                }
            }
            if (logEntriesScroll != null)
            {
                logEntriesScroll.Clear();
                var entries = commandPanel.LogEntries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var line = new Label(entries[i]);
                    line.AddToClassList("log-line");
                    logEntriesScroll.Add(line);
                }
            }
        }

        private void RefreshResult()
        {
            if (resultFrame == null || commandPanel == null) return;
            var outcome = commandPanel.CurrentOutcome;
            if (!outcome.HasValue)
            {
                resultFrame.style.display = DisplayStyle.None;
                return;
            }
            SetFrameRect(resultFrame, UiLayout.Calculate(Screen.width, Screen.height).Result, Screen.width, Screen.height);
            resultFrame.style.display = DisplayStyle.Flex;
            if (resultHeadline != null) resultHeadline.text = outcome.Value.Headline;
            if (resultDetail != null) resultDetail.text = outcome.Value.Detail;
            if (resultPack != null)
            {
                resultPack.text = commandPanel.MatchPackText();
                resultPack.style.display = string.IsNullOrEmpty(resultPack.text) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (resultRestart != null)
            {
                resultRestart.text = UiText.T("Play again", "もう一度");
                resultRestart.style.display = commandPanel.MatchRestart == null ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void RefreshSupply()
        {
            if (supplyFrame == null) return;
            if (supplySource == null || supplySource.IsSetupOpen)
            {
                supplyFrame.style.display = DisplayStyle.None;
                return;
            }

            SetFrameRect(supplyFrame, UiLayout.Calculate(Screen.width, Screen.height).Supply, Screen.width, Screen.height);
            supplyFrame.style.display = DisplayStyle.Flex;
            if (supplyTitle != null) supplyTitle.text = UiText.T("Supply / reinforcements", "兵站・増援");
            if (supplyUnits != null) supplyUnits.text = supplySource.SupplyUnitsText();
            if (supplyLine0 != null) supplyLine0.text = supplySource.SupplyLineCount > 0 ? supplySource.SupplyLineText(0) : "";
            if (supplyLine1 != null) supplyLine1.text = supplySource.SupplyLineCount > 1 ? supplySource.SupplyLineText(1) : "";
        }

        private void RefreshTimeline()
        {
            if (timelineFrame == null) return;
            if (timelineSource == null || timelineScroll == null)
            {
                timelineFrame.style.display = DisplayStyle.None;
                return;
            }

            SetFrameRect(timelineFrame, UiLayout.Calculate(Screen.width, Screen.height).Timeline, Screen.width, Screen.height);
            timelineFrame.style.display = DisplayStyle.Flex;
            if (timelineTitle != null) timelineTitle.text = UiText.T("Timeline", "時系列");
            timelineSource.Timeline.Ingest(view.LatestFrame);
            var entries = timelineSource.Timeline.Entries;
            bool grew = entries.Count > renderedTimelineLineCount;
            if (entries.Count < renderedTimelineLineCount)
            {
                timelineScroll.Clear();
                timelineLineSlots.Clear();
                renderedTimelineLineCount = 0;
                renderedTimelineLastTick = long.MinValue;
                renderedTimelineLastText = "";
                grew = entries.Count != 0;
            }

            for (int i = renderedTimelineLineCount; i < entries.Count; i++)
            {
                var label = new Label();
                label.AddToClassList("timeline-line-text");
                timelineScroll.Add(label);
                timelineLineSlots.Add(new TimelineLineSlot { Label = label });
            }
            renderedTimelineLineCount = entries.Count;
            for (int i = 0; i < entries.Count; i++)
                timelineLineSlots[i].Label.text = timelineSource.TimelineEntryText(entries[i]);

            bool newestChanged = entries.Count > 0 && (entries[entries.Count - 1].Tick != renderedTimelineLastTick
                || entries[entries.Count - 1].Text != renderedTimelineLastText);
            if (entries.Count > 0)
            {
                renderedTimelineLastTick = entries[entries.Count - 1].Tick;
                renderedTimelineLastText = entries[entries.Count - 1].Text;
            }
            if (grew || newestChanged)
                timelineScroll.schedule.Execute(() => timelineScroll.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        private void RefreshClock()
        {
            if (clockFrame == null) return;
            if (timelineSource == null)
            {
                clockFrame.style.display = DisplayStyle.None;
                return;
            }

            SetFrameRect(clockFrame, UiLayout.Calculate(Screen.width, Screen.height).TopCenter, Screen.width, Screen.height);
            clockFrame.style.display = DisplayStyle.Flex;
            var current = timelineSource.MatchClock;
            if (clockInfo != null) clockInfo.text = timelineSource.ClockDisplayText();
            if (clockPause != null)
            {
                clockPause.text = current == null ? UiText.T("Pause", "停止") : (current.Paused ? UiText.T("Play", "再生") : UiText.T("Pause", "停止"));
                clockPause.SetEnabled(current != null);
            }
            if (clockStep != null) { clockStep.text = "+1"; clockStep.SetEnabled(current != null); }
            ConfigureClockSpeedButton(clockSpeed1, 1, current);
            ConfigureClockSpeedButton(clockSpeed2, 2, current);
            ConfigureClockSpeedButton(clockSpeed4, 4, current);
            if (clockFaction != null)
            {
                clockFaction.text = current == null ? UiText.T("View", "陣営") : UiText.T("View ", "陣営 ") + (3 - current.ViewFactionId);
                clockFaction.SetEnabled(current != null);
            }
        }

        private static void ConfigureClockSpeedButton(Button button, int speed, IMatchClock current)
        {
            if (button == null) return;
            button.text = "x" + speed;
            button.SetEnabled(current != null);
            button.EnableInClassList("is-selected", current != null && current.SpeedMultiplier == speed);
        }

        private void RefreshEconomy(EconomyView economy)
        {
            if (economyFrame == null) return;
            if (economyPanel == null || economy == null)
            {
                economyFrame.style.display = DisplayStyle.None;
                return;
            }

            SetFrameRect(economyFrame, UiLayout.Calculate(Screen.width, Screen.height).Economy, Screen.width, Screen.height);
            economyFrame.style.display = DisplayStyle.Flex;
            if (economyTitle != null) economyTitle.text = UiText.T("Economy", "内政");
            if (economyHeaderStatus != null) economyHeaderStatus.text = UiText.T("Commands go through the economy port", "命令は内政の送り口を通ります");
            RefreshEconomyTabs();

            var actions = economyPanel.GetToolkitActions();
            string signature = actions.Count.ToString();
            for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                signature += "|" + actions[actionIndex].Id + ":" + actions[actionIndex].Message;
            if (signature != economyActionSignature)
            {
                economyActionSignature = signature;
                economyActions.Clear();
                economyActionSlots.Clear();
                for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                {
                    var row = new VisualElement();
                    row.AddToClassList("economy-action-row");
                    var action = actions[actionIndex];
                    string actionId = action.Id;
                    var button = new Button(() => economyPanel.ExecuteToolkitAction(actionId));
                    button.AddToClassList("economy-action");
                    var reason = new Label();
                    reason.AddToClassList("economy-reason");
                    row.Add(button);
                    row.Add(reason);
                    economyActions.Add(row);
                    economyActionSlots.Add(new EconomyActionSlot { Row = row, Button = button, Reason = reason });
                }
            }

            for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
            {
                var action = actions[actionIndex];
                var slot = economyActionSlots[actionIndex];
                slot.Button.text = action.Label;
                slot.Button.SetEnabled(action.Enabled && !action.Message);
                slot.Button.EnableInClassList("is-selected", action.Selected);
                slot.Button.EnableInClassList("is-message", action.Message);
                slot.Row.EnableInClassList("is-message-row", action.Message);
                slot.Reason.text = action.Reason;
                slot.Reason.style.display = string.IsNullOrEmpty(action.Reason) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (economyHint != null) economyHint.text = economyPanel.PlacementHint;
            if (economyNotice != null) economyNotice.text = economyPanel.LatestNotice;
        }

        private void RefreshEconomyTabs()
        {
            if (economyPanel == null) return;
            var selected = economyPanel.SelectedTab;
            if (economyTabBuild != null) economyTabBuild.EnableInClassList("is-selected", selected == EconomyPanel.Tab.Build);
            if (economyTabMake != null) economyTabMake.EnableInClassList("is-selected", selected == EconomyPanel.Tab.Make);
            if (economyTabResearch != null) economyTabResearch.EnableInClassList("is-selected", selected == EconomyPanel.Tab.Research);
            if (economyTabPolicy != null) economyTabPolicy.EnableInClassList("is-selected", selected == EconomyPanel.Tab.Policy);
        }

        private void RefreshCommands()
        {
            if (commandsFrame == null) return;
            if (commandPanel == null || view == null)
            {
                commandsFrame.style.display = DisplayStyle.None;
                return;
            }

            var rect = UiLayout.Calculate(Screen.width, Screen.height).Commands;
            commandsFrame.style.left = Length.Percent(Screen.width <= 0f ? 0f : rect.x / Screen.width * 100f);
            commandsFrame.style.top = Length.Percent(Screen.height <= 0f ? 0f : rect.y / Screen.height * 100f);
            commandsFrame.style.width = Length.Percent(Screen.width <= 0f ? 0f : rect.width / Screen.width * 100f);
            commandsFrame.style.height = Length.Percent(Screen.height <= 0f ? 0f : rect.height / Screen.height * 100f);
            commandsFrame.style.display = DisplayStyle.Flex;

            var selection = view.Selected;
            if (commandsTitle != null) commandsTitle.text = UiText.T("Commands", "命令");
            if (commandsHelp != null) commandsHelp.text = UiText.T("Select a force, then issue an order", "軍団を選び、命令を出す");

            if (selection.Kind == SelectionKind.Army)
            {
                int alive = SelectedAliveCount();
                if (commandsSelectionTitle != null)
                    commandsSelectionTitle.text = UiText.T("Army " + selection.Id, "軍団 " + selection.Id);
                if (commandsSelectionDetail != null)
                    commandsSelectionDetail.text = UiText.T("Alive " + alive, "生存 " + alive);
                if (commandsSelectionHint != null)
                    commandsSelectionHint.text = view.SelectedArmies.Count > 1
                        ? UiText.T("Selected armies: " + view.SelectedArmies.Count, "選択中の軍団数：" + view.SelectedArmies.Count)
                        : UiText.T("WASD move, wheel zoom", "WASDで移動、ホイールで拡大縮小");
            }
            else if (selection.Kind == SelectionKind.Outpost)
            {
                if (commandsSelectionTitle != null) commandsSelectionTitle.text = UiText.T("Outpost " + selection.Id, "拠点 " + selection.Id);
                if (commandsSelectionDetail != null) commandsSelectionDetail.text = view.DescribeSelection();
                if (commandsSelectionHint != null) commandsSelectionHint.text = UiText.T("Select an army to command it", "命令するには軍団を選択");
            }
            else if (selection.Kind == SelectionKind.Core)
            {
                if (commandsSelectionTitle != null) commandsSelectionTitle.text = UiText.T("Core " + selection.Id, "コア " + selection.Id);
                if (commandsSelectionDetail != null) commandsSelectionDetail.text = view.DescribeSelection();
                if (commandsSelectionHint != null) commandsSelectionHint.text = UiText.T("Select an army to command it", "命令するには軍団を選択");
            }
            else
            {
                if (commandsSelectionTitle != null) commandsSelectionTitle.text = UiText.T("Nothing selected", "未選択");
                if (commandsSelectionDetail != null) commandsSelectionDetail.text = "";
                if (commandsSelectionHint != null) commandsSelectionHint.text = UiText.T(
                    "Click an army or drag a box to select", "軍団をクリック、またはドラッグで囲む");
            }

            bool armySelected = selection.Kind == SelectionKind.Army && view.SelectedArmies.Count != 0;
            bool outpostSelected = selection.Kind == SelectionKind.Outpost;
            ConfigureCommandButton(commandAttack, commandPanel.IsAwaitingGround
                ? UiText.T("Attack: click ground", "攻撃：地面をクリック")
                : UiText.T("Attack", "攻撃"), armySelected);
            ConfigureCommandButton(commandRetreat, UiText.T("Retreat", "撤退"), armySelected);
            ConfigureCommandButton(commandDefend, UiText.T("Defend core", "コアを守る"), armySelected);
            ConfigureCommandButton(commandAbandon, UiText.T("Abandon outpost", "拠点を放棄"), outpostSelected);
            ConfigureCommandButton(commandReserve, UiText.T("Reserve 30%", "予備30%"), true);
            ConfigureCommandButton(commandAuto, UiText.T("Back to auto", "お任せに戻す"), true);
            ConfigureCommandButton(commandCancel, UiText.T("Cancel", "取消"), commandPanel.IsAwaitingGround);
        }

        private int SelectedAliveCount()
        {
            if (view == null || view.LatestFrame == null) return 0;
            int alive = 0;
            var selected = view.SelectedArmies;
            foreach (var army in view.LatestFrame.Observation.OwnArmies)
                for (int i = 0; i < selected.Count; i++)
                    if (army.Id == selected[i]) { alive += army.AliveCount; break; }
            return alive;
        }

        private static void ConfigureCommandButton(Button button, string text, bool enabled)
        {
            if (button == null) return;
            button.text = text;
            button.SetEnabled(enabled);
        }

        private void ToggleStaffAiList()
        {
            staffAiListOpen = !staffAiListOpen;
            if (staffOptions != null) staffOptions.style.display = staffAiListOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SelectStaffAi(string name)
        {
            if (staff == null) return;
            staff.SelectedAiName = name;
            staffAiListOpen = false;
            if (staffOptions != null) staffOptions.style.display = DisplayStyle.None;
        }

        private void SendStaff()
        {
            if (staff == null || staffInput == null) return;
            staff.Speak(staffInput.value ?? "");
        }

        private void CancelStaff()
        {
            if (staff != null) staff.CancelLast();
        }

        private void OnStaffKeyDown(KeyDownEvent evt)
        {
            bool enter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;
            // During IME conversion Enter confirms the candidate; only a plain Enter sends the instruction.
            if (!enter || !string.IsNullOrEmpty(Input.compositionString)) return;
            evt.StopPropagation();
            evt.PreventDefault();
            SendStaff();
        }

        private void SetStaffInputFocus(bool focused)
        {
            staffInputFocused = focused;
            UiHitAreas.Shared.SetKeyboardCaptured(focused);
            Input.imeCompositionMode = focused ? IMECompositionMode.On : IMECompositionMode.Auto;
        }

        private void RefreshStaff()
        {
            if (staffFrame == null) return;
            if (staff == null)
            {
                staffFrame.style.display = DisplayStyle.None;
                SetStaffInputFocus(false);
                return;
            }

            var rect = UiLayout.Calculate(Screen.width, Screen.height).Strategist;
            staffFrame.style.left = Length.Percent(Screen.width <= 0f ? 0f : rect.x / Screen.width * 100f);
            staffFrame.style.top = Length.Percent(Screen.height <= 0f ? 0f : rect.y / Screen.height * 100f);
            staffFrame.style.width = Length.Percent(Screen.width <= 0f ? 0f : rect.width / Screen.width * 100f);
            staffFrame.style.height = Length.Percent(Screen.height <= 0f ? 0f : rect.height / Screen.height * 100f);
            staffFrame.style.display = DisplayStyle.Flex;

            RefreshStaffChoices();
            staffTargetLabel.text = staff.TargetLabel ?? "";
            staffCostLabel.text = "AI " + staff.SpentYen.ToString("0.0") + "円 / 予算の残り "
                + staff.RemainingBudgetYen.ToString("0.0") + "円";

            decimal estimate = 0m;
            try { estimate = staff.EstimateYen(staffInput == null ? "" : staffInput.value ?? ""); }
            catch (Exception) { }
            bool expensive = staff.IsExpensiveEstimate(estimate);
            staffEstimateLabel.text = expensive ? "1回 約" + estimate.ToString("0.0") + "円" : "";
            staffEstimateLabel.EnableInClassList("is-visible", expensive);
            bool warning = false;
            try { warning = staff.ShowBudgetWarning(staffInput == null ? "" : staffInput.value ?? ""); }
            catch (Exception) { }
            staffNoticeLabel.text = warning ? "予算を超えそうです" : (staff.LastNotice ?? "");
            staffNoticeLabel.EnableInClassList("is-warning", warning);
            staffNoticeLabel.EnableInClassList("is-visible", warning || !string.IsNullOrEmpty(staff.LastNotice));
            RefreshStaffConversation(staff.Conversation);

            if (staffInputFocused && staffInput != null)
            {
                Input.imeCompositionMode = IMECompositionMode.On;
                var field = staffInput.worldBound;
                Input.compositionCursorPos = new Vector2(field.x, field.y + field.height + 8f);
            }
        }

        private void RefreshStaffChoices()
        {
            if (staffOptions == null || staffAiButton == null || staff == null) return;
            var choices = staff.AiChoices ?? Array.Empty<StaffAiOption>();
            string signature = choices.Count.ToString();
            for (int i = 0; i < choices.Count; i++)
                signature += "|" + choices[i].Name + ":" + choices[i].DisplayName + ":" + choices[i].Available;
            if (signature != staffAiSignature)
            {
                staffAiSignature = signature;
                staffOptions.Clear();
                for (int i = 0; i < choices.Count; i++)
                {
                    var choice = choices[i];
                    var option = new Button(() => SelectStaffAi(choice.Name));
                    option.text = choice.DisplayName + (choice.Available ? "" : "（キー未設定）");
                    option.SetEnabled(choice.Available);
                    option.AddToClassList("staff-ai-option");
                    staffOptions.Add(option);
                }
            }

            string selectedName = staff.SelectedAiName ?? "";
            string selectedLabel = selectedName;
            for (int i = 0; i < choices.Count; i++)
                if (choices[i].Name == selectedName) { selectedLabel = choices[i].DisplayName; break; }
            staffAiButton.text = string.IsNullOrEmpty(selectedLabel) ? "AI を選ぶ  ▼" : selectedLabel + "  ▼";
            staffOptions.style.display = staffAiListOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshStaffConversation(IReadOnlyList<StaffChatLine> lines)
        {
            if (staffScroll == null) return;
            lines = lines ?? Array.Empty<StaffChatLine>();
            bool grew = lines.Count > renderedStaffLineCount;
            if (lines.Count < renderedStaffLineCount)
            {
                staffScroll.Clear();
                staffLineSlots.Clear();
                renderedStaffLineCount = 0;
                grew = lines.Count != 0;
            }

            for (int i = renderedStaffLineCount; i < lines.Count; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("staff-line");
                var bubble = new VisualElement();
                bubble.AddToClassList("staff-bubble");
                var label = new Label();
                label.AddToClassList("staff-line-text");
                bubble.Add(label);
                row.Add(bubble);
                staffScroll.Add(row);
                staffLineSlots.Add(new StaffLineSlot { Row = row, Bubble = bubble, Label = label });
            }
            renderedStaffLineCount = lines.Count;

            for (int i = 0; i < lines.Count; i++) ApplyStaffLine(staffLineSlots[i], lines[i]);
            if (staffEmptyLabel != null) staffEmptyLabel.style.display = lines.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (grew)
                staffScroll.schedule.Execute(() => staffScroll.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        private static void ApplyStaffLine(StaffLineSlot slot, StaffChatLine line)
        {
            if (slot == null || line == null) return;
            string prefix = string.IsNullOrEmpty(line.Speaker) ? "" : (line.Time + " " + line.Speaker + "：");
            slot.Label.text = prefix + line.Body;
            slot.Row.EnableInClassList("staff-line-you", line.Tone == StaffChatLine.YouTone);
            slot.Row.EnableInClassList("staff-line-staff", line.Tone == StaffChatLine.StaffTone);
            slot.Row.EnableInClassList("staff-line-detail", line.Tone == StaffChatLine.DetailTone);
            slot.Row.EnableInClassList("staff-line-bad", line.Tone == StaffChatLine.BadTone);
            slot.Row.EnableInClassList("staff-line-status", line.Speaker == "状態");
            slot.Bubble.EnableInClassList("staff-tone-you", line.Tone == StaffChatLine.YouTone);
            slot.Bubble.EnableInClassList("staff-tone-staff", line.Tone == StaffChatLine.StaffTone);
            slot.Bubble.EnableInClassList("staff-tone-detail", line.Tone == StaffChatLine.DetailTone);
            slot.Bubble.EnableInClassList("staff-tone-bad", line.Tone == StaffChatLine.BadTone);
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
                Texture2D iconTexture;
                if (resourceIcons.TryGetValue(entry.Kind, out iconTexture) && iconTexture != null)
                    icon.style.backgroundImage = new StyleBackground(iconTexture);
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
                slots.Add(new ResourceSlot
                {
                    Kind = entry.Kind,
                    Label = entry.Label,
                    Icon = icon,
                    ValueLabel = value,
                    Value = int.MinValue
                });
            }
        }

        private static int CountIdle(EconomyView economy)
        {
            int count = 0;
            foreach (var villager in economy.Villagers)
                if (villager.IsOwn && villager.Activity == VillagerActivity.Idle) count++;
            return count;
        }

        private void LoadHudAssets()
        {
            if (hudAssetsLoaded) return;
            hudAssetsLoaded = true;

            bundledRegularFont = CreateBundledFont(Resources.Load<Font>(RegularFontResourcePath));
            bundledBoldFont = CreateBundledFont(Resources.Load<Font>(BoldFontResourcePath));
            populationIconTexture = Resources.Load<Texture2D>("Hud/Icons/person");
            if (populationIconTexture == null)
                Debug.LogWarning("UI Toolkit HUD icon not found at Resources/Hud/Icons/person.");

            for (int i = 0; i < IconKinds.Length; i++)
            {
                var kind = IconKinds[i];
                var texture = Resources.Load<Texture2D>("Hud/Icons/" + IconFileName(kind));
                resourceIcons[kind] = texture;
                if (texture == null)
                    Debug.LogWarning("UI Toolkit HUD icon not found at Resources/Hud/Icons/" + IconFileName(kind) + ".");
            }

        }

        private static string IconFileName(TopBarResourceVisibility.ResourceKind kind)
        {
            switch (kind)
            {
                case TopBarResourceVisibility.ResourceKind.Food: return "wheat";
                case TopBarResourceVisibility.ResourceKind.Wood: return "wood-pile";
                case TopBarResourceVisibility.ResourceKind.Ore: return "ore";
                case TopBarResourceVisibility.ResourceKind.Metal: return "metal-bar";
                case TopBarResourceVisibility.ResourceKind.Stone: return "stone-pile";
                case TopBarResourceVisibility.ResourceKind.Gems: return "cut-diamond";
                case TopBarResourceVisibility.ResourceKind.Gold: return "two-coins";
                case TopBarResourceVisibility.ResourceKind.Charcoal: return "coal-pile";
                case TopBarResourceVisibility.ResourceKind.Steel: return "anvil";
                case TopBarResourceVisibility.ResourceKind.BowGear: return "quiver";
                default: return "";
            }
        }

        private FontAsset CreateBundledFont(Font font)
        {
            if (font == null) return null;
            try
            {
                var asset = FontAsset.CreateFontAsset(font);
                if (asset != null) createdFontAssets.Add(asset);
                return asset;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private FontAsset CreateOsFont(string[] families)
        {
            foreach (var family in families)
            {
                try
                {
                    var asset = FontAsset.CreateFontAsset(family, "Regular", 32, 4, GlyphRenderMode.SDFAA);
                    if (asset != null)
                    {
                        createdFontAssets.Add(asset);
                        return asset;
                    }
                }
                catch (Exception)
                {
                    // The next family is the fallback. Font creation is optional and must not stop the HUD.
                }
            }
            return null;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < createdFontAssets.Count; i++)
            {
                if (createdFontAssets[i] != null) Destroy(createdFontAssets[i]);
            }
            if (panelSettings != null) Destroy(panelSettings);
        }
    }
}
