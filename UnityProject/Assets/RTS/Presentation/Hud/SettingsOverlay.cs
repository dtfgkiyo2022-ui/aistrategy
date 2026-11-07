using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rts.Presentation
{
    /// <summary>歯車・Esc のメニューと、5 タブの設定画面。既存 HUD の矩形を変更せず上に重ねる。</summary>
    public sealed class SettingsOverlay : MonoBehaviour
    {
        private UIDocument document;
        private GameObject uiHost;
        private UnityEngine.TextCore.Text.FontAsset fontAsset;
        private PanelSettings panelSettings;
        private VisualElement root, menu, settings, tabBar, content, confirm;
        private Button gear, resume, openSettings, restart, quit, apply, revert, defaults, close;
        private Label fpsLabel;
        private float fpsSampleAt;
        private int fpsFrames;
        private readonly List<Button> tabs = new List<Button>();
        private SettingsValues pending, atOpen, beforeConfirm;
        private int tab;
        private float confirmUntil;
        private bool initialized;
        private static readonly string[] TabNames = { "画質", "画面", "音", "操作", "ゲーム" };

        private void OnEnable() { EnsureUi(); }

        private void Update()
        {
            EnsureUi();
            if (!initialized) return;
            if (confirmUntil > 0f && Time.unscaledTime >= confirmUntil) CancelDisplayChange();
            UpdateFps();
        }

        // After every panel's Update: an Esc that cancelled a placement or an order does not also open the menu.
        private void LateUpdate()
        {
            if (!initialized) return;
            bool open = menu.style.display == DisplayStyle.Flex || settings.style.display == DisplayStyle.Flex
                || (confirm != null && confirm.style.display == DisplayStyle.Flex);
            if (KeyBindings.GetKeyDown(KeyBindingAction.MenuCancel) && (open || !KeyBindings.EscapeHandledThisFrame)
                && !UiHitAreas.Shared.KeyboardCaptured)
            {
                if (confirm != null && confirm.style.display == DisplayStyle.Flex) CancelDisplayChange();
                else if (settings.style.display == DisplayStyle.Flex) CloseSettings();
                else if (menu.style.display == DisplayStyle.Flex) menu.style.display = DisplayStyle.None;
                else menu.style.display = DisplayStyle.Flex;
            }
            RegisterHitAreas();
        }

        /// <summary>Clicks on the gear, or anywhere while the menu or settings are open, must not reach the map.</summary>
        private void RegisterHitAreas()
        {
            bool open = menu.style.display == DisplayStyle.Flex || settings.style.display == DisplayStyle.Flex
                || (confirm != null && confirm.style.display == DisplayStyle.Flex);
            if (open) { UiHitAreas.Shared.Register(new Rect(0f, 0f, Screen.width, Screen.height)); return; }
            var panelSize = root.worldBound;
            if (gear == null || panelSize.width <= 0f || panelSize.height <= 0f) return;
            float sx = Screen.width / panelSize.width, sy = Screen.height / panelSize.height;
            var g = gear.worldBound;
            UiHitAreas.Shared.Register(new Rect(g.x * sx - 2f, g.y * sy - 2f, g.width * sx + 4f, g.height * sy + 4f));
        }

        private void EnsureUi()
        {
            if (initialized) return;
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Runtime RTS Settings PanelSettings";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.sortingOrder = 100;
            // Same theme as the HUD: without one the root gets no size and the percentage-sized cards collapse.
            var theme = Resources.Load<ThemeStyleSheet>("Hud/HudRuntimeTheme");
            if (theme != null) panelSettings.themeStyleSheet = theme;
            // Its own top-level object: the HUD already has a UIDocument on this game object and Unity allows only one
            // (build1107a: NullReferenceException every frame). Not a child either, since a UIDocument under another
            // UIDocument must share its PanelSettings (build1107b: assertion every frame). Destroyed in OnDestroy.
            uiHost = new GameObject("RTS Settings UI");
            document = uiHost.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            root = document.rootVisualElement;
            // The same bundled Japanese font as the HUD; the theme's default font has no Japanese glyphs.
            var japanese = Resources.Load<Font>("Hud/Fonts/NotoSansJP-Regular");
            if (japanese != null)
            {
                fontAsset = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(japanese);
                if (fontAsset != null) root.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
            }
            root.style.flexGrow = 1f;
            root.pickingMode = PickingMode.Ignore;
            BuildShell();
            pending = SettingsStore.Load();
            SettingsStore.Apply(pending);
            UpdateFpsVisibility();
            initialized = true;
            string shots = ShotsFolder();
            if (shots != null) StartCoroutine(TakeSettingsShots(shots));
        }

        // Checking only: "-rts-settings-shots <folder>" photographs the four quality levels, the menu and every settings
        // tab in the real player, where post-processing works (an editor batch capture shows almost none of it).
        private static string ShotsFolder()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-rts-settings-shots");
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        private System.Collections.IEnumerator TakeSettingsShots(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            yield return new WaitForSecondsRealtime(25f);
            string[] names = { "low", "medium", "high", "ultra" };
            for (int i = 0; i < names.Length; i++)
            {
                GraphicsQuality.ApplyLevel((GraphicsQualityLevel)i);
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot(folder, "quality_" + names[i]);
            }
            GraphicsQuality.ApplyLevel(GraphicsQualityLevel.Low);
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot(folder, "quality_low_again");
            menu.style.display = DisplayStyle.Flex;
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot(folder, "menu");
            OpenSettings();
            for (int i = 0; i < TabNames.Length; i++)
            {
                SelectTab(i);
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot(folder, "tab" + i);
            }
            CloseSettings();
            Debug.Log("RTS settings shots written to " + folder + ".");
        }

        private static System.Collections.IEnumerator Shot(string folder, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, name + ".png"));
            yield return null;
            yield return null;
        }

        private void BuildShell()
        {
            gear = new Button(ToggleMenu) { text = "設定" }; // The bundled Japanese font has no gear glyph (it drew a box).
            gear.name = "settings-gear";
            gear.style.position = Position.Absolute; gear.style.right = 16f; gear.style.top = 10f;
            gear.style.width = 64f; gear.style.height = 42f; gear.style.fontSize = 18f;
            gear.style.backgroundColor = new Color(0.12f, 0.10f, 0.08f, 0.94f); gear.style.color = new Color(1f, 0.82f, 0.35f);
            gear.pickingMode = PickingMode.Position; root.Add(gear);

            menu = Card(0.5f, 0.12f, 0.25f, 0.56f); menu.style.display = DisplayStyle.None; menu.name = "settings-menu";
            AddTitle(menu, "メニュー");
            resume = MenuButton("再開", () => menu.style.display = DisplayStyle.None); menu.Add(resume);
            openSettings = MenuButton("設定", OpenSettings); menu.Add(openSettings);
            restart = MenuButton("試合をやめてタイトルへ", RestartMatch); menu.Add(restart);
            quit = MenuButton("ゲームを終了", () => Application.Quit()); menu.Add(quit);
            root.Add(menu);

            settings = Card(0.07f, 0.06f, 0.86f, 0.88f); settings.style.display = DisplayStyle.None; settings.name = "settings-screen";
            AddTitle(settings, "設定");
            tabBar = new VisualElement(); tabBar.style.flexDirection = FlexDirection.Row; tabBar.style.marginBottom = 8f; settings.Add(tabBar);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i; var button = new Button(() => SelectTab(index)) { text = TabNames[i] };
                button.style.flexGrow = 1f; button.style.height = 34f; button.style.marginRight = 4f; button.style.color = new Color(1f, 0.90f, 0.70f); button.pickingMode = PickingMode.Position;
                tabs.Add(button); tabBar.Add(button);
            }
            content = new ScrollView(ScrollViewMode.Vertical); content.style.flexGrow = 1f; content.name = "settings-content"; settings.Add(content);
            var footer = new VisualElement(); footer.style.flexDirection = FlexDirection.Row; footer.style.marginTop = 8f; settings.Add(footer);
            apply = FooterButton("適用", ApplySettings); revert = FooterButton("元に戻す", RevertSettings);
            defaults = FooterButton("既定に戻す", ResetTab); close = FooterButton("閉じる", CloseSettings);
            footer.Add(apply); footer.Add(revert); footer.Add(defaults); footer.Add(close);
            root.Add(settings);

            confirm = Card(0.30f, 0.38f, 0.40f, 0.23f); confirm.style.display = DisplayStyle.None; confirm.name = "display-confirm";
            AddTitle(confirm, "この表示でよいですか？");
            confirm.Add(new Label("15 秒で元に戻ります"));
            var confirmButtons = new VisualElement(); confirmButtons.style.flexDirection = FlexDirection.Row; confirm.Add(confirmButtons);
            var keep = FooterButton("この表示を使う", KeepDisplayChange); var cancel = FooterButton("元に戻す", CancelDisplayChange);
            confirmButtons.Add(keep); confirmButtons.Add(cancel); root.Add(confirm);

            fpsLabel = new Label("FPS");
            fpsLabel.name = "settings-fps";
            fpsLabel.style.position = Position.Absolute;
            fpsLabel.style.left = 12f; fpsLabel.style.top = 10f;
            fpsLabel.style.color = new Color(1f, 0.84f, 0.45f);
            fpsLabel.style.backgroundColor = new Color(0.10f, 0.085f, 0.065f, 0.80f);
            fpsLabel.style.paddingLeft = 6f; fpsLabel.style.paddingRight = 6f;
            root.Add(fpsLabel);
        }

        private static VisualElement Card(float left, float top, float width, float height)
        {
            var card = new VisualElement(); card.style.position = Position.Absolute;
            card.style.left = Length.Percent(left * 100f); card.style.top = Length.Percent(top * 100f);
            card.style.width = Length.Percent(width * 100f); card.style.height = Length.Percent(height * 100f);
            card.style.paddingLeft = 18f; card.style.paddingRight = 18f; card.style.paddingTop = 14f; card.style.paddingBottom = 14f;
            card.style.backgroundColor = new Color(0.10f, 0.085f, 0.065f, 0.98f);
            card.style.borderTopWidth = 2f; card.style.borderBottomWidth = 2f; card.style.borderLeftWidth = 2f; card.style.borderRightWidth = 2f;
            card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = new Color(0.70f, 0.50f, 0.20f);
            card.pickingMode = PickingMode.Position; return card;
        }

        private static void AddTitle(VisualElement parent, string text)
        {
            var title = new Label(text); title.style.fontSize = 22f; title.style.color = new Color(1f, 0.84f, 0.45f); title.style.marginBottom = 10f; parent.Add(title);
        }

        private static Button MenuButton(string text, Action action)
        {
            var button = new Button(action) { text = text }; button.style.height = 42f; button.style.marginBottom = 8f; button.style.fontSize = 16f; return button;
        }

        private static Button FooterButton(string text, Action action)
        {
            var button = new Button(action) { text = text }; button.style.flexGrow = 1f; button.style.height = 34f; button.style.marginRight = 5f; return button;
        }

        private void ToggleMenu()
        {
            if (settings.style.display == DisplayStyle.Flex) return;
            menu.style.display = menu.style.display == DisplayStyle.Flex ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void OpenSettings()
        {
            pending = SettingsStore.Load(); atOpen = pending.Clone(); tab = 0;
            menu.style.display = DisplayStyle.None; settings.style.display = DisplayStyle.Flex; BuildTab();
        }

        private void CloseSettings()
        {
            if (settings.style.display == DisplayStyle.Flex) RevertSettings();
            settings.style.display = DisplayStyle.None; menu.style.display = DisplayStyle.None;
        }

        private void SelectTab(int index) { tab = index; BuildTab(); }

        private void BuildTab()
        {
            if (content == null) return;
            content.Clear();
            for (int i = 0; i < tabs.Count; i++) tabs[i].style.backgroundColor = i == tab ? new Color(0.48f, 0.31f, 0.12f) : new Color(0.18f, 0.15f, 0.11f);
            switch (tab)
            {
                case 0: BuildQualityTab(); break;
                case 1: BuildScreenTab(); break;
                case 2: BuildAudioTab(); break;
                case 3: BuildControlsTab(); break;
                default: BuildGameTab(); break;
            }
        }

        private void BuildQualityTab()
        {
            AddInfo("段を選ぶと細かい項目をまとめて変更します。細かく変えると「カスタム」になります。");
            GraphicsQualityLevel detected = GraphicsQualityRules.Detect(pending.Quality);
            AddChoiceRow("画質", new[] { "低", "中", "高", "最高", "カスタム" }, (int)detected,
                i => { if (i < 4) pending.Quality = GraphicsQualityRules.For((GraphicsQualityLevel)i); });
            AddChoiceRow("模型の細かさ", new[] { "低", "中", "高", "最高" }, Mathf.Clamp(pending.Quality.ModelDetail, 0, 3), i => pending.Quality.ModelDetail = i);
            AddChoiceRow("影", new[] { "ソフト・2048・50m", "ソフト・2048・80m", "ソフト・4096・120m", "ソフト・4096・180m" }, ShadowChoice(pending.Quality), i =>
            {
                pending.Quality.ShadowResolution = new[] { 2048, 2048, 4096, 4096 }[i];
                pending.Quality.ShadowDistance = new[] { 50f, 80f, 120f, 180f }[i];
                pending.Quality.ShadowCascades = 4;
            });
            AddChoiceRow("MSAA", new[] { "なし", "2x", "4x", "8x" }, QualityChoice(pending.Quality.Msaa, new[] { 0, 2, 4, 8 }), i => pending.Quality.Msaa = new[] { 0, 2, 4, 8 }[i]);
            AddChoiceRow("描画解像度", new[] { "100%", "125%" }, pending.Quality.RenderScale > 1.1f ? 1 : 0, i => pending.Quality.RenderScale = i == 1 ? 1.25f : 1f);
            AddChoiceRow("色の仕上げ", new[] { "なし", "ACES" }, pending.Quality.ToneMapping ? 1 : 0, i => pending.Quality.ToneMapping = i == 1);
            AddChoiceRow("光のにじみ", new[] { "切", "入" }, pending.Quality.Bloom ? 1 : 0, i => pending.Quality.Bloom = i == 1);
            AddChoiceRow("物の陰り", new[] { "切", "入", "高品質" }, pending.Quality.Ssao ? (pending.Quality.HighQualitySsao ? 2 : 1) : 0, i => { pending.Quality.Ssao = i > 0; pending.Quality.HighQualitySsao = i > 1; });
            AddChoiceRow("ビネット", new[] { "切", "入" }, pending.Quality.Vignette ? 1 : 0, i => pending.Quality.Vignette = i == 1);
            AddChoiceRow("異方性フィルタ", new[] { "切", "強制オン" }, pending.Quality.Anisotropic ? 1 : 0, i => pending.Quality.Anisotropic = i == 1);
            AddSlider("LOD bias", 1, 3, pending.Quality.LodBias, v => pending.Quality.LodBias = v);
            AddChoiceRow("森の飾りの木", new[] { "3000", "4500", "6000", "9000" }, QualityChoice(pending.Quality.ForestTrees, new[] { 3000, 4500, 6000, 9000 }), i => pending.Quality.ForestTrees = new[] { 3000, 4500, 6000, 9000 }[i]);
            AddChoiceRow("岩", new[] { "1200", "1800", "2400", "3600" }, QualityChoice(pending.Quality.MountainRocks, new[] { 1200, 1800, 2400, 3600 }), i => pending.Quality.MountainRocks = new[] { 1200, 1800, 2400, 3600 }[i]);
            AddChoiceRow("草・花", new[] { "8000", "16000", "28000", "48000" }, QualityChoice(pending.Quality.Decorations, new[] { 8000, 16000, 28000, 48000 }), i => pending.Quality.Decorations = new[] { 8000, 16000, 28000, 48000 }[i]);
            AddToggle("FPS を表示", pending.Quality.ShowFps, v => pending.Quality.ShowFps = v);
        }

        private void BuildScreenTab()
        {
            AddChoiceRow("表示モード", new[] { "全画面（ボーダーレス）", "全画面（排他）", "ウィンドウ" }, (int)pending.DisplayMode, i => pending.DisplayMode = (DisplayMode)i);
            var resolutions = SettingsStore.AvailableResolutions();
            if (!resolutions.Contains(pending.Resolution)) resolutions.Add(pending.Resolution);
            var labels = new string[Mathf.Min(resolutions.Count, 12)]; for (int i = 0; i < labels.Length; i++) labels[i] = resolutions[i].ToString();
            int selected = 0; for (int i = 0; i < labels.Length; i++) if (resolutions[i].Equals(pending.Resolution)) selected = i;
            AddChoiceRow("解像度", labels, selected, i => pending.Resolution = resolutions[i]);
            AddToggle("垂直同期", pending.VSync, v => pending.VSync = v);
            AddChoiceRow("FPS の上限", new[] { "30", "60", "120", "144", "無制限" }, pending.FpsLimit <= 0 ? 4 : Mathf.Clamp(Array.IndexOf(new[] { 30, 60, 120, 144 }, pending.FpsLimit), 0, 3), i => pending.FpsLimit = i == 4 ? 0 : new[] { 30, 60, 120, 144 }[i]);
            AddSliderInt("UI の大きさ", 80, 150, pending.UiScale, v => pending.UiScale = v);
            AddSliderInt("明るさ（ガンマ）", -50, 50, pending.Gamma, v => pending.Gamma = v);
            AddInfo("解像度・全画面を適用すると15秒の確認が表示されます。");
        }

        private void BuildAudioTab()
        {
            AddAudio("全体", pending.MasterVolume, pending.MasterMute, (v, m) => { pending.MasterVolume = v; pending.MasterMute = m; GameAudio.MasterVolume = v; GameAudio.MasterMuted = m; });
            AddAudio("音楽", pending.MusicVolume, pending.MusicMute, (v, m) => { pending.MusicVolume = v; pending.MusicMute = m; GameAudio.MusicVolume = v; GameAudio.MusicMuted = m; });
            AddAudio("効果音", pending.EffectsVolume, pending.EffectsMute, (v, m) => { pending.EffectsVolume = v; pending.EffectsMute = m; GameAudio.EffectsVolume = v; GameAudio.EffectsMuted = m; });
            AddAudio("UI", pending.UiVolume, pending.UiMute, (v, m) => { pending.UiVolume = v; pending.UiMute = m; GameAudio.UiVolume = v; GameAudio.UiMuted = m; });
            AddToggle("裏にある時に音を消す", pending.MuteWhenUnfocused, v => { pending.MuteWhenUnfocused = v; GameAudio.MuteWhenUnfocused = v; });
        }

        private void BuildControlsTab()
        {
            AddInfo("キーを押して割り当てを変更できます。重なったキーは自動で入れ替えます。");
            AddKey("待機中の村人を選ぶ", KeyBindingAction.SelectIdleVillagers); AddKey("操作モード", KeyBindingAction.ToggleControlMode);
            AddKey("建物を動かす", KeyBindingAction.MoveBuilding); AddKey("取り壊す／予約取消", KeyBindingAction.RemoveOrCancel); AddKey("取消／メニュー", KeyBindingAction.MenuCancel);
            AddKey("選択に足す", KeyBindingAction.AddSelection); AddKey("カメラ上", KeyBindingAction.CameraUp); AddKey("カメラ下", KeyBindingAction.CameraDown);
            AddKey("カメラ左", KeyBindingAction.CameraLeft); AddKey("カメラ右", KeyBindingAction.CameraRight);
            AddKey("回転", KeyBindingAction.Rotate); AddKey("挿入物を切替", KeyBindingAction.NextInserter); AddKey("詳細表示", KeyBindingAction.DetailMode);
            var reset = new Button(() => { KeyBindings.Reset(); BuildTab(); }) { text = "キーを既定に戻す" }; content.Add(reset);
            AddSlider("カメラの速さ", 0.5f, 2f, pending.CameraSpeed, v => pending.CameraSpeed = v);
            AddSlider("ズームの速さ", 0.5f, 2f, pending.ZoomSpeed, v => pending.ZoomSpeed = v);
            AddToggle("ズーム方向を逆にする", pending.InvertZoom, v => pending.InvertZoom = v); AddToggle("画面端で移動", pending.EdgeScroll, v => pending.EdgeScroll = v);
            AddSliderInt("画面端の幅", 4, 64, pending.EdgeWidth, v => pending.EdgeWidth = v); AddSliderInt("ドラッグ判定距離", 4, 20, pending.DragThreshold, v => pending.DragThreshold = v);
        }

        private void BuildGameTab()
        {
            AddChoiceRow("言語", new[] { "日本語", "English" }, pending.Japanese ? 0 : 1, i => pending.Japanese = i == 0);
            AddChoiceRow("体力バー", new[] { "標準（今まで通り）", "常に", "傷ついた物だけ", "選んだ物だけ" }, (int)pending.HealthBars, i => pending.HealthBars = (HealthBarMode)i);
            AddToggle("ダメージの数字", pending.DamageNumbers, v => pending.DamageNumbers = v); AddToggle("操作の説明（ツールチップ）", pending.Tooltips, v => pending.Tooltips = v);
            AddChoiceRow("既定の試合の速さ", new[] { "x1", "x2", "x4" }, pending.DefaultGameSpeed == 4 ? 2 : pending.DefaultGameSpeed == 2 ? 1 : 0, i => pending.DefaultGameSpeed = new[] { 1, 2, 4 }[i]);
        }

        private void AddInfo(string text) { var label = new Label(text); label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 8f; label.style.color = new Color(0.80f, 0.75f, 0.65f); content.Add(label); }

        private void AddChoiceRow(string title, string[] options, int selected, Action<int> changed)
        {
            var row = Row(title); for (int i = 0; i < options.Length; i++) { int choice = i; var button = new Button(() => { changed(choice); BuildTab(); }) { text = (i == selected ? "✓ " : "") + options[i] }; button.style.marginRight = 4f; row.Add(button); }
        }

        private void AddToggle(string title, bool value, Action<bool> changed)
        {
            var row = Row(title); var toggle = new Toggle { value = value }; toggle.RegisterValueChangedCallback(e => changed(e.newValue)); row.Add(toggle);
        }

        private void AddSlider(string title, float min, float max, float value, Action<float> changed)
        {
            var row = Row(title); var slider = new Slider(min, max) { value = value }; slider.style.flexGrow = 1f; slider.RegisterValueChangedCallback(e => changed(e.newValue)); row.Add(slider);
        }

        private void AddSliderInt(string title, int min, int max, int value, Action<int> changed)
        {
            var row = Row(title); var slider = new SliderInt(min, max) { value = Mathf.Clamp(value, min, max) }; slider.style.flexGrow = 1f; slider.RegisterValueChangedCallback(e => changed(e.newValue)); row.Add(slider);
        }

        private void AddAudio(string title, float value, bool muted, Action<float, bool> changed)
        {
            var row = Row(title); var slider = new Slider(0f, 1f) { value = value }; slider.style.flexGrow = 1f;
            var toggle = new Toggle("ミュート") { value = muted }; slider.RegisterValueChangedCallback(e => changed(e.newValue, toggle.value)); toggle.RegisterValueChangedCallback(e => changed(slider.value, e.newValue)); row.Add(slider); row.Add(toggle);
        }

        private void AddKey(string title, KeyBindingAction action)
        {
            var row = Row(title); var button = new Button { text = KeyBindings.Display(action) }; button.style.minWidth = 105f; button.focusable = true;
            button.clicked += () => button.text = "キーを入力…";
            button.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode != KeyCode.None) { KeyBindings.Set(action, e.keyCode); button.text = KeyBindings.Display(action); e.StopPropagation(); } }); row.Add(button);
        }

        private VisualElement Row(string title)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.minHeight = 36f; row.style.marginBottom = 4f;
            var label = new Label(title); label.style.width = 220f; label.style.color = new Color(0.90f, 0.84f, 0.70f); row.Add(label); content.Add(row); return row;
        }

        private void RestartMatch()
        {
            var panel = FindFirstObjectByType<CommandPanel>();
            if (panel != null && panel.MatchRestart != null) panel.MatchRestart.RestartMatch(); else Debug.Log("RTS settings: title scene is unavailable; restart was requested.");
            menu.style.display = DisplayStyle.None;
        }

        private void ApplySettings()
        {
            beforeConfirm = SettingsStore.Load(); beforeConfirm.Resolution = new ResolutionKey(Screen.width, Screen.height);
            SettingsStore.Save(pending); SettingsStore.Apply(pending); CameraSettingsRuntime.Apply(pending); UpdateFpsVisibility();
            atOpen = pending.Clone();
            bool displayChanged = !beforeConfirm.Resolution.Equals(pending.Resolution) || beforeConfirm.DisplayMode != pending.DisplayMode;
            if (displayChanged) { confirmUntil = Time.unscaledTime + 15f; confirm.style.display = DisplayStyle.Flex; }
            else CloseSettings();
        }

        private void RevertSettings()
        {
            if (atOpen != null) { pending = atOpen.Clone(); SettingsStore.Save(pending); SettingsStore.Apply(pending); }
            else pending = SettingsStore.Load();
            BuildTab();
        }

        private void ResetTab()
        {
            var defaultsState = new SettingsValues();
            switch (tab)
            {
                case 0: pending.Quality = defaultsState.Quality; break;
                case 1: pending.DisplayMode = DisplayMode.Borderless; pending.Resolution = new ResolutionKey(Screen.width, Screen.height); pending.VSync = false; pending.FpsLimit = 0; pending.UiScale = 100; pending.Gamma = 0; break;
                case 2: pending.MasterVolume = pending.MusicVolume = pending.EffectsVolume = pending.UiVolume = 1f; pending.MasterMute = pending.MusicMute = pending.EffectsMute = pending.UiMute = false; pending.MuteWhenUnfocused = false; break;
                case 3: pending.CameraSpeed = pending.ZoomSpeed = 1f; pending.InvertZoom = false; pending.EdgeScroll = false; pending.EdgeWidth = 16; pending.DragThreshold = 8; KeyBindings.Reset(); break;
                case 4: pending.Japanese = true; pending.HealthBars = HealthBarMode.Standard; pending.DamageNumbers = true; pending.Tooltips = true; pending.DefaultGameSpeed = 1; break;
            }
            BuildTab();
        }

        private void KeepDisplayChange() { confirm.style.display = DisplayStyle.None; confirmUntil = 0f; CloseSettings(); }

        private void CancelDisplayChange()
        {
            confirm.style.display = DisplayStyle.None; confirmUntil = 0f;
            if (beforeConfirm != null) { SettingsStore.Save(beforeConfirm); SettingsStore.Apply(beforeConfirm); pending = beforeConfirm.Clone(); }
            CloseSettings();
        }

        private static int ShadowChoice(GraphicsQualityValues value)
        {
            int[] resolutions = { 2048, 2048, 4096, 4096 };
            float[] distances = { 50f, 80f, 120f, 180f };
            for (int i = 0; i < distances.Length; i++) if (value.ShadowResolution == resolutions[i] && Mathf.Abs(value.ShadowDistance - distances[i]) < 0.01f) return i;
            return -1;
        }

        private static int QualityChoice(int value, int[] options)
        {
            for (int i = 0; i < options.Length; i++) if (value == options[i]) return i;
            return -1;
        }

        private void UpdateFpsVisibility()
        {
            if (fpsLabel == null) return;
            fpsLabel.style.display = GraphicsQuality.Current.ShowFps ? DisplayStyle.Flex : DisplayStyle.None;
            if (!GraphicsQuality.Current.ShowFps) { fpsSampleAt = Time.unscaledTime; fpsFrames = 0; }
        }

        private void UpdateFps()
        {
            if (fpsLabel == null || !GraphicsQuality.Current.ShowFps) return;
            fpsFrames++;
            if (Time.unscaledTime - fpsSampleAt < 0.5f) return;
            float elapsed = Mathf.Max(0.001f, Time.unscaledTime - fpsSampleAt);
            fpsLabel.text = "FPS " + Mathf.RoundToInt(fpsFrames / elapsed);
            fpsFrames = 0; fpsSampleAt = Time.unscaledTime;
        }

        private void OnDestroy()
        {
            if (panelSettings != null) Destroy(panelSettings);
            if (uiHost != null) Destroy(uiHost);
            if (fontAsset != null) Destroy(fontAsset);
        }
    }
}
