using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// UI hit rectangles are collected while drawing one frame and consumed by input on the next.
    /// This avoids depending on Unity's OnGUI callback order.
    /// </summary>
    public sealed class UiHitAreas
    {
        private readonly List<Rect> current = new List<Rect>();
        private readonly List<Rect> previous = new List<Rect>();
        private int frame = -1;
        private bool keyboardCaptured;

        public static UiHitAreas Shared { get; } = new UiHitAreas();

        public void BeginFrame(int frameNumber)
        {
            if (frame == frameNumber) return;
            previous.Clear();
            previous.AddRange(current);
            current.Clear();
            frame = frameNumber;
        }

        public void Register(Rect rect)
        {
            if (rect.width > 0f && rect.height > 0f) current.Add(rect);
        }

        public bool ContainsGui(Vector2 point)
        {
            for (int i = 0; i < previous.Count; i++)
                if (previous[i].Contains(point)) return true;
            return false;
        }

        /// <summary>The registered area that holds this GUI point, for diagnosing blocked input; false if none.</summary>
        public bool TryFindGui(Vector2 point, out Rect area)
        {
            for (int i = 0; i < previous.Count; i++)
                if (previous[i].Contains(point)) { area = previous[i]; return true; }
            area = default(Rect);
            return false;
        }

        public bool ContainsScreen(Vector2 screenPoint, float screenHeight)
        {
            return ContainsGui(new Vector2(screenPoint.x, screenHeight - screenPoint.y));
        }

        /// <summary>Text inputs use this to stop camera keyboard shortcuts while IME composition is active.</summary>
        public bool KeyboardCaptured { get { return keyboardCaptured; } }

        public void SetKeyboardCaptured(bool captured)
        {
            keyboardCaptured = captured;
        }

        public void Reset()
        {
            current.Clear();
            previous.Clear();
            frame = -1;
            keyboardCaptured = false;
        }
    }
}
