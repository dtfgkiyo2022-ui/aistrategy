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
        private IMapChoice mapChoice;
        private IMatchRuleChoice matchRuleChoice;
        private bool setupOpen;
        private uint factionId;
        private uint ownCoreId;
        private BattlefieldView view;
        private ulong issuerSequence;
        private bool awaitingGround;
        private readonly List<string> log = new List<string>();

        public bool IsAwaitingGround { get { return awaitingGround; } }

        /// <summary>The optional outside AI. Null hides the panel, which is what the mock scene wants.</summary>
        public IExternalAiControl ExternalAi { get { return externalAi; } set { externalAi = value; } }

        /// <summary>Lets the result overlay start the next match. Null hides the button, which is what the mock scene wants.</summary>
        public IMatchRestart MatchRestart { get { return matchRestart; } set { matchRestart = value; } }

        /// <summary>Lets the player pick the opponent's doctrine. Null hides the picker, which is what the mock scene wants.</summary>
        public IOpponentControl Opponent { get { return opponent; } set { opponent = value; } }

        /// <summary>Random economy map or the classic one. Null hides the row.</summary>
        public IMapChoice MapChoice { get { return mapChoice; } set { mapChoice = value; } }

        /// <summary>Optional economy-map rules. Null hides the row.</summary>
        public IMatchRuleChoice MatchRuleChoice { get { return matchRuleChoice; } set { matchRuleChoice = value; } }

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
            if (ExtraBlocksClick != null && ExtraBlocksClick(screenPoint)) return true;
            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return ButtonsRect().Contains(guiPoint) || LogToggleRect().Contains(guiPoint)
                || (logOpen && (LogRect().Contains(guiPoint) || StatusRect().Contains(guiPoint)))
                || SupplyRect().Contains(guiPoint) || SetupButtonRect().Contains(guiPoint) || LanguageButtonRect().Contains(guiPoint)
                || (setupOpen && SetupRect().Contains(guiPoint))
                || (Outcome().HasValue && ResultRect().Contains(guiPoint));
        }

        // Left click while waiting for a ground target: returns true when the click was consumed.
        public bool TryConsumeGroundClick(Camera camera, Vector2 screenPoint)
        {
            if (ExtraGroundClick != null && ExtraGroundClick(camera, screenPoint)) return true;
            if (!awaitingGround || port == null) return false;
            var ray = camera.ScreenPointToRay(screenPoint);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter)) { AddLog(UiText.T("Click was not on the ground.", "地面ではない所をクリックしました。")); return true; }
            var hit = ray.GetPoint(enter);
            if (!GroundPointQuantizer.TryQuantize(hit.x, hit.z, mapWidthMeters, mapHeightMeters, out var point))
            {
                AddLog(UiText.T("Click was outside the map.", "マップの外をクリックしました。"));
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

        private Rect ButtonsRect() { return new Rect(10f, Screen.height - 10f - ButtonRows * (ButtonHeight + 4f) - HeaderHeight, ButtonWidth + 8f, ButtonRows * (ButtonHeight + 4f) + 4f + HeaderHeight); }

        private Rect StatusRect() { return new Rect(Screen.width - 430f, LogRect().yMax + 8f, 422f, MaxLogLines * 20f + 30f); }

        private Rect SupplyRect() { return new Rect(10f, 40f, 250f, 26f + 22f * 3f); }

        // The match settings (reply delay, opponent, map, outside AI) change rarely and each restarts the match, so they
        // share one panel that stays folded; open, it sits over the supply box and the top of the battlefield.
        private Rect SetupButtonRect() { return new Rect(10f, 8f, 250f, 26f); }
        private Rect LanguageButtonRect() { return new Rect(264f, 8f, 90f, 26f); }

        /// <summary>Called after the player switches the on-screen language, so the host can remember it.</summary>
        public System.Action<bool> LanguageChanged;
        private const float SetupRow = 30f;
        private Rect SetupRect() { return new Rect(10f, 40f, 470f, 26f + SetupRow * 4f + 62f); }

        private Rect ResultRect() { return new Rect(Screen.width / 2f - 190f, Screen.height / 2f - 80f, 380f, 160f); }

        private MatchOutcome? Outcome()
        {
            var frame = view == null ? null : view.LatestFrame;
            return frame == null ? (MatchOutcome?)null : MatchOutcome.Describe(frame.Result, factionId, frame.Tick);
        }

        private bool logOpen;

        /// <summary>The fold button at the top of the right column; the log opens under it.</summary>
        private Rect LogToggleRect() { return new Rect(Screen.width - 430f, 8f, 422f, 26f); }

        private Rect LogRect() { return new Rect(Screen.width - 430f, 38f, 422f, MaxLogLines * 20f + 30f); }

        private void OnGUI()
        {
            if (port == null) return;
            var buttons = ButtonsRect();
            GUI.Box(buttons, UiText.T("Commands", "命令"));
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

            if (!setupOpen) DrawSupply();
            if (GUI.Button(SetupButtonRect(), setupOpen ? UiText.T("Match setup (close)", "試合の設定（閉じる）") : UiText.T("Match setup (delay, opponent, map, outside AI)", "試合の設定（遅延・相手・マップ・外部AI）"))) setupOpen = !setupOpen;
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
            GUI.Box(statusRect, UiText.T("Command status (7 states)", "命令の状態（7段階）"));
            var frame = view.LatestFrame;
            if (frame != null)
            {
                int shown = 0;
                for (int i = frame.Commands.Count - 1; i >= 0 && shown < MaxLogLines; i--, shown++)
                {
                    var c = frame.Commands[i];
                    string reason = c.Reason == ReasonCode.None ? "" : " (" + c.Reason + ")";
                    string wait = "";
                    // While interpreting there is no apply tick yet, so show how long the reply has been awaited.
                    if (c.Status == CommandStatus.Interpreting) wait = UiText.T(" waiting for the reply (", " 返答待ち（") + Seconds(frame.Tick - c.AcceptedTick) + ")";
                    else if (c.Status == CommandStatus.Pending) wait = UiText.T(" applies in ", " 適用まで ") + Seconds(c.ApplyTick - frame.Tick);
                    GUI.Label(new Rect(statusRect.x + 6f, statusRect.y + 22f + shown * 20f, statusRect.width - 12f, 20f),
                        "#" + c.CommandId + " " + c.Kind + " " + c.Target.Kind + " " + c.Target.Id + " [" + c.Status + "]" + wait + reason);
                }
            }

            var logRect = LogRect();
            GUI.Box(logRect, UiText.T("Command log", "命令の記録"));
            for (int i = 0; i < log.Count; i++)
                GUI.Label(new Rect(logRect.x + 6f, logRect.y + 22f + i * 20f, logRect.width - 12f, 20f), log[i]);

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
            GUI.Box(rect, outcome.Value.Headline);
            var big = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            var small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            GUI.Label(new Rect(rect.x + 8f, rect.y + 22f, rect.width - 16f, 44f), outcome.Value.Headline, big);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 70f, rect.width - 24f, 40f), outcome.Value.Detail, small);
            if (matchRestart != null && GUI.Button(new Rect(rect.x + rect.width / 2f - 80f, rect.y + rect.height - 44f, 160f, 32f), UiText.T("Play again", "もう一度")))
                matchRestart.RestartMatch();
        }

        private static string Seconds(long ticks)
        {
            return (ticks < 0 ? 0 : ticks / 20f).ToString("0.0") + "s";
        }

        private void DrawSupply()
        {
            var frame = view.LatestFrame;
            var rect = SupplyRect();
            GUI.Box(rect, UiText.T("Supply / reinforcements", "兵站・増援"));
            if (frame == null) return;
            GUI.Label(new Rect(rect.x + 6f, rect.y + 22f, rect.width - 12f, 20f), UiText.T("Units ", "兵 ") + frame.AliveCount + " / " + frame.FactionCap);
            int row = 1;
            foreach (var r in frame.Reinforcements)
            {
                if (row > 2) break;
                GUI.Label(new Rect(rect.x + 6f, rect.y + 22f + row * 22f, rect.width - 12f, 20f),
                    r.Kind + " " + r.Id + UiText.T(": next in ", "：次まで ") + Seconds(r.TicksRemaining));
                row++;
            }
        }

        private void DrawSetup()
        {
            var rect = SetupRect();
            GUI.Box(rect, UiText.T("Match setup (every change restarts the match)", "試合の設定（変えると試合が最初から始まります）"));
            float x = rect.x + 8f, y = rect.y + 24f, labelWidth = 96f, cell = (rect.width - 16f - labelWidth) / 4f;

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

            GUI.Label(new Rect(x, y, labelWidth, 24f), UiText.T("Outside AI", "外部AI"));
            if (externalAi == null) return;
            var line = new Rect(x + labelWidth, y, rect.width - 16f - labelWidth, 24f);
            if (!externalAi.KeyAvailable) { GUI.Label(line, UiText.T("Off. No key is set on this PC.", "切。このPCにはキーが設定されていません。")); return; }
            bool ai = externalAi.Enabled;
            bool aiNow = GUI.Toggle(line, ai, ai ? UiText.T("On - asking an outside AI", "入 - 外部AIに聞いています") : UiText.T("Off - ask an outside AI", "切 - 外部AIに聞く"), GUI.skin.button);
            if (aiNow != ai) externalAi.Enabled = aiNow;
            // The notice stays next to the switch: turning it on sends what the faction can see to an outside service.
            GUI.Label(new Rect(x, y + 26f, rect.width - 16f, 34f), ai ? externalAi.Status.Replace("\n", "   ")
                : UiText.T("Turning it on sends what your side can see (positions, counts, outposts) to an outside service.", "入れると、自陣営に見えている情報（位置・人数・拠点）を外部のサービスに送ります。"));
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
