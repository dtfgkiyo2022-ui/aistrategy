using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rts.Tests.EditMode
{
    public sealed class ProjectSkeletonTests
    {
        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string name = "";
            public string[] references = Array.Empty<string>();
            public bool noEngineReferences = false;
            public bool autoReferenced = false;
            public bool overrideReferences = false;
            public string[] precompiledReferences = Array.Empty<string>();
            public bool allowUnsafeCode = false;
        }

        // Design chapter 2. Editor can use the seven runtime assemblies; tests also use Editor.
        private static readonly Dictionary<string, string[]> AllowedReferences = new Dictionary<string, string[]>
        {
            ["Contracts"] = Array.Empty<string>(),
            ["Decision"] = new[] { "Rts.Contracts" },
            ["Simulation"] = new[] { "Rts.Contracts", "Rts.Decision" },
            ["Replay"] = new[] { "Rts.Contracts" },
            ["Application"] = new[] { "Rts.Contracts", "Rts.Simulation", "Rts.Replay" },
            ["Presentation"] = new[] { "Rts.Contracts" },
            ["UnityHost"] = new[] { "Rts.Application", "Rts.Presentation", "Rts.Contracts" },
            ["Editor"] = new[] { "Rts.Contracts", "Rts.Decision", "Rts.Simulation", "Rts.Replay",
                "Rts.Application", "Rts.Presentation", "Rts.UnityHost" },
            ["Tests.EditMode"] = new[] { "Rts.Contracts", "Rts.Decision", "Rts.Simulation", "Rts.Replay",
                "Rts.Application", "Rts.Presentation", "Rts.UnityHost", "Rts.Editor" }
        };

        private static AssemblyDefinition ReadDefinition(string name)
        {
            string folder = name.Replace('.', Path.DirectorySeparatorChar);
            return JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(
                Path.Combine(UnityEngine.Application.dataPath, "RTS", folder, "Rts." + name + ".asmdef")));
        }

        [TestCaseSource(nameof(AssemblyNames))]
        public void AllAssembliesRespectTheReferenceAllowlist(string name)
        {
            var definition = ReadDefinition(name);
            Assert.That(definition.name, Is.EqualTo("Rts." + name));
            foreach (string rawReference in definition.references)
            {
                string reference = rawReference;
                if (reference.StartsWith("GUID:", StringComparison.Ordinal))
                {
                    string path = AssetDatabase.GUIDToAssetPath(reference.Substring(5));
                    Assert.That(File.Exists(path), Is.True, "Unresolved asmdef reference: " + reference);
                    reference = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path)).name;
                }
                if (reference.StartsWith("Rts.", StringComparison.Ordinal))
                    Assert.That(AllowedReferences[name], Does.Contain(reference), "Rts." + name + " -> " + reference);
                else
                {
                    // Package references are explicit too: an unknown assembly must not bypass the table.
                    string[] packages = name == "Editor"
                        ? new[] { "Unity.RenderPipelines.Core.Runtime", "Unity.RenderPipelines.Universal.Runtime" }
                        : name == "Tests.EditMode" ? new[] { "UnityEngine.TestRunner", "UnityEditor.TestRunner" }
                        : Array.Empty<string>();
                    Assert.That(packages, Does.Contain(reference), "Rts." + name + " -> " + reference);
                }
            }
        }

        private static IEnumerable<string> AssemblyNames => AllowedReferences.Keys;

        [Test]
        public void DecisionCannotReferenceSimulationAndPresentationCanOnlyReferenceContracts()
        {
            Assert.That(AllowedReferences["Decision"], Does.Not.Contain("Rts.Simulation"));
            Assert.That(ReadDefinition("Decision").references, Does.Not.Contain("Rts.Simulation"));
            Assert.That(AllowedReferences["Presentation"], Is.EqualTo(new[] { "Rts.Contracts" }));
            Assert.That(ReadDefinition("Presentation").references, Is.SubsetOf(new[] { "Rts.Contracts" }));
        }

        [TestCase("Contracts")]
        [TestCase("Decision")]
        [TestCase("Simulation")]
        [TestCase("Replay")]
        [TestCase("Application")]
        public void PureAssembliesHaveNoTransitiveUnityDependency(string name)
        {
            var root = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Rts." + name);
            CheckDependencies(root, root.GetName().Name, new HashSet<string>(StringComparer.Ordinal));
        }

        private static void CheckDependencies(Assembly assembly, string chain, HashSet<string> visited)
        {
            if (!visited.Add(assembly.FullName)) return;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                string nextChain = chain + " -> " + reference.Name;
                Assert.That(reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)
                    || reference.Name.StartsWith("UnityEditor", StringComparison.Ordinal), Is.False, nextChain);
                // Validate each project edge, not the root's direct allowlist (Simulation -> Decision is legal).
                if (assembly.GetName().Name.StartsWith("Rts.", StringComparison.Ordinal)
                    && reference.Name.StartsWith("Rts.", StringComparison.Ordinal))
                    Assert.That(AllowedReferences[assembly.GetName().Name.Substring(4)],
                        Does.Contain(reference.Name), nextChain);
                Assembly dependency;
                try { dependency = Assembly.Load(reference); }
                catch (Exception error)
                {
                    Assert.Fail("Cannot inspect dependency: " + nextChain + "\n" + error);
                    return;
                }
                CheckDependencies(dependency, nextChain, visited);
            }
        }

        [TestCase("Contracts", new string[0])]
        [TestCase("Decision", new[] { "Rts.Contracts" })]
        [TestCase("Simulation", new[] { "Rts.Contracts", "Rts.Decision" })]
        [TestCase("Replay", new[] { "Rts.Contracts" })]
        [TestCase("Application", new[] { "Rts.Contracts", "Rts.Simulation", "Rts.Replay" })]
        public void PureAssembliesRespectTheDependencyBoundary(string name, string[] allowed)
        {
            var definition = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(
                Path.Combine(UnityEngine.Application.dataPath, "RTS", name, "Rts." + name + ".asmdef")));
            Assert.That(definition.references, Is.EquivalentTo(allowed));
            Assert.That(definition.noEngineReferences, Is.True);
            Assert.That(definition.autoReferenced, Is.False);
            Assert.That(definition.overrideReferences, Is.True);
            Assert.That(definition.precompiledReferences, Is.Empty);
            Assert.That(definition.allowUnsafeCode, Is.False);

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .Single(a => a.GetName().Name == "Rts." + name);
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal), Is.False);
                Assert.That(reference.Name.StartsWith("UnityEditor", StringComparison.Ordinal), Is.False);
                if (reference.Name.StartsWith("Rts.", StringComparison.Ordinal))
                    Assert.That(allowed, Does.Contain(reference.Name));
            }
        }

        [Test]
        public void EachMarkerIsCompiledIntoItsOwnAssembly()
        {
            Type[] markers =
            {
                typeof(Contracts.ContractsAssemblyMarker),
                typeof(Decision.DecisionAssemblyMarker),
                typeof(Simulation.SimulationAssemblyMarker),
                typeof(Replay.ReplayAssemblyMarker),
                typeof(Application.ApplicationAssemblyMarker),
                typeof(Presentation.PresentationAssemblyMarker),
                typeof(UnityHost.UnityHostAssemblyMarker),
                typeof(Editor.EditorAssemblyMarker),
                typeof(TestsEditModeAssemblyMarker)
            };
            foreach (var marker in markers)
                Assert.That(marker.Assembly.GetName().Name, Is.EqualTo(marker.Namespace), marker.FullName);
        }

        [Test]
        public void GraphicsAndEveryQualityLevelUseTheSavedPipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(
                "Assets/Settings/RtsUniversalRenderPipeline.asset");
            Assert.That(pipeline, Is.Not.Null);
            Assert.That(pipeline.GetType().FullName,
                Is.EqualTo("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset"));
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.EqualTo(pipeline));
            var serializedPipeline = new SerializedObject(pipeline);
            var renderers = serializedPipeline.FindProperty("m_RendererDataList");
            Assert.That(renderers.arraySize, Is.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(0).objectReferenceValue),
                Is.EqualTo("Assets/Settings/RtsUniversalRenderer.asset"));
            Assert.That(serializedPipeline.FindProperty("m_DefaultRendererIndex").intValue, Is.Zero);
            for (int i = 0; i < QualitySettings.names.Length; i++)
                Assert.That(QualitySettings.GetRenderPipelineAssetAt(i), Is.EqualTo(pipeline), QualitySettings.names[i]);
        }

        [Test]
        public void VersionControlSettingsAreTextAndVisibleMetaFiles()
        {
            Assert.That(EditorSettings.serializationMode, Is.EqualTo(SerializationMode.ForceText));
            Assert.That(UnityEditor.VersionControlSettings.mode, Is.EqualTo("Visible Meta Files"));
        }
    }
}
