using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>Read-only display of FactionFrame. Interpolates S(t-1) to S(t) over one tick; never writes simulation state.</summary>
    public sealed class BattlefieldView : MonoBehaviour
    {
        public const float TickSeconds = 0.05f;
        private const float Fix64Scale = 65536f;
        private const float UnitModelScale = 2.2f;
        private const float CoreModelScale = 1.2f;

        [SerializeField] private int coreMaxHp = 3000;
        [SerializeField] private GameObject infantryModel;
        [SerializeField] private GameObject scoutModel;
        [SerializeField] private GameObject coreModel;

        private sealed class Visual
        {
            public GameObject Object;
            public Vector3 From;
            public Vector3 To;
            public Transform HpFill;
            public int Hp;
        }

        private readonly Dictionary<uint, Visual> units = new Dictionary<uint, Visual>();
        private readonly Dictionary<uint, Visual> cores = new Dictionary<uint, Visual>();
        private readonly List<uint> scratch = new List<uint>();
        private readonly Dictionary<uint, Vector3> previousUnitPositions = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, Vector3> previousCorePositions = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, Visual> armies = new Dictionary<uint, Visual>();
        private readonly Dictionary<uint, Vector3> previousArmyPositions = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, int> armyAlive = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> coreHp = new Dictionary<uint, int>();
        private readonly Dictionary<uint, Visual> outposts = new Dictionary<uint, Visual>();
        private GameObject selectionRing;
        private FactionFrame latestFrame;
        private sealed class Arrow { public LineRenderer Line; public uint ArmyId; public Vector3 Goal; }
        private readonly Dictionary<ulong, Arrow> arrows = new Dictionary<ulong, Arrow>();

        public FactionFrame LatestFrame { get { return latestFrame; } }
        private GameObject terrainObject;
        private SelectionTarget selected = SelectionTarget.None;
        private float sinceUpdate;

        public SelectionTarget Selected { get { return selected; } }

        public void Select(SelectionTarget target)
        {
            selected = target;
            UpdateSelectionRing();
        }

        public bool TryPick(Camera camera, Vector2 screenPoint, float radiusPixels, out SelectionTarget target)
        {
            target = SelectionTarget.None;
            float best = radiusPixels * radiusPixels;
            foreach (var pair in armies) Consider(camera, screenPoint, pair.Value, SelectionKind.Army, pair.Key, ref best, ref target);
            foreach (var pair in cores) Consider(camera, screenPoint, pair.Value, SelectionKind.Core, pair.Key, ref best, ref target);
            foreach (var pair in outposts) Consider(camera, screenPoint, pair.Value, SelectionKind.Outpost, pair.Key, ref best, ref target);
            return target.Kind != SelectionKind.None;
        }

        public string DescribeSelection()
        {
            switch (selected.Kind)
            {
                case SelectionKind.Army:
                    return "Selected: Army " + selected.Id + (armyAlive.TryGetValue(selected.Id, out var alive) ? " (alive " + alive + ")" : "");
                case SelectionKind.Outpost:
                    return "Selected: Outpost " + selected.Id;
                case SelectionKind.Core:
                    return "Selected: Core " + selected.Id + (coreHp.TryGetValue(selected.Id, out var hp) ? " (HP " + hp + ")" : "");
                default:
                    return "";
            }
        }

        private static void Consider(Camera camera, Vector2 screenPoint, Visual visual, SelectionKind kind, uint id, ref float best, ref SelectionTarget target)
        {
            var projected = camera.WorldToScreenPoint(visual.Object.transform.position);
            if (projected.z <= 0f) return;
            float distance = ((Vector2)projected - screenPoint).sqrMagnitude;
            if (distance > best) return;
            best = distance;
            target = new SelectionTarget(kind, id);
        }

        private void UpdateSelectionRing()
        {
            if (selected.Kind == SelectionKind.None)
            {
                if (selectionRing != null) selectionRing.SetActive(false);
                return;
            }
            if (selectionRing == null)
            {
                selectionRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                selectionRing.name = "SelectionRing";
                selectionRing.transform.SetParent(transform, false);
                Discard(selectionRing.GetComponent<Collider>());
                selectionRing.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(1f, 0.9f, 0.2f));
            }
            selectionRing.SetActive(true);
            bool isCore = selected.Kind == SelectionKind.Core;
            selectionRing.transform.localScale = isCore ? new Vector3(12f, 0.05f, 12f) : new Vector3(6f, 0.05f, 6f);
            PlaceSelectionRing();
        }

        private void PlaceSelectionRing()
        {
            if (selectionRing == null || selected.Kind == SelectionKind.None) return;
            var map = selected.Kind == SelectionKind.Core ? cores : selected.Kind == SelectionKind.Outpost ? outposts : armies;
            if (!map.TryGetValue(selected.Id, out var visual)) { selectionRing.SetActive(false); return; }
            selectionRing.SetActive(true);
            var position = visual.Object.transform.position;
            selectionRing.transform.position = new Vector3(position.x, 0.05f, position.z);
        }

        public void Push(FactionFrame frame)
        {
            sinceUpdate = 0f;
            SyncUnits(frame);
            SyncCores(frame);
            SyncArmies(frame);
            SyncOutposts(frame);
            latestFrame = frame;
            SyncArrows(frame);
            Apply(0f);
        }

        public void Apply(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            foreach (var visual in units.Values)
                visual.Object.transform.position = Vector3.Lerp(visual.From, visual.To, alpha);
            foreach (var visual in cores.Values)
                visual.Object.transform.position = Vector3.Lerp(visual.From, visual.To, alpha);
            foreach (var visual in armies.Values)
                visual.Object.transform.position = Vector3.Lerp(visual.From, visual.To, alpha);
            foreach (var arrow in arrows.Values)
            {
                if (!armies.TryGetValue(arrow.ArmyId, out var army)) { arrow.Line.enabled = false; continue; }
                arrow.Line.enabled = true;
                var start = army.Object.transform.position;
                arrow.Line.SetPosition(0, new Vector3(start.x, 1.2f, start.z));
                arrow.Line.SetPosition(1, arrow.Goal);
            }
            PlaceSelectionRing();
            var camera = Camera.main;
            if (camera == null) return;
            foreach (var visual in cores.Values)
                visual.HpFill.parent.rotation = camera.transform.rotation;
            FaceCamera(camera);
        }

        private void Update()
        {
            sinceUpdate += Time.deltaTime;
            Apply(sinceUpdate / TickSeconds);
        }

        private void SyncUnits(FactionFrame frame)
        {
            var present = new HashSet<uint>();
            foreach (var unit in frame.Units)
            {
                present.Add(unit.Id);
                var target = ToWorld(unit.Position, ModelFor(unit.Kind) != null ? 0f : 1.1f);
                if (!units.TryGetValue(unit.Id, out var visual))
                {
                    visual = new Visual { Object = CreateUnitObject(unit), From = target };
                    units.Add(unit.Id, visual);
                }
                else
                {
                    visual.From = visual.Object.transform.position;
                    if (previousUnitPositions.TryGetValue(unit.Id, out var previous)) visual.From = previous;
                }
                visual.To = target;
            }

            scratch.Clear();
            foreach (var pair in units)
                if (!present.Contains(pair.Key)) scratch.Add(pair.Key);
            foreach (var id in scratch)
            {
                Discard(units[id].Object);
                units.Remove(id);
            }

            previousUnitPositions.Clear();
            foreach (var pair in units) previousUnitPositions[pair.Key] = pair.Value.To;
        }

        private void SyncCores(FactionFrame frame)
        {
            foreach (var objective in frame.Objectives)
            {
                if (objective.Kind != GoalKind.Core) continue;
                var target = ToWorld(objective.Position, coreModel != null ? 0f : 2f);
                if (!cores.TryGetValue(objective.Id, out var visual))
                {
                    visual = CreateCoreObject(objective);
                    visual.From = target;
                    cores.Add(objective.Id, visual);
                }
                else
                {
                    visual.From = previousCorePositions.TryGetValue(objective.Id, out var previous) ? previous : target;
                }
                visual.To = target;
                coreHp[objective.Id] = objective.IsHpKnown ? objective.Hp : coreMaxHp;
                UpdateHpBar(visual, objective.IsHpKnown ? objective.Hp : coreMaxHp);
            }

            previousCorePositions.Clear();
            foreach (var pair in cores) previousCorePositions[pair.Key] = pair.Value.To;
        }

        private void SyncArmies(FactionFrame frame)
        {
            var present = new HashSet<uint>();
            foreach (var army in frame.Observation.OwnArmies)
            {
                present.Add(army.Id);
                var target = ToWorld(army.Position, 6f);
                if (!armies.TryGetValue(army.Id, out var visual))
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.name = "Army_" + army.Id;
                    marker.transform.SetParent(transform, false);
                    marker.transform.localScale = new Vector3(2f, 2f, 2f);
                    marker.transform.rotation = Quaternion.Euler(45f, 45f, 0f);
                    Discard(marker.GetComponent<Collider>());
                    marker.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(ArmyPalette[(int)((army.Id + 3) % 4)]);
                    visual = new Visual { Object = marker, From = target };
                    armies.Add(army.Id, visual);
                }
                else
                {
                    visual.From = previousArmyPositions.TryGetValue(army.Id, out var previous) ? previous : target;
                }
                visual.To = target;
                armyAlive[army.Id] = army.AliveCount;
            }

            scratch.Clear();
            foreach (var pair in armies)
                if (!present.Contains(pair.Key)) scratch.Add(pair.Key);
            foreach (var id in scratch)
            {
                Discard(armies[id].Object);
                armies.Remove(id);
                armyAlive.Remove(id);
            }

            previousArmyPositions.Clear();
            foreach (var pair in armies) previousArmyPositions[pair.Key] = pair.Value.To;
        }

        public void SetTerrain(TerrainMap terrain)
        {
            var existing = transform.Find("Terrain");
            if (existing != null) Discard(existing.gameObject);
            var texture = new Texture2D(terrain.WidthCells, terrain.HeightCells, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var open = new Color(0.42f, 0.6f, 0.36f);
            var wall = new Color(0.16f, 0.2f, 0.16f);
            var pixels = new Color[terrain.WidthCells * terrain.HeightCells];
            for (int z = 0; z < terrain.HeightCells; z++)
                for (int x = 0; x < terrain.WidthCells; x++)
                    pixels[z * terrain.WidthCells + x] = terrain.IsBlocked(x, z) ? wall : open;
            texture.SetPixels(pixels);
            texture.Apply();

            var terrainObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            terrainObject.name = "Terrain";
            terrainObject.transform.SetParent(transform, false);
            float width = terrain.WidthCells * terrain.CellSizeMeters;
            float height = terrain.HeightCells * terrain.CellSizeMeters;
            terrainObject.transform.position = new Vector3(width / 2f, 0.02f, height / 2f);
            terrainObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            terrainObject.transform.localScale = new Vector3(width, height, 1f);
            Discard(terrainObject.GetComponent<Collider>());
            terrainObject.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.NewUnlitTextured(texture);
        }

        private static readonly Color[] ArmyPalette =
        {
            new Color(0.4f, 0.9f, 1f), new Color(1f, 0.75f, 0.2f), new Color(0.75f, 0.5f, 1f), new Color(0.6f, 1f, 0.4f)
        };

        private static Color FactionColor(uint faction)
        {
            if (faction == 1) return new Color(0.2f, 0.45f, 0.95f);
            if (faction == 2) return new Color(0.9f, 0.25f, 0.2f);
            return new Color(0.65f, 0.65f, 0.65f);
        }

        private void SyncOutposts(FactionFrame frame)
        {
            foreach (var objective in frame.Objectives)
            {
                if (objective.Kind != GoalKind.Outpost) continue;
                var target = ToWorld(objective.Position, 0.4f);
                if (!outposts.TryGetValue(objective.Id, out var visual))
                {
                    var root = new GameObject("Outpost_" + objective.Id);
                    root.transform.SetParent(transform, false);
                    var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    body.transform.SetParent(root.transform, false);
                    body.transform.localScale = new Vector3(9f, 0.4f, 9f);
                    Discard(body.GetComponent<Collider>());
                    var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pole.name = "Pole";
                    pole.transform.SetParent(root.transform, false);
                    pole.transform.localPosition = new Vector3(0f, 2f, 0f);
                    pole.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
                    Discard(pole.GetComponent<Collider>());
                    var bar = new GameObject("CaptureGauge");
                    bar.transform.SetParent(root.transform, false);
                    bar.transform.localPosition = new Vector3(0f, 6f, 0f);
                    var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    back.name = "Back";
                    back.transform.SetParent(bar.transform, false);
                    back.transform.localScale = new Vector3(8.4f, 1.6f, 1f);
                    Discard(back.GetComponent<Collider>());
                    back.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.08f, 0.08f, 0.08f));
                    var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    fill.name = "Fill";
                    fill.transform.SetParent(bar.transform, false);
                    Discard(fill.GetComponent<Collider>());
                    visual = new Visual { Object = root, HpFill = fill.transform, From = target };
                    outposts.Add(objective.Id, visual);
                }
                visual.To = target;
                visual.From = target;
                visual.Object.transform.position = target;
                uint owner = objective.IsOwnerKnown ? objective.OwnerFactionId : 0u;
                var ownerMaterial = PresentationMaterials.Get(FactionColor(owner));
                foreach (var renderer in visual.Object.GetComponentsInChildren<Renderer>())
                    if (renderer.gameObject.name != "Back" && renderer.gameObject.name != "Fill") renderer.sharedMaterial = ownerMaterial;

                float ratio = objective.CaptureDurationTicks > 0 ? Mathf.Clamp01(objective.CaptureTicks / (float)objective.CaptureDurationTicks) : 0f;
                const float width = 8f;
                visual.HpFill.gameObject.SetActive(ratio > 0f);
                visual.HpFill.localScale = new Vector3(width * ratio, 1.2f, 1f);
                visual.HpFill.localPosition = new Vector3(-width * (1f - ratio) / 2f, 0f, -0.01f);
                visual.HpFill.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(FactionColor(objective.CapturingFactionId));
            }
        }

        private bool TryGoalPosition(PolicyGoal goal, FactionFrame frame, out Vector3 position)
        {
            position = default(Vector3);
            if (goal.Kind == GoalKind.Point) { position = ToWorld(goal.Point, 1.2f); return true; }
            var wanted = goal.Kind == GoalKind.Core ? GoalKind.Core : GoalKind.Outpost;
            if (goal.Kind != GoalKind.Core && goal.Kind != GoalKind.Outpost) return false;
            foreach (var objective in frame.Objectives)
                if (objective.Kind == wanted && objective.Id == goal.Id) { position = ToWorld(objective.Position, 1.2f); return true; }
            return false;
        }

        private void SyncArrows(FactionFrame frame)
        {
            var live = new HashSet<ulong>();
            foreach (var command in frame.Commands)
            {
                bool active = command.Status == CommandStatus.Executing || command.Status == CommandStatus.Pending;
                if (!active || command.Target.Kind != ScopeKind.Army) continue;
                if (!TryGoalPosition(command.Goal, frame, out var goal)) continue;
                live.Add(command.CommandId);
                if (!arrows.TryGetValue(command.CommandId, out var arrow))
                {
                    var go = new GameObject("Arrow_" + command.CommandId);
                    go.transform.SetParent(transform, false);
                    var line = go.AddComponent<LineRenderer>();
                    line.positionCount = 2;
                    line.useWorldSpace = true;
                    line.widthMultiplier = 1.6f;
                    line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.85f, 0.3f), new Keyframe(0.86f, 1.2f), new Keyframe(1f, 0f));
                    line.numCapVertices = 0;
                    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    arrow = new Arrow { Line = line, ArmyId = command.Target.Id };
                    arrows.Add(command.CommandId, arrow);
                }
                arrow.Goal = goal;
                arrow.Line.sharedMaterial = PresentationMaterials.GetUnlit(command.Status == CommandStatus.Pending ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 1f, 1f));
            }

            scratchIds.Clear();
            foreach (var pair in arrows)
                if (!live.Contains(pair.Key)) scratchIds.Add(pair.Key);
            foreach (var id in scratchIds)
            {
                Discard(arrows[id].Line.gameObject);
                arrows.Remove(id);
            }
        }

        private readonly List<ulong> scratchIds = new List<ulong>();

        private void FaceCamera(Camera camera)
        {
            foreach (var visual in outposts.Values)
                visual.HpFill.parent.rotation = camera.transform.rotation;
        }

        private static void Discard(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static Vector3 ToWorld(SimPoint point, float height)
        {
            return new Vector3(point.X.Raw / Fix64Scale, height, point.Z.Raw / Fix64Scale);
        }

        private GameObject ModelFor(UnitKind kind)
        {
            return kind == UnitKind.Scout ? scoutModel : infantryModel;
        }

        private GameObject CreateUnitObject(RenderUnit unit)
        {
            var color = PresentationMaterials.Get(unit.IsOwn ? new Color(0.2f, 0.5f, 1f) : new Color(1f, 0.3f, 0.25f));
            var model = ModelFor(unit.Kind);
            GameObject go;
            if (model != null)
            {
                go = Instantiate(model, transform);
                go.transform.localScale = Vector3.one * UnitModelScale;
                Tint(go, color);
            }
            else
            {
                var type = unit.Kind == UnitKind.Scout ? PrimitiveType.Sphere : PrimitiveType.Capsule;
                go = GameObject.CreatePrimitive(type);
                go.transform.SetParent(transform, false);
                float size = unit.Kind == UnitKind.Scout ? 1.6f : 2.2f;
                go.transform.localScale = new Vector3(size, size, size);
                Discard(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = color;
            }
            go.name = (unit.IsOwn ? "Own_" : "Enemy_") + unit.Id;
            return go;
        }

        private static void Tint(GameObject go, Material material)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = material;
        }

        private Visual CreateCoreObject(KnownObjective objective)
        {
            var root = new GameObject("Core_" + objective.Id);
            root.transform.SetParent(transform, false);
            var color = PresentationMaterials.Get(
                objective.OwnerFactionId == 1 ? new Color(0.1f, 0.3f, 0.8f) : new Color(0.8f, 0.15f, 0.1f));
            float barHeight;
            if (coreModel != null)
            {
                var body = Instantiate(coreModel, root.transform);
                body.transform.localScale = Vector3.one * CoreModelScale;
                Tint(body, color);
                barHeight = 8f;
            }
            else
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(root.transform, false);
                body.transform.localScale = new Vector3(8f, 4f, 8f);
                Discard(body.GetComponent<Collider>());
                body.GetComponent<Renderer>().sharedMaterial = color;
                barHeight = 4.6f;
            }

            var bar = new GameObject("HpBar");
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, barHeight, 0f);
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Back";
            back.transform.SetParent(bar.transform, false);
            back.transform.localScale = new Vector3(8.4f, 1.9f, 1f);
            Discard(back.GetComponent<Collider>());
            back.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.08f, 0.08f, 0.08f));
            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            fill.transform.SetParent(bar.transform, false);
            fill.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            Discard(fill.GetComponent<Collider>());
            return new Visual { Object = root, HpFill = fill.transform };
        }

        private void UpdateHpBar(Visual visual, int hp)
        {
            float ratio = Mathf.Clamp01(hp / (float)coreMaxHp);
            const float width = 8f;
            visual.HpFill.localScale = new Vector3(width * ratio, 1.5f, 1f);
            visual.HpFill.localPosition = new Vector3(-width * (1f - ratio) / 2f, 0f, -0.01f);
            visual.HpFill.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(
                Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.2f, 0.85f, 0.2f), ratio));
            visual.Hp = hp;
        }
    }
}
