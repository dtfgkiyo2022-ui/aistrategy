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
        private bool staffInputFocused;
        private bool staffAiListOpen;
        private string staffAiSignature = "";
        private int renderedStaffLineCount;
        private readonly List<StaffLineSlot> staffLineSlots = new List<StaffLineSlot>();
        private readonly List<EconomyActionSlot> economyActionSlots = new List<EconomyActionSlot>();
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
            Bind(battlefield, null);
        }

        public void Bind(BattlefieldView battlefield, IStaffControl staffControl)
        {
            Bind(battlefield, null, staffControl, null);
        }

        public void Bind(BattlefieldView battlefield, IStaffControl staffControl, CommandPanel commandControl)
        {
            Bind(battlefield, null, staffControl, commandControl);
        }

        public void Bind(BattlefieldView battlefield, EconomyPanel economyControl, IStaffControl staffControl, CommandPanel commandControl)
        {
            view = battlefield;
            economyPanel = economyControl;
            staff = staffControl;
            commandPanel = commandControl;
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
            BindStaffEvents();
            BindCommandEvents();
            BindEconomyEvents();
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
