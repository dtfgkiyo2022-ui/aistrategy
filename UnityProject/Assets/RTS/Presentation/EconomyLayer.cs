using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Ver.3 placeholder visuals for the economy (V3-1 PR5): resource points, villagers and buildings, read from the
    /// latest frame only. Wood is a green trunk, food a yellow bush; own villagers are light blue, enemy villagers orange;
    /// a barracks is a box in its faction colour, low and dark while it is being built.
    /// V3-2 (PR4): ore is a grey block; a mine is a low box and a smelter a middle one, each with a small yellow marker on
    /// the side its output comes out of; a belt is a dark plate with a light stripe toward where it carries, and the item
    /// on it a small cube (ore brown, metal silver) that slides along the cell as its ticks run.
    /// </summary>
    public sealed class EconomyLayer : MonoBehaviour
    {
        private const float Fix64Scale = 65536f;
        private const float VillagerSpeed = 8f; // display catch-up only, metres per second

        private BattlefieldView view;
        private readonly Dictionary<uint, GameObject> resources = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, GameObject> ownVillagers = new Dictionary<uint, GameObject>();
        private readonly List<GameObject> enemyVillagers = new List<GameObject>();
        private readonly Dictionary<uint, GameObject> buildings = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, GameObject> ports = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, Vector3> villagerTargets = new Dictionary<uint, Vector3>();
        private GameObject ghost;
        private readonly Dictionary<int, GameObject> belts = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> items = new Dictionary<int, GameObject>();
        private readonly List<GameObject> beltPreview = new List<GameObject>();
        private const float CellMeters = 2f;
        private const int MapWidthCells = 128;

        private static readonly Color WoodColor = new Color(0.2f, 0.55f, 0.2f);
        private static readonly Color FoodColor = new Color(0.95f, 0.8f, 0.2f);
        private static readonly Color OreColor = new Color(0.45f, 0.42f, 0.48f);
        private static readonly Color OreItemColor = new Color(0.55f, 0.35f, 0.2f);
        private static readonly Color MetalItemColor = new Color(0.85f, 0.88f, 0.95f);
        private static readonly Color BeltColor = new Color(0.18f, 0.18f, 0.2f);
        private static readonly Color EnemyBeltColor = new Color(0.3f, 0.2f, 0.2f);
        private static readonly Color StripeColor = new Color(0.75f, 0.75f, 0.8f);
        private static readonly Color PortColor = new Color(1f, 0.85f, 0.2f);
        // V3-3: what the player holds (the automatic economy leaves it alone) carries a small white flag, and a held belt
        // is a lighter plate.
        private static readonly Color HeldColor = new Color(1f, 1f, 1f);
        private static readonly Color HeldBeltColor = new Color(0.42f, 0.42f, 0.5f);
        private readonly Dictionary<uint, GameObject> buildingFlags = new Dictionary<uint, GameObject>();
        private static readonly Color OwnVillagerColor = new Color(0.55f, 0.8f, 1f);
        private static readonly Color EnemyVillagerColor = new Color(1f, 0.6f, 0.25f);
        private static readonly Color WestColor = new Color(0.25f, 0.4f, 0.9f);
        private static readonly Color EastColor = new Color(0.85f, 0.25f, 0.25f);

        public void Bind(BattlefieldView battlefield) { view = battlefield; }

        /// <summary>Placement preview: a translucent box over the footprint the next click would ask for, or hidden.</summary>
        public void ShowGhost(bool show, Vector3 center, float size, bool legalLooking)
        {
            if (ghost == null)
            {
                ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ghost.name = "PlacementGhost";
                Destroy(ghost.GetComponent<Collider>());
                ghost.transform.SetParent(transform, false);
            }
            ghost.SetActive(show);
            if (!show) return;
            ghost.transform.position = new Vector3(center.x, 0.5f, center.z);
            ghost.transform.localScale = new Vector3(size, 1f, size);
            ghost.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(legalLooking ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f));
        }

        public void Clear()
        {
            foreach (var go in resources.Values) Destroy(go);
            foreach (var go in ownVillagers.Values) Destroy(go);
            foreach (var go in enemyVillagers) Destroy(go);
            foreach (var go in buildings.Values) Destroy(go);
            foreach (var go in ports.Values) Destroy(go);
            ports.Clear();
            foreach (var go in buildingFlags.Values) Destroy(go);
            buildingFlags.Clear();
            foreach (var go in belts.Values) Destroy(go);
            foreach (var go in items.Values) Destroy(go);
            resources.Clear(); ownVillagers.Clear(); enemyVillagers.Clear(); buildings.Clear(); villagerTargets.Clear();
            belts.Clear(); items.Clear();
        }

        private void Update()
        {
            var frame = view == null ? null : view.LatestFrame;
            var economy = frame == null ? null : frame.Economy;
            if (economy == null) { if (resources.Count + ownVillagers.Count + buildings.Count + enemyVillagers.Count + belts.Count > 0) Clear(); return; }
            SyncResources(economy);
            SyncVillagers(economy);
            SyncBuildings(economy);
            SyncBelts(economy);
            foreach (var pair in ownVillagers)
                if (villagerTargets.TryGetValue(pair.Key, out var target))
                    pair.Value.transform.position = Vector3.MoveTowards(pair.Value.transform.position, target, VillagerSpeed * Time.deltaTime);
        }

        private void SyncResources(EconomyView economy)
        {
            var seen = new HashSet<uint>();
            foreach (var r in economy.Resources)
            {
                seen.Add(r.Id);
                if (!resources.TryGetValue(r.Id, out var go))
                {
                    bool ore = r.Kind == ResourceKind.Ore;
                    go = GameObject.CreatePrimitive(r.Kind == ResourceKind.Wood ? PrimitiveType.Cylinder : ore ? PrimitiveType.Cube : PrimitiveType.Sphere);
                    go.name = (r.Kind == ResourceKind.Wood ? "Wood " : ore ? "Ore " : "Food ") + r.Id;
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    go.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(r.Kind == ResourceKind.Wood ? WoodColor : ore ? OreColor : FoodColor);
                    if (ore) go.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
                    resources.Add(r.Id, go);
                }
                // Shrinks as it is gathered, never below a third so a nearly empty point is still visible.
                float fill = Mathf.Clamp01(r.Remaining / (r.Kind == ResourceKind.Ore ? 400f : 300f)) * 0.66f + 0.34f;
                var p = ToWorld(r.Position);
                if (r.Kind == ResourceKind.Wood) { go.transform.position = new Vector3(p.x, 1.5f * fill, p.z); go.transform.localScale = new Vector3(1.2f, 1.5f * fill, 1.2f); }
                else if (r.Kind == ResourceKind.Ore) { go.transform.position = new Vector3(p.x, 0.5f * fill, p.z); go.transform.localScale = new Vector3(1.5f, 1f, 1.5f) * fill; }
                else { go.transform.position = new Vector3(p.x, 0.6f * fill, p.z); go.transform.localScale = Vector3.one * 1.3f * fill; }
            }
            Remove(resources, seen);
        }

        private void SyncVillagers(EconomyView economy)
        {
            var seen = new HashSet<uint>();
            int enemy = 0;
            foreach (var v in economy.Villagers)
            {
                var p = ToWorld(v.Position) + Vector3.up * 0.5f;
                if (v.IsOwn)
                {
                    seen.Add(v.Id);
                    if (!ownVillagers.TryGetValue(v.Id, out var go))
                    {
                        go = Capsule("Villager " + v.Id, OwnVillagerColor);
                        go.transform.position = p;
                        ownVillagers.Add(v.Id, go);
                    }
                    villagerTargets[v.Id] = p;
                    SetFlag(go.transform, v.PlayerHeld, new Vector3(0f, 1.4f, 0f), new Vector3(0.18f, 0.7f, 0.18f));
                }
                else
                {
                    if (enemy == enemyVillagers.Count) enemyVillagers.Add(Capsule("Enemy villager", EnemyVillagerColor));
                    enemyVillagers[enemy].SetActive(true);
                    enemyVillagers[enemy].transform.position = p;
                    enemy++;
                }
            }
            for (int i = enemy; i < enemyVillagers.Count; i++) enemyVillagers[i].SetActive(false);
            var gone = new List<uint>();
            foreach (var id in ownVillagers.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone) { Destroy(ownVillagers[id]); ownVillagers.Remove(id); villagerTargets.Remove(id); }
        }

        private void SyncBuildings(EconomyView economy)
        {
            var seen = new HashSet<uint>();
            foreach (var b in economy.Buildings)
            {
                seen.Add(b.Id);
                if (!buildings.TryGetValue(b.Id, out var go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = b.Kind + " " + b.Id;
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    if (b.Kind != BuildingKind.Barracks)
                    {
                        // The output side: a small yellow block just outside the footprint, not scaled with the building.
                        var port = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        port.name = "Output " + b.Id;
                        Destroy(port.GetComponent<Collider>());
                        port.transform.SetParent(transform, false);
                        port.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(PortColor);
                        port.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
                        var p0 = ToWorld(b.Center) + Direction(b.Facing) * (b.SizeMeters / 2f + 0.2f);
                        port.transform.position = new Vector3(p0.x, 0.4f, p0.z);
                        ports.Add(b.Id, port);
                    }
                    buildings.Add(b.Id, go);
                }
                // An enemy building's progress is not shown (0), so it is drawn as finished.
                bool finished = b.Complete || b.MaxHp == 0;
                float full = b.Kind == BuildingKind.Mine ? 1.6f : b.Kind == BuildingKind.Smelter ? 2.4f : 3f;
                float height = finished ? full : 0.6f + (full - 0.6f) * (b.Work == 0 ? 0f : (float)b.Progress / b.Work);
                var color = b.FactionId == 1 ? WestColor : EastColor;
                if (!finished) color *= 0.55f;
                var p = ToWorld(b.Center);
                go.transform.position = new Vector3(p.x, height / 2f, p.z);
                go.transform.localScale = new Vector3(b.SizeMeters, height, b.SizeMeters);
                go.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(color);
                if (b.PlayerHeld)
                {
                    if (!buildingFlags.TryGetValue(b.Id, out var flag))
                    {
                        flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        flag.name = "Held " + b.Id;
                        Destroy(flag.GetComponent<Collider>());
                        flag.transform.SetParent(transform, false);
                        flag.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(HeldColor);
                        flag.transform.localScale = new Vector3(0.3f, 1.4f, 0.3f);
                        buildingFlags.Add(b.Id, flag);
                    }
                    flag.transform.position = new Vector3(p.x, height + 0.7f, p.z);
                }
                else if (buildingFlags.TryGetValue(b.Id, out var old)) { Destroy(old); buildingFlags.Remove(b.Id); }
            }
            Remove(buildings, seen);
            Remove(buildingFlags, seen);
            Remove(ports, seen);
        }

        private void SyncBelts(EconomyView economy)
        {
            var seen = new HashSet<int>();
            var carrying = new HashSet<int>();
            int ticks = Mathf.Max(1, economy.BeltTicksPerCell);
            uint ownFaction = view.LatestFrame.FactionId;
            foreach (var b in economy.Belts)
            {
                seen.Add(b.Cell);
                var centre = CellCenter(b.Cell);
                var dir = Direction(b.Facing);
                if (!belts.TryGetValue(b.Cell, out var plate))
                {
                    plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    plate.name = "Belt " + b.Cell;
                    Destroy(plate.GetComponent<Collider>());
                    plate.transform.SetParent(transform, false);
                    plate.transform.localScale = new Vector3(1.8f, 0.1f, 1.8f);
                    var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stripe.name = "Stripe";
                    Destroy(stripe.GetComponent<Collider>());
                    stripe.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(StripeColor);
                    stripe.transform.SetParent(plate.transform, false);
                    stripe.transform.localScale = new Vector3(0.18f, 1.2f, 0.45f);
                    stripe.transform.localPosition = new Vector3(0f, 0.1f, 0.25f);
                    belts.Add(b.Cell, plate);
                }
                plate.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(b.FactionId != ownFaction ? EnemyBeltColor : b.PlayerHeld ? HeldBeltColor : BeltColor);
                plate.transform.position = new Vector3(centre.x, 0.05f, centre.z);
                plate.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                if (b.Item == 0) continue;
                carrying.Add(b.Cell);
                if (!items.TryGetValue(b.Cell, out var item))
                {
                    item = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    item.name = "Item " + b.Cell;
                    Destroy(item.GetComponent<Collider>());
                    item.transform.SetParent(transform, false);
                    item.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
                    items.Add(b.Cell, item);
                }
                item.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(b.Item == ResourceKind.Metal ? MetalItemColor : OreItemColor);
                // From the edge it came in at to the edge it leaves by, as its ticks on this cell run.
                float along = Mathf.Clamp01((float)b.Progress / ticks) - 0.5f;
                item.transform.position = new Vector3(centre.x, 0.5f, centre.z) + dir * (along * CellMeters);
            }
            var gone = new List<int>();
            foreach (var cell in belts.Keys) if (!seen.Contains(cell)) gone.Add(cell);
            foreach (var cell in gone) { Destroy(belts[cell]); belts.Remove(cell); }
            gone.Clear();
            foreach (var cell in items.Keys) if (!carrying.Contains(cell)) gone.Add(cell);
            foreach (var cell in gone) { Destroy(items[cell]); items.Remove(cell); }
        }

        /// <summary>Belt placement preview: one translucent plate per cell of the run (green or red), or none.</summary>
        public void ShowBeltPreview(IList<int> cells, bool legalLooking)
        {
            int n = cells == null ? 0 : cells.Count;
            while (beltPreview.Count < n)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "BeltPreview";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(1.8f, 0.3f, 1.8f);
                beltPreview.Add(go);
            }
            var material = PresentationMaterials.GetUnlit(legalLooking ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f));
            for (int i = 0; i < beltPreview.Count; i++)
            {
                bool on = i < n;
                beltPreview[i].SetActive(on);
                if (!on) continue;
                var c = CellCenter(cells[i]);
                beltPreview[i].transform.position = new Vector3(c.x, 0.15f, c.z);
                beltPreview[i].GetComponent<Renderer>().sharedMaterial = material;
            }
        }

        public static Vector3 CellCenter(int cell)
            => new Vector3((cell % MapWidthCells + 0.5f) * CellMeters, 0f, (cell / MapWidthCells + 0.5f) * CellMeters);

        public static Vector3 Direction(Facing facing)
            => facing == Facing.North ? Vector3.forward : facing == Facing.East ? Vector3.right : facing == Facing.South ? Vector3.back : Vector3.left;

        /// <summary>A white flag child on a unit's object, made once and shown only while the player holds it.</summary>
        private static void SetFlag(Transform parent, bool shown, Vector3 localPosition, Vector3 worldScale)
        {
            var flag = parent.Find("Held");
            if (flag == null)
            {
                if (!shown) return;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Held";
                Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(HeldColor);
                go.transform.SetParent(parent, false);
                var s = parent.lossyScale;
                go.transform.localScale = new Vector3(worldScale.x / s.x, worldScale.y / s.y, worldScale.z / s.z);
                go.transform.localPosition = new Vector3(localPosition.x / s.x, localPosition.y / s.y, localPosition.z / s.z);
                flag = go.transform;
            }
            flag.gameObject.SetActive(shown);
        }

        private GameObject Capsule(string name, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
            go.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(color);
            return go;
        }

        private static void Remove(Dictionary<uint, GameObject> visuals, HashSet<uint> seen)
        {
            var gone = new List<uint>();
            foreach (var id in visuals.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone) { Destroy(visuals[id]); visuals.Remove(id); }
        }

        public static Vector3 ToWorld(SimPoint point) => new Vector3(point.X.Raw / Fix64Scale, 0f, point.Z.Raw / Fix64Scale);
    }
}
