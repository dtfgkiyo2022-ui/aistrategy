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
        private const float LeftColumn = 246f, RightColumn = 440f, MaxWidth = 460f, MinWidth = 300f, Height = 150f, IndustryRows = 104f;
        private const int CellMeters = 2, MapWidthCells = 128, MapHeightCells = 64;

        private enum Mode { None, Barracks, Mine, Smelter, Belt, RemoveBelt }

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

        private Rect PanelRect()
        {
            var economy = Economy();
            float height = Height + (economy != null && economy.Industry ? IndustryRows : 0f);
            float room = Screen.width - LeftColumn - RightColumn;
            float width = Mathf.Clamp(room - 8f, MinWidth, MaxWidth);
            float x = room >= MinWidth ? LeftColumn + (room - width) / 2f : Screen.width / 2f - width / 2f;
            return new Rect(x, Screen.height - 10f - height, width, height);
        }

        public bool BlocksClick(Vector2 screenPoint)
        {
            if (port == null) return false;
            return PanelRect().Contains(new Vector2(screenPoint.x, Screen.height - screenPoint.y));
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
                    dragStart = cell; // the run is sent when the button comes up (Update)
                    return true;
                case Mode.RemoveBelt:
                    port.SubmitEconomy(EconomyCommand.RemoveBelt(faction, ++sequence, cell));
                    Note(UiText.T("Belt removal requested.", "ベルトを外すよう依頼しました。"));
                    return true;
            }
            var kind = mode == Mode.Mine ? BuildingKind.Mine : mode == Mode.Smelter ? BuildingKind.Smelter : BuildingKind.Barracks;
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
            return kind == BuildingKind.Mine ? e.MineSizeCells : kind == BuildingKind.Smelter ? e.SmelterSizeCells : e.BuildingSizeCells;
        }

        private int WoodOf(BuildingKind kind)
        {
            var e = Economy();
            if (e == null) return 0;
            return kind == BuildingKind.Mine ? e.MineWoodCost : kind == BuildingKind.Smelter ? e.SmelterWoodCost : e.BarracksWoodCost;
        }

        private static string Name(BuildingKind kind)
            => kind == BuildingKind.Mine ? UiText.T("Mine", "採掘場") : kind == BuildingKind.Smelter ? UiText.T("Smelter", "精錬所") : UiText.T("Barracks", "兵舎");

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
            if (mode == Mode.Belt)
            {
                var facings = new List<Facing>();
                var run = dragStart >= 0 && onMap ? BeltRun(dragStart, cell, facings) : onMap ? new List<int> { cell } : null;
                layer.ShowBeltPreview(run, run != null && economy.Wood >= run.Count * economy.BeltWoodCost);
                if (dragStart >= 0 && Input.GetMouseButtonUp(0))
                {
                    if (onMap)
                    {
                        run = BeltRun(dragStart, cell, facings);
                        port.SubmitEconomy(EconomyCommand.PlaceBelt(faction, ++sequence, run, facings));
                        Note(run.Count + UiText.T(" belt cell(s) requested.", " マスのベルトを依頼しました。"));
                    }
                    dragStart = -1;
                }
                return;
            }
            if (mode == Mode.RemoveBelt) { layer.ShowBeltPreview(onMap ? new List<int> { cell } : null, false); return; }
            var kind = mode == Mode.Mine ? BuildingKind.Mine : mode == Mode.Smelter ? BuildingKind.Smelter : BuildingKind.Barracks;
            int size = SizeOf(kind);
            var center = Vector3.zero;
            bool shown = onMap && Footprint(cell, size, out _, out center);
            layer.ShowGhost(shown, center, size * CellMeters, economy.Wood >= WoodOf(kind));
        }

        private EconomyView Economy() { var frame = view == null ? null : view.LatestFrame; return frame == null ? null : frame.Economy; }

        private void OnGUI()
        {
            if (port == null) return;
            var rect = PanelRect();
            var economy = Economy();
            GUI.Box(rect, economy == null ? UiText.T("Economy (off on this map)", "内政（このマップでは無し）") : UiText.T("Economy", "内政"));
            float x = rect.x + 8f, y = rect.y + 22f, w = rect.width - 16f, half = (w - 8f) / 2f, right = x + half + 8f;
            if (economy == null) { DrawNotes(new Rect(x, y, w, rect.yMax - y - 4f)); return; }

            string stock = UiText.T("Food ", "食料 ") + economy.Food + UiText.T("  Wood ", "  木材 ") + economy.Wood;
            if (economy.Industry) stock += UiText.T("  Ore ", "  鉱石 ") + economy.Ore + UiText.T("  Metal ", "  金属 ") + economy.Metal;
            GUI.Label(new Rect(x, y, w, 20f), stock + UiText.T("  Pop ", "  人口 ") + economy.Population + "/" + economy.PopulationCap
                + UiText.T("  Idle ", "  待機 ") + CountIdle(economy) + (economy.CorePlayerHeld ? UiText.T("  (core: yours)", "  （コア：手動）") : ""));
            y += 22f;
            bool auto = GUI.Toggle(new Rect(x, y, half, 22f), economy.AutoEconomy, economy.AutoEconomy ? UiText.T("Auto economy: on", "お任せ内政：入") : UiText.T("Auto economy: off", "お任せ内政：切"), GUI.skin.button);
            if (auto != economy.AutoEconomy) Send(EconomyCommand.Auto(faction, ++sequence, auto), auto ? UiText.T("Auto economy on", "お任せ内政を入れました") : UiText.T("Auto economy off: villagers wait for you", "お任せ内政を切りました：村人は指示を待ちます"));
            string queue = economy.VillagerQueued == 0 ? "" : " [" + economy.VillagerQueued + ", " + Seconds(economy.VillagerTrainRemaining) + "]";
            if (GUI.Button(new Rect(right, y, half, 22f), UiText.T("Villager (", "村人（食料 ") + economy.VillagerFoodCost + UiText.T(" food)", "）") + queue))
                Send(EconomyCommand.Train(faction, ++sequence, 0, UnitKind.Villager), UiText.T("Villager requested", "村人を依頼しました"));
            y += 26f;
            ModeButton(new Rect(x, y, half, 22f), Mode.Barracks, UiText.T("Barracks (", "兵舎（木材 ") + economy.BarracksWoodCost + UiText.T(" wood)", "）"));
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
            else GUI.Label(new Rect(right, y, half, 22f), barracks.HasValue ? UiText.T("Barracks: building ", "兵舎：建設中 ") + Percent(barracks.Value) : UiText.T("No barracks yet", "兵舎はまだありません"));
            y += 26f;
            if (economy.Industry)
            {
                // V3-3: the economy policy, and handing back what the player holds.
                float quarter = (w - 12f) / 4f;
                PolicyButton(new Rect(x, y, quarter, 22f), economy, EconomyPolicy.Balanced, UiText.T("Balanced", "均衡"));
                PolicyButton(new Rect(x + quarter + 4f, y, quarter, 22f), economy, EconomyPolicy.Military, UiText.T("Army first", "兵を優先"));
                PolicyButton(new Rect(x + 2f * (quarter + 4f), y, quarter, 22f), economy, EconomyPolicy.Growth, UiText.T("Economy first", "内政を優先"));
                if (GUI.Button(new Rect(x + 3f * (quarter + 4f), y, quarter, 22f), UiText.T("Hand back", "お任せに戻す")))
                    Send(EconomyCommand.ReturnToAuto(faction, ++sequence), UiText.T("Everything you held goes back to the auto economy", "触った物をすべてお任せの内政に戻しました"));
                y += 26f;
                ModeButton(new Rect(x, y, half, 22f), Mode.Mine, UiText.T("Mine (", "採掘場（木材 ") + economy.MineWoodCost + UiText.T(" wood)", "）"));
                ModeButton(new Rect(right, y, half, 22f), Mode.Smelter, UiText.T("Smelter (", "精錬所（木材 ") + economy.SmelterWoodCost + UiText.T(" wood)", "）"));
                y += 26f;
                ModeButton(new Rect(x, y, half, 22f), Mode.Belt, mode == Mode.Belt ? UiText.T("Drag on the ground (Esc ends)", "地面をドラッグ（Escで終了）")
                    : UiText.T("Belt (", "ベルト（木材 ") + economy.BeltWoodCost + UiText.T(" wood per cell)", "／マス）"));
                ModeButton(new Rect(right, y, half - 96f, 22f), Mode.RemoveBelt, UiText.T("Remove belt", "ベルトを外す"));
                GUI.Label(new Rect(right + half - 92f, y, 92f, 22f), UiText.T("R: ", "R：") + FacingName(facing));
                y += 26f;
                var mine = OwnBuilding(economy, BuildingKind.Mine);
                GUI.enabled = mine.HasValue && mine.Value.Complete;
                if (GUI.Button(new Rect(x, y, w, 22f), UiText.T("Idle villagers -> carry from the mine by hand", "待機中の村人 → 採掘場から手で運ぶ")))
                    SendIdleTo(economy, EconomyTargetKind.Building, mine.Value.Id, UiText.T("carry from the mine", "採掘場から手で運ぶ"));
                GUI.enabled = true;
                y += 26f;
            }
            if (GUI.Button(new Rect(x, y, half, 22f), UiText.T("Idle villagers -> food", "待機中の村人 → 食料"))) SendIdle(economy, ResourceKind.Food);
            if (GUI.Button(new Rect(right, y, half, 22f), UiText.T("Idle villagers -> wood", "待機中の村人 → 木材"))) SendIdle(economy, ResourceKind.Wood);
            y += 24f;
            string hint = mode == Mode.None ? null : mode == Mode.Belt ? UiText.T("Belts carry toward the stripe. R sets a single cell's direction.", "ベルトは白い線の向きに運びます。1マスだけのときは R で向きを変えます。")
                : mode == Mode.RemoveBelt ? UiText.T("Click a belt of yours.", "外す自分のベルトをクリック。")
                : UiText.T("Click the ground (Esc cancels). R turns the output side: ", "地面をクリック（Escで取消）。R で出口の向きを回す：") + FacingName(facing);
            if (hint != null) GUI.Label(new Rect(x, y, w, rect.yMax - y - 2f), hint);
            else DrawNotes(new Rect(x, y, w, rect.yMax - y - 2f));
        }

        private void PolicyButton(Rect r, EconomyView economy, EconomyPolicy policy, string label)
        {
            bool on = GUI.Toggle(r, economy.Policy == policy, label, GUI.skin.button);
            if (on && economy.Policy != policy)
                Send(EconomyCommand.SetPolicy(faction, ++sequence, policy), UiText.T("Economy policy: ", "内政の方針：") + label);
        }

        private void ModeButton(Rect r, Mode target, string label)
        {
            bool on = GUI.Toggle(r, mode == target, mode == target && target != Mode.Belt ? UiText.T("Click ground (Esc cancels)", "地面をクリック（Escで取消）") : label, GUI.skin.button);
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
            SendIdleTo(economy, EconomyTargetKind.ResourceNode, best, kind == ResourceKind.Food ? UiText.T("food", "食料") : UiText.T("wood", "木材"));
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
