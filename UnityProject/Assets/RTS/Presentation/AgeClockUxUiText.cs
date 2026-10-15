using System;
using System.Globalization;

namespace Rts.Presentation
{
    /// <summary>Shared labels for the age-clock reservation, instant payment, and manual choice deadline.</summary>
    public static class AgeClockUxUiText
    {
        public static string Reservation(string civilisation, long clockTick, long currentTick)
        {
            long seconds = Math.Max(0, (clockTick - currentTick + 19) / 20);
            string time = (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
            return UiText.T(civilisation + " reserved (advances at " + time + ")", civilisation + "を予約（" + time + " に上がる）");
        }

        public static string AdvanceNow(int food, int wood, int gold = 0)
        {
            string price = UiText.T(" (food " + food + ", wood " + wood + (gold > 0 ? ", gold " + gold : "") + ")",
                "（食料" + food + "・木材" + wood + (gold > 0 ? "・金" + gold : "") + "）");
            return UiText.T("Advance now" + price, "今すぐ時代を上げる" + price);
        }

        public static string ChoiceDeadline(long deadlineTick, long currentTick)
        {
            long seconds = Math.Max(0, (deadlineTick - currentTick + 19) / 20);
            return UiText.T("Choose a civilisation (automatic choice in " + seconds + " seconds)",
                "文明を選んでください（あと" + seconds + "秒で自動で選びます）");
        }

        public static bool IsChoiceWindow(long clockTick, long deadlineTick, long currentTick)
            => clockTick > 0 && deadlineTick > clockTick && currentTick >= clockTick && currentTick < deadlineTick;
    }
}
