using System.IO;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Replay;
using Rts.UnityHost;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rts.Editor
{
    public static class MockBattlefieldSceneBuilder
    {
        private const string ScenePath = "Assets/RTS/Scenes/MockBattlefield.unity";
        private const string LiveScenePath = "Assets/RTS/Scenes/LiveBattlefield.unity";
        private const string ReplayScenePath = "Assets/RTS/Scenes/ReplayView.unity";

        [MenuItem("RTS/Create Replay View Scene")]
        public static void CreateReplayScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Populate(false, true);
            EditorSceneManager.SaveScene(scene, ReplayScenePath);
            Debug.Log("[ReplayView] Saved " + ReplayScenePath);
        }

        /// <summary>
        /// Issue #31: from each faction's view, over a real match, checks that nothing hidden reaches the display.
        /// Writes a report to D:/rts-verify/31 and logs "[LeakCheck] RESULT PASS|FAIL".
        /// </summary>
        public static void VerifyFogLeak()
        {
            const int ticks = 3000;
            var outDir = "D:/rts-verify/31";
            Directory.CreateDirectory(outDir);
            var report = new System.Text.StringBuilder();
            int totalFailures = 0;
            var map = Rts.Simulation.WeekTwoScenario.Create().Map;

            foreach (uint faction in new uint[] { 1, 2 })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var view = Populate(true).view;
                var host = Object.FindFirstObjectByType<LiveMatchHost>();
                ((IMatchClock)host).ViewFactionId = faction;
                host.Begin();

                int enemyFrameTotal = 0, hiddenEnemyInFrame = 0, hpLeak = 0, visualCountMismatch = 0;
                int interpolationIntoFog = 0, eventsInFog = 0, maxEnemies = 0, ghostsSeen = 0;
                long firstEnemyTick = -1;
                var positions = new System.Collections.Generic.List<Vector3>();

                for (int i = 0; i < ticks && !host.HasEnded; i++)
                {
                    host.StepOnce();
                    var frame = host.Frame;
                    var visible = frame.Fog.VisibleCells;
                    int enemies = 0;
                    foreach (var u in frame.Units)
                    {
                        if (u.IsOwn) continue;
                        enemies++;
                        if (!Visible(visible, map, u.Position.X.Raw / 65536f, u.Position.Z.Raw / 65536f)) hiddenEnemyInFrame++;
                        if (u.HasHp || u.Hp != 0) hpLeak++;
                    }
                    enemyFrameTotal += enemies;
                    if (enemies > maxEnemies) maxEnemies = enemies;
                    if (enemies > 0 && firstEnemyTick < 0) firstEnemyTick = frame.Tick;
                    if (view.EnemyVisualCount != enemies) visualCountMismatch++;
                    foreach (var contact in frame.Observation.Contacts) if (!contact.IsCurrentlyVisible && !contact.IsArmyContact) ghostsSeen++;

                    foreach (var alpha in new[] { 0f, 0.5f, 1f })
                    {
                        view.Apply(alpha);
                        view.CollectEnemyVisualPositions(positions);
                        foreach (var p in positions) if (!Visible(visible, map, p.x, p.z)) interpolationIntoFog++;
                    }

                    foreach (var e in frame.Events)
                        if ((e.Kind == EventKind.Attack || e.Kind == EventKind.Death)
                            && !Visible(visible, map, e.Position.X.Raw / 65536f, e.Position.Z.Raw / 65536f)) eventsInFog++;
                }

                int failures = hiddenEnemyInFrame + hpLeak + visualCountMismatch + interpolationIntoFog;
                totalFailures += failures;
                string line = "faction " + faction + ": ticks=" + host.Tick + " firstEnemyTick=" + firstEnemyTick + " maxEnemiesShown=" + maxEnemies
                    + " enemyUnitTicks=" + enemyFrameTotal + " | hiddenEnemyInFrame=" + hiddenEnemyInFrame + " enemyHpLeak=" + hpLeak
                    + " visualCountMismatch=" + visualCountMismatch + " interpolationIntoFog=" + interpolationIntoFog
                    + " | attackOrDeathEventsAtFoggedPosition=" + eventsInFog + " ghostContactTicks=" + ghostsSeen;
                report.AppendLine(line);
                Debug.Log("[LeakCheck] " + line);
                view.Apply(1f);
                Render(Path.Combine(outDir, "faction" + faction + ".png"));
            }

            string result = totalFailures == 0 ? "PASS" : "FAIL";
            report.AppendLine("RESULT " + result);
            File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
            Debug.Log("[LeakCheck] RESULT " + result);
        }

        private static bool Visible(System.Collections.Generic.IReadOnlyList<bool> visible, Rts.Simulation.MapDefinition map, float x, float z)
        {
            int cx = Mathf.FloorToInt(x / map.CellSizeMeters), cz = Mathf.FloorToInt(z / map.CellSizeMeters);
            if (cx < 0 || cz < 0 || cx >= map.WidthCells || cz >= map.HeightCells) return false;
            return visible[cz * map.WidthCells + cx];
        }

        // Batch entry: records a short match, then plays it back through the display without Play mode.
        public static void CaptureReplay()
        {
            var outDir = "D:/rts-verify/live";
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "sample.rtsreplay");
            var scenario = Rts.Simulation.WeekTwoScenario.Create();
            var build = new BuildIdentity { Commit = "editor", SourceHash = new string('a', 64), Backend = "editor" };
            var inputs = Rts.Application.PolicyPresets.RecordedInputs(scenario, "none", "maintain", 400);
            using (var file = File.Create(path))
                Rts.Application.ReplayRunner.Record(file, scenario, inputs, 400, build, null, "none", "maintain");
            Debug.Log("[ReplayView] Recorded " + path + " inputs=" + inputs.Length);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var view = Populate(false, true).view;
            var host = Object.FindFirstObjectByType<ReplayViewHost>();
            host.Folder = outDir;
            host.Refresh();
            Debug.Log("[ReplayView] " + host.Status + " files=" + host.Files.Length);
            bool opened = host.Open(path);
            Debug.Log("[ReplayView] opened=" + opened + " " + host.Status);

            var clock = (IMatchClock)host;
            for (int i = 0; i < 400; i++) clock.StepOneTick();
            var frame = view.LatestFrame;
            int own = 0, enemy = 0;
            foreach (var u in frame.Units) { if (u.IsOwn) own++; else enemy++; }
            Debug.Log("[ReplayView] played tick=" + host.Tick + " faction=" + frame.FactionId + " own=" + own
                + " enemy=" + enemy + " enemyVisuals=" + view.EnemyVisualCount + " mismatch=" + host.MismatchTick
                + " commands=" + frame.Commands.Count);
            view.Apply(1f);
            Render(Path.Combine(outDir, "replay.png"));
        }

        /// <summary>Opens the live match scene without pressing Play, so the Game view is ready before you press it.</summary>
        [MenuItem("RTS/Open Live Battlefield")]
        public static void OpenLive()
        {
            EditorSceneManager.OpenScene(LiveScenePath);
        }

        /// <summary>One click to play: opens the live match scene and presses Play.</summary>
        [MenuItem("RTS/Play Live Battlefield")]
        public static void PlayLive()
        {
            EditorSceneManager.OpenScene(LiveScenePath);
            EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
        }

        // Batch entry: enters real Play mode on the live scene, lets it run, and renders the Game camera to a PNG.
        public static void PlaySmoke()
        {
            Directory.CreateDirectory("D:/rts-verify/play");
            EditorSceneManager.OpenScene(LiveScenePath);
            // Keep this script's callbacks alive across Play (a domain reload would drop them). Reverted from git afterwards.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            int frames = 0;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    EditorApplication.update += () =>
                    {
                        frames++;
                        if (frames != 200) return;
                        var cam = Camera.main;
                        Debug.Log("[PlaySmoke] camera=" + (cam == null ? "null" : cam.transform.position + " rot=" + cam.transform.eulerAngles));
                        var view = Object.FindFirstObjectByType<BattlefieldView>();
                        var host = Object.FindFirstObjectByType<LiveMatchHost>();
                        int renderers = view.GetComponentsInChildren<Renderer>().Length;
                        Debug.Log("[PlaySmoke] tick=" + host.Tick + " renderers=" + renderers + " enemyVisuals=" + view.EnemyVisualCount);
                        Render("D:/rts-verify/play/play.png");
                        EditorApplication.ExitPlaymode();
                    };
                }
                else if (state == PlayModeStateChange.EnteredEditMode && frames >= 200) EditorApplication.Exit(0);
            };
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("RTS/Create Live Battlefield Scene")]
        public static void CreateLiveScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Populate(true);
            EditorSceneManager.SaveScene(scene, LiveScenePath);
            Debug.Log("[LiveBattlefield] Saved " + LiveScenePath);
        }

        // Batch entry: runs the real Simulation through the gateway without Play mode and renders the result.
        public static void CaptureLive()
        {
            var outDir = "D:/rts-verify/live";
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var built = Populate(true);
            var view = built.view;
            var host = Object.FindFirstObjectByType<LiveMatchHost>();
            host.Begin();
            LogLive(host, view, "t0");

            var army = new ScopeKey(1, ScopeKind.Army, 1);
            var intent = new UserPolicyIntent(1, army, PolicyKind.Focus,
                new PolicyGoal(GoalKind.Outpost, 1, default(SimPoint)), 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.SubjectGone));
            ulong request = host.Submit(intent);
            Debug.Log("[LiveBattlefield] Submitted request " + request);

            for (int i = 0; i < 120; i++)
            {
                host.StepOnce();
                if (i == 0 || i == 40 || i == 119) LogLive(host, view, "t" + (i + 1));
            }
            view.Apply(1f);
            Render(Path.Combine(outDir, "live.png"));

            var timelinePanel = Object.FindFirstObjectByType<TimelinePanel>();
            var clock = (IMatchClock)host;
            clock.Paused = true;
            long before = clock.Tick;
            clock.StepOneTick();
            Debug.Log("[LiveBattlefield] clock paused=" + clock.Paused + " stepped " + before + " -> " + clock.Tick);
            clock.SpeedMultiplier = 4;
            clock.SpeedMultiplier = 3;
            Debug.Log("[LiveBattlefield] speed after 4 then invalid 3 = x" + clock.SpeedMultiplier);
            clock.Paused = false;

            for (int i = 0; i < 880 && !host.HasEnded; i++)
            {
                host.StepOnce();
                timelinePanel.Timeline.Ingest(host.Frame);
            }
            var entries = timelinePanel.Timeline.Entries;
            Debug.Log("[LiveBattlefield] timeline entries=" + entries.Count);
            for (int i = System.Math.Max(0, entries.Count - 8); i < entries.Count; i++)
                Debug.Log("[LiveBattlefield] timeline t" + entries[i].Tick + " " + entries[i].Text);

            clock.ViewFactionId = 2;
            var otherFrame = host.Frame;
            int otherOwn = 0, otherEnemy = 0;
            foreach (var u in otherFrame.Units) { if (u.IsOwn) otherOwn++; else otherEnemy++; }
            Debug.Log("[LiveBattlefield] switched view faction=" + otherFrame.FactionId + " own=" + otherOwn
                + " enemy=" + otherEnemy + " unitVisuals=" + (otherOwn + otherEnemy) + " enemyVisuals=" + view.EnemyVisualCount);
            view.Apply(1f);
            Render(Path.Combine(outDir, "live_faction2.png"));
            clock.ViewFactionId = 1;
            LogLive(host, view, "t1000");
            view.Apply(1f);
            Render(Path.Combine(outDir, "live_late.png"));
        }

        private static void LogLive(LiveMatchHost host, BattlefieldView view, string label)
        {
            var frame = host.Frame;
            int own = 0, enemy = 0;
            foreach (var u in frame.Units) { if (u.IsOwn) own++; else enemy++; }
            int visible = 0;
            foreach (var v in frame.Fog.VisibleCells) if (v) visible++;
            Debug.Log("[LiveBattlefield] " + label + " tick=" + frame.Tick + " faction=" + frame.FactionId
                + " own=" + own + " enemy=" + enemy + " visibleCells=" + visible + " alive=" + frame.AliveCount
                + "/" + frame.FactionCap + " unitVisuals=" + (own + enemy) + " enemyVisuals=" + view.EnemyVisualCount
                + " commands=" + frame.Commands.Count + " ended=" + frame.Result.HasEnded);
            foreach (var c in frame.Commands)
                Debug.Log("[LiveBattlefield] " + label + " cmd #" + c.CommandId + " " + c.Source + " " + c.Kind
                    + " " + c.Target.Kind + c.Target.Id + " [" + c.Status + "] reason=" + c.Reason
                    + " accepted=" + c.AcceptedTick + " apply=" + c.ApplyTick);
        }

        [MenuItem("RTS/Create Mock Battlefield Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Populate();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[MockBattlefield] Saved " + ScenePath);
        }

        // Batch entry: renders frame-interpolation stages to PNG files for checking without pressing Play.
        public static void Capture()
        {
            var outDir = "D:/rts-verify/11";
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var view = Populate().view;
            var source = new MockFrameSource();
            for (int i = 0; i < 400; i++) source.Advance();
            view.Push(source.Latest(1));
            source.Advance();
            view.Push(source.Latest(1));
            LogFog(view, source, "t400");
            foreach (var alpha in new[] { 0f, 0.5f, 1f })
            {
                view.Apply(alpha);
                Render(Path.Combine(outDir, "alpha_" + (int)(alpha * 100) + ".png"));
            }
            view.Apply(1f);
            var cam = Camera.main;
            foreach (var probe in new[] { new Vector3(24f, 2f, 64f), new Vector3(232f, 2f, 64f) })
            {
                var screen = cam.WorldToScreenPoint(probe);
                bool hit = view.TryPick(cam, screen, 36f, out var picked);
                Debug.Log("[MockBattlefield] Pick core at " + probe + " -> " + hit + " " + picked.Kind + " " + picked.Id);
            }
            var armyScreen = cam.WorldToScreenPoint(GameObject.Find("Army_2").transform.position);
            bool armyHit = view.TryPick(cam, armyScreen, 36f, out var armyPicked);
            Debug.Log("[MockBattlefield] Pick army 2 -> " + armyHit + " " + armyPicked.Kind + " " + armyPicked.Id);
            bool missHit = view.TryPick(cam, new Vector2(5f, 5f), 36f, out var miss);
            Debug.Log("[MockBattlefield] Pick empty -> " + missHit + " " + miss.Kind);
            view.Select(armyPicked);
            // Quantizer: nearest 1/256 m, midpoint away from zero, NaN/out-of-range rejected.
            bool q1 = Rts.Presentation.GroundPointQuantizer.TryQuantize(10.001953125f, 20f, 256f, 128f, out var qp1);
            bool q2 = Rts.Presentation.GroundPointQuantizer.TryQuantize(float.NaN, 20f, 256f, 128f, out _);
            bool q3 = Rts.Presentation.GroundPointQuantizer.TryQuantize(-1f, 20f, 256f, 128f, out _);
            bool q4 = Rts.Presentation.GroundPointQuantizer.TryQuantize(255.9999f, 127.9999f, 256f, 128f, out _);
            Debug.Log("[MockBattlefield] Quantize ok=" + q1 + " raw=" + qp1.X.Raw + "/" + qp1.Z.Raw + " nan=" + q2 + " neg=" + q3 + " edge=" + q4);
            Render(Path.Combine(outDir, "selected_army.png"));
            var port = new MockCommandPort();
            source.CommandProvider = port.Views;
            port.SetTick(source.Tick);
            UserPolicyIntent Attack(ulong seq, uint army, int x) => new UserPolicyIntent(seq, new ScopeKey(1, ScopeKind.Army, army), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, new SimPoint(Fix64.FromInt(x), Fix64.FromInt(96))), 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.SubjectGone));
            port.Submit(Attack(1, 1, 180));
            port.Submit(Attack(2, 2, 230));
            foreach (var delay in new[] { 0, 60, 200, 400 })
            {
                port.DelayTicks = delay;
                var delayed = port.Submit(Attack((ulong)(10 + delay), 1, 100));
                for (int t = 0; t <= 480; t += 40)
                {
                    port.SetTick(source.Tick + t);
                    foreach (var v in port.Views())
                        if (v.CommandId == delayed) Debug.Log("[MockBattlefield] Delay" + delay + " +" + t + " #" + v.CommandId + " " + v.Status + " " + v.Reason + " apply=" + (v.ApplyTick - v.AcceptedTick));
                }
                port.SetTick(source.Tick);
            }
            port.DelayTicks = 60;
            for (int i = 0; i < 30; i++) { source.Advance(); port.SetTick(source.Tick); }
            foreach (var v in port.Views()) Debug.Log("[MockBattlefield] Cmd age30 #" + v.CommandId + " " + v.Status + " " + v.Reason);
            for (int i = 0; i < 50; i++) { source.Advance(); port.SetTick(source.Tick); }
            view.Push(source.Latest(1));
            foreach (var v in port.Views()) Debug.Log("[MockBattlefield] Cmd age80 #" + v.CommandId + " " + v.Status + " " + v.Reason);
            view.Apply(1f);
            Render(Path.Combine(outDir, "arrows.png"));
            for (int i = 0; i < 300; i++) { source.Advance(); }
            view.Push(source.Latest(1));
            view.Apply(1f);
            LogFog(view, source, "t780");
            Render(Path.Combine(outDir, "fog.png"));
            for (int i = 0; i < 2300; i++) source.Advance();
            view.Push(source.Latest(1));
            view.Apply(1f);
            Render(Path.Combine(outDir, "low_hp.png"));
        }

        private static (BattlefieldView view, Camera camera) Populate(bool live = false, bool replay = false)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.12f);
            camera.transform.SetPositionAndRotation(new Vector3(128f, 120f, 10f), Quaternion.Euler(70f, 0f, 0f));
            camera.farClipPlane = 500f;
            cameraObject.AddComponent<BattlefieldCamera>();

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(128f, -0.05f, 64f);
            ground.transform.localScale = new Vector3(25.6f, 1f, 12.8f);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.GetComponent<Renderer>().sharedMaterial = PresentationMaterials.Get(new Color(0.28f, 0.4f, 0.25f));

            var root = new GameObject("Battlefield");
            var view = root.AddComponent<BattlefieldView>();
            view.SetTerrain(MockTerrain.Create());
            MonoBehaviour host = replay ? (MonoBehaviour)root.AddComponent<ReplayViewHost>()
                : live ? root.AddComponent<LiveMatchHost>() : root.AddComponent<MockBattlefieldHost>();
            var selector = root.AddComponent<BattlefieldSelector>();
            var panel = replay ? null : root.AddComponent<CommandPanel>();
            var selectorSerialized = new SerializedObject(selector);
            selectorSerialized.FindProperty("view").objectReferenceValue = view;
            selectorSerialized.FindProperty("panel").objectReferenceValue = panel;
            if (live || replay)
            {
                var timeline = root.AddComponent<TimelinePanel>();
                var timelineSerialized = new SerializedObject(timeline);
                timelineSerialized.FindProperty("view").objectReferenceValue = view;
                timelineSerialized.FindProperty("clockSource").objectReferenceValue = host;
                timelineSerialized.ApplyModifiedPropertiesWithoutUndo();
                selectorSerialized.FindProperty("timelinePanel").objectReferenceValue = timeline;
            }
            selectorSerialized.ApplyModifiedPropertiesWithoutUndo();
            var serialized = new SerializedObject(host);
            serialized.FindProperty("view").objectReferenceValue = view;
            if (panel != null) serialized.FindProperty("panel").objectReferenceValue = panel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var viewSerialized = new SerializedObject(view);
            SetModel(viewSerialized, "infantryModel", "Infantry");
            SetModel(viewSerialized, "scoutModel", "Scout");
            SetModel(viewSerialized, "coreModel", "Core");
            viewSerialized.ApplyModifiedPropertiesWithoutUndo();
            return (view, camera);
        }

        private static void LogFog(BattlefieldView view, MockFrameSource source, string label)
        {
            var frame = source.Latest(1);
            int visibleCells = 0, exploredCells = 0, enemiesInFrame = 0, enemiesInVisibleCells = 0;
            foreach (var v in frame.Fog.VisibleCells) if (v) visibleCells++;
            foreach (var e in frame.Fog.ExploredCells) if (e) exploredCells++;
            foreach (var u in frame.Units)
            {
                if (u.IsOwn) continue;
                enemiesInFrame++;
                int cx = (int)(u.Position.X.Raw / 65536 / 2), cz = (int)(u.Position.Z.Raw / 65536 / 2);
                if (frame.Fog.VisibleCells[cz * 128 + cx]) enemiesInVisibleCells++;
            }
            Debug.Log("[MockBattlefield] Fog " + label + " visible=" + visibleCells + " explored=" + exploredCells
                + " enemiesInFrame=" + enemiesInFrame + " inVisibleCells=" + enemiesInVisibleCells + " enemyVisuals=" + view.EnemyVisualCount);
            foreach (var l in view.BuildContactLabels()) Debug.Log("[MockBattlefield] Label " + label + ": " + l.Value);
        }

        private static void SetModel(SerializedObject target, string field, string modelName)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RTS/Models/Placeholder/" + modelName + ".fbx");
            if (model != null) target.FindProperty(field).objectReferenceValue = model;
        }

        private static void Render(string path)
        {
            var camera = Camera.main;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(texture);
            target.Release();
            Debug.Log("[MockBattlefield] Wrote " + path);
        }
    }
}
