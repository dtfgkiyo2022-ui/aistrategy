using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rts.Editor
{
    /// <summary>Build-time plan for placing the repository samples outside Assets.</summary>
    public sealed class TacticSamplesBuildCopyPlan
    {
        public TacticSamplesBuildCopyPlan(string sourceDirectory, string destinationDirectory)
        {
            SourceDirectory = sourceDirectory;
            DestinationDirectory = destinationDirectory;
        }

        public string SourceDirectory { get; private set; }
        public string DestinationDirectory { get; private set; }
    }

    public static class TacticSamplesBuildPaths
    {
        public static TacticSamplesBuildCopyPlan Resolve(string repositoryRoot, string buildOutputPath)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot)) throw new ArgumentException("repositoryRoot is required", "repositoryRoot");
            if (string.IsNullOrWhiteSpace(buildOutputPath)) throw new ArgumentException("buildOutputPath is required", "buildOutputPath");

            string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(buildOutputPath));
            string fileName = Path.GetFileNameWithoutExtension(buildOutputPath);
            string dataDirectory = Path.Combine(outputDirectory, fileName + "_Data");
            return new TacticSamplesBuildCopyPlan(
                Path.Combine(Path.GetFullPath(repositoryRoot), "TacticSamples"),
                Path.Combine(dataDirectory, "StreamingAssets", "TacticSamples"));
        }
    }

    /// <summary>Copies the samples into the product's StreamingAssets after Unity has built it.</summary>
    public sealed class TacticSamplesBuildPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPostprocessBuild(BuildReport report)
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", ".."));
            var plan = TacticSamplesBuildPaths.Resolve(repositoryRoot, report.summary.outputPath);
            if (!Directory.Exists(plan.SourceDirectory))
            {
                Debug.LogWarning("TacticSamples is missing; samples were not copied: " + plan.SourceDirectory);
                return;
            }

            CopyDirectory(plan.SourceDirectory, plan.DestinationDirectory);
            Debug.Log("Copied tactic samples to " + plan.DestinationDirectory);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
