using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Standard-command buttons. Builds a UserPolicyIntent and sends it only through ICommandPort.</summary>
    public sealed class CommandPanel : MonoBehaviour
    {
        private const int MaxLogLines = 8;
        private const float ButtonWidth = 150f;
        private const float ButtonHeight = 28f;

        [SerializeField] private float mapWidthMeters = 256f;
        [SerializeField] private float mapHeightMeters = 128f;

        private ICommandPort port;
        private ICommandDelayControl delayControl;
        private uint factionId;
        private uint ownCoreId;
        private BattlefieldView view;
        private ulong issuerSequence;
        private bool awaitingGround;
        private readonly List<string> log = new List<string>();

        public bool IsAwaitingGround { get { return awaitingGround; } }

        public void Bind(ICommandPort commandPort, uint faction, uint ownCore, BattlefieldView battlefield)
        {
            port = commandPort;
            delayControl = commandPort as ICommandDelayControl;
            factionId = faction;
            ownCoreId = ownCore;
            view = battlefield;
        }

        public bool BlocksClick(Vector2 screenPoint)
        {
            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return ButtonsRect().Contains(guiPoint) || LogRect().Contains(guiPoint) || StatusRect().Contains(guiPoint)
                || SupplyRect().Contains(guiPoint) || DelayRect().Contains(guiPoint);
        }

        // Left click while waiting for a ground target: returns true when the click was consumed.
        public bool TryConsumeGroundClick(Camera camera, Vector2 screenPoint)
        {
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

        private Rect ButtonsRect() { return new Rect(10f, Screen.height - 10f - ButtonRows * (ButtonHeight + 4f), ButtonWidth + 8f, ButtonRows * (ButtonHeight + 4f) + 4f); }

        private Rect StatusRect() { return new Rect(Screen.width - 430f, LogRect().yMax + 8f, 422f, MaxLogLines * 20f + 30f); }

        private Rect SupplyRect() { return new Rect(10f, 40f, 250f, 26f + 22f * 3f); }

        private Rect DelayRect() { return new Rect(10f, SupplyRect().yMax + 8f, 250f, 62f); }

        private Rect LogRect() { return new Rect(Screen.width - 430f, 8f, 422f, MaxLogLines * 20f + 30f); }

        private void OnGUI()
        {
            if (port == null) return;
            var buttons = ButtonsRect();
            GUI.Box(buttons, "Commands");
            var selection = view.Selected;
            bool armySelected = selection.Kind == SelectionKind.Army;
            float y = buttons.y + 24f;

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
                    long remaining = c.ApplyTick - frame.Tick;
                    if (c.Status == CommandStatus.Interpreting) wait = " reserving, applies in " + Seconds(remaining);
                    else if (c.Status == CommandStatus.Pending) wait = " applies in " + Seconds(remaining);
                    GUI.Label(new Rect(statusRect.x + 6f, statusRect.y + 22f + shown * 20f, statusRect.width - 12f, 20f),
                        "#" + c.CommandId + " " + c.Kind + " " + c.Target.Kind + " " + c.Target.Id + " [" + c.Status + "]" + wait + reason);
                }
            }

            var logRect = LogRect();
            GUI.Box(logRect, "Command log");
            for (int i = 0; i < log.Count; i++)
                GUI.Label(new Rect(logRect.x + 6f, logRect.y + 22f + i * 20f, logRect.width - 12f, 20f), log[i]);
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
