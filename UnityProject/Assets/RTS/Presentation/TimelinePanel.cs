using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Verification UI: speed 1/2/4, pause, single tick, faction view, and the event timeline.</summary>
    public sealed class TimelinePanel : MonoBehaviour
    {
        private const int VisibleLines = 12;

        [SerializeField] private BattlefieldView view;
        [SerializeField] private MonoBehaviour clockSource;

        private readonly MatchTimeline timeline = new MatchTimeline();
        private IMatchClock clock;

        public MatchTimeline Timeline { get { return timeline; } }

        /// <summary>The clock used by both the old IMGUI panel and the UI Toolkit clock.</summary>
        public IMatchClock MatchClock { get { return Clock; } }

        public bool BlocksClick(Vector2 screenPoint)
        {
            return UiHitAreas.Shared.ContainsScreen(screenPoint, Screen.height);
        }

        private IMatchClock Clock { get { return clock ?? (clock = clockSource as IMatchClock); } }

        private static Rect ClockRect() { return UiLayout.Calculate(Screen.width, Screen.height).TopCenter; }

        private static Rect TimelineRect() { return UiLayout.Calculate(Screen.width, Screen.height).Timeline; }

        public string ClockDisplayText()
        {
            var current = Clock;
            return current == null ? UiText.T("Clock", "時計")
                : UiText.T("Elapsed ", "経過 ") + MatchOutcome.Clock(current.Tick) + "  (t=" + current.Tick + ")"
                    + UiText.T("  faction ", "  陣営 ") + current.ViewFactionId;
        }

        public string TimelineEntryText(MatchTimeline.Entry entry)
        {
            return MatchOutcome.Clock(entry.Tick) + "  " + entry.Text;
        }

        public void TogglePause()
        {
            var current = Clock;
            if (current != null) current.Paused = !current.Paused;
        }

        public void StepOneTick()
        {
            var current = Clock;
            if (current != null) current.StepOneTick();
        }

        public void SetSpeed(int speed)
        {
            var current = Clock;
            if (current != null) current.SpeedMultiplier = speed;
        }

        public void ToggleViewFaction()
        {
            var current = Clock;
            if (current != null) current.ViewFactionId = 3 - current.ViewFactionId;
        }

        private void Update()
        {
            long probe = PerfProbe.Start();
            UpdateMeasured();
            PerfProbe.Stop("TimelinePanel", probe);
        }

        private void UpdateMeasured()
        {
            timeline.Ingest(view.LatestFrame);
        }

        private void OnGUI()
        {
            if (HudToolkit.IsEnabled) return;
            UiStyles.Begin();
            UiHitAreas.Shared.BeginFrame(Time.frameCount);
            var clockRect = ClockRect();
            var current = Clock;
            // Minutes and seconds first (20 ticks a second); the raw tick stays for checking replays and logs.
            UiStyles.Box(clockRect, ClockDisplayText());
            UiHitAreas.Shared.Register(clockRect);
            if (current != null)
            {
                float x = clockRect.x + 8f;
                if (GUI.Button(new Rect(x, clockRect.y + 28f, 56f, 22f), current.Paused ? UiText.T("Play", "再生") : UiText.T("Pause", "停止")))
                    TogglePause();
                x += 60f;
                if (GUI.Button(new Rect(x, clockRect.y + 28f, 48f, 22f), "+1")) StepOneTick();
                x += 52f;
                foreach (int speed in new[] { 1, 2, 4 })
                {
                    bool on = current.SpeedMultiplier == speed;
                    if (GUI.Toggle(new Rect(x, clockRect.y + 28f, 32f, 22f), on, "x" + speed, GUI.skin.button) && !on)
                        SetSpeed(speed);
                    x += 36f;
                }
                x += 4f;
                if (GUI.Button(new Rect(x, clockRect.y + 28f, 72f, 22f), UiText.T("View ", "陣営 ") + (3 - current.ViewFactionId)))
                    ToggleViewFaction();
            }

            var timelineRect = TimelineRect();
            UiStyles.Box(timelineRect, UiText.T("Timeline", "時系列"));
            UiHitAreas.Shared.Register(timelineRect);
            var entries = timeline.Entries;
            // Row height follows the text size (a fixed 18 px cut the bottom of the letters once the text grew).
            float row = UiStyles.LineHeight;
            float top = UiStyles.HeaderHeight + 2f;
            int lines = Mathf.Max(1, (int)((timelineRect.height - top - 4f) / row));
            int first = Mathf.Max(0, entries.Count - lines);
            for (int i = first; i < entries.Count; i++)
                GUI.Label(new Rect(timelineRect.x + 6f, timelineRect.y + top + (i - first) * row, timelineRect.width - 12f, row),
                    TimelineEntryText(entries[i]));
        }
    }
}
