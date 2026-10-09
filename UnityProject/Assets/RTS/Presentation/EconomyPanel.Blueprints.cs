using System;
using System.Collections.Generic;
using System.IO;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    public sealed partial class EconomyPanel
    {
        private BlueprintDocument activeBlueprint;
        private readonly List<string> blueprintFiles = new List<string>();

        public IReadOnlyList<string> BlueprintFiles
        {
            get { RefreshBlueprintFiles(); return blueprintFiles; }
        }

        public void BeginBlueprintSave()
        {
            SetMode(Mode.BlueprintSave);
            Note(UiText.T("Drag a rectangle to save a blueprint.", "設計図にする範囲を四角くドラッグしてください。"));
        }

        public void BeginBlueprintPaste(string fileName)
        {
            var loaded = LoadBlueprint(fileName);
            if (loaded == null) return;
            activeBlueprint = loaded;
            SetMode(Mode.BlueprintPaste);
            Note(UiText.T("Blueprint attached to the cursor.", "設計図をマウスに付けました。"));
        }

        public void DeleteBlueprint(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            string path = Path.Combine(BlueprintDirectory(), Path.GetFileName(fileName));
            if (File.Exists(path)) File.Delete(path);
            RefreshBlueprintFiles();
            Note(UiText.T("Blueprint deleted.", "設計図を削除しました。"));
        }

        private void SaveBlueprintFromArea(List<int> area)
        {
            if (area == null || area.Count == 0) return;
            var economy = Economy();
            if (economy == null) return;
            int first = area[0], last = area[area.Count - 1];
            int minX = Math.Min(first % MapWidthCells, last % MapWidthCells), maxX = Math.Max(first % MapWidthCells, last % MapWidthCells);
            int minZ = Math.Min(first / MapWidthCells, last / MapWidthCells), maxZ = Math.Max(first / MapWidthCells, last / MapWidthCells);
            string name = string.IsNullOrEmpty(BlueprintNameForSave) ? "設計図" : BlueprintNameForSave.Trim();
            var blueprint = new BlueprintDocument { Name = name, Width = maxX - minX + 1, Height = maxZ - minZ + 1 };
            foreach (var building in economy.Buildings)
            {
                if (building.FactionId != faction) continue;
                int cell = CellOf(building.Center), cx = cell % MapWidthCells, cz = cell / MapWidthCells;
                int size = Math.Max(1, Mathf.RoundToInt(building.SizeMeters / (float)CellMeters));
                int x = cx - size / 2, z = cz - size / 2;
                if (x < minX || z < minZ || x >= minX + blueprint.Width || z >= minZ + blueprint.Height) continue;
                blueprint.Buildings.Add(new BlueprintBuilding(building.Kind, x - minX, z - minZ, size, size, building.Facing));
            }
            foreach (var belt in economy.Belts)
            {
                if (belt.FactionId != faction) continue;
                int x = belt.Cell % MapWidthCells, z = belt.Cell / MapWidthCells;
                if (x >= minX && x <= maxX && z >= minZ && z <= maxZ)
                {
                    int pair = belt.PairCell < 0 ? -1 : (belt.PairCell % MapWidthCells - minX) + (belt.PairCell / MapWidthCells - minZ) * blueprint.Width;
                    blueprint.Belts.Add(new BlueprintBelt(x - minX, z - minZ, belt.Facing, belt.Component, belt.SorterKind, pair, belt.Speed));
                }
            }
            string safe = SafeFileName(name);
            Directory.CreateDirectory(BlueprintDirectory());
            File.WriteAllText(Path.Combine(BlueprintDirectory(), safe + ".json"), BlueprintTools.Serialize(blueprint));
            RefreshBlueprintFiles();
            Note(UiText.T("Blueprint saved: ", "設計図を保存しました：") + name);
            SetMode(Mode.None);
        }

        private void RemoveArea(List<int> area)
        {
            if (area == null || area.Count == 0) return;
            var cells = new HashSet<int>(area);
            var economy = Economy();
            if (economy == null) return;
            var beltCells = new List<int>();
            var buildingIds = new List<uint>();
            foreach (var belt in economy.Belts) if (belt.FactionId == faction && cells.Contains(belt.Cell)) beltCells.Add(belt.Cell);
            foreach (var building in economy.Buildings) if (building.FactionId == faction && cells.Contains(CellOf(building.Center))) buildingIds.Add(building.Id);
            Note(UiText.T("Remove belts ", "ベルト ") + beltCells.Count + UiText.T(" and buildings ", "・建物 ") + buildingIds.Count + UiText.T("?", " を撤去"));
            for (int i = 0; i < beltCells.Count; i++) port.SubmitEconomy(EconomyCommand.RemoveBelt(faction, ++sequence, beltCells[i]));
            for (int i = 0; i < buildingIds.Count; i++) port.SubmitEconomy(EconomyCommand.RemoveBuilding(faction, ++sequence, buildingIds[i]));
            SetMode(Mode.None);
        }

        private void UpdateBlueprintPreview(int cell)
        {
            if (activeBlueprint == null || cell < 0) { layer.ShowBeltPreview(null, false); layer.ShowGhost(false, Vector3.zero, 0f, false); return; }
            var beltCells = new List<int>();
            for (int i = 0; i < activeBlueprint.Belts.Count; i++)
            {
                var belt = activeBlueprint.Belts[i];
                beltCells.Add(cell + belt.X + belt.Z * MapWidthCells);
            }
            bool legal = BlueprintFits(cell);
            layer.ShowBeltPreview(beltCells, legal);
            if (activeBlueprint.Buildings.Count == 0) { layer.ShowGhost(false, Vector3.zero, 0f, legal); return; }
            var building = activeBlueprint.Buildings[0];
            int origin = cell + building.X + building.Z * MapWidthCells;
            int size = Math.Max(building.Width, building.Height);
            Vector3 center = EconomyLayer.CellCenter(origin) + new Vector3(size * CellMeters / 2f, 0f, size * CellMeters / 2f);
            layer.ShowGhost(true, center, size * CellMeters, legal);
        }

        private void PasteBlueprintAt(int cell)
        {
            if (activeBlueprint == null || !BlueprintFits(cell))
            {
                Note(UiText.T("The blueprint does not fit here.", "設計図を置けない場所です。"));
                return;
            }
            for (int i = 0; i < activeBlueprint.Buildings.Count; i++)
            {
                var building = activeBlueprint.Buildings[i];
                int origin = cell + building.X + building.Z * MapWidthCells;
                port.SubmitEconomy(EconomyCommand.Place(faction, ++sequence, building.Kind, origin, building.Facing));
            }
            var placed = new List<BlueprintBelt>();
            for (int i = 0; i < activeBlueprint.Belts.Count; i++)
            {
                var belt = activeBlueprint.Belts[i];
                int absolute = cell + belt.X + belt.Z * MapWidthCells;
                if (belt.Component == BeltComponentKind.Splitter)
                    port.SubmitEconomy(EconomyCommand.PlaceSplitter(faction, ++sequence, absolute, belt.Facing));
                else if (belt.Component == BeltComponentKind.Sorter)
                    port.SubmitEconomy(EconomyCommand.PlaceSorter(faction, ++sequence, absolute, belt.Facing, belt.SorterKind));
                else if (belt.Component == BeltComponentKind.UndergroundEntrance && belt.PairCell >= 0)
                    port.SubmitEconomy(EconomyCommand.PlaceUnderground(faction, ++sequence, absolute,
                        cell + belt.PairCell % activeBlueprint.Width + (belt.PairCell / activeBlueprint.Width) * MapWidthCells, belt.Facing));
                else if (belt.Component == BeltComponentKind.None)
                    placed.Add(belt);
            }
            var runs = BlueprintTools.SplitBeltRuns(placed, EconomyCommand.MaxBeltRun);
            for (int i = 0; i < runs.Count; i++)
            {
                for (int start = 0; start < runs[i].Count; )
                {
                    BeltSpeed speed = runs[i][start].Speed;
                    var cells = new List<int>();
                    var facings = new List<Facing>();
                    int end = start;
                    while (end < runs[i].Count && runs[i][end].Speed == speed)
                    {
                        cells.Add(cell + runs[i][end].X + runs[i][end].Z * MapWidthCells);
                        facings.Add(runs[i][end].Facing);
                        end++;
                    }
                    if (speed == BeltSpeed.Fast)
                        port.SubmitEconomy(EconomyCommand.PlaceFastBelt(faction, ++sequence, cells, facings));
                    else
                        port.SubmitEconomy(EconomyCommand.PlaceBelt(faction, ++sequence, cells, facings));
                    start = end;
                }
            }
            Note(UiText.T("Blueprint placement requested.", "設計図の配置を依頼しました。"));
            SetMode(Mode.None);
        }

        private bool BlueprintFits(int originCell)
        {
            if (activeBlueprint == null) return false;
            int ox = originCell % MapWidthCells, oz = originCell / MapWidthCells;
            if (ox < 0 || oz < 0 || ox + activeBlueprint.Width > MapWidthCells || oz + activeBlueprint.Height > MapHeightCells) return false;
            var economy = Economy();
            if (economy == null) return false;
            var occupied = new HashSet<int>();
            foreach (var belt in economy.Belts) occupied.Add(belt.Cell);
            foreach (var building in economy.Buildings)
            {
                if (building.FactionId != faction) occupied.Add(CellOf(building.Center));
            }
            for (int i = 0; i < activeBlueprint.Belts.Count; i++)
                if (occupied.Contains(originCell + activeBlueprint.Belts[i].X + activeBlueprint.Belts[i].Z * MapWidthCells)) return false;
            for (int i = 0; i < activeBlueprint.Buildings.Count; i++)
            {
                var b = activeBlueprint.Buildings[i];
                for (int z = 0; z < b.Height; z++) for (int x = 0; x < b.Width; x++)
                    if (occupied.Contains(originCell + b.X + x + (b.Z + z) * MapWidthCells)) return false;
            }
            return true;
        }

        private void RefreshBlueprintFiles()
        {
            blueprintFiles.Clear();
            string directory = BlueprintDirectory();
            if (!Directory.Exists(directory)) return;
            var files = Directory.GetFiles(directory, "*.json");
            for (int i = 0; i < files.Length; i++) blueprintFiles.Add(Path.GetFileName(files[i]));
            blueprintFiles.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private BlueprintDocument LoadBlueprint(string fileName)
        {
            try
            {
                string path = Path.Combine(BlueprintDirectory(), Path.GetFileName(fileName));
                return File.Exists(path) ? BlueprintTools.Deserialize(File.ReadAllText(path)) : null;
            }
            catch (Exception exception)
            {
                Note(UiText.T("Blueprint could not be read: ", "設計図を読めませんでした：") + exception.Message);
                return null;
            }
        }

        private string BlueprintDirectory() { return Path.Combine(UnityEngine.Application.persistentDataPath, "blueprints"); }
        private static string SafeFileName(string value)
        {
            var result = value;
            foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid.ToString(), "_");
            return string.IsNullOrEmpty(result) ? "blueprint" : result;
        }

        private static int CellOf(SimPoint point)
        {
            int x = Mathf.FloorToInt(point.X.Raw / 65536f / CellMeters);
            int z = Mathf.FloorToInt(point.Z.Raw / 65536f / CellMeters);
            return z * MapWidthCells + x;
        }

        private static List<int> RectangleCells(int first, int last)
        {
            int minX = Math.Min(first % MapWidthCells, last % MapWidthCells), maxX = Math.Max(first % MapWidthCells, last % MapWidthCells);
            int minZ = Math.Min(first / MapWidthCells, last / MapWidthCells), maxZ = Math.Max(first / MapWidthCells, last / MapWidthCells);
            var cells = new List<int>();
            for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++) cells.Add(z * MapWidthCells + x);
            return cells;
        }

        public string BlueprintNameForSave { get; set; }
    }
}
