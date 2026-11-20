using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    public sealed partial class BattlefieldView
    {
        private sealed class NeutralVisual
        {
            public GameObject Root;
            public Renderer Body;
            public Renderer OwnerBand;
            public WorldHealthBarVisuals.Handle HealthBar;
            // 79.3 (11-19): the camp's territory on the ground and the beast's wish-to-return gauge under its health bar.
            public LineRenderer LeashCircle;
            public float LeashRadius = -1f;
            public float LeashAlpha = -1f;
            public WorldHealthBarVisuals.Handle LeashGauge;
            // Drawn with GUI.Label in OnGUI like the building tags. A TextMesh whose text changed every frame made the
            // player build's memory grow by about 30 MB a second (build1117c, 11-17).
            public string LabelText = "";
            public int LabelHp = -1, LabelMaxHp = -1;
            public long LabelRespawnTick = -1;
            public Vector3 LabelPosition;
        }

        private GUIStyle neutralLabelStyle;

        /// <summary>
        /// One visible beast (11-18). A camp used to be drawn as one shape, so a pack of three wolves showed as one.
        /// Each visible beast now has its own model (NeutralModelArt) or placeholder, moved smoothly between frames like
        /// the soldiers: walking while it moves, attacking for a moment after its health drops, idle otherwise.
        /// Display only: nothing here goes back into the simulation.
        /// </summary>
        private sealed class BeastVisual
        {
            public GameObject Root;
            public LocalVisualPack.AnimationHandle Animation;
            public Vector3 From, To;
            public float Facing;
            public int LastHp = -1;
            public float CombatSeconds;
            public bool Walking;
            public bool Seen;
            public DragonElement Element;
        }

        private readonly Dictionary<uint, BeastVisual> beastVisuals = new Dictionary<uint, BeastVisual>();

        private readonly Dictionary<uint, NeutralVisual> neutralVisuals = new Dictionary<uint, NeutralVisual>();
        private readonly Dictionary<ulong, GameObject> blessingMarks = new Dictionary<ulong, GameObject>();

        public bool TryPickNeutralCamp(Camera camera, Vector2 screenPoint, float radiusPixels, out uint campId)
        {
            campId = 0;
            if (camera == null || latestFrame == null) return false;
            float best = radiusPixels * radiusPixels;
            foreach (var camp in latestFrame.NeutralCamps)
            {
                Vector3 screen = camera.WorldToScreenPoint(ToWorld(camp.Position, 0.6f));
                if (screen.z <= 0f) continue;
                float dx = screen.x - screenPoint.x, dy = screen.y - screenPoint.y, distance = dx * dx + dy * dy;
                if (distance <= best && (campId == 0 || camp.Id < campId)) { best = distance; campId = camp.Id; }
            }
            return campId != 0;
        }

        private void SyncNeutrals(FactionFrame frame)
        {
            var visible = new Dictionary<uint, VisibleNeutral>();
            foreach (var neutral in frame.VisibleNeutrals) visible[neutral.CampId] = neutral;
            // 79.3: per camp, the fullest wish-to-return gauge among its visible beasts and whether one is going home.
            var leashPatience = new Dictionary<uint, int>();
            var leashReturning = new HashSet<uint>();
            var campFighting = new HashSet<uint>();
            foreach (var neutral in frame.VisibleNeutrals)
            {
                if (!neutral.Alive) continue;
                leashPatience.TryGetValue(neutral.CampId, out int fullest);
                if (neutral.LeashPatiencePermille > fullest) leashPatience[neutral.CampId] = neutral.LeashPatiencePermille;
                if (neutral.Returning) leashReturning.Add(neutral.CampId);
                if (neutral.Returning || neutral.LeashPatiencePermille > 0 || neutral.Hp < neutral.MaxHp) campFighting.Add(neutral.CampId);
            }
            SyncBeasts(frame);
            var live = new HashSet<uint>();
            foreach (var camp in frame.NeutralCamps)
            {
                if (!camp.LastKnownAlive && !visible.ContainsKey(camp.Id) && camp.RespawnTick <= frame.Tick) continue;
                live.Add(camp.Id);
                if (!neutralVisuals.TryGetValue(camp.Id, out var visual))
                {
                    var root = new GameObject("NeutralCamp_" + camp.Id);
                    root.transform.SetParent(transform, false);
                    // The remembered shape stands still, so its animation graph is released at once.
                    var body = CreateBeastBody(camp.Kind, DragonElement.None, root.transform, camp.Id, out var campAnimation);
                    if (campAnimation != null) campAnimation.Dispose();
                    var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    band.name = "NeutralOwnerBand"; band.transform.SetParent(root.transform, false);
                    band.transform.localPosition = new Vector3(0f, 0.18f, 0f);
                    band.transform.localScale = new Vector3(0.42f, 0.05f, 0.42f);
                    Discard(band.GetComponent<Collider>());
                    visual = new NeutralVisual
                    {
                        Root = root,
                        Body = body,
                        OwnerBand = band.GetComponent<Renderer>(),
                        HealthBar = WorldHealthBarVisuals.Create(root.transform, "NeutralHp", 2.5f, 0.14f)
                    };
                    visual.LeashGauge = WorldHealthBarVisuals.Create(root.transform, "NeutralLeash", 2.5f, 0.07f);
                    var circle = new GameObject("NeutralLeashArea");
                    circle.transform.SetParent(transform, false);
                    visual.LeashCircle = circle.AddComponent<LineRenderer>();
                    visual.LeashCircle.loop = true; visual.LeashCircle.positionCount = 64; visual.LeashCircle.widthMultiplier = 0.08f;
                    visual.LeashCircle.useWorldSpace = false;
                    neutralVisuals.Add(camp.Id, visual);
                }
                SetLeashArea(visual, camp, campFighting.Contains(camp.Id));
                var position = visible.TryGetValue(camp.Id, out var seen) ? seen.Position : camp.Position;
                int hp = visible.TryGetValue(camp.Id, out seen) ? seen.Hp : camp.LastKnownHp;
                int maxHp = visible.TryGetValue(camp.Id, out seen) ? seen.MaxHp : camp.LastKnownMaxHp;
                visual.Root.SetActive(true);
                visual.Root.transform.position = ToWorld(position, 0f);
                var visibleNeutral = visible.TryGetValue(camp.Id, out var current) ? current : default(VisibleNeutral);
                // The remembered camp shape stands in only while none of its beasts is in sight.
                if (visual.Body != null)
                {
                    visual.Body.gameObject.SetActive(!visibleCamps.Contains(camp.Id));
                    if (!NeutralModelArt.Has(camp.Kind))
                        visual.Body.sharedMaterial = PresentationMaterials.GetNature(NeutralColor(camp.Kind, visibleNeutral.Element));
                }
                visual.OwnerBand.sharedMaterial = PresentationMaterials.GetGlow(OwnerColor(visibleNeutral.OwnerFactionId, frame.FactionId));
                visual.OwnerBand.gameObject.SetActive(visibleNeutral.OwnerFactionId != 0);
                visual.HealthBar.Root.transform.position = ToWorld(position, 2.1f);
                visual.HealthBar.Root.transform.rotation = Camera.main == null ? Quaternion.identity : Camera.main.transform.rotation;
                WorldHealthBarVisuals.Set(visual.HealthBar, hp, maxHp, ToWorld(position, 2.1f), Camera.main == null ? Quaternion.identity : Camera.main.transform.rotation);
                bool returning = leashReturning.Contains(camp.Id);
                // A beast going home cannot be hurt (79.2), so its bar is drawn faded.
                if (returning && visual.HealthBar.Fill != null)
                    visual.HealthBar.Fill.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(new Color(0.75f, 0.75f, 0.75f, 0.35f));
                leashPatience.TryGetValue(camp.Id, out int patience);
                SetLeashGauge(visual.LeashGauge, returning ? 0 : patience, ToWorld(position, 1.88f),
                    Camera.main == null ? Quaternion.identity : Camera.main.transform.rotation);
                if (visual.LabelHp != hp || visual.LabelMaxHp != maxHp || visual.LabelRespawnTick != camp.RespawnTick)
                {
                    visual.LabelHp = hp; visual.LabelMaxHp = maxHp; visual.LabelRespawnTick = camp.RespawnTick;
                    string element = visible.TryGetValue(camp.Id, out seen) && seen.Element != DragonElement.None ? " " + seen.Element : "";
                    visual.LabelText = UiText.NeutralName(camp.Kind) + element + " HP " + hp + "/" + maxHp;
                    if (camp.RespawnTick > frame.Tick)
                    {
                        long seconds = (camp.RespawnTick - frame.Tick + 19) / 20;
                        visual.LabelText += UiText.T(" Respawn ", " 湧き直し ")
                            + (seconds / 60).ToString() + ":" + (seconds % 60).ToString("D2");
                    }
                }
                visual.LabelPosition = ToWorld(position, 2.65f);
            }
            var remove = new List<uint>();
            foreach (var pair in neutralVisuals) if (!live.Contains(pair.Key)) remove.Add(pair.Key);
            foreach (uint id in remove) { DiscardLeashCircle(neutralVisuals[id]); Discard(neutralVisuals[id].Root); neutralVisuals.Remove(id); }
        }

        private readonly HashSet<uint> visibleCamps = new HashSet<uint>();

        private static readonly Color LeashAreaColor = new Color(1f, 0.82f, 0.35f, 1f);
        private static readonly Color LeashGaugeColor = new Color(0.72f, 0.42f, 1f, 1f);

        /// <summary>79.3: a faint ring on the ground at the camp's territory, a little brighter while its beasts fight.</summary>
        private static void SetLeashArea(NeutralVisual visual, NeutralCampView camp, bool fighting)
        {
            if (visual.LeashCircle == null) return;
            bool show = camp.LeashMeters > 0;
            visual.LeashCircle.gameObject.SetActive(show);
            if (!show) return;
            visual.LeashCircle.transform.position = ToWorld(camp.Position, 0.05f);
            float radius = camp.LeashMeters;
            if (!Mathf.Approximately(visual.LeashRadius, radius))
            {
                visual.LeashRadius = radius;
                for (int i = 0; i < visual.LeashCircle.positionCount; i++)
                {
                    float a = i / (float)visual.LeashCircle.positionCount * Mathf.PI * 2f;
                    visual.LeashCircle.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                }
            }
            float alpha = fighting ? 0.55f : 0.2f;
            if (!Mathf.Approximately(visual.LeashAlpha, alpha))
            {
                visual.LeashAlpha = alpha;
                visual.LeashCircle.sharedMaterial = PresentationMaterials.GetUnlit(new Color(LeashAreaColor.r, LeashAreaColor.g, LeashAreaColor.b, alpha));
            }
        }

        private void DiscardLeashCircle(NeutralVisual visual)
        {
            if (visual.LeashCircle != null) Discard(visual.LeashCircle.gameObject);
            visual.LeashCircle = null;
        }

        /// <summary>79.3: a thin purple gauge under the health bar, shown only while the beast is outside its territory.</summary>
        private static void SetLeashGauge(WorldHealthBarVisuals.Handle gauge, int permille, Vector3 position, Quaternion rotation)
        {
            if (gauge == null || gauge.Fill == null) return;
            bool show = permille > 0;
            gauge.Root.SetActive(show);
            if (!show) return;
            float ratio = Mathf.Clamp01(permille / 1000f);
            gauge.Root.transform.position = position;
            gauge.Root.transform.rotation = rotation;
            gauge.Fill.localScale = new Vector3(gauge.Width * ratio, 0.07f, 1f);
            gauge.Fill.localPosition = new Vector3(-gauge.Width * (1f - ratio) / 2f, 0f, -0.01f);
            gauge.Fill.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetUnlit(LeashGaugeColor);
        }

        /// <summary>The model (with its animation) or the old placeholder shape; the returned renderer is the body.</summary>
        private Renderer CreateBeastBody(NeutralKind kind, DragonElement element, Transform parent, uint id, out LocalVisualPack.AnimationHandle animation)
        {
            animation = null;
            // The one dragon model is tinted by its element (fire red, water blue, earth brown, wind green).
            Color? tint = kind == NeutralKind.Dragon && element != DragonElement.None ? NeutralColor(kind, element) : (Color?)null;
            if (NeutralModelArt.TryCreate(kind, parent, id, out var model, out animation, tint))
                return model.GetComponentInChildren<Renderer>();
            var body = GameObject.CreatePrimitive(NeutralPrimitive(kind));
            body.name = "NeutralShape"; body.transform.SetParent(parent, false);
            body.transform.localScale = NeutralScale(kind);
            Discard(body.GetComponent<Collider>());
            var renderer = body.GetComponent<Renderer>();
            renderer.sharedMaterial = PresentationMaterials.GetNature(NeutralColor(kind, element));
            return renderer;
        }

        private void SyncBeasts(FactionFrame frame)
        {
            visibleCamps.Clear();
            foreach (var beast in beastVisuals.Values) beast.Seen = false;
            foreach (var neutral in frame.VisibleNeutrals)
            {
                if (!neutral.Alive) continue;
                visibleCamps.Add(neutral.CampId);
                // A pack stands on one simulation point (3 wolves at the camp), so the display spreads them a little.
                var to = ToWorld(neutral.Position, 0f) + PackOffset(neutral);
                if (beastVisuals.TryGetValue(neutral.Id, out var old) && old.Element != neutral.Element)
                {
                    // A respawned dragon keeps its id but may have a new element: build it again in the new colour.
                    if (old.Animation != null) old.Animation.Dispose();
                    Discard(old.Root);
                    beastVisuals.Remove(neutral.Id);
                }
                if (!beastVisuals.TryGetValue(neutral.Id, out var beast))
                {
                    var root = new GameObject("Neutral_" + neutral.Id);
                    root.transform.SetParent(transform, false);
                    root.transform.position = to;
                    beast = new BeastVisual { Root = root, From = to, To = to, Element = neutral.Element };
                    CreateBeastBody(neutral.Kind, neutral.Element, root.transform, neutral.Id, out beast.Animation);
                    beastVisuals.Add(neutral.Id, beast);
                }
                beast.Seen = true;
                beast.From = beast.Root.transform.position;
                beast.To = to;
                var step = new Vector3(to.x - beast.From.x, 0f, to.z - beast.From.z);
                beast.Walking = step.sqrMagnitude > 0.0004f;
                if (beast.LastHp >= 0 && neutral.Hp < beast.LastHp) beast.CombatSeconds = 1.5f;
                beast.LastHp = neutral.Hp;
                if (beast.Walking) beast.Facing = Mathf.Atan2(step.x, step.z) * Mathf.Rad2Deg;
                else if (beast.CombatSeconds > 0f && TryNearestUnit(frame, to, 8f, out var near))
                    beast.Facing = Mathf.Atan2(near.x - to.x, near.z - to.z) * Mathf.Rad2Deg;
            }
            var gone = new List<uint>();
            foreach (var pair in beastVisuals) if (!pair.Value.Seen) gone.Add(pair.Key);
            foreach (uint id in gone)
            {
                var beast = beastVisuals[id];
                if (beast.Animation != null) beast.Animation.Dispose();
                Discard(beast.Root);
                beastVisuals.Remove(id);
            }
        }

        private static Vector3 PackOffset(VisibleNeutral neutral)
        {
            if (neutral.Kind != NeutralKind.WolfPack) return Vector3.zero;
            float angle = (neutral.Id % 3u) * 2.094f + neutral.CampId * 0.7f;
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * 1.1f;
        }

        private static bool TryNearestUnit(FactionFrame frame, Vector3 from, float range, out Vector3 nearest)
        {
            nearest = from;
            float best = range * range;
            bool found = false;
            foreach (var unit in frame.Units)
            {
                var p = ToWorld(unit.Position, 0f);
                float d = (p.x - from.x) * (p.x - from.x) + (p.z - from.z) * (p.z - from.z);
                if (d < best) { best = d; nearest = p; found = true; }
            }
            return found;
        }

        /// <summary>Every rendered frame: slide between the last two simulation frames and play the right motion.</summary>
        private void UpdateNeutralBeasts(float deltaTime, float matchRate, float frameFraction)
        {
            float t = Mathf.Clamp01(frameFraction);
            foreach (var beast in beastVisuals.Values)
            {
                if (beast.Root == null) continue;
                beast.Root.transform.position = Vector3.Lerp(beast.From, beast.To, t);
                var target = Quaternion.Euler(0f, beast.Facing, 0f);
                beast.Root.transform.rotation = Quaternion.RotateTowards(beast.Root.transform.rotation, target, 360f * deltaTime);
                if (matchRate > 0f) beast.CombatSeconds = Mathf.Max(0f, beast.CombatSeconds - deltaTime * matchRate);
                if (beast.Animation == null) continue;
                beast.Animation.SetDesired(beast.Walking, beast.CombatSeconds > 0f && !beast.Walking, false, false);
                beast.Animation.SetPlaybackRate(1f, matchRate);
                beast.Animation.Tick(deltaTime, matchRate);
            }
        }

        /// <summary>
        /// Checking only (-rts-settings-shots): where to point the camera for close photographs of the game's own models,
        /// one own villager (a man and a woman), one own soldier per kind, one enemy unit and one visible beast per kind.
        /// Read from the latest frame; nothing is changed.
        /// </summary>
        public List<KeyValuePair<string, Vector3>> ArtShotSubjects()
        {
            var subjects = new List<KeyValuePair<string, Vector3>>();
            var frame = latestFrame;
            if (frame == null) return subjects;
            var taken = new HashSet<string>();
            if (frame.Economy != null)
                foreach (var villager in frame.Economy.Villagers)
                {
                    string name = (villager.IsOwn ? "villager_" : "enemy_villager_") + (villager.Id % 2 == 1 ? "woman" : "man");
                    if (taken.Add(name)) subjects.Add(new KeyValuePair<string, Vector3>(name, ToWorld(villager.Position, 0f)));
                }
            foreach (var unit in frame.Units)
            {
                string name = (unit.IsOwn ? "unit_" : "enemy_") + unit.Kind;
                if (taken.Add(name)) subjects.Add(new KeyValuePair<string, Vector3>(name, ToWorld(unit.Position, 0f)));
            }
            foreach (var neutral in frame.VisibleNeutrals)
            {
                if (!neutral.Alive) continue;
                string name = "beast_" + neutral.Kind;
                if (taken.Add(name)) subjects.Add(new KeyValuePair<string, Vector3>(name, ToWorld(neutral.Position, 0f)));
            }
            return subjects;
        }

        private void SyncNeutralBlessingMarks(FactionFrame frame)
        {
            var live = new HashSet<ulong>();
            foreach (var unit in frame.Units)
            {
                if (!unit.IsOwn || unit.Blessings == NeutralBlessing.None) continue;
                ulong key = UnitKey(unit); live.Add(key);
                if (!blessingMarks.TryGetValue(key, out var mark))
                {
                    mark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    mark.name = "NeutralBlessingMark"; mark.transform.SetParent(transform, false);
                    mark.transform.localScale = Vector3.one * 0.22f; Discard(mark.GetComponent<Collider>());
                    blessingMarks.Add(key, mark);
                }
                if (units.TryGetValue(key, out var visual))
                {
                    mark.SetActive(true); mark.transform.position = visual.Object.transform.position + Vector3.up * 2.5f;
                    mark.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.GetGlow(BlessingColor(unit.Blessings));
                }
            }
            var remove = new List<ulong>();
            foreach (var pair in blessingMarks) if (!live.Contains(pair.Key)) remove.Add(pair.Key);
            foreach (ulong id in remove) { Discard(blessingMarks[id]); blessingMarks.Remove(id); }
        }

        private static PrimitiveType NeutralPrimitive(NeutralKind kind)
            => kind == NeutralKind.ShellBeast ? PrimitiveType.Sphere
            : kind == NeutralKind.AzureSerpent || kind == NeutralKind.Dragon ? PrimitiveType.Cylinder : PrimitiveType.Capsule;

        private static Vector3 NeutralScale(NeutralKind kind)
            => kind == NeutralKind.ShellBeast ? new Vector3(2.4f, 1.5f, 2.4f)
            : kind == NeutralKind.FlameHornBeast ? new Vector3(1.6f, 2.4f, 1.6f)
            : kind == NeutralKind.AzureSerpent ? new Vector3(1.2f, 2.8f, 1.2f)
            : kind == NeutralKind.Dragon ? new Vector3(2.8f, 3.4f, 2.8f)
            : kind == NeutralKind.GreatLord ? new Vector3(3.4f, 4.4f, 3.4f) : new Vector3(1.2f, 1.4f, 1.2f);

        private static Color NeutralColor(NeutralKind kind)
            => kind == NeutralKind.FlameHornBeast ? new Color(0.95f, 0.24f, 0.08f)
            : kind == NeutralKind.AzureSerpent ? new Color(0.16f, 0.52f, 0.95f)
            : kind == NeutralKind.Dragon ? new Color(0.75f, 0.16f, 0.82f)
            : kind == NeutralKind.GreatLord ? new Color(0.72f, 0.18f, 0.82f)
            : kind == NeutralKind.RockBeast ? new Color(0.45f, 0.42f, 0.38f)
            : kind == NeutralKind.ShellBeast ? new Color(0.52f, 0.28f, 0.12f) : new Color(0.62f, 0.48f, 0.28f);

        private static Color NeutralColor(NeutralKind kind, DragonElement element)
        {
            if (kind != NeutralKind.Dragon || element == DragonElement.None) return NeutralColor(kind);
            if (element == DragonElement.Fire) return new Color(0.95f, 0.22f, 0.08f);
            if (element == DragonElement.Water) return new Color(0.12f, 0.48f, 0.95f);
            if (element == DragonElement.Earth) return new Color(0.52f, 0.35f, 0.16f);
            return new Color(0.22f, 0.78f, 0.46f);
        }

        private static Color OwnerColor(uint owner, uint viewer)
        {
            if (owner == 0) return new Color(0f, 0f, 0f, 0f);
            return owner == viewer ? new Color(0.25f, 0.92f, 0.38f) : new Color(0.95f, 0.25f, 0.24f);
        }

        /// <summary>Returns an enemy building under the cursor for the owned Siege Beast direct-order path.</summary>
        public bool TryPickEnemyBuildingForSiege(Camera camera, Vector2 screenPoint, out uint buildingId)
        {
            buildingId = 0;
            if (camera == null || latestFrame == null || latestFrame.Economy == null) return false;
            foreach (var building in latestFrame.Economy.Buildings)
            {
                if (building.FactionId != latestFrame.FactionId && BuildingFootprintContains(camera, screenPoint, building)
                    && (buildingId == 0 || building.Id < buildingId)) buildingId = building.Id;
            }
            return buildingId != 0;
        }

        private void DrawNeutralLabels(Camera camera)
        {
            if (neutralVisuals.Count == 0) return;
            if (neutralLabelStyle == null)
            {
                neutralLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
                neutralLabelStyle.normal.textColor = new Color(1f, 0.92f, 0.78f);
            }
            foreach (var visual in neutralVisuals.Values)
            {
                if (visual.Root == null || !visual.Root.activeSelf || visual.LabelText.Length == 0) continue;
                var screen = camera.WorldToScreenPoint(visual.LabelPosition);
                if (screen.z <= 0f) continue;
                if (UiHitAreas.Shared.ContainsGui(new Vector2(screen.x, Screen.height - screen.y))) continue;
                GUI.Label(new Rect(screen.x - 90f, Screen.height - screen.y - 10f, 180f, 20f), visual.LabelText, neutralLabelStyle);
            }
        }

        private static Color BlessingColor(NeutralBlessing blessings)
        {
            if ((blessings & NeutralBlessing.Flame) != 0) return new Color(1f, 0.28f, 0.05f);
            if ((blessings & NeutralBlessing.Azure) != 0) return new Color(0.1f, 0.55f, 1f);
            return new Color(0.84f, 0.72f, 0.28f);
        }

        private void ClearNeutralVisuals()
        {
            foreach (var visual in neutralVisuals.Values) { DiscardLeashCircle(visual); Discard(visual.Root); }
            neutralVisuals.Clear();
            foreach (var beast in beastVisuals.Values)
            {
                if (beast.Animation != null) beast.Animation.Dispose();
                Discard(beast.Root);
            }
            beastVisuals.Clear();
            foreach (var mark in blessingMarks.Values) Discard(mark);
            blessingMarks.Clear();
        }
    }
}
