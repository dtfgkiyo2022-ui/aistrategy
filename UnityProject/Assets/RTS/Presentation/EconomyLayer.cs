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
        private readonly Dictionary<uint, int> resourceLevels = new Dictionary<uint, int>();
        private readonly Dictionary<uint, GameObject> ownVillagers = new Dictionary<uint, GameObject>();
        private readonly List<uint> selectedVillagers = new List<uint>();
        private readonly Dictionary<uint, GameObject> villagerSelectionRings = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, LocalVisualPack.AnimationHandle> ownVillagerAnimations = new Dictionary<uint, LocalVisualPack.AnimationHandle>();
        private readonly List<GameObject> enemyVillagers = new List<GameObject>();
        private readonly List<LocalVisualPack.AnimationHandle> enemyVillagerAnimations = new List<LocalVisualPack.AnimationHandle>();
        private readonly Dictionary<uint, GameObject> buildings = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, WorldHealthBarVisuals.Handle> buildingHealthBars = new Dictionary<uint, WorldHealthBarVisuals.Handle>();
        private readonly Dictionary<uint, WorldHealthBarVisuals.Handle> buildingConstructionBars = new Dictionary<uint, WorldHealthBarVisuals.Handle>();
        private readonly Dictionary<uint, GameObject> buildingCivStandards = new Dictionary<uint, GameObject>();
        private readonly HashSet<uint> packedBuildings = new HashSet<uint>();
        private readonly Dictionary<uint, int> packedBuildingStages = new Dictionary<uint, int>();
        private sealed class BuildingFxState
        {
            public Vector3 Position;
            public float Height;
            public int Hp;
            public int MaxHp;
        }
        private sealed class TimedEffect
        {
            public GameObject Object;
            public float Remaining;
            public string File;
        }
        private readonly Dictionary<uint, BuildingFxState> buildingFxStates = new Dictionary<uint, BuildingFxState>();
        private readonly Dictionary<uint, TimedEffect> buildingFire = new Dictionary<uint, TimedEffect>();
        private readonly List<TimedEffect> buildingEffects = new List<TimedEffect>();
        private const int MaxBuildingEffects = 6;
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
        private readonly Dictionary<int, ResourceKind> itemKinds = new Dictionary<int, ResourceKind>();
        private readonly Dictionary<uint, GameObject> buildingStatusBubbles = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, GameObject> buildingActivityPuffs = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, GameObject> buildingDetailOverlays = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, GameObject> buildingLineHighlights = new Dictionary<uint, GameObject>();
        private readonly HashSet<uint> highlightedBuildingIds = new HashSet<uint>();
        private readonly HashSet<int> highlightedBeltCells = new HashSet<int>();
        private readonly CraftLineTraversal.Scratch lineTraversalScratch = new CraftLineTraversal.Scratch();
        private readonly Dictionary<int, BeltComponentKind> beltComponents = new Dictionary<int, BeltComponentKind>();
        private readonly HashSet<int> beltSeen = new HashSet<int>();
        private readonly HashSet<int> beltCarrying = new HashSet<int>();
        private readonly HashSet<int> beltOwnCells = new HashSet<int>();
        private readonly HashSet<int> beltEndCells = new HashSet<int>();
        private readonly List<int> beltGone = new List<int>();
        // Unity forbids creating a property block in a field initializer (the layer is added at run time), so it is made
        // on first use.
        private MaterialPropertyBlock beltPropertyBlock;
        private readonly List<GameObject> beltPreview = new List<GameObject>();
        private readonly EconomyFlowMetrics flowMetrics = new EconomyFlowMetrics();
        private long lastMetricTick = long.MinValue;
        private const float CellMeters = 2f;
        private const int MapWidthCells = 128, MapHeightCells = 64;

        private static readonly Color BeltColor = new Color(0.18f, 0.18f, 0.2f);
        private static readonly Color EnemyBeltColor = new Color(0.3f, 0.2f, 0.2f);
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
        private static readonly Color VillagerSelectionColor = new Color(0.35f, 1f, 0.85f);
        private const int MaxCargoIcons = 24;
        private const float CargoIconDistance = 18f;
        private uint selectedBuildingId;
        private bool detailModeEnabled;
        private readonly List<GameObject> coreClearanceEdges = new List<GameObject>();
        private bool coreClearanceVisible;
        private long lastLineTraversalTick = long.MinValue;
        private uint lastLineTraversalBuilding;

        private static readonly Color LineHighlightColor = new Color(1f, 0.82f, 0.18f, 1f);

        public void Bind(BattlefieldView battlefield) { view = battlefield; }

        public IReadOnlyList<uint> SelectedVillagers { get { return selectedVillagers; } }
        public bool HasVillagerSelection { get { return selectedVillagers.Count > 0; } }
        public bool DetailModeEnabled { get { return detailModeEnabled; } }
        public EconomyFlowMetrics FlowMetrics { get { return flowMetrics; } }
        public uint SelectedBuildingId { get { return selectedBuildingId; } }

        public void ToggleDetailMode() { detailModeEnabled = CraftDisplayMode.Toggle(detailModeEnabled); }

        public void SelectBuilding(uint id)
        {
            selectedBuildingId = id;
            lastLineTraversalTick = long.MinValue;
        }

        public void ClearBuildingSelection()
        {
            if (selectedBuildingId == 0) return;
            selectedBuildingId = 0;
            lastLineTraversalTick = long.MinValue;
            highlightedBuildingIds.Clear();
            highlightedBeltCells.Clear();
        }

        public void ClearVillagerSelection()
        {
            selectedVillagers.Clear();
            ClearVillagerSelectionRings();
        }

        public void SetCoreClearanceVisible(bool visible)
        {
            coreClearanceVisible = visible;
            UpdateCoreClearance();
        }

        private void UpdateCoreClearance()
        {
            if (!coreClearanceVisible || view == null || view.LatestFrame == null || view.LatestFrame.Economy == null)
            {
                for (int i = 0; i < coreClearanceEdges.Count; i++) coreClearanceEdges[i].SetActive(false);
                return;
            }
            var frame = view.LatestFrame;
            var economy = frame.Economy;
            if (economy.CoreClearanceCells <= 0 || economy.MapWidthCells <= 0 || economy.CellSizeMeters <= 0)
            {
                for (int i = 0; i < coreClearanceEdges.Count; i++) coreClearanceEdges[i].SetActive(false);
                return;
            }
            int centerCell = -1;
            foreach (var objective in frame.Objectives)
            {
                if (objective.Kind != GoalKind.Core || !objective.IsOwnerKnown || objective.OwnerFactionId != frame.FactionId) continue;
                long cellRaw = (long)economy.CellSizeMeters * 65536L;
                int x = (int)(objective.Position.X.Raw / cellRaw);
                int z = (int)(objective.Position.Z.Raw / cellRaw);
                centerCell = z * economy.MapWidthCells + x;
                break;
            }
            var corners = CoreClearanceGeometry.CalculateCorners(centerCell, economy.CoreClearanceCells, economy.MapWidthCells);
            if (corners.Length != 4) return;
            while (coreClearanceEdges.Count < 4)
            {
                var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                edge.name = "Core clearance";
                Destroy(edge.GetComponent<Collider>());
                edge.transform.SetParent(transform, false);
                edge.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.86f, 0.64f, 0.25f, 0.42f));
                coreClearanceEdges.Add(edge);
            }
            float minX = corners[0].X * CellMeters, maxX = (corners[1].X + 1) * CellMeters;
            float minZ = corners[0].Z * CellMeters, maxZ = (corners[2].Z + 1) * CellMeters;
            float width = Mathf.Max(0.1f, maxX - minX), depth = Mathf.Max(0.1f, maxZ - minZ);
            Vector3[] positions =
            {
                new Vector3((minX + maxX) * 0.5f, 0.12f, minZ), new Vector3(maxX, 0.12f, (minZ + maxZ) * 0.5f),
                new Vector3((minX + maxX) * 0.5f, 0.12f, maxZ), new Vector3(minX, 0.12f, (minZ + maxZ) * 0.5f)
            };
            Vector3[] scales = { new Vector3(width, 0.08f, 0.08f), new Vector3(0.08f, 0.08f, depth), new Vector3(width, 0.08f, 0.08f), new Vector3(0.08f, 0.08f, depth) };
            Vector3 parentScale = transform.lossyScale;
            if (Mathf.Abs(parentScale.x) < 0.0001f) parentScale.x = 1f;
            if (Mathf.Abs(parentScale.y) < 0.0001f) parentScale.y = 1f;
            if (Mathf.Abs(parentScale.z) < 0.0001f) parentScale.z = 1f;
            for (int i = 0; i < coreClearanceEdges.Count; i++)
            {
                coreClearanceEdges[i].SetActive(true);
                coreClearanceEdges[i].transform.position = positions[i];
                coreClearanceEdges[i].transform.localScale = new Vector3(
                    scales[i].x / parentScale.x, scales[i].y / parentScale.y, scales[i].z / parentScale.z);
            }
        }

        public void SelectVillager(uint id, bool additive)
        {
            var next = VillagerControlRules.ApplyClick(selectedVillagers, id, additive);
            selectedVillagers.Clear();
            selectedVillagers.AddRange(next);
        }

        public void SelectVillagers(IReadOnlyList<uint> ids, bool additive)
        {
            if (!additive) selectedVillagers.Clear();
            for (int i = 0; i < ids.Count; i++)
            {
                uint id = ids[i];
                if (id != 0 && !selectedVillagers.Contains(id)) selectedVillagers.Add(id);
            }
            selectedVillagers.Sort();
        }

        public void SelectIdleVillagers()
        {
            var economy = view == null || view.LatestFrame == null ? null : view.LatestFrame.Economy;
            if (economy == null) return;
            selectedVillagers.Clear();
            foreach (var villager in economy.Villagers)
                if (villager.IsOwn && villager.Activity == VillagerActivity.Idle) selectedVillagers.Add(villager.Id);
            selectedVillagers.Sort();
        }

        public bool TryPickOwnVillager(Camera camera, Vector2 screenPoint, float radiusPixels, out uint id)
        {
            id = 0;
            float best = radiusPixels * radiusPixels;
            foreach (var pair in ownVillagers)
            {
                var projected = camera.WorldToScreenPoint(pair.Value.transform.position);
                if (projected.z <= 0f) continue;
                float distance = ((Vector2)projected - screenPoint).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                id = pair.Key;
            }
            return id != 0;
        }

        public List<uint> VillagersInScreenRect(Camera camera, Rect screenRect)
        {
            var found = new List<uint>();
            foreach (var pair in ownVillagers)
            {
                var projected = camera.WorldToScreenPoint(pair.Value.transform.position);
                if (projected.z > 0f && screenRect.Contains(new Vector2(projected.x, projected.y))) found.Add(pair.Key);
            }
            found.Sort();
            return found;
        }

        public bool TryPickResource(Camera camera, Vector2 screenPoint, float radiusPixels, out ResourceView resource)
        {
            resource = default(ResourceView);
            float best = radiusPixels * radiusPixels;
            var economy = view == null || view.LatestFrame == null ? null : view.LatestFrame.Economy;
            if (economy == null) return false;
            foreach (var candidate in economy.Resources)
            {
                if (!resources.TryGetValue(candidate.Id, out var visual)) continue;
                var projected = camera.WorldToScreenPoint(visual.transform.position);
                if (projected.z <= 0f) continue;
                float distance = ((Vector2)projected - screenPoint).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                resource = candidate;
            }
            return resource.Id != 0;
        }

        public bool TryPickOwnBuilding(Camera camera, Vector2 screenPoint, float radiusPixels, out BuildingView building)
        {
            building = default(BuildingView);
            float best = radiusPixels * radiusPixels;
            var economy = view == null || view.LatestFrame == null ? null : view.LatestFrame.Economy;
            if (economy == null) return false;
            uint ownFaction = view.LatestFrame.FactionId;
            foreach (var candidate in economy.Buildings)
            {
                if (candidate.FactionId != ownFaction || !buildings.TryGetValue(candidate.Id, out var visual)) continue;
                var projected = camera.WorldToScreenPoint(visual.transform.position);
                if (projected.z <= 0f) continue;
                float distance = ((Vector2)projected - screenPoint).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                building = candidate;
            }
            return building.Id != 0;
        }

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
            ClearVillagerSelectionRings();
        }

        public void Clear()
        {
            foreach (var go in resources.Values) { ResourceModels.Release(go); Destroy(go); }
            foreach (var go in ownVillagers.Values) Destroy(go);
            ClearVillagerSelectionRings();
            foreach (var go in enemyVillagers) Destroy(go);
            foreach (var animation in ownVillagerAnimations.Values) animation.Dispose();
            foreach (var animation in enemyVillagerAnimations) if (animation != null) animation.Dispose();
            foreach (var go in buildings.Values) Destroy(go);
            foreach (var bar in buildingHealthBars.Values) Destroy(bar.Root);
            foreach (var bar in buildingConstructionBars.Values) Destroy(bar.Root);
            foreach (var standard in buildingCivStandards.Values) Destroy(standard);
            foreach (var go in ports.Values) Destroy(go);
            ports.Clear();
            foreach (var go in buildingFlags.Values) Destroy(go);
            buildingFlags.Clear();
            foreach (var go in carryBoxes.Values) Destroy(go);
            carryBoxes.Clear();
            foreach (var go in belts.Values) Destroy(go);
            foreach (var go in items.Values) Destroy(go);
            foreach (var go in buildingStatusBubbles.Values) Destroy(go);
            foreach (var go in buildingActivityPuffs.Values) Destroy(go);
            foreach (var go in buildingDetailOverlays.Values) Destroy(go);
            foreach (var go in buildingLineHighlights.Values) Destroy(go);
            foreach (var go in coreClearanceEdges) Destroy(go);
            resources.Clear(); resourceLevels.Clear(); ownVillagers.Clear(); enemyVillagers.Clear(); buildings.Clear(); buildingHealthBars.Clear(); buildingConstructionBars.Clear(); buildingCivStandards.Clear(); villagerTargets.Clear();
            selectedVillagers.Clear();
            packedBuildings.Clear(); packedBuildingStages.Clear(); buildingFxStates.Clear();
            foreach (var fx in buildingEffects) Destroy(fx.Object);
            buildingEffects.Clear(); buildingFire.Clear();
            villagerFrom.Clear(); villagerFacing.Clear(); villagerWalkRates.Clear(); villagerActivities.Clear(); villagerWorkTargets.Clear();
            enemyVillagerFrom.Clear(); enemyVillagerTargets.Clear(); enemyVillagerFacing.Clear(); enemyVillagerWalkRates.Clear();
            enemyVillagerActivities.Clear(); enemyVillagerWorkTargets.Clear();
            lastVillagerFrameTick = long.MinValue;
            villagerInterpolationSeconds = 0f;
            ownVillagerAnimations.Clear(); enemyVillagerAnimations.Clear();
            packedBuildings.Clear();
            belts.Clear(); items.Clear(); itemKinds.Clear(); beltComponents.Clear(); beltSeen.Clear(); beltCarrying.Clear(); beltOwnCells.Clear(); beltEndCells.Clear(); beltGone.Clear();
            buildingStatusBubbles.Clear(); buildingActivityPuffs.Clear(); flowMetrics.Clear(); lastMetricTick = long.MinValue;
            buildingDetailOverlays.Clear(); buildingLineHighlights.Clear(); highlightedBuildingIds.Clear(); highlightedBeltCells.Clear();
            selectedBuildingId = 0; detailModeEnabled = false; lastLineTraversalTick = long.MinValue; lastLineTraversalBuilding = 0;
            coreClearanceVisible = false;
            coreClearanceEdges.Clear();
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
            if (economy == null)
            {
                if (detailModeEnabled || selectedBuildingId != 0 || resources.Count + ownVillagers.Count + buildings.Count + enemyVillagers.Count + belts.Count > 0) Clear();
                return;
            }
            if (!UiHitAreas.Shared.KeyboardCaptured && Input.GetKeyDown(KeyCode.LeftAlt)) ToggleDetailMode();
            long part = PerfProbe.Start();
            SyncResources(economy);
            PerfProbe.Stop("EcoRes", part);
            part = PerfProbe.Start();
            SyncVillagers(economy);
            PerfProbe.Stop("EcoVil", part);
            part = PerfProbe.Start();
            SyncBuildings(economy);
            UpdateCoreClearance();
            UpdateBuildingEffects(Time.deltaTime);
            UpdateBuildingStatusBillboards();
            PerfProbe.Stop("EcoBld", part);
            part = PerfProbe.Start();
            SyncBelts(economy);
            UpdateCargoIcons();
            PerfProbe.Stop("EcoBelt", part);
            float matchRate = view == null ? 0f : view.MatchRate;
            villagerInterpolationSeconds = Mathf.Min(BattlefieldView.TickSeconds,
                villagerInterpolationSeconds + Time.deltaTime * matchRate);
            float alpha = BattlefieldView.TickSeconds <= 0f ? 1f : villagerInterpolationSeconds / BattlefieldView.TickSeconds;
            foreach (var pair in ownVillagers)
                ApplyVillager(pair.Key, pair.Value, ownVillagerAnimations, alpha, matchRate);
            for (int i = 0; i < enemyVillagers.Count; i++)
                ApplyEnemyVillager(i, alpha, matchRate);
            SyncVillagerSelection(economy);
        }

        private void SyncVillagerSelection(EconomyView economy)
        {
            Color ringFactionColor = view == null || view.LatestFrame == null ? VillagerSelectionColor
                : FactionRingColor(view.LatestFrame.FactionId);
            var existing = new HashSet<uint>(ownVillagers.Keys);
            var present = VillagerControlRules.PruneMissing(selectedVillagers, existing);
            selectedVillagers.Clear();
            selectedVillagers.AddRange(present);

            var seen = new HashSet<uint>();
            for (int i = 0; i < selectedVillagers.Count; i++)
            {
                uint id = selectedVillagers[i];
                if (!ownVillagers.TryGetValue(id, out var villager)) continue;
                seen.Add(id);
                if (!villagerSelectionRings.TryGetValue(id, out var ring))
                {
                    ring = SelectionRingVisuals.Create(transform, "VillagerSelectionRing " + id, 0.62f, ringFactionColor);
                    villagerSelectionRings.Add(id, ring);
                }
                var position = villager.transform.position;
                ring.transform.position = new Vector3(position.x, 0.06f, position.z);
                ring.SetActive(true);
            }

            var gone = new List<uint>();
            foreach (var pair in villagerSelectionRings)
                if (!seen.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (uint id in gone)
            {
                Destroy(villagerSelectionRings[id]);
                villagerSelectionRings.Remove(id);
            }
        }

        private void ClearVillagerSelectionRings()
        {
            foreach (var ring in villagerSelectionRings.Values) Destroy(ring);
            villagerSelectionRings.Clear();
        }

        private void SyncResources(EconomyView economy)
        {
            var seen = new HashSet<uint>();
            foreach (var r in economy.Resources)
            {
                seen.Add(r.Id);
                if (!resources.TryGetValue(r.Id, out var go))
                {
                    go = new GameObject(r.Kind + " " + r.Id);
                    go.transform.SetParent(transform, false);
                    var p = ToWorld(r.Position);
                    go.transform.position = new Vector3(p.x, 0f, p.z);
                    resources.Add(r.Id, go);
                }
                // The model loses trees, boulders or bushes as the point is gathered; it is rebuilt only when the step
                // changes, and a nearly empty point still shows one piece.
                int level = ResourceModels.Level(r.Kind, r.Remaining);
                if (!resourceLevels.TryGetValue(r.Id, out var shown) || shown != level)
                {
                    ResourceModels.Build(go, r.Kind, r.Id, level);
                    resourceLevels[r.Id] = level;
                }
            }
            var spent = new List<uint>();
            foreach (var id in resources.Keys) if (!seen.Contains(id)) spent.Add(id);
            foreach (var id in spent)
            {
                ResourceModels.Release(resources[id]);
                Destroy(resources[id]);
                resources.Remove(id);
                resourceLevels.Remove(id);
            }
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
            RefreshLineHighlights(economy, ownFaction);
            foreach (var b in economy.Buildings)
            {
                seen.Add(b.Id);
                bool finished = b.Complete || b.MaxHp == 0;
                bool own = b.FactionId == ownFaction;
                int constructionStage = (int)LocalVisualPack.GetBuildingConstructionStage(b.Complete, b.Progress, b.Work);
                if (!buildings.TryGetValue(b.Id, out var go))
                {
                    bool created = false;
                    if (own && !finished && constructionStage < 2)
                        created = LocalVisualPack.TryCreateBuildingConstruction(b.Kind, constructionStage, b.FactionId, transform,
                            b.SizeMeters * LocalVisualPack.BuildingWidthScale(b.Kind), out go);
                    if (!created)
                        created = LocalVisualPack.TryCreateBuilding(b.Kind, b.FactionId, transform,
                            b.SizeMeters * LocalVisualPack.BuildingWidthScale(b.Kind), out go);
                    if (created)
                        packedBuildings.Add(b.Id);
                    else
                        go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    if (created && own && !finished && constructionStage < 2)
                        packedBuildingStages[b.Id] = constructionStage;
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
                int desiredPackedStage = finished ? 2 : constructionStage;
                int previousStage;
                bool hasPreviousStage = packedBuildingStages.TryGetValue(b.Id, out previousStage);
                bool shouldRecreate = packedBuildings.Contains(b.Id) && own &&
                    ((desiredPackedStage == 2 && hasPreviousStage) ||
                    (desiredPackedStage < 2 && (!hasPreviousStage || previousStage != desiredPackedStage)));
                if (shouldRecreate)
                {
                    Destroy(go);
                    packedBuildings.Remove(b.Id);
                    packedBuildingStages.Remove(b.Id);
                    bool recreated = desiredPackedStage == 2
                        ? LocalVisualPack.TryCreateBuilding(b.Kind, b.FactionId, transform,
                            b.SizeMeters * LocalVisualPack.BuildingWidthScale(b.Kind), out go)
                        : LocalVisualPack.TryCreateBuildingConstruction(b.Kind, desiredPackedStage, b.FactionId, transform,
                            b.SizeMeters * LocalVisualPack.BuildingWidthScale(b.Kind), out go);
                    if (recreated)
                    {
                        go.name = b.Kind + " " + b.Id;
                        buildings[b.Id] = go;
                        packedBuildings.Add(b.Id);
                        if (desiredPackedStage < 2) packedBuildingStages[b.Id] = desiredPackedStage;
                    }
                    else
                    {
                        packedBuildings.Add(b.Id);
                    }
                }
                float full = b.Kind == BuildingKind.Mine || b.Kind == BuildingKind.Drill ? 1.6f : b.Kind == BuildingKind.Smelter ? 2.4f : b.Kind == BuildingKind.Farm ? 0.8f
                    : b.Kind == BuildingKind.House ? 1.4f : b.Kind == BuildingKind.DropSite ? 1.0f
                    : b.Kind == BuildingKind.Wall ? 2.2f : b.Kind == BuildingKind.Tower ? 4.5f : b.Kind == BuildingKind.Blacksmith ? 2.0f
                    : b.Kind == BuildingKind.Market ? 1.8f : b.Kind == BuildingKind.SiegeWorkshop ? 2.6f
                    : b.Kind == BuildingKind.ArcheryRange ? 2.2f : b.Kind == BuildingKind.Stable ? 2.4f
                    : b.Kind == BuildingKind.Castle ? 6f : b.Kind == BuildingKind.DryingRack ? 1.8f : 3f;
                float height = finished ? full : 0.6f + (full - 0.6f) * (b.Work == 0 ? 0f : (float)b.Progress / b.Work);
                var color = b.FactionId == 1 ? WestColor : EastColor;
                if (!finished) color *= 0.55f;
                var p = ToWorld(b.Center);
                if (packedBuildings.Contains(b.Id))
                {
                    go.transform.position = new Vector3(p.x, 0f, p.z);
                    if (finished)
                    {
                        LocalVisualPack.SetBuildingProgress(go, 1f);
                        height = LocalVisualPack.BuildingHeight(b.Kind);
                    }
                    else if (packedBuildingStages.ContainsKey(b.Id))
                    {
                        // A construction-stage model (foundation or frame) is shown at its own size, not squashed.
                        LocalVisualPack.SetBuildingProgress(go, 1f);
                        height = LocalVisualPack.BuildingHeight(b.Kind) * (height / full);
                    }
                    else
                    {
                        float rise = height / full;
                        LocalVisualPack.SetBuildingProgress(go, rise);
                        height = LocalVisualPack.BuildingHeight(b.Kind) * rise;
                    }
                }
                else
                {
                    go.transform.position = new Vector3(p.x, height / 2f, p.z);
                    go.transform.localScale = new Vector3(b.SizeMeters, height, b.SizeMeters);
                    go.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(color);
                }
                if (b.MaxHp > 0)
                {
                    if (!buildingHealthBars.TryGetValue(b.Id, out var healthBar))
                    {
                        healthBar = WorldHealthBarVisuals.Create(transform, "BuildingHp_" + b.Id, 2.7f, 0.14f);
                        buildingHealthBars.Add(b.Id, healthBar);
                    }
                    WorldHealthBarVisuals.Set(healthBar, b.Hp, b.MaxHp,
                        new Vector3(p.x, height + 0.28f, p.z), Quaternion.identity);
                }
                else if (buildingHealthBars.TryGetValue(b.Id, out var noHealthBar))
                {
                    noHealthBar.Root.SetActive(false);
                }
                if (!b.Complete)
                {
                    if (!buildingConstructionBars.TryGetValue(b.Id, out var constructionBar))
                    {
                        constructionBar = WorldHealthBarVisuals.Create(transform, "BuildingConstruction_" + b.Id, 2.7f, 0.14f);
                        buildingConstructionBars.Add(b.Id, constructionBar);
                    }
                    WorldHealthBarVisuals.SetConstruction(constructionBar, UiInfoRules.ConstructionRatio(b.Progress, b.Work),
                        new Vector3(p.x, height + 0.58f, p.z), Quaternion.identity);
                }
                else if (buildingConstructionBars.TryGetValue(b.Id, out var finishedConstructionBar))
                {
                    finishedConstructionBar.Root.SetActive(false);
                }
                if (own && finished && economy.Civ != CivKind.Primitive && IsCivStandardBuilding(b.Kind)
                    && !buildingCivStandards.ContainsKey(b.Id))
                {
                    if (LocalVisualPack.TryCreateCivStandard(economy.Civ, b.FactionId, transform,
                        Mathf.Max(1.8f, height + 0.9f), Mathf.Max(1.0f, b.SizeMeters * 0.55f), out var standard))
                    {
                        standard.transform.position = new Vector3(p.x, 0f, p.z);
                        buildingCivStandards.Add(b.Id, standard);
                    }
                }
                if (own && b.MaxHp > 0)
                    SyncBuildingFire(b.Id, b.Hp, b.MaxHp, new Vector3(p.x, height, p.z));
                buildingFxStates[b.Id] = new BuildingFxState { Position = new Vector3(p.x, 0f, p.z), Height = height, Hp = b.Hp, MaxHp = b.MaxHp };
                buildingTags[b.Id] = new KeyValuePair<BuildingKind, Vector3>(b.Kind, new Vector3(p.x, height + 0.4f, p.z));
                SyncBuildingState(b, economy, own, new Vector3(p.x, height, p.z));
                CraftRecipe recipe = default(CraftRecipe);
                bool hasRecipe = own && CraftVisualRules.TryGetRecipe(b.Kind, out recipe);
                SetBuildingDetail(b.Id, hasRecipe, recipe, new Vector3(p.x, height, p.z));
                SetBuildingLineHighlight(b.Id, highlightedBuildingIds.Contains(b.Id), b.SizeMeters, new Vector3(p.x, height, p.z));
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
            var healthBarsGone = new List<uint>();
            foreach (var id in buildingHealthBars.Keys) if (!seen.Contains(id)) healthBarsGone.Add(id);
            foreach (var id in healthBarsGone)
            {
                Destroy(buildingHealthBars[id].Root);
                buildingHealthBars.Remove(id);
            }
            var constructionBarsGone = new List<uint>();
            foreach (var id in buildingConstructionBars.Keys) if (!seen.Contains(id)) constructionBarsGone.Add(id);
            foreach (var id in constructionBarsGone)
            {
                Destroy(buildingConstructionBars[id].Root);
                buildingConstructionBars.Remove(id);
            }
            var standardsGone = new List<uint>();
            foreach (var id in buildingCivStandards.Keys) if (!seen.Contains(id)) standardsGone.Add(id);
            foreach (var id in standardsGone)
            {
                Destroy(buildingCivStandards[id]);
                buildingCivStandards.Remove(id);
            }
            var packedGone = new List<uint>();
            foreach (var id in packedBuildings) if (!seen.Contains(id)) packedGone.Add(id);
            foreach (var id in packedGone) packedBuildings.Remove(id);
            foreach (var id in packedGone) packedBuildingStages.Remove(id);
            var fxGone = new List<uint>();
            foreach (var pair in buildingFxStates)
            {
                if (seen.Contains(pair.Key)) continue;
                if (pair.Value.MaxHp > 0 && pair.Value.Hp <= pair.Value.MaxHp * 0.5f)
                    SpawnBuildingEffect("FX_Building_Destroyed_mid.prefab", pair.Value.Position + Vector3.up * pair.Value.Height);
                fxGone.Add(pair.Key);
            }
            foreach (var id in fxGone) { buildingFxStates.Remove(id); RemoveBuildingFire(id); }
            Remove(buildingFlags, seen);
            Remove(ports, seen);
            Remove(buildingStatusBubbles, seen);
            Remove(buildingActivityPuffs, seen);
            Remove(buildingDetailOverlays, seen);
            Remove(buildingLineHighlights, seen);
            var untagged = new List<uint>();
            foreach (var id in buildingTags.Keys) if (!seen.Contains(id)) untagged.Add(id);
            foreach (var id in untagged) buildingTags.Remove(id);
        }

        private void RefreshLineHighlights(EconomyView economy, uint ownFaction)
        {
            if (selectedBuildingId == 0)
            {
                highlightedBuildingIds.Clear();
                highlightedBeltCells.Clear();
                lastLineTraversalTick = long.MinValue;
                return;
            }
            if (lastLineTraversalTick == view.LatestFrame.Tick && lastLineTraversalBuilding == selectedBuildingId) return;
            highlightedBuildingIds.Clear();
            highlightedBeltCells.Clear();
            BuildingView selected = default(BuildingView);
            bool found = false;
            foreach (var building in economy.Buildings)
                if (building.Id == selectedBuildingId && building.FactionId == ownFaction) { selected = building; found = true; break; }
            if (!found) { selectedBuildingId = 0; lastLineTraversalTick = long.MinValue; return; }
            CraftLineTraversal.FindBeltCellsForBuilding(selected, economy, MapWidthCells, MapHeightCells,
                highlightedBeltCells, highlightedBuildingIds, lineTraversalScratch);
            lastLineTraversalTick = view.LatestFrame.Tick;
            lastLineTraversalBuilding = selectedBuildingId;
        }

        private void SetBuildingDetail(uint id, bool shown, CraftRecipe recipe, Vector3 top)
        {
            if (!detailModeEnabled || !shown)
            {
                if (buildingDetailOverlays.TryGetValue(id, out var hidden)) hidden.SetActive(false);
                return;
            }
            if (!buildingDetailOverlays.TryGetValue(id, out var overlay))
            {
                overlay = new GameObject("Craft detail " + id);
                overlay.transform.SetParent(transform, false);
                CreateDetailIcon(overlay.transform, "Input", -0.55f, 0f);
                CreateDetailIcon(overlay.transform, "InputSecondary", -0.22f, 0f);
                var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arrow.name = "Arrow";
                Destroy(arrow.GetComponent<Collider>());
                arrow.transform.SetParent(overlay.transform, false);
                arrow.transform.localScale = new Vector3(0.22f, 0.08f, 0.08f);
                arrow.transform.localPosition = new Vector3(0.1f, 0f, 0f);
                arrow.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(1f, 0.85f, 0.25f));
                CreateDetailIcon(overlay.transform, "Output", 0.48f, 0f);
                buildingDetailOverlays.Add(id, overlay);
            }
            overlay.SetActive(true);
            overlay.transform.position = top + Vector3.up * 1.46f;
            var camera = Camera.main;
            if (camera != null) overlay.transform.rotation = Quaternion.LookRotation(camera.transform.position - overlay.transform.position, Vector3.up);
            SetDetailIcon(overlay.transform.Find("Input"), recipe.Input);
            SetDetailIcon(overlay.transform.Find("InputSecondary"), recipe.InputSecondary);
            SetDetailIcon(overlay.transform.Find("Output"), recipe.Output);
        }

        private static void CreateDetailIcon(Transform parent, string name, float x, float z)
        {
            var icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
            icon.name = name;
            Destroy(icon.GetComponent<Collider>());
            icon.transform.SetParent(parent, false);
            icon.transform.localPosition = new Vector3(x, 0f, z);
            icon.transform.localScale = Vector3.one * 0.38f;
        }

        private static void SetDetailIcon(Transform icon, ResourceKind kind)
        {
            if (icon == null) return;
            bool shown = kind != 0 && GoodsIcons.Get(kind) != null;
            icon.gameObject.SetActive(shown);
            if (shown) icon.GetComponent<Renderer>().sharedMaterial = GoodsIcons.GetMaterial(kind);
        }

        private void SetBuildingLineHighlight(uint id, bool shown, int sizeMeters, Vector3 top)
        {
            if (!shown)
            {
                if (buildingLineHighlights.TryGetValue(id, out var hidden)) hidden.SetActive(false);
                return;
            }
            if (!buildingLineHighlights.TryGetValue(id, out var outline))
            {
                outline = new GameObject("Line highlight " + id);
                outline.transform.SetParent(transform, false);
                CreateHighlightEdge(outline.transform, "North");
                CreateHighlightEdge(outline.transform, "South");
                CreateHighlightEdge(outline.transform, "East");
                CreateHighlightEdge(outline.transform, "West");
                buildingLineHighlights.Add(id, outline);
            }
            outline.SetActive(true);
            float size = Mathf.Max(1f, sizeMeters) + 0.22f;
            outline.transform.position = new Vector3(top.x, 0.18f, top.z);
            foreach (Transform edge in outline.transform)
            {
                bool horizontal = edge.name == "North" || edge.name == "South";
                edge.localScale = horizontal ? new Vector3(size, 0.08f, 0.08f) : new Vector3(0.08f, 0.08f, size);
                edge.localPosition = edge.name == "North" ? new Vector3(0f, 0f, size / 2f)
                    : edge.name == "South" ? new Vector3(0f, 0f, -size / 2f)
                    : edge.name == "East" ? new Vector3(size / 2f, 0f, 0f) : new Vector3(-size / 2f, 0f, 0f);
            }
        }

        private static void CreateHighlightEdge(Transform parent, string name)
        {
            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = name;
            Destroy(edge.GetComponent<Collider>());
            edge.transform.SetParent(parent, false);
            edge.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(LineHighlightColor);
        }

        private void SyncBuildingState(BuildingView building, EconomyView economy, bool own, Vector3 top)
        {
            if (!own)
            {
                SetBuildingStatusBubble(building.Id, null, top);
                SetBuildingActivityPuff(building.Id, false, top);
                return;
            }
            var state = CraftVisualRules.Evaluate(building, economy);
            Texture2D icon;
            if (building.Kind == BuildingKind.Drill)
            {
                ResourceKind resource = CraftVisualRules.DrillResource(building, economy);
                icon = building.Output >= CraftVisualRules.OutputBufferLimit || resource == 0
                    ? GoodsIcons.GetStatus(CraftBuildingState.OutputBlocked) : GoodsIcons.Get(resource);
            }
            else
                icon = state == CraftBuildingState.WaitingForMaterial
                    ? GoodsIcons.Get(CraftVisualRules.MissingInputKind(building.Kind))
                    : GoodsIcons.GetStatus(state);
            SetBuildingStatusBubble(building.Id, icon, top);
            SetBuildingActivityPuff(building.Id, state == CraftBuildingState.Working, top);
        }

        private static bool IsCivStandardBuilding(BuildingKind kind)
        {
            return kind == BuildingKind.Barracks || kind == BuildingKind.Market || kind == BuildingKind.Castle
                || kind == BuildingKind.Academy || kind == BuildingKind.Monastery || kind == BuildingKind.Town
                || kind == BuildingKind.Caravanserai || kind == BuildingKind.Tollgate;
        }

        private static Color FactionRingColor(uint faction)
        {
            var swatch = CivPalette.FactionSwatch(faction);
            return new Color(swatch.Red, swatch.Green, swatch.Blue, 1f);
        }

        private void SetBuildingStatusBubble(uint id, Texture2D icon, Vector3 top)
        {
            if (icon == null)
            {
                if (buildingStatusBubbles.TryGetValue(id, out var hidden)) hidden.SetActive(false);
                return;
            }
            if (!buildingStatusBubbles.TryGetValue(id, out var bubble))
            {
                bubble = GameObject.CreatePrimitive(PrimitiveType.Quad);
                bubble.name = "Craft status " + id;
                Destroy(bubble.GetComponent<Collider>());
                bubble.transform.SetParent(transform, false);
                bubble.transform.localScale = Vector3.one * 0.62f;
                buildingStatusBubbles.Add(id, bubble);
            }
            bubble.SetActive(true);
            bubble.transform.position = top + Vector3.up * 1.02f;
            bubble.GetComponent<Renderer>().sharedMaterial = GoodsIcons.GetMaterial(icon);
        }

        private void SetBuildingActivityPuff(uint id, bool shown, Vector3 top)
        {
            if (!shown)
            {
                if (buildingActivityPuffs.TryGetValue(id, out var hidden)) hidden.SetActive(false);
                return;
            }
            if (!buildingActivityPuffs.TryGetValue(id, out var puff))
            {
                puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = "Craft steam " + id;
                Destroy(puff.GetComponent<Collider>());
                puff.transform.SetParent(transform, false);
                puff.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.82f, 0.86f, 0.88f, 0.75f));
                buildingActivityPuffs.Add(id, puff);
            }
            puff.SetActive(true);
            float phase = (id % 17) * 0.31f;
            float wave = (Mathf.Sin(Time.time * 2.1f + phase) + 1f) * 0.5f;
            puff.transform.position = top + Vector3.up * (0.28f + wave * 0.42f);
            float scale = 0.12f + wave * 0.1f;
            puff.transform.localScale = Vector3.one * scale;
        }

        private void UpdateBuildingStatusBillboards()
        {
            var camera = Camera.main;
            if (camera == null) return;
            foreach (var bubble in buildingStatusBubbles.Values)
            {
                if (!bubble.activeSelf) continue;
                bubble.transform.rotation = Quaternion.LookRotation(camera.transform.position - bubble.transform.position, Vector3.up);
            }
            foreach (var overlay in buildingDetailOverlays.Values)
            {
                if (!overlay.activeSelf) continue;
                overlay.transform.rotation = Quaternion.LookRotation(camera.transform.position - overlay.transform.position, Vector3.up);
            }
            foreach (var bar in buildingHealthBars.Values)
            {
                if (bar.Root.activeSelf) bar.Root.transform.rotation = camera.transform.rotation;
            }
            foreach (var bar in buildingConstructionBars.Values)
            {
                if (bar.Root.activeSelf) bar.Root.transform.rotation = camera.transform.rotation;
            }
        }

        private void SyncBuildingFire(uint id, int hp, int maxHp, Vector3 position)
        {
            string file = hp <= maxHp * 0.25f ? "FX_Building_burning.prefab" : hp <= maxHp * 0.5f ? "FX_Building_burning_small.prefab" : null;
            if (file == null) { RemoveBuildingFire(id); return; }
            if (buildingFire.TryGetValue(id, out var current))
            {
                if (current.Object != null && current.File == file)
                {
                    current.Object.transform.position = position;
                    return;
                }
                if (current.Object != null) RemoveBuildingFire(id);
            }
            if (buildingFire.Count + buildingEffects.Count >= MaxBuildingEffects) return;
            if (!LocalVisualPack.TryCreateEffect(file, transform, out var effect)) return;
            effect.transform.position = position;
            var fx = new TimedEffect { Object = effect, Remaining = float.PositiveInfinity, File = file };
            buildingFire[id] = fx;
            buildingEffects.Add(fx);
        }

        private void RemoveBuildingFire(uint id)
        {
            if (!buildingFire.TryGetValue(id, out var fx)) return;
            Destroy(fx.Object);
            buildingEffects.Remove(fx);
            buildingFire.Remove(id);
        }

        private void SpawnBuildingEffect(string file, Vector3 position)
        {
            if (buildingEffects.Count >= MaxBuildingEffects) return;
            if (!LocalVisualPack.TryCreateEffect(file, transform, out var effect)) return;
            effect.transform.position = position;
            buildingEffects.Add(new TimedEffect { Object = effect, Remaining = 3f });
        }

        private void UpdateBuildingEffects(float deltaTime)
        {
            for (int i = buildingEffects.Count - 1; i >= 0; i--)
            {
                var fx = buildingEffects[i];
                if (float.IsPositiveInfinity(fx.Remaining)) continue;
                fx.Remaining -= deltaTime;
                if (fx.Remaining > 0f) continue;
                Destroy(fx.Object);
                buildingEffects.RemoveAt(i);
            }
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
            }
        }

        /// <summary>Short display names for the name tags (display only).</summary>
        public static string BuildingName(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Barracks: return UiText.T("Barracks", "兵舎");
                case BuildingKind.Mine: return UiText.T("Mine", "鉱山");
                case BuildingKind.Drill: return UiText.T("Drill", "採掘機");
                case BuildingKind.Smelter: return UiText.T("Smelter", "製錬所");
                case BuildingKind.Mill: return UiText.T("Mill", "製粉所");
                case BuildingKind.BrickKiln: return UiText.T("Brick kiln", "煉瓦窯");
                case BuildingKind.Saddlery: return UiText.T("Saddlery", "馬具工房");
                case BuildingKind.Sawmill: return UiText.T("Sawmill", "製材所");
                case BuildingKind.Scriptorium: return UiText.T("Scriptorium", "写本所");
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
                case BuildingKind.DryingRack: return UiText.T("Drying rack", "干し場");
                case BuildingKind.Lapidary: return UiText.T("Lapidary", "研磨所");
                case BuildingKind.Weaver: return UiText.T("Weaver", "織物工房");
                case BuildingKind.IncenseHouse: return UiText.T("Incense house", "香房");
                case BuildingKind.CharmWorkshop: return UiText.T("Charm workshop", "護符工房");
                case BuildingKind.RepairYard: return UiText.T("Repair yard", "修繕所");
                case BuildingKind.ToolShop: return UiText.T("Tool shop", "工具工房");
                case BuildingKind.Stonecutter: return UiText.T("Stonecutter", "石工場");
                case BuildingKind.Joinery: return UiText.T("Joinery", "木工所");
                 case BuildingKind.Assembler: return UiText.T("Assembler", "組立所");
                default: return kind.ToString();
            }
        }

        private void SyncBelts(EconomyView economy)
        {
            beltSeen.Clear();
            beltCarrying.Clear();
            beltOwnCells.Clear();
            beltEndCells.Clear();
            uint ownFaction = view.LatestFrame.FactionId;
            foreach (var belt in economy.Belts) if (belt.FactionId == ownFaction) beltOwnCells.Add(belt.Cell);
            foreach (var b in economy.Belts)
            {
                beltSeen.Add(b.Cell);
                int ticks = b.Speed == BeltSpeed.Fast && economy.FastBeltTicksPerCell > 0 ? economy.FastBeltTicksPerCell : Mathf.Max(1, economy.BeltTicksPerCell);
                if (b.FactionId == ownFaction && !beltOwnCells.Contains(NextCell(b.Cell, b.Facing))) beltEndCells.Add(b.Cell);
                var centre = CellCenter(b.Cell);
                var dir = Direction(b.Facing);
                if (!belts.TryGetValue(b.Cell, out var plate))
                {
                    plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    plate.name = "Belt " + b.Cell;
                    Destroy(plate.GetComponent<Collider>());
                    plate.transform.SetParent(transform, false);
                    plate.transform.localScale = new Vector3(1.8f, 0.1f, 1.8f);
                    plate.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(Color.white);
                    // The moving chevrons lie on a quad: its v axis runs along the plate's forward (the belt's facing),
                    // which a cube's top face does not guarantee. Child sizes are in the plate's scaled units.
                    var stripe = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    stripe.name = "Stripe";
                    Destroy(stripe.GetComponent<Collider>());
                    stripe.transform.SetParent(plate.transform, false);
                    stripe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    stripe.transform.localScale = new Vector3(0.72f, 1f, 1f);
                    stripe.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                    belts.Add(b.Cell, plate);
                }
                bool stalled = b.FactionId == ownFaction && b.Item != 0 && b.Progress >= ticks * 2;
                bool lineHighlighted = highlightedBeltCells.Contains(b.Cell);
                Color baseColor = lineHighlighted ? LineHighlightColor : b.FactionId != ownFaction ? EnemyBeltColor : b.PlayerHeld ? HeldBeltColor : BeltColor;
                var plateRenderer = plate.GetComponent<Renderer>();
                var stripeTransform = plate.transform.Find("Stripe");
                if (stripeTransform != null) stripeTransform.GetComponent<Renderer>().sharedMaterial = BeltVisualMaterials.Get(b.Speed, stalled);
                if (beltPropertyBlock == null) beltPropertyBlock = new MaterialPropertyBlock();
                beltPropertyBlock.Clear();
                Color beltColor = stalled ? new Color(1f, 0.24f, 0.18f) : baseColor;
                beltPropertyBlock.SetColor("_BaseColor", beltColor);
                beltPropertyBlock.SetColor("_Color", beltColor);
                plateRenderer.SetPropertyBlock(beltPropertyBlock);
                EnsureBeltAccent(plate, b.Component);
                plate.transform.position = new Vector3(centre.x, 0.05f, centre.z);
                plate.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                if (b.Item == 0) continue;
                beltCarrying.Add(b.Cell);
                if (!items.TryGetValue(b.Cell, out var item))
                {
                    item = new GameObject("Item " + b.Cell);
                    item.transform.SetParent(transform, false);
                    items.Add(b.Cell, item);
                }
                if (!itemKinds.TryGetValue(b.Cell, out var oldKind) || oldKind != b.Item)
                {
                    BuildCargoVisual(item, b.Item);
                    itemKinds[b.Cell] = b.Item;
                }
                // From the edge it came in at to the edge it leaves by, as its ticks on this cell run.
                float along = Mathf.Clamp01((float)b.Progress / ticks) - 0.5f;
                item.transform.position = new Vector3(centre.x, 0.5f, centre.z) + dir * (along * CellMeters);
                item.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
            beltGone.Clear();
            foreach (var cell in belts.Keys) if (!beltSeen.Contains(cell)) beltGone.Add(cell);
            foreach (var cell in beltGone) { Destroy(belts[cell]); belts.Remove(cell); beltComponents.Remove(cell); }
            beltGone.Clear();
            foreach (var cell in items.Keys) if (!beltCarrying.Contains(cell)) beltGone.Add(cell);
            foreach (var cell in beltGone) { Destroy(items[cell]); items.Remove(cell); itemKinds.Remove(cell); }
            BeltVisualMaterials.Tick(Time.time);
            if (view.LatestFrame.Tick != lastMetricTick)
            {
                lastMetricTick = view.LatestFrame.Tick;
                flowMetrics.ObserveBuildings(lastMetricTick, economy.Buildings, ownFaction);
                flowMetrics.ObserveBeltEnds(lastMetricTick, economy.Belts, beltEndCells, ownFaction);
            }
        }

        private void EnsureBeltAccent(GameObject plate, BeltComponentKind component)
        {
            int cell = ParseCell(plate.name);
            if (beltComponents.TryGetValue(cell, out var old))
            {
                if (old == component) return;
            }
            foreach (Transform child in plate.transform)
                if (child.name != "Stripe") Destroy(child.gameObject);
            CreateBeltEdge(plate.transform, "Belt edge L", -0.48f);
            CreateBeltEdge(plate.transform, "Belt edge R", 0.48f);
            if (component != BeltComponentKind.None)
            {
                var part = GameObject.CreatePrimitive(component == BeltComponentKind.Splitter ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                part.name = "Belt component";
                Destroy(part.GetComponent<Collider>());
                part.transform.SetParent(plate.transform, false);
                // In the plate's scaled units (y is 0.1): stands 0.35 m / 0.28 m on top of the plate.
                part.transform.localPosition = new Vector3(0f, component == BeltComponentKind.Splitter ? 2.25f : 1.9f, 0f);
                part.transform.localScale = component == BeltComponentKind.Splitter ? new Vector3(0.5f, 3.5f, 0.5f) : new Vector3(0.5f, 2.8f, 0.5f);
                part.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(
                    component == BeltComponentKind.Sorter ? new Color(0.75f, 0.35f, 0.95f)
                    : component == BeltComponentKind.Splitter ? new Color(0.95f, 0.75f, 0.2f)
                    : new Color(0.3f, 0.35f, 0.45f));
            }
            beltComponents[cell] = component;
        }

        private static void CreateBeltEdge(Transform parent, string name, float x)
        {
            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = name;
            Destroy(edge.GetComponent<Collider>());
            edge.transform.SetParent(parent, false);
            // In the plate's scaled units (1.8 x 0.1 x 1.8): the rails sit on the plate's sides, one cell long.
            edge.transform.localPosition = new Vector3(x, 0.9f, 0f);
            edge.transform.localScale = new Vector3(0.04f, 1.8f, 1f);
            edge.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.08f, 0.08f, 0.1f));
        }

        private static int ParseCell(string name)
        {
            int split = name.LastIndexOf(' ');
            return split < 0 ? -1 : int.TryParse(name.Substring(split + 1), out var cell) ? cell : -1;
        }

        private static void BuildCargoVisual(GameObject item, ResourceKind kind)
        {
            for (int i = item.transform.childCount - 1; i >= 0; i--)
                if (item.transform.GetChild(i).name != "CargoIcon") Destroy(item.transform.GetChild(i).gameObject);
            var visual = GoodsCargoVisuals.Get(kind);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Cargo " + visual.Shape;
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(item.transform, false);
            body.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(visual.Color);
            switch (visual.Shape)
            {
                case CargoShape.Log:
                    body.transform.localScale = new Vector3(0.42f, 0.42f, 0.9f);
                    body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    break;
                case CargoShape.RoughStone:
                    body = ReplaceCargoPrimitive(body, PrimitiveType.Sphere, new Vector3(0.72f, 0.58f, 0.68f));
                    break;
                case CargoShape.Ingot:
                    body.transform.localScale = new Vector3(0.46f, 0.28f, 0.86f);
                    break;
                case CargoShape.StoneBlock:
                case CargoShape.Crate:
                case CargoShape.Book:
                case CargoShape.RepairKit:
                    body.transform.localScale = visual.Shape == CargoShape.Book ? new Vector3(0.62f, 0.18f, 0.78f) : new Vector3(0.66f, 0.5f, 0.66f);
                    break;
                case CargoShape.Gem:
                    body = ReplaceCargoPrimitive(body, PrimitiveType.Cylinder, new Vector3(0.54f, 0.45f, 0.54f));
                    body.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    break;
                case CargoShape.Coin:
                    body = ReplaceCargoPrimitive(body, PrimitiveType.Cylinder, new Vector3(0.62f, 0.14f, 0.62f));
                    break;
                case CargoShape.Sack:
                case CargoShape.Charcoal:
                case CargoShape.Jar:
                case CargoShape.Charm:
                    body = ReplaceCargoPrimitive(body, visual.Shape == CargoShape.Sack ? PrimitiveType.Capsule : PrimitiveType.Sphere,
                        visual.Shape == CargoShape.Sack ? new Vector3(0.5f, 0.62f, 0.5f) : new Vector3(0.52f, 0.56f, 0.52f));
                    break;
                case CargoShape.Bow:
                    body = ReplaceCargoPrimitive(body, PrimitiveType.Capsule, new Vector3(0.22f, 0.65f, 0.22f));
                    body.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    break;
                case CargoShape.Tool:
                    body = ReplaceCargoPrimitive(body, PrimitiveType.Cylinder, new Vector3(0.2f, 0.65f, 0.2f));
                    body.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
                    break;
            }
            body.transform.localPosition = Vector3.zero;
            EnsureCargoIcon(item, kind);
        }

        private static GameObject ReplaceCargoPrimitive(GameObject body, PrimitiveType type, Vector3 scale)
        {
            var parent = body.transform.parent;
            var material = body.GetComponent<Renderer>().sharedMaterial;
            Destroy(body);
            body = GameObject.CreatePrimitive(type);
            body.name = "Cargo shape";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(parent, false);
            body.transform.localScale = scale;
            body.GetComponent<Renderer>().sharedMaterial = material;
            return body;
        }

        private static void EnsureCargoIcon(GameObject item, ResourceKind kind)
        {
            var icon = item.transform.Find("CargoIcon");
            if (icon == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "CargoIcon";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(item.transform, false);
                go.transform.localScale = Vector3.one * 0.3f;
                icon = go.transform;
            }
            icon.GetComponent<Renderer>().sharedMaterial = GoodsIcons.GetMaterial(kind);
            icon.gameObject.SetActive(GoodsIcons.Get(kind) != null);
        }

        private void UpdateCargoIcons()
        {
            var camera = Camera.main;
            if (camera == null) return;
            int shown = 0;
            float maxDistance = CargoIconDistance * CargoIconDistance;
            foreach (var item in items)
            {
                var icon = item.Value.transform.Find("CargoIcon");
                if (icon == null) continue;
                bool visible = shown < MaxCargoIcons && (detailModeEnabled || (camera.transform.position - item.Value.transform.position).sqrMagnitude <= maxDistance);
                icon.gameObject.SetActive(visible && icon.GetComponent<Renderer>().sharedMaterial != null);
                if (!visible) continue;
                shown++;
                icon.position = item.Value.transform.position + Vector3.up * 0.48f;
                icon.rotation = Quaternion.LookRotation(camera.transform.position - icon.position, Vector3.up);
            }
        }

        private static int NextCell(int cell, Facing facing)
        {
            int x = cell % MapWidthCells, z = cell / MapWidthCells;
            if (facing == Facing.North) z++; else if (facing == Facing.South) z--; else if (facing == Facing.East) x++; else x--;
            return x < 0 || z < 0 || x >= MapWidthCells || z >= MapHeightCells ? -1 : z * MapWidthCells + x;
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
