using System.IO;
using Rts.Contracts;
using Rts.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rts.Editor
{
    /// <summary>
    /// Photographs every neutral beast model in a row (11-18), to check import, size, facing and the walk pose without a
    /// match. Batch: Unity -batchmode -executeMethod Rts.Editor.NeutralModelPreview.Capture -rts-out &lt;png&gt;
    /// (not -nographics: the camera has to render). The beasts should face the camera (the game's +Z is their front).
    /// Colours are not reliable here: in a batch run every material showed one and the same picture (11-18, a different
    /// one each run, even on plain quads), so check the colours in a player build. Shape, size and facing are reliable.
    /// </summary>
    public static class NeutralModelPreview
    {
        public static void Capture()
        {
            string output = "neutral_models.png";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-rts-out") output = args[i + 1];
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool batching = UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching;
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f); // from behind the camera, like the game's sun
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(4f, 1f, 2f);
            var kinds = new[] { NeutralKind.WolfPack, NeutralKind.RockBeast, NeutralKind.FlameHornBeast,
                NeutralKind.AzureSerpent, NeutralKind.ShellBeast, NeutralKind.AugmentGuardian };
            float x = -kinds.Length * 2.2f;
            int made = 0;
            foreach (var kind in kinds)
            {
                var holder = new GameObject(kind.ToString());
                holder.transform.position = new Vector3(x, 0f, 0f);
                // Turned 30 degrees so the side of the body shows; the front still faces the camera.
                holder.transform.rotation = Quaternion.Euler(0f, 180f + 30f, 0f);
                x += 4.4f;
                if (!NeutralModelArt.TryCreate(kind, holder.transform, 1, out var instance, out var animation))
                {
                    Debug.Log("RTS neutral preview: no model for " + kind);
                    continue;
                }
                made++;
                if (animation != null)
                {
                    animation.SetDesired(true, false, false, false);
                    for (int i = 0; i < 30; i++) animation.Tick(0.05f);
                }
                var shown = instance.GetComponentInChildren<Renderer>();
                var picture0 = shown == null || shown.sharedMaterial == null ? null : shown.sharedMaterial.GetTexture("_BaseMap");
                Debug.Log("RTS neutral preview: " + kind + " animated=" + (animation != null) + " renderers="
                    + instance.GetComponentsInChildren<Renderer>().Length + " material=" + (shown == null || shown.sharedMaterial == null ? "none" : shown.sharedMaterial.name)
                    + " texture=" + (picture0 == null ? "none" : picture0.name));
            }
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 7f, -16f);
            camera.transform.LookAt(new Vector3(0f, 1.2f, 0f));
            camera.fieldOfView = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.35f, 0.42f, 0.5f);
            var target = new RenderTexture(1800, 700, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            picture.Apply();
            File.WriteAllBytes(output, picture.EncodeToPNG());
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = batching;
            Debug.Log("RTS neutral preview: " + made + " models -> " + output);
        }
    }
}
