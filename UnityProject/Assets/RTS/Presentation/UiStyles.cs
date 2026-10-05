using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Shared IMGUI styles and the height-based text scale for every presentation panel.</summary>
    public static class UiStyles
    {
        private static GUIStyle body;
        private static GUIStyle heading;
        private static GUIStyle button;
        private static GUIStyle panel;
        private static GUIStyle tiny;
        private static Texture2D panelTexture;
        private static Texture2D headerTexture;
        private static int lastHeight;

        public static float Scale { get { return Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.25f); } }
        public static GUIStyle Body { get { Ensure(); return body; } }
        public static GUIStyle Heading { get { Ensure(); return heading; } }
        public static GUIStyle Button { get { Ensure(); return button; } }
        public static GUIStyle Panel { get { Ensure(); return panel; } }
        public static GUIStyle Tiny { get { Ensure(); return tiny; } }
        // Panels place their first row about 22 px down, so the strip stays near that whatever the scale.
        /// <summary>One line of body text with its descenders, for panels that stack labels.</summary>
        public static float LineHeight { get { return Mathf.Ceil(Body.lineHeight) + 4f; } }
        public static float HeaderHeight { get { return Mathf.Max(20f, 22f * Scale); } }

        public static void Begin()
        {
            Ensure();
            // Existing drawing calls use GUI.skin overloads. Point them at the same shared styles.
            // Text fields and toggles keep their own skin (a frame to type in, a check box); only their size follows.
            GUI.skin.label = body;
            GUI.skin.button = button;
            GUI.skin.box = panel;
            GUI.skin.textField.fontSize = body.fontSize;
            GUI.skin.toggle.fontSize = body.fontSize;
        }

        public static void Box(Rect rect, string title)
        {
            GUI.Box(rect, GUIContent.none, Panel);
            var header = new Rect(rect.x, rect.y, rect.width, Mathf.Min(HeaderHeight, rect.height));
            GUI.DrawTexture(header, headerTexture, ScaleMode.StretchToFill, false);
            // The title fills the strip exactly (no padding, centred vertically), so its lower half is never cut off.
            GUI.Label(new Rect(rect.x + 7f, rect.y, rect.width - 14f, header.height), title, Heading);
        }

        /// <summary>A panel without a title strip, for bars whose own text sits on the top line.</summary>
        public static void Plain(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, Panel);
        }

        private static void Ensure()
        {
            int height = Screen.height;
            if (body != null && height == lastHeight) return;
            lastHeight = height;
            // Copies of the skin's styles: after the first Begin the skin holds these, so a resize copies the copies.
            body = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(16f * Scale),
                wordWrap = true,
                clipping = TextClipping.Clip
            };
            heading = new GUIStyle(body)
            {
                fontSize = Mathf.RoundToInt(15f * Scale),
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            button = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(15f * Scale),
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
            panel = new GUIStyle(GUI.skin.box)
            {
                fontSize = Mathf.RoundToInt(16f * Scale),
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            tiny = new GUIStyle(body) { fontSize = Mathf.RoundToInt(13f * Scale) };
            panelTexture = panelTexture ?? Solid(new Color(0.03f, 0.04f, 0.06f, 0.78f));
            headerTexture = headerTexture ?? Solid(new Color(0.12f, 0.18f, 0.27f, 0.92f));
            panel.normal.background = panelTexture;
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
