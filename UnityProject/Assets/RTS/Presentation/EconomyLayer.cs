using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Ver.3 placeholder visuals for the economy (V3-1 PR5): resource points, villagers and buildings, read from the
    /// latest frame only. Wood is a green trunk, food a yellow bush; own villagers are light blue, enemy villagers orange;
    /// a barracks is a box in its faction colour, low and dark while it is being built.
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
        private readonly Dictionary<uint, Vector3> villagerTargets = new Dictionary<uint, Vector3>();
        private GameObject ghost;

        private static readonly Color WoodColor = new Color(0.2f, 0.55f, 0.2f);
        private static readonly Color FoodColor = new Color(0.95f, 0.8f, 0.2f);
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
            resources.Clear(); ownVillagers.Clear(); enemyVillagers.Clear(); buildings.Clear(); villagerTargets.Clear();
        }

        private void Update()
        {
            var frame = view == null ? null : view.LatestFrame;
            var economy = frame == null ? null : frame.Economy;
            if (economy == null) { if (resources.Count + ownVillagers.Count + buildings.Count + enemyVillagers.Count > 0) Clear(); return; }
            SyncResources(economy);
            SyncVillagers(economy);
            SyncBuildings(economy);
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
                    go = GameObject.CreatePrimitive(r.Kind == ResourceKind.Wood ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
                    go.name = (r.Kind == ResourceKind.Wood ? "Wood " : "Food ") + r.Id;
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    go.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(r.Kind == ResourceKind.Wood ? WoodColor : FoodColor);
                    resources.Add(r.Id, go);
                }
                // Shrinks as it is gathered, never below a third so a nearly empty point is still visible.
                float fill = Mathf.Clamp01(r.Remaining / 300f) * 0.66f + 0.34f;
                var p = ToWorld(r.Position);
                go.transform.position = r.Kind == ResourceKind.Wood ? new Vector3(p.x, 1.5f * fill, p.z) : new Vector3(p.x, 0.6f * fill, p.z);
                go.transform.localScale = r.Kind == ResourceKind.Wood ? new Vector3(1.2f, 1.5f * fill, 1.2f) : Vector3.one * 1.3f * fill;
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
                    go.name = "Barracks " + b.Id;
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    buildings.Add(b.Id, go);
                }
                // An enemy building's progress is not shown (0), so it is drawn as finished.
                bool finished = b.Complete || b.MaxHp == 0;
                float height = finished ? 3f : 0.6f + 2.4f * (b.Work == 0 ? 0f : (float)b.Progress / b.Work);
                var color = b.FactionId == 1 ? WestColor : EastColor;
                if (!finished) color *= 0.55f;
                var p = ToWorld(b.Center);
                go.transform.position = new Vector3(p.x, height / 2f, p.z);
                go.transform.localScale = new Vector3(b.SizeMeters, height, b.SizeMeters);
                go.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(color);
            }
            Remove(buildings, seen);
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
