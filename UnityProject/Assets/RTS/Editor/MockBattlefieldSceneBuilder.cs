using System.IO;
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
            foreach (var alpha in new[] { 0f, 0.5f, 1f })
            {
                view.Apply(alpha);
                Render(Path.Combine(outDir, "alpha_" + (int)(alpha * 100) + ".png"));
            }
            for (int i = 0; i < 2600; i++) source.Advance();
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
            var host = root.AddComponent<MockBattlefieldHost>();
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
