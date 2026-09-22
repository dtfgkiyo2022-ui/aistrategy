using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Left click picks the nearest army marker or core on screen and passes its frame ID to the view. Dragging draws a
    /// box instead, and every own army whose marker ends inside it is selected together.
    /// </summary>
    public sealed class BattlefieldSelector : MonoBehaviour
    {
        [SerializeField] private BattlefieldView view;
        [SerializeField] private CommandPanel panel;
        [SerializeField] private TimelinePanel timelinePanel;
        [SerializeField] private float pickRadiusPixels = 36f;
        /// <summary>A press that moves less than this is a click, not a box.</summary>
        [SerializeField] private float dragThresholdPixels = 8f;

        private bool pressed;
        private Vector2 pressAt;

        private void Update()
        {
            var camera = Camera.main;
            if (camera == null) return;
            if (Input.GetMouseButtonDown(0))
            {
                pressed = false;
                if (panel != null && panel.BlocksClick(Input.mousePosition)) return;
                if (timelinePanel != null && timelinePanel.BlocksClick(Input.mousePosition)) return;
                if (panel != null && panel.TryConsumeGroundClick(camera, Input.mousePosition)) return;
                pressed = true;
                pressAt = Input.mousePosition;
                return;
            }
            if (!pressed || !Input.GetMouseButtonUp(0)) return;
            pressed = false;
            Vector2 releaseAt = Input.mousePosition;
            if ((releaseAt - pressAt).magnitude < dragThresholdPixels)
            {
                view.Select(view.TryPick(camera, pressAt, pickRadiusPixels, out var target) ? target : SelectionTarget.None);
                return;
            }
            view.SelectArmies(view.ArmiesInScreenRect(camera, BoxRect(pressAt, releaseAt)));
        }

        private static Rect BoxRect(Vector2 a, Vector2 b)
            => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        private void OnGUI()
        {
            if (!pressed) return;
            Vector2 now = Input.mousePosition;
            if ((now - pressAt).magnitude < dragThresholdPixels) return;
            // Screen coordinates start at the bottom, GUI coordinates at the top.
            var box = BoxRect(pressAt, now);
            var rect = new Rect(box.xMin, Screen.height - box.yMax, box.width, box.height);
            var old = GUI.color;
            GUI.color = new Color(1f, 0.9f, 0.2f, 0.35f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
