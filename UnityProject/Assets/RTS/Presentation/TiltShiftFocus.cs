using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Rts.Presentation
{
    /// <summary>
    /// Ultra (11-09): keeps the depth-of-field focus on the ground the camera looks at, so only the far part of the
    /// view (the top of the screen) softens, whatever the camera height. Display only.
    /// </summary>
    public sealed class TiltShiftFocus : MonoBehaviour
    {
        private DepthOfField focus;

        public void Bind(DepthOfField depthOfField)
        {
            focus = depthOfField;
            enabled = focus != null;
        }

        private void LateUpdate()
        {
            if (focus == null) return;
            var camera = Camera.main;
            if (camera == null) return;
            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float distance = ray.direction.y < -0.01f ? -ray.origin.y / ray.direction.y : 150f;
            // Sharp up to a little beyond the middle of the screen, soft by about twice that distance.
            focus.gaussianStart.value = distance * 1.15f;
            focus.gaussianEnd.value = distance * 2.4f;
        }
    }
}
