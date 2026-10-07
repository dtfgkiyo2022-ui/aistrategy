using System;
using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Standard-command buttons. Builds a UserPolicyIntent and sends it only through ICommandPort.</summary>
    public sealed class CommandPanel : MonoBehaviour
    {
        private const int MaxLogLines = 8;
        private const float ButtonWidth = 220f;
        private const float ButtonHeight = 28f;

        [SerializeField] private float mapWidthMeters = 256f;
        [SerializeField] private float mapHeightMeters = 128f;

        private ICommandPort port;
        private ICommandDelayControl delayControl;
        private IExternalAiControl externalAi;
        private IMatchRestart matchRestart;
        private IOpponentControl opponent;
        private ITacticControl opponentTactic;
        private IMapChoice mapChoice;
        private IMatchRuleChoice matchRuleChoice;
        private IPlayerFilesControl playerFiles;
        private IWorkshopControl workshop;
        private bool setupOpen;
        private uint factionId;
        private uint ownCoreId;
        private BattlefieldView view;
        private ulong issuerSequence;
        private bool awaitingGround;
        private TacticSignalView awaitingSignal;
        private readonly List<string> log = new List<string>();

        public bool IsAwaitingGround { get { return awaitingGround; } }

        private void Update()
        {
            if ((awaitingGround || awaitingSignal != null) && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)))
            {
                awaitingGround = false;
                awaitingSignal = null;
                AddLog(UiText.T("Cancelled.", "取り消しました。"));
            }
        }

        /// <summary>The optional outside AI. Null hides the panel, which is what the mock scene wants.</summary>
        public IExternalAiControl ExternalAi { get { return externalAi; } set { externalAi = value; } }

        /// <summary>Lets the result overlay start the next match. Null hides the button, which is what the mock scene wants.</summary>
        public IMatchRestart MatchRestart { get { return matchRestart; } set { matchRestart = value; } }

        /// <summary>Lets the player pick the opponent's doctrine. Null hides the picker, which is what the mock scene wants.</summary>
        public IOpponentControl Opponent { get { return opponent; } set { opponent = value; } }

        private IOpponentControl ownDoctrine;
        private ITacticControl ownTactic;
        /// <summary>Lets the player pick a doctrine for their own side too ("none" leaves the armies to the player). Null hides it.</summary>
        public IOpponentControl OwnDoctrine { get { return ownDoctrine; } set { ownDoctrine = value; } }

        /// <summary>Lets the host expose the opponent's optional tactic and its diagnostics.</summary>
        public ITacticControl OpponentTactic { get { return opponentTactic; } set { opponentTactic = value; } }

        /// <summary>Lets the host expose the own side's optional tactic and its diagnostics.</summary>
        public ITacticControl OwnTactic { get { return ownTactic; } set { ownTactic = value; } }

        /// <summary>Returns the completed match-pack path for the result overlay, if saving succeeded.</summary>
        public System.Func<string> MatchPackPathProvider;

        /// <summary>Random economy map or the classic one. Null hides the row.</summary>
        public IMapChoice MapChoice { get { return mapChoice; } set { mapChoice = value; } }

        /// <summary>Optional economy-map rules. Null hides the row.</summary>
        public IMatchRuleChoice MatchRuleChoice { get { return matchRuleChoice; } set { matchRuleChoice = value; } }

        /// <summary>Lets the Unity host open the player's folders and rescan tactics without restarting.</summary>
        public IPlayerFilesControl PlayerFiles { get { return playerFiles; } set { playerFiles = value; } }

        /// <summary>Lets the Unity host expose optional Steam Workshop actions without a Steamworks reference here.</summary>
        public IWorkshopControl Workshop { get { return workshop; } set { workshop = value; } }

        public void Bind(ICommandPort commandPort, uint faction, uint ownCore, BattlefieldView battlefield)
        {
            port = commandPort;
            delayControl = commandPort as ICommandDelayControl;
            factionId = faction;
            ownCoreId = ownCore;
            view = battlefield;
        }

        /// <summary>Another panel on the same screen (the economy panel): its area blocks clicks and it may take a ground click first.</summary>
        public System.Func<Vector2, bool> ExtraBlocksClick;
        public System.Func<Camera, Vector2, bool> ExtraGroundClick;

        public bool BlocksClick(Vector2 screenPoint)
        {
            return UiHitAreas.Shared.ContainsScreen(screenPoint, Screen.height);
        }

        // Left click while waiting for a ground target: returns true when the click was consumed.
        public bool TryConsumeGroundClick(Camera camera, Vector2 screenPoint)
        {
            if (ExtraGroundClick != null && ExtraGroundClick(camera, screenPoint)) return true;
            if ((!awaitingGround && awaitingSignal == null) || port == null) return false;
            var ray = camera.ScreenPointToRay(screenPoint);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter)) { AddLog(UiText.T("Click was not on the ground.", "地面ではない所をクリックしました。")); return true; }
            var hit = ray.GetPoint(enter);
            if (!GroundPointQuantizer.TryQuantize(hit.x, hit.z, mapWidthMeters, mapHeightMeters, out var point))
            {
                AddLog(UiText.T("Click was outside the map.", "マップの外をクリックしました。"));
                return true;
            }
            if (awaitingSignal != null)
            {
                var signal = awaitingSignal;
                awaitingSignal = null;
                if (!ownTactic.SendSignal(signal.Name, point, out var reason)) AddLog(reason);
                else AddLog(UiText.T(signal.Label + " sent", signal.Label + "を送りました"));
                return true;
            }
            awaitingGround = false;
            foreach (uint army in SelectedArmies())
                Send(PolicyKind.Focus, new ScopeKey(factionId, ScopeKind.Army, army), new PolicyGoal(GoalKind.Point, 0, point), 0,
                    UiText.T("Attack Army ", "攻撃 軍団 ") + army + " -> (" + hit.x.ToString("0.0") + ", " + hit.z.ToString("0.0") + ")");
            return true;
        }

        private const int ButtonRows = 7;
        private const float HeaderHeight = 44f;

        private UiLayoutRects Layout() { return UiLayout.Calculate(Screen.width, Screen.height); }
        private Rect ButtonsRect() { return Layout().Commands; }

        private Rect StatusRect() { return Layout().LogStatus; }

        private Rect SupplyRect() { return Layout().Supply; }

        // The match settings (reply delay, opponent, map, outside AI) change rarely and each restarts the match, so they
        // share one panel that stays folded; open, it sits over the supply box and the top of the battlefield.
        private Rect SetupButtonRect() { return new Rect(Layout().TopRight.x, Layout().TopRight.y, Layout().TopRight.width * 0.64f, 28f); }
        private Rect LanguageButtonRect() { var r = Layout().TopRight; return new Rect(r.x + r.width * 0.66f, r.y, r.width * 0.34f, 28f); }

        /// <summary>Called after the player switches the on-screen language, so the host can remember it.</summary>
        public System.Action<bool> LanguageChanged;
        private const float SetupRow = 30f;
        private Rect SetupRect() { return Layout().Setup; }

        private Rect ResultRect() { return Layout().Result; }

        private MatchOutcome? Outcome()
        {
            var frame = view == null ? null : view.LatestFrame;
            return frame == null ? (MatchOutcome?)null : MatchOutcome.Describe(frame.Result, factionId, frame.Tick);
        }

        private bool logOpen;

        /// <summary>The fold button at the top of the right column; the log opens under it.</summary>
        private Rect LogToggleRect() { return Layout().LogToggle; }

        private Rect LogRect() { return Layout().LogEntries; }

        private void OnGUI()
        {
            if (port == null) return;
            UiStyles.Begin();
            UiHitAreas.Shared.BeginFrame(Time.frameCount);
            var buttons = ButtonsRect();
            UiStyles.Box(buttons, UiText.T("Commands", "命令"));
            UiHitAreas.Shared.Register(buttons);
            UiHitAreas.Shared.Register(LogToggleRect());
            UiHitAreas.Shared.Register(SetupButtonRect());
            UiHitAreas.Shared.Register(LanguageButtonRect());
            var selection = view.Selected;
            bool armySelected = selection.Kind == SelectionKind.Army;
            string selectionText = view.DescribeSelection();
            GUI.Label(new Rect(buttons.x + 6f, buttons.y + 20f, buttons.width - 12f, 20f),
                selectionText.Length > 0 ? selectionText : UiText.T("Nothing selected: click an army or drag a box", "未選択：軍団をクリック、またはドラッグで囲む"));
            GUI.Label(new Rect(buttons.x + 6f, buttons.y + 38f, buttons.width - 12f, 20f), UiText.T("WASD move, wheel zoom", "WASDで移動、ホイールで拡大縮小"));
            float y = buttons.y + 24f + HeaderHeight;

            GUI.enabled = armySelected;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), awaitingGround ? UiText.T("Attack: click ground", "攻撃：地面をクリック") : UiText.T("Attack (pick ground)", "攻撃（地点を選ぶ）")))
                awaitingGround = true;
            y += ButtonHeight + 4f;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), UiText.T("Retreat", "撤退")))
                foreach (uint army in SelectedArmies())
                    Send(PolicyKind.Retreat, ArmyScope(army), new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 0, UiText.T("Retreat Army ", "撤退 軍団 ") + army);
            y += ButtonHeight + 4f;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), UiText.T("Defend own core", "自コアを守る")))
                foreach (uint army in SelectedArmies())
                    Send(PolicyKind.Defend, ArmyScope(army), new PolicyGoal(GoalKind.Core, ownCoreId, default(SimPoint)), 0, UiText.T("Defend Army ", "防衛 軍団 ") + army + UiText.T(" -> Core ", " → コア ") + ownCoreId);
            y += ButtonHeight + 4f;

            GUI.enabled = selection.Kind == SelectionKind.Outpost;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), UiText.T("Allow abandon outpost", "拠点の放棄を許す")))
                Send(PolicyKind.AllowAbandon, new ScopeKey(factionId, ScopeKind.Outpost, selection.Id), new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 0, UiText.T("Allow abandon Outpost ", "放棄を許可 拠点 ") + selection.Id);
            y += ButtonHeight + 4f;

            GUI.enabled = true;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), UiText.T("Keep reserve 30%", "予備を30%保つ")))
                Send(PolicyKind.MaintainReserve, new ScopeKey(factionId, ScopeKind.All, 0), new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 300, UiText.T("Keep reserve 30% (all)", "予備を30%保つ（全軍）"));
            y += ButtonHeight + 4f;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), UiText.T("Return to auto", "お任せに戻す")))
                Send(PolicyKind.ReturnToAuto, new ScopeKey(factionId, ScopeKind.All, 0), new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 0, UiText.T("Return to auto (all)", "お任せに戻す（全軍）"));
            y += ButtonHeight + 4f;
            if (awaitingGround && GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), UiText.T("Cancel", "取消")))
                awaitingGround = false;

            if (!setupOpen)
            {
                DrawSupply();
                UiHitAreas.Shared.Register(SupplyRect());
            }
            else UiHitAreas.Shared.Register(SetupRect());
            if (GUI.Button(SetupButtonRect(), setupOpen ? UiText.T("Match setup ▲", "試合の設定 ▲") : UiText.T("Match setup ▼", "試合の設定 ▼"))) setupOpen = !setupOpen;
            // Shows the language it switches to, in that language.
            if (GUI.Button(LanguageButtonRect(), UiText.Japanese ? "English" : "日本語"))
            {
                UiText.Japanese = !UiText.Japanese;
                if (LanguageChanged != null) LanguageChanged(UiText.Japanese);
            }

            // The command log and status stay folded unless the player opens them: they are for checking, not playing.
            if (GUI.Button(LogToggleRect(), logOpen ? UiText.T("Fold the command log ^", "命令の記録と状態を畳む ▲")
                : UiText.T("Command log and status v", "命令の記録と状態を開く ▼"))) logOpen = !logOpen;
            if (!logOpen) { if (setupOpen) DrawSetup(); DrawResult(); return; }
            var statusRect = StatusRect();
            UiStyles.Box(statusRect, UiText.T("Command status (7 states)", "命令の状態（7段階）"));
            UiHitAreas.Shared.Register(statusRect);
            var frame = view.LatestFrame;
            if (frame != null)
            {
                int shown = 0;
                int statusLines = Mathf.Max(1, (int)((statusRect.height - 26f) / UiStyles.LineHeight));
                for (int i = frame.Commands.Count - 1; i >= 0 && shown < Mathf.Min(MaxLogLines, statusLines); i--, shown++)
                {
                    var c = frame.Commands[i];
                    string reason = c.Reason == ReasonCode.None ? "" : " (" + c.Reason + ")";
                    string wait = "";
                    // While interpreting there is no apply tick yet, so show how long the reply has been awaited.
                    if (c.Status == CommandStatus.Interpreting) wait = UiText.T(" waiting for the reply (", " 返答待ち（") + Seconds(frame.Tick - c.AcceptedTick) + ")";
                    else if (c.Status == CommandStatus.Pending) wait = UiText.T(" applies in ", " 適用まで ") + Seconds(c.ApplyTick - frame.Tick);
                    GUI.Label(new Rect(statusRect.x + 6f, statusRect.y + 22f + shown * UiStyles.LineHeight, statusRect.width - 12f, UiStyles.LineHeight),
                        "#" + c.CommandId + " " + c.Kind + " " + c.Target.Kind + " " + c.Target.Id + " [" + c.Status + "]" + wait + reason);
                }
            }

            var logRect = LogRect();
            UiStyles.Box(logRect, UiText.T("Command log", "命令の記録"));
            UiHitAreas.Shared.Register(logRect);
            int logLines = Mathf.Max(0, (int)((logRect.height - 26f) / UiStyles.LineHeight));
            for (int i = 0; i < log.Count && i < logLines; i++)
                GUI.Label(new Rect(logRect.x + 6f, logRect.y + 22f + i * UiStyles.LineHeight, logRect.width - 12f, UiStyles.LineHeight), log[i]);

            if (setupOpen) DrawSetup();
            DrawResult();
        }

        // Drawn last so it sits on top. The battlefield stays visible behind it: the frame that ended the match is
        // still the frame on screen, and the player can read how it ended.
        private void DrawResult()
        {
            var outcome = Outcome();
            if (!outcome.HasValue) return;
            var rect = ResultRect();
            UiStyles.Box(rect, outcome.Value.Headline);
            UiHitAreas.Shared.Register(rect);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 22f, rect.width - 16f, 44f), outcome.Value.Headline, UiStyles.Heading);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 70f, rect.width - 24f, 40f), outcome.Value.Detail, UiStyles.Tiny);
            string packPath = MatchPackPathProvider == null ? "" : MatchPackPathProvider();
            if (!string.IsNullOrEmpty(packPath))
                GUI.Label(new Rect(rect.x + 12f, rect.y + 110f, rect.width - 24f, 32f), UiText.T("Pack saved: " + packPath, "記録パックの保存先：" + packPath), UiStyles.Tiny);
            if (matchRestart != null && GUI.Button(new Rect(rect.x + rect.width / 2f - 80f, rect.y + rect.height - 44f, 160f, 32f), UiText.T("Play again", "もう一度")))
                matchRestart.RestartMatch();
        }

        private static string Seconds(long ticks)
        {
            return (ticks < 0 ? 0 : ticks / 20f).ToString("0.0") + UiText.T("s", "秒");
        }

        private static string ReinforcementSite(GoalKind kind)
        {
            return kind == GoalKind.Core ? UiText.T("Core", "コア") : kind == GoalKind.Outpost ? UiText.T("Outpost", "拠点") : kind.ToString();
        }

        private void DrawSupply()
        {
            var frame = view.LatestFrame;
            var rect = SupplyRect();
            UiStyles.Box(rect, UiText.T("Supply / reinforcements", "兵站・増援"));
            if (frame == null) return;
            GUI.Label(new Rect(rect.x + 6f, rect.y + 22f, rect.width - 12f, 20f), UiText.T("Units ", "兵 ") + frame.AliveCount + " / " + frame.FactionCap);
            int row = 1;
            foreach (var r in frame.Reinforcements)
            {
                if (row > 2) break;
                GUI.Label(new Rect(rect.x + 6f, rect.y + 22f + row * 22f, rect.width - 12f, 20f),
                    ReinforcementSite(r.Kind) + " " + r.Id + UiText.T(": next reinforcement in ", "：次の増援まで ") + Seconds(r.TicksRemaining));
                row++;
            }
        }

        private Vector2 setupScroll;
        private float setupContentHeight = 400f;

        /// <summary>
        /// The setup rows (two tactic choices and their status windows included) can be taller than the space left
        /// above the economy panel, so they scroll inside the setup box instead of covering other panels.
        /// </summary>
        private void DrawSetup()
        {
            var rect = SetupRect();
            UiStyles.Box(rect, UiText.T("Match setup", "試合の設定"));
            var viewport = new Rect(rect.x, rect.y + 24f, rect.width, Mathf.Max(0f, rect.height - 28f));
            bool scrolls = setupContentHeight > viewport.height;
            var content = new Rect(0f, 0f, rect.width - (scrolls ? 18f : 0f), Mathf.Max(viewport.height, setupContentHeight));
            setupScroll = GUI.BeginScrollView(viewport, setupScroll, content);
            setupContentHeight = DrawSetupRows(new Rect(0f, -24f, content.width, content.height));
            GUI.EndScrollView();
        }

        /// <summary>Draws the setup rows with <paramref name="rect"/> as the box; returns the height they used.</summary>
        private float DrawSetupRows(Rect rect)
        {
            float x = rect.x + 8f, y = rect.y + 24f, labelWidth = 96f, cell = (rect.width - 16f - labelWidth) / 4f;

            // The UI Toolkit screen is being built a panel at a time (docs/ui-toolkit.md); this lets it be tried in place.
            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("New screen", "新しい画面"));
            bool toolkit = HudToolkit.IsEnabled;
            if (GUI.Toggle(new Rect(x + labelWidth, y, cell * 2f - 4f, 24f), toolkit,
                    toolkit ? UiText.T("On (trial)", "使う（試作）") : UiText.T("Off", "使わない"), GUI.skin.button) != toolkit)
                HudToolkit.SetEnabled(!toolkit);
            y += SetupRow;

            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Reply delay", "返答の遅延"));
            if (delayControl != null)
            {
                int[] options = { 0, 60, 200, 400 };
                string[] names = { "0s", "3s", "10s", "20s" };
                for (int i = 0; i < options.Length; i++)
                {
                    bool on = delayControl.DelayTicks == options[i];
                    if (GUI.Toggle(new Rect(x + labelWidth + i * cell, y, cell - 4f, 24f), on, names[i], GUI.skin.button) && !on) delayControl.DelayTicks = options[i];
                }
            }
            y += SetupRow;

            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Opponent", "相手の方針"));
            if (opponent != null)
            {
                var choices = opponent.Choices;
                float width = (rect.width - 16f - labelWidth) / choices.Length;
                for (int i = 0; i < choices.Length; i++)
                {
                    bool on = opponent.Current == choices[i];
                    if (GUI.Toggle(new Rect(x + labelWidth + i * width, y, width - 4f, 24f), on, PresetLabel(choices[i]), GUI.skin.button) && !on) opponent.Current = choices[i];
                }
            }
            y += SetupRow;

            // The same doctrines for the player's own side, so a hands-off match can run both sides alike.
            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Own side", "自軍の方針"));
            if (ownDoctrine != null)
            {
                var choices = ownDoctrine.Choices;
                float width = (rect.width - 16f - labelWidth) / choices.Length;
                for (int i = 0; i < choices.Length; i++)
                {
                    bool on = ownDoctrine.Current == choices[i];
                    if (GUI.Toggle(new Rect(x + labelWidth + i * width, y, width - 4f, 24f), on, PresetLabel(choices[i]), GUI.skin.button) && !on) ownDoctrine.Current = choices[i];
                }
            }
            y += SetupRow;

            // The own tactic comes first, with its knobs and signals right under its list: those are what the player
            // moves during a match. The opponent's list and knobs follow and are shown only.
            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Own tactic", "自軍の戦術"));
            y += Mathf.Max(SetupRow, DrawTacticChoices(ownTactic, new Rect(x + labelWidth, y, rect.width - 16f - labelWidth, 24f)) + 6f);
            y = DrawTacticHint(ownTactic, x, y, rect.width - 16f);
            y = DrawTacticStatus(ownTactic, UiText.T("Own tactic status", "自軍の戦術の状態"), x, y, rect.width - 16f);
            y = DrawTacticReloadControls(ownTactic, x, y, rect.width - 16f);
            y = DrawTacticParams(ownTactic, true, x, y, rect.width - 16f);
            if (workshop != null)
            {
                GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Workshop", "Workshop"));
                bool canPublish = ownTactic != null && workshop.CanPublishTactic(ownTactic.Current);
                GUI.enabled = canPublish && workshop.SteamAvailable;
                if (GUI.Button(new Rect(x + labelWidth, y, 150f, 24f), UiText.T("Publish tactic", "戦術を公開")))
                    workshop.PublishTactic(ownTactic.Current);
                GUI.enabled = true;
                string[] visibility = workshop.VisibilityChoices ?? new string[0];
                float visibilityWidth = visibility.Length == 0 ? 0f : (rect.width - 16f - labelWidth - 158f) / visibility.Length;
                for (int i = 0; i < visibility.Length; i++)
                {
                    bool on = workshop.Visibility == visibility[i];
                    if (GUI.Toggle(new Rect(x + labelWidth + 158f + i * visibilityWidth, y, visibilityWidth - 4f, 24f), on, visibility[i], GUI.skin.button) && !on)
                        workshop.Visibility = visibility[i];
                }
                y += SetupRow;
                if (GUI.Button(new Rect(x + labelWidth, y, 150f, 24f), UiText.T("Refresh Workshop", "Workshopを読み直す")))
                    workshop.RefreshWorkshopTactics();
                GUI.Label(new Rect(x + labelWidth + 158f, y, rect.width - 16f - labelWidth - 158f, 42f), workshop.WorkshopStatus ?? "", UiStyles.Tiny);
                y += 46f;
            }

            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Opponent tactic", "相手の戦術"));
            y += Mathf.Max(SetupRow, DrawTacticChoices(opponentTactic, new Rect(x + labelWidth, y, rect.width - 16f - labelWidth, 24f)) + 6f);
            y = DrawTacticStatus(opponentTactic, UiText.T("Opponent tactic status", "相手の戦術の状態"), x, y, rect.width - 16f);
            y = DrawTacticReloadControls(opponentTactic, x, y, rect.width - 16f);
            y = DrawTacticParams(opponentTactic, false, x, y, rect.width - 16f);

            if (playerFiles != null)
            {
                GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Folders", "フォルダ"));
                float folderWidth = (rect.width - 16f - labelWidth) / 4f;
                if (GUI.Button(new Rect(x + labelWidth, y, folderWidth - 4f, 24f), UiText.T("Open tactics folder", "戦術のフォルダを開く")))
                    playerFiles.OpenTacticsFolder();
                if (GUI.Button(new Rect(x + labelWidth + folderWidth, y, folderWidth - 4f, 24f), UiText.T("Open packs folder", "記録パックのフォルダを開く")))
                    playerFiles.OpenPacksFolder();
                if (GUI.Button(new Rect(x + labelWidth + folderWidth * 2f, y, folderWidth - 4f, 24f), UiText.T("Refresh list", "一覧を更新")))
                    playerFiles.RefreshTacticList();
                if (GUI.Button(new Rect(x + labelWidth + folderWidth * 3f, y, folderWidth - 4f, 24f), UiText.T("Export rulebook", "ルールブックを書き出す")))
                    playerFiles.ExportRulebook();
                y += SetupRow;
                if (!string.IsNullOrEmpty(playerFiles.RulebookStatus))
                {
                    GUI.Label(new Rect(x + labelWidth, y, rect.width - 16f - labelWidth, 32f), playerFiles.RulebookStatus, UiStyles.Tiny);
                    y += 34f;
                }
            }

            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Map", "マップ"));
            if (mapChoice != null)
            {
                bool economyMap = mapChoice.EconomyMap;
                float half = (rect.width - 16f - labelWidth) / 2f;
                bool now = GUI.Toggle(new Rect(x + labelWidth, y, half - 4f, 24f), economyMap, economyMap ? UiText.T("Random #", "ランダム #") + mapChoice.Seed + UiText.T(" (economy, lines, terrain)", "（内政・ライン・地形）") : UiText.T("Classic two roads", "旧来の二本道"), GUI.skin.button);
                if (now != economyMap) mapChoice.EconomyMap = now;
                GUI.enabled = economyMap;
                if (GUI.Button(new Rect(x + labelWidth + half, y, half - 4f, 24f), UiText.T("New random map", "新しいランダムマップ"))) mapChoice.NewMap();
                GUI.enabled = true;
            }
            y += SetupRow;

            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Extra rules", "追加ルール"));
            if (matchRuleChoice != null)
            {
                bool economyMap = mapChoice != null && mapChoice.EconomyMap;
                float half = (rect.width - 16f - labelWidth) / 2f;
                GUI.enabled = economyMap;
                bool monks = matchRuleChoice.Monks;
                bool monksNow = GUI.Toggle(new Rect(x + labelWidth, y, half - 4f, 24f), monks,
                    monks ? UiText.T("Monks: on", "僧侶：入") : UiText.T("Monks: off", "僧侶：切"), GUI.skin.button);
                if (monksNow != monks) matchRuleChoice.Monks = monksNow;
                bool ageVictory = matchRuleChoice.AgeVictory;
                bool ageVictoryNow = GUI.Toggle(new Rect(x + labelWidth + half, y, half - 4f, 24f), ageVictory,
                    ageVictory ? UiText.T("Age victory: on", "時代到達勝利：入") : UiText.T("Age victory: off", "時代到達勝利：切"), GUI.skin.button);
                if (ageVictoryNow != ageVictory) matchRuleChoice.AgeVictory = ageVictoryNow;
                GUI.enabled = true;
            }
            y += SetupRow;

            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Civilisations", "文明"));
            if (matchRuleChoice != null)
            {
                GUI.enabled = mapChoice != null && mapChoice.EconomyMap;
                bool all = matchRuleChoice.AllCivilisations;
                bool allNow = GUI.Toggle(new Rect(x + labelWidth, y, rect.width - 16f - labelWidth, 24f), all,
                    all ? UiText.T("All 14 (12 more civilisations; gold, monks and fish on)", "14個全部（森林〜聖地の12文明を追加、金・僧侶・漁あり）")
                        : UiText.T("First 2 only (agrarian, metallurgy)", "最初の2つだけ（農耕・冶金）"), GUI.skin.button);
                if (allNow != all) matchRuleChoice.AllCivilisations = allNow;
                GUI.enabled = true;
            }
            y += SetupRow;

            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Outside AI", "外部AI"));
            if (externalAi == null) return y + SetupRow;
            var line = new Rect(x + labelWidth, y, rect.width - 16f - labelWidth, 24f);
            if (!externalAi.KeyAvailable) { GUI.Label(line, UiText.T("Off. No key is set on this PC.", "切。このPCにはキーが設定されていません。")); return y + SetupRow; }
            bool ai = externalAi.Enabled;
            bool aiNow = GUI.Toggle(line, ai, ai ? UiText.T("On - asking an outside AI", "入 - 外部AIに聞いています") : UiText.T("Off - ask an outside AI", "切 - 外部AIに聞く"), GUI.skin.button);
            if (aiNow != ai) externalAi.Enabled = aiNow;
            // The notice stays next to the switch: turning it on sends what the faction can see to an outside service.
            GUI.Label(new Rect(x, y + 26f, rect.width - 16f, 34f), ai ? externalAi.Status.Replace("\n", "   ")
                : UiText.T("Turning it on sends what your side can see (positions, counts, outposts) to an outside service.", "入れると、自陣営に見えている情報（位置・人数・拠点）を外部のサービスに送ります。"));
            return y + 64f;
        }

        private static string PresetLabel(string name)
        {
            switch (name)
            {
                case "none": return UiText.T("none", "なし");
                case "maintain": return UiText.T("maintain", "維持型");
                case "concentrate": return UiText.T("concentrate", "集中型");
                case "maintain-legacy": return UiText.T("maintain-legacy", "維持型（旧）");
                default: return name;
            }
        }

        /// <summary>
        /// Lays the tactic buttons out in as many rows as their names need, so no name is cut off.
        /// Returns the height used (one row is <paramref name="area"/>'s height).
        /// </summary>
        private static float DrawTacticChoices(ITacticControl control, Rect area)
        {
            if (control == null) return 0f;
            var views = control.ChoiceViews;
            if (views != null && views.Count != 0)
            {
                float viewWidest = 0f;
                for (int i = 0; i < views.Count; i++)
                    viewWidest = Mathf.Max(viewWidest, GUI.skin.button.CalcSize(new GUIContent(TacticLabel(views[i]))).x + 12f);
                int viewColumns = Mathf.Clamp((int)(area.width / Mathf.Max(1f, viewWidest)), 1, views.Count);
                int viewRows = (views.Count + viewColumns - 1) / viewColumns;
                float viewWidth = area.width / viewColumns;
                // Only a row holding a recommended tactic (name plus a description line) is two lines tall.
                float rowY = area.y;
                for (int row = 0; row < viewRows; row++)
                {
                    bool twoLines = false;
                    for (int i = row * viewColumns; i < Math.Min(views.Count, (row + 1) * viewColumns); i++)
                        if (views[i].Recommended && !string.IsNullOrWhiteSpace(views[i].Description)) twoLines = true;
                    float rowHeight = twoLines ? 44f : area.height;
                    for (int i = row * viewColumns; i < Math.Min(views.Count, (row + 1) * viewColumns); i++)
                    {
                        var choice = views[i];
                        bool on = control.Current == choice.Selection;
                        var cell = new Rect(area.x + (i % viewColumns) * viewWidth, rowY, viewWidth - 4f, rowHeight);
                        if (GUI.Toggle(cell, on, TacticLabel(choice), GUI.skin.button) && !on) control.Current = choice.Selection;
                    }
                    rowY += rowHeight + 4f;
                }
                return rowY - area.y - 4f;
            }
            var choices = control.Choices;
            if (choices == null || choices.Length == 0) return 0f;
            float widest = 0f;
            for (int i = 0; i < choices.Length; i++)
                widest = Mathf.Max(widest, GUI.skin.button.CalcSize(new GUIContent(TacticLabel(choices[i]))).x + 12f);
            int columns = Mathf.Clamp((int)(area.width / Mathf.Max(1f, widest)), 1, choices.Length);
            int rows = (choices.Length + columns - 1) / columns;
            float width = area.width / columns;
            for (int i = 0; i < choices.Length; i++)
            {
                bool on = control.Current == choices[i];
                var cell = new Rect(area.x + (i % columns) * width, area.y + (i / columns) * (area.height + 4f), width - 4f, area.height);
                if (GUI.Toggle(cell, on, TacticLabel(choices[i]), GUI.skin.button) && !on) control.Current = choices[i];
            }
            return rows * area.height + (rows - 1) * 4f;
        }

        private static string TacticLabel(string selection)
        {
            if (string.IsNullOrEmpty(selection)) return UiText.T("none (preset)", "なし（方針プリセット）");
            return System.IO.Path.GetFileName(selection);
        }

        private static string TacticLabel(TacticChoiceView choice)
        {
            if (string.IsNullOrEmpty(choice.Selection)) return TacticLabel(choice.Selection);
            string name = string.IsNullOrEmpty(choice.DisplayName) ? System.IO.Path.GetFileName(choice.Selection) : choice.DisplayName;
            string type = choice.Style == "auto" ? UiText.T("All hands-off", "全部お任せ型")
                : choice.Style == "partner" ? UiText.T("Partner", "相棒型") : "";
            string prefix = choice.Recommended ? UiText.T("Recommended: ", "おすすめ：") : "";
            string suffix = type.Length == 0 ? "" : " (" + type + ")";
            // Only the recommended pair carries its one-line description; on every button it made the list too tall.
            return prefix + name + suffix + (!choice.Recommended || string.IsNullOrWhiteSpace(choice.Description) ? "" : "\n" + choice.Description);
        }

        private static float DrawTacticHint(ITacticControl control, float x, float y, float width)
        {
            if (control == null || string.IsNullOrEmpty(control.Current) || control.ChoiceViews == null) return y;
            for (int i = 0; i < control.ChoiceViews.Count; i++)
            {
                var choice = control.ChoiceViews[i];
                if (choice.Selection != control.Current || choice.Style != "partner") continue;
                GUI.Label(new Rect(x, y, width, 40f), UiText.T(
                    "This tactic leaves units you control alone. Select a unit and move it yourself.",
                    "この戦術は、あなたが動かしている部隊には触れません。部隊を選んで動かしてみてください。"), UiStyles.Tiny);
                return y + 42f;
            }
            return y;
        }

        private static float DrawTacticStatus(ITacticControl control, string title, float x, float y, float width)
        {
            if (control == null || !control.Active) return y;
            string failure = control.FailureCount == 0 ? UiText.T("none", "なし") : control.LastFailureReason;
            string stopped = control.Disabled ? UiText.T(" STOPPED (10 consecutive failures)", " 停止（10回連続失敗）") : "";
            string summary = title + ": " + control.Name
                + UiText.T(" | last tick ", "｜最後のtick ") + control.LastTick
                + UiText.T(" | commands ", "｜命令 ") + control.SentCommands
                + UiText.T(" | discarded ", "｜破棄 ") + control.RejectedCommands
                + UiText.T(" | failures ", "｜失敗 ") + control.FailureCount
                + UiText.T(" | last reason ", "｜最後の理由 ") + failure + stopped;
            GUI.Label(new Rect(x, y, width, 22f), summary, UiStyles.Tiny);
            y += 20f;
            var lines = control.ConsoleLines;
            var recent = new List<string>();
            if (lines != null)
            {
                int start = Mathf.Max(0, lines.Count - 5);
                for (int i = start; i < lines.Count; i++) recent.Add(lines[i]);
            }
            string console = recent.Count == 0 ? UiText.T("none", "なし") : string.Join("\n", recent.ToArray());
            GUI.Label(new Rect(x, y, width, 70f), UiText.T("console.log (latest 5): ", "console.log（最新5行）：") + console, UiStyles.Tiny);
            y += 70f;
            return y + 2f;
        }

        private static float DrawTacticReloadControls(ITacticControl control, float x, float y, float width)
        {
            if (control == null || !control.Active) return y;
            float buttonWidth = width * 0.42f;
            bool auto = control.AutoReload;
            bool next = GUI.Toggle(new Rect(x, y, buttonWidth - 4f, 24f), auto,
                auto ? UiText.T("Auto reload: on", "自動で読み直す：入") : UiText.T("Auto reload: off", "自動で読み直す：切"), GUI.skin.button);
            if (next != auto) control.AutoReload = next;
            if (GUI.Button(new Rect(x + buttonWidth, y, width - buttonWidth, 24f), UiText.T("Reload", "読み直す")))
                control.Reload();
            y += 26f;
            if (!string.IsNullOrEmpty(control.ReloadMessage))
            {
                GUI.Label(new Rect(x, y, width, 34f), control.ReloadMessage, UiStyles.Tiny);
                y += 36f;
            }
            return y + 2f;
        }

        private float DrawTacticParams(ITacticControl control, bool editable, float x, float y, float width)
        {
            if (control == null || !control.Active) return y;
            if (control.Parameters != null && control.Parameters.Count != 0)
            {
                GUI.Label(new Rect(x, y, width, 22f), UiText.T("Tactic knobs", "戦術のつまみ"), UiStyles.Body);
                y += 22f;
            }
            foreach (var parameter in control.Parameters ?? Array.Empty<TacticParamView>())
            {
                object raw;
                if (!control.ParamValues.TryGetValue(parameter.Name, out raw)) raw = parameter.DefaultValue;
                string label = UiText.T(parameter.Name, parameter.Label);
                if (parameter.Type == "bool")
                {
                    bool value = raw is bool b && b;
                    if (editable)
                    {
                        bool next = GUI.Toggle(new Rect(x, y, width, 24f), value, label + ": " + (value ? "on" : "off"), GUI.skin.button);
                        if (next != value) control.SetParam(parameter.Name, next);
                    }
                    else GUI.Label(new Rect(x, y, width, 24f), label + ": " + (value ? "on" : "off"));
                }
                else if (parameter.Type == "choice")
                {
                    GUI.Label(new Rect(x, y, width, 22f), label + ": " + (raw ?? ""));
                    if (editable)
                    {
                        float cell = width / parameter.Choices.Count;
                        for (int i = 0; i < parameter.Choices.Count; i++)
                        {
                            string choice = parameter.Choices[i];
                            if (GUI.Button(new Rect(x + i * cell, y + 22f, cell - 4f, 22f), choice) && !Equals(raw, choice))
                                control.SetParam(parameter.Name, choice);
                        }
                        y += 22f;
                    }
                }
                else
                {
                    decimal current = raw is decimal m ? m : System.Convert.ToDecimal(raw, System.Globalization.CultureInfo.InvariantCulture);
                    GUI.Label(new Rect(x, y, width, 20f), label + ": " + current.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    if (editable)
                    {
                        // The bare slider was nearly invisible on the dark panel (owner, 10-08): a box behind it and
                        // −/+ buttons that move one step make the knob findable and usable without dragging.
                        decimal step = parameter.Step ?? (parameter.Type == "int" ? 1m : (parameter.Max.Value - parameter.Min.Value) / 20m);
                        float left = x + width * 0.42f, sliderWidth = width * 0.56f;
                        GUI.Box(new Rect(left + 28f, y, sliderWidth - 56f, 22f), GUIContent.none);
                        decimal next = current;
                        if (GUI.Button(new Rect(left, y, 24f, 22f), "−")) next = current - step;
                        if (GUI.Button(new Rect(left + sliderWidth - 24f, y, 24f, 22f), "+")) next = current + step;
                        float slider = GUI.HorizontalSlider(new Rect(left + 34f, y + 5f, sliderWidth - 68f, 18f),
                            (float)current, (float)parameter.Min.Value, (float)parameter.Max.Value);
                        if (next == current)
                        {
                            next = parameter.Type == "int" ? Math.Round((decimal)slider, 0, System.MidpointRounding.AwayFromZero) : (decimal)slider;
                            if (parameter.Step.HasValue) next = parameter.Min.Value + Math.Round((next - parameter.Min.Value) / parameter.Step.Value, 0, System.MidpointRounding.AwayFromZero) * parameter.Step.Value;
                        }
                        next = Math.Min(parameter.Max.Value, Math.Max(parameter.Min.Value, next));
                        if (next != current) control.SetParam(parameter.Name, parameter.Type == "int" ? (object)(int)next : next);
                    }
                }
                y += editable && parameter.Type == "choice" ? 48f : 26f;
            }
            return DrawTacticSignals(control, editable, x, y, width) + 2f;
        }

        private float DrawTacticSignals(ITacticControl control, bool editable, float x, float y, float width)
        {
            if (control == null || !control.Active || !editable || control.Signals == null || control.Signals.Count == 0) return y;
            GUI.Label(new Rect(x, y, width, 22f), UiText.T("Tactic signals", "戦術の合図"), UiStyles.Body);
            y += 24f;
            int columns = Mathf.Min(2, control.Signals.Count);
            float cell = width / columns;
            for (int i = 0; i < control.Signals.Count; i++)
            {
                var signal = control.Signals[i];
                var rect = new Rect(x + (i % columns) * cell, y + (i / columns) * 28f, cell - 4f, 24f);
                string text = signal.Label + (signal.NeedsPoint ? UiText.T(" (pick point)", "（地点を選ぶ）") : "");
                if (!GUI.Button(rect, text)) continue;
                if (signal.NeedsPoint)
                {
                    awaitingSignal = signal;
                    awaitingGround = false;
                    AddLog(UiText.T("Click the map for " + signal.Label + ". Esc/right-click cancels.", signal.Label + "の地点を地図でクリックしてください。Esc/右クリックで取消。"));
                }
                else if (!control.SendSignal(signal.Name, null, out var reason)) AddLog(reason);
                else AddLog(UiText.T(signal.Label + " sent", signal.Label + "を送りました"));
            }
            return y + ((control.Signals.Count + columns - 1) / columns) * 28f;
        }

        private ScopeKey ArmyScope(uint army) { return new ScopeKey(factionId, ScopeKind.Army, army); }

        /// <summary>A copy, so sending (which may change the frame) never walks a list that is changing.</summary>
        private uint[] SelectedArmies()
        {
            var armies = new uint[view.SelectedArmies.Count];
            for (int i = 0; i < armies.Length; i++) armies[i] = view.SelectedArmies[i];
            return armies;
        }

        private void Send(PolicyKind kind, ScopeKey target, PolicyGoal goal, ushort reservePermille, string description)
        {
            issuerSequence++;
            var intent = new UserPolicyIntent(
                issuerSequence, target, kind, goal, 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), reservePermille,
                new Expiration(long.MaxValue, 0, ExpireFlags.SubjectGone));
            ulong requestId = port.Submit(intent);
            AddLog("#" + requestId + " " + description);
        }

        private void AddLog(string line)
        {
            log.Add(line);
            if (log.Count > MaxLogLines) log.RemoveAt(0);
        }
    }
}
