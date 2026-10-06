using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rts.Editor
{
    /// <summary>Builds the Windows IL2CPP player used by the product smoke command.</summary>
    public static class SmokeBuild
    {
        private const string DefaultOutput = "D:/rts-verify/smoke/player/Game.exe";

        public static void Build()
        {
            string requested = Value("-smokeOut") ?? DefaultOutput;
            string output = Path.GetExtension(requested).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? requested
                : Path.Combine(requested, "Game.exe");
            output = Path.GetFullPath(output);
            string parent = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/RTS/Scenes/LiveBattlefield.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.StrictMode
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Smoke build failed: " + report.summary.result);
            Debug.Log("RTS smoke player built at " + output);
        }

        private static string Value(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
