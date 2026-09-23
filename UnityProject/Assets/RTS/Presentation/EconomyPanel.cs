using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>What the map row of the match setup may ask of the host. Changing either restarts the match.</summary>
    public interface IMapChoice
    {
        /// <summary>True: a random map with the economy. False: the Ver.1 two-road map.</summary>
        bool EconomyMap { get; set; }
        ulong Seed { get; }
        /// <summary>A fresh random map (new seed), from tick 0.</summary>
        void NewMap();
    }

    /// <summary>
    /// Ver.3 economy panel (V3-1 PR5, V3-2 PR4), bottom centre. Shows the stock and population and sends direct economy
    /// operations through IEconomyPort only; it never changes the simulation itself. A building takes the next ground
    /// click (the footprint is centred on the clicked cell, R turns the output side); belts are drawn by dragging on the
    /// ground, one bend at most. The simulation decides whether any of it is legal.
    /// </summary>
    public sealed class EconomyPanel : MonoBehaviour
    {
        // Between the command buttons on the left (238 px) and the log/timeline column on the right (440 px).
        private const float LeftColumn = 246f, RightColumn = 440f, MaxWidth = 460f, MinWidth = 300f;
        private const int CellMeters = 2, MapWidthCells = 128, MapHeightCells = 64;

        private enum Mode { None, Barracks, Mine, Smelter, Farm, House, DropSite, Tower, Wall, Blacksmith, Market, SiegeWorkshop, ArcheryRange, Stable, Belt, RemoveBelt }

        private IEconomyPort port;
        private BattlefieldView view;
        private EconomyLayer layer;
        private uint faction;
        private ulong sequence;
        private Mode mode;
        private Facing facing = Facing.East;
        private int dragStart = -1;
        private readonly List<string> notes = new List<string>();

        public bool IsPlacing { get { return mode != Mode.None; } }

        public void Bind(IEconomyPort economyPort, uint factionId, BattlefieldView battlefield, EconomyLayer economyLayer)
        {
            port = economyPort;
            faction = factionId;
            view = battlefield;
            layer = economyLayer;
            SetMode(Mode.None);
        }

        // The panel keeps one height: a row of tabs and at most seven rows of buttons under it, then one line of notes.
        // Seven since V3-5 (32 #9): the make tab can show the ram and the market's trades as well.
        private const float TabbedHeight = 22f + 26f + 7f * 26f + 24f;

        private enum Tab { Build, Make, Research, Policy }
        private Tab tab = Tab.Build;

        private Rect PanelRect()
        {
            float room = Screen.width - LeftColumn - RightColumn;
            float width = Mathf.Clamp(room - 8f, MinWidth, MaxWidth);
            float x = room >= MinWidth ? LeftColumn + (room - width) / 2f : Screen.width / 2f - width / 2f;
            return new Rect(x, Screen.height - 10f - TabbedHeight, width, TabbedHeight);
        }

        /// <summary>
        /// The bar across the top between the setup buttons (left, 354 px) and the log column (right, 430 px): the age and
        /// the stock are always in view there, so the panel below only holds buttons.
        /// </summary>
        private Rect TopBarRect()
        {
            float left = 364f, right = Screen.width - 438f;
            return new Rect(left, 8f, Mathf.Max(200f, right - left), 26f);
        }

        public bool BlocksClick(Vector2 screenPoint)
        {
            if (port == null) return false;
            var point = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return PanelRect().Contains(point) || (Economy() != null && TopBarRect().Contains(point));
        }

        private void SetMode(Mode next)
        {
            mode = next;
            dragStart = -1;
            if (layer == null) return;
            layer.ShowGhost(false, Vector3.zero, 0f, false);
            layer.ShowBeltPreview(null, false);
        }

        /// <summary>A ground click while placing becomes a request. Returns true when the click was used.</summary>
        public bool TryConsumeGroundClick(Camera camera, Vector2 screenPoint)
        {
            if (mode == Mode.None || port == null) return false;
            if (!GroundCell(camera, screenPoint, out int cell)) { Note(UiText.T("Click was outside the map.", "マップの外をクリックしました。")); return true; }
            switch (mode)
            {
                case Mode.Belt:
                case Mode.Wall:
                    dragStart = cell; // the run is sent when the button comes up (Update)
                    return true;
                case Mode.RemoveBelt:
                    port.SubmitEconomy(EconomyCommand.RemoveBelt(faction, ++sequence, cell));
                    Note(UiText.T("Belt removal requested.", "ベルトを外すよう依頼しました。"));
                    return true;
            }
            var kind = KindOf(mode);
            if (!Footprint(cell, SizeOf(kind), out int origin, out _)) { Note(UiText.T("Too close to the edge of the map.", "マップの端に近すぎます。")); return true; }
            port.SubmitEconomy(EconomyCommand.Place(faction, ++sequence, kind, origin, facing));
            Note(Name(kind) + UiText.T(" requested at cell ", "を依頼しました：セル ") + origin + UiText.T(" (the simulation checks the ground).", "（置けるかはシミュレーションが判断します）"));
            SetMode(Mode.None);
            return true;
        }

        private bool GroundCell(Camera camera, Vector2 screenPoint, out int cell)
        {
            cell = -1;
            var ray = camera.ScreenPointToRay(screenPoint);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter)) return false;
            var hit = ray.GetPoint(enter);
            int cx = Mathf.FloorToInt(hit.x / CellMeters), cz = Mathf.FloorToInt(hit.z / CellMeters);
            if (cx < 0 || cz < 0 || cx >= MapWidthCells || cz >= MapHeightCells) return false;
            cell = cz * MapWidthCells + cx;
            return true;
        }

        private static bool Footprint(int cell, int size, out int origin, out Vector3 center)
        {
            int x0 = cell % MapWidthCells - size / 2, z0 = cell / MapWidthCells - size / 2;
            origin = -1; center = Vector3.zero;
            if (x0 < 0 || z0 < 0 || x0 + size > MapWidthCells || z0 + size > MapHeightCells) return false;
            origin = z0 * MapWidthCells + x0;
            center = new Vector3((x0 + size / 2f) * CellMeters, 0f, (z0 + size / 2f) * CellMeters);
            return true;
        }

        private int SizeOf(BuildingKind kind)
        {
            var e = Economy();
            if (e == null) return 3;
            return kind == BuildingKind.Mine ? e.MineSizeCells : kind == BuildingKind.Smelter ? e.SmelterSizeCells : kind == BuildingKind.Farm ? e.FarmSizeCells
                : kind == BuildingKind.House || kind == BuildingKind.DropSite || kind == BuildingKind.Tower ? 2 : e.BuildingSizeCells;
        }

        private int WoodOf(BuildingKind kind)
        {
            var e = Economy();
            if (e == null) return 0;
            return kind == BuildingKind.Mine ? e.MineWoodCost : kind == BuildingKind.Smelter ? e.SmelterWoodCost : kind == BuildingKind.Farm ? e.FarmWoodCost
                : kind == BuildingKind.House ? e.HouseWoodCost : kind == BuildingKind.DropSite ? e.DropSiteWoodCost
                : kind == BuildingKind.Blacksmith ? e.BlacksmithWoodCost
                : kind == BuildingKind.Market ? e.MarketWoodCost : kind == BuildingKind.SiegeWorkshop ? e.WorkshopWoodCost
                : kind == BuildingKind.ArcheryRange ? e.RangeWoodCost : kind == BuildingKind.Stable ? e.StableWoodCost : e.BarracksWoodCost;
        }

        private static string Name(BuildingKind kind)
            => kind == BuildingKind.Mine ? UiText.T("Mine", "採掘場") : kind == BuildingKind.Smelter ? UiText.T("Smelter", "精錬所")
                : kind == BuildingKind.Farm ? UiText.T("Farm", "農場") : kind == BuildingKind.House ? UiText.T("House", "住居")
                : kind == BuildingKind.DropSite ? UiText.T("Drop-off", "資源置き場") : kind == BuildingKind.Tower ? UiText.T("Tower", "見張り塔")
                : kind == BuildingKind.Blacksmith ? UiText.T("Blacksmith", "鍛冶場")
                : kind == BuildingKind.Market ? UiText.T("Market", "市場") : kind == BuildingKind.SiegeWorkshop ? UiText.T("Siege workshop", "攻城工房")
                : kind == BuildingKind.ArcheryRange ? UiText.T("Archery range", "射撃場") : kind == BuildingKind.Stable ? UiText.T("Stable", "厩舎")
                : UiText.T("Barracks", "兵舎");

        private static BuildingKind KindOf(Mode m)
            => m == Mode.Mine ? BuildingKind.Mine : m == Mode.Smelter ? BuildingKind.Smelter : m == Mode.Farm ? BuildingKind.Farm
                : m == Mode.House ? BuildingKind.House : m == Mode.DropSite ? BuildingKind.DropSite : m == Mode.Tower ? BuildingKind.Tower
                : m == Mode.Blacksmith ? BuildingKind.Blacksmith
                : m == Mode.Market ? BuildingKind.Market : m == Mode.SiegeWorkshop ? BuildingKind.SiegeWorkshop
                : m == Mode.ArcheryRange ? BuildingKind.ArcheryRange : m == Mode.Stable ? BuildingKind.Stable : BuildingKind.Barracks;

        private static string CivName(CivKind c)
            => c == CivKind.Agrarian ? UiText.T("farming", "農耕の文明") : c == CivKind.Metallurgy ? UiText.T("metallurgy", "冶金の文明") : UiText.T("primitive age", "原始時代");

        /// <summary>V3-5: the civilisation with its age - the second age is the city age for farming, the iron age for metallurgy.</summary>
        private static string AgeName(CivKind c, int age)
        {
            if (age < 2) return CivName(c);
            if (c == CivKind.Agrarian)
                return age == 2 ? UiText.T("farming, city age", "農耕の文明・都市の時代") : UiText.T("farming, trade age", "農耕の文明・交易の時代");
            return age == 2 ? UiText.T("metallurgy, iron age", "冶金の文明・鉄の時代") : UiText.T("metallurgy, steel age", "冶金の文明・鋼の時代");
        }

        private static string FacingName(Facing f)
            => f == Facing.North ? UiText.T("north", "北") : f == Facing.East ? UiText.T("east", "東") : f == Facing.South ? UiText.T("south", "南") : UiText.T("west", "西");

        /// <summary>From <paramref name="from"/> along x first, then along z; each belt faces the next, the last keeps going.</summary>
        private List<int> BeltRun(int from, int to, List<Facing> facings)
        {
            var cells = new List<int>();
            facings.Clear();
            int x = from % MapWidthCells, z = from / MapWidthCells, tx = to % MapWidthCells, tz = to / MapWidthCells;
            cells.Add(from);
            while ((x != tx || z != tz) && cells.Count < EconomyCommand.MaxBeltRun)
            {
                Facing step;
                if (x != tx) { step = tx > x ? Facing.East : Facing.West; x += tx > x ? 1 : -1; }
                else { step = tz > z ? Facing.North : Facing.South; z += tz > z ? 1 : -1; }
                facings.Add(step);
                cells.Add(z * MapWidthCells + x);
            }
            facings.Add(facings.Count == 0 ? facing : facings[facings.Count - 1]);
            return cells;
        }

        private void Update()
        {
            if (mode == Mode.None || layer == null) return;
            var camera = Camera.main;
            if (camera == null) return;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { SetMode(Mode.None); return; }
            if (Input.GetKeyDown(KeyCode.R)) facing = (Facing)(((int)facing + 1) % 4);
            var economy = Economy();
            if (economy == null) return;
            bool onMap = GroundCell(camera, Input.mousePosition, out int cell);
            if (mode == Mode.Belt || mode == Mode.Wall)
            {
                bool wall = mode == Mode.Wall;
                var facings = new List<Facing>();
                var run = dragStart >= 0 && onMap ? BeltRun(dragStart, cell, facings) : onMap ? new List<int> { cell } : null;
                bool affordable = run != null && (wall ? economy.Stone >= run.Count * economy.WallStoneCost : economy.Wood >= run.Count * economy.BeltWoodCost);
                layer.ShowBeltPreview(run, affordable);
                if (dragStart >= 0 && Input.GetMouseButtonUp(0))
                {
                    if (onMap)
                    {
                        run = BeltRun(dragStart, cell, facings);
                        if (wall)
                        {
                            port.SubmitEconomy(EconomyCommand.PlaceWall(faction, ++sequence, run));
                            Note(run.Count + UiText.T(" wall cell(s) requested.", " マスの壁を依頼しました。"));
                        }
                        else
                        {
                            port.SubmitEconomy(EconomyCommand.PlaceBelt(faction, ++sequence, run, facings));
                            Note(run.Count + UiText.T(" belt cell(s) requested.", " マスのベルトを依頼しました。"));
                        }
                    }
                    dragStart = -1;
                }
                return;
            }
            if (mode == Mode.RemoveBelt) { layer.ShowBeltPreview(onMap ? new List<int> { cell } : null, false); return; }
            var kind = KindOf(mode);
            int size = SizeOf(kind);
            var center = Vector3.zero;
            bool shown = onMap && Footprint(cell, size, out _, out center);
            layer.ShowGhost(shown, center, size * CellMeters, economy.Wood >= WoodOf(kind));
        }

        private EconomyView Economy() { var frame = view == null ? null : view.LatestFrame; return frame == null ? null : frame.Economy; }

        private void OnGUI()
        {
            if (port == null) return;
            var economy = Economy();
            if (economy != null) DrawTopBar(economy);
            var rect = PanelRect();
            GUI.Box(rect, economy == null ? UiText.T("Economy (off on this map)", "内政（このマップでは無し）") : UiText.T("Economy", "内政"));
            float x = rect.x + 8f, y = rect.y + 22f, w = rect.width - 16f, half = (w - 8f) / 2f, right = x + half + 8f;
            if (economy == null) { DrawNotes(new Rect(x, y, w, rect.yMax - y - 4f)); return; }

            // Tabs: build (ground work), make (people and soldiers), policy (how the automatic economy runs).
            float quarter = (w - 12f) / 4f;
            TabButton(new Rect(x, y, quarter, 22f), Tab.Build, UiText.T("Build", "建てる"));
            TabButton(new Rect(x + quarter + 4f, y, quarter, 22f), Tab.Make, UiText.T("Make", "作る"));
            TabButton(new Rect(x + 2f * (quarter + 4f), y, quarter, 22f), Tab.Research, UiText.T("Research", "研究"));
            TabButton(new Rect(x + 3f * (quarter + 4f), y, quarter, 22f), Tab.Policy, UiText.T("Policy", "方針"));
            y += 26f;
            if (tab == Tab.Build) DrawBuild(economy, x, y, w, half, right);
            else if (tab == Tab.Make) DrawMake(economy, x, y, w, half, right);
            else if (tab == Tab.Research) DrawResearch(economy, x, y, w, half, right);
            else DrawPolicy(economy, x, y, w, half, right);

            float notesY = rect.yMax - 24f;
            string hint = mode == Mode.None ? null : mode == Mode.Wall ? UiText.T("Drag near your base; a wall never shuts the way to the enemy.", "自陣の近くをドラッグ。敵への道を完全には塞げない")
                : mode == Mode.Belt ? UiText.T("Belts carry toward the stripe. R sets a single cell's direction.", "ベルトは白い線の向きに運ぶ。1マスだけなら R で向き")
                : mode == Mode.RemoveBelt ? UiText.T("Click a belt of yours.", "外す自分のベルトをクリック")
                : UiText.T("Click the ground (Esc cancels). R turns the output: ", "地面をクリック（Escで取消）。R で出口の向き：") + FacingName(facing);
            if (hint != null) GUI.Label(new Rect(x, notesY, w, 22f), hint);
            else DrawNotes(new Rect(x, notesY, w, 22f));
        }

        /// <summary>Age, stock and population, always in view.</summary>
        private void DrawTopBar(EconomyView economy)
        {
            var bar = TopBarRect();
            GUI.Box(bar, "");
            string age = !economy.Ages ? "" : economy.AdvanceRemaining > 0
                ? UiText.T("Advancing to ", "進めている：") + AgeName(economy.AdvancingTo, economy.Civ == CivKind.Primitive ? 1 : 2) + " " + Seconds(economy.AdvanceRemaining) + "  |  "
                : AgeName(economy.Civ, economy.Age) + "  |  ";
            string stock = UiText.T("Food ", "食料 ") + economy.Food + UiText.T("  Wood ", "  木材 ") + economy.Wood;
            if (economy.Industry) stock += UiText.T("  Ore ", "  鉱石 ") + economy.Ore + UiText.T("  Metal ", "  金属 ") + economy.Metal;
            if (economy.Ages) stock += UiText.T("  Stone ", "  石 ") + economy.Stone;
            string people = UiText.T("  |  Pop ", "  |  人口 ") + economy.Population + "/" + economy.PopulationCap + UiText.T("  Idle ", "  待機 ") + CountIdle(economy);
            GUI.Label(new Rect(bar.x + 8f, bar.y + 3f, bar.width - 16f, 22f), age + stock + people);
        }

        private void TabButton(Rect r, Tab target, string label)
        {
            if (GUI.Toggle(r, tab == target, label, GUI.skin.button) && tab != target) { tab = target; SetMode(Mode.None); }
        }

        /// <summary>Barracks, the civilisation's buildings, and belts.</summary>
        private void DrawBuild(EconomyView economy, float x, float y, float w, float half, float right)
        {
            ModeButton(new Rect(x, y, half, 22f), Mode.Barracks, UiText.T("Barracks (", "兵舎（木材 ") + economy.BarracksWoodCost + UiText.T(" wood)", "）"));
            if (economy.Ages)
                ModeButton(new Rect(right, y, half, 22f), Mode.House, UiText.T("House +pop (", "住居・人口+（木材 ") + economy.HouseWoodCost + UiText.T(" wood)", "）"));
            else
            {
                var barracks = OwnBuilding(economy, BuildingKind.Barracks);
                if (barracks.HasValue && !barracks.Value.Complete)
                    GUI.Label(new Rect(right, y, half, 22f), UiText.T("Barracks: building ", "兵舎：建設中 ") + Percent(barracks.Value));
            }
            y += 26f;
            if (economy.Ages)
            {
                float third = (w - 8f) / 3f;
                ModeButton(new Rect(x, y, third, 22f), Mode.DropSite, UiText.T("Drop-off (", "資源置き場（木") + economy.DropSiteWoodCost + UiText.T("W)", "）"));
                ModeButton(new Rect(x + third + 4f, y, third, 22f), Mode.Tower, UiText.T("Tower (", "見張り塔（石") + economy.TowerStoneCost + UiText.T("S ", " 木") + economy.TowerWoodCost + UiText.T("W)", "）"));
                ModeButton(new Rect(x + 2f * (third + 4f), y, third, 22f), Mode.Wall, mode == Mode.Wall ? UiText.T("Drag the wall", "壁をドラッグ")
                    : UiText.T("Wall (", "壁（石") + economy.WallStoneCost + UiText.T("S/cell)", "／マス）"));
                y += 26f;
                // V3-5 (32 #9): the market comes with a civilisation, the siege workshop with the second age.
                if (economy.Civ != CivKind.Primitive)
                    ModeButton(new Rect(x, y, half, 22f), Mode.Market, UiText.T("Market (", "市場（木材 ") + economy.MarketWoodCost + UiText.T(" wood)", "）"));
                if (economy.Age >= 2)
                    ModeButton(new Rect(right, y, half, 22f), Mode.SiegeWorkshop, UiText.T("Siege workshop (", "攻城工房（木材 ") + economy.WorkshopWoodCost + UiText.T(" wood)", "）"));
                if (economy.Civ != CivKind.Primitive || economy.Age >= 2) y += 26f;
                // V3-5 (32 #12): the range and the stable, so either civilisation can field the unit it does not train.
                if (economy.Age >= 2)
                {
                    ModeButton(new Rect(x, y, half, 22f), Mode.ArcheryRange, UiText.T("Archery range (", "射撃場（木材 ") + economy.RangeWoodCost + UiText.T(" wood)", "）"));
                    ModeButton(new Rect(right, y, half, 22f), Mode.Stable, UiText.T("Stable (", "厩舎（木材 ") + economy.StableWoodCost + UiText.T(" wood)", "）"));
                    y += 26f;
                    // V3-5 (32 #13): the triangle, said once where the two buildings are chosen.
                    GUI.Label(new Rect(x, y, w, 22f), UiText.T("Archers beat infantry, cavalry beats archers, infantry beats cavalry",
                        "相性：弓兵は歩兵に強く、騎兵は弓兵に強く、歩兵は騎兵に強い"));
                    y += 26f;
                }
            }
            if (!economy.Industry) return;
            // Mines and smelters belong to metallurgy, farms to farming (V3-4); a map without ages has mines only.
            if (!economy.Ages || economy.Civ == CivKind.Metallurgy)
            {
                ModeButton(new Rect(x, y, half, 22f), Mode.Mine, UiText.T("Mine (", "採掘場（木材 ") + economy.MineWoodCost + UiText.T(" wood)", "）"));
                ModeButton(new Rect(right, y, half, 22f), Mode.Smelter, UiText.T("Smelter (", "精錬所（木材 ") + economy.SmelterWoodCost + UiText.T(" wood)", "）"));
            }
            else if (economy.Civ == CivKind.Agrarian)
            {
                ModeButton(new Rect(x, y, half, 22f), Mode.Farm, UiText.T("Farm (", "農場（木材 ") + economy.FarmWoodCost + UiText.T(" wood)", "）"));
                GUI.Label(new Rect(right, y, half, 22f), UiText.T("Faster by food and river", "食料の点と川の近くほど速い"));
            }
            else GUI.Label(new Rect(x, y, w, 22f), UiText.T("Mines and farms come with a civilisation", "採掘場・農場は文明に進んでから"));
            y += 26f;
            ModeButton(new Rect(x, y, half, 22f), Mode.Belt, mode == Mode.Belt ? UiText.T("Drag on the ground", "地面をドラッグ")
                : UiText.T("Belt (", "ベルト（木材 ") + economy.BeltWoodCost + UiText.T("/cell)", "／マス）"));
            ModeButton(new Rect(right, y, half - 70f, 22f), Mode.RemoveBelt, UiText.T("Remove", "ベルトを外す"));
            GUI.Label(new Rect(right + half - 66f, y, 66f, 22f), UiText.T("R: ", "R：") + FacingName(facing));
        }

        /// <summary>Villagers, infantry, advancing the age, and sending idle villagers to work.</summary>
        private void DrawMake(EconomyView economy, float x, float y, float w, float half, float right)
        {
            string queue = economy.VillagerQueued == 0 ? "" : " [" + economy.VillagerQueued + ", " + Seconds(economy.VillagerTrainRemaining) + "]";
            if (GUI.Button(new Rect(x, y, half, 22f), UiText.T("Villager (", "村人（食料 ") + economy.VillagerFoodCost + UiText.T(" food)", "）") + queue))
                Send(EconomyCommand.Train(faction, ++sequence, 0, UnitKind.Villager), UiText.T("Villager requested", "村人を依頼しました"));
            var barracks = OwnBuilding(economy, BuildingKind.Barracks);
            if (barracks.HasValue && barracks.Value.Complete)
            {
                var b = barracks.Value;
                string trains = b.Queued == 0 ? "" : " [" + b.Queued + ", " + Seconds(b.TrainRemaining) + "]";
                string cost = UiText.T("Infantry (", "歩兵（食") + economy.InfantryFoodCost + UiText.T("F ", " 木") + economy.InfantryWoodCost
                    + (economy.InfantryMetalCost > 0 ? UiText.T("W ", " 金") + economy.InfantryMetalCost + UiText.T("M)", "）") : UiText.T("W)", "）"));
                if (GUI.Button(new Rect(right, y, half - 56f, 22f), cost + trains))
                    Send(EconomyCommand.Train(faction, ++sequence, b.Id, UnitKind.Infantry), UiText.T("Infantry requested", "歩兵を依頼しました"));
                if (GUI.Button(new Rect(right + half - 52f, y, 52f, 22f), UiText.T("Undo", "取消"))) Send(EconomyCommand.CancelTrain(faction, ++sequence, b.Id), UiText.T("Last infantry cancelled", "最後の歩兵を取り消しました"));
            }
            else GUI.Label(new Rect(right, y, half, 22f), UiText.T("Infantry: build a barracks", "歩兵：兵舎を建てると作れる"));
            y += 26f;
            if (economy.Ages)
            {
                // V3-5: scouts from the barracks.
                GUI.enabled = barracks.HasValue && barracks.Value.Complete;
                if (GUI.Button(new Rect(x, y, half, 22f), UiText.T("Scout (", "斥候（食料 ") + economy.ScoutFoodCost + UiText.T(" food)", "）")) && barracks.HasValue)
                    Send(EconomyCommand.Train(faction, ++sequence, barracks.Value.Id, UnitKind.Scout), UiText.T("Scout requested", "斥候を依頼しました"));
                // V3-5 (32 #8): the civilisation's own unit, from its second age.
                if (economy.Age >= 2 && economy.Civ == CivKind.Agrarian)
                {
                    if (GUI.Button(new Rect(right, y, half, 22f), UiText.T("Archer (", "弓兵（食") + economy.ArcherFoodCost + UiText.T("F ", " 木") + economy.ArcherWoodCost + UiText.T("W)", "）")) && barracks.HasValue)
                        Send(EconomyCommand.Train(faction, ++sequence, barracks.Value.Id, UnitKind.Archer), UiText.T("Archer requested", "弓兵を依頼しました"));
                }
                else if (economy.Age >= 2 && economy.Civ == CivKind.Metallurgy)
                {
                    if (GUI.Button(new Rect(right, y, half, 22f), UiText.T("Cavalry (", "騎兵（食") + economy.CavalryFoodCost + UiText.T("F ", " 木") + economy.CavalryWoodCost
                        + UiText.T("W ", " 金") + economy.CavalryMetalCost + UiText.T("M)", "）")) && barracks.HasValue)
                        Send(EconomyCommand.Train(faction, ++sequence, barracks.Value.Id, UnitKind.Cavalry), UiText.T("Cavalry requested", "騎兵を依頼しました"));
                }
                GUI.enabled = true;
                y += 26f;
                // V3-5 (32 #9): the ram from the siege workshop, and the market's trades.
                var workshop = OwnBuilding(economy, BuildingKind.SiegeWorkshop);
                if (workshop.HasValue)
                {
                    string queued = workshop.Value.Queued == 0 ? "" : " [" + workshop.Value.Queued + ", " + Seconds(workshop.Value.TrainRemaining) + "]";
                    GUI.enabled = workshop.Value.Complete;
                    if (GUI.Button(new Rect(x, y, w, 22f), UiText.T("Ram (", "破城槌（食") + economy.RamFoodCost + UiText.T("F ", " 木") + economy.RamWoodCost + UiText.T("W)", "）") + queued))
                        Send(EconomyCommand.Train(faction, ++sequence, workshop.Value.Id, UnitKind.Ram), UiText.T("Ram requested", "破城槌を依頼しました"));
                    GUI.enabled = true;
                    y += 26f;
                }
                var range = OwnBuilding(economy, BuildingKind.ArcheryRange);
                var stable = OwnBuilding(economy, BuildingKind.Stable);
                if (range.HasValue || stable.HasValue)
                {
                    if (range.HasValue)
                    {
                        GUI.enabled = range.Value.Complete;
                        string queued = range.Value.Queued == 0 ? "" : " [" + range.Value.Queued + "]";
                        if (GUI.Button(new Rect(x, y, half, 22f), UiText.T("Archer (", "弓兵（食") + economy.ArcherFoodCost + UiText.T("F ", " 木") + economy.ArcherWoodCost + UiText.T("W)", "）") + queued))
                            Send(EconomyCommand.Train(faction, ++sequence, range.Value.Id, UnitKind.Archer), UiText.T("Archer requested", "弓兵を依頼しました"));
                    }
                    if (stable.HasValue)
                    {
                        GUI.enabled = stable.Value.Complete;
                        string queued = stable.Value.Queued == 0 ? "" : " [" + stable.Value.Queued + "]";
                        if (GUI.Button(new Rect(right, y, half, 22f), UiText.T("Cavalry (", "騎兵（食") + economy.CavalryFoodCost + UiText.T("F ", " 木") + economy.CavalryWoodCost
                            + UiText.T("W ", " 金") + economy.CavalryMetalCost + UiText.T("M)", "）") + queued))
                            Send(EconomyCommand.Train(faction, ++sequence, stable.Value.Id, UnitKind.Cavalry), UiText.T("Cavalry requested", "騎兵を依頼しました"));
                    }
                    GUI.enabled = true;
                    y += 26f;
                }
                var market = OwnBuilding(economy, BuildingKind.Market);
                if (market.HasValue)
                {
                    GUI.enabled = market.Value.Complete;
                    GUI.Label(new Rect(x, y, 96f, 22f), UiText.T("Market ", "市場 ") + economy.TradeLot + UiText.T("->", "→") + economy.TradeReturn);
                    float third = (w - 100f - 8f) / 3f;
                    TradeButton(new Rect(x + 100f, y, third, 22f), economy, ResourceKind.Food, ResourceKind.Wood, UiText.T("Food->Wood", "食料→木材"));
                    TradeButton(new Rect(x + 100f + third + 4f, y, third, 22f), economy, ResourceKind.Wood, ResourceKind.Food, UiText.T("Wood->Food", "木材→食料"));
                    TradeButton(new Rect(x + 100f + 2f * (third + 4f), y, third, 22f), economy, ResourceKind.Wood, ResourceKind.Stone, UiText.T("Wood->Stone", "木材→石"));
                    GUI.enabled = true;
                    y += 26f;
                }
            }
            if (economy.Ages && economy.Civ == CivKind.Primitive && economy.AdvanceRemaining == 0)
            {
                // V3-4: advancing out of the primitive age, into one civilisation.
                string cost = UiText.T(" (", "（食") + economy.AdvanceFoodCost + UiText.T("F ", " 木") + economy.AdvanceWoodCost + UiText.T("W)", "）");
                if (GUI.Button(new Rect(x, y, half, 22f), UiText.T("Advance: farming", "時代を進める：農耕") + cost))
                    Send(EconomyCommand.Advance(faction, ++sequence, CivKind.Agrarian), UiText.T("Advancing into farming requested", "農耕の文明へ進めるよう依頼しました"));
                if (GUI.Button(new Rect(right, y, half, 22f), UiText.T("Advance: metallurgy", "時代を進める：冶金") + cost))
                    Send(EconomyCommand.Advance(faction, ++sequence, CivKind.Metallurgy), UiText.T("Advancing into metallurgy requested", "冶金の文明へ進めるよう依頼しました"));
                y += 26f;
            }
            else if (economy.Ages && (economy.Age == 1 || economy.Age == 2) && economy.AdvanceRemaining == 0)
            {
                // V3-5 (32 #7, #10): the next age of the civilisation taken - the second, then the third.
                string next = UiText.T("Advance: ", "時代を進める：") + AgeName(economy.Civ, economy.Age + 1);
                int food = economy.Age == 1 ? economy.Age2FoodCost : economy.Age3FoodCost;
                int wood = economy.Age == 1 ? economy.Age2WoodCost : economy.Age3WoodCost;
                if (GUI.Button(new Rect(x, y, w, 22f), next + UiText.T(" (", "（食") + food + UiText.T("F ", " 木") + wood + UiText.T("W)", "）")))
                    Send(EconomyCommand.Advance(faction, ++sequence, economy.Civ), next);
                y += 26f;
            }
            if (economy.Ages)
            {
                float third = (w - 8f) / 3f;
                if (GUI.Button(new Rect(x, y, third, 22f), UiText.T("Idle -> food", "待機 → 食料"))) SendIdle(economy, ResourceKind.Food);
                if (GUI.Button(new Rect(x + third + 4f, y, third, 22f), UiText.T("Idle -> wood", "待機 → 木材"))) SendIdle(economy, ResourceKind.Wood);
                if (GUI.Button(new Rect(x + 2f * (third + 4f), y, third, 22f), UiText.T("Idle -> stone", "待機 → 石"))) SendIdle(economy, ResourceKind.Stone);
            }
            else
            {
                if (GUI.Button(new Rect(x, y, half, 22f), UiText.T("Idle -> food", "待機中の村人 → 食料"))) SendIdle(economy, ResourceKind.Food);
                if (GUI.Button(new Rect(right, y, half, 22f), UiText.T("Idle -> wood", "待機中の村人 → 木材"))) SendIdle(economy, ResourceKind.Wood);
            }
            y += 26f;
            // Nothing to carry by hand before a civilisation brings mines or farms.
            if (!economy.Industry || (economy.Ages && economy.Civ == CivKind.Primitive)) return;
            bool farming = economy.Ages && economy.Civ == CivKind.Agrarian;
            var source = OwnBuilding(economy, farming ? BuildingKind.Farm : BuildingKind.Mine);
            string from = farming ? UiText.T("the farm", "農場") : UiText.T("the mine", "採掘場");
            GUI.enabled = source.HasValue && source.Value.Complete;
            if (GUI.Button(new Rect(x, y, w, 22f), UiText.T("Idle -> carry from ", "待機中の村人 → ") + from + UiText.T(" by hand", "から手で運ぶ")))
                SendIdleTo(economy, EconomyTargetKind.Building, source.Value.Id, from + UiText.T(" by hand", "から手で運ぶ"));
            GUI.enabled = true;
        }

        /// <summary>One trade at the market: TradeLot of one resource for TradeReturn of another (32 #9).</summary>
        private void TradeButton(Rect r, EconomyView economy, ResourceKind give, ResourceKind take, string label)
        {
            if (GUI.Button(r, label))
                Send(EconomyCommand.Trade(faction, ++sequence, give, take), label + UiText.T(" traded", " を交換しました"));
        }

        private static string TechName(TechKind t)
        {
            switch (t)
            {
                case TechKind.Weapons: return UiText.T("Weapons (attack +2)", "武器（攻撃+2）");
                case TechKind.Armour: return UiText.T("Armour (HP +20)", "鎧（HP+20）");
                case TechKind.Tools: return UiText.T("Tools (gather faster)", "道具（採集が速い）");
                case TechKind.Carts: return UiText.T("Carts (carry +5)", "荷車（運ぶ量+5）");
                case TechKind.Irrigation: return UiText.T("Irrigation (farms)", "灌漑（農場が速い）");
                case TechKind.Siegecraft: return UiText.T("Siegecraft (rams +30)", "攻城術（破城槌+30）");
                case TechKind.Masonry: return UiText.T("Masonry (towers +4)", "石工（見張り塔+4）");
                case TechKind.Banking: return UiText.T("Banking (trade +25)", "両替（交換+25）");
                case TechKind.SteelWeapons: return UiText.T("Steel weapons (attack +3)", "鋼の武器（攻撃+3）");
                case TechKind.SteelArmour: return UiText.T("Steel armour (HP +30)", "鋼の鎧（HP+30）");
                default: return UiText.T("Blast furnace (smelting)", "高炉（精錬が速い）");
            }
        }

        /// <summary>V3-5: the blacksmith and its techs (each once; the civilisation's own tech only for that civilisation).</summary>
        private void DrawResearch(EconomyView economy, float x, float y, float w, float half, float right)
        {
            if (!economy.Ages) { GUI.Label(new Rect(x, y, w, 22f), UiText.T("No research on this map", "このマップには研究はありません")); return; }
            if (economy.Civ == CivKind.Primitive) { GUI.Label(new Rect(x, y, w, 22f), UiText.T("Research comes with a civilisation", "研究は文明に進んでから")); return; }
            var smith = OwnBuilding(economy, BuildingKind.Blacksmith);
            if (!smith.HasValue)
            {
                ModeButton(new Rect(x, y, half, 22f), Mode.Blacksmith, UiText.T("Blacksmith (", "鍛冶場（木材 ") + economy.BlacksmithWoodCost + UiText.T(" wood)", "）"));
                return;
            }
            var b = smith.Value;
            GUI.Label(new Rect(x, y, w, 22f), !b.Complete ? UiText.T("Blacksmith: building ", "鍛冶場：建設中 ") + Percent(b)
                : b.Researching != 0 ? UiText.T("Researching: ", "研究中：") + TechName(b.Researching) + " " + Seconds(b.ResearchRemaining)
                : UiText.T("Blacksmith: pick a tech", "鍛冶場：研究を選ぶ"));
            y += 26f;
            var techs = new List<TechKind> { TechKind.Weapons, TechKind.Armour, TechKind.Tools, TechKind.Carts };
            techs.Add(economy.Civ == CivKind.Agrarian ? TechKind.Irrigation : TechKind.BlastFurnace);
            // V3-5 (32 #10): the third age opens three more, the same for both civilisations.
            // V3-5 (32 #14): the steel pair comes with the second age and is paid in metal.
            if (economy.Age >= 2) { techs.Add(TechKind.SteelWeapons); techs.Add(TechKind.SteelArmour); }
            if (economy.Age >= 3) { techs.Add(TechKind.Masonry); techs.Add(TechKind.Siegecraft); techs.Add(TechKind.Banking); }
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                int index = (int)t - 1;
                bool done = (economy.Techs & (1UL << index)) != 0;
                string cost = index < economy.TechFoodCosts.Count
                    ? UiText.T(" F", " 食") + economy.TechFoodCosts[index] + UiText.T(" W", " 木") + economy.TechWoodCosts[index]
                        + (index < economy.TechMetalCosts.Count && economy.TechMetalCosts[index] > 0 ? UiText.T(" M", " 金") + economy.TechMetalCosts[index] : "") : "";
                GUI.enabled = b.Complete && b.Researching == 0 && !done;
                var r = new Rect(i % 2 == 0 ? x : right, y + (i / 2) * 26f, half, 22f);
                if (GUI.Button(r, (done ? UiText.T("Done: ", "済：") : "") + TechName(t) + (done ? "" : cost)))
                    Send(EconomyCommand.Research(faction, ++sequence, b.Id, t), UiText.T("Research requested: ", "研究を依頼しました：") + TechName(t));
            }
            GUI.enabled = true;
        }

        /// <summary>How the automatic economy runs: on or off, its policy, and handing back what the player holds.</summary>
        private void DrawPolicy(EconomyView economy, float x, float y, float w, float half, float right)
        {
            bool auto = GUI.Toggle(new Rect(x, y, half, 22f), economy.AutoEconomy, economy.AutoEconomy ? UiText.T("Auto economy: on", "お任せ内政：入") : UiText.T("Auto economy: off", "お任せ内政：切"), GUI.skin.button);
            if (auto != economy.AutoEconomy) Send(EconomyCommand.Auto(faction, ++sequence, auto), auto ? UiText.T("Auto economy on", "お任せ内政を入れました") : UiText.T("Auto economy off: villagers wait for you", "お任せ内政を切りました：村人は指示を待ちます"));
            if (economy.CorePlayerHeld) GUI.Label(new Rect(right, y, half, 22f), UiText.T("Core: trained by hand", "コア：手動で村人を作っている"));
            y += 26f;
            if (!economy.Industry) return;
            float third = (w - 8f) / 3f;
            PolicyButton(new Rect(x, y, third, 22f), economy, EconomyPolicy.Balanced, UiText.T("Balanced", "均衡"));
            PolicyButton(new Rect(x + third + 4f, y, third, 22f), economy, EconomyPolicy.Military, UiText.T("Army first", "兵を優先"));
            PolicyButton(new Rect(x + 2f * (third + 4f), y, third, 22f), economy, EconomyPolicy.Growth, UiText.T("Economy first", "内政を優先"));
            y += 26f;
            if (GUI.Button(new Rect(x, y, w, 22f), UiText.T("Hand everything you touched back to the auto economy", "触った物をすべてお任せに戻す")))
                Send(EconomyCommand.ReturnToAuto(faction, ++sequence), UiText.T("Everything you held goes back to the auto economy", "触った物をすべてお任せの内政に戻しました"));
        }

        private void PolicyButton(Rect r, EconomyView economy, EconomyPolicy policy, string label)
        {
            bool on = GUI.Toggle(r, economy.Policy == policy, label, GUI.skin.button);
            if (on && economy.Policy != policy)
                Send(EconomyCommand.SetPolicy(faction, ++sequence, policy), UiText.T("Economy policy: ", "内政の方針：") + label);
        }

        private void ModeButton(Rect r, Mode target, string label)
        {
            bool on = GUI.Toggle(r, mode == target, mode == target && target != Mode.Belt && target != Mode.Wall ? UiText.T("Click ground (Esc cancels)", "地面をクリック（Escで取消）") : label, GUI.skin.button);
            if (on && mode != target) SetMode(target);
            else if (!on && mode == target) SetMode(Mode.None);
        }

        private void DrawNotes(Rect rect)
        {
            if (notes.Count == 0) return;
            GUI.Label(rect, notes[notes.Count - 1]);
        }

        /// <summary>Every idle own villager goes to the point of that kind nearest the own core (the simulation re-checks it).</summary>
        private void SendIdle(EconomyView economy, ResourceKind kind)
        {
            var core = OwnCorePosition();
            uint best = 0; float bestDistance = float.MaxValue;
            foreach (var r in economy.Resources)
            {
                if (r.Kind != kind) continue;
                float d = (EconomyLayer.ToWorld(r.Position) - core).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = r.Id; }
            }
            if (best == 0) { Note(UiText.T("Nothing of that kind is left.", "その資源はもう残っていません。")); return; }
            SendIdleTo(economy, EconomyTargetKind.ResourceNode, best, kind == ResourceKind.Food ? UiText.T("food", "食料")
                : kind == ResourceKind.Stone ? UiText.T("stone", "石") : UiText.T("wood", "木材"));
        }

        private void SendIdleTo(EconomyView economy, EconomyTargetKind target, uint targetId, string what)
        {
            var idle = new List<uint>();
            foreach (var v in economy.Villagers) if (v.IsOwn && v.Activity == VillagerActivity.Idle) idle.Add(v.Id);
            if (idle.Count == 0) { Note(UiText.T("No idle villagers.", "待機中の村人はいません。")); return; }
            Send(EconomyCommand.Assign(faction, ++sequence, idle, target, targetId), idle.Count + UiText.T(" idle villager(s) -> ", " 人の待機中の村人 → ") + what);
        }

        private Vector3 OwnCorePosition()
        {
            var frame = view.LatestFrame;
            foreach (var o in frame.Objectives)
                if (o.Kind == GoalKind.Core && o.OwnerFactionId == faction) return EconomyLayer.ToWorld(o.Position);
            return Vector3.zero;
        }

        /// <summary>The own building of that kind: a finished one if there is one, else the first under construction.</summary>
        private BuildingView? OwnBuilding(EconomyView economy, BuildingKind kind)
        {
            BuildingView? found = null;
            foreach (var b in economy.Buildings)
            {
                if (b.FactionId != faction || b.Kind != kind) continue;
                if (b.Complete) return b;
                if (!found.HasValue) found = b;
            }
            return found;
        }

        private static int CountIdle(EconomyView economy)
        {
            int count = 0;
            foreach (var v in economy.Villagers) if (v.IsOwn && v.Activity == VillagerActivity.Idle) count++;
            return count;
        }

        private void Send(EconomyCommand command, string note) { port.SubmitEconomy(command); Note(note); }

        private void Note(string line) { notes.Add(line); if (notes.Count > 8) notes.RemoveAt(0); }

        private static string Percent(BuildingView b) => (b.Work == 0 ? 0 : b.Progress * 100 / b.Work) + "%";

        private static string Seconds(long ticks) => (ticks < 0 ? 0 : ticks / 20f).ToString("0.0") + "s";
    }
}
