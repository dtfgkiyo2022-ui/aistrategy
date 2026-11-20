using System.Collections.Generic;
using Rts.Contracts;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Combat alerts and the minimap (11-19, display only). Reads FactionFrame and the display terrain; nothing here
    /// goes back into the simulation. Everything is made once and reused (a TextMesh rebuilt every frame once made the
    /// player build's memory grow, 11-17).
    /// </summary>
    public sealed partial class BattlefieldView
    {
        private const int CombatRingCount = 8;
        private const int MinimapTextureWidth = 256;
        private const float MinimapMargin = 16f;
        private const float MinimapWidthPixels = 256f;
        private const float MinimapRefreshSeconds = 0.5f;
        private const float AlertCountRadius = 30f;

        private readonly List<CombatAlertRules.Alert> combatAlerts = new List<CombatAlertRules.Alert>();
        private readonly List<CombatAlertRules.HealthSample> previousAlertSamples = new List<CombatAlertRules.HealthSample>();
        private readonly List<CombatAlertRules.HealthSample> currentAlertSamples = new List<CombatAlertRules.HealthSample>();
        private readonly List<LineRenderer> combatRings = new List<LineRenderer>();
        private readonly string[] alertLines = new string[3];
        private Texture2D minimapTexture;
        private Texture2D arrowTexture;
        private Color32[] minimapPixels;
        private int minimapTextureHeight;
        private float nextMinimapRefresh;
        private int alertCursor;
        private int lastSpaceFrame = -1;
        private bool minimapDragging;
        private BattlefieldCamera alertCamera;

        private float MinimapHeightPixels => minimapTextureHeight <= 0 ? 128f : MinimapWidthPixels * minimapTextureHeight / MinimapTextureWidth;

        private void EnsureCombatAlertAssets()
        {
            if (arrowTexture == null)
            {
                arrowTexture = new Texture2D(20, 20, TextureFormat.RGBA32, false);
                for (int y = 0; y < 20; y++)
                    for (int x = 0; x < 20; x++)
                        arrowTexture.SetPixel(x, y, y >= Mathf.Abs(x - 9.5f) * 1.7f ? Color.white : Color.clear);
                arrowTexture.Apply(false, true);
            }
            if (minimapTexture == null && currentTerrain != null && currentTerrain.WidthCells > 0)
            {
                minimapTextureHeight = Mathf.Clamp(Mathf.RoundToInt(MinimapTextureWidth * (float)currentTerrain.HeightCells / currentTerrain.WidthCells), 32, 256);
                minimapTexture = new Texture2D(MinimapTextureWidth, minimapTextureHeight, TextureFormat.RGBA32, false);
                minimapTexture.filterMode = FilterMode.Point; minimapTexture.wrapMode = TextureWrapMode.Clamp;
                minimapPixels = new Color32[MinimapTextureWidth * minimapTextureHeight];
            }
            while (combatRings.Count < CombatRingCount)
            {
                var go = new GameObject("CombatAlertRing"); go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.loop = true;
                line.positionCount = 48; line.numCapVertices = 2; line.enabled = false;
                combatRings.Add(line);
            }
        }

        private void SyncCombatAlerts(FactionFrame previous, FactionFrame frame)
        {
            if (!DisplaySettingsRuntime.CombatAlerts || frame == null) return;
            EnsureCombatAlertAssets();
            previousAlertSamples.Clear(); currentAlertSamples.Clear();
            AddAlertSamples(previous, previousAlertSamples); AddAlertSamples(frame, currentAlertSamples);
            var detected = CombatAlertRules.Detect(previousAlertSamples, currentAlertSamples, Time.unscaledTime);
            CombatAlertRules.Accumulate(combatAlerts, detected, Time.unscaledTime);
            if (alertCursor >= combatAlerts.Count) alertCursor = 0;
            if (Time.unscaledTime >= nextMinimapRefresh) { RefreshMinimap(frame); nextMinimapRefresh = Time.unscaledTime + MinimapRefreshSeconds; }
        }

        private void AddAlertSamples(FactionFrame frame, List<CombatAlertRules.HealthSample> target)
        {
            if (frame == null || frame.Units == null) return;
            foreach (var unit in frame.Units)
            {
                if (!unit.HasHp || unit.Kind == UnitKind.Villager) continue;
                bool visible = unit.IsOwn || IsCellVisible(frame, unit.Position);
                target.Add(new CombatAlertRules.HealthSample(unit.Id, unit.Position.X.Raw / Fix64Scale,
                    unit.Position.Z.Raw / Fix64Scale, unit.Hp, unit.IsOwn, visible));
            }
        }

        private bool IsCellVisible(FactionFrame frame, SimPoint point)
        {
            if (frame.Fog == null || frame.Fog.VisibleCells == null || currentTerrain == null) return false;
            float size = currentTerrain.CellSizeMeters <= 0f ? 2f : currentTerrain.CellSizeMeters;
            int x = (int)(point.X.Raw / Fix64Scale / size), z = (int)(point.Z.Raw / Fix64Scale / size);
            int width = currentTerrain.WidthCells;
            if (width <= 0 || x < 0 || z < 0 || x >= width || z >= frame.Fog.VisibleCells.Count / width) return false;
            return frame.Fog.VisibleCells[z * width + x];
        }

        /// <summary>Every rendered frame: each fight's red ring spreads out on the ground and fades.</summary>
        private void UpdateCombatAlertRings()
        {
            if (!DisplaySettingsRuntime.CombatAlerts) { foreach (var ring in combatRings) ring.enabled = false; return; }
            EnsureCombatAlertAssets();
            for (int i = 0; i < combatRings.Count; i++)
            {
                var ring = combatRings[i];
                if (i >= combatAlerts.Count) { ring.enabled = false; continue; }
                var alert = combatAlerts[i];
                float phase = Mathf.Repeat(Time.unscaledTime * 1.2f + i * 0.17f, 1f);
                float radius = 2f + phase * 10f;
                // Own soldiers being hit: deep red. Only the enemy being hit (we attack): orange.
                var colour = alert.OwnUnderAttack ? new Color(0.95f, 0.05f, 0.03f) : new Color(1f, 0.5f, 0.05f);
                colour.a = (1f - phase) * 0.85f;
                ring.sharedMaterial = PresentationMaterials.GetUnlit(alert.OwnUnderAttack ? new Color(0.95f, 0.05f, 0.03f, 0.8f) : new Color(1f, 0.5f, 0.05f, 0.8f));
                ring.startColor = colour; ring.endColor = colour;
                ring.widthMultiplier = 0.35f * (1f - phase * 0.5f);
                ring.enabled = true;
                var centre = new Vector3(alert.X, 0.12f, alert.Z);
                for (int p = 0; p < ring.positionCount; p++)
                {
                    float a = p * Mathf.PI * 2f / ring.positionCount;
                    ring.SetPosition(p, centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                }
            }
        }

        private void RefreshMinimap(FactionFrame frame)
        {
            EnsureCombatAlertAssets();
            if (currentTerrain == null || minimapPixels == null) return;
            int w = currentTerrain.WidthCells, h = currentTerrain.HeightCells;
            bool fog = frame != null && frame.Fog != null && frame.Fog.ExploredCells != null && frame.Fog.VisibleCells != null;
            for (int z = 0; z < minimapTextureHeight; z++)
                for (int x = 0; x < MinimapTextureWidth; x++)
                {
                    int tx = Mathf.Clamp((int)((x + .5f) / MinimapTextureWidth * w), 0, w - 1);
                    int tz = Mathf.Clamp((int)((z + .5f) / minimapTextureHeight * h), 0, h - 1);
                    byte kind = currentTerrain.KindAt(tx, tz);
                    Color32 c = kind == 2 ? new Color32(48, 110, 170, 255) : kind == 3 ? new Color32(106, 105, 100, 255)
                        : kind == 1 ? new Color32(48, 105, 57, 255) : new Color32(142, 126, 75, 255);
                    if (fog)
                    {
                        int index = tz * w + tx;
                        if (index >= frame.Fog.ExploredCells.Count || !frame.Fog.ExploredCells[index]) c = new Color32(12, 12, 18, 255);
                        else if (index < frame.Fog.VisibleCells.Count && !frame.Fog.VisibleCells[index])
                            c = new Color32((byte)(c.r * 55 / 100), (byte)(c.g * 55 / 100), (byte)(c.b * 55 / 100), 255);
                    }
                    minimapPixels[z * MinimapTextureWidth + x] = c;
                }
            if (frame != null)
            {
                // Remembered enemy buildings (only ones this side has seen), then units: own, and enemies only in sight.
                if (frame.RememberedBuildings != null)
                    foreach (var building in frame.RememberedBuildings)
                        PutMinimap(building.Position.X.Raw / Fix64Scale, building.Position.Z.Raw / Fix64Scale, new Color32(170, 40, 40, 255), 2);
                if (frame.VisibleNeutrals != null)
                    foreach (var neutral in frame.VisibleNeutrals)
                        if (neutral.Alive) PutMinimap(neutral.Position.X.Raw / Fix64Scale, neutral.Position.Z.Raw / Fix64Scale, new Color32(200, 200, 200, 255), 1);
                if (frame.NeutralCamps != null)
                    foreach (var camp in frame.NeutralCamps)
                        if (camp.LastKnownAlive || camp.RespawnTick > frame.Tick)
                            PutMinimap(camp.Position.X.Raw / Fix64Scale, camp.Position.Z.Raw / Fix64Scale, new Color32(220, 180, 80, 255), 2);
                foreach (var unit in frame.Units)
                {
                    if (!unit.IsOwn && !MinimapRules.CanDrawEnemy(IsCellVisible(frame, unit.Position))) continue;
                    PutMinimap(unit.Position.X.Raw / Fix64Scale, unit.Position.Z.Raw / Fix64Scale,
                        unit.IsOwn ? new Color32(60, 170, 255, 255) : new Color32(235, 60, 60, 255), 1);
                }
                if (OwnCoreWorld.HasValue) PutMinimap(OwnCoreWorld.Value.x, OwnCoreWorld.Value.z, new Color32(255, 255, 255, 255), 3);
            }
            minimapTexture.SetPixels32(minimapPixels); minimapTexture.Apply(false, false);
        }

        private void PutMinimap(float x, float z, Color32 colour, int radius)
        {
            float worldWidth = currentTerrain.WidthCells * currentTerrain.CellSizeMeters;
            float worldHeight = currentTerrain.HeightCells * currentTerrain.CellSizeMeters;
            var p = MinimapRules.ToMinimap(x, z, worldWidth, worldHeight, 0f, 0f, MinimapTextureWidth, minimapTextureHeight);
            int px = Mathf.Clamp((int)p.X, 0, MinimapTextureWidth - 1), pz = Mathf.Clamp((int)p.Y, 0, minimapTextureHeight - 1);
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dz * dz > radius * radius) continue;
                    int xx = px + dx, zz = pz + dz;
                    if (xx >= 0 && zz >= 0 && xx < MinimapTextureWidth && zz < minimapTextureHeight) minimapPixels[zz * MinimapTextureWidth + xx] = colour;
                }
        }

        private Rect MinimapGuiRect => new Rect(Screen.width - MinimapWidthPixels - MinimapMargin, Screen.height - MinimapHeightPixels - MinimapMargin,
            MinimapWidthPixels, MinimapHeightPixels);

        private void DrawCombatAlertsGui()
        {
            if (!DisplaySettingsRuntime.CombatAlerts) return;
            EnsureCombatAlertAssets();
            var mapRect = MinimapGuiRect;
            if (minimapTexture != null) UiHitAreas.Shared.Register(mapRect);
            var old = GUI.color;
            DrawOffscreenCombatArrows();
            bool japanese = UiText.Japanese;
            for (int i = 0; i < Mathf.Min(3, combatAlerts.Count); i++)
            {
                var a = combatAlerts[i];
                CountNear(a.X, a.Z, out int own, out int enemy);
                string direction = CombatAlertRules.Direction(OwnCoreWorld.HasValue ? OwnCoreWorld.Value.x : a.X, OwnCoreWorld.HasValue ? OwnCoreWorld.Value.z : a.Z, a.X, a.Z, japanese);
                alertLines[i] = CombatAlertRules.Line(direction, own, enemy, japanese);
                var rect = new Rect(16f, 74f + i * 28f, 380f, 24f);
                UiHitAreas.Shared.Register(rect);
                GUI.color = a.OwnUnderAttack ? new Color(1f, 0.55f, 0.5f, 0.95f) : new Color(1f, 0.85f, 0.55f, 0.95f);
                if (GUI.Button(rect, alertLines[i])) ShowCombatAlert(a);
            }
            GUI.color = Color.white;
            if (minimapTexture != null)
            {
                GUI.DrawTexture(mapRect, minimapTexture);
                DrawMinimapOverlay(mapRect);
                HandleMinimap(mapRect);
            }
            // OnGUI runs several times a frame (layout, repaint, input), so the Space key is read once per frame.
            if (lastSpaceFrame != Time.frameCount && Input.GetKeyDown(KeyCode.Space) && !UiHitAreas.Shared.KeyboardCaptured && combatAlerts.Count > 0)
            {
                lastSpaceFrame = Time.frameCount;
                if (alertCursor >= combatAlerts.Count) alertCursor = 0;
                ShowCombatAlert(combatAlerts[alertCursor]);
                alertCursor = (alertCursor + 1) % combatAlerts.Count;
            }
            GUI.color = old;
        }

        /// <summary>Visible soldiers of each side within the fight's radius, for the alert line.</summary>
        private void CountNear(float x, float z, out int own, out int enemy)
        {
            own = 0; enemy = 0;
            if (latestFrame == null) return;
            foreach (var unit in latestFrame.Units)
            {
                if (!unit.HasHp || unit.Kind == UnitKind.Villager) continue;
                float dx = unit.Position.X.Raw / Fix64Scale - x, dz = unit.Position.Z.Raw / Fix64Scale - z;
                if (dx * dx + dz * dz > AlertCountRadius * AlertCountRadius) continue;
                if (unit.IsOwn) own++;
                else if (IsCellVisible(latestFrame, unit.Position)) enemy++;
            }
        }

        /// <summary>The camera's view as a frame and the fights as blinking dots, drawn over the minimap.</summary>
        private void DrawMinimapOverlay(Rect mapRect)
        {
            float worldWidth = currentTerrain.WidthCells * currentTerrain.CellSizeMeters;
            float worldHeight = currentTerrain.HeightCells * currentTerrain.CellSizeMeters;
            var camera = Camera.main;
            if (camera != null)
            {
                var ground = new Plane(Vector3.up, Vector3.zero);
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue; bool any = false;
                Vector2[] corners = { new Vector2(0f, 0f), new Vector2(Screen.width, 0f), new Vector2(0f, Screen.height), new Vector2(Screen.width, Screen.height) };
                foreach (var corner in corners)
                {
                    var ray = camera.ScreenPointToRay(corner);
                    if (!ground.Raycast(ray, out float distance)) continue;
                    var hit = ray.GetPoint(distance); any = true;
                    minX = Mathf.Min(minX, hit.x); maxX = Mathf.Max(maxX, hit.x); minZ = Mathf.Min(minZ, hit.z); maxZ = Mathf.Max(maxZ, hit.z);
                }
                if (any)
                {
                    var a = MinimapRules.ToMinimap(Mathf.Clamp(minX, 0f, worldWidth), Mathf.Clamp(minZ, 0f, worldHeight), worldWidth, worldHeight, 0f, 0f, mapRect.width, mapRect.height);
                    var b = MinimapRules.ToMinimap(Mathf.Clamp(maxX, 0f, worldWidth), Mathf.Clamp(maxZ, 0f, worldHeight), worldWidth, worldHeight, 0f, 0f, mapRect.width, mapRect.height);
                    // Minimap points have a bottom-left origin; GUI rectangles a top-left one.
                    var frameRect = new Rect(mapRect.x + a.X, mapRect.yMax - b.Y, Mathf.Max(2f, b.X - a.X), Mathf.Max(2f, b.Y - a.Y));
                    GUI.color = new Color(1f, 1f, 1f, 0.9f);
                    GUI.DrawTexture(new Rect(frameRect.x, frameRect.y, frameRect.width, 1f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(frameRect.x, frameRect.yMax - 1f, frameRect.width, 1f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(frameRect.x, frameRect.y, 1f, frameRect.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(frameRect.xMax - 1f, frameRect.y, 1f, frameRect.height), Texture2D.whiteTexture);
                }
            }
            bool blink = Mathf.Repeat(Time.unscaledTime * 3f, 1f) < 0.6f;
            if (blink)
                foreach (var alert in combatAlerts)
                {
                    var p = MinimapRules.ToMinimap(alert.X, alert.Z, worldWidth, worldHeight, 0f, 0f, mapRect.width, mapRect.height);
                    GUI.color = alert.OwnUnderAttack ? new Color(1f, 0.1f, 0.05f, 1f) : new Color(1f, 0.6f, 0.1f, 1f);
                    GUI.DrawTexture(new Rect(mapRect.x + p.X - 4f, mapRect.yMax - p.Y - 4f, 8f, 8f), Texture2D.whiteTexture);
                }
            GUI.color = Color.white;
        }

        private void ShowCombatAlert(CombatAlertRules.Alert alert)
        {
            if (alertCamera == null) alertCamera = FindFirstObjectByType<BattlefieldCamera>();
            if (alertCamera != null) alertCamera.ShowPoint(new Vector3(alert.X, 0f, alert.Z), 55f);
        }

        private void DrawOffscreenCombatArrows()
        {
            var camera = Camera.main; if (camera == null || arrowTexture == null) return;
            int drawn = 0; Vector2 centre = new Vector2(Screen.width * .5f, Screen.height * .5f);
            // Fights where own soldiers are hit come first.
            for (int pass = 0; pass < 2; pass++)
                foreach (var alert in combatAlerts)
                {
                    if (drawn >= 4) return;
                    if (alert.OwnUnderAttack != (pass == 0)) continue;
                    Vector3 projected = camera.WorldToScreenPoint(new Vector3(alert.X, 0f, alert.Z));
                    if (projected.z > 0f && projected.x > 12f && projected.x < Screen.width - 12f && projected.y > 12f && projected.y < Screen.height - 12f) continue;
                    Vector2 target = new Vector2(projected.x, projected.y); if (projected.z <= 0f) target = centre - (target - centre);
                    Vector2 direction = target - centre; if (direction.sqrMagnitude < 1f) continue; direction.Normalize();
                    float edge = Mathf.Min(Screen.width, Screen.height) * .5f - 28f;
                    Vector2 screenPoint = centre + direction * edge;
                    // GUI coordinates have a top-left origin, so y flips and the angle turns the other way.
                    var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
                    float angle = 90f - Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                    var matrix = GUI.matrix;
                    GUIUtility.RotateAroundPivot(angle + 180f, guiPoint);
                    GUI.color = alert.OwnUnderAttack ? new Color(1f, 0.1f, 0.05f, 0.95f) : new Color(1f, 0.6f, 0.1f, 0.95f);
                    GUI.DrawTexture(new Rect(guiPoint.x - 12f, guiPoint.y - 12f, 24f, 24f), arrowTexture);
                    GUI.matrix = matrix;
                    drawn++;
                }
            GUI.color = Color.white;
        }

        private void HandleMinimap(Rect rect)
        {
            if (!Input.GetMouseButton(0)) { minimapDragging = false; return; }
            var guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (Input.GetMouseButtonDown(0)) minimapDragging = rect.Contains(guiMouse);
            if (!minimapDragging) return;
            float worldWidth = currentTerrain.WidthCells * currentTerrain.CellSizeMeters;
            float worldHeight = currentTerrain.HeightCells * currentTerrain.CellSizeMeters;
            // Input.mousePosition has a bottom-left origin, like the minimap points.
            float fromBottom = Input.mousePosition.y - (Screen.height - rect.yMax);
            var world = MinimapRules.ToWorld(Input.mousePosition.x - rect.x, fromBottom, worldWidth, worldHeight, 0f, 0f, rect.width, rect.height);
            if (alertCamera == null) alertCamera = FindFirstObjectByType<BattlefieldCamera>();
            if (alertCamera != null) alertCamera.ShowPoint(new Vector3(world.X, 0f, world.Y), 65f);
        }
    }
}
