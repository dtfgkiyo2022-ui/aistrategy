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
        private const int MaxContactLabels = 8;

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
            public bool IsEnemy;
            // Outposts drawn with a purchased pack: the tower and the owner it is currently colored for.
            public GameObject PackTower;
            public uint PackOwner;
        }

        private readonly Dictionary<ulong, Visual> units = new Dictionary<ulong, Visual>();
        private readonly List<ulong> scratchUnitIds = new List<ulong>();
        private readonly Dictionary<uint, Visual> cores = new Dictionary<uint, Visual>();
        private readonly List<uint> scratch = new List<uint>();
        private readonly Dictionary<ulong, Vector3> previousUnitPositions = new Dictionary<ulong, Vector3>();
        private readonly Dictionary<uint, Vector3> previousCorePositions = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, Visual> armies = new Dictionary<uint, Visual>();
        private readonly Dictionary<uint, Vector3> previousArmyPositions = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, int> armyAlive = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> coreHp = new Dictionary<uint, int>();
        private readonly Dictionary<uint, Visual> outposts = new Dictionary<uint, Visual>();
        private GameObject selectionRing;
        private Texture2D fogTexture;
        private int fogWidth;
        private float fogCellSize;

        private bool IsVisibleNow(Vector3 position)
        {
            // Fail safe: when the fog data cannot be used, nothing counts as visible, so an enemy is never
            // interpolated through a cell that might be hidden (it stays at its latest reported position).
            if (latestFrame == null || latestFrame.Fog == null || fogCellSize <= 0f) return false;
            var cells = latestFrame.Fog.VisibleCells;
            if (cells.Count != fogWidth * fogHeight) return false;
            int x = Mathf.FloorToInt(position.x / fogCellSize), z = Mathf.FloorToInt(position.z / fogCellSize);
            if (x < 0 || z < 0 || x >= fogWidth || z >= fogHeight) return false;
            return cells[z * fogWidth + x];
        }
        private int fogHeight;
        private readonly Dictionary<ulong, GameObject> ghosts = new Dictionary<ulong, GameObject>();
        private readonly List<ulong> scratchGhostIds = new List<ulong>();
        private FactionFrame latestFrame;
        private sealed class Arrow { public LineRenderer Line; public uint ArmyId; public Vector3 Goal; }
        private readonly Dictionary<ulong, Arrow> arrows = new Dictionary<ulong, Arrow>();

        public FactionFrame LatestFrame { get { return latestFrame; } }
        private GameObject terrainObject;
        private SelectionTarget selected = SelectionTarget.None;
        private float sinceUpdate;

        public SelectionTarget Selected { get { return selected; } }

        // Box selection: every own army picked by the last drag. Selected is then the first of them, so code that reads
        // one army still works; the army commands go to each army in this list.
        private readonly List<uint> selectedArmies = new List<uint>();
        private readonly List<GameObject> extraRings = new List<GameObject>();

        /// <summary>The selected own armies: one for a click, several after a box, empty without an army.</summary>
        public IReadOnlyList<uint> SelectedArmies { get { return selectedArmies; } }

        public void Select(SelectionTarget target)
        {
            selected = target;
            selectedArmies.Clear();
            if (target.Kind == SelectionKind.Army) selectedArmies.Add(target.Id);
            UpdateSelectionRing();
        }

        /// <summary>Selects these armies together (none clears the selection).</summary>
        public void SelectArmies(IList<uint> ids)
        {
            if (ids.Count == 0) { Select(SelectionTarget.None); return; }
            Select(new SelectionTarget(SelectionKind.Army, ids[0]));
            for (int i = 1; i < ids.Count; i++) selectedArmies.Add(ids[i]);
            PlaceSelectionRing();
        }

        /// <summary>Own armies whose marker is inside the screen rectangle (screen coordinates, origin bottom left), by id.</summary>
        public List<uint> ArmiesInScreenRect(Camera camera, Rect screenRect)
        {
            var found = new List<uint>();
            foreach (var pair in armies)
            {
                var projected = camera.WorldToScreenPoint(pair.Value.Object.transform.position);
                if (projected.z > 0f && screenRect.Contains(new Vector2(projected.x, projected.y))) found.Add(pair.Key);
            }
            found.Sort();
            return found;
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
            if (selectedArmies.Count > 1)
            {
                int alive = 0;
                foreach (uint id in selectedArmies) if (armyAlive.TryGetValue(id, out var count)) alive += count;
                return UiText.T("Selected: Armies ", "選択中：軍団 ") + string.Join(", ", selectedArmies) + UiText.T(" (alive ", "（生存 ") + alive + ")";
            }
            switch (selected.Kind)
            {
                case SelectionKind.Army:
                    return UiText.T("Selected: Army ", "選択中：軍団 ") + selected.Id + (armyAlive.TryGetValue(selected.Id, out var alive) ? UiText.T(" (alive ", "（生存 ") + alive + ")" : "");
                case SelectionKind.Outpost:
                    return UiText.T("Selected: Outpost ", "選択中：拠点 ") + selected.Id;
                case SelectionKind.Core:
                    return UiText.T("Selected: Core ", "選択中：コア ") + selected.Id + (coreHp.TryGetValue(selected.Id, out var hp) ? (hp < 0 ? UiText.T(" (HP unknown)", "（HP不明）") : " (HP " + hp + ")") : "");
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
            if (selectionRing == null || selected.Kind == SelectionKind.None) { PlaceExtraRings(); return; }
            var map = selected.Kind == SelectionKind.Core ? cores : selected.Kind == SelectionKind.Outpost ? outposts : armies;
            if (!map.TryGetValue(selected.Id, out var visual)) { selectionRing.SetActive(false); return; }
            selectionRing.SetActive(true);
            var position = visual.Object.transform.position;
            selectionRing.transform.position = new Vector3(position.x, 0.08f, position.z);
            PlaceExtraRings();
        }

        /// <summary>One more ring under each army of a box selection after the first.</summary>
        private void PlaceExtraRings()
        {
            int needed = Mathf.Max(0, selectedArmies.Count - 1);
            while (extraRings.Count < needed)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "SelectionRing";
                ring.transform.SetParent(transform, false);
                Discard(ring.GetComponent<Collider>());
                ring.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(1f, 0.9f, 0.2f));
                ring.transform.localScale = new Vector3(6f, 0.05f, 6f);
                extraRings.Add(ring);
            }
            for (int i = 0; i < extraRings.Count; i++)
            {
                bool shown = i < needed && armies.TryGetValue(selectedArmies[i + 1], out var army);
                extraRings[i].SetActive(shown);
                if (!shown) continue;
                var position = armies[selectedArmies[i + 1]].Object.transform.position;
                extraRings[i].transform.position = new Vector3(position.x, 0.08f, position.z);
            }
        }

        public void Push(FactionFrame frame)
        {
            sinceUpdate = 0f;
            SyncUnits(frame);
            SyncCores(frame);
            SyncArmies(frame);
            SyncOutposts(frame);
            latestFrame = frame;
            SyncFog(frame);
            SyncGhosts(frame);
            SyncArrows(frame);
            Apply(0f);
        }

        public void Apply(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            foreach (var visual in units.Values)
            {
                var position = Vector3.Lerp(visual.From, visual.To, alpha);
                // Chapter 12: never interpolate an enemy through a cell this faction cannot see now.
                if (visual.IsEnemy && !IsVisibleNow(position)) position = visual.To;
                visual.Object.transform.position = position;
            }
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

        private static ulong UnitKey(RenderUnit unit)
        {
            return unit.Id | (unit.IsOwn ? 0UL : 1UL << 32);
        }

        private void SyncUnits(FactionFrame frame)
        {
            var present = new HashSet<ulong>();
            foreach (var unit in frame.Units)
            {
                ulong key = UnitKey(unit);
                present.Add(key);
                var target = ToWorld(unit.Position, ModelFor(unit.Kind) != null || LocalVisualPack.HasUnit(unit.Kind) ? 0f : 1.1f);
                if (!units.TryGetValue(key, out var visual))
                {
                    visual = new Visual { Object = CreateUnitObject(unit), From = target, IsEnemy = !unit.IsOwn };
                    units.Add(key, visual);
                }
                else
                {
                    visual.From = visual.Object.transform.position;
                    if (previousUnitPositions.TryGetValue(key, out var previous)) visual.From = previous;
                }
                visual.To = target;
            }

            scratchUnitIds.Clear();
            foreach (var pair in units)
                if (!present.Contains(pair.Key)) scratchUnitIds.Add(pair.Key);
            foreach (var id in scratchUnitIds)
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
                var target = ToWorld(objective.Position, coreModel != null || LocalVisualPack.HasCore() ? 0f : 2f);
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
                // An unobserved core must not show a full bar; hide it instead of inventing a value.
                coreHp[objective.Id] = objective.IsHpKnown ? objective.Hp : -1;
                visual.HpFill.parent.gameObject.SetActive(objective.IsHpKnown);
                if (objective.IsHpKnown) UpdateHpBar(visual, objective.Hp);
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
            // V3-4 terrain kinds: forest, river, mountain.
            var forest = new Color(0.1f, 0.33f, 0.14f);
            var river = new Color(0.2f, 0.42f, 0.78f);
            var mountain = new Color(0.5f, 0.48f, 0.46f);
            var pixels = new Color[terrain.WidthCells * terrain.HeightCells];
            for (int z = 0; z < terrain.HeightCells; z++)
                for (int x = 0; x < terrain.WidthCells; x++)
                {
                    byte kind = terrain.KindAt(x, z);
                    pixels[z * terrain.WidthCells + x] = kind == 1 ? forest : kind == 2 ? river : kind == 3 ? mountain : terrain.IsBlocked(x, z) ? wall : open;
                }
            texture.SetPixels(pixels);
            texture.Apply();
            fogCellSize = terrain.CellSizeMeters;
            fogWidth = terrain.WidthCells;
            fogHeight = terrain.HeightCells;
            var oldFog = transform.Find("Fog");
            if (oldFog != null) Discard(oldFog.gameObject);
            fogTexture = new Texture2D(fogWidth, fogHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var fogObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fogObject.name = "Fog";
            fogObject.transform.SetParent(transform, false);
            fogObject.transform.position = new Vector3(terrain.WidthCells * terrain.CellSizeMeters / 2f, 0.03f, terrain.HeightCells * terrain.CellSizeMeters / 2f);
            fogObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            fogObject.transform.localScale = new Vector3(terrain.WidthCells * terrain.CellSizeMeters, terrain.HeightCells * terrain.CellSizeMeters, 1f);
            Discard(fogObject.GetComponent<Collider>());
            var fogMaterial = new Material(Shader.Find("Sprites/Default"));
            fogMaterial.mainTexture = fogTexture;
            fogObject.GetComponent<Renderer>().sharedMaterial = fogMaterial;

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
                var target = ToWorld(objective.Position, LocalVisualPack.HasOutpost() ? 0f : 0.4f);
                if (!outposts.TryGetValue(objective.Id, out var visual))
                {
                    var root = new GameObject("Outpost_" + objective.Id);
                    root.transform.SetParent(transform, false);
                    // A purchased pack, when this machine has it, replaces the placeholder body and pole.
                    float gaugeHeight = 6f;
                    if (LocalVisualPack.TryCreateOutpost(root.transform, out var packTower, out var towerHeight)) gaugeHeight = towerHeight + 1f;
                    else
                    {
                        packTower = null;
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
                    }
                    var bar = new GameObject("CaptureGauge");
                    bar.transform.SetParent(root.transform, false);
                    bar.transform.localPosition = new Vector3(0f, gaugeHeight, 0f);
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
                    visual = new Visual { Object = root, HpFill = fill.transform, From = target, PackTower = packTower, PackOwner = uint.MaxValue };
                    outposts.Add(objective.Id, visual);
                }
                visual.To = target;
                visual.From = target;
                visual.Object.transform.position = target;
                uint owner = objective.IsOwnerKnown ? objective.OwnerFactionId : 0u;
                if (visual.PackTower != null)
                {
                    if (visual.PackOwner != owner) { LocalVisualPack.SetOutpostOwner(visual.PackTower, owner); visual.PackOwner = owner; }
                }
                else
                {
                    var ownerMaterial = PresentationMaterials.Get(FactionColor(owner));
                    foreach (var renderer in visual.Object.GetComponentsInChildren<Renderer>())
                        if (renderer.gameObject.name != "Back" && renderer.gameObject.name != "Fill") renderer.sharedMaterial = ownerMaterial;
                }

                float ratio = objective.CaptureDurationTicks > 0 ? Mathf.Clamp01(objective.CaptureTicks / (float)objective.CaptureDurationTicks) : 0f;
                const float width = 8f;
                visual.HpFill.gameObject.SetActive(ratio > 0f);
                visual.HpFill.localScale = new Vector3(width * ratio, 1.2f, 1f);
                visual.HpFill.localPosition = new Vector3(-width * (1f - ratio) / 2f, 0f, -0.01f);
                visual.HpFill.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(FactionColor(objective.CapturingFactionId));
            }
        }

        private void SyncFog(FactionFrame frame)
        {
            if (fogTexture == null || frame.Fog == null) return;
            var visible = frame.Fog.VisibleCells;
            var explored = frame.Fog.ExploredCells;
            int count = fogWidth * fogHeight;
            if (visible.Count != count || explored.Count != count) return;
            var pixels = new Color[count];
            var unexplored = new Color(0f, 0f, 0f, 0.82f);
            var remembered = new Color(0f, 0f, 0f, 0.42f);
            for (int i = 0; i < count; i++)
                pixels[i] = visible[i] ? Color.clear : explored[i] ? remembered : unexplored;
            fogTexture.SetPixels(pixels);
            fogTexture.Apply();
        }

        private static ulong ContactKey(EnemyContact contact)
        {
            return contact.ContactId | (contact.IsArmyContact ? 1UL << 32 : 0UL);
        }

        private void SyncGhosts(FactionFrame frame)
        {
            var live = new HashSet<ulong>();
            foreach (var contact in frame.Observation.Contacts)
            {
                if (contact.IsArmyContact || contact.IsCurrentlyVisible) continue;
                ulong key = ContactKey(contact);
                live.Add(key);
                if (!ghosts.TryGetValue(key, out var ghost))
                {
                    ghost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    ghost.name = "Ghost_" + contact.ContactId;
                    ghost.transform.SetParent(transform, false);
                    ghost.transform.localScale = new Vector3(2f, 2f, 2f);
                    Discard(ghost.GetComponent<Collider>());
                    ghost.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.75f, 0.45f, 0.42f));
                    ghosts.Add(key, ghost);
                }
                ghost.transform.position = ToWorld(contact.LastPosition, 1f);
            }
            scratchGhostIds.Clear();
            foreach (var pair in ghosts)
                if (!live.Contains(pair.Key)) scratchGhostIds.Add(pair.Key);
            foreach (var id in scratchGhostIds)
            {
                Discard(ghosts[id]);
                ghosts.Remove(id);
            }
        }

        public int EnemyVisualCount
        {
            get
            {
                int count = 0;
                foreach (var visual in units.Values)
                    if (visual.Object.name.StartsWith("Enemy_")) count++;
                return count;
            }
        }

        /// <summary>Verification hook: where enemy units are drawn right now (after interpolation).</summary>
        public void CollectEnemyVisualPositions(List<Vector3> into)
        {
            into.Clear();
            foreach (var visual in units.Values)
                if (visual.Object.name.StartsWith("Enemy_")) into.Add(visual.Object.transform.position);
        }

        public List<KeyValuePair<Vector3, string>> BuildContactLabels()
        {
            var labels = new List<KeyValuePair<Vector3, string>>();
            if (latestFrame == null) return labels;
            foreach (var contact in latestFrame.Observation.Contacts)
            {
                long seconds = (latestFrame.Tick - contact.LastSeenTick) / 20;
                string text;
                if (contact.IsArmyContact)
                    text = "~" + contact.EstimateMax + UiText.T(" seen, total unknown", " 目撃、総数不明");
                else if (contact.IsCurrentlyVisible)
                    continue;
                else if (contact.IsStrengthUnknown)
                    text = UiText.T("strength ? (", "兵力 ?（") + seconds + UiText.T("s ago)", "秒前）");
                else
                    text = "~" + contact.EstimateMax + " (" + seconds + UiText.T("s ago)", "秒前)") + (contact.IsUncertain ? "?" : "") + (contact.IsAbsentAtLastPosition ? UiText.T(" absent", " 不在") : "");
                labels.Add(new KeyValuePair<Vector3, string>(ToWorld(contact.LastPosition, 3f), text));
            }
            return labels;
        }

        private readonly List<Rect> drawnLabels = new List<Rect>();

        private void OnGUI()
        {
            var camera = Camera.main;
            if (camera == null) return;
            drawnLabels.Clear();
            foreach (var label in BuildContactLabels())
            {
                if (drawnLabels.Count >= MaxContactLabels) break;
                var screen = camera.WorldToScreenPoint(label.Key);
                if (screen.z <= 0f) continue;
                var rect = new Rect(screen.x - 70f, Screen.height - screen.y - 10f, 180f, 20f);
                // Contacts cluster together, so a label that would sit on top of another one is dropped.
                bool overlaps = false;
                foreach (var drawn in drawnLabels) if (drawn.Overlaps(rect)) { overlaps = true; break; }
                if (overlaps) continue;
                drawnLabels.Add(rect);
                GUI.Label(rect, label.Value);
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

        /// <summary>Drops every per-faction visual. Frame IDs are faction-local, so a view switch must rebuild them.</summary>
        public void ResetVisuals()
        {
            foreach (var visual in units.Values) Discard(visual.Object);
            units.Clear(); previousUnitPositions.Clear();
            foreach (var visual in armies.Values) Discard(visual.Object);
            armies.Clear(); previousArmyPositions.Clear(); armyAlive.Clear();
            foreach (var visual in cores.Values) Discard(visual.Object);
            cores.Clear(); previousCorePositions.Clear(); coreHp.Clear();
            foreach (var visual in outposts.Values) Discard(visual.Object);
            outposts.Clear();
            foreach (var ghost in ghosts.Values) Discard(ghost);
            ghosts.Clear();
            foreach (var arrow in arrows.Values) Discard(arrow.Line.gameObject);
            arrows.Clear();
            if (selectionRing != null) { Discard(selectionRing); selectionRing = null; }
            foreach (var ring in extraRings) Discard(ring);
            extraRings.Clear();
            selectedArmies.Clear();
            selected = SelectionTarget.None;
            latestFrame = null;
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
            // A purchased pack, when this machine has it, replaces the placeholders; everyone else keeps them.
            if (LocalVisualPack.TryCreateUnit(unit.Kind, unit.IsOwn, transform, out var packed))
            {
                packed.name = (unit.IsOwn ? "Own_" : "Enemy_") + unit.Id;
                return packed;
            }
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
            MarkClass(go, unit.Kind);
            return go;
        }

        /// <summary>V3-5: archers carry a tall thin bow, cavalry stands wider on a low block, so both read apart from infantry.</summary>
        private void MarkClass(GameObject go, UnitKind kind)
        {
            if (kind != UnitKind.Archer && kind != UnitKind.Cavalry) return;
            var mark = GameObject.CreatePrimitive(kind == UnitKind.Archer ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            mark.name = kind == UnitKind.Archer ? "Bow" : "Mount";
            Discard(mark.GetComponent<Collider>());
            mark.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(kind == UnitKind.Archer ? new Color(0.55f, 0.35f, 0.15f) : new Color(0.35f, 0.25f, 0.15f));
            mark.transform.SetParent(go.transform, false);
            var s = go.transform.lossyScale;
            if (kind == UnitKind.Archer)
            {
                mark.transform.localScale = new Vector3(0.12f / s.x, 1.1f / s.y, 0.12f / s.z);
                mark.transform.localPosition = new Vector3(0.6f / s.x, 0.4f / s.y, 0f);
            }
            else
            {
                mark.transform.localScale = new Vector3(1.6f / s.x, 0.6f / s.y, 2.4f / s.z);
                mark.transform.localPosition = new Vector3(0f, -0.4f / s.y, 0f);
            }
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
            if (LocalVisualPack.TryCreateCore(objective.OwnerFactionId == 1, root.transform, out _, out var packedHeight))
            {
                barHeight = packedHeight + 1f;
            }
            else if (coreModel != null)
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
