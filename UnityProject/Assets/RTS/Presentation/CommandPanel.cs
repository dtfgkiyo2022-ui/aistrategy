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
            return ButtonsRect().Contains(guiPoint) || LogRect().Contains(guiPoint) || StatusRect().Contains(guiPoint)
                || SupplyRect().Contains(guiPoint) || DelayRect().Contains(guiPoint)
                || (externalAi != null && ExternalAiRect().Contains(guiPoint))
                || (opponent != null && OpponentRect().Contains(guiPoint))
                || (Outcome().HasValue && ResultRect().Contains(guiPoint));
        }

        // Left click while waiting for a ground target: returns true when the click was consumed.
        public bool TryConsumeGroundClick(Camera camera, Vector2 screenPoint)
        {
            if (ExtraGroundClick != null && ExtraGroundClick(camera, screenPoint)) return true;
            if (!awaitingGround || port == null) return false;
            var ray = camera.ScreenPointToRay(screenPoint);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter)) { AddLog("Click was not on the ground."); return true; }
            var hit = ray.GetPoint(enter);
            if (!GroundPointQuantizer.TryQuantize(hit.x, hit.z, mapWidthMeters, mapHeightMeters, out var point))
            {
                AddLog("Click was outside the map.");
                return true;
            }
            awaitingGround = false;
            var selection = view.Selected;
            Send(PolicyKind.Focus, new ScopeKey(factionId, ScopeKind.Army, selection.Id), new PolicyGoal(GoalKind.Point, 0, point), 0,
                "Attack Army " + selection.Id + " -> (" + hit.x.ToString("0.0") + ", " + hit.z.ToString("0.0") + ")");
            return true;
        }

        private const int ButtonRows = 7;
        private const float HeaderHeight = 44f;

        private Rect ButtonsRect() { return new Rect(10f, Screen.height - 10f - ButtonRows * (ButtonHeight + 4f) - HeaderHeight, ButtonWidth + 8f, ButtonRows * (ButtonHeight + 4f) + 4f + HeaderHeight); }

        private Rect StatusRect() { return new Rect(Screen.width - 430f, LogRect().yMax + 8f, 422f, MaxLogLines * 20f + 30f); }

        private Rect SupplyRect() { return new Rect(10f, 40f, 250f, 26f + 22f * 3f); }

        private Rect DelayRect() { return new Rect(10f, SupplyRect().yMax + 8f, 250f, 62f); }

        private const int OpponentRows = 4;
        private Rect OpponentRect() { return new Rect(10f, DelayRect().yMax + 8f, 250f, 28f + OpponentRows * 28f); }

        // Top centre, under the match clock. The left column is supply, reply delay and the command buttons, and the
        // buttons grow upward from the bottom edge, so anything stacked under the delay box runs into them on a short
        // window; the right column is the log and the timeline. The clock's bottom edge is 8 + 58 (TimelinePanel).
        private const float ClockBottom = 66f;
        private Rect ExternalAiRect() { return new Rect(Screen.width / 2f - 200f, ClockBottom + 6f, 400f, 78f); }

        private Rect ResultRect() { return new Rect(Screen.width / 2f - 190f, Screen.height / 2f - 80f, 380f, 160f); }

        private MatchOutcome? Outcome()
        {
            var frame = view == null ? null : view.LatestFrame;
            return frame == null ? (MatchOutcome?)null : MatchOutcome.Describe(frame.Result, factionId, frame.Tick);
        }

        private Rect LogRect() { return new Rect(Screen.width - 430f, 8f, 422f, MaxLogLines * 20f + 30f); }

        private void OnGUI()
        {
            if (port == null) return;
            var buttons = ButtonsRect();
            GUI.Box(buttons, "Commands");
            var selection = view.Selected;
            bool armySelected = selection.Kind == SelectionKind.Army;
            string selectionText = view.DescribeSelection();
            GUI.Label(new Rect(buttons.x + 6f, buttons.y + 20f, buttons.width - 12f, 20f),
                selectionText.Length > 0 ? selectionText : "Nothing selected: click an army");
            GUI.Label(new Rect(buttons.x + 6f, buttons.y + 38f, buttons.width - 12f, 20f), "WASD move, wheel zoom");
            float y = buttons.y + 24f + HeaderHeight;

            GUI.enabled = armySelected;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), awaitingGround ? "Attack: click ground" : "Attack (pick ground)"))
                awaitingGround = true;
            y += ButtonHeight + 4f;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), "Retreat"))
                Send(PolicyKind.Retreat, ArmyScope(selection), new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 0, "Retreat Army " + selection.Id);
            y += ButtonHeight + 4f;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), "Defend own core"))
                Send(PolicyKind.Defend, ArmyScope(selection), new PolicyGoal(GoalKind.Core, ownCoreId, default(SimPoint)), 0, "Defend Army " + selection.Id + " -> Core " + ownCoreId);
            y += ButtonHeight + 4f;

            GUI.enabled = selection.Kind == SelectionKind.Outpost;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), "Allow abandon outpost"))
                Send(PolicyKind.AllowAbandon, new ScopeKey(factionId, ScopeKind.Outpost, selection.Id), new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 0, "Allow abandon Outpost " + selection.Id);
            y += ButtonHeight + 4f;

            GUI.enabled = true;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), "Keep reserve 30%"))
                Send(PolicyKind.MaintainReserve, new ScopeKey(factionId, ScopeKind.All, 0), new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 300, "Keep reserve 30% (all)");
            y += ButtonHeight + 4f;
            if (GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), "Return to auto"))
                Send(PolicyKind.ReturnToAuto, new ScopeKey(factionId, ScopeKind.All, 0), new PolicyGoal(GoalKind.None, 0, default(SimPoint)), 0, "Return to auto (all)");
            y += ButtonHeight + 4f;
            if (awaitingGround && GUI.Button(new Rect(buttons.x + 4f, y, ButtonWidth, ButtonHeight), "Cancel"))
                awaitingGround = false;

            DrawSupply();
            DrawDelaySelector();
            if (opponent != null) DrawOpponentSelector();
            if (externalAi != null) DrawExternalAi();

            var statusRect = StatusRect();
            GUI.Box(statusRect, "Command status (7 states)");
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
                    if (c.Status == CommandStatus.Interpreting) wait = " waiting for the reply (" + Seconds(frame.Tick - c.AcceptedTick) + ")";
                    else if (c.Status == CommandStatus.Pending) wait = " applies in " + Seconds(c.ApplyTick - frame.Tick);
                    GUI.Label(new Rect(statusRect.x + 6f, statusRect.y + 22f + shown * 20f, statusRect.width - 12f, 20f),
                        "#" + c.CommandId + " " + c.Kind + " " + c.Target.Kind + " " + c.Target.Id + " [" + c.Status + "]" + wait + reason);
                }
            }

            var logRect = LogRect();
            GUI.Box(logRect, "Command log");
            for (int i = 0; i < log.Count; i++)
                GUI.Label(new Rect(logRect.x + 6f, logRect.y + 22f + i * 20f, logRect.width - 12f, 20f), log[i]);

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
            if (matchRestart != null && GUI.Button(new Rect(rect.x + rect.width / 2f - 80f, rect.y + rect.height - 44f, 160f, 32f), "Play again"))
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
            GUI.Box(rect, "Supply / reinforcements");
            if (frame == null) return;
            GUI.Label(new Rect(rect.x + 6f, rect.y + 22f, rect.width - 12f, 20f), "Units " + frame.AliveCount + " / " + frame.FactionCap);
            int row = 1;
            foreach (var r in frame.Reinforcements)
            {
                if (row > 2) break;
                GUI.Label(new Rect(rect.x + 6f, rect.y + 22f + row * 22f, rect.width - 12f, 20f),
                    r.Kind + " " + r.Id + ": next in " + Seconds(r.TicksRemaining));
                row++;
            }
        }

        private void DrawExternalAi()
        {
            var rect = ExternalAiRect();
            GUI.Box(rect, "Outside AI (optional)");
            if (!externalAi.KeyAvailable)
            {
                GUI.Label(new Rect(rect.x + 6f, rect.y + 22f, rect.width - 12f, 52f),
                    "Off. No key is set on this PC, so it cannot be turned on.");
                return;
            }
            bool on = externalAi.Enabled;
            bool now = GUI.Toggle(new Rect(rect.x + 6f, rect.y + 20f, rect.width - 12f, 22f), on, on ? "On - asking an outside AI" : "Off - ask an outside AI", GUI.skin.button);
            if (now != on) externalAi.Enabled = now; // this restarts the match, like the reply delay above
            var text = new Rect(rect.x + 6f, rect.y + 44f, rect.width - 12f, 32f);
            // The notice is on screen next to the switch, not behind it: turning it on sends the faction's view out.
            GUI.Label(text, on ? externalAi.Status.Replace("\n", "   ")
                : "Turning it on restarts the match and sends what your side can see (positions, counts, outposts) to an outside service.");
        }

        private void DrawDelaySelector()
        {
            var rect = DelayRect();
            GUI.Box(rect, "AI reply delay (verification)");
            if (delayControl == null) return;
            int[] options = { 0, 60, 200, 400 };
            string[] names = { "0s", "3s", "10s", "20s" };
            for (int i = 0; i < options.Length; i++)
            {
                bool on = delayControl.DelayTicks == options[i];
                var buttonRect = new Rect(rect.x + 6f + i * 60f, rect.y + 24f, 56f, 26f);
                if (GUI.Toggle(buttonRect, on, names[i], GUI.skin.button) && !on) delayControl.DelayTicks = options[i];
            }
        }

        // Picking a different doctrine restarts the match immediately, like the reply delay and outside AI above:
        // this is the "choose an opponent and start" screen, folded into the panel that is already on screen from
        // tick 0 rather than a separate pre-game screen, so it works the same way whether it is the first match or
        // the fifth "Play again".
        private void DrawOpponentSelector()
        {
            var rect = OpponentRect();
            GUI.Box(rect, "Opponent (restarts the match)");
            var choices = opponent.Choices;
            for (int i = 0; i < choices.Length; i++)
            {
                bool on = opponent.Current == choices[i];
                var buttonRect = new Rect(rect.x + 6f, rect.y + 24f + i * 28f, rect.width - 12f, 24f);
                if (GUI.Toggle(buttonRect, on, choices[i], GUI.skin.button) && !on) opponent.Current = choices[i];
            }
        }

        private ScopeKey ArmyScope(SelectionTarget selection) { return new ScopeKey(factionId, ScopeKind.Army, selection.Id); }

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
