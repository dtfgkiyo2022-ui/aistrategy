using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Left click picks the nearest army marker, core, or own villager on screen and passes its frame ID to the view.
    /// Dragging draws a box instead; a box containing an own soldier keeps the existing army selection behavior.
    /// </summary>
    public sealed class BattlefieldSelector : MonoBehaviour
    {
        [SerializeField] private BattlefieldView view;
        [SerializeField] private CommandPanel panel;
        [SerializeField] private TimelinePanel timelinePanel;
        [SerializeField] private EconomyLayer economyLayer;
        [SerializeField] private EconomyPanel economyPanel;
        [SerializeField] private float pickRadiusPixels = 36f;
        [SerializeField] private float villagerPickRadiusPixels = 30f;
        /// <summary>A press that moves less than this is a click, not a box.</summary>
        [SerializeField] private float dragThresholdPixels = 8f;

        private bool pressed;
        private Vector2 pressAt;

        private void Update()
        {
            long probe = PerfProbe.Start();
            UpdateMeasured();
            PerfProbe.Stop("Selector", probe);
        }

        private void UpdateMeasured()
        {
            UiHitAreas.Shared.BeginFrame(Time.frameCount);
            var camera = Camera.main;
            if (camera == null) return;
            if (economyLayer == null) economyLayer = FindFirstObjectByType<EconomyLayer>();
            if (economyPanel == null) economyPanel = FindFirstObjectByType<EconomyPanel>();
            if (!UiHitAreas.Shared.KeyboardCaptured && Input.GetKeyDown(KeyCode.H) && economyLayer != null)
            {
                economyLayer.SelectIdleVillagers();
                if (economyLayer.HasVillagerSelection && view != null) view.Select(SelectionTarget.None);
                return;
            }
            if (Input.GetMouseButtonDown(1))
            {
                if (UiHitAreas.Shared.ContainsScreen(Input.mousePosition, Screen.height)) return;
                if (panel != null && panel.IsAwaitingGround)
                {
                    panel.CancelGroundPick();
                    return;
                }
                // The order itself is an economy command, so it goes through the economy panel's port.
                if (economyLayer != null && economyLayer.HasVillagerSelection && economyPanel != null)
                {
                    economyPanel.TryIssueSelectedVillagerOrder(camera, Input.mousePosition);
                    return;
                }
                if (panel != null && view != null && view.Selected.Kind == SelectionKind.Army)
                    panel.TryIssueSelectedArmyOrder(camera, Input.mousePosition);
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                pressed = false;
                if (UiHitAreas.Shared.ContainsScreen(Input.mousePosition, Screen.height)) return;
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
                bool additive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                uint villagerId;
                if (economyLayer != null && economyLayer.TryPickOwnVillager(camera, pressAt, villagerPickRadiusPixels, out villagerId))
                {
                    economyLayer.SelectVillager(villagerId, additive);
                    view.Select(SelectionTarget.None);
                    return;
                }
                if (view.TryPick(camera, pressAt, pickRadiusPixels, out var target))
                {
                    if (economyLayer != null) economyLayer.ClearVillagerSelection();
                    view.Select(target);
                }
                else
                {
                    if (!additive && economyLayer != null) economyLayer.ClearVillagerSelection();
                    view.Select(SelectionTarget.None);
                }
                return;
            }
            var box = BoxRect(pressAt, releaseAt);
            var armies = view.ArmiesInScreenRect(camera, box);
            if (armies.Count > 0)
            {
                if (economyLayer != null) economyLayer.ClearVillagerSelection();
                view.SelectArmies(armies);
                return;
            }
            if (economyLayer != null)
            {
                economyLayer.SelectVillagers(economyLayer.VillagersInScreenRect(camera, box),
                    Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                view.Select(SelectionTarget.None);
            }
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
