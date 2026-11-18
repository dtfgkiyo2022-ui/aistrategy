using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Rts.Editor
{
    /// <summary>
    /// Batch check of the soldier, villager and neutral-beast models per graphics tier (11-18). For each model in
    /// UnitModelsHD / NeutralModelsHD: the Humanoid ones need a valid human avatar, the others their idle/walk/attack
    /// clips; every model needs its 2048 base colour picture. Writes a report to -tierCheckOut and exits 1 on a problem.
    /// Unity.exe -batchmode -quit -projectPath ... -executeMethod Rts.Editor.TierModelCheck.Run -tierCheckOut report.txt
    /// </summary>
    public static class TierModelCheck
    {
        public static void Run()
        {
            string output = "TierModelCheck.txt";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-tierCheckOut") output = args[i + 1];
            AssetDatabase.Refresh();
            var report = new StringBuilder();
            int problems = 0, models = 0;
            foreach (string folder in new[] { "Assets/RTS/Art/Units/Resources/UnitModelsHD", "Assets/RTS/Art/Neutrals/Resources/NeutralModelsHD" })
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                    var avatar = assets.OfType<Avatar>().FirstOrDefault();
                    var clips = assets.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).Select(c => c.name.ToLowerInvariant()).ToArray();
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    int triangles = model == null ? 0 : model.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => r.sharedMesh == null ? 0 : r.sharedMesh.triangles.Length / 3)
                        + model.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh == null ? 0 : f.sharedMesh.triangles.Length / 3);
                    string file = Path.GetFileNameWithoutExtension(path);
                    string name = file.Substring(0, file.LastIndexOf('_'));
                    bool human = importer != null && importer.animationType == ModelImporterAnimationType.Human;
                    string problem = null;
                    if (model == null) problem = "no model";
                    else if (human && (avatar == null || !avatar.isValid || !avatar.isHuman)) problem = "human avatar not valid";
                    else if (!human && (!clips.Any(c => c.Contains("idle")) || !clips.Any(c => c.Contains("walk") || c.Contains("run")) || !clips.Any(c => c.Contains("attack") || c.Contains("bite"))))
                        problem = "clips missing (" + string.Join(",", clips) + ")";
                    else if (AssetDatabase.LoadAssetAtPath<Texture2D>(Path.GetDirectoryName(path).Replace('\\', '/') + "/" + name + "_BaseColor2k.jpg") == null)
                        problem = "no _BaseColor2k picture";
                    models++;
                    if (problem != null) problems++;
                    report.AppendLine(file + " " + (human ? "human" : "generic") + " tris=" + triangles + " clips=" + clips.Length + " " + (problem ?? "ok"));
                }
            }
            report.AppendLine("MODELS " + models + " PROBLEMS " + problems);
            File.WriteAllText(output, report.ToString());
            Debug.Log(report.ToString());
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(problems == 0 && models > 0 ? 0 : 1);
        }
    }
}
