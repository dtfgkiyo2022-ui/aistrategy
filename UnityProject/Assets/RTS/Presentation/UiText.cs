namespace Rts.Presentation
{
    /// <summary>
    /// The on-screen language, English or Japanese, until real localisation (string tables) comes later. Every panel asks
    /// for a line in both languages at the place it draws it, so switching takes effect on the next frame. Plain C#: the
    /// host loads and saves the choice.
    /// </summary>
    public static class UiText
    {
        public static bool Japanese;

        public static string T(string english, string japanese) { return Japanese ? japanese : english; }

        public static string DropSiteLabel(int count, int max, int woodCost)
        {
            string countText = max > 0 ? count + "/" + max : "";
            return Japanese ? "集積所 " + countText + "（木" + woodCost + "）"
                : "Drop-off " + countText + " (" + woodCost + " wood)";
        }

        public static string StorageDescription()
            => T("Villagers can unload here; the exit belt sends it to the core or, when enabled, to a processing building",
                "村人が荷を下ろせる（出口からベルトで送る。旗が有効なら加工の建物へも送る）");

        public static string CraftTree4ProductDescription()
            => T("Age 4: Siege engine = 2 siege parts + 1 mechanism; Elite kit = 2 arms kits + 1 mechanism + 5 metal.",
                "時代4：攻城機関は攻城部品2＋精密部品1。精鋭キットは武具キット2＋精密部品1＋金属5。");

        public static string CannonDescription()
            => T("Cannon: SiegeWorkshop unit; consumes 1 siege engine, 100 food and 100 wood. Range 8; deals double ram building damage.",
                "大砲：攻城工房で生産。攻城機関1・食料100・木材100を使う。射程8、建物には破城槌の2倍の攻撃。");

        public static string PrimitiveCraftHutDescription()
            => T("Hut: primitive workbench. Stone tools use 1 stone + 1 wood; jerky uses 3 food and improves soldier HP.",
                "作業小屋：原始時代の作業台。石器は石1＋木1、干し肉は食料3で作り、兵のHPを高める。");

        public static string PrimitiveCraftGoodsLabel()
            => T("Stone tools / Jerky", "石器／干し肉");
    }
}
