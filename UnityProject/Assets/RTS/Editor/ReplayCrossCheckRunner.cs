using System.IO;
using Rts.UnityHost;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rts.Editor
{
    /// <summary>Batch entries for Issue 4-1. Editor (Mono) replay, and a Windows IL2CPP player build that does the same.</summary>
    public static class ReplayCrossCheckRunner
    {
        private const string PlayerScenePath = "Assets/RTS/Scenes/ReplayCheck.unity";

        // Unity.exe -batchmode -executeMethod Rts.Editor.ReplayCrossCheckRunner.RunInEditor -replay X -hashes Y -summary Z -quit
        public static void RunInEditor()
        {
            int exit = ReplayCrossCheck.RunFromCommandLine();
            Debug.Log("[ReplayCrossCheck] Editor (" + ReplayCrossCheck.Backend + ") exit=" + exit);
            EditorApplication.Exit(exit);
        }

        // Unity.exe -batchmode -executeMethod Rts.Editor.ReplayCrossCheckRunner.BuildIl2cppPlayer -buildOut <dir> -quit
        public static void BuildIl2cppPlayer()
        {
            string dir = CommandLineArg("-buildOut") ?? "D:/rts-verify/28/il2cpp-player";
            Directory.CreateDirectory(dir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("ReplayCheck").AddComponent<ReplayCheckBootstrap>();
            EditorSceneManager.SaveScene(scene, PlayerScenePath);

            var previous = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            BuildResult result;
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { PlayerScenePath },
                    locationPathName = Path.Combine(dir, "ReplayCheck.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });
                result = report.summary.result;
                Debug.Log("[ReplayCrossCheck] IL2CPP build result=" + result + " errors=" + report.summary.totalErrors
                    + " seconds=" + report.summary.totalTime.TotalSeconds);
            }
            finally
            {
                // Put the project back before exiting: the temporary scene and the scripting backend must not stay behind.
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previous);
                AssetDatabase.DeleteAsset(PlayerScenePath);
            }
            EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
        }

        private static string CommandLineArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
