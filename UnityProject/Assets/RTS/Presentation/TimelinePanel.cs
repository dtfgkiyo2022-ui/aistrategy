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

        private static Rect ClockRect() { return new Rect(Screen.width / 2f - 200f, 8f, 400f, 58f); }

        private static Rect TimelineRect() { return new Rect(Screen.width / 2f - 240f, 72f, 480f, VisibleLines * 18f + 26f); }

        private void Update()
        {
            timeline.Ingest(view.LatestFrame);
        }

        private void OnGUI()
        {
            var clockRect = ClockRect();
            var current = Clock;
            GUI.Box(clockRect, current == null ? "Clock" : "Clock  t=" + current.Tick + "  faction " + current.ViewFactionId);
            if (current != null)
            {
                float x = clockRect.x + 8f;
                if (GUI.Button(new Rect(x, clockRect.y + 26f, 64f, 24f), current.Paused ? "Play" : "Pause"))
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
                if (GUI.Button(new Rect(x, clockRect.y + 26f, 100f, 24f), "View faction " + (3 - current.ViewFactionId)))
                    current.ViewFactionId = 3 - current.ViewFactionId;
            }

            var timelineRect = TimelineRect();
            GUI.Box(timelineRect, "Timeline");
            var entries = timeline.Entries;
            int first = Mathf.Max(0, entries.Count - VisibleLines);
            for (int i = first; i < entries.Count; i++)
                GUI.Label(new Rect(timelineRect.x + 6f, timelineRect.y + 20f + (i - first) * 18f, timelineRect.width - 12f, 18f),
                    "t" + entries[i].Tick + "  " + entries[i].Text);
        }
    }
}
