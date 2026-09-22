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

        public bool BlocksClick(Vector2 screenPoint)
        {
            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return ClockRect().Contains(guiPoint) || TimelineRect().Contains(guiPoint);
        }

        private IMatchClock Clock { get { return clock ?? (clock = clockSource as IMatchClock); } }

        // Under the stock bar that runs across the top (EconomyPanel), so the two never overlap.
        private static Rect ClockRect() { return new Rect(Screen.width / 2f - 200f, 40f, 400f, 58f); }

        private static Rect TimelineRect()
        {
            float top = 8f + 2f * (8f * 20f + 30f) + 16f;
            float bottom = Screen.height - 10f;
            float height = Mathf.Clamp(bottom - top, 62f, VisibleLines * 18f + 26f);
            return new Rect(Screen.width - 432f, bottom - height, 422f, height);
        }

        private void Update()
        {
            timeline.Ingest(view.LatestFrame);
        }

        private void OnGUI()
        {
            var clockRect = ClockRect();
            var current = Clock;
            GUI.Box(clockRect, current == null ? UiText.T("Clock", "時計") : UiText.T("Clock  t=", "時計  t=") + current.Tick + UiText.T("  faction ", "  陣営 ") + current.ViewFactionId);
            if (current != null)
            {
                float x = clockRect.x + 8f;
                if (GUI.Button(new Rect(x, clockRect.y + 26f, 64f, 24f), current.Paused ? UiText.T("Play", "再生") : UiText.T("Pause", "停止")))
                    current.Paused = !current.Paused;
                x += 68f;
                if (GUI.Button(new Rect(x, clockRect.y + 26f, 64f, 24f), "+1 tick")) current.StepOneTick();
                x += 68f;
                foreach (int speed in new[] { 1, 2, 4 })
                {
                    bool on = current.SpeedMultiplier == speed;
                    if (GUI.Toggle(new Rect(x, clockRect.y + 26f, 40f, 24f), on, "x" + speed, GUI.skin.button) && !on)
                        current.SpeedMultiplier = speed;
                    x += 44f;
                }
                x += 8f;
                if (GUI.Button(new Rect(x, clockRect.y + 26f, 100f, 24f), UiText.T("View faction ", "陣営を見る ") + (3 - current.ViewFactionId)))
                    current.ViewFactionId = 3 - current.ViewFactionId;
            }

            var timelineRect = TimelineRect();
            GUI.Box(timelineRect, UiText.T("Timeline", "時系列"));
            var entries = timeline.Entries;
            int lines = Mathf.Max(1, (int)((timelineRect.height - 26f) / 18f));
            int first = Mathf.Max(0, entries.Count - lines);
            for (int i = first; i < entries.Count; i++)
                GUI.Label(new Rect(timelineRect.x + 6f, timelineRect.y + 20f + (i - first) * 18f, timelineRect.width - 12f, 18f),
                    "t" + entries[i].Tick + "  " + entries[i].Text);
        }
    }
}
