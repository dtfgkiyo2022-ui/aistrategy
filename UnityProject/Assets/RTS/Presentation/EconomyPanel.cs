using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>What the map row of the economy panel may ask of the host. Changing either restarts the match.</summary>
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
        private const float Width = 460f, Height = 176f;
        private const int CellMeters = 2, MapWidthCells = 128, MapHeightCells = 64;

        private IEconomyPort port;
        private IMapChoice map;
        private BattlefieldView view;
        private EconomyLayer layer;
        private uint faction;
        private ulong sequence;
        private bool placing;
        private readonly List<string> notes = new List<string>();

        public bool IsPlacing { get { return placing; } }

        public void Bind(IEconomyPort economyPort, uint factionId, BattlefieldView battlefield, EconomyLayer economyLayer, IMapChoice mapChoice)
        {
            port = economyPort;
            faction = factionId;
            view = battlefield;
            layer = economyLayer;
            map = mapChoice;
            placing = false;
        }

        private Rect PanelRect() { return new Rect(Screen.width / 2f - Width / 2f, Screen.height - 10f - Height, Width, Height); }

        public bool BlocksClick(Vector2 screenPoint)
        {
            if (port == null) return false;
            return PanelRect().Contains(new Vector2(screenPoint.x, Screen.height - screenPoint.y));
        }

        /// <summary>A ground click while placing becomes a PlaceBuilding request. Returns true when the click was used.</summary>
        public bool TryConsumeGroundClick(Camera camera, Vector2 screenPoint)
        {
            if (!placing || port == null) return false;
            if (!TryFootprint(camera, screenPoint, out int origin, out _)) { Note("Click was outside the map."); return true; }
            placing = false;
            layer.ShowGhost(false, Vector3.zero, 0f, false);
            port.SubmitEconomy(EconomyCommand.Place(faction, ++sequence, BuildingKind.Barracks, origin));
            Note("Barracks requested at cell " + origin + " (the simulation checks the ground).");
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
            GUI.Box(rect, economy == null ? "Economy (off on this map)" : "Economy");
            float x = rect.x + 8f, y = rect.y + 22f, w = rect.width - 16f;
            DrawMapRow(new Rect(x, y, w, 24f));
            y += 28f;
            if (economy == null) { DrawNotes(new Rect(x, y, w, rect.yMax - y - 4f)); return; }

            GUI.Label(new Rect(x, y, w, 20f), "Food " + economy.Food + "   Wood " + economy.Wood + "   Population " + economy.Population + " / " + economy.PopulationCap
                + "   Idle " + CountIdle(economy));
            y += 22f;
            bool auto = GUI.Toggle(new Rect(x, y, 210f, 22f), economy.AutoEconomy, economy.AutoEconomy ? "Auto economy: on" : "Auto economy: off", GUI.skin.button);
            if (auto != economy.AutoEconomy) Send(EconomyCommand.Auto(faction, ++sequence, auto), auto ? "Auto economy on" : "Auto economy off: villagers wait for you");
            string queue = economy.VillagerQueued == 0 ? "" : " [" + economy.VillagerQueued + ", " + Seconds(economy.VillagerTrainRemaining) + "]";
            if (GUI.Button(new Rect(x + 218f, y, 226f, 22f), "Villager (" + economy.VillagerFoodCost + " food)" + queue))
                Send(EconomyCommand.Train(faction, ++sequence, 0, UnitKind.Villager), "Villager requested");
            y += 26f;
            if (GUI.Toggle(new Rect(x, y, 210f, 22f), placing, placing ? "Click ground (Esc cancels)" : "Barracks (" + economy.BarracksWoodCost + " wood)", GUI.skin.button) != placing)
            { placing = !placing; if (!placing) layer.ShowGhost(false, Vector3.zero, 0f, false); }
            var barracks = OwnBarracks(economy);
            if (barracks.HasValue && barracks.Value.Complete)
            {
                var b = barracks.Value;
                string trains = b.Queued == 0 ? "" : " [" + b.Queued + ", " + Seconds(b.TrainRemaining) + "]";
                if (GUI.Button(new Rect(x + 218f, y, 170f, 22f), "Infantry (" + economy.InfantryFoodCost + "F " + economy.InfantryWoodCost + "W)" + trains))
                    Send(EconomyCommand.Train(faction, ++sequence, b.Id, UnitKind.Infantry), "Infantry requested");
                if (GUI.Button(new Rect(x + 392f, y, 52f, 22f), "Undo")) Send(EconomyCommand.CancelTrain(faction, ++sequence, b.Id), "Last infantry cancelled");
            }
            else GUI.Label(new Rect(x + 218f, y, 226f, 22f), barracks.HasValue ? "Barracks: building " + Percent(barracks.Value) : "No barracks yet");
            y += 26f;
            if (GUI.Button(new Rect(x, y, 210f, 22f), "Idle villagers -> food")) SendIdle(economy, ResourceKind.Food);
            if (GUI.Button(new Rect(x + 218f, y, 226f, 22f), "Idle villagers -> wood")) SendIdle(economy, ResourceKind.Wood);
            y += 24f;
            DrawNotes(new Rect(x, y, w, rect.yMax - y - 2f));
        }

        private void DrawMapRow(Rect row)
        {
            if (map == null) return;
            bool economyMap = map.EconomyMap;
            bool now = GUI.Toggle(new Rect(row.x, row.y, 210f, row.height), economyMap, economyMap ? "Map: random #" + map.Seed : "Map: classic two roads", GUI.skin.button);
            if (now != economyMap) map.EconomyMap = now;
            GUI.enabled = economyMap;
            if (GUI.Button(new Rect(row.x + 218f, row.y, 226f, row.height), "New random map")) map.NewMap();
            GUI.enabled = true;
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
            if (idle.Count == 0) { Note("No idle villagers."); return; }
            var core = OwnCorePosition();
            uint best = 0; float bestDistance = float.MaxValue;
            foreach (var r in economy.Resources)
            {
                if (r.Kind != kind) continue;
                float d = (EconomyLayer.ToWorld(r.Position) - core).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = r.Id; }
            }
            if (best == 0) { Note("No " + kind.ToString().ToLowerInvariant() + " left."); return; }
            Send(EconomyCommand.Assign(faction, ++sequence, idle, EconomyTargetKind.ResourceNode, best), idle.Count + " idle villager(s) -> " + kind.ToString().ToLowerInvariant());
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
