using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Left click picks the nearest army marker or core on screen and passes its frame ID to the view.</summary>
    public sealed class BattlefieldSelector : MonoBehaviour
    {
        [SerializeField] private BattlefieldView view;
        [SerializeField] private CommandPanel panel;
        [SerializeField] private TimelinePanel timelinePanel;
        [SerializeField] private float pickRadiusPixels = 36f;

        private void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            var camera = Camera.main;
            if (camera == null) return;
            if (panel != null && panel.BlocksClick(Input.mousePosition)) return;
            if (timelinePanel != null && timelinePanel.BlocksClick(Input.mousePosition)) return;
            if (panel != null && panel.TryConsumeGroundClick(camera, Input.mousePosition)) return;
            view.Select(view.TryPick(camera, Input.mousePosition, pickRadiusPixels, out var target) ? target : SelectionTarget.None);
        }

        private void OnGUI()
        {
            var text = view.DescribeSelection();
            if (text.Length > 0) GUI.Label(new Rect(12f, 10f, 500f, 24f), text);
        }
    }
}
