using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// All presentation rectangles live here.  These are screen-pixel rectangles in GUI coordinates
    /// (origin at the top-left), so the calculation is independent of drawing and easy to test.
    /// </summary>
    public struct UiLayoutRects
    {
        public float Scale;
        public Rect TopLeft;
        public Rect TopCenter;
        public Rect TopRight;
        public Rect Supply;
        public Rect Commands;
        public Rect Economy;
        public Rect Strategist;
        public Rect LogToggle;
        public Rect LogStatus;
        public Rect LogEntries;
        public Rect Timeline;
        public Rect Setup;
        public Rect Result;

        public Rect[] InteractiveRects
        {
            get
            {
                return new[]
                {
                    TopLeft, TopCenter, TopRight, Supply, Commands, Economy,
                    Strategist, LogToggle, LogStatus, LogEntries, Timeline, Setup, Result
                };
            }
        }
    }

    public static class UiLayout
    {
        public static UiLayoutRects Calculate(float screenWidth, float screenHeight)
        {
            float scale = Mathf.Clamp(screenHeight / 1080f, 0.8f, 1.25f);
            float pad = 10f;
            float gap = 8f;
            float topHeight = 58f;
            float leftWidth = 250f;
            float rightWidth = 420f;

            float centerX = leftWidth + gap;
            float rightX = screenWidth - rightWidth;
            float centerWidth = Mathf.Max(300f, rightX - centerX - gap);
            float bottom = screenHeight - pad;
            float contentTop = pad + topHeight + gap;

            float commandHeight = 300f;
            float supplyHeight = 104f;
            float economyHeight = Mathf.Min(358f, Mathf.Max(250f, screenHeight - topHeight - 2f * pad));
            float timelineHeight = Mathf.Clamp(screenHeight - 520f, 150f, 250f);
            float timelineY = bottom - timelineHeight;

            // The staff panel holds the chat log, so it takes what the screen can spare (180 at 720 high, 380 from 900).
            float advisorHeight = Mathf.Clamp(screenHeight - 520f, 180f, 380f);
            float advisorY = contentTop;
            float logToggleHeight = 28f;
            float logToggleY = advisorY + advisorHeight + gap;
            float logAreaY = logToggleY + logToggleHeight + gap;
            float logAreaHeight = Mathf.Max(82f, timelineY - gap - logAreaY);
            float statusHeight = Mathf.Max(38f, (logAreaHeight - gap) * 0.46f);

            var layout = new UiLayoutRects
            {
                Scale = scale,
                TopLeft = new Rect(pad, pad, screenWidth * 0.42f - pad, topHeight),
                TopCenter = new Rect(screenWidth * 0.42f, pad, screenWidth * 0.28f, topHeight),
                TopRight = new Rect(screenWidth * 0.70f, pad, screenWidth * 0.30f - pad, topHeight),
                Supply = new Rect(pad, contentTop, leftWidth - pad, supplyHeight),
                Commands = new Rect(pad, bottom - commandHeight, leftWidth - pad, commandHeight),
                Economy = new Rect(centerX, bottom - economyHeight, centerWidth, economyHeight),
                Strategist = new Rect(rightX, advisorY, rightWidth, advisorHeight),
                LogToggle = new Rect(rightX, logToggleY, rightWidth, logToggleHeight),
                LogStatus = new Rect(rightX, logAreaY, rightWidth, statusHeight),
                LogEntries = new Rect(rightX, logAreaY + statusHeight + gap, rightWidth,
                    Mathf.Max(38f, logAreaHeight - statusHeight - gap)),
                Timeline = new Rect(rightX, timelineY, rightWidth, timelineHeight),
                // The setup rows scroll inside this box (CommandPanel.DrawSetup), so it keeps clear of the economy panel.
                Setup = new Rect(centerX, contentTop, centerWidth,
                    Mathf.Max(250f, bottom - economyHeight - contentTop - gap)),
                Result = new Rect(centerX + centerWidth / 2f - 190f, contentTop, 380f, 160f)
            };

            return ClampToScreen(layout, screenWidth, screenHeight);
        }

        private static UiLayoutRects ClampToScreen(UiLayoutRects layout, float width, float height)
        {
            layout.TopLeft = Clamp(layout.TopLeft, width, height);
            layout.TopCenter = Clamp(layout.TopCenter, width, height);
            layout.TopRight = Clamp(layout.TopRight, width, height);
            layout.Supply = Clamp(layout.Supply, width, height);
            layout.Commands = Clamp(layout.Commands, width, height);
            layout.Economy = Clamp(layout.Economy, width, height);
            layout.Strategist = Clamp(layout.Strategist, width, height);
            layout.LogToggle = Clamp(layout.LogToggle, width, height);
            layout.LogStatus = Clamp(layout.LogStatus, width, height);
            layout.LogEntries = Clamp(layout.LogEntries, width, height);
            layout.Timeline = Clamp(layout.Timeline, width, height);
            layout.Setup = Clamp(layout.Setup, width, height);
            layout.Result = Clamp(layout.Result, width, height);
            return layout;
        }

        private static Rect Clamp(Rect rect, float width, float height)
        {
            float w = Mathf.Min(rect.width, width);
            float h = Mathf.Min(rect.height, height);
            float x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, width - w));
            float y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, height - h));
            return new Rect(x, y, w, h);
        }
    }
}
