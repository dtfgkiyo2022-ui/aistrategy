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

        private BattlefieldView view;
        private readonly Dictionary<uint, GameObject> resources = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, GameObject> ownVillagers = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, LocalVisualPack.AnimationHandle> ownVillagerAnimations = new Dictionary<uint, LocalVisualPack.AnimationHandle>();
        private readonly List<GameObject> enemyVillagers = new List<GameObject>();
        private readonly List<LocalVisualPack.AnimationHandle> enemyVillagerAnimations = new List<LocalVisualPack.AnimationHandle>();
        private readonly Dictionary<uint, GameObject> buildings = new Dictionary<uint, GameObject>();
        private readonly HashSet<uint> packedBuildings = new HashSet<uint>();
        // Name tags over the building boxes: until each building has its own look, the boxes are told apart by name.
        private readonly Dictionary<uint, KeyValuePair<BuildingKind, Vector3>> buildingTags = new Dictionary<uint, KeyValuePair<BuildingKind, Vector3>>();
        private GUIStyle tagStyle;
        private readonly Dictionary<uint, GameObject> ports = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, Vector3> villagerTargets = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, Vector3> villagerFrom = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, float> villagerFacing = new Dictionary<uint, float>();
        private readonly Dictionary<uint, float> villagerWalkRates = new Dictionary<uint, float>();
        private readonly Dictionary<uint, VillagerActivity> villagerActivities = new Dictionary<uint, VillagerActivity>();
        private readonly Dictionary<uint, Vector3> villagerWorkTargets = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, GameObject> carryBoxes = new Dictionary<uint, GameObject>();
        private readonly List<Vector3> enemyVillagerFrom = new List<Vector3>();
        private readonly List<Vector3> enemyVillagerTargets = new List<Vector3>();
        private readonly List<float> enemyVillagerFacing = new List<float>();
        private readonly List<float> enemyVillagerWalkRates = new List<float>();
        private readonly List<VillagerActivity> enemyVillagerActivities = new List<VillagerActivity>();
        private readonly List<Vector3> enemyVillagerWorkTargets = new List<Vector3>();
        private long lastVillagerFrameTick = long.MinValue;
        private float villagerInterpolationSeconds;
        private GameObject ghost;
        private readonly Dictionary<int, GameObject> belts = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> items = new Dictionary<int, GameObject>();
        private readonly Dictionary<uint, string> buildingWarnings = new Dictionary<uint, string>();
        private readonly List<GameObject> beltPreview = new List<GameObject>();
        private readonly EconomyFlowMetrics flowMetrics = new EconomyFlowMetrics();
        private long lastMetricTick = long.MinValue;
        private const float CellMeters = 2f;
        private const int MapWidthCells = 128, MapHeightCells = 64;

        private static readonly Color WoodColor = new Color(0.2f, 0.55f, 0.2f);
        private static readonly Color FoodColor = new Color(0.95f, 0.8f, 0.2f);
        private static readonly Color OreColor = new Color(0.45f, 0.42f, 0.48f);
        private static readonly Color StoneColor = new Color(0.78f, 0.78f, 0.74f);
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
        private static readonly Color BeltStallColor = new Color(1f, 0.12f, 0.08f);
        private static readonly Color WarningColor = new Color(1f, 0.55f, 0.2f);
        private const int DisplayBufferLimit = 10;

        public void Bind(BattlefieldView battlefield) { view = battlefield; }

        public float BuildingUtilization(uint buildingId) { return flowMetrics.BuildingUtilization(buildingId); }
        public int BeltDeliveriesPerMinute(int cell) { return flowMetrics.BeltDeliveriesPerMinute(cell); }

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

        // Leaving Play mode destroys the villagers without going through Clear; their animation graphs must still go.
        private void OnDestroy()
        {
            foreach (var animation in ownVillagerAnimations.Values) animation.Dispose();
            foreach (var animation in enemyVillagerAnimations) if (animation != null) animation.Dispose();
        }

        public void Clear()
        {
            foreach (var go in resources.Values) Destroy(go);
            foreach (var go in ownVillagers.Values) Destroy(go);
            foreach (var go in enemyVillagers) Destroy(go);
            foreach (var animation in ownVillagerAnimations.Values) animation.Dispose();
            foreach (var animation in enemyVillagerAnimations) if (animation != null) animation.Dispose();
            foreach (var go in buildings.Values) Destroy(go);
            foreach (var go in ports.Values) Destroy(go);
            ports.Clear();
            foreach (var go in buildingFlags.Values) Destroy(go);
            buildingFlags.Clear();
            foreach (var go in carryBoxes.Values) Destroy(go);
            carryBoxes.Clear();
            foreach (var go in belts.Values) Destroy(go);
            foreach (var go in items.Values) Destroy(go);
            resources.Clear(); ownVillagers.Clear(); enemyVillagers.Clear(); buildings.Clear(); villagerTargets.Clear();
            villagerFrom.Clear(); villagerFacing.Clear(); villagerWalkRates.Clear(); villagerActivities.Clear(); villagerWorkTargets.Clear();
            enemyVillagerFrom.Clear(); enemyVillagerTargets.Clear(); enemyVillagerFacing.Clear(); enemyVillagerWalkRates.Clear();
            enemyVillagerActivities.Clear(); enemyVillagerWorkTargets.Clear();
            lastVillagerFrameTick = long.MinValue;
            villagerInterpolationSeconds = 0f;
            ownVillagerAnimations.Clear(); enemyVillagerAnimations.Clear();
            packedBuildings.Clear();
            belts.Clear(); items.Clear(); buildingWarnings.Clear(); flowMetrics.Clear(); lastMetricTick = long.MinValue;
        }

        private void Update()
        {
            long probe = PerfProbe.Start();
            UpdateMeasured();
            PerfProbe.Stop("EconomyLayer", probe);
        }

        private void UpdateMeasured()
        {
            var frame = view == null ? null : view.LatestFrame;
            var economy = frame == null ? null : frame.Economy;
            if (economy == null) { if (resources.Count + ownVillagers.Count + buildings.Count + enemyVillagers.Count + belts.Count > 0) Clear(); return; }
            SyncResources(economy);
            SyncVillagers(economy);
            SyncBuildings(economy);
            SyncBelts(economy);
            float matchRate = view == null ? 0f : view.MatchRate;
            villagerInterpolationSeconds = Mathf.Min(BattlefieldView.TickSeconds,
                villagerInterpolationSeconds + Time.deltaTime * matchRate);
            float alpha = BattlefieldView.TickSeconds <= 0f ? 1f : villagerInterpolationSeconds / BattlefieldView.TickSeconds;
            foreach (var pair in ownVillagers)
                ApplyVillager(pair.Key, pair.Value, ownVillagerAnimations, alpha, matchRate);
            for (int i = 0; i < enemyVillagers.Count; i++)
                ApplyEnemyVillager(i, alpha, matchRate);
        }

        private void SyncResources(EconomyView economy)
        {
            var seen = new HashSet<uint>();
            foreach (var r in economy.Resources)
            {
                seen.Add(r.Id);
                if (!resources.TryGetValue(r.Id, out var go))
                {
                    bool ore = r.Kind == ResourceKind.Ore, stone = r.Kind == ResourceKind.Stone;
                    go = GameObject.CreatePrimitive(r.Kind == ResourceKind.Wood ? PrimitiveType.Cylinder : ore || stone ? PrimitiveType.Cube : PrimitiveType.Sphere);
                    go.name = (r.Kind == ResourceKind.Wood ? "Wood " : ore ? "Ore " : stone ? "Stone " : "Food ") + r.Id;
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    go.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(r.Kind == ResourceKind.Wood ? WoodColor : ore ? OreColor : stone ? StoneColor : FoodColor);
                    if (ore) go.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
                    resources.Add(r.Id, go);
                }
                // Shrinks as it is gathered, never below a third so a nearly empty point is still visible.
                float fill = Mathf.Clamp01(r.Remaining / (r.Kind == ResourceKind.Ore ? 400f : 300f)) * 0.66f + 0.34f;
                var p = ToWorld(r.Position);
                if (r.Kind == ResourceKind.Wood) { go.transform.position = new Vector3(p.x, 1.5f * fill, p.z); go.transform.localScale = new Vector3(1.2f, 1.5f * fill, 1.2f); }
                else if (r.Kind == ResourceKind.Ore || r.Kind == ResourceKind.Stone) { go.transform.position = new Vector3(p.x, 0.5f * fill, p.z); go.transform.localScale = new Vector3(1.5f, 1f, 1.5f) * fill; }
                else { go.transform.position = new Vector3(p.x, 0.6f * fill, p.z); go.transform.localScale = Vector3.one * 1.3f * fill; }
            }
            Remove(resources, seen);
        }

        private void SyncVillagers(EconomyView economy)
        {
            long frameTick = view == null || view.LatestFrame == null ? long.MinValue : view.LatestFrame.Tick;
            bool newFrame = frameTick != lastVillagerFrameTick;
            if (newFrame)
            {
                lastVillagerFrameTick = frameTick;
                villagerInterpolationSeconds = 0f;
            }
            var seen = new HashSet<uint>();
            int enemy = 0;
            bool villagerPacked = LocalVisualPack.HasUnit(UnitKind.Villager);
            foreach (var v in economy.Villagers)
            {
                // A pack villager stands with its feet on the ground; the placeholder capsule is centred half a metre up.
                var p = ToWorld(v.Position) + (villagerPacked ? Vector3.zero : Vector3.up * 0.5f);
                if (v.IsOwn)
                {
                    seen.Add(v.Id);
                    if (!ownVillagers.TryGetValue(v.Id, out var go))
                    {
                        go = CreateVillager("Villager " + v.Id, true);
                        go.transform.position = p;
                        ownVillagers.Add(v.Id, go);
                        var animation = LocalVisualPack.TryCreateAnimation(go, UnitKind.Villager, v.Id);
                        if (animation != null) ownVillagerAnimations.Add(v.Id, animation);
                        villagerFrom[v.Id] = p;
                        villagerFacing[v.Id] = 0f;
                    }
                    if (newFrame)
                    {
                        villagerFrom[v.Id] = villagerTargets.TryGetValue(v.Id, out var oldTarget) ? oldTarget : p;
                        villagerTargets[v.Id] = p;
                        villagerActivities[v.Id] = v.Activity;
                        villagerWorkTargets[v.Id] = FindVillagerWorkTarget(v, economy);
                        villagerFacing[v.Id] = VillagerFacing(v, villagerFrom[v.Id], p, villagerFacing[v.Id], villagerWorkTargets[v.Id]);
                        villagerWalkRates[v.Id] = VillagerWalkRate(v.Id, villagerFrom[v.Id], p);
                    }
                    SetFlag(go.transform, v.PlayerHeld, new Vector3(0f, 1.4f, 0f), new Vector3(0.18f, 0.7f, 0.18f));
                    SetCarryBox(v.Id, go.transform, v.Activity == VillagerActivity.Returning || v.Activity == VillagerActivity.Hauling,
                        v.Carry > 0 ? v.CarryKind : ResourceKind.Food);
                }
                else
                {
                    if (enemy == enemyVillagers.Count)
                    {
                        var enemyObject = CreateVillager("Enemy villager", false);
                        enemyVillagers.Add(enemyObject);
                        enemyVillagerAnimations.Add(LocalVisualPack.TryCreateAnimation(enemyObject, UnitKind.Villager, (ulong)(enemy + 1)));
                    }
                    enemyVillagers[enemy].SetActive(true);
                    EnsureEnemyVillagerState(enemy, p, v, economy, newFrame);
                    enemy++;
                }
            }
            for (int i = enemy; i < enemyVillagers.Count; i++) enemyVillagers[i].SetActive(false);
            while (enemyVillagerFrom.Count > enemy) enemyVillagerFrom.RemoveAt(enemyVillagerFrom.Count - 1);
            while (enemyVillagerTargets.Count > enemy) enemyVillagerTargets.RemoveAt(enemyVillagerTargets.Count - 1);
            while (enemyVillagerFacing.Count > enemy) enemyVillagerFacing.RemoveAt(enemyVillagerFacing.Count - 1);
            while (enemyVillagerWalkRates.Count > enemy) enemyVillagerWalkRates.RemoveAt(enemyVillagerWalkRates.Count - 1);
            while (enemyVillagerActivities.Count > enemy) enemyVillagerActivities.RemoveAt(enemyVillagerActivities.Count - 1);
            while (enemyVillagerWorkTargets.Count > enemy) enemyVillagerWorkTargets.RemoveAt(enemyVillagerWorkTargets.Count - 1);
            var gone = new List<uint>();
            foreach (var id in ownVillagers.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                if (ownVillagerAnimations.TryGetValue(id, out var animation))
                {
                    animation.Dispose();
                    ownVillagerAnimations.Remove(id);
                }
                Destroy(ownVillagers[id]);
                ownVillagers.Remove(id);
                villagerTargets.Remove(id);
                villagerFrom.Remove(id);
                villagerFacing.Remove(id);
                villagerWalkRates.Remove(id);
                villagerActivities.Remove(id);
                villagerWorkTargets.Remove(id);
                if (carryBoxes.TryGetValue(id, out var box)) { Destroy(box); carryBoxes.Remove(id); }
            }
        }

        private void EnsureEnemyVillagerState(int index, Vector3 position, VillagerView villager, EconomyView economy, bool newFrame)
        {
            while (enemyVillagerFrom.Count <= index)
            {
                enemyVillagerFrom.Add(position);
                enemyVillagerTargets.Add(position);
                enemyVillagerFacing.Add(0f);
                enemyVillagerWalkRates.Add(1f);
                enemyVillagerActivities.Add(VillagerActivity.Idle);
                enemyVillagerWorkTargets.Add(Vector3.zero);
            }
            if (!newFrame) return;
            enemyVillagerFrom[index] = enemyVillagerTargets[index];
            enemyVillagerTargets[index] = position;
            enemyVillagerActivities[index] = villager.Activity;
            enemyVillagerWorkTargets[index] = FindVillagerWorkTarget(villager, economy);
            enemyVillagerFacing[index] = VillagerFacing(villager, enemyVillagerFrom[index], position,
                enemyVillagerFacing[index], enemyVillagerWorkTargets[index]);
            enemyVillagerWalkRates[index] = VillagerWalkRate((ulong)(index + 1), enemyVillagerFrom[index], position);
        }

        private void ApplyVillager(uint id, GameObject go, Dictionary<uint, LocalVisualPack.AnimationHandle> animations,
            float alpha, float matchRate)
        {
            if (!villagerTargets.TryGetValue(id, out var target) || !villagerFrom.TryGetValue(id, out var from)) return;
            UnitMotionMath.Interpolate(from.x, from.z, target.x, target.z, alpha, out var x, out var z);
            go.transform.position = new Vector3(x, target.y, z);
            if (villagerFacing.TryGetValue(id, out var facing))
                go.transform.rotation = Quaternion.Euler(0f, facing, 0f);
            VillagerActivity activity = villagerActivities.TryGetValue(id, out var value) ? value : VillagerActivity.Idle;
            bool moving = IsWalking(activity) && (target - from).sqrMagnitude > 0.0001f;
            bool working = IsWorking(activity);
            if (animations.TryGetValue(id, out var animation))
            {
                animation.SetDesired(moving, false, working, false);
                animation.SetPlaybackRate(villagerWalkRates.TryGetValue(id, out var rate) ? rate : 1f, matchRate);
                animation.Tick(Time.deltaTime, matchRate);
            }
        }

        private void ApplyEnemyVillager(int index, float alpha, float matchRate)
        {
            if (index >= enemyVillagers.Count || index >= enemyVillagerTargets.Count) return;
            var from = enemyVillagerFrom[index];
            var target = enemyVillagerTargets[index];
            UnitMotionMath.Interpolate(from.x, from.z, target.x, target.z, alpha, out var x, out var z);
            enemyVillagers[index].transform.position = new Vector3(x, target.y, z);
            enemyVillagers[index].transform.rotation = Quaternion.Euler(0f, enemyVillagerFacing[index], 0f);
            VillagerActivity activity = enemyVillagerActivities[index];
            bool moving = IsWalking(activity) && (target - from).sqrMagnitude > 0.0001f;
            bool working = IsWorking(activity);
            if (index < enemyVillagerAnimations.Count && enemyVillagerAnimations[index] != null)
            {
                enemyVillagerAnimations[index].SetDesired(moving, false, working, false);
                enemyVillagerAnimations[index].SetPlaybackRate(enemyVillagerWalkRates[index], matchRate);
                enemyVillagerAnimations[index].Tick(Time.deltaTime, matchRate);
            }
        }

        private Vector3 FindVillagerWorkTarget(VillagerView villager, EconomyView economy)
        {
            bool resource = villager.Activity == VillagerActivity.ToResource || villager.Activity == VillagerActivity.Gathering;
            bool building = villager.Activity == VillagerActivity.ToBuild || villager.Activity == VillagerActivity.Building;
            if (!resource && !building) return Vector3.zero;
            Vector3 origin = ToWorld(villager.Position);
            float best = float.MaxValue;
            Vector3 target = Vector3.zero;
            if (resource)
            {
                foreach (var item in economy.Resources)
                {
                    Vector3 candidate = ToWorld(item.Position);
                    float distance = (candidate - origin).sqrMagnitude;
                    if (distance < best) { best = distance; target = candidate; }
                }
            }
            else
            {
                foreach (var site in economy.Buildings)
                {
                    Vector3 candidate = ToWorld(site.Center);
                    float distance = (candidate - origin).sqrMagnitude;
                    if (distance < best) { best = distance; target = candidate; }
                }
            }
            return target;
        }

        private static float VillagerFacing(VillagerView villager, Vector3 from, Vector3 to, float fallback, Vector3 workTarget)
        {
            if (IsWorking(villager.Activity) && workTarget != Vector3.zero)
                return UnitMotionMath.FacingAngleDegrees(from.x, from.z, workTarget.x, workTarget.z, fallback);
            return UnitMotionMath.FacingAngleDegrees(from.x, from.z, to.x, to.z, fallback);
        }

        private static float VillagerWalkRate(ulong id, Vector3 from, Vector3 to)
        {
            return UnitMotionMath.WalkPlaybackRate(Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z)),
                BattlefieldView.TickSeconds, PresentationVisualConstants.UnitWalkBaseSpeed,
                UnitMotionMath.SpeedVariation(id, PresentationVisualConstants.UnitWalkSpeedVariation),
                PresentationVisualConstants.UnitWalkPlaybackMinimum, PresentationVisualConstants.UnitWalkPlaybackMaximum);
        }

        private static bool IsWalking(VillagerActivity activity)
        {
            return activity == VillagerActivity.ToResource || activity == VillagerActivity.ToBuild
                || activity == VillagerActivity.Returning || activity == VillagerActivity.Hauling;
        }

        private static bool IsWorking(VillagerActivity activity)
        {
            return activity == VillagerActivity.Gathering || activity == VillagerActivity.Building;
        }

        private void SetCarryBox(uint id, Transform parent, bool shown, ResourceKind kind)
        {
            if (!carryBoxes.TryGetValue(id, out var box))
            {
                box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "CarryBox " + id;
                Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(parent, false);
                box.transform.localScale = Vector3.one * PresentationVisualConstants.CarryBoxSize;
                carryBoxes.Add(id, box);
            }
            box.SetActive(shown);
            if (!shown) return;
            box.transform.localPosition = new Vector3(0f, PresentationVisualConstants.CarryBoxHeight, 0f);
            box.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(PresentationVisualConstants.CarryColor(kind));
        }

        private void SyncBuildings(EconomyView economy)
        {
            var seen = new HashSet<uint>();
            uint ownFaction = view.LatestFrame.FactionId;
            foreach (var b in economy.Buildings)
            {
                seen.Add(b.Id);
                if (!buildings.TryGetValue(b.Id, out var go))
                {
                    if (LocalVisualPack.TryCreateBuilding(b.Kind, b.FactionId, transform,
                        b.SizeMeters * LocalVisualPack.BuildingWidthScale(b.Kind), out go))
                        packedBuildings.Add(b.Id);
                    else
                        go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = b.Kind + " " + b.Id;
                    if (go.GetComponent<Collider>() != null) Destroy(go.GetComponent<Collider>());
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
                float full = b.Kind == BuildingKind.Mine ? 1.6f : b.Kind == BuildingKind.Smelter ? 2.4f : b.Kind == BuildingKind.Farm ? 0.8f
                    : b.Kind == BuildingKind.House ? 1.4f : b.Kind == BuildingKind.DropSite ? 1.0f
                    : b.Kind == BuildingKind.Wall ? 2.2f : b.Kind == BuildingKind.Tower ? 4.5f : b.Kind == BuildingKind.Blacksmith ? 2.0f
                    : b.Kind == BuildingKind.Market ? 1.8f : b.Kind == BuildingKind.SiegeWorkshop ? 2.6f
                    : b.Kind == BuildingKind.ArcheryRange ? 2.2f : b.Kind == BuildingKind.Stable ? 2.4f
                    : b.Kind == BuildingKind.Castle ? 6f : 3f;
                float height = finished ? full : 0.6f + (full - 0.6f) * (b.Work == 0 ? 0f : (float)b.Progress / b.Work);
                var color = b.FactionId == 1 ? WestColor : EastColor;
                if (!finished) color *= 0.55f;
                var p = ToWorld(b.Center);
                if (packedBuildings.Contains(b.Id))
                {
                    // The model is built at its finished height; under construction it rises with the progress.
                    float rise = finished ? 1f : height / full;
                    LocalVisualPack.SetBuildingProgress(go, rise);
                    go.transform.position = new Vector3(p.x, 0f, p.z);
                    height = LocalVisualPack.BuildingHeight(b.Kind) * rise;
                }
                else
                {
                    go.transform.position = new Vector3(p.x, height / 2f, p.z);
                    go.transform.localScale = new Vector3(b.SizeMeters, height, b.SizeMeters);
                    go.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(color);
                }
                buildingTags[b.Id] = new KeyValuePair<BuildingKind, Vector3>(b.Kind, new Vector3(p.x, height + 0.4f, p.z));
                if (b.FactionId == ownFaction)
                {
                    string warning = b.Output >= DisplayBufferLimit ? UiText.T("出口が詰まり", "出口が詰まり")
                        : RequiresInput(b.Kind) && b.Complete && b.Input == 0 ? UiText.T("材料待ち", "材料待ち") : "";
                    if (string.IsNullOrEmpty(warning)) buildingWarnings.Remove(b.Id); else buildingWarnings[b.Id] = warning;
                }
                else buildingWarnings.Remove(b.Id);
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
            var packedGone = new List<uint>();
            foreach (var id in packedBuildings) if (!seen.Contains(id)) packedGone.Add(id);
            foreach (var id in packedGone) packedBuildings.Remove(id);
            Remove(buildingFlags, seen);
            Remove(ports, seen);
            var warningGone = new List<uint>();
            foreach (var id in buildingWarnings.Keys) if (!seen.Contains(id)) warningGone.Add(id);
            foreach (var id in warningGone) buildingWarnings.Remove(id);
            var untagged = new List<uint>();
            foreach (var id in buildingTags.Keys) if (!seen.Contains(id)) untagged.Add(id);
            foreach (var id in untagged) buildingTags.Remove(id);
        }

        private void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null || buildingTags.Count == 0) return;
            if (tagStyle == null)
            {
                tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
                tagStyle.normal.textColor = Color.white;
            }
            foreach (var tagPair in buildingTags)
            {
                var tag = tagPair.Value;
                var screen = cam.WorldToScreenPoint(tag.Value);
                if (screen.z <= 0f) continue;
                // A map label is drawn after the HUD panels, so one under a panel would sit on top of it (10-08).
                if (UiHitAreas.Shared.ContainsGui(new Vector2(screen.x, Screen.height - screen.y))) continue;
                GUI.Label(new Rect(screen.x - 50f, Screen.height - screen.y - 10f, 100f, 20f), BuildingName(tag.Key), tagStyle);
                string warning;
                if (buildingWarnings.TryGetValue(tagPair.Key, out warning) && !string.IsNullOrEmpty(warning))
                    GUI.Label(new Rect(screen.x - 70f, Screen.height - screen.y + 8f, 140f, 18f), warning, WarningStyle());
            }
        }

        private static GUIStyle WarningStyle()
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            style.normal.textColor = WarningColor;
            return style;
        }

        /// <summary>Short display names for the name tags (display only).</summary>
        internal static string BuildingName(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Barracks: return UiText.T("Barracks", "兵舎");
                case BuildingKind.Mine: return UiText.T("Mine", "鉱山");
                case BuildingKind.Smelter: return UiText.T("Smelter", "製錬所");
                case BuildingKind.Farm: return UiText.T("Farm", "農場");
                case BuildingKind.House: return UiText.T("House", "住居");
                case BuildingKind.DropSite: return UiText.T("Drop site", "集積所");
                case BuildingKind.Wall: return "";
                case BuildingKind.Tower: return UiText.T("Tower", "塔");
                case BuildingKind.Blacksmith: return UiText.T("Blacksmith", "鍛冶場");
                case BuildingKind.Market: return UiText.T("Market", "市場");
                case BuildingKind.SiegeWorkshop: return UiText.T("Siege workshop", "攻城工房");
                case BuildingKind.ArcheryRange: return UiText.T("Archery range", "射撃場");
                case BuildingKind.Stable: return UiText.T("Stable", "厩舎");
                case BuildingKind.Castle: return UiText.T("Castle", "城");
                case BuildingKind.CharcoalKiln: return UiText.T("Charcoal kiln", "炭焼き窯");
                case BuildingKind.Steelworks: return UiText.T("Steelworks", "製鋼所");
                case BuildingKind.LumberCamp: return UiText.T("Lumber camp", "林業所");
                case BuildingKind.Fletcher: return UiText.T("Fletcher", "弓工房");
                case BuildingKind.Quarry: return UiText.T("Quarry", "採石場");
                case BuildingKind.Caravanserai: return UiText.T("Caravanserai", "隊商宿");
                case BuildingKind.EngineerCamp: return UiText.T("Engineer camp", "工兵所");
                case BuildingKind.Bridge: return UiText.T("Bridge", "橋");
                case BuildingKind.Academy: return UiText.T("Academy", "学府");
                case BuildingKind.Monastery: return UiText.T("Monastery", "修道院");
                case BuildingKind.Harbor: return UiText.T("Harbor", "漁港");
                case BuildingKind.MineShaft: return UiText.T("Mine shaft", "坑道小屋");
                case BuildingKind.Tollgate: return UiText.T("Tollgate", "関所");
                case BuildingKind.GrandHouse: return UiText.T("Grand house", "大住居");
                case BuildingKind.Shrine: return UiText.T("Shrine", "祠");
                case BuildingKind.Storage: return UiText.T("Storage", "倉庫");
                default: return kind.ToString();
            }
        }

        private void SyncBelts(EconomyView economy)
        {
            var seen = new HashSet<int>();
            var carrying = new HashSet<int>();
            var ownCells = new HashSet<int>();
            var endCells = new HashSet<int>();
            uint ownFaction = view.LatestFrame.FactionId;
            foreach (var belt in economy.Belts) if (belt.FactionId == ownFaction) ownCells.Add(belt.Cell);
            foreach (var b in economy.Belts)
            {
                seen.Add(b.Cell);
                int ticks = b.Speed == BeltSpeed.Fast && economy.FastBeltTicksPerCell > 0 ? economy.FastBeltTicksPerCell : Mathf.Max(1, economy.BeltTicksPerCell);
                if (b.FactionId == ownFaction && !ownCells.Contains(NextCell(b.Cell, b.Facing))) endCells.Add(b.Cell);
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
                bool stalled = b.FactionId == ownFaction && b.Item != 0 && b.Progress >= ticks * 2;
                Color baseColor = b.Component == BeltComponentKind.Splitter ? new Color(0.95f, 0.75f, 0.2f)
                    : b.Component == BeltComponentKind.Sorter ? new Color(0.75f, 0.35f, 0.95f)
                    : b.Component == BeltComponentKind.UndergroundEntrance || b.Component == BeltComponentKind.UndergroundExit ? new Color(0.3f, 0.35f, 0.45f)
                    : BeltColor;
                plate.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(stalled ? BeltStallColor : b.FactionId != ownFaction ? EnemyBeltColor : b.PlayerHeld ? HeldBeltColor : baseColor);
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
            if (view.LatestFrame.Tick != lastMetricTick)
            {
                lastMetricTick = view.LatestFrame.Tick;
                flowMetrics.ObserveBuildings(lastMetricTick, economy.Buildings, ownFaction);
                flowMetrics.ObserveBeltEnds(lastMetricTick, economy.Belts, endCells, ownFaction);
            }
        }

        private static int NextCell(int cell, Facing facing)
        {
            int x = cell % MapWidthCells, z = cell / MapWidthCells;
            if (facing == Facing.North) z++; else if (facing == Facing.South) z--; else if (facing == Facing.East) x++; else x--;
            return x < 0 || z < 0 || x >= MapWidthCells || z >= MapHeightCells ? -1 : z * MapWidthCells + x;
        }

        private static bool RequiresInput(BuildingKind kind)
        {
            return kind == BuildingKind.Smelter || kind == BuildingKind.Steelworks || kind == BuildingKind.Fletcher;
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

        private GameObject CreateVillager(string name, bool own)
        {
            if (LocalVisualPack.TryCreateUnit(UnitKind.Villager, own, transform, out var packed))
            {
                packed.name = name;
                return packed;
            }
            return Capsule(name, own ? OwnVillagerColor : EnemyVillagerColor);
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
