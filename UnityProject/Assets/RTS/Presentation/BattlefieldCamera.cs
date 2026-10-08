using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>WASD/arrow keys pan, mouse wheel zooms. Camera looks at a ground point kept inside the map.</summary>
    public sealed class BattlefieldCamera : MonoBehaviour
    {
        [SerializeField] private float mapWidth = 256f;
        [SerializeField] private float mapHeight = 128f;
        [SerializeField] private float panSpeed = 1.1f;
        [SerializeField] private float zoomStep = 12f;
        private float nextBlockedLog;
        [SerializeField] private float minDistance = 25f;
        [SerializeField] private float maxDistance = 170f;

        private Vector3 focus;
        private float distance;

        private void Start()
        {
            var t = transform;
            var forward = t.forward;
            distance = forward.y < -0.01f ? Mathf.Clamp(-t.position.y / forward.y, minDistance, maxDistance) : 100f;
            focus = t.position + forward * distance;
            focus.y = 0f;
        }

        private void LateUpdate()
        {
            var move = UiHitAreas.Shared.KeyboardCaptured
                ? Vector3.zero
                : new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 1f) move.Normalize();
            focus += move * (panSpeed * distance * Time.unscaledDeltaTime);
            focus.x = Mathf.Clamp(focus.x, 0f, mapWidth);
            focus.z = Mathf.Clamp(focus.z, 0f, mapHeight);

            float wheel = Input.mouseScrollDelta.y;
            bool overPanel = UiHitAreas.Shared.ContainsScreen(Input.mousePosition, Screen.height);
            if (!overPanel && Mathf.Abs(wheel) > 0.01f)
                distance = Mathf.Clamp(distance - wheel * zoomStep, minDistance, maxDistance);
            else if (overPanel && Mathf.Abs(wheel) > 0.01f && Time.unscaledTime >= nextBlockedLog)
            {
                // The wheel over a panel scrolls the panel, not the map. Logged (at most once a second) so a panel
                // that covers more than it shows can be found from Editor.log (zoom stopped working once, 10-08).
                nextBlockedLog = Time.unscaledTime + 1f;
                var point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                UiHitAreas.Shared.TryFindGui(point, out var area);
                Debug.Log("RTS zoom blocked by a panel: mouse=" + point + " area=" + area + " screen=" + Screen.width + "x" + Screen.height);
            }

            transform.position = focus - transform.forward * distance;
        }
    }
}
