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
        public Rect Minimap;
        public Rect Setup;
        public Rect Result;

        public Rect[] InteractiveRects
        {
            get
            {
                return new[]
                {
                    TopLeft, TopCenter, TopRight, Supply, Commands, Economy,
                    Strategist, LogToggle, LogStatus, LogEntries, Timeline, Minimap, Setup, Result
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
            // The right column keeps the same margin as the left one, so its border is not cut by the screen edge.
            float rightX = screenWidth - rightWidth - pad;
            float centerWidth = Mathf.Max(300f, rightX - centerX - gap);
            float bottom = screenHeight - pad;
            float contentTop = pad + topHeight + gap;

            // 10-18 test play: with the patrol button the command grid has one more row and the last row was cut off.
            float commandHeight = Mathf.Clamp(screenHeight * 0.42f, 300f, 420f);
            float supplyHeight = 104f;
            // The buttons sit four to a row (UI Toolkit), so the panel needs far less height than one button per row did.
            float economyHeight = Mathf.Min(260f, Mathf.Max(200f, screenHeight - topHeight - 2f * pad));
            // 11-20 test play: the minimap was drawn over the timeline. The minimap now owns the bottom of the right
            // column, and the timeline (the list of match events) moved into the folded log area above it.
            // The staff panel holds the chat log, so it takes what the screen can spare (180 at 720 high, 380 from 900).
            float advisorHeight = Mathf.Clamp(screenHeight - 520f, 180f, 380f);
            float advisorY = contentTop;
            float logToggleHeight = 28f;
            float logToggleY = advisorY + advisorHeight + gap;
            float logAreaY = logToggleY + logToggleHeight + gap;
            // The log area keeps at least 120 px; the minimap gives way when the screen is short (900 high: 262 px).
            float minimapHeight = Mathf.Min(Mathf.Clamp(screenHeight * 0.30f, 200f, 330f),
                Mathf.Max(120f, bottom - logAreaY - gap - 120f));
            float minimapY = bottom - minimapHeight;
            float logAreaHeight = Mathf.Max(120f, minimapY - gap - logAreaY);
            // Status, command log and timeline share the log area (3 : 3.5 : 3.5).
            float statusHeight = Mathf.Max(30f, (logAreaHeight - 2f * gap) * 0.30f);
            float entriesHeight = Mathf.Max(30f, (logAreaHeight - 2f * gap) * 0.35f);
            float timelineY = logAreaY + statusHeight + gap + entriesHeight + gap;
            float timelineHeight = Mathf.Max(30f, logAreaY + logAreaHeight - timelineY);

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
                LogEntries = new Rect(rightX, logAreaY + statusHeight + gap, rightWidth, entriesHeight),
                Timeline = new Rect(rightX, timelineY, rightWidth, timelineHeight),
                Minimap = new Rect(rightX, minimapY, rightWidth, minimapHeight),
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
            layout.Minimap = Clamp(layout.Minimap, width, height);
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
