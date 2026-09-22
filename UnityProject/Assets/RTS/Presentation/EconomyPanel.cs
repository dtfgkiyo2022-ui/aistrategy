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
    /// Ver.3 economy panel (V3-1 PR5), bottom centre. Shows the stock and population and sends direct economy operations
    /// through IEconomyPort only; it never changes the simulation itself. Placing a barracks takes the next ground click:
    /// the footprint is centred on the clicked cell and the simulation decides whether it is legal.
    /// </summary>
    public sealed class EconomyPanel : MonoBehaviour
    {
        // Between the command buttons on the left (238 px) and the log/timeline column on the right (440 px).
        private const float LeftColumn = 246f, RightColumn = 440f, MaxWidth = 460f, MinWidth = 300f, Height = 150f;
        private const int CellMeters = 2, MapWidthCells = 128, MapHeightCells = 64;

        private IEconomyPort port;
        private BattlefieldView view;
        private EconomyLayer layer;
        private uint faction;
        private ulong sequence;
        private bool placing;
        private readonly List<string> notes = new List<string>();

        public bool IsPlacing { get { return placing; } }

        public void Bind(IEconomyPort economyPort, uint factionId, BattlefieldView battlefield, EconomyLayer economyLayer)
        {
            port = economyPort;
            faction = factionId;
            view = battlefield;
            layer = economyLayer;
            placing = false;
        }

        private Rect PanelRect()
        {
            float room = Screen.width - LeftColumn - RightColumn;
            float width = Mathf.Clamp(room - 8f, MinWidth, MaxWidth);
            float x = room >= MinWidth ? LeftColumn + (room - width) / 2f : Screen.width / 2f - width / 2f;
            return new Rect(x, Screen.height - 10f - Height, width, Height);
        }

        public bool BlocksClick(Vector2 screenPoint)
        {
            if (port == null) return false;
            return PanelRect().Contains(new Vector2(screenPoint.x, Screen.height - screenPoint.y));
        }

        /// <summary>A ground click while placing becomes a PlaceBuilding request. Returns true when the click was used.</summary>
        public bool TryConsumeGroundClick(Camera camera, Vector2 screenPoint)
        {
            if (!placing || port == null) return false;
            if (!TryFootprint(camera, screenPoint, out int origin, out _)) { Note(UiText.T("Click was outside the map.", "マップの外をクリックしました。")); return true; }
            placing = false;
            layer.ShowGhost(false, Vector3.zero, 0f, false);
            port.SubmitEconomy(EconomyCommand.Place(faction, ++sequence, BuildingKind.Barracks, origin));
            Note(UiText.T("Barracks requested at cell ", "兵舎を依頼しました：セル ") + origin + UiText.T(" (the simulation checks the ground).", "（置けるかはシミュレーションが判断します）"));
            return true;
        }

        private bool TryFootprint(Camera camera, Vector2 screenPoint, out int origin, out Vector3 center)
        {
            origin = -1; center = Vector3.zero;
            var economy = Economy();
            if (economy == null) return false;
            var ray = camera.ScreenPointToRay(screenPoint);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter)) return false;
            var hit = ray.GetPoint(enter);
            int cx = Mathf.FloorToInt(hit.x / CellMeters), cz = Mathf.FloorToInt(hit.z / CellMeters), size = economy.BuildingSizeCells;
            int x0 = cx - size / 2, z0 = cz - size / 2;
            if (x0 < 0 || z0 < 0 || x0 + size > MapWidthCells || z0 + size > MapHeightCells) return false;
            origin = z0 * MapWidthCells + x0;
            center = new Vector3((x0 + size / 2f) * CellMeters, 0f, (z0 + size / 2f) * CellMeters);
            return true;
        }

        private void Update()
        {
            if (!placing || layer == null) return;
            var camera = Camera.main;
            if (camera == null) return;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { placing = false; layer.ShowGhost(false, Vector3.zero, 0f, false); return; }
            var economy = Economy();
            bool shown = TryFootprint(camera, Input.mousePosition, out _, out var center);
            layer.ShowGhost(shown, center, economy == null ? 0f : economy.BuildingSizeCells * CellMeters, economy != null && economy.Wood >= economy.BarracksWoodCost);
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

            GUI.Label(new Rect(x, y, w, 20f), UiText.T("Food ", "食料 ") + economy.Food + UiText.T("   Wood ", "   木材 ") + economy.Wood + UiText.T("   Population ", "   人口 ") + economy.Population + " / " + economy.PopulationCap
                + UiText.T("   Idle ", "   待機 ") + CountIdle(economy));
            y += 22f;
            bool auto = GUI.Toggle(new Rect(x, y, half, 22f), economy.AutoEconomy, economy.AutoEconomy ? UiText.T("Auto economy: on", "お任せ内政：入") : UiText.T("Auto economy: off", "お任せ内政：切"), GUI.skin.button);
            if (auto != economy.AutoEconomy) Send(EconomyCommand.Auto(faction, ++sequence, auto), auto ? UiText.T("Auto economy on", "お任せ内政を入れました") : UiText.T("Auto economy off: villagers wait for you", "お任せ内政を切りました：村人は指示を待ちます"));
            string queue = economy.VillagerQueued == 0 ? "" : " [" + economy.VillagerQueued + ", " + Seconds(economy.VillagerTrainRemaining) + "]";
            if (GUI.Button(new Rect(right, y, half, 22f), UiText.T("Villager (", "村人（食料 ") + economy.VillagerFoodCost + UiText.T(" food)", "）") + queue))
                Send(EconomyCommand.Train(faction, ++sequence, 0, UnitKind.Villager), UiText.T("Villager requested", "村人を依頼しました"));
            y += 26f;
            if (GUI.Toggle(new Rect(x, y, half, 22f), placing, placing ? UiText.T("Click ground (Esc cancels)", "地面をクリック（Escで取消）") : UiText.T("Barracks (", "兵舎（木材 ") + economy.BarracksWoodCost + UiText.T(" wood)", "）"), GUI.skin.button) != placing)
            { placing = !placing; if (!placing) layer.ShowGhost(false, Vector3.zero, 0f, false); }
            var barracks = OwnBarracks(economy);
            if (barracks.HasValue && barracks.Value.Complete)
            {
                var b = barracks.Value;
                string trains = b.Queued == 0 ? "" : " [" + b.Queued + ", " + Seconds(b.TrainRemaining) + "]";
                if (GUI.Button(new Rect(right, y, half - 56f, 22f), UiText.T("Infantry (", "歩兵（食") + economy.InfantryFoodCost + UiText.T("F ", " 木") + economy.InfantryWoodCost + UiText.T("W)", "）") + trains))
                    Send(EconomyCommand.Train(faction, ++sequence, b.Id, UnitKind.Infantry), UiText.T("Infantry requested", "歩兵を依頼しました"));
                if (GUI.Button(new Rect(right + half - 52f, y, 52f, 22f), UiText.T("Undo", "取消"))) Send(EconomyCommand.CancelTrain(faction, ++sequence, b.Id), UiText.T("Last infantry cancelled", "最後の歩兵を取り消しました"));
            }
            else GUI.Label(new Rect(right, y, half, 22f), barracks.HasValue ? UiText.T("Barracks: building ", "兵舎：建設中 ") + Percent(barracks.Value) : UiText.T("No barracks yet", "兵舎はまだありません"));
            y += 26f;
            if (GUI.Button(new Rect(x, y, half, 22f), UiText.T("Idle villagers -> food", "待機中の村人 → 食料"))) SendIdle(economy, ResourceKind.Food);
            if (GUI.Button(new Rect(right, y, half, 22f), UiText.T("Idle villagers -> wood", "待機中の村人 → 木材"))) SendIdle(economy, ResourceKind.Wood);
            y += 24f;
            DrawNotes(new Rect(x, y, w, rect.yMax - y - 2f));
        }

        private void DrawNotes(Rect rect)
        {
            if (notes.Count == 0) return;
            GUI.Label(rect, notes[notes.Count - 1]);
        }

        /// <summary>Every idle own villager goes to the point of that kind nearest the own core (the simulation re-checks it).</summary>
        private void SendIdle(EconomyView economy, ResourceKind kind)
        {
            var idle = new List<uint>();
            foreach (var v in economy.Villagers) if (v.IsOwn && v.Activity == VillagerActivity.Idle) idle.Add(v.Id);
            if (idle.Count == 0) { Note(UiText.T("No idle villagers.", "待機中の村人はいません。")); return; }
            var core = OwnCorePosition();
            uint best = 0; float bestDistance = float.MaxValue;
            foreach (var r in economy.Resources)
            {
                if (r.Kind != kind) continue;
                float d = (EconomyLayer.ToWorld(r.Position) - core).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = r.Id; }
            }
            if (best == 0) { Note(UiText.T("Nothing of that kind is left.", "その資源はもう残っていません。")); return; }
            Send(EconomyCommand.Assign(faction, ++sequence, idle, EconomyTargetKind.ResourceNode, best), idle.Count + UiText.T(" idle villager(s) -> ", " 人の待機中の村人 → ") + (kind == ResourceKind.Food ? UiText.T("food", "食料") : UiText.T("wood", "木材")));
        }

        private Vector3 OwnCorePosition()
        {
            var frame = view.LatestFrame;
            foreach (var o in frame.Objectives)
                if (o.Kind == GoalKind.Core && o.OwnerFactionId == faction) return EconomyLayer.ToWorld(o.Position);
            return Vector3.zero;
        }

        private BuildingView? OwnBarracks(EconomyView economy)
        {
            BuildingView? found = null;
            foreach (var b in economy.Buildings)
            {
                if (b.FactionId != faction || b.Kind != BuildingKind.Barracks) continue;
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
