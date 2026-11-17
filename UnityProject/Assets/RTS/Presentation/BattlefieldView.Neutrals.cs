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
            public WorldHealthBarVisuals.Handle HealthBar;
            // Drawn with GUI.Label in OnGUI like the building tags. A TextMesh whose text changed every frame made the
            // player build's memory grow by about 30 MB a second (build1117c, 11-17).
            public string LabelText = "";
            public int LabelHp = -1, LabelMaxHp = -1;
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
            SyncBeasts(frame);
            var live = new HashSet<uint>();
            foreach (var camp in frame.NeutralCamps)
            {
                if (!camp.LastKnownAlive && !visible.ContainsKey(camp.Id)) continue;
                live.Add(camp.Id);
                if (!neutralVisuals.TryGetValue(camp.Id, out var visual))
                {
                    var root = new GameObject("NeutralCamp_" + camp.Id);
                    root.transform.SetParent(transform, false);
                    // The remembered shape stands still, so its animation graph is released at once.
                    var body = CreateBeastBody(camp.Kind, root.transform, camp.Id, out var campAnimation);
                    if (campAnimation != null) campAnimation.Dispose();
                    visual = new NeutralVisual
                    {
                        Root = root,
                        Body = body,
                        HealthBar = WorldHealthBarVisuals.Create(root.transform, "NeutralHp", 2.5f, 0.14f)
                    };
                    neutralVisuals.Add(camp.Id, visual);
                }
                var position = visible.TryGetValue(camp.Id, out var seen) ? seen.Position : camp.Position;
                int hp = visible.TryGetValue(camp.Id, out seen) ? seen.Hp : camp.LastKnownHp;
                int maxHp = visible.TryGetValue(camp.Id, out seen) ? seen.MaxHp : camp.LastKnownMaxHp;
                visual.Root.SetActive(true);
                visual.Root.transform.position = ToWorld(position, 0f);
                // The remembered camp shape stands in only while none of its beasts is in sight.
                if (visual.Body != null)
                {
                    visual.Body.gameObject.SetActive(!visibleCamps.Contains(camp.Id));
                    if (!NeutralModelArt.Has(camp.Kind)) visual.Body.sharedMaterial = PresentationMaterials.GetNature(NeutralColor(camp.Kind));
                }
                visual.HealthBar.Root.transform.position = ToWorld(position, 2.1f);
                visual.HealthBar.Root.transform.rotation = Camera.main == null ? Quaternion.identity : Camera.main.transform.rotation;
                WorldHealthBarVisuals.Set(visual.HealthBar, hp, maxHp, ToWorld(position, 2.1f), Camera.main == null ? Quaternion.identity : Camera.main.transform.rotation);
                if (visual.LabelHp != hp || visual.LabelMaxHp != maxHp)
                {
                    visual.LabelHp = hp; visual.LabelMaxHp = maxHp;
                    visual.LabelText = UiText.NeutralName(camp.Kind) + " HP " + hp + "/" + maxHp;
                }
                visual.LabelPosition = ToWorld(position, 2.65f);
            }
            var remove = new List<uint>();
            foreach (var pair in neutralVisuals) if (!live.Contains(pair.Key)) remove.Add(pair.Key);
            foreach (uint id in remove) { Discard(neutralVisuals[id].Root); neutralVisuals.Remove(id); }
        }

        private readonly HashSet<uint> visibleCamps = new HashSet<uint>();

        /// <summary>The model (with its animation) or the old placeholder shape; the returned renderer is the body.</summary>
        private Renderer CreateBeastBody(NeutralKind kind, Transform parent, uint id, out LocalVisualPack.AnimationHandle animation)
        {
            animation = null;
            if (NeutralModelArt.TryCreate(kind, parent, id, out var model, out animation))
                return model.GetComponentInChildren<Renderer>();
            var body = GameObject.CreatePrimitive(NeutralPrimitive(kind));
            body.name = "NeutralShape"; body.transform.SetParent(parent, false);
            body.transform.localScale = NeutralScale(kind);
            Discard(body.GetComponent<Collider>());
            var renderer = body.GetComponent<Renderer>();
            renderer.sharedMaterial = PresentationMaterials.GetNature(NeutralColor(kind));
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
                var to = ToWorld(neutral.Position, 0f);
                if (!beastVisuals.TryGetValue(neutral.Id, out var beast))
                {
                    var root = new GameObject("Neutral_" + neutral.Id);
                    root.transform.SetParent(transform, false);
                    root.transform.position = to;
                    beast = new BeastVisual { Root = root, From = to, To = to };
                    CreateBeastBody(neutral.Kind, root.transform, neutral.Id, out beast.Animation);
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
            : kind == NeutralKind.AzureSerpent ? PrimitiveType.Cylinder : PrimitiveType.Capsule;

        private static Vector3 NeutralScale(NeutralKind kind)
            => kind == NeutralKind.ShellBeast ? new Vector3(2.4f, 1.5f, 2.4f)
            : kind == NeutralKind.FlameHornBeast ? new Vector3(1.6f, 2.4f, 1.6f)
            : kind == NeutralKind.AzureSerpent ? new Vector3(1.2f, 2.8f, 1.2f) : new Vector3(1.2f, 1.4f, 1.2f);

        private static Color NeutralColor(NeutralKind kind)
            => kind == NeutralKind.FlameHornBeast ? new Color(0.95f, 0.24f, 0.08f)
            : kind == NeutralKind.AzureSerpent ? new Color(0.16f, 0.52f, 0.95f)
            : kind == NeutralKind.RockBeast ? new Color(0.45f, 0.42f, 0.38f)
            : kind == NeutralKind.ShellBeast ? new Color(0.52f, 0.28f, 0.12f) : new Color(0.62f, 0.48f, 0.28f);

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
            foreach (var visual in neutralVisuals.Values) Discard(visual.Root);
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
