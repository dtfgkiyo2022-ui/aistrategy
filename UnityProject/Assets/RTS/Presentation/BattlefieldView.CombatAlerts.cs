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
        private const float MinimapRefreshSeconds = 0.5f;
        private const float MinimapHeaderHeight = 26f;
        private const float AlertCountRadius = 30f;

        private readonly List<CombatAlertRules.Alert> combatAlerts = new List<CombatAlertRules.Alert>();
        private readonly List<CombatAlertRules.HealthSample> previousAlertSamples = new List<CombatAlertRules.HealthSample>();
        private readonly List<CombatAlertRules.HealthSample> currentAlertSamples = new List<CombatAlertRules.HealthSample>();
        private readonly List<LineRenderer> combatRings = new List<LineRenderer>();
        private readonly string[] alertLines = new string[3];
        private Texture2D minimapTexture;
        // 11-20: the markers sit on their own see-through layer with sharp pixels, over the smoothed terrain.
        private Texture2D minimapMarkers;
        private Texture2D minimapDot, minimapRing, minimapDiamond;
        private Texture2D arrowTexture;
        private Color32[] minimapPixels;
        private Color32[] minimapMarkerPixels;
        private int minimapTextureHeight;
        private float nextMinimapRefresh;
        private int alertCursor;
        private int lastSpaceFrame = -1;
        private bool minimapDragging;
        private BattlefieldCamera alertCamera;
        private GUIStyle minimapTitleStyle, minimapSmallStyle;
        // The respawn text of each camp is rebuilt only when its second changes (no new string every frame).
        private readonly Dictionary<uint, long> campTimerSeconds = new Dictionary<uint, long>();
        private readonly Dictionary<uint, string> campTimerText = new Dictionary<uint, string>();

        private static Texture2D MinimapShape(int size, int shape)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = (size - 1) * 0.5f, r = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 0: filled dot, 1: ring, 2: diamond. Soft edges so they read well when scaled.
                    float a = shape == 0 ? Mathf.Clamp01(r + 0.5f - d)
                        : shape == 1 ? Mathf.Clamp01(1.6f - Mathf.Abs(d - (r - 1.2f)))
                        : Mathf.Clamp01(r + 0.5f - (Mathf.Abs(dx) + Mathf.Abs(dy)));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply(false, true);
            return texture;
        }

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
                minimapTexture.filterMode = FilterMode.Bilinear; minimapTexture.wrapMode = TextureWrapMode.Clamp;
                minimapPixels = new Color32[MinimapTextureWidth * minimapTextureHeight];
                minimapMarkers = new Texture2D(MinimapTextureWidth, minimapTextureHeight, TextureFormat.RGBA32, false);
                minimapMarkers.filterMode = FilterMode.Point; minimapMarkers.wrapMode = TextureWrapMode.Clamp;
                minimapMarkerPixels = new Color32[MinimapTextureWidth * minimapTextureHeight];
            }
            if (minimapDot == null) minimapDot = MinimapShape(16, 0);
            if (minimapRing == null) minimapRing = MinimapShape(20, 1);
            if (minimapDiamond == null) minimapDiamond = MinimapShape(20, 2);
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
                    // Shore: water next to land is shallower, land next to water is sand.
                    bool shore = false;
                    for (int n = 0; n < 4 && !shore; n++)
                    {
                        int nx = Mathf.Clamp(tx + (n == 0 ? 2 : n == 1 ? -2 : 0), 0, w - 1), nz = Mathf.Clamp(tz + (n == 2 ? 2 : n == 3 ? -2 : 0), 0, h - 1);
                        shore = (currentTerrain.KindAt(nx, nz) == 2) != (kind == 2);
                    }
                    Color32 c = kind == 2 ? (shore ? new Color32(74, 136, 178, 255) : new Color32(42, 92, 146, 255))
                        : shore ? new Color32(168, 152, 104, 255)
                        : kind == 3 ? new Color32(122, 116, 104, 255)
                        : kind == 1 ? new Color32(40, 88, 50, 255) : new Color32(112, 128, 74, 255);
                    // A little fixed grain so the ground does not look flat (display only).
                    int grain = 93 + (((tx * 73856093) ^ (tz * 19349663)) & 15);
                    c = new Color32((byte)Mathf.Min(255, c.r * grain / 100), (byte)Mathf.Min(255, c.g * grain / 100), (byte)Mathf.Min(255, c.b * grain / 100), 255);
                    if (fog)
                    {
                        int index = tz * w + tx;
                        if (index >= frame.Fog.ExploredCells.Count || !frame.Fog.ExploredCells[index])
                            c = ((x >> 3) + (z >> 3) & 1) == 0 ? new Color32(24, 26, 32, 255) : new Color32(29, 31, 38, 255);
                        else if (index < frame.Fog.VisibleCells.Count && !frame.Fog.VisibleCells[index])
                        {
                            // Seen before but not in sight now: darker and greyer.
                            int grey = (c.r + c.g + c.b) / 3;
                            c = new Color32((byte)((c.r * 45 + grey * 15) / 100), (byte)((c.g * 45 + grey * 15) / 100), (byte)((c.b * 45 + grey * 15) / 100), 255);
                        }
                    }
                    minimapPixels[z * MinimapTextureWidth + x] = c;
                }
            for (int i = 0; i < minimapMarkerPixels.Length; i++) minimapMarkerPixels[i] = new Color32(0, 0, 0, 0);
            if (frame != null)
            {
                // Buildings first (own from the economy view, enemy ones only as remembered), then units on top:
                // own ones, and enemies only while in sight. Camps, the core, the camera and fights are drawn in OnGUI.
                if (frame.Economy != null && frame.Economy.Buildings != null)
                    foreach (var building in frame.Economy.Buildings)
                        if (building.FactionId == frame.FactionId)
                            PutMinimapSquare(building.Center.X.Raw / Fix64Scale, building.Center.Z.Raw / Fix64Scale,
                                new Color32(70, 140, 235, 255), building.SizeMeters >= 8 ? 2 : 1);
                if (frame.RememberedBuildings != null)
                    foreach (var building in frame.RememberedBuildings)
                        PutMinimapSquare(building.Position.X.Raw / Fix64Scale, building.Position.Z.Raw / Fix64Scale,
                            new Color32(205, 52, 44, 255), building.SizeMeters >= 8 ? 2 : 1);
                if (frame.VisibleNeutrals != null)
                    foreach (var neutral in frame.VisibleNeutrals)
                        if (neutral.Alive) PutMinimapSquare(neutral.Position.X.Raw / Fix64Scale, neutral.Position.Z.Raw / Fix64Scale, new Color32(236, 196, 92, 255), 1);
                foreach (var unit in frame.Units)
                {
                    if (!unit.IsOwn && !MinimapRules.CanDrawEnemy(IsCellVisible(frame, unit.Position))) continue;
                    bool villager = unit.Kind == UnitKind.Villager;
                    var colour = unit.IsOwn ? (villager ? new Color32(176, 222, 255, 255) : new Color32(110, 196, 255, 255))
                        : (villager ? new Color32(255, 170, 160, 255) : new Color32(255, 78, 66, 255));
                    PutMinimapSquare(unit.Position.X.Raw / Fix64Scale, unit.Position.Z.Raw / Fix64Scale, colour, 0);
                }
            }
            minimapTexture.SetPixels32(minimapPixels); minimapTexture.Apply(false, false);
            minimapMarkers.SetPixels32(minimapMarkerPixels); minimapMarkers.Apply(false, false);
        }

        /// <summary>A filled square of side 2*half+1 with a dark rim, on the marker layer.</summary>
        private void PutMinimapSquare(float x, float z, Color32 colour, int half)
        {
            float worldWidth = currentTerrain.WidthCells * currentTerrain.CellSizeMeters;
            float worldHeight = currentTerrain.HeightCells * currentTerrain.CellSizeMeters;
            var p = MinimapRules.ToMinimap(x, z, worldWidth, worldHeight, 0f, 0f, MinimapTextureWidth, minimapTextureHeight);
            int px = Mathf.Clamp((int)p.X, 0, MinimapTextureWidth - 1), pz = Mathf.Clamp((int)p.Y, 0, minimapTextureHeight - 1);
            var rim = new Color32((byte)(colour.r / 4), (byte)(colour.g / 4), (byte)(colour.b / 4), 255);
            int outer = half + 1;
            for (int dz = -outer; dz <= outer; dz++)
                for (int dx = -outer; dx <= outer; dx++)
                {
                    int xx = px + dx, zz = pz + dz;
                    if (xx < 0 || zz < 0 || xx >= MinimapTextureWidth || zz >= minimapTextureHeight) continue;
                    bool edge = Mathf.Abs(dx) == outer || Mathf.Abs(dz) == outer;
                    int index = zz * MinimapTextureWidth + xx;
                    // A rim never paints over another marker's body, so crowds stay readable.
                    if (edge) { if (half > 0 && minimapMarkerPixels[index].a == 0) minimapMarkerPixels[index] = rim; }
                    else minimapMarkerPixels[index] = colour;
                }
        }

        private void DrawCombatAlertsGui()
        {
            if (!DisplaySettingsRuntime.CombatAlerts) return;
            EnsureCombatAlertAssets();
            var panelRect = UiLayout.Calculate(Screen.width, Screen.height).Minimap;
            if (minimapTexture != null) UiHitAreas.Shared.Register(panelRect);
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
                var mapRect = DrawMinimapPanel(panelRect);
                GUI.color = Color.white;
                GUI.DrawTexture(mapRect, minimapTexture);
                GUI.DrawTexture(mapRect, minimapMarkers);
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

        private static void FillRect(Rect rect, Color colour)
        {
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        private static void OutlineRect(Rect rect, Color colour, float thickness)
        {
            FillRect(new Rect(rect.x, rect.y, rect.width, thickness), colour);
            FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), colour);
            FillRect(new Rect(rect.x, rect.y, thickness, rect.height), colour);
            FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), colour);
        }

        private void EnsureMinimapStyles()
        {
            if (minimapTitleStyle != null) return;
            minimapTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            minimapTitleStyle.normal.textColor = new Color(0.93f, 0.80f, 0.52f);
            minimapSmallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Overflow };
            minimapSmallStyle.normal.textColor = new Color(0.88f, 0.86f, 0.80f);
        }

        /// <summary>
        /// The stone-and-brass frame, the title and the legend (11-20 test play: "make it look properly made").
        /// Returns where the map itself goes: as large as fits, keeping the map's shape, centred.
        /// </summary>
        private Rect DrawMinimapPanel(Rect panel)
        {
            EnsureMinimapStyles();
            FillRect(new Rect(panel.x + 3f, panel.y + 4f, panel.width, panel.height), new Color(0f, 0f, 0f, 0.35f));
            FillRect(panel, new Color(0.12f, 0.105f, 0.09f, 0.95f));
            OutlineRect(panel, new Color(0.72f, 0.58f, 0.33f, 1f), 2f);
            OutlineRect(new Rect(panel.x + 3f, panel.y + 3f, panel.width - 6f, panel.height - 6f), new Color(0.33f, 0.26f, 0.16f, 1f), 1f);

            var header = new Rect(panel.x + 10f, panel.y + 4f, panel.width - 20f, MinimapHeaderHeight - 4f);
            GUI.color = Color.white;
            GUI.Label(header, UiText.T("Map", "ミニマップ"), minimapTitleStyle);
            // Legend, right-aligned on the title row.
            float x = header.xMax;
            x = DrawLegendItem(x, header, minimapDot, new Color(1f, 0.25f, 0.1f), UiText.T("Fight", "戦闘"));
            x = DrawLegendItem(x, header, minimapRing, new Color(0.93f, 0.75f, 0.33f), UiText.T("Beasts", "中立"));
            x = DrawLegendItem(x, header, Texture2D.whiteTexture, new Color(1f, 0.31f, 0.26f), UiText.T("Enemy", "敵"));
            DrawLegendItem(x, header, Texture2D.whiteTexture, new Color(0.43f, 0.77f, 1f), UiText.T("Own", "自軍"));
            FillRect(new Rect(panel.x + 8f, panel.y + MinimapHeaderHeight + 1f, panel.width - 16f, 1f), new Color(0.72f, 0.58f, 0.33f, 0.55f));

            var area = new Rect(panel.x + 10f, panel.y + MinimapHeaderHeight + 6f, panel.width - 20f, panel.height - MinimapHeaderHeight - 14f);
            float scale = Mathf.Min(area.width / MinimapTextureWidth, area.height / Mathf.Max(1, minimapTextureHeight));
            float w = MinimapTextureWidth * scale, h = minimapTextureHeight * scale;
            var map = new Rect(area.x + (area.width - w) * 0.5f, area.y + (area.height - h) * 0.5f, w, h);
            OutlineRect(new Rect(map.x - 2f, map.y - 2f, map.width + 4f, map.height + 4f), new Color(0.05f, 0.04f, 0.03f, 1f), 2f);
            return map;
        }

        /// <summary>One legend entry drawn leftwards from <paramref name="right"/>; returns the new right edge.</summary>
        private float DrawLegendItem(float right, Rect row, Texture2D icon, Color colour, string label)
        {
            float textWidth = minimapSmallStyle.CalcSize(new GUIContent(label)).x;
            float x = right - textWidth;
            GUI.color = Color.white;
            GUI.Label(new Rect(x, row.y, textWidth, row.height), label, minimapSmallStyle);
            x -= 13f;
            GUI.color = colour;
            GUI.DrawTexture(new Rect(x, row.y + row.height * 0.5f - 4.5f, 9f, 9f), icon);
            return x - 10f;
        }

        private string CampTimerText(uint campId, long ticks)
        {
            long seconds = (ticks + 19) / 20;
            if (campTimerSeconds.TryGetValue(campId, out long cached) && cached == seconds && campTimerText.TryGetValue(campId, out string text))
                return text;
            text = (seconds / 60).ToString() + ":" + (seconds % 60).ToString("D2");
            campTimerSeconds[campId] = seconds; campTimerText[campId] = text;
            return text;
        }

        /// <summary>Camps (with the respawn time for the side that cleared them), the own core, the camera's view
        /// as a frame and the fights as blinking dots, drawn over the minimap.</summary>
        private void DrawMinimapOverlay(Rect mapRect)
        {
            float worldWidth = currentTerrain.WidthCells * currentTerrain.CellSizeMeters;
            float worldHeight = currentTerrain.HeightCells * currentTerrain.CellSizeMeters;
            var frame = latestFrame;
            if (frame != null && frame.NeutralCamps != null)
                foreach (var camp in frame.NeutralCamps)
                {
                    bool respawning = camp.RespawnTick > frame.Tick;
                    if (!camp.LastKnownAlive && !respawning) continue;
                    var p = MinimapRules.ToMinimap(camp.Position.X.Raw / Fix64Scale, camp.Position.Z.Raw / Fix64Scale, worldWidth, worldHeight, 0f, 0f, mapRect.width, mapRect.height);
                    var centre = new Vector2(mapRect.x + p.X, mapRect.yMax - p.Y);
                    GUI.color = respawning ? new Color(0.62f, 0.60f, 0.55f, 0.95f) : new Color(0.95f, 0.76f, 0.32f, 1f);
                    GUI.DrawTexture(new Rect(centre.x - 6f, centre.y - 6f, 12f, 12f), minimapRing);
                    if (!respawning) continue;
                    // 86.5: only the side that cleared the camp gets this time; others see nothing here.
                    string text = CampTimerText(camp.Id, camp.RespawnTick - frame.Tick);
                    var label = new Rect(centre.x + 7f, centre.y - 8f, 40f, 16f);
                    GUI.color = new Color(0f, 0f, 0f, 0.85f);
                    GUI.Label(new Rect(label.x + 1f, label.y + 1f, label.width, label.height), text, minimapSmallStyle);
                    GUI.color = new Color(1f, 0.92f, 0.70f, 1f);
                    GUI.Label(label, text, minimapSmallStyle);
                }
            if (OwnCoreWorld.HasValue)
            {
                var p = MinimapRules.ToMinimap(OwnCoreWorld.Value.x, OwnCoreWorld.Value.z, worldWidth, worldHeight, 0f, 0f, mapRect.width, mapRect.height);
                var centre = new Vector2(mapRect.x + p.X, mapRect.yMax - p.Y);
                GUI.color = new Color(0.05f, 0.15f, 0.35f, 1f);
                GUI.DrawTexture(new Rect(centre.x - 8f, centre.y - 8f, 16f, 16f), minimapDiamond);
                GUI.color = new Color(0.92f, 0.97f, 1f, 1f);
                GUI.DrawTexture(new Rect(centre.x - 6f, centre.y - 6f, 12f, 12f), minimapDiamond);
            }
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
                    GUI.DrawTexture(new Rect(mapRect.x + p.X - 6f, mapRect.yMax - p.Y - 6f, 12f, 12f), minimapDot);
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
