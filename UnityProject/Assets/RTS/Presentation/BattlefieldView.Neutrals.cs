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
            // Drawn with GUI.Label in OnGUI like the building tags. A TextMesh whose text changed every frame made the
            // player build's memory grow by about 30 MB a second (build1117c, 11-17).
            public string LabelText = "";
            public int LabelHp = -1, LabelMaxHp = -1;
            public Vector3 LabelPosition;
        }

        private GUIStyle neutralLabelStyle;

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
            var live = new HashSet<uint>();
            foreach (var camp in frame.NeutralCamps)
            {
                if (!camp.LastKnownAlive && !visible.ContainsKey(camp.Id)) continue;
                live.Add(camp.Id);
                if (!neutralVisuals.TryGetValue(camp.Id, out var visual))
                {
                    var root = new GameObject("NeutralCamp_" + camp.Id);
                    root.transform.SetParent(transform, false);
                    var body = GameObject.CreatePrimitive(NeutralPrimitive(camp.Kind));
                    body.name = "NeutralShape"; body.transform.SetParent(root.transform, false);
                    body.transform.localScale = NeutralScale(camp.Kind);
                    Discard(body.GetComponent<Collider>());
                    var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    band.name = "NeutralOwnerBand"; band.transform.SetParent(root.transform, false);
                    band.transform.localPosition = new Vector3(0f, 0.18f, 0f);
                    band.transform.localScale = new Vector3(0.42f, 0.05f, 0.42f);
                    Discard(band.GetComponent<Collider>());
                    visual = new NeutralVisual
                    {
                        Root = root,
                        Body = body.GetComponent<Renderer>(),
                        OwnerBand = band.GetComponent<Renderer>(),
                        HealthBar = WorldHealthBarVisuals.Create(root.transform, "NeutralHp", 2.5f, 0.14f)
                    };
                    neutralVisuals.Add(camp.Id, visual);
                }
                var position = visible.TryGetValue(camp.Id, out var seen) ? seen.Position : camp.Position;
                int hp = visible.TryGetValue(camp.Id, out seen) ? seen.Hp : camp.LastKnownHp;
                int maxHp = visible.TryGetValue(camp.Id, out seen) ? seen.MaxHp : camp.LastKnownMaxHp;
                visual.Root.SetActive(true);
                visual.Root.transform.position = ToWorld(position, 0f);
                var visibleNeutral = visible.TryGetValue(camp.Id, out var current) ? current : default(VisibleNeutral);
                visual.Body.sharedMaterial = PresentationMaterials.GetNature(NeutralColor(camp.Kind, visibleNeutral.Element));
                visual.OwnerBand.sharedMaterial = PresentationMaterials.GetGlow(OwnerColor(visibleNeutral.OwnerFactionId, frame.FactionId));
                visual.OwnerBand.gameObject.SetActive(visibleNeutral.OwnerFactionId != 0);
                visual.HealthBar.Root.transform.position = ToWorld(position, 2.1f);
                visual.HealthBar.Root.transform.rotation = Camera.main == null ? Quaternion.identity : Camera.main.transform.rotation;
                WorldHealthBarVisuals.Set(visual.HealthBar, hp, maxHp, ToWorld(position, 2.1f), Camera.main == null ? Quaternion.identity : Camera.main.transform.rotation);
                if (visual.LabelHp != hp || visual.LabelMaxHp != maxHp)
                {
                    visual.LabelHp = hp; visual.LabelMaxHp = maxHp;
                    string element = visible.TryGetValue(camp.Id, out seen) && seen.Element != DragonElement.None ? " " + seen.Element : "";
                    visual.LabelText = UiText.NeutralName(camp.Kind) + element + " HP " + hp + "/" + maxHp;
                }
                visual.LabelPosition = ToWorld(position, 2.65f);
            }
            var remove = new List<uint>();
            foreach (var pair in neutralVisuals) if (!live.Contains(pair.Key)) remove.Add(pair.Key);
            foreach (uint id in remove) { Discard(neutralVisuals[id].Root); neutralVisuals.Remove(id); }
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
            foreach (var visual in neutralVisuals.Values) Discard(visual.Root);
            neutralVisuals.Clear();
            foreach (var mark in blessingMarks.Values) Discard(mark);
            blessingMarks.Clear();
        }
    }
}
