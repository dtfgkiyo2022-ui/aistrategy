using System.IO;
using Rts.Contracts;
using Rts.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rts.Editor
{
    /// <summary>
    /// Photographs the soldiers as the game creates them (LocalVisualPack: the game's own model first), each in three
    /// poses of the pack's clips (idle, mid-walk, mid-attack), to check the Humanoid retargeting and the held weapons.
    /// Batch: Unity -batchmode -executeMethod Rts.Editor.UnitModelPreview.Capture -rts-out &lt;png&gt; (not -nographics).
    /// Colours may be wrong in a batch run (see NeutralModelPreview); shapes and poses are reliable.
    /// </summary>
    public static class UnitModelPreview
    {
        public static void Capture()
        {
            string output = "unit_models.png";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-rts-out") output = args[i + 1];
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
            var kinds = new[] { UnitKind.Villager, UnitKind.Infantry, UnitKind.HeavyInfantry, UnitKind.Scout,
                UnitKind.Archer, UnitKind.SkirmishArcher, UnitKind.Mercenary, UnitKind.Monk };
            for (int k = 0; k < kinds.Length; k++)
            {
                for (int pose = 0; pose < 3; pose++)
                {
                    var holder = new GameObject(kinds[k] + "_" + pose);
                    holder.transform.position = new Vector3((k - kinds.Length / 2f) * 3.2f, 0f, pose * 3.5f);
                    holder.transform.rotation = Quaternion.Euler(0f, 180f + 25f, 0f);
                    if (!LocalVisualPack.TryCreateUnit(kinds[k], pose != 1, holder.transform, out var unit)) continue;
                    var animation = LocalVisualPack.TryCreateAnimation(unit, kinds[k], (ulong)(k + 1));
                    if (animation == null) { Debug.Log("RTS unit preview: no animation for " + kinds[k]); continue; }
                    animation.EvaluateAt(pose, pose == 0 ? 0.3 : pose == 1 ? 0.25 : 0.35);
                    if (pose == 0)
                        Debug.Log("RTS unit preview: " + kinds[k] + " custom=" + CustomUnitArt.Has(kinds[k]));
                }
            }
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 9f, -14f);
            camera.transform.LookAt(new Vector3(0f, 0.5f, 3.5f));
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.35f, 0.42f, 0.5f);
            var target = new RenderTexture(2000, 1000, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            picture.Apply();
            File.WriteAllBytes(output, picture.EncodeToPNG());
            Debug.Log("RTS unit preview -> " + output);
        }
    }
}
