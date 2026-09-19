using System.IO;
using Rts.Contracts;
using Rts.Presentation;
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

        private static (BattlefieldView view, Camera camera) Populate()
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
            var host = root.AddComponent<MockBattlefieldHost>();
            var selector = root.AddComponent<BattlefieldSelector>();
            var panel = root.AddComponent<CommandPanel>();
            var panelHost = new SerializedObject(host);
            panelHost.FindProperty("panel").objectReferenceValue = panel;
            panelHost.ApplyModifiedPropertiesWithoutUndo();
            var selectorSerialized = new SerializedObject(selector);
            selectorSerialized.FindProperty("view").objectReferenceValue = view;
            selectorSerialized.FindProperty("panel").objectReferenceValue = panel;
            selectorSerialized.ApplyModifiedPropertiesWithoutUndo();
            var serialized = new SerializedObject(host);
            serialized.FindProperty("view").objectReferenceValue = view;
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
